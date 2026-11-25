using System.Globalization;
using System.Text;

namespace YaGt.Core;

/// <summary>Impostazioni in YA-GT.ini accanto all'eseguibile (mai AppData, mai registro).</summary>
public sealed class Settings
{
    readonly string _path;
    readonly Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new object();

    public static string AppDir
    {
        get
        {
            var p = Compat.ProcessPath;
            if (!string.IsNullOrEmpty(p))
            {
                var d = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>Cartella "Dati" accanto al programma (report LoTW, copie dei dati scaricati).</summary>
    public static string DataDir
    {
        get
        {
            var d = Path.Combine(AppDir, "Dati");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    public Settings() : this(AppDir) { }

    public Settings(string dir)
    {
        _path = Path.Combine(dir, "YA-GT.ini");
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var raw in File.ReadAllLines(_path))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                int i = l.IndexOf('=');
                if (i > 0) _kv[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
        }
        catch { }
    }

    public string Get(string k, string def) { lock (_lock) return _kv.TryGetValue(k, out var v) ? v : def; }
    public int GetInt(string k, int def) => int.TryParse(Get(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    public double GetDouble(string k, double def) => double.TryParse(Get(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    public bool GetBool(string k, bool def)
    {
        var v = Get(k, "");
        if (v == "") return def;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
    public void Set(string k, string v) { lock (_lock) _kv[k] = v ?? ""; }
    public void SetInt(string k, int v) => Set(k, v.ToString(CultureInfo.InvariantCulture));
    public void SetDouble(string k, double v) => Set(k, v.ToString("R", CultureInfo.InvariantCulture));
    public void SetBool(string k, bool v) => Set(k, v ? "1" : "0");

    public void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("; YA-GT - impostazioni (file portabile accanto al programma). La password LoTW non viene mai salvata.\r\n");
            lock (_lock)
                foreach (var k in _kv.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append(k).Append('=').Append(_kv[k]).Append("\r\n");
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}

/// <summary>Registro degli errori accanto al programma (errore-avvio.txt per gli errori fatali, usato anche dalla prova di avvio su GitHub).</summary>
public static class ErrorLog
{
    public static void Fatal(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Settings.AppDir, "errore-avvio.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n"); } catch { }
    }

    public static void Write(string what, Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Settings.DataDir, "errori.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + what + ": " + ex.Message + "\r\n"); } catch { }
    }
}
