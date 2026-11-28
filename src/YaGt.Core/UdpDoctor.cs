using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace YaGt.Core;

/// <summary>
/// "Verifica UDP": legge le impostazioni UDP dei programmi digitali e del log e, a richiesta, le corregge perché lavorino con YA-GT.
/// Dove stanno le impostazioni (ricavato dai sorgenti):
///  - WSJT-X (e WSJT-X Improved): WSJT-X.ini (QSettings IniFormat, cartella ConfigLocation: %LOCALAPPDATA%\WSJT-X, ~/.config),
///    gruppo [Configuration], voci UDPServer, UDPServerPort, AcceptUDPRequests; le altre configurazioni in [MultiSettings] "nome\Configuration\...".
///  - JTDX: JTDX.ini, stesse voci e stesso gruppo (%LOCALAPPDATA%\JTDX, ~/.config).
///  - MSHV: settings/ms_stinfonet nella cartella del programma: udp_broad_server, udp_broad_port, udp_broad_log_all = qso#adif#decodifiche (1/0).
///  - MadModem: settings.mad (QSettings IniFormat) accanto al programma o nella cartella utente: [Logbook] udpEnabled, udpServer, udpPort.
///  - Aurora: %APPDATA%\Aurora\settings-motore.json {"version","values":{UDPServer, UDPServerPort, AcceptUDPRequests...}}.
///  - QLog: nel suo database qlog.db, tabella log_param: network/listener/wsjtx/port.
/// I programmi riscrivono le impostazioni quando si chiudono, quindi si corregge solo un programma chiuso, e prima si fa una copia
/// del file nella cartella Dati\copie-impostazioni.
/// </summary>
public static class UdpDoctor
{
    public sealed class Finding
    {
        public string Program = "";
        public string File = "";
        public bool Ok;
        public string Text = "";
        public bool Running;
        public Func<string> Fix;      // null = niente da correggere o non correggibile
        public override string ToString() => (Ok ? "✔ " : "✘ ") + Program + ": " + Text;
    }

    /// <summary>Che cosa deve avere ogni programma: dove manda (ascolto di YA-GT) e dove ascolta il log (ritrasmissione di YA-GT).</summary>
    public sealed class Target
    {
        public string ListenHost = "127.0.0.1";
        public int ListenPort = 2237;
        public bool Forward;
        public int ForwardPort = 2238;
        public string ForwardHost = "127.0.0.1";
        /// <summary>UDP secondaria (solo ADIF dei QSO): "warn" = solo avvisi, non si tocca (alimenta magari altri programmi);
        /// "set" = si imposta verso SecondaryHost:SecondaryPort; "off" = si spegne.</summary>
        public string SecondaryMode = "warn";
        public string SecondaryHost = "127.0.0.1";
        public int SecondaryPort = 2333;
        /// <summary>Porte che QLog usa: WSJT-X (protocollo binario) e ADIF grezzo (ricevitore "FLDigi ADIF UDP", 9876).</summary>
        public int QLogWsjtPort = -1, QLogAdifPort = -1;
    }

    /// <summary>Il programma è aperto? Si confronta l'inizio del nome del processo (MSHV_x64, MSHV_WIN64, aurora-desktop...).</summary>
    static bool Running(params string[] names)
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                string n;
                try { n = p.ProcessName; } catch { continue; }
                if (names.Any(x => n.StartsWith(x, StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }
        return false;
    }

    static string Backup(string path, string program)
    {
        var dir = Path.Combine(Settings.DataDir, "copie-impostazioni");
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, program + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Path.GetFileName(path));
        File.Copy(path, dst, true);
        return dst;
    }

    static bool SameHost(string a, string b)
    {
        a = (a ?? "").Trim().Trim('"'); b = (b ?? "").Trim();
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        bool la = a == "localhost" || a == "127.0.0.1" || a == "::1", lb = b == "localhost" || b == "127.0.0.1" || b == "::1";
        return la && lb;
    }

    // ---------------- file INI di Qt (QSettings IniFormat) ----------------

    /// <summary>Un file INI di Qt tenuto riga per riga, per cambiare solo le voci che servono e lasciare il resto identico.</summary>
    sealed class QtIni
    {
        public readonly List<string> Lines;
        readonly string _nl;
        public QtIni(string text)
        {
            _nl = text.Contains("\r\n") ? "\r\n" : "\n";
            Lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (Lines.Count > 0 && Lines[Lines.Count - 1] == "") Lines.RemoveAt(Lines.Count - 1);
        }
        public override string ToString() => string.Join(_nl, Lines) + _nl;

        static string Unquote(string v) { v = v.Trim(); return v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? v.Substring(1, v.Length - 2) : v; }

        /// <summary>Tutte le voci "chiave" nella sezione (anche quelle annidate che finiscono con \chiave, per [MultiSettings]).</summary>
        public IEnumerable<(int line, string fullKey, string value)> Find(string section, string key, bool nested)
        {
            string cur = "";
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i].Trim();
                if (l.StartsWith("[") && l.EndsWith("]")) { cur = l.Substring(1, l.Length - 2); continue; }
                if (!cur.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
                int e = l.IndexOf('=');
                if (e <= 0) continue;
                var k = l.Substring(0, e).Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase) || (nested && (k.EndsWith("\\" + key, StringComparison.OrdinalIgnoreCase) || k.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase))))
                    yield return (i, k, Unquote(l.Substring(e + 1)));
            }
        }

        public string Get(string section, string key) => Find(section, key, false).Select(x => x.value).FirstOrDefault();

        public void Set(string section, string key, string value)
        {
            var hit = Find(section, key, false).ToList();
            if (hit.Count > 0) { foreach (var h in hit) Lines[h.line] = h.fullKey + "=" + value; return; }
            // voce mancante: in fondo alla sezione, o una sezione nuova
            int start = Lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0) { if (Lines.Count > 0 && Lines[Lines.Count - 1].Trim() != "") Lines.Add(""); Lines.Add("[" + section + "]"); Lines.Add(key + "=" + value); return; }
            int end = start + 1;
            while (end < Lines.Count && !Lines[end].TrimStart().StartsWith("[")) end++;
            while (end > start + 1 && Lines[end - 1].Trim() == "") end--;
            Lines.Insert(end, key + "=" + value);
        }

        /// <summary>Cambia anche le voci annidate già presenti (altre configurazioni di WSJT-X/JTDX in [MultiSettings]).</summary>
        public void SetNested(string section, string key, string value)
        {
            foreach (var h in Find(section, key, true).ToList()) Lines[h.line] = h.fullKey + "=" + value;
        }
    }

    // ---------------- ricerca dei file ----------------

    static string Env(Environment.SpecialFolder f) { try { return Environment.GetFolderPath(f); } catch { return ""; } }

    static IEnumerable<string> Files(string dir, string pattern, bool deep = false)
    {
        try { if (dir != "" && Directory.Exists(dir)) return Directory.GetFiles(dir, pattern, deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); } catch { }
        return Array.Empty<string>();
    }

    static IEnumerable<string> Dirs(string dir, string pattern)
    {
        try { if (dir != "" && Directory.Exists(dir)) return Directory.GetDirectories(dir, pattern); } catch { }
        return Array.Empty<string>();
    }

    /// <summary>Cartelle d'installazione possibili (radici dei dischi, Program Files): per MSHV e MadModem, che tengono le impostazioni accanto al programma.</summary>
    static IEnumerable<string> InstallRoots()
    {
        var roots = new List<string>();
        foreach (var v in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" }) { var p = Environment.GetEnvironmentVariable(v); if (!string.IsNullOrEmpty(p)) roots.Add(p); }
        try { foreach (var d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add(d.RootDirectory.FullName); } catch { }
        var home = Env(Environment.SpecialFolder.UserProfile);
        if (home != "") { roots.Add(home); roots.Add(Path.Combine(home, "Desktop")); roots.Add(Path.Combine(home, "Downloads")); }
        roots.Add("/opt"); roots.Add("/usr/share");
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static List<string> WsjtLike(string app)
    {
        var list = new List<string>();
        var local = Env(Environment.SpecialFolder.LocalApplicationData);
        var home = Env(Environment.SpecialFolder.UserProfile);
        list.AddRange(Files(Path.Combine(local, app), app + "*.ini"));
        foreach (var d in Dirs(local, app + " - *")) list.AddRange(Files(d, app + "*.ini"));   // --rig-name (anche le copie avviate da N1MM)
        list.AddRange(Files(local, app + "*.ini"));
        if (home != "") list.AddRange(Files(Path.Combine(home, ".config"), app + "*.ini"));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> MshvFiles(IEnumerable<string> extraDirs)
    {
        var list = new List<string>();
        foreach (var r in InstallRoots())
            foreach (var d in Dirs(r, "MSHV*"))
            {
                list.AddRange(Files(Path.Combine(d, "settings"), "ms_stinfonet"));
                foreach (var sub in Dirs(d, "*")) list.AddRange(Files(Path.Combine(sub, "settings"), "ms_stinfonet"));
            }
        foreach (var d in extraDirs) list.AddRange(Files(Path.Combine(d, "settings"), "ms_stinfonet"));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> MadModemFiles(IEnumerable<string> extraDirs)
    {
        var list = new List<string>();
        foreach (var r in InstallRoots())
            foreach (var d in Dirs(r, "MadModem*")) list.AddRange(Files(d, "settings.mad", true));
        foreach (var b in new[] { Env(Environment.SpecialFolder.LocalApplicationData), Env(Environment.SpecialFolder.ApplicationData), Path.Combine(Env(Environment.SpecialFolder.UserProfile), ".config") })
        {
            list.AddRange(Files(Path.Combine(b, "MadModem"), "settings.mad", true));
            foreach (var d in Dirs(b, "*")) list.AddRange(Files(Path.Combine(d, "MadModem"), "settings.mad"));
        }
        foreach (var d in extraDirs) list.AddRange(Files(d, "settings.mad"));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> N1mmFiles()
    {
        var docs = Env(Environment.SpecialFolder.MyDocuments);
        return Files(Path.Combine(docs, "N1MM Logger+"), "N1MM Logger*.ini").ToList();
    }

    /// <summary>
    /// N1MM Logger+: N1MM Logger.ini, sezione [ExternalBroadcast]: IsBroadcastContact, DestinationIPs ("ip" o "ip:porta" separati da spazi)
    /// e DestinationPort (di base 12060). N1MM manda i QSO in XML (contactinfo), non nel protocollo WSJT-X e non in ADIF.
    /// </summary>
    static Finding CheckN1mm(string file, Target t)
    {
        var ini = new QtIni(File.ReadAllText(file));
        bool on = (ini.Get("ExternalBroadcast", "IsBroadcastContact") ?? "False").Equals("True", StringComparison.OrdinalIgnoreCase);
        int.TryParse(ini.Get("ExternalBroadcast", "DestinationPort") ?? "12060", out var defPort);
        if (defPort <= 0) defPort = 12060;
        var dest = new List<(string host, int port)>();
        foreach (var tok in (ini.Get("ExternalBroadcast", "DestinationIPs") ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int c = tok.LastIndexOf(':');
            if (c > 0 && int.TryParse(tok.Substring(c + 1), out var pp)) dest.Add((tok.Substring(0, c), pp)); else dest.Add((tok, defPort));
        }
        var name = "N1MM Logger+ — invio QSO (XML)" + (Path.GetFileNameWithoutExtension(file).Length > 11 ? " [" + Path.GetFileNameWithoutExtension(file) + "]" : "");
        var problems = new List<string>();
        if (on)
        {
            if (dest.Count == 0) problems.Add("invio dei QSO acceso ma senza destinazione (DestinationIPs vuoto)");
            foreach (var (h, pp) in dest)
            {
                bool local = SameHost(h, "127.0.0.1");
                if (!local) continue;
                if (pp == t.ListenPort) problems.Add($"manda l'XML alla porta {pp}, dove ascolta YA-GT con il protocollo WSJT-X");
                else if (t.QLogWsjtPort > 0 && pp == t.QLogWsjtPort) problems.Add($"manda l'XML alla porta WSJT-X di QLog ({pp}): QLog non legge l'XML di N1MM");
                else if (t.QLogAdifPort > 0 && pp == t.QLogAdifPort) problems.Add($"manda l'XML al ricevitore ADIF di QLog ({pp}): QLog lì aspetta l'ADIF, non l'XML di N1MM");
                else if (!Listening(pp)) problems.Add($"nessun programma ascolta sulla porta {pp}");
            }
        }
        if (t.SecondaryMode == "off" && on) problems.Add("in YA-GT hai scelto di spegnere gli invii secondari");
        var f = new Finding { Program = name, File = file, Running = Running("n1mmlogger"), Ok = problems.Count == 0 };
        f.Text = problems.Count == 0 ? (on ? "manda i QSO a " + string.Join(", ", dest.Select(d => d.host + ":" + d.port)) : "invio dei QSO spento") + (t.SecondaryMode == "warn" ? " (YA-GT non lo tocca)" : "") : string.Join("; ", problems);
        if (problems.Count > 0 && t.SecondaryMode == "off")
            f.Fix = () =>
            {
                if (Running("n1mmlogger")) return "N1MM Logger+: è aperto, chiudilo e riprova (alla chiusura riscriverebbe il file .ini)";
                var bak = Backup(file, "N1MM");
                var w = new QtIni(File.ReadAllText(file));
                w.Set("ExternalBroadcast", "IsBroadcastContact", "False");
                File.WriteAllText(file, w.ToString(), new UTF8Encoding(false));
                return $"N1MM Logger+: invio dei QSO spento (copia in {Path.GetFileName(bak)})";
            };
        else if (problems.Count > 0 && t.SecondaryMode == "set" && dest.Count > 0)
            f.Fix = () =>
            {
                if (Running("n1mmlogger")) return "N1MM Logger+: è aperto, chiudilo e riprova (alla chiusura riscriverebbe il file .ini)";
                var bak = Backup(file, "N1MM");
                var w = new QtIni(File.ReadAllText(file));
                // si tengono le destinazioni giuste, si tolgono quelle che vanno a YA-GT o a QLog (protocollo sbagliato)
                var keep = dest.Where(d => !(SameHost(d.host, "127.0.0.1") && (d.port == t.ListenPort || d.port == t.QLogWsjtPort || d.port == t.QLogAdifPort))).ToList();
                w.Set("ExternalBroadcast", "DestinationIPs", string.Join(" ", keep.Select(d => d.host + ":" + d.port)));
                if (keep.Count == 0) w.Set("ExternalBroadcast", "IsBroadcastContact", "False");
                File.WriteAllText(file, w.ToString(), new UTF8Encoding(false));
                return "N1MM Logger+: tolte le destinazioni sbagliate" + (keep.Count == 0 ? " e spento l'invio" : "") + $" (copia in {Path.GetFileName(bak)})";
            };
        else if (problems.Count > 0) f.Text += " — per farlo correggere scegli in Impostazioni \"spegnila\" o \"mandala a\" per la UDP secondaria";
        return f;
    }

    public static List<string> AuroraFiles()
    {
        var list = new List<string>();
        var dd = Environment.GetEnvironmentVariable("AURORA_DATA_DIR");
        if (!string.IsNullOrEmpty(dd)) list.AddRange(Files(dd, "settings-motore.json"));
        list.AddRange(Files(Path.Combine(Env(Environment.SpecialFolder.ApplicationData), "Aurora"), "settings-motore.json"));
        var home = Env(Environment.SpecialFolder.UserProfile);
        if (home != "") { list.AddRange(Files(Path.Combine(home, ".local", "share", "Aurora"), "settings-motore.json")); list.AddRange(Files(Path.Combine(home, ".config", "Aurora"), "settings-motore.json")); }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------- controlli ----------------

    public static List<Finding> Check(Target t, string qlogPath, IEnumerable<string> extraDirs)
    {
        var res = new List<Finding>();
        // prima le porte di QLog, che servono per capire dove va la UDP secondaria
        try
        {
            var q0 = string.IsNullOrWhiteSpace(qlogPath) ? QLogDb.DefaultPath() : qlogPath;
            if (File.Exists(q0)) ReadQLogPorts(q0, t);
        }
        catch { }
        var extra = (extraDirs ?? Enumerable.Empty<string>()).Where(d => d != "").ToList();
        bool multicast = IPAddress.TryParse(t.ListenHost, out var lip) && (lip.GetAddressBytes()[0] & 0xF0) == 0xE0;
        string sendHost = multicast || (t.ListenHost != "0.0.0.0" && t.ListenHost != "") ? t.ListenHost : "127.0.0.1";

        foreach (var app in new[] { "WSJT-X", "JTDX" })
            foreach (var f in WsjtLike(app))
                try
                {
                    var a = CheckWsjt(app, f, t, sendHost);
                    res.Add(a);
                    res.Add(SecondaryWsjt(app, f, t, a.Program, a.Running));
                }
                catch (Exception ex) { res.Add(new Finding { Program = app, File = f, Text = "non leggibile: " + ex.Message }); }
        foreach (var f in MshvFiles(extra))
            try
            {
                var a = CheckMshv(f, t, sendHost);
                res.Add(a);
                var l = File.ReadAllLines(f).Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("udp2_broad_all="));
                var parts = (l == null ? "127.0.0.1#2333#0" : l.Substring(15).Trim('"')).Split('#');
                int.TryParse(parts.Length > 1 ? parts[1] : "2333", out var p2);
                string file = f;
                res.Add(Secondary(a.Program + " — 2° server UDP (ADIF)", f, t, parts.Length > 2 && parts[2] == "1", parts[0], p2, a.Running, "mshv", (h, pp, e) =>
                {
                    var lines = File.ReadAllLines(file).ToList();
                    var v = "udp2_broad_all=" + h + "#" + pp.ToString(CultureInfo.InvariantCulture) + "#" + (e ? "1" : "0");
                    int i = lines.FindIndex(x => x.Trim().StartsWith("udp2_broad_all="));
                    if (i >= 0) lines[i] = v; else lines.Add(v);
                    Backup(file, "MSHV");
                    File.WriteAllText(file, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
                }));
            }
            catch (Exception ex) { res.Add(new Finding { Program = "MSHV", File = f, Text = "non leggibile: " + ex.Message }); }
        foreach (var f in MadModemFiles(extra))
            try { res.Add(CheckMadModem(f, t, sendHost)); } catch (Exception ex) { res.Add(new Finding { Program = "MadModem", File = f, Text = "non leggibile: " + ex.Message }); }
        foreach (var f in AuroraFiles())
            try
            {
                var a = CheckAurora(f, t, sendHost);
                res.Add(a);
                var vals = (JsonNode.Parse(File.ReadAllText(f)) as JsonObject)?["values"] as JsonObject;
                string V(string k, string d) => vals?[k]?.GetValue<string>() ?? d;
                int.TryParse(V("UDP2ServerPort", "2237"), out var p2);
                string file = f;
                res.Add(Secondary("Aurora — 2° server UDP (ADIF)", f, t, V("EnableUDP2adifBroadcast", "false").Equals("true", StringComparison.OrdinalIgnoreCase), V("UDP2Server", "127.0.0.1"), p2, a.Running, "aurora", (h, pp, e) =>
                {
                    var r2 = (JsonObject)JsonNode.Parse(File.ReadAllText(file));
                    var v2 = (JsonObject)r2["values"];
                    v2["UDP2Server"] = h; v2["UDP2ServerPort"] = pp.ToString(CultureInfo.InvariantCulture); v2["EnableUDP2adifBroadcast"] = e ? "true" : "false";
                    Backup(file, "Aurora");
                    File.WriteAllText(file, r2.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                }));
            }
            catch (Exception ex) { res.Add(new Finding { Program = "Aurora", File = f, Text = "non leggibile: " + ex.Message }); }
        foreach (var f in N1mmFiles())
            try { var n = CheckN1mm(f, t); if (n.Text != "invio dei QSO spento (YA-GT non lo tocca)") res.Add(n); } catch (Exception ex) { res.Add(new Finding { Program = "N1MM Logger+", File = f, Text = "non leggibile: " + ex.Message }); }
        var qdb = string.IsNullOrWhiteSpace(qlogPath) ? QLogDb.DefaultPath() : qlogPath;
        if (File.Exists(qdb))
            try { res.Insert(0, CheckQLog(qdb, t)); } catch (Exception ex) { res.Add(new Finding { Program = "QLog", File = qdb, Text = "non leggibile: " + ex.Message }); }
        res.AddRange(CheckPorts(t));
        if (res.Count == 0) res.Add(new Finding { Program = "Verifica", Ok = true, Text = "nessun programma trovato (WSJT-X, JTDX, MSHV, MadModem, Aurora, N1MM Logger+, QLog)" });
        return res;
    }

    static string Want(Target t, string sendHost) => sendHost + ":" + t.ListenPort.ToString(CultureInfo.InvariantCulture);

    static Finding SecondaryWsjt(string app, string file, Target t, string name, bool running)
    {
        var ini = new QtIni(File.ReadAllText(file));
        // JTDX: secondo server UDP (ADIF); WSJT-X: "N1MM Logger+ Broadcasts" (ADIF del QSO registrato)
        string kh = app == "JTDX" ? "UDP2Server" : "N1MMServer", kp = app == "JTDX" ? "UDP2ServerPort" : "N1MMServerPort", ke = app == "JTDX" ? "EnableUDP2adifBroadcast" : "BroadcastToN1MM";
        var host = ini.Get("Configuration", kh) ?? "127.0.0.1";
        int.TryParse(ini.Get("Configuration", kp) ?? "2333", out var port);
        bool on = (ini.Get("Configuration", ke) ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        return Secondary(name + (app == "JTDX" ? " — 2° server UDP (ADIF)" : " — invio ADIF \"N1MM\""), file, t, on, host, port, running, app == "JTDX" ? "jtdx" : "wsjtx",
            (h, p2, e) =>
            {
                var w = new QtIni(File.ReadAllText(file));
                foreach (var (k, v) in new[] { (kh, h), (kp, p2.ToString(CultureInfo.InvariantCulture)), (ke, e ? "true" : "false") })
                { w.Set("Configuration", k, v); w.SetNested("MultiSettings", k, v); }
                Backup(file, app);
                File.WriteAllText(file, w.ToString(), new UTF8Encoding(false));
            });
    }

    /// <summary>Controllo comune della UDP secondaria (ADIF): avvisi sulle scelte fuorvianti e, se richiesto, la correzione.</summary>
    static Finding Secondary(string name, string file, Target t, bool on, string host, int port, bool running, string proc, Action<string, int, bool> apply)
    {
        var problems = new List<string>();
        bool local = SameHost(host, "127.0.0.1");
        if (on)
        {
            if (local && port == t.ListenPort) problems.Add($"manda l'ADIF alla porta {port}, dove ascolta YA-GT con il protocollo WSJT-X: va su un'altra porta");
            if (local && t.QLogWsjtPort > 0 && port == t.QLogWsjtPort) problems.Add($"manda l'ADIF alla porta WSJT-X di QLog ({port}): QLog lì aspetta il protocollo WSJT-X, non l'ADIF");
            if (local && t.QLogAdifPort > 0 && port == t.QLogAdifPort && t.Forward) problems.Add($"manda l'ADIF a QLog ({port}) mentre QLog riceve già il QSO dal rimbalzo di YA-GT: rischio di QSO doppi");
            if (local && !Listening(port)) problems.Add($"nessun programma ascolta sulla porta {port} (log chiuso o porta sbagliata)");
        }
        bool want = t.SecondaryMode == "set", off = t.SecondaryMode == "off";
        if (want && (!on || !SameHost(host, t.SecondaryHost) || port != t.SecondaryPort)) problems.Add($"in YA-GT hai scelto di mandarla a {t.SecondaryHost}:{t.SecondaryPort}");
        if (off && on) problems.Add("in YA-GT hai scelto di spegnerla");
        var f = new Finding { Program = name, File = file, Running = running, Ok = problems.Count == 0 };
        f.Text = problems.Count == 0 ? (on ? $"ADIF a {host}:{port}" : "spenta") + (t.SecondaryMode == "warn" ? " (YA-GT non la tocca)" : "") : string.Join("; ", problems);
        if (problems.Count > 0 && (want || off))
            f.Fix = () =>
            {
                if (Running(proc)) return name + ": è aperto, chiudilo e riprova";
                apply(want ? t.SecondaryHost : host, want ? t.SecondaryPort : port, want);
                return name + (want ? $": ADIF verso {t.SecondaryHost}:{t.SecondaryPort}" : ": UDP secondaria spenta");
            };
        else if (problems.Count > 0) f.Text += " — per farla correggere scegli in Impostazioni la destinazione della UDP secondaria (ora: solo avvisi)";
        return f;
    }

    static bool Listening(int port)
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == port); } catch { return true; }
    }

    static void ReadQLogPorts(string db, Target t)
    {
        var copy = DbCopy.Make(db, "qlog-copia", "qlog-verifica.db");
        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            con.Open();
            int Get(string name, int def)
            {
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM log_param WHERE name=$n";
                    cmd.Parameters.AddWithValue("$n", name);
                    var v = cmd.ExecuteScalar();
                    return v != null && v != DBNull.Value && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var x) ? x : def;
                }
            }
            t.QLogWsjtPort = Get("network/listener/wsjtx/port", 2237);
            t.QLogAdifPort = Get("network/listener/fldigi/udp/port", 9876);
        }
    }

    static Finding CheckWsjt(string app, string file, Target t, string sendHost)
    {
        var ini = new QtIni(File.ReadAllText(file));
        string host = ini.Get("Configuration", "UDPServer") ?? "127.0.0.1";
        string portS = ini.Get("Configuration", "UDPServerPort") ?? "2237";
        string accept = ini.Get("Configuration", "AcceptUDPRequests") ?? "false";
        int.TryParse(portS, out var port);
        var problems = new List<string>();
        if (!SameHost(host, sendHost)) problems.Add($"manda a {host} invece di {sendHost}");
        if (port != t.ListenPort) problems.Add($"porta {port} invece di {t.ListenPort}");
        if (!accept.Equals("true", StringComparison.OrdinalIgnoreCase)) problems.Add("\"Accetta richieste UDP\" spento (le risposte di QLog e degli altri vengono ignorate)");
        var others = ini.Find("MultiSettings", "UDPServerPort", true).Where(x => x.value != t.ListenPort.ToString(CultureInfo.InvariantCulture)).Count();
        if (others > 0) problems.Add($"{others} altre configurazioni con un'altra porta");
        bool running = Running(app == "JTDX" ? "jtdx" : "wsjtx");
        var name = app + (Path.GetFileNameWithoutExtension(file).Length > app.Length ? " (" + Path.GetFileNameWithoutExtension(file).Substring(app.Length).Trim(' ', '-') + ")" : "");
        var f = new Finding { Program = name, File = file, Running = running, Ok = problems.Count == 0 };
        f.Text = f.Ok ? $"manda a {Want(t, sendHost)}, richieste UDP accettate" : string.Join("; ", problems);
        if (!f.Ok)
            f.Fix = () =>
            {
                if (Running(app == "JTDX" ? "jtdx" : "wsjtx")) return $"{name}: è aperto, chiudilo e riprova (alla chiusura riscriverebbe le impostazioni)";
                var bak = Backup(file, app);
                var w = new QtIni(File.ReadAllText(file));
                foreach (var (k, v) in new[] { ("UDPServer", sendHost), ("UDPServerPort", t.ListenPort.ToString(CultureInfo.InvariantCulture)), ("AcceptUDPRequests", "true") })
                { w.Set("Configuration", k, v); w.SetNested("MultiSettings", k, v); }
                File.WriteAllText(file, w.ToString(), new UTF8Encoding(false));
                return $"{name}: corretto (copia in {Path.GetFileName(bak)})";
            };
        return f;
    }

    static Finding CheckMshv(string file, Target t, string sendHost)
    {
        var lines = File.ReadAllLines(file).ToList();
        string Val(string k) => lines.Select(l => l.Trim()).Where(l => l.StartsWith(k + "=")).Select(l => l.Substring(k.Length + 1).Trim('"')).FirstOrDefault();
        var host = Val("udp_broad_server") ?? "127.0.0.1";
        int.TryParse(Val("udp_broad_port") ?? "2237", out var port);
        var flags = (Val("udp_broad_log_all") ?? "0#0#0").Split('#');
        bool logQso = flags.Length > 0 && flags[0] == "1", dec = flags.Length > 2 && flags[2] == "1";
        var problems = new List<string>();
        if (!SameHost(host, sendHost)) problems.Add($"manda a {host} invece di {sendHost}");
        if (port != t.ListenPort) problems.Add($"porta {port} invece di {t.ListenPort}");
        if (!dec) problems.Add("\"Enable Decoded Text\" spento (YA-GT non vede le decodifiche)");
        if (!logQso) problems.Add("\"Enable Logged QSO\" spento (i QSO non arrivano a QLog)");
        var dir = Path.GetDirectoryName(Path.GetDirectoryName(file)) ?? "";
        var name = "MSHV (" + Path.GetFileName(dir) + ")";
        var f = new Finding { Program = name, File = file, Running = Running("mshv"), Ok = problems.Count == 0 };
        f.Text = f.Ok ? $"manda a {Want(t, sendHost)} decodifiche e QSO" : string.Join("; ", problems);
        if (!f.Ok)
            f.Fix = () =>
            {
                if (Running("mshv")) return name + ": è aperto, chiudilo e riprova (alla chiusura riscriverebbe le impostazioni)";
                var bak = Backup(file, "MSHV");
                var l2 = File.ReadAllLines(file).ToList();
                void Put(string k, string v)
                {
                    int i = l2.FindIndex(l => l.Trim().StartsWith(k + "="));
                    if (i >= 0) l2[i] = k + "=" + v; else l2.Add(k + "=" + v);
                }
                var fl = (Val("udp_broad_log_all") ?? "0#0#0").Split('#').ToList();
                while (fl.Count < 3) fl.Add("0");
                fl[0] = "1"; fl[2] = "1";
                Put("udp_broad_server", sendHost);
                Put("udp_broad_port", t.ListenPort.ToString(CultureInfo.InvariantCulture));
                Put("udp_broad_log_all", string.Join("#", fl));
                File.WriteAllText(file, string.Join("\n", l2) + "\n", new UTF8Encoding(false));
                return $"{name}: corretto (copia in {Path.GetFileName(bak)})";
            };
        return f;
    }

    static Finding CheckMadModem(string file, Target t, string sendHost)
    {
        var ini = new QtIni(File.ReadAllText(file));
        bool on = (ini.Get("Logbook", "udpEnabled") ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        var host = ini.Get("Logbook", "udpServer") ?? "127.0.0.1";
        int.TryParse(ini.Get("Logbook", "udpPort") ?? "2237", out var port);
        // MadModem manda solo il QSO registrato (e il segnale di vita): conviene mandarlo dove ascolta QLog o a YA-GT che lo rimbalza
        var problems = new List<string>();
        if (!on) problems.Add("invio UDP dei QSO spento");
        if (!SameHost(host, sendHost)) problems.Add($"manda a {host} invece di {sendHost}");
        if (port != t.ListenPort) problems.Add($"porta {port} invece di {t.ListenPort}");
        var name = "MadModem (" + Path.GetFileName(Path.GetDirectoryName(file)) + ")";
        var f = new Finding { Program = name, File = file, Running = Running("madmodem"), Ok = problems.Count == 0 };
        f.Text = f.Ok ? $"manda i QSO a {Want(t, sendHost)}" : string.Join("; ", problems);
        if (!f.Ok)
            f.Fix = () =>
            {
                if (Running("madmodem")) return name + ": è aperto, chiudilo e riprova (alla chiusura riscriverebbe le impostazioni)";
                var bak = Backup(file, "MadModem");
                var w = new QtIni(File.ReadAllText(file));
                w.Set("Logbook", "udpEnabled", "true");
                w.Set("Logbook", "udpServer", sendHost);
                w.Set("Logbook", "udpPort", t.ListenPort.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(file, w.ToString(), new UTF8Encoding(false));
                return $"{name}: corretto (copia in {Path.GetFileName(bak)})";
            };
        return f;
    }

    static Finding CheckAurora(string file, Target t, string sendHost)
    {
        var root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new Exception("file non valido");
        var vals = root["values"] as JsonObject ?? throw new Exception("manca \"values\"");
        string V(string k, string d) => vals[k]?.GetValue<string>() ?? d;
        var host = V("UDPServer", "127.0.0.1");
        int.TryParse(V("UDPServerPort", "2237"), out var port);
        bool accept = V("AcceptUDPRequests", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        var problems = new List<string>();
        if (host == "") problems.Add("server UDP primario spento");
        else if (!SameHost(host, sendHost)) problems.Add($"manda a {host} invece di {sendHost}");
        if (port != t.ListenPort) problems.Add($"porta {port} invece di {t.ListenPort}");
        if (!accept) problems.Add("\"Accetta richieste UDP\" spento");
        var f = new Finding { Program = "Aurora", File = file, Running = Running("aurora"), Ok = problems.Count == 0 };
        f.Text = f.Ok ? $"manda a {Want(t, sendHost)}, richieste UDP accettate" : string.Join("; ", problems);
        if (!f.Ok)
            f.Fix = () =>
            {
                if (Running("aurora")) return "Aurora: è aperta, chiudila e riprova";
                var bak = Backup(file, "Aurora");
                var r2 = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                var v2 = (JsonObject)r2["values"];
                v2["UDPServer"] = sendHost;
                v2["UDPServerPort"] = t.ListenPort.ToString(CultureInfo.InvariantCulture);
                v2["AcceptUDPRequests"] = "true";
                File.WriteAllText(file, r2.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                return $"Aurora: corretta (copia in {Path.GetFileName(bak)})";
            };
        return f;
    }

    static Finding CheckQLog(string db, Target t)
    {
        var copy = DbCopy.Make(db, "qlog-copia", "qlog-verifica.db");
        int port = 2237;
        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            con.Open();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM log_param WHERE name='network/listener/wsjtx/port'";
                var v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value) int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out port);
            }
        }
        var f = new Finding { Program = "QLog", File = db, Running = Running("qlog") };
        var problems = new List<string>();
        if (!t.Forward) problems.Add("in YA-GT il rimbalzo UDP è spento: accendilo nelle Impostazioni, altrimenti a QLog non arriva nulla");
        if (port == t.ListenPort) problems.Add($"ascolta sulla {port}, la stessa porta di YA-GT: i due programmi si rubano i pacchetti");
        else if (port != t.ForwardPort) problems.Add($"ascolta sulla {port}, ma YA-GT ritrasmette sulla {t.ForwardPort}");
        f.Ok = problems.Count == 0;
        f.Text = f.Ok ? $"ascolta sulla {port}, dove YA-GT ritrasmette" : string.Join("; ", problems);
        if (!f.Ok && (port != t.ForwardPort))
            f.Fix = () =>
            {
                if (Running("qlog")) return "QLog: è aperto, chiudilo e riprova";
                var bak = Backup(db, "QLog");
                using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
                {
                    con.Open();
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText = "INSERT OR REPLACE INTO log_param (name, value) VALUES ('network/listener/wsjtx/port', $p)";
                        cmd.Parameters.AddWithValue("$p", t.ForwardPort.ToString(CultureInfo.InvariantCulture));
                        cmd.ExecuteNonQuery();
                    }
                }
                return $"QLog: ora ascolta sulla {t.ForwardPort} (copia in {Path.GetFileName(bak)})" + (t.Forward ? "" : " — accendi anche il rimbalzo UDP in YA-GT");
            };
        return f;
    }

    /// <summary>Le porte UDP in ascolto sul computer: se la porta di ritrasmissione è libera, nessuno riceve quello che YA-GT rimbalza.</summary>
    static IEnumerable<Finding> CheckPorts(Target t)
    {
        IPEndPoint[] eps;
        try { eps = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners(); } catch { yield break; }
        bool fw = eps.Any(e => e.Port == t.ForwardPort);
        if (t.Forward && SameHost(t.ForwardHost, "127.0.0.1"))
            yield return new Finding { Program = "Porta " + t.ForwardPort, Ok = fw, Text = fw ? "qualcuno ascolta sulla porta di ritrasmissione (di solito QLog)" : "nessun programma ascolta sulla porta di ritrasmissione: apri QLog o controlla la sua porta" };
    }

    /// <summary>Le correzioni possibili, una per programma; restituisce l'esito di ognuna.</summary>
    public static List<string> FixAll(IEnumerable<Finding> findings)
    {
        var res = new List<string>();
        foreach (var f in findings.Where(x => x.Fix != null))
            try { res.Add(f.Fix()); } catch (Exception ex) { res.Add(f.Program + ": non corretto, " + ex.Message); }
        return res;
    }
}
