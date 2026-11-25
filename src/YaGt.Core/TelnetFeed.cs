using System.Net.Sockets;
using System.Text;

namespace YaGt.Core;

/// <summary>Client telnet per DX cluster, RBN e cluster DM4X (da LIVETROPOVHF).</summary>
public sealed class TelnetFeed
{
    public string Id = "";
    public string Src = "";       // RBN, DM4X, CL2
    public string Host = "";
    public int Port;
    public string Login = "";
    public string Password = "";  // inviata quando il server la chiede ("password:")
    public string Cmds = "";
    public bool Reconnect = true;
    public bool AutoStart;

    public int Lines;
    public string LastError = "";
    bool NodeDown;
    public bool Connected { get; private set; }
    public bool Wanted { get; private set; }

    /// <summary>Riga per il registro (testo, classe).</summary>
    public event Action<string> Log;
    public event Action StateChanged;
    /// <summary>Ogni riga ricevuta (già ripulita dai comandi telnet).</summary>
    public event Action<string> LineReceived;

    readonly object _lock = new();
    TcpClient _client;
    NetworkStream _stream;
    CancellationTokenSource _cts;
    string _activeLogin = "";

    public void Connect(string fallbackLogin)
    {
        lock (_lock)
        {
            _activeLogin = string.IsNullOrWhiteSpace(Login) ? fallbackLogin : Login.Trim();
            if (Wanted) return;
            Wanted = true;
            _cts = new CancellationTokenSource();
        }
        var ct = _cts.Token;
        Task.Run(() => Run(ct));
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            Wanted = false;
            try { _cts?.Cancel(); } catch { }
            try { _client?.Close(); } catch { }
            _client = null;
            _stream = null;
            Connected = false;
            LastError = "";
        }
        StateChanged?.Invoke();
    }

    public void Send(string cmd)
    {
        NetworkStream s;
        lock (_lock) s = _stream;
        if (s == null) { Log?.Invoke("— non connesso: comando non inviato —"); return; }
        try
        {
            var b = Encoding.ASCII.GetBytes(cmd + "\r\n");
            s.Write(b, 0, b.Length);
            Log?.Invoke("> " + cmd);
        }
        catch (Exception ex) { Log?.Invoke("— invio non riuscito: " + ex.Message + " —"); }
    }

    async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Log?.Invoke($"— connessione a {Host}:{Port} —");
            var client = new TcpClient();
            try
            {
                using var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tcs.CancelAfter(TimeSpan.FromSeconds(12));
                await Compat.Connect(client, Host, Port, tcs.Token);
            }
            catch (Exception ex)
            {
                try { client.Close(); } catch { }
                if (ct.IsCancellationRequested) return;
                LastError = "errore: " + ex.Message;
                Log?.Invoke("— " + ex.Message + " —");
                StateChanged?.Invoke();
                if (!Reconnect) { Wanted = false; StateChanged?.Invoke(); return; }
                try { await Task.Delay(15000, ct); } catch { return; }
                continue;
            }

            var stream = client.GetStream();
            lock (_lock) { _client = client; _stream = stream; Connected = true; LastError = ""; }
            StateChanged?.Invoke();

            bool loginSent = false, passSent = false;
            var loginLock = new object();
            void SendLogin()
            {
                lock (loginLock) { if (loginSent) return; loginSent = true; }
                Send(_activeLogin);
                _ = Task.Run(async () =>
                {
                    foreach (var c in (Cmds ?? "").Split(';'))
                    {
                        var cmd = c.Trim();
                        if (cmd.Length == 0) continue;
                        try { await Task.Delay(600, ct); } catch { return; }
                        Send(cmd);
                    }
                });
            }
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(4000, ct); } catch { return; }
                if (Connected) SendLogin();
            });

            var buf = new byte[8192];
            var pending = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length, ct);
                    if (n <= 0) break;
                    var chunk = Compat.Latin1.GetString(StripTelnet(buf, n));
                    pending.Append(chunk);
                    while (true)
                    {
                        var p = pending.ToString();
                        int i = p.IndexOf('\n');
                        if (i < 0) break;
                        var line = p.Substring(0, i).TrimEnd('\r');
                        pending.Remove(0, i + 1);
                        HandleLine(line);
                        // il server risponde ma il programma del cluster dietro di lui è fermo (es. 9A0DXC: "Error on connect to 127.0.0.1 port …")
                        if (line.IndexOf("Error on connect to 127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Error on connect to localhost", StringComparison.OrdinalIgnoreCase) >= 0)
                            NodeDown = true;
                        if (line.Contains("password", StringComparison.OrdinalIgnoreCase) && loginSent && !passSent && !string.IsNullOrEmpty(Password))
                        { passSent = true; Send(Password); }
                    }
                    var low = pending.ToString().ToLowerInvariant();
                    if (low.Contains("password") && loginSent && !passSent && !string.IsNullOrEmpty(Password))
                    {
                        HandleLine(pending.ToString());
                        pending.Clear();
                        passSent = true;
                        Send(Password);
                        continue;
                    }
                    if (low.Contains("login") || low.Contains("call:") || low.Contains("callsign") || low.Contains("your call"))
                    {
                        HandleLine(pending.ToString());
                        pending.Clear();
                        SendLogin();
                    }
                }
            }
            catch { }

            try { client.Close(); } catch { }
            lock (_lock) { _client = null; _stream = null; Connected = false; }
            if (NodeDown) { LastError = "nodo del cluster fermo: il server risponde ma non raggiunge il programma del cluster" + (Reconnect ? $" (ritento alle {DateTime.Now.AddSeconds(10):HH:mm:ss})" : ""); NodeDown = false; }
            StateChanged?.Invoke();
            if (ct.IsCancellationRequested || !Wanted) return;
            Log?.Invoke("— connessione chiusa —");
            if (!Reconnect) { Wanted = false; StateChanged?.Invoke(); return; }
            try { await Task.Delay(10000, ct); } catch { return; }
        }
    }

    static byte[] StripTelnet(byte[] b, int n)
    {
        var o = new List<byte>(n);
        for (int i = 0; i < n; i++)
        {
            if (b[i] == 0xFF && i + 1 < n)
            {
                if (b[i + 1] >= 0xFB && b[i + 1] <= 0xFE) i += 2; else i++;
                continue;
            }
            if (b[i] == 0 || b[i] == 7) continue;
            o.Add(b[i]);
        }
        return o.ToArray();
    }

    void HandleLine(string line)
    {
        Interlocked.Increment(ref Lines);
        Log?.Invoke(line);
        LineReceived?.Invoke(line);
    }
}
