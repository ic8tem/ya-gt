using Microsoft.Data.Sqlite;

namespace YaGt.Core;

/// <summary>
/// Lettura del log di QLog (OK1MLG, github.com/foldynl/QLog): database SQLite qlog.db, tabella contacts (callsign,
/// gridsquare, band, mode, start_time, qsl_rcvd, lotw_qsl_rcvd…). Su Windows QLog lo tiene in
/// %LOCALAPPDATA%\hamradio\QLog\qlog.db (QStandardPaths::AppLocalDataLocation). Il file non si tocca: si copia (con i file
/// -wal e -shm, se QLog è aperto) nella cartella Dati e si legge la copia in sola lettura.
/// </summary>
public sealed class QLogDb
{
    public List<QsoRecord> Qsos = new List<QsoRecord>();
    public string Status = "";
    public DateTime Loaded = DateTime.MinValue;
    public long Version;

    public static string DefaultPath()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var p = Path.Combine(local, "hamradio", "QLog", "qlog.db");
            return p;
        }
        catch { return ""; }
    }

    public void Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = DefaultPath();
        if (!File.Exists(path)) { Status = "QLog: database non trovato (" + path + ")"; return; }
        try
        {
            var dir = Path.Combine(Settings.DataDir, "qlog-copia");
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, "qlog.db");
            foreach (var suf in new[] { "", "-wal", "-shm" })
            {
                var dst = copy + suf;
                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                var src = path + suf;
                if (!File.Exists(src)) continue;
                using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write))
                    i.CopyTo(o);
            }
            var list = new List<QsoRecord>();
            var cs = new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
            using (var con = new SqliteConnection(cs))
            {
                con.Open();
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM contacts";
                    using (var r = cmd.ExecuteReader())
                    {
                        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < r.FieldCount; i++) cols[r.GetName(i)] = i;
                        string S(string name) { if (!cols.TryGetValue(name, out var i) || r.IsDBNull(i)) return ""; return Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? ""; }
                        while (r.Read())
                        {
                            var q = new QsoRecord
                            {
                                Call = S("callsign").Trim().ToUpperInvariant(),
                                Band = Bands.Normalize(S("band"), S("freq")),
                                Mode = S("mode").Trim().ToUpperInvariant(),
                                PropMode = S("prop_mode").Trim().ToUpperInvariant(),
                                Grid = S("gridsquare").Trim().ToUpperInvariant(),
                                Lotw = S("lotw_qsl_rcvd").Trim().ToUpperInvariant() == "Y",
                                Qsl = S("qsl_rcvd").Trim().ToUpperInvariant() == "Y",
                            };
                            var sub = S("submode").Trim().ToUpperInvariant();
                            if (sub.Length > 0) q.Mode = sub;
                            var st = S("start_time");
                            if (DateTime.TryParse(st, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
                            { q.Date = t.ToString("yyyyMMdd"); q.Time = t.ToString("HHmmss"); }
                            q.AddGrid(q.Grid);
                            foreach (var g in S("vucc_grids").Split(',', ' ', ';')) q.AddGrid(g);
                            if (q.Call != "") list.Add(q);
                        }
                    }
                }
            }
            Qsos = list;
            Loaded = DateTime.UtcNow;
            Version++;
            Status = $"QLog: {list.Count} QSO";
        }
        catch (Exception ex) { Status = "QLog: " + ex.Message; }
    }
}
