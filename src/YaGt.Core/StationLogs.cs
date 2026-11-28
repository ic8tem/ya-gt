using System.Data.Common;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace YaGt.Core;

/// <summary>
/// I log di stazione oltre a QLog, uno per riga nelle Impostazioni:
///  - file ADIF (.adi, .adif) e ADX (.adx): log di WSJT-X, JTDX, MSHV, Logger32, UcxLog... riletti quando cambiano;
///  - file Cabrillo (.log, .cbr) dei contest;
///  - database SQLite (.sqlite, .s3db, .db, .dat, .sql): Log4OM 2 e 1, N1MM Logger+, KLog, MacLoggerDX...;
///  - database Microsoft Access (.mdb, .accdb, solo Windows): DXKeeper, ACLog, Swisslog, HRD Logbook;
///  - "mysql: server=...;database=...;user=...;password=..." (CQRLOG, HRD o Log4OM su MySQL/MariaDB);
///  - "wavelog: https://indirizzo CHIAVE ID_STAZIONE" (Wavelog e Cloudlog, api/get_contacts_adif);
///  - "qrz: CHIAVE" (QRZ Logbook, serve l'abbonamento QRZ).
/// I file degli altri programmi non si toccano mai: i database si copiano nella cartella Dati e si legge la copia.
/// Nei database la tabella e le colonne si riconoscono dai nomi, così la lettura regge alle varie versioni dei programmi.
/// </summary>
public sealed class StationLogs
{
    public sealed class Source
    {
        public string Line = "";
        public string Kind = "";
        public string Status = "";
        public List<QsoRecord> Qsos = new List<QsoRecord>();
        public string Stamp = "";
        public DateTime Fetched = DateTime.MinValue;
    }

    readonly object _lock = new object();
    readonly Dictionary<string, Source> _cache = new Dictionary<string, Source>();
    public List<Source> Sources = new List<Source>();

    public List<QsoRecord> Qsos
    {
        get { lock (_lock) return Sources.SelectMany(s => s.Qsos).ToList(); }
    }

    public string Status
    {
        get { lock (_lock) return string.Join(" · ", Sources.Where(s => s.Status != "").Select(s => s.Status)); }
    }

    public static IEnumerable<string> Lines(string text) =>
        (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith("#"));

    public static string KindOf(string line)
    {
        var l = line.Trim();
        int c = l.IndexOf(':');
        if (c > 1)
        {
            var p = l.Substring(0, c).ToLowerInvariant();
            if (p == "mysql" || p == "wavelog" || p == "cloudlog" || p == "qrz") return p == "cloudlog" ? "wavelog" : p;
        }
        switch (Path.GetExtension(l).ToLowerInvariant())
        {
            case ".adi": case ".adif": return "adif";
            case ".adx": return "adx";
            case ".log": case ".cbr": case ".cabrillo": return "cabrillo";
            case ".mdb": case ".accdb": return "access";
            default: return "sqlite";
        }
    }

    /// <summary>Legge tutte le fonti (i file solo se sono cambiati, quelle in rete al più ogni 30 minuti).</summary>
    public async Task LoadAsync(string text, HttpClient http, CancellationToken ct)
    {
        var list = new List<Source>();
        foreach (var line in Lines(text))
        {
            Source s;
            lock (_lock) { if (!_cache.TryGetValue(line, out s)) { s = new Source { Line = line, Kind = KindOf(line) }; _cache[line] = s; } }
            try
            {
                switch (s.Kind)
                {
                    case "mysql": ReadDb(s, () => MySqlOpen(Arg(line)), "MySQL"); break;
                    case "wavelog": if (DateTime.UtcNow - s.Fetched > TimeSpan.FromMinutes(30)) await Wavelog(s, http, ct); break;
                    case "qrz": if (DateTime.UtcNow - s.Fetched > TimeSpan.FromMinutes(30)) await Qrz(s, http, ct); break;
                    default: ReadFile(s); break;
                }
            }
            catch (Exception ex) { s.Status = Label(s) + ": " + ex.Message; }
            list.Add(s);
        }
        lock (_lock) Sources = list;
    }

    static string Arg(string line) { int c = line.IndexOf(':'); return c < 0 ? "" : line.Substring(c + 1).Trim(); }

    static string Label(Source s)
    {
        switch (s.Kind)
        {
            case "mysql": return "MySQL";
            case "wavelog": return "Wavelog";
            case "qrz": return "QRZ";
            default: return Path.GetFileName(s.Line);
        }
    }

    // ---------------- file ----------------

    void ReadFile(Source s)
    {
        var path = s.Line;
        if (!File.Exists(path)) { s.Qsos = new List<QsoRecord>(); s.Status = Path.GetFileName(path) + L.T(": file non trovato"); return; }
        var fi = new FileInfo(path);
        var stamp = fi.LastWriteTimeUtc.Ticks + "/" + fi.Length;
        foreach (var suf in new[] { "-wal", "-journal" })
            if (File.Exists(path + suf)) { var w = new FileInfo(path + suf); stamp += "/" + w.LastWriteTimeUtc.Ticks + "/" + w.Length; }
        if (stamp == s.Stamp) return;
        List<QsoRecord> q;
        switch (s.Kind)
        {
            case "adif": q = Adif.Parse(ReadShared(path)).Select(f => Adif.ToQso(f, false)).Where(x => x.Call != "").ToList(); break;
            case "adx": q = ReadAdx(ReadShared(path)); break;
            case "cabrillo": q = ReadCabrillo(Encoding.UTF8.GetString(ReadShared(path))); break;
            case "access":
            {
                var copy = DbCopy.Make(path, "log-copie", Safe(path));
                ReadDb(s, () => AccessOpen(copy), Path.GetFileName(path));
                s.Stamp = stamp;
                return;
            }
            default:
            {
                var copy = DbCopy.Make(path, "log-copie", Safe(path));
                ReadDb(s, () =>
                {
                    var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
                    c.Open();
                    return c;
                }, Path.GetFileName(path));
                s.Stamp = stamp;
                return;
            }
        }
        s.Qsos = q;
        s.Stamp = stamp;
        s.Status = $"{Path.GetFileName(path)}: {q.Count} QSO";
    }

    static string Safe(string path)
    {
        var n = Path.GetFileName(path);
        uint h = 2166136261;
        foreach (var ch in path.ToLowerInvariant()) h = (h ^ ch) * 16777619;
        return h.ToString("x8") + "-" + n;
    }

    static byte[] ReadShared(string path)
    {
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var m = new MemoryStream()) { f.CopyTo(m); return m.ToArray(); }
    }

    static List<QsoRecord> ReadAdx(byte[] data)
    {
        var list = new List<QsoRecord>();
        var doc = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(data));
        foreach (var r in doc.Descendants().Where(e => e.Name.LocalName.Equals("RECORD", StringComparison.OrdinalIgnoreCase)))
        {
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in r.Elements()) f[e.Name.LocalName] = e.Value;
            var q = Adif.ToQso(f, false);
            if (q.Call != "") list.Add(q);
        }
        return list;
    }

    /// <summary>Cabrillo: "QSO: freq modo data ora mio-call rst scambio call rst scambio"; il locatore se c'è nello scambio.</summary>
    static List<QsoRecord> ReadCabrillo(string text)
    {
        var list = new List<QsoRecord>();
        string myCall = "";
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.StartsWith("CALLSIGN:", StringComparison.OrdinalIgnoreCase)) myCall = l.Substring(9).Trim().ToUpperInvariant();
            if (!l.StartsWith("QSO:", StringComparison.OrdinalIgnoreCase)) continue;
            var t = l.Substring(4).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 7) continue;
            double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var khz);
            var mode = t[1].ToUpperInvariant();
            var q = new QsoRecord
            {
                // frequenza in kHz in HF; da 50 MHz in su Cabrillo scrive la banda in MHz (50, 144, 432...)
                Band = Bands.Normalize("", khz <= 0 ? "" : (khz < 1000 ? khz : khz / 1000.0).ToString(CultureInfo.InvariantCulture)),
                Mode = mode == "PH" ? "SSB" : mode == "RY" ? "RTTY" : mode == "DG" ? "DATA" : mode,
                Date = t[2].Replace("-", ""),
                Time = t[3].PadLeft(4, '0'),
            };
            // la seconda stazione dopo la nostra: si cerca il primo nominativo diverso dal nostro dopo l'ora
            int mine = 4;
            if (myCall != "") for (int i = 4; i < t.Length; i++) if (t[i].Equals(myCall, StringComparison.OrdinalIgnoreCase)) { mine = i; break; }
            for (int i = mine + 1; i < t.Length; i++)
            {
                var w = t[i].ToUpperInvariant();
                if (q.Call == "" && w.Length >= 3 && w.Any(char.IsDigit) && w.Any(char.IsLetter) && !Locator.IsValid(w) && !w.All(char.IsDigit)) { q.Call = w; continue; }
                if (q.Call != "" && Locator.IsValid(w)) { q.Grid = w; q.AddGrid(w); break; }
            }
            if (q.Call != "" && q.Band != "?") list.Add(q);
        }
        return list;
    }

    // ---------------- database (SQLite, Access, MySQL) ----------------

    static DbConnection AccessOpen(string file)
    {
#if NETFRAMEWORK
        Exception last = null;
        foreach (var prov in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" })
        {
            try
            {
                var c = new System.Data.OleDb.OleDbConnection($"Provider={prov};Data Source={file};Mode=Read;Persist Security Info=False");
                c.Open();
                return c;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new Exception(L.T("manca il driver di Microsoft Access (Access Database Engine) per questa versione di Windows") + (last != null ? " — " + last.Message : ""));
#else
        if (!OperatingSystem.IsWindows()) throw new Exception(L.T("i database Access si leggono solo su Windows"));
        Exception last = null;
        foreach (var prov in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" })
        {
            try
            {
                var c = new System.Data.OleDb.OleDbConnection($"Provider={prov};Data Source={file};Mode=Read;Persist Security Info=False");
                c.Open();
                return c;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new Exception(L.T("manca il driver di Microsoft Access a 64 bit (Microsoft Access Database Engine)") + (last != null ? " — " + last.Message : ""));
#endif
    }

    static DbConnection MySqlOpen(string cs)
    {
        var c = new MySqlConnector.MySqlConnection(cs);
        c.Open();
        return c;
    }

    /// <summary>Nome di colonna ridotto: minuscolo, senza "col_", "fld", trattini e spazi (COL_GRIDSQUARE, fldGridR, qso_date...).</summary>
    static string Norm(string n)
    {
        n = n.ToLowerInvariant();
        if (n.StartsWith("col_")) n = n.Substring(4);
        if (n.StartsWith("fld")) n = n.Substring(3);
        return new string(n.Where(char.IsLetterOrDigit).ToArray());
    }

    static readonly string[] CallNames = { "call", "callsign", "dxcall" };
    static readonly string[] PreferredTables = { "log", "dxlog", "qsos", "tblcontacts", "cqrlogmain", "tablehrdcontactsv01", "contacts", "logbook" };

    sealed class Map
    {
        public int Call = -1, Band = -1, Freq = -1, Mode = -1, Sub = -1, Date = -1, Time = -1, Grid = -1, Vucc = -1, Prop = -1, Conf = -1, Lotw = -1, Qsl = -1;
        public bool BandIsId;
    }

    static int Find(Dictionary<string, int> cols, params string[] names)
    {
        foreach (var n in names) if (cols.TryGetValue(n, out var i)) return i;
        return -1;
    }

    static Map MapColumns(DbDataReader r)
    {
        var cols = new Dictionary<string, int>();
        for (int i = 0; i < r.FieldCount; i++) { var k = Norm(r.GetName(i)); if (!cols.ContainsKey(k)) cols[k] = i; }
        var m = new Map
        {
            Call = Find(cols, CallNames),
            Band = Find(cols, "band", "bandname"),
            Freq = Find(cols, "freq", "frequency", "qrg", "freqtx", "txfreq"),
            Mode = Find(cols, "mode", "modename"),
            Sub = Find(cols, "submode"),
            Date = Find(cols, "qsodate", "qsobegin", "qsostart", "starttime", "ts", "timeon", "datetime", "datestr", "date", "qsodatetime"),
            Time = Find(cols, "timeon", "timeonstr", "qsotime", "time"),
            Grid = Find(cols, "gridsquare", "dxgrid", "grid", "gridr", "loc", "locator", "qthloc"),
            Vucc = Find(cols, "vuccgrids", "vucc"),
            Prop = Find(cols, "propmode", "prop"),
            Conf = Find(cols, "qsoconfirmations"),
            Lotw = Find(cols, "lotwqslrcvd", "lotwqslr", "lotwrcvd", "lotwqslreceived", "lotwr"),
            Qsl = Find(cols, "qslrcvd", "qslr", "qslreceived", "qslrcvdpaper"),
        };
        if (m.Band < 0) { m.Band = Find(cols, "bandid"); m.BandIsId = m.Band >= 0; }
        if (m.Mode < 0) m.Mode = Find(cols, "modeid");
        if (m.Time == m.Date) m.Time = -1;
        return m;
    }

    void ReadDb(Source s, Func<DbConnection> open, string label)
    {
        using (var con = open())
        {
            var tables = ListTables(con);
            // tabelle delle bande e dei modi per i log che salvano solo un numero (KLog)
            var bandIds = IdTable(con, tables, "band");
            var modeIds = IdTable(con, tables, "mode");

            string Quote(string t) => con.GetType().Name.StartsWith("MySql") ? "`" + t.Replace("`", "") + "`" : con.GetType().Name.StartsWith("OleDb") ? "[" + t.Replace("]", "") + "]" : "\"" + t.Replace("\"", "") + "\"";

            // la tabella del log: quella con il nominativo e la banda o la frequenza, preferendo i nomi noti e poi la più lunga
            string best = null; long bestScore = -1;
            foreach (var t in tables)
            {
                try
                {
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM " + Quote(t) + " WHERE 1=0";
                        using (var r = cmd.ExecuteReader())
                        {
                            var m = MapColumns(r);
                            if (m.Call < 0 || (m.Band < 0 && m.Freq < 0)) continue;
                        }
                    }
                    long n = 0;
                    using (var cmd = con.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM " + Quote(t); n = Convert.ToInt64(cmd.ExecuteScalar()); }
                    var nt = Norm(t);
                    long score = n + (PreferredTables.Any(p => nt == p || nt.StartsWith("qsotable")) ? 1_000_000_000L : 0);
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                catch { }
            }
            if (best == null) { s.Qsos = new List<QsoRecord>(); s.Status = label + L.T(": tabella del log non trovata"); return; }

            var list = new List<QsoRecord>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM " + Quote(best);
                using (var r = cmd.ExecuteReader())
                {
                    var m = MapColumns(r);
                    string S(int i) { if (i < 0 || r.IsDBNull(i)) return ""; return Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? ""; }
                    while (r.Read())
                    {
                        var band = S(m.Band).Trim();
                        if (m.BandIsId && bandIds.TryGetValue(band, out var bn)) band = bn;
                        // banda scritta come numero di MHz (N1MM: 14, 50, 144)
                        if (double.TryParse(band.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var bmhz) && bmhz > 1 && !m.BandIsId) band = Bands.FromMHz(bmhz);
                        var q = new QsoRecord
                        {
                            Call = S(m.Call).Trim().ToUpperInvariant(),
                            Band = Bands.Normalize(band, MHz(S(m.Freq))),
                            Mode = S(m.Mode).Trim().ToUpperInvariant(),
                            PropMode = S(m.Prop).Trim().ToUpperInvariant(),
                            Grid = S(m.Grid).Trim().ToUpperInvariant(),
                            Lotw = Received(S(m.Lotw)),
                            Qsl = Received(S(m.Qsl)),
                        };
                        if (modeIds.Count > 0 && modeIds.TryGetValue(q.Mode, out var mn) && q.Mode.All(char.IsDigit)) q.Mode = mn.ToUpperInvariant();
                        var sub = S(m.Sub).Trim().ToUpperInvariant();
                        if (sub.Length > 0) q.Mode = sub;
                        ReadConfirmations(S(m.Conf), q);
                        var when = When(m.Date >= 0 ? r.GetValue(m.Date) : null, S(m.Time));
                        if (when != DateTime.MinValue) { q.Date = when.ToString("yyyyMMdd", CultureInfo.InvariantCulture); q.Time = when.ToString("HHmmss", CultureInfo.InvariantCulture); }
                        q.AddGrid(q.Grid);
                        foreach (var g in S(m.Vucc).Split(',', ' ', ';')) q.AddGrid(g);
                        if (q.Call != "") list.Add(q);
                    }
                }
            }
            s.Qsos = list;
            s.Status = $"{label}: {list.Count} QSO";
        }
    }

    static List<string> ListTables(DbConnection con)
    {
        var tables = new List<string>();
        var kind = con.GetType().Name;
        if (kind.StartsWith("OleDb"))
        {
            var schema = con.GetSchema("Tables");
            foreach (System.Data.DataRow row in schema.Rows)
            {
                var type = Convert.ToString(row["TABLE_TYPE"]) ?? "";
                var name = Convert.ToString(row["TABLE_NAME"]) ?? "";
                if (type.Equals("TABLE", StringComparison.OrdinalIgnoreCase) && name != "" && !name.StartsWith("MSys")) tables.Add(name);
            }
            return tables;
        }
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = kind.StartsWith("MySql") ? "SHOW TABLES" : "SELECT name FROM sqlite_master WHERE type='table'";
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    var name = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture) ?? "";
                    if (name != "" && !name.StartsWith("sqlite_")) tables.Add(name);
                }
        }
        return tables;
    }

    static Dictionary<string, string> IdTable(DbConnection con, List<string> tables, string name)
    {
        var map = new Dictionary<string, string>();
        var t = tables.FirstOrDefault(x => Norm(x) == name);
        if (t == null) return map;
        try
        {
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM " + t;
                using (var r = cmd.ExecuteReader())
                {
                    int id = -1, nm = -1;
                    for (int i = 0; i < r.FieldCount; i++) { var n = Norm(r.GetName(i)); if (n == "id") id = i; else if (n == "name" && nm < 0) nm = i; }
                    if (id < 0 || nm < 0) return map;
                    while (r.Read()) map[Convert.ToString(r.GetValue(id), CultureInfo.InvariantCulture) ?? ""] = Convert.ToString(r.GetValue(nm), CultureInfo.InvariantCulture) ?? "";
                }
            }
        }
        catch { }
        return map;
    }

    /// <summary>Frequenza in MHz da un valore che può essere in Hz, kHz o MHz.</summary>
    static string MHz(string v)
    {
        if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || f <= 0) return "";
        if (f > 1e6) f /= 1e6; else if (f > 1000) f /= 1000;
        return f.ToString(CultureInfo.InvariantCulture);
    }

    static bool Received(string v)
    {
        v = v.Trim().ToUpperInvariant();
        return v == "Y" || v == "YES" || v == "V" || v == "1" || v == "TRUE" || v == "L" || v == "Q" || v == "-1";
    }

    static DateTime When(object v, string time)
    {
        if (v == null || v is DBNull) return DateTime.MinValue;
        if (v is DateTime dt) return AddTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc), time);
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (s == "") return DateTime.MinValue;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) && !s.Contains("-"))
        {
            if (s.Length == 8 && num > 19000000) return AddTime(DateTime.ParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), time);
            if (num > 1e12) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(num);
            if (num > 1e8) return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(num);
            if (num > 20000 && num < 80000) return AddTime(new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Utc).AddDays(num), time); // data OLE
            return DateTime.MinValue;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) return AddTime(t, time);
        return DateTime.MinValue;
    }

    static DateTime AddTime(DateTime d, string time)
    {
        var t = new string((time ?? "").Where(char.IsDigit).ToArray());
        if (d.TimeOfDay != TimeSpan.Zero || t.Length < 4) return d;
        int h = int.Parse(t.Substring(0, 2)), m = int.Parse(t.Substring(2, 2)), sec = t.Length >= 6 ? int.Parse(t.Substring(4, 2)) : 0;
        return h < 24 && m < 60 && sec < 60 ? d.Date.AddHours(h).AddMinutes(m).AddSeconds(sec) : d;
    }

    /// <summary>Conferme di Log4OM 2 (colonna qsoconfirmations, JSON): LoTW o QSL se "R" = "Yes".</summary>
    public static void ReadConfirmations(string json, QsoRecord q)
    {
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '[') return;
        try
        {
            using (var doc = JsonDocument.Parse(json))
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    string ct = e.TryGetProperty("CT", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : "";
                    string rc = e.TryGetProperty("R", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "";
                    if (!string.Equals(rc, "Yes", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(ct, "LOTW", StringComparison.OrdinalIgnoreCase)) q.Lotw = true;
                    else if (string.Equals(ct, "QSL", StringComparison.OrdinalIgnoreCase)) q.Qsl = true;
                }
        }
        catch { }
    }

    // ---------------- log in rete ----------------

    /// <summary>Wavelog / Cloudlog: POST index.php/api/get_contacts_adif {key, station_id, fetchfromid} → JSON con l'ADIF.</summary>
    async Task Wavelog(Source s, HttpClient http, CancellationToken ct)
    {
        var a = Arg(s.Line).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 3) { s.Status = L.T("Wavelog: scrivi indirizzo, chiave API e numero della stazione"); return; }
        var url = a[0].TrimEnd('/');
        if (!url.Contains("/index.php")) url += "/index.php";
        var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["key"] = a[1], ["station_id"] = a[2], ["fetchfromid"] = 0 });
        using (var req = new HttpRequestMessage(HttpMethod.Post, url + "/api/get_contacts_adif") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
        using (var res = await http.SendAsync(req, ct))
        {
            var txt = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) { s.Status = "Wavelog: errore " + (int)res.StatusCode; return; }
            string adif = "";
            using (var doc = JsonDocument.Parse(txt))
                if (doc.RootElement.TryGetProperty("adif", out var ad)) adif = ad.GetString() ?? "";
            s.Qsos = Adif.Parse(Encoding.UTF8.GetBytes(adif)).Select(f => Adif.ToQso(f, false)).Where(x => x.Call != "").ToList();
            s.Fetched = DateTime.UtcNow;
            s.Status = $"Wavelog: {s.Qsos.Count} QSO";
        }
    }

    /// <summary>QRZ Logbook: POST https://logbook.qrz.com/api KEY, ACTION=FETCH, OPTION=MAX:250,AFTERLOGID:n (a pagine).</summary>
    async Task Qrz(Source s, HttpClient http, CancellationToken ct)
    {
        var key = Arg(s.Line);
        if (key == "") { s.Status = L.T("QRZ: manca la chiave API del logbook"); return; }
        var all = new List<QsoRecord>();
        long after = 0;
        for (int page = 0; page < 400; page++)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["KEY"] = key, ["ACTION"] = "FETCH", ["OPTION"] = "MAX:250,AFTERLOGID:" + after.ToString(CultureInfo.InvariantCulture) });
            using (var res = await http.PostAsync("https://logbook.qrz.com/api", form, ct))
            {
                var txt = await res.Content.ReadAsStringAsync();
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in txt.Split('&'))
                {
                    int e = part.IndexOf('=');
                    if (e > 0) kv[part.Substring(0, e)] = Uri.UnescapeDataString(part.Substring(e + 1).Replace('+', ' '));
                }
                kv.TryGetValue("RESULT", out var result);
                if (!string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    if (all.Count == 0) { s.Status = "QRZ: " + (kv.TryGetValue("REASON", out var why) ? why : L.T("risposta non valida")); return; }
                    break;
                }
                kv.TryGetValue("ADIF", out var adif);
                adif = System.Net.WebUtility.HtmlDecode(adif ?? "");
                var recs = Adif.Parse(Encoding.UTF8.GetBytes(adif));
                if (recs.Count == 0) break;
                foreach (var f in recs)
                {
                    var q = Adif.ToQso(f, false);
                    if (f.TryGetValue("APP_QRZLOG_STATUS", out var st) && st.Trim().ToUpperInvariant() == "C") q.Qsl = true;
                    if (q.Call != "") all.Add(q);
                    if (f.TryGetValue("APP_QRZLOG_LOGID", out var id) && long.TryParse(id, out var lid) && lid > after) after = lid;
                }
                if (recs.Count < 250) break;
            }
        }
        s.Qsos = all;
        s.Fetched = DateTime.UtcNow;
        s.Status = $"QRZ: {all.Count} QSO";
    }

    // ---------------- ricerca dei log sul computer ----------------

    /// <summary>File di log noti nelle cartelle abituali dei programmi (solo lettura).</summary>
    public static List<string> FindKnown()
    {
        var found = new List<string>();
        void Add(string p) { try { if (File.Exists(p) && !found.Contains(p)) found.Add(p); } catch { } }
        void AddAll(string dir, string pattern) { try { if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories)) Add(f); } catch { } }
        string local = "", roam = "", docs = "", home = "";
        try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
        try { roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } catch { }
        try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
        try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
        if (local != "")
        {
            Add(Path.Combine(local, "WSJT-X", "wsjtx_log.adi"));
            Add(Path.Combine(local, "JTDX", "wsjtx_log.adi"));
        }
        if (roam != "") { AddAll(Path.Combine(roam, "Log4OM2"), "*.SQLite"); AddAll(Path.Combine(roam, "HRDLLC"), "*.mdb"); }
        if (docs != "")
        {
            AddAll(Path.Combine(docs, "N1MM Logger+", "Databases"), "*.s3db");
            AddAll(Path.Combine(docs, "Log4OM2"), "*.SQLite");
            AddAll(Path.Combine(docs, "N3FJP Software", "ACLog"), "*.mdb");
        }
        foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            if (!string.IsNullOrEmpty(pf)) { AddAll(Path.Combine(pf, "DXLab Suite", "DXKeeper", "Databases"), "*.mdb"); AddAll(Path.Combine(pf, "MSHV"), "*.adi"); }
        if (home != "")
        {
            Add(Path.Combine(home, ".local", "share", "WSJT-X", "wsjtx_log.adi"));
            Add(Path.Combine(home, ".local", "share", "JTDX", "wsjtx_log.adi"));
            Add(Path.Combine(home, ".klog", "logbook.dat"));
            AddAll(Path.Combine(home, ".klog"), "*.db");
        }
        return found;
    }
}

/// <summary>Copia di un database di un altro programma (con -wal, -shm e -journal) nella cartella Dati: l'originale non si tocca.</summary>
public static class DbCopy
{
    public static string Make(string path, string folder, string name)
    {
        var dir = Path.Combine(Settings.DataDir, folder);
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, name);
        foreach (var suf in new[] { "", "-wal", "-shm", "-journal" })
        {
            var dst = copy + suf;
            try { if (File.Exists(dst)) File.Delete(dst); } catch { }
            var src = path + suf;
            if (!File.Exists(src)) continue;
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write))
                i.CopyTo(o);
        }
        return copy;
    }
}
