using System.Globalization;
using System.Text;

namespace YaGt.Core;

/// <summary>Un QSO letto da LoTW, da un file ADIF o dal log di QLog.</summary>
public sealed class QsoRecord
{
    public string Call = "";
    public string Band = "";
    public string Mode = "";
    public string PropMode = "";
    public string Date = "";   // AAAAMMGG
    public string Time = "";   // HHMM[SS]
    public string Grid = "";   // locatore come scritto nel log (4-10 caratteri)
    public List<string> Grids = new List<string>(); // quadrati a 4 caratteri
    public bool Lotw;   // confermato via LoTW
    public bool Qsl;    // confermato con QSL cartacea / altro

    public string Key => Call + "|" + Band + "|" + Date + "|" + (Time.Length >= 4 ? Time.Substring(0, 4) : Time);
    public string DateText => Date.Length == 8 ? Date.Substring(0, 4) + "-" + Date.Substring(4, 2) + "-" + Date.Substring(6, 2) : Date;

    public void AddGrid(string g)
    {
        var s = Locator.Square4(g);
        if (s != null && !Grids.Contains(s)) Grids.Add(s);
    }
}

/// <summary>Lettura ADIF (dal LoTW Grid Map v2 di IC8TEM).</summary>
public static class Adif
{
    public static List<Dictionary<string, string>> Parse(byte[] data)
    {
        var list = new List<Dictionary<string, string>>();
        var cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int n = data.Length, i = 0;
        bool inHeader = n > 0 && data[0] != (byte)'<';
        while (i < n)
        {
            if (data[i] != (byte)'<') { i++; continue; }
            int close = Array.IndexOf(data, (byte)'>', i + 1);
            if (close < 0) break;
            if (close - i - 1 > 200) { i++; continue; }
            string tag = Encoding.ASCII.GetString(data, i + 1, close - i - 1).Trim();
            i = close + 1;
            string lower = tag.ToLowerInvariant();
            if (lower == "eoh") { cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); inHeader = false; continue; }
            if (lower == "eor")
            {
                if (!inHeader && cur.Count > 0) list.Add(cur);
                cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            string[] parts = tag.Split(':');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[1].Trim(), out var len) || len < 0) continue;
            if (i + len > n) len = n - i;
            string val = Encoding.UTF8.GetString(data, i, len);
            i += len;
            cur[parts[0].Trim()] = val.Trim();
        }
        return list;
    }

    static string Get(Dictionary<string, string> f, string k) => f.TryGetValue(k, out var v) && v != null ? v.Trim() : "";
    static bool Yes(string v) { v = v.Trim().ToUpperInvariant(); return v == "Y" || v == "V"; }

    /// <summary>Converte un record ADIF in QSO. fromLotw = true per il report scaricato da LoTW.</summary>
    public static QsoRecord ToQso(Dictionary<string, string> f, bool fromLotw)
    {
        var q = new QsoRecord
        {
            Call = Get(f, "CALL").ToUpperInvariant(),
            Band = Bands.Normalize(Get(f, "BAND"), Get(f, "FREQ")),
            Mode = Get(f, "MODE").ToUpperInvariant(),
            PropMode = Get(f, "PROP_MODE").ToUpperInvariant(),
            Date = Get(f, "QSO_DATE"),
            Time = Get(f, "TIME_ON"),
            Grid = Get(f, "GRIDSQUARE").ToUpperInvariant(),
        };
        var sub = Get(f, "SUBMODE").ToUpperInvariant();
        if (sub.Length > 0) q.Mode = sub;
        q.AddGrid(Get(f, "GRIDSQUARE"));
        foreach (var g in Get(f, "VUCC_GRIDS").Split(',', ' ', ';')) q.AddGrid(g);
        if (fromLotw) q.Lotw = Yes(Get(f, "QSL_RCVD"));
        else { q.Lotw = Yes(Get(f, "LOTW_QSL_RCVD")); q.Qsl = Yes(Get(f, "QSL_RCVD")); }
        return q;
    }
}

/// <summary>Un quadrato Maidenhead lavorato, con lo stato di conferma (regole di LoTW Grid Map v2).</summary>
public sealed class SquareInfo
{
    public string Grid = "";
    public int Qsos;
    public bool Lotw, Qsl;
    public bool AllHf;   // confermato su ognuna delle bande 80-10 m (esclusi i 60 m)
}

/// <summary>
/// QSO di LoTW (report ADIF scaricato) e dei file ADIF del proprio log, uniti. Colori confermati da IC8TEM (v2):
/// arancione = lavorato, verde = confermato LoTW, blu = confermato solo con QSL, grigio = confermato su ognuna delle bande
/// 80, 40, 30, 20, 17, 15, 12 e 10 m (esclusi i 60 m), visibile in "Tutte le bande", in "HF 80-10 m" e nelle singole bande 80-10 m.
/// La password LoTW non viene mai salvata; tutto resta nella cartella Dati accanto al programma.
/// </summary>
public sealed class LotwLog
{
    public readonly object Lock = new object();
    public List<QsoRecord> Qsos = new List<QsoRecord>();
    public string Status = "";
    public long Version;
    public static string ReportPath => Path.Combine(Settings.DataDir, "lotw_report.adi");

    public static readonly uint ColWorked = 0x82FF8C00;
    public static readonly uint ColLotw = 0x8200AF00;
    public static readonly uint ColQsl = 0x821E5AFF;
    public static readonly uint ColAllHf = 0xA0646464;

    /// <summary>Gruppi selezionabili: ALL, G:1 (HF 80-10), G:2 (VHF), G:3 (UHF e oltre) o B:banda.</summary>
    public static bool Matches(QsoRecord q, string tag, bool onlyEme)
    {
        if (onlyEme && q.PropMode != "EME") return false;
        if (tag == "ALL") return true;
        if (tag.StartsWith("G:")) return Bands.GroupOf(q.Band).ToString(CultureInfo.InvariantCulture) == tag.Substring(2);
        if (tag.StartsWith("B:")) return q.Band == tag.Substring(2);
        return false;
    }

    /// <summary>Il grigio si vede in "Tutte le bande", in "HF 80-10 m" e nelle singole bande da 80 a 10 m.</summary>
    public static bool GreyTab(string tag) => tag == "ALL" || tag == "G:1" || tag.StartsWith("B:") && Bands.IsGreyBand(tag.Substring(2));

    public void LoadCached()
    {
        try { if (File.Exists(ReportPath)) Merge(File.ReadAllBytes(ReportPath), true, "LoTW (copia salvata)"); } catch { }
    }

    /// <summary>Unisce un ADIF (LoTW o log locale) ai QSO già presenti.</summary>
    public int Merge(byte[] data, bool fromLotw, string what)
    {
        var recs = Adif.Parse(data);
        int n = 0;
        lock (Lock)
        {
            var byKey = new Dictionary<string, QsoRecord>();
            foreach (var q in Qsos) byKey[q.Key] = q;
            foreach (var r in recs)
            {
                var q = Adif.ToQso(r, fromLotw);
                if (q.Call == "") continue;
                if (byKey.TryGetValue(q.Key, out var old))
                {
                    old.Lotw |= q.Lotw; old.Qsl |= q.Qsl;
                    foreach (var g in q.Grids) if (!old.Grids.Contains(g)) old.Grids.Add(g);
                    if (old.PropMode.Length == 0) old.PropMode = q.PropMode;
                    if (old.Grid.Length < q.Grid.Length) old.Grid = q.Grid;
                }
                else { byKey[q.Key] = q; Qsos.Add(q); }
                n++;
            }
            Version++;
        }
        Status = $"{what}: {n} QSO letti, {Qsos.Count} in totale";
        return n;
    }

    /// <summary>Scarica il report ADIF da LoTW (la password serve solo per la richiesta e non si salva).</summary>
    public async Task<string> DownloadAsync(HttpClient http, string user, string password, string ownCall, IProgress<string> progress, CancellationToken ct)
    {
        string url = "https://lotw.arrl.org/lotwuser/lotwreport.adi?login=" + Uri.EscapeDataString(user.Trim()) +
                     "&password=" + Uri.EscapeDataString(password) +
                     "&qso_query=1&qso_qsl=no&qso_qsldetail=yes&qso_mydetail=yes&qso_withown=yes";
        if (!string.IsNullOrWhiteSpace(ownCall)) url += "&qso_owncall=" + Uri.EscapeDataString(ownCall.Trim().ToUpperInvariant());
        progress?.Report("Collegamento a LoTW in corso…");
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        using (var s = await resp.Content.ReadAsStreamAsync())
        using (var ms = new MemoryStream())
        {
            var buf = new byte[65536];
            int r;
            var last = DateTime.MinValue;
            while ((r = await s.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                ms.Write(buf, 0, r);
                if ((DateTime.Now - last).TotalMilliseconds > 300) { last = DateTime.Now; progress?.Report($"Ricezione dati da LoTW: {ms.Length / 1024:N0} kB…"); }
            }
            var data = ms.ToArray();
            var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 8000));
            var tail = Encoding.ASCII.GetString(data, Math.Max(0, data.Length - 2000), Math.Min(data.Length, 2000));
            if (head.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase) < 0)
                return head.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || head.IndexOf("username", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "LoTW ha rifiutato l'accesso: controlla nome utente e password."
                    : "Risposta inattesa da LoTW (non è un file ADIF). Riprova più tardi.";
            if (tail.IndexOf("<app_lotw_eof>", StringComparison.OrdinalIgnoreCase) < 0 && tail.IndexOf("<eor>", StringComparison.OrdinalIgnoreCase) < 0 && tail.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase) < 0)
                return "Il file ricevuto da LoTW sembra incompleto. Riprova.";
            File.WriteAllBytes(ReportPath, data);
            Merge(data, true, "LoTW");
            return "";
        }
    }

    /// <summary>Quadrati confermati (LoTW o QSL) su ognuna delle bande 80, 40, 30, 20, 17, 15, 12 e 10 m.</summary>
    HashSet<string> AllHfConfirmed(bool onlyEme)
    {
        var perGrid = new Dictionary<string, HashSet<string>>();
        foreach (var q in Qsos)
        {
            if (!q.Lotw && !q.Qsl) continue;
            if (onlyEme && q.PropMode != "EME") continue;
            if (!Bands.IsGreyBand(q.Band)) continue;
            foreach (var g in q.Grids)
            {
                if (!perGrid.TryGetValue(g, out var set)) perGrid[g] = set = new HashSet<string>();
                set.Add(q.Band);
            }
        }
        var res = new HashSet<string>();
        foreach (var kv in perGrid) if (kv.Value.Count == Bands.GreyBands.Length) res.Add(kv.Key);
        return res;
    }

    public Dictionary<string, SquareInfo> BuildSquares(string tag, bool onlyConfirmed, bool onlyEme)
    {
        var d = new Dictionary<string, SquareInfo>();
        lock (Lock)
        {
            foreach (var q in Qsos)
            {
                if (!Matches(q, tag, onlyEme)) continue;
                if (onlyConfirmed && !q.Lotw && !q.Qsl) continue;
                foreach (var g in q.Grids)
                {
                    if (!d.TryGetValue(g, out var si)) d[g] = si = new SquareInfo { Grid = g };
                    si.Qsos++;
                    si.Lotw |= q.Lotw; si.Qsl |= q.Qsl;
                }
            }
            var grey = AllHfConfirmed(onlyEme);
            foreach (var si in d.Values) si.AllHf = grey.Contains(si.Grid);
        }
        return d;
    }

    /// <summary>Bande presenti nei dati (per le linguette/scelte di banda), in ordine.</summary>
    public List<string> BandsPresent()
    {
        lock (Lock) return Qsos.Select(q => q.Band).Distinct().OrderBy(Bands.Order).ToList();
    }

    public uint ColorOf(SquareInfo si, bool useGrey)
    {
        if (useGrey && si.AllHf) return ColAllHf;
        if (si.Lotw) return ColLotw;
        if (si.Qsl) return ColQsl;
        return ColWorked;
    }
}
