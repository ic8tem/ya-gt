using System.IO.Compression;
using System.Text;

namespace YaGt.Core;

/// <summary>
/// Fonti di CALL3.TXT, nello stesso ordine di MSHV-IC8TEM: CALL3.TXT di WSJT-X Improved, CALL3.TXT EME di W7GJ,
/// grid_data.bin di JTDX. Il CALL3.TXT del programma si riscrive unendo le fonti in quest'ordine (la prima che ha un
/// nominativo vince) e tenendo in fondo le voci del CALL3.TXT precedente che le fonti non hanno.
/// </summary>
public static class Call3Sources
{
    public static readonly (string Name, string Url, bool Binary)[] Sources =
    {
        ("WSJT-X Improved", "https://downloads.sourceforge.net/project/wsjt-x-improved/Additional%20Files/CALL3.TXT", false),
        ("W7GJ (EME)", "https://www.bigskyspaces.com/w7gj/CALL3.TXT", false),
        ("JTDX grid_data.bin", "https://downloads.sourceforge.net/project/jtdx/grid_data.bin", true),
    };

    static bool LooksLikeCall(string c) => c.Length >= 3 && c.Length <= 16 && c.Any(char.IsDigit) && c.Any(char.IsLetter) && c.All(ch => char.IsLetterOrDigit(ch) || ch == '/');

    /// <summary>CALL3.TXT: CALL,LOCATORE,… con righe di commento //.</summary>
    public static List<(string call, string grid)> ParseCall3(string text)
    {
        var r = new List<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("ZZZZ")) continue;
            var p = line.Split(',');
            if (p.Length < 2) continue;
            var call = p[0].Trim().ToUpperInvariant(); var grid = p[1].Trim().ToUpperInvariant();
            if (LooksLikeCall(call) && Locator.IsValid(grid)) r.Add((call, grid));
        }
        return r;
    }

    /// <summary>
    /// grid_data.bin di JTDX: qCompress (4 byte di lunghezza + zlib) di un QDataStream big-endian:
    /// quint32 righe, poi per ogni riga due QString (quint32 byte, UTF-16BE; 0xFFFFFFFF = nulla).
    /// </summary>
    public static List<(string call, string grid)> ParseGridData(byte[] raw)
    {
        var r = new List<(string, string)>();
        if (raw.Length <= 8 || raw[4] != 0x78) throw new InvalidDataException("non è un grid_data.bin");
        byte[] d;
        using (var ms = new MemoryStream(raw, 6, raw.Length - 6))        // salta lunghezza (4) e intestazione zlib (2)
        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
        using (var outp = new MemoryStream()) { ds.CopyTo(outp); d = outp.ToArray(); }
        int pos = 0;
        uint U32() { uint v = (uint)(d[pos] << 24 | d[pos + 1] << 16 | d[pos + 2] << 8 | d[pos + 3]); pos += 4; return v; }
        string Str() { uint n = U32(); if (n == 0xFFFFFFFF) return ""; var s = Encoding.BigEndianUnicode.GetString(d, pos, (int)n); pos += (int)n; return s; }
        uint rows = U32();
        if (rows == 0 || rows > 2_000_000) throw new InvalidDataException("numero di righe non valido");
        for (uint i = 0; i < rows && pos + 8 <= d.Length; i++)
        {
            var call = Str().Trim().ToUpperInvariant().Replace("<", "").Replace(">", ""); var grid = Str().Trim().ToUpperInvariant();
            if (LooksLikeCall(call) && Locator.IsValid(grid)) r.Add((call, grid));
        }
        return r;
    }

    /// <summary>
    /// Scarica le fonti e prepara le voci del nuovo CALL3.TXT (la prima fonte che ha un nominativo vince), tenendo le voci
    /// precedenti che le fonti non hanno e i locatori a 6 caratteri precedenti dove una fonte ne ha solo 4 dello stesso
    /// quadrato. Voci a mano e correzioni dal cluster le tiene CallDb, in cima al file.
    /// </summary>
    public static async Task<(List<string> header, Dictionary<string, string> body, string summary)> BuildAsync(HttpClient http, Dictionary<string, string> previous, Action<string> status)
    {
        var merged = new Dictionary<string, string>(1_100_000, StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var (name, url, bin) in Sources)
        {
            try
            {
                status?.Invoke($"Scarico {name}…");
                var bytes = await http.GetByteArrayAsync(url);
                var list = bin ? ParseGridData(bytes) : ParseCall3(Compat.Latin1.GetString(bytes));
                if (list.Count < 300) { parts.Add($"{name}: file non valido"); continue; }
                int added = 0;
                foreach (var (c, g) in list) if (!merged.ContainsKey(c)) { merged[c] = g; added++; }
                parts.Add($"{name}: {list.Count:N0} ({added:N0} nuovi)");
            }
            catch (Exception ex) { parts.Add($"{name}: non riuscito ({ex.Message})"); }
        }
        if (merged.Count < 50_000) throw new InvalidDataException("fonti non raggiungibili o troppo piccole: " + string.Join("; ", parts));
        int kept = 0, six = 0;
        foreach (var kv in previous ?? new Dictionary<string, string>())
        {
            if (!merged.TryGetValue(kv.Key, out var g)) { merged[kv.Key] = kv.Value; kept++; }
            else if (g.Length == 4 && kv.Value.Length == 6 && kv.Value.StartsWith(g)) { merged[kv.Key] = kv.Value; six++; }
        }
        parts.Add($"dal CALL3.TXT precedente: {kept:N0} (+{six:N0} a 6 caratteri)");
        var header = new List<string>
        {
            "// CALL3.TXT di YA-GT, riscritto il " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC",
            "// Fonti, in ordine di precedenza: voci a mano, correzioni dal cluster, WSJT-X Improved (DG2YCB), CALL3.TXT EME di W7GJ, grid_data.bin di JTDX; poi le voci del CALL3.TXT precedente e i locatori imparati",
            "// " + string.Join(" · ", parts),
            "// Contains " + merged.Count + " unique callsigns.",
            "//",
        };
        return (header, merged, string.Join(" · ", parts));
    }
}
