using System.Globalization;
using System.Text;

namespace YaGt.Core;

/// <summary>
/// Database nominativi → locatore: un solo file, CALL3.TXT (formato di JTDX/WSJT-X, CALL,LOCATORE,,,), che resta
/// leggibile da WSJT-X, JTDX e MSHV. Dalla 1.3.4 non ci sono più file a parte: voci a mano, correzioni dal cluster,
/// file importati, locatori imparati dal vivo (ex LOC6.TXT) e risposte di G7VRD stanno tutti dentro CALL3.TXT.
///
/// In cima al file, dopo l'intestazione, due blocchi segnati da una riga di commento:
///   // [LIVETROPOVHF-MANUALI]  voci a mano: valgono sopra tutto e restano agli aggiornamenti;
///   // [LIVETROPOVHF-CLUSTER]  locatore a 6 caratteri dichiarato dalla stazione negli spot automatici (DM4X, RBN),
///                              con la data dell'ultima conferma nel terzo campo (come le date di JTDX);
///   // [LIVETROPOVHF-FONTI]    tutte le altre voci, in ordine alfabetico.
/// Ogni nominativo compare una volta sola. Precedenza: voci a mano &gt; correzioni dal cluster &gt; fonti.
/// </summary>
public sealed class CallDb
{
    public const string MarkManual = "// [LIVETROPOVHF-MANUALI]";
    public const string MarkCluster = "// [LIVETROPOVHF-CLUSTER]";
    public const string MarkBody = "// [LIVETROPOVHF-FONTI]";

    readonly object _lock = new();
    readonly object _saveLock = new();
    Dictionary<string, string> _map = new(StringComparer.Ordinal);                                   // fonti
    readonly Dictionary<string, string> _manual = new(StringComparer.Ordinal);                       // voci a mano
    readonly Dictionary<string, (string loc, DateTime when)> _cluster = new(StringComparer.Ordinal); // dal cluster
    List<string> _header = new();
    int _dirty;
    bool _writable;            // false finché CALL3.TXT non è caricato per intero: mai riscriverlo mezzo vuoto
    DateTime _lastSave = DateTime.UtcNow;

    public bool Ready { get; private set; }
    /// <summary>File effettivamente caricato ("" se non trovato).</summary>
    public string PathUsed { get; private set; } = "";
    public string Error { get; private set; } = "";
    /// <summary>Dove si scrive: il CALL3.TXT caricato o, se non c'era, quello accanto al programma.</summary>
    public string TargetPath => PathUsed != "" ? PathUsed : Path.Combine(Settings.AppDir, "CALL3.TXT");

    public int Count { get { lock (_lock) return _map.Count + _manual.Count + _cluster.Count; } }
    public int ManualCount { get { lock (_lock) return _manual.Count; } }
    public int ClusterCount { get { lock (_lock) return _cluster.Count; } }

    /// <summary>Carica il primo CALL3.TXT trovato tra i percorsi indicati.</summary>
    public void Load(IEnumerable<string> candidates)
    {
        var list = candidates.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        var found = list.FirstOrDefault(File.Exists);
        if (found == null)
        {
            Error = "CALL3.TXT non trovato (cercato in: " + string.Join("; ", list.Select(Path.GetDirectoryName).Distinct()) + ")";
            _writable = true;       // si crea accanto al programma con le voci a mano; il primo aggiornamento lo riempie
            Ready = true;
            return;
        }
        PathUsed = found;
        Load(found);
    }

    static bool IsMark(string line) => line.StartsWith(MarkManual) || line.StartsWith(MarkCluster) || line.StartsWith(MarkBody);

    static DateTime ParseDate(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length >= 10 && DateTime.TryParseExact(s.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) return d;
        return DateTime.UtcNow;
    }

    public void Load(string path)
    {
        var map = new Dictionary<string, string>(1_100_000, StringComparer.Ordinal);
        var man = new Dictionary<string, string>(StringComparer.Ordinal);
        var clu = new Dictionary<string, (string, DateTime)>(StringComparer.Ordinal);
        var header = new List<string>();
        bool expired = false, ok = false;
        try
        {
            if (!File.Exists(path)) Error = "CALL3.TXT non trovato";
            else
            {
                int section = 0;            // 0 = fonti, 1 = a mano, 2 = cluster
                bool inHeader = true;
                foreach (var raw in File.ReadLines(path, Compat.Latin1))
                {
                    if (raw.Length == 0) continue;
                    if (raw.StartsWith("//"))
                    {
                        if (raw.StartsWith(MarkManual)) { section = 1; inHeader = false; }
                        else if (raw.StartsWith(MarkCluster)) { section = 2; inHeader = false; }
                        else if (raw.StartsWith(MarkBody)) { section = 0; inHeader = false; }
                        else if (inHeader) header.Add(raw);
                        continue;
                    }
                    inHeader = false;
                    var p = raw.Split(',');
                    if (p.Length < 2) continue;
                    var call = p[0].Trim().ToUpperInvariant();
                    var loc = p[1].Trim().ToUpperInvariant();
                    if (call.Length < 3 || !Locator.IsValid(loc)) continue;
                    if (section == 1) { man[call] = loc; continue; }
                    if (man.ContainsKey(call)) continue;
                    if (section == 2)
                    {
                        var when = ParseDate(p.Length > 2 ? p[2] : "");
                        // non confermato da 2 anni: resta come voce normale, senza precedenza
                        if (DateTime.UtcNow - when > TimeSpan.FromDays(730)) { map[call] = loc; expired = true; }
                        else clu[call] = (loc, when);
                        continue;
                    }
                    if (!clu.ContainsKey(call)) map[call] = loc;
                }
                Error = "";
                ok = true;
            }
        }
        catch (Exception ex) { Error = "CALL3.TXT: " + ex.Message; }
        if (!ok) { _writable = false; Ready = true; return; }    // il database in memoria resta quello di prima
        lock (_lock)
        {
            _map = map;
            _manual.Clear(); foreach (var kv in man) _manual[kv.Key] = kv.Value;
            _cluster.Clear(); foreach (var kv in clu) _cluster[kv.Key] = kv.Value;
            _header = header;
            _dirty = expired ? 1 : 0;
            _writable = true;
        }
        Ready = true;
    }

    /// <summary>Ricarica CALL3.TXT dal disco (dopo averlo modificato a mano con un editor).</summary>
    public void Reload() { lock (_saveLock) if (File.Exists(TargetPath)) { PathUsed = TargetPath; Load(TargetPath); } }

    /// <summary>
    /// Cerca solo il nominativo esatto. Prefissi e suffissi di portatile (F/IC8TEM, IC8TEM/P) non si riconducono al
    /// nominativo base: la stazione potrebbe essere altrove, quindi senza voce esatta non ha locatore.
    /// </summary>
    public string Lookup(string call)
    {
        if (string.IsNullOrEmpty(call)) return "";
        call = call.ToUpperInvariant();
        lock (_lock)
        {
            if (_manual.TryGetValue(call, out var m)) return m;
            if (_cluster.TryGetValue(call, out var k)) return k.loc;
            return _map.TryGetValue(call, out var l) ? l : "";
        }
    }

    /// <summary>Da dove viene il locatore: "manual", "cluster", "call3" o "".</summary>
    public string SourceOf(string call)
    {
        if (string.IsNullOrEmpty(call)) return "";
        call = call.ToUpperInvariant();
        lock (_lock) return _manual.ContainsKey(call) ? "manual" : _cluster.ContainsKey(call) ? "cluster" : _map.ContainsKey(call) ? "call3" : "";
    }

    // ------------------------------------------------------------ voci a mano

    public List<(string call, string loc)> Manual() { lock (_lock) return _manual.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(); }
    public string ManualOf(string call) { if (string.IsNullOrEmpty(call)) return ""; lock (_lock) return _manual.TryGetValue(call.ToUpperInvariant(), out var l) ? l : ""; }

    void PutManual(string call, string loc)
    {
        _manual[call] = loc; _map.Remove(call); _cluster.Remove(call);
    }

    /// <summary>
    /// Aggiunge o cambia una voce a mano e riscrive CALL3.TXT. Locatore vuoto = toglie il nominativo da CALL3.TXT
    /// (il prossimo aggiornamento lo riprende dalle fonti, se ce l'hanno).
    /// </summary>
    public bool SetManual(string call, string loc)
    {
        call = (call ?? "").Trim().ToUpperInvariant();
        loc = (loc ?? "").Trim().ToUpperInvariant();
        if (call.Length < 3 || loc != "" && !Locator.IsValid(loc)) return false;
        lock (_lock)
        {
            if (loc == "") { _manual.Remove(call); _cluster.Remove(call); _map.Remove(call); }
            else PutManual(call, loc);
            _dirty = 1;
        }
        _ = Task.Run(() => Save(true));     // ~25 MB: non si blocca la finestra
        return true;
    }

    /// <summary>Legge un file CALL,LOCATORE (separatori , ; tabulazione o spazio).</summary>
    public static IEnumerable<(string call, string loc)> ReadFile(string path)
    {
        foreach (var raw in File.ReadLines(path, Compat.Latin1))
        {
            if (raw.Length == 0 || raw.StartsWith("//")) continue;
            var p = raw.Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) continue;
            var call = p[0].Trim().ToUpperInvariant();
            var loc = p[1].Trim().ToUpperInvariant();
            if (call.Length >= 3 && Locator.IsValid(loc)) yield return (call, loc);
        }
    }

    /// <summary>Unisce un elenco CALL,LOCATORE alle voci a mano di CALL3.TXT. Restituisce il numero di voci lette.</summary>
    public int Import(string path)
    {
        var items = ReadFile(path).ToList();
        lock (_lock) { foreach (var (c, l) in items) PutManual(c, l); _dirty = 1; }
        _ = Task.Run(() => Save(true));
        return items.Count;
    }

    // ------------------------------------------------ correzioni dal cluster

    /// <summary>
    /// Locatore a 6 caratteri dichiarato dalla stazione stessa (spot automatici DM4X/RBN: "KN09tp&lt;&gt;KN09", il primo è
    /// quello di chi spotta). Se è diverso da quello del database lo sostituisce. Restituisce il locatore precedente
    /// ("" se non c'era) quando c'è stato un cambio, altrimenti null.
    /// </summary>
    public string SetCluster(string call, string loc)
    {
        call = (call ?? "").Trim().ToUpperInvariant(); loc = (loc ?? "").Trim().ToUpperInvariant();
        if (call.Length < 3 || loc.Length != 6 || !Locator.IsValid(loc)) return null;
        lock (_lock)
        {
            if (_manual.ContainsKey(call)) return null;                     // la voce a mano vince sempre
            if (_cluster.TryGetValue(call, out var c) && c.loc == loc)
            {
                if (DateTime.UtcNow - c.when > TimeSpan.FromDays(1)) { _cluster[call] = (loc, DateTime.UtcNow); _dirty = 1; }
                return null;
            }
            var before = _cluster.TryGetValue(call, out var c0) ? c0.loc : _map.TryGetValue(call, out var m) ? m : "";
            if (before == loc) return null;
            _cluster[call] = (loc, DateTime.UtcNow);
            _map.Remove(call);
            _dirty = 1;
            return before;
        }
    }

    // ------------------------------------------------ locatori imparati (dal vivo, G7VRD)

    /// <summary>
    /// Locatore a 6 caratteri imparato dal vivo (PSKReporter, cluster, UDP) o da G7VRD: entra in CALL3.TXT solo se il
    /// nominativo non c'è o ha 4 caratteri dello stesso quadrato. Non cambia mai una voce a 6 caratteri, a mano o dal
    /// cluster. Restituisce true se il database è cambiato.
    /// </summary>
    public bool Learn(string call, string loc)
    {
        call = (call ?? "").Trim().ToUpperInvariant().Trim('<', '>');
        loc = (loc ?? "").Trim().ToUpperInvariant();
        if (loc.Length > 6) loc = loc.Substring(0, 6);
        if (call.Length < 3 || loc.Length != 6 || !Locator.IsValid(loc)) return false;
        lock (_lock)
        {
            if (_manual.ContainsKey(call) || _cluster.ContainsKey(call)) return false;
            if (_map.TryGetValue(call, out var cur) && !(cur.Length == 4 && loc.StartsWith(cur))) return false;
            _map[call] = loc;
            _dirty = 1;
            return true;
        }
    }

    // ------------------------------------------------ aggiornamento dalle fonti

    /// <summary>Copia delle voci delle fonti (senza voci a mano né correzioni), per l'aggiornamento.</summary>
    public Dictionary<string, string> BodySnapshot() { lock (_lock) return new Dictionary<string, string>(_map, StringComparer.Ordinal); }

    /// <summary>Sostituisce le voci delle fonti e l'intestazione; voci a mano e correzioni dal cluster restano.</summary>
    public void ReplaceBody(List<string> header, Dictionary<string, string> body)
    {
        lock (_lock)
        {
            foreach (var c in _manual.Keys.Concat(_cluster.Keys).ToList()) body.Remove(c);
            _map = body;
            _header = header;
            _dirty = 1;
        }
    }

    // ------------------------------------------------ scrittura

    /// <summary>
    /// Riscrive CALL3.TXT se qualcosa è cambiato. Senza force al massimo ogni 10 minuti (il file è di ~25 MB).
    /// Il CALL3.TXT dello zip è in sola lettura: l'attributo si toglie.
    /// </summary>
    public bool Save(bool force = false)
    {
        lock (_saveLock)
        {
            if (!Ready || !_writable) return false;
            if (Volatile.Read(ref _dirty) == 0) return true;
            if (!force && DateTime.UtcNow - _lastSave < TimeSpan.FromMinutes(10)) return true;
            var sb = new StringBuilder(32_000_000);
            lock (_lock)
            {
                int total = _map.Count + _manual.Count + _cluster.Count;
                var header = _header.Count > 0 ? _header : new List<string> { "// CALL3.TXT di YA-GT" };
                foreach (var h in header)
                    sb.Append(h.StartsWith("// Contains ") && h.EndsWith(" unique callsigns.") ? $"// Contains {total} unique callsigns." : h).Append('\n');
                sb.Append(MarkManual).Append(" voci inserite a mano: valgono sopra tutto e restano agli aggiornamenti / manual entries: win over everything and survive updates\n");
                foreach (var kv in _manual.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(",,,\n");
                sb.Append(MarkCluster).Append(" locatore a 6 caratteri dichiarato dalla stazione negli spot automatici (DM4X, RBN), data dell'ultima conferma / locator declared by the station itself\n");
                foreach (var kv in _cluster.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append(',').Append(kv.Value.loc).Append(',').Append(kv.Value.when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(",,\n");
                sb.Append(MarkBody).Append(" tutte le altre voci / all other entries\n");
                foreach (var kv in _map.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(",,,\n");
                _dirty = 0;
            }
            var dest = TargetPath;
            try
            {
                var tmp = dest + ".nuovo";
                File.WriteAllBytes(tmp, Compat.Latin1.GetBytes(sb.ToString()));
                if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                Compat.MoveOver(tmp, dest);
                PathUsed = dest;
                _lastSave = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                Error = "CALL3.TXT non scritto: " + ex.Message;
                Interlocked.Exchange(ref _dirty, 1);
                return false;
            }
        }
    }
}
