using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using MQTTnet;
using MQTTnet.Client;

namespace YaGt.Core;

/// <summary>
/// Flusso MQTT di mqtt.pskreporter.info (come in LIVETROPOVHF), filtrato sul nominativo dell'utente:
/// pskr/filter/v2/{banda}/{modo}/{tx}/{rx}/{loc tx}/{loc rx}/{dxcc tx}/{dxcc rx}.
/// Due sottoscrizioni: l'utente trasmette (chi mi sente) e l'utente riceve (chi sento io).
/// </summary>
public sealed class PskMqtt
{
    public string Host = "mqtt.pskreporter.info";
    public int Port = 1883;
    public string MyCall = "";

    public bool Connected { get; private set; }
    public bool Wanted { get; private set; }
    public string LastError = "";
    public long Reports;

    public event Action StateChanged;
    public event Action<SpotItem> Spot;

    IMqttClient _client;
    CancellationTokenSource _cts;
    readonly Dictionary<string, DateTime> _seen = new Dictionary<string, DateTime>();

    static string T(string call) => (call ?? "").ToUpperInvariant().Replace('/', '.');
    static string FromTopic(string call) => (call ?? "").Replace('.', '/').ToUpperInvariant();

    public void Connect()
    {
        if (Wanted || string.IsNullOrEmpty(MyCall)) return;
        Wanted = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() => Run(ct));
    }

    public void Disconnect()
    {
        Wanted = false;
        try { _cts?.Cancel(); } catch { }
        var c = _client;
        _client = null;
        if (c != null)
            Task.Run(async () =>
            {
                try { await c.DisconnectAsync(); } catch { }
                try { c.Dispose(); } catch { }
            });
        Connected = false;
        StateChanged?.Invoke();
    }

    async Task Run(CancellationToken ct)
    {
        var factory = new MqttFactory();
        while (!ct.IsCancellationRequested)
        {
            var client = factory.CreateMqttClient();
            _client = client;
            var closed = new TaskCompletionSource<bool>();
            client.ApplicationMessageReceivedAsync += e =>
            {
                try { Handle(e.ApplicationMessage.Topic, e.ApplicationMessage.PayloadSegment); } catch { }
                return Task.CompletedTask;
            };
            client.DisconnectedAsync += e => { closed.TrySetResult(true); return Task.CompletedTask; };
            try
            {
                var ob = new MqttClientOptionsBuilder()
                    .WithTcpServer(Host, Port)
                    .WithClientId("YA-GT-" + Guid.NewGuid().ToString("N").Substring(0, 8))
                    .WithCleanSession();
                await client.ConnectAsync(ob.Build(), ct);
                Connected = true; LastError = "";
                StateChanged?.Invoke();
                foreach (var t in new[] { $"pskr/filter/v2/+/+/{T(MyCall)}/#", $"pskr/filter/v2/+/+/+/{T(MyCall)}/#" })
                    await client.SubscribeAsync(factory.CreateSubscribeOptionsBuilder().WithTopicFilter(f => f.WithTopic(t)).Build(), ct);
                await Task.WhenAny(closed.Task, Task.Delay(Timeout.Infinite, ct));
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) LastError = ex.Message;
            }
            Connected = false;
            StateChanged?.Invoke();
            try { client.Dispose(); } catch { }
            if (ct.IsCancellationRequested || !Wanted) return;
            try { await Task.Delay(15000, ct); } catch { return; }
        }
    }

    void Handle(string topic, ArraySegment<byte> payload)
    {
        var p = topic.Split('/');
        if (p.Length < 9) return;
        var key = topic + "|" + (payload.Count > 0 ? Convert.ToBase64String(payload.Array, payload.Offset, Math.Min(payload.Count, 48)) : "");
        lock (_seen)
        {
            if (_seen.ContainsKey(key)) return;
            _seen[key] = DateTime.UtcNow;
            if (_seen.Count > 5000)
                foreach (var k in _seen.Where(kv => DateTime.UtcNow - kv.Value > TimeSpan.FromMinutes(2)).Select(kv => kv.Key).ToList()) _seen.Remove(k);
        }
        var s = new SpotItem
        {
            Time = DateTime.UtcNow,
            Source = "PSK",
            Mode = p[4].ToUpperInvariant(),
            Tx = FromTopic(p[5]),
            Rx = FromTopic(p[6]),
            TxGrid = Grid(p[7]),
            RxGrid = Grid(p[8]),
            Band = Bands.Normalize(p[3], ""),
        };
        if (payload.Count > 0)
        {
            try
            {
                using (var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(payload.Array, payload.Offset, payload.Count)))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("rp", out var rp) && rp.ValueKind == JsonValueKind.Number) s.Snr = rp.GetInt32();
                    if (root.TryGetProperty("f", out var f) && f.ValueKind == JsonValueKind.Number) s.FreqKHz = f.GetDouble() / 1000.0;
                    if (root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Number) s.Time = DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime;
                    if (root.TryGetProperty("sc", out var sc) && sc.ValueKind == JsonValueKind.String && (sc.GetString() ?? "").Length > 2) s.Tx = sc.GetString().Trim().ToUpperInvariant();
                    if (root.TryGetProperty("rc", out var rc) && rc.ValueKind == JsonValueKind.String && (rc.GetString() ?? "").Length > 2) s.Rx = rc.GetString().Trim().ToUpperInvariant();
                    if (root.TryGetProperty("sl", out var sl) && sl.ValueKind == JsonValueKind.String && Grid(sl.GetString()) != "") s.TxGrid = Grid(sl.GetString());
                    if (root.TryGetProperty("rl", out var rl) && rl.ValueKind == JsonValueKind.String && Grid(rl.GetString()) != "") s.RxGrid = Grid(rl.GetString());
                }
            }
            catch { }
        }
        if (s.FreqKHz > 0) { var b = Bands.FromKHz(s.FreqKHz); if (b != "") s.Band = b; }
        var me = MyCall.ToUpperInvariant();
        if (s.Tx == me) s.Dir = SpotDir.HeardMe;
        else if (s.Rx == me || ClusterParser.SkimmerBase(s.Rx) == me) s.Dir = SpotDir.IHeard;
        else return;
        Interlocked.Increment(ref Reports);
        Spot?.Invoke(s);
    }

    public static string Grid(string g)
    {
        if (string.IsNullOrEmpty(g)) return "";
        g = g.Trim().ToUpperInvariant();
        if (g.Length >= 6 && Locator.IsValid(g.Substring(0, 6))) return g.Substring(0, 6);
        if (g.Length >= 4 && Locator.IsValid(g.Substring(0, 4))) return g.Substring(0, 4);
        return "";
    }
}

/// <summary>
/// Storico dell'ultima ora dall'interfaccia HTTP di PSKReporter (retrieve.pskreporter.info), all'avvio e su richiesta:
/// non più di una richiesta ogni 5 minuti, come chiede il servizio.
/// </summary>
public static class PskHttp
{
    static DateTime _last = DateTime.MinValue;
    public static string LastStatus = "";

    public static async Task<List<SpotItem>> FetchAsync(HttpClient http, string myCall, int minutes, CancellationToken ct)
    {
        var res = new List<SpotItem>();
        if (string.IsNullOrEmpty(myCall)) return res;
        if (DateTime.UtcNow - _last < TimeSpan.FromMinutes(5)) return res;
        _last = DateTime.UtcNow;
        var me = myCall.ToUpperInvariant();
        foreach (var dir in new[] { SpotDir.HeardMe, SpotDir.IHeard })
        {
            var who = dir == SpotDir.HeardMe ? "senderCallsign" : "receiverCallsign";
            var url = "https://retrieve.pskreporter.info/query?" + who + "=" + Uri.EscapeDataString(me) +
                      "&flowStartSeconds=-" + (minutes * 60).ToString(CultureInfo.InvariantCulture) +
                      "&rronly=1&noactive=1&appcontact=" + Uri.EscapeDataString("ic8tem@gmail.com");
            try
            {
                using (var r = await http.GetAsync(url, ct))
                {
                    if (!r.IsSuccessStatusCode) { LastStatus = "PSKReporter HTTP " + (int)r.StatusCode; continue; }
                    var xml = await Compat.ReadString(r.Content, ct);
                    var doc = XDocument.Parse(xml);
                    int n = 0;
                    foreach (var e in doc.Descendants().Where(x => x.Name.LocalName == "receptionReport"))
                    {
                        string A(string k) => (string)e.Attribute(k) ?? "";
                        var s = new SpotItem
                        {
                            Source = "PSK",
                            Dir = dir,
                            Tx = A("senderCallsign").ToUpperInvariant(),
                            Rx = A("receiverCallsign").ToUpperInvariant(),
                            TxGrid = PskMqtt.Grid(A("senderLocator")),
                            RxGrid = PskMqtt.Grid(A("receiverLocator")),
                            Mode = A("mode").ToUpperInvariant(),
                        };
                        if (double.TryParse(A("frequency"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) && hz > 0) { s.FreqKHz = hz / 1000; s.Band = Bands.FromKHz(s.FreqKHz); }
                        if (int.TryParse(A("sNR"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var snr)) s.Snr = snr;
                        if (long.TryParse(A("flowStartSeconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)) s.Time = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                        else s.Time = DateTime.UtcNow;
                        res.Add(s); n++;
                    }
                    LastStatus = "PSKReporter HTTP: " + n + " rapporti";
                }
            }
            catch (Exception ex) { LastStatus = "PSKReporter HTTP: " + ex.Message; }
        }
        return res;
    }
}
