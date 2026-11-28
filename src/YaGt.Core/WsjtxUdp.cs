using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YaGt.Core;

/// <summary>Una stazione decodificata dal programma digitale dell'utente (WSJT-X, JTDX, MSHV).</summary>
public sealed class Decode
{
    public DateTime Time;
    public string Band = "";
    public string Mode = "";
    public int Snr;
    public double Dt;
    public uint Df;
    public string Message = "";
    public string Call = "";
    public string Grid = "";
    public LatLon Pos;
    public bool HasPos, PosApprox;
    public double Km, Az;
}

/// <summary>
/// Ascolta i pacchetti UDP del protocollo WSJT-X (anche JTDX e MSHV), ne ricava le decodifiche e li ripete identici
/// verso gli indirizzi di inoltro (es. QLog, GridTracker, JTAlert): "host:porta" separati da virgola o punto e virgola.
/// Il rimbalzo è nei due sensi: ogni destinazione ha la sua porta e quello che risponde (Reply, Highlight, Halt,
/// FreeText, Location...) torna al programma digitale dalla porta d'ascolto, come se parlasse direttamente con lui.
/// I QSO registrati (QSO Logged, Logged ADIF) passano così come sono, quindi QLog li scrive nel suo log da solo.
/// </summary>
public sealed class WsjtxUdp
{
    public string ListenAddress = "127.0.0.1";
    public int ListenPort = 2237;
    /// <summary>Destinazione principale della ritrasmissione (es. QLog): indirizzo e porta scelti a parte.</summary>
    public string ForwardHost = "127.0.0.1";
    public int ForwardPort = 2238;
    /// <summary>Altre destinazioni facoltative "host:porta" separate da virgola.</summary>
    public string ForwardTo = "";
    public bool Forward;

    /// <summary>Tutte le destinazioni: quella principale più le altre.</summary>
    public string AllTargets => (ForwardHost.Trim() != "" && ForwardPort > 0 ? ForwardHost.Trim() + ":" + ForwardPort.ToString(CultureInfo.InvariantCulture) + "," : "") + ForwardTo;

    public bool Running { get; private set; }
    public string LastError = "";
    public string ProgramId = "";
    public double DialHz;
    public string DialMode = "";
    public long Packets, Forwarded, Returned, Decodes, Logged;

    public event Action StateChanged;
    public event Action<Decode> Decoded;
    /// <summary>Un QSO appena registrato dal programma digitale (messaggio QSO Logged).</summary>
    public event Action<QsoRecord> QsoLogged;

    UdpClient _rx;
    CancellationTokenSource _cts;
    readonly List<Relay> _relays = new List<Relay>();
    volatile IPEndPoint _source;   // ultimo indirizzo del programma digitale (dove rimandare le risposte)

    sealed class Relay { public IPEndPoint Target; public UdpClient Sock; }

    public string CurrentBand => DialHz > 0 ? Bands.FromMHz(DialHz / 1e6) : "";

    /// <summary>Legge l'elenco "host:porta, host:porta" (nomi risolti subito).</summary>
    public static List<IPEndPoint> ParseTargets(string s, out string error)
    {
        error = "";
        var list = new List<IPEndPoint>();
        foreach (var raw in (s ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim();
            int i = t.LastIndexOf(':');
            if (i <= 0 || !int.TryParse(t.Substring(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
            { error = "indirizzo non valido: " + t; continue; }
            var host = t.Substring(0, i);
            try
            {
                if (!IPAddress.TryParse(host, out var ip))
                    ip = Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
                list.Add(new IPEndPoint(ip, port));
            }
            catch { error = "indirizzo non trovato: " + host; }
        }
        return list;
    }

    public void Start()
    {
        Stop();
        try
        {
            var targets = ParseTargets(AllTargets, out var err);
            LastError = Forward ? err : "";
            var listenIp = IPAddress.Parse(ListenAddress.Trim());
            bool multicast = listenIp.AddressFamily == AddressFamily.InterNetwork && (listenIp.GetAddressBytes()[0] & 0xF0) == 0xE0;
            _rx = new UdpClient(AddressFamily.InterNetwork);
            // la porta condivisa solo in multicast: in unicast due programmi sulla stessa porta si "rubano" i pacchetti
            if (multicast) _rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try { _rx.Client.Bind(new IPEndPoint(multicast ? IPAddress.Any : listenIp, ListenPort)); }
            catch (SocketException)
            {
                throw new Exception(L.F("porta UDP {0} già occupata (forse da QLog o GridTracker): metti quel programma su un'altra porta, es. 2238, e scrivila tra gli inoltri", ListenPort));
            }
            if (multicast) _rx.JoinMulticastGroup(listenIp);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _relays.Clear();
            if (Forward)
                foreach (var t in targets)
                {
                    if (t.Port == ListenPort && (IPAddress.IsLoopback(t.Address) || t.Address.Equals(listenIp))) { LastError = L.T("l'inoltro non può puntare alla porta d'ascolto ") + ListenPort; continue; }
                    var r = new Relay { Target = t, Sock = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) };
                    _relays.Add(r);
                    Task.Run(() => ReturnLoop(r, ct));
                }
            Running = true;
            StateChanged?.Invoke();
            Task.Run(() => Loop(ct));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Running = false;
            StateChanged?.Invoke();
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _rx?.Close(); } catch { }
        foreach (var r in _relays) try { r.Sock.Close(); } catch { }
        _relays.Clear();
        _rx = null;
        if (Running) { Running = false; StateChanged?.Invoke(); }
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await Compat.Receive(_rx, ct); }
            catch { break; }
            Interlocked.Increment(ref Packets);
            var data = res.Buffer;
            _source = res.RemoteEndPoint;
            foreach (var r in _relays.ToArray())
            {
                try { await r.Sock.SendAsync(data, data.Length, r.Target); Interlocked.Increment(ref Forwarded); } catch { }
            }
            try { Parse(data); } catch { }
        }
        Running = false;
        StateChanged?.Invoke();
    }

    /// <summary>Le risposte di una destinazione (QLog, JTAlert, GridTracker) tornano al programma digitale.</summary>
    async Task ReturnLoop(Relay r, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await Compat.Receive(r.Sock, ct); }
            catch (SocketException) { if (ct.IsCancellationRequested) break; continue; } // ICMP "porta chiusa": la destinazione non è ancora aperta
            catch { break; }
            var src = _source; var rx = _rx;
            if (src == null || rx == null) continue;
            try { await rx.SendAsync(res.Buffer, res.Buffer.Length, src); Interlocked.Increment(ref Returned); } catch { }
        }
    }

    sealed class Reader
    {
        readonly byte[] _b; int _p;
        public Reader(byte[] b) { _b = b; }
        public bool Eof => _p >= _b.Length;
        public uint U32() { uint v = (uint)(_b[_p] << 24 | _b[_p + 1] << 16 | _b[_p + 2] << 8 | _b[_p + 3]); _p += 4; return v; }
        public int I32() => (int)U32();
        public ulong U64() { ulong hi = U32(); ulong lo = U32(); return hi << 32 | lo; }
        public bool Bool() => _b[_p++] != 0;
        public double F64()
        {
            var t = new byte[8];
            Array.Copy(_b, _p, t, 0, 8); _p += 8;
            if (BitConverter.IsLittleEndian) Array.Reverse(t);
            return BitConverter.ToDouble(t, 0);
        }
        public byte U8() => _b[_p++];
        /// <summary>QDateTime serializzato: giorno giuliano, millisecondi, tipo di ora (2 = con scarto).</summary>
        public DateTime QDateTime()
        {
            long jd = (long)U64();
            uint ms = U32();
            byte spec = U8();
            if (spec == 2) I32();
            if (jd <= 0) return DateTime.MinValue;
            return new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc).AddDays(jd - 2400001).AddMilliseconds(ms);
        }
        public string Utf8()
        {
            uint n = U32();
            if (n == 0xFFFFFFFF) return "";
            var s = Encoding.UTF8.GetString(_b, _p, (int)n); _p += (int)n; return s;
        }
    }

    void Parse(byte[] data)
    {
        var r = new Reader(data);
        if (data.Length < 12 || r.U32() != 0xADBCCBDA) return;
        r.U32();                 // schema
        uint type = r.U32();
        string id = r.Utf8();
        switch (type)
        {
            case 1: // Status
            {
                ulong dial = r.U64();
                string mode = r.Utf8();
                DialHz = dial; DialMode = mode; ProgramId = id;
                StateChanged?.Invoke();
                break;
            }
            case 2: // Decode
            {
                r.Bool();
                r.U32();
                int snr = r.I32();
                double dt = r.F64();
                uint df = r.U32();
                r.Utf8();
                string msg = r.Utf8();
                var d = new Decode { Time = DateTime.UtcNow, Band = CurrentBand, Mode = DialMode, Snr = snr, Dt = dt, Df = df, Message = msg };
                ExtractStation(msg, d);
                Interlocked.Increment(ref Decodes);
                if (d.Call != "") Decoded?.Invoke(d);
                break;
            }
            case 5: // QSO Logged
            {
                r.QDateTime();                 // fine QSO
                string call = r.Utf8().Trim().ToUpperInvariant();
                string grid = r.Utf8().Trim().ToUpperInvariant();
                ulong freq = r.U64();
                string mode = r.Utf8();
                r.Utf8(); r.Utf8(); r.Utf8(); r.Utf8(); r.Utf8(); // rapporti, potenza, commento, nome
                var on = r.QDateTime();
                string prop = "";
                try { r.Utf8(); r.Utf8(); r.Utf8(); r.Utf8(); r.Utf8(); if (!r.Eof) prop = r.Utf8(); } catch { }
                if (call == "") break;
                if (on == DateTime.MinValue) on = DateTime.UtcNow;
                var q = new QsoRecord
                {
                    Call = call, Grid = grid, Mode = mode, PropMode = prop,
                    Band = freq > 0 ? Bands.FromMHz(freq / 1e6) : CurrentBand,
                    Date = on.ToString("yyyyMMdd", CultureInfo.InvariantCulture), Time = on.ToString("HHmmss", CultureInfo.InvariantCulture),
                };
                q.AddGrid(grid);
                Interlocked.Increment(ref Logged);
                QsoLogged?.Invoke(q);
                StateChanged?.Invoke();
                break;
            }
        }
    }

    static readonly HashSet<string> CqWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DX", "EU", "TEST", "NA", "SA", "AS", "AF", "OC", "QRP", "POTA", "SOTA", "FD", "RU", "WW" };

    /// <summary>Ricava la stazione trasmittente e il suo locatore dal testo del messaggio (come in LIVETROPOVHF).</summary>
    public static void ExtractStation(string msg, Decode d)
    {
        var t = (msg ?? "").Trim().ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return;
        int i;
        if (t[0] == "CQ" || t[0] == "QRZ" || t[0].StartsWith("CQ_"))
        {
            i = 1;
            if (i < t.Length && (CqWords.Contains(t[i]) || (t[i].All(char.IsDigit) && t[i].Length <= 3))) i++;
        }
        else i = 1;
        if (i >= t.Length) return;
        var call = t[i].Trim('<', '>');
        if (call.Length < 3 || !call.Any(char.IsDigit) || !call.Any(char.IsLetter) || call == "RR73" || call == "73") return;
        d.Call = call;
        if (i + 1 < t.Length)
        {
            var g = t[i + 1];
            if (g.Length == 4 && g != "RR73" && Locator.IsValid(g)) d.Grid = g;
        }
    }
}

/// <summary>Decodifiche dell'utente per stazione e banda (l'ultima).</summary>
public sealed class DecodeStore
{
    readonly object _lock = new object();
    readonly Dictionary<string, Decode> _map = new Dictionary<string, Decode>();

    public void Add(Decode d)
    {
        lock (_lock)
        {
            var key = d.Band + "|" + d.Call;
            if (_map.TryGetValue(key, out var o) && d.Grid == "" && o.Grid != "") d.Grid = o.Grid;
            _map[key] = d;
        }
    }

    public List<Decode> Snapshot(DateTime nowUtc, int minutes)
    {
        var cut = nowUtc.AddMinutes(-minutes);
        lock (_lock) return _map.Values.Where(d => d.Time > cut).ToList();
    }

    public void Prune(DateTime nowUtc, int minutes)
    {
        var cut = nowUtc.AddMinutes(-minutes);
        lock (_lock) foreach (var k in _map.Where(kv => kv.Value.Time < cut).Select(kv => kv.Key).ToList()) _map.Remove(k);
    }
}
