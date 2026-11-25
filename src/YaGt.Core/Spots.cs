using System.Globalization;
using System.Text.RegularExpressions;

namespace YaGt.Core;

/// <summary>Da quale parte sta il nominativo dell'utente: lui viene sentito, oppure è lui che sente.</summary>
public enum SpotDir { HeardMe, IHeard }

/// <summary>Un rapporto di ricezione che riguarda il nominativo dell'utente.</summary>
public sealed class SpotItem
{
    public DateTime Time;          // UTC
    public string Source = "";     // PSK, RBN, DM4X, CL2
    public SpotDir Dir;
    public string Tx = "", Rx = "";
    public string TxGrid = "", RxGrid = "";
    public int? Snr;
    public double FreqKHz;
    public string Band = "";
    public string Mode = "";
    /// <summary>La stazione all'altro capo (chi mi sente, o chi sento io).</summary>
    public string Other => Dir == SpotDir.HeardMe ? Rx : Tx;
    public string OtherGrid => Dir == SpotDir.HeardMe ? RxGrid : TxGrid;
    // posizione risolta (CALL3.TXT, locatore ricevuto o cty.dat)
    public LatLon Pos;
    public bool HasPos;
    public bool PosApprox;         // solo il paese (cty.dat)
    public string PosSource = "";  // spot, call3, cty
    public double Km, Az;
}

/// <summary>Spot in memoria: per ogni direzione, stazione e banda si tiene l'ultimo (con il miglior SNR del minuto).</summary>
public sealed class SpotStore
{
    readonly object _lock = new object();
    readonly Dictionary<string, SpotItem> _map = new Dictionary<string, SpotItem>(StringComparer.Ordinal);
    public long Version;

    public void Add(SpotItem s)
    {
        if (string.IsNullOrEmpty(s.Other)) return;
        var key = (s.Dir == SpotDir.HeardMe ? "M|" : "I|") + s.Other + "|" + s.Band + "|" + s.Source;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var o) && o.Time > s.Time) return;
            if (s.OtherGrid == "" && o != null && o.OtherGrid != "")
            {
                if (s.Dir == SpotDir.HeardMe) s.RxGrid = o.RxGrid; else s.TxGrid = o.TxGrid;
            }
            _map[key] = s;
            Version++;
        }
    }

    public List<SpotItem> Snapshot(DateTime nowUtc, int maxAgeMin)
    {
        var cut = nowUtc.AddMinutes(-maxAgeMin);
        lock (_lock) return _map.Values.Where(s => s.Time >= cut).ToList();
    }

    public void Prune(DateTime nowUtc, int maxAgeMin)
    {
        var cut = nowUtc.AddMinutes(-maxAgeMin);
        lock (_lock)
            foreach (var k in _map.Where(kv => kv.Value.Time < cut).Select(kv => kv.Key).ToList()) _map.Remove(k);
    }

    public int Count { get { lock (_lock) return _map.Count; } }
    public void Clear() { lock (_lock) { _map.Clear(); Version++; } }
}

/// <summary>Righe "DX de" dei cluster telnet (RBN, DM4X, DXMAPS) che riguardano il nominativo dell'utente.</summary>
public static class ClusterParser
{
    static readonly Regex ReSpot = new Regex(@"^DX de\s+([^:\s]+):?\s+([0-9]+(?:\.[0-9]+)?)\s+(\S+)\s+(.*?)\s*([0-9]{4})Z", RegexOptions.Compiled);
    static readonly Regex ReDb = new Regex(@"([-+]?[0-9]{1,3})\s*dB", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ReLoc = new Regex(@"\b([A-R]{2}[0-9]{2}(?:[A-X]{2})?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ReSuffix = new Regex(@"-(#|[0-9]{1,2})$", RegexOptions.Compiled);
    static readonly string[] Modes = { "FT8", "FT4", "CW", "RTTY", "PSK31", "PSK63", "JT65", "JT9", "Q65", "MSK144", "FST4", "FST4W", "WSPR", "SSB", "USB", "LSB", "FM", "AM", "BPSK", "JS8" };

    public static string CleanCall(string c)
    {
        c = (c ?? "").Trim().ToUpperInvariant().TrimEnd(':');
        return ReSuffix.Replace(c, "");
    }

    /// <summary>Nominativo base di un ricevitore automatico: toglie -SDR, /SDR, -# e -1…-99 (skimmer RBN, ricevitori PSKReporter).</summary>
    public static string SkimmerBase(string c)
    {
        c = (c ?? "").Trim().ToUpperInvariant();
        foreach (var suf in new[] { "-SDR", "/SDR", "-#", "/#" })
            if (c.EndsWith(suf)) c = c.Substring(0, c.Length - suf.Length);
        return ReSuffix.Replace(c, "");
    }

    /// <summary>
    /// Legge una riga di cluster. Restituisce lo spot solo se riguarda myCall: come DX (mi hanno sentito) o come chi spotta
    /// (ho sentito io, con il proprio skimmer). Il primo locatore del commento è quello di chi spotta (stile DM4X "KN09tp&lt;&gt;JN70").
    /// </summary>
    public static SpotItem Parse(string line, string source, string myCall, DateTime nowUtc)
    {
        var m = ReSpot.Match((line ?? "").Trim());
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double f)) return null;
        var spotter = CleanCall(m.Groups[1].Value);
        var dx = CleanCall(m.Groups[3].Value);
        var me = (myCall ?? "").ToUpperInvariant();
        bool heardMe = dx == me;
        bool iHeard = !heardMe && SkimmerBase(spotter) == me;
        if (!heardMe && !iHeard) return null;
        var comment = m.Groups[4].Value.Trim();
        var s = new SpotItem
        {
            Time = nowUtc,
            Source = source,
            Dir = heardMe ? SpotDir.HeardMe : SpotDir.IHeard,
            Tx = dx,
            Rx = spotter,
            FreqKHz = f,
            Band = Bands.FromKHz(f),
        };
        var dbm = ReDb.Match(comment);
        if (dbm.Success && int.TryParse(dbm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snr)) s.Snr = snr;
        var up = comment.ToUpperInvariant();
        foreach (var tok in up.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Modes.Contains(tok)) { s.Mode = tok == "USB" || tok == "LSB" ? "SSB" : tok; break; }
        }
        var locs = new List<string>();
        foreach (Match lm in ReLoc.Matches(comment))
        {
            var lu = lm.Value.ToUpperInvariant();
            if (lu == s.Mode) continue;
            if (Locator.IsValid(lu)) locs.Add(lu);
        }
        // DM4X: "KN09tp<>JN70" = locatore di chi spotta <> locatore del DX
        if (locs.Count >= 2) { s.RxGrid = locs[0]; s.TxGrid = locs[1]; }
        else if (locs.Count == 1 && source == "DM4X") s.RxGrid = locs[0];
        return s;
    }
}

/// <summary>Scala dei colori dell'SNR (rosso = forte, blu = debole). RBN in CW/RTTY dà dB sopra il rumore, gli altri dB in 2500 Hz.</summary>
public static class SnrScale
{
    public static readonly uint Unknown = 0xFF9AA3AE;

    static readonly uint[] Cols = { 0xFF3B82C4, 0xFF27A08A, 0xFF5BC07A, 0xFFE8D24A, 0xFFF08A3C, 0xFFD62839 };

    public static uint Color(int? snr, string source, string mode)
    {
        if (!snr.HasValue) return Unknown;
        int v = snr.Value;
        bool aboveNoise = source == "RBN" && (mode == "CW" || mode == "RTTY" || mode == "");
        int i;
        if (aboveNoise) i = v >= 30 ? 5 : v >= 22 ? 4 : v >= 15 ? 3 : v >= 9 ? 2 : v >= 4 ? 1 : 0;
        else i = v >= 0 ? 5 : v >= -6 ? 4 : v >= -12 ? 3 : v >= -17 ? 2 : v >= -21 ? 1 : 0;
        return Cols[i];
    }

    public static string Text(int? snr) => snr.HasValue ? (snr.Value > 0 ? "+" + snr.Value : snr.Value.ToString(CultureInfo.InvariantCulture)) : "?";
}
