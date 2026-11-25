using System.Globalization;
using System.Text.RegularExpressions;

namespace YaGt.Core;

/// <summary>
/// cty.dat (AD1C, country-files.com): dal prefisso del nominativo al paese DXCC e alla sua posizione indicativa.
/// Serve solo quando il nominativo non è in CALL3.TXT: la posizione è approssimata (centro del paese o del prefisso).
/// </summary>
public sealed class Cty
{
    public sealed class Entity
    {
        public string Name = "";
        public string Prefix = "";
        public double Lat, Lon;
    }

    readonly Dictionary<string, (Entity e, double lat, double lon)> _prefix = new Dictionary<string, (Entity, double, double)>(StringComparer.Ordinal);
    readonly Dictionary<string, (Entity e, double lat, double lon)> _exact = new Dictionary<string, (Entity, double, double)>(StringComparer.Ordinal);
    int _maxLen;
    public bool Loaded { get; private set; }

    static readonly Regex ReLatLon = new Regex(@"<([-0-9.]+)/([-0-9.]+)>", RegexOptions.Compiled);
    static readonly Regex ReMods = new Regex(@"\([0-9]+\)|\[[0-9]+\]|<[^>]*>|\{[^}]*\}|~[^~]*~", RegexOptions.Compiled);

    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        Entity cur = null;
        var text = File.ReadAllText(path, Compat.Latin1);
        var pending = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line[0] != ' ' && line[0] != '\t' && line.Contains(':'))
            {
                var p = line.Split(':');
                if (p.Length >= 8)
                {
                    cur = new Entity { Name = p[0].Trim(), Prefix = p[7].Trim().TrimStart('*') };
                    double.TryParse(p[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out cur.Lat);
                    double.TryParse(p[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w);
                    cur.Lon = -w;   // in cty.dat la longitudine è positiva verso ovest
                    pending = "";
                }
                continue;
            }
            if (cur == null) continue;
            pending += line.Trim();
            if (!pending.EndsWith(";")) continue;
            foreach (var tok0 in pending.TrimEnd(';').Split(','))
            {
                var tok = tok0.Trim();
                if (tok.Length == 0) continue;
                double lat = cur.Lat, lon = cur.Lon;
                var m = ReLatLon.Match(tok);
                if (m.Success)
                {
                    double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat);
                    double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w2);
                    lon = -w2;
                }
                var key = ReMods.Replace(tok, "").Trim().ToUpperInvariant();
                if (key.Length == 0) continue;
                if (key[0] == '=') _exact[key.Substring(1)] = (cur, lat, lon);
                else { _prefix[key] = (cur, lat, lon); if (key.Length > _maxLen) _maxLen = key.Length; }
            }
            pending = "";
        }
        Loaded = _prefix.Count > 0;
    }

    /// <summary>Posizione indicativa e nome del paese, o false se il prefisso non è riconosciuto.</summary>
    public bool TryLookup(string call, out LatLon pos, out string country)
    {
        pos = default(LatLon); country = "";
        if (string.IsNullOrEmpty(call) || !Loaded) return false;
        var c = call.ToUpperInvariant();
        if (_exact.TryGetValue(c, out var ex)) { pos = new LatLon(ex.lat, ex.lon); country = ex.e.Name; return true; }
        // portatili: F/IC8TEM → prefisso F; IC8TEM/P → IC8TEM
        var parts = c.Split('/');
        string basis = c;
        if (parts.Length >= 2)
        {
            var a = parts[0]; var b = parts[1];
            if (b == "P" || b == "M" || b == "MM" || b == "AM" || b == "QRP" || b.Length == 1 && char.IsDigit(b[0])) basis = a;
            else basis = a.Length <= b.Length ? a : b;
        }
        for (int len = Math.Min(_maxLen, basis.Length); len >= 1; len--)
        {
            if (_prefix.TryGetValue(basis.Substring(0, len), out var hit))
            {
                pos = new LatLon(hit.lat, hit.lon); country = hit.e.Name; return true;
            }
        }
        return false;
    }
}
