using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace YaGt.Core;

/// <summary>
/// Ultima fonte di locatori: l'API pubblica di G7VRD "locator by callsign"
/// (https://api.g7vrd.co.uk/v1/locators/by-callsign/CALL1,CALL2). Si interroga solo per i nominativi che, dopo voci a
/// mano, correzioni dal cluster e CALL3.TXT (con i locatori imparati dal vivo), non hanno ancora un locatore a 6 caratteri.
/// Si accetta solo un locatore a 6 caratteri valido, non il centro del quadrato (LL/MM). Le richieste sono raggruppate
/// (al massimo 40 nominativi al minuto) e ogni nominativo si chiede una volta ogni 24 ore: è un servizio hobbistico.
/// I locatori trovati entrano in CALL3.TXT (CallDb.Learn): nessun file a parte. In YA-GT le ricerche partono ogni 6 ore.
/// </summary>
public static class G7Lookup
{
    public const string Api = "https://api.g7vrd.co.uk/v1/locators/by-callsign/";
    public static bool Enabled = true;
    static readonly ConcurrentDictionary<string, DateTime> _asked = new();
    static readonly ConcurrentQueue<string> _queue = new();
    public static string LastStatus = "";
    /// <summary>Intervallo tra due tornate di ricerche (6 ore).</summary>
    public static TimeSpan Period = TimeSpan.FromHours(6);
    static bool _burst;
    public static int Pending => _queue.Count;
    /// <summary>Dove va un locatore trovato (CallDb.Learn); true se il database è cambiato.</summary>
    public static Func<string, string, bool> Found;

    /// <summary>Mette in coda il nominativo, se non è già stato chiesto nelle ultime 24 ore.</summary>
    public static void Request(string call)
    {
        if (!Enabled || string.IsNullOrEmpty(call) || call.Length < 3 || call.Contains('/') || call.Contains('-')) return;
        var now = DateTime.UtcNow;
        if (_asked.TryGetValue(call, out var t) && now - t < TimeSpan.FromHours(24)) return;
        _asked[call] = now;
        _queue.Enqueue(call);
    }

    /// <summary>Ciclo in sottofondo: un gruppo di nominativi al minuto.</summary>
    public static async Task RunAsync(HttpClient http, Action changed, CancellationToken ct = default)
    {
        // prima tornata 2 minuti dopo l'avvio, poi ogni 6 ore
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch { return; }
        _burst = true;
        while (!ct.IsCancellationRequested)
        {
            // YA-GT: le ricerche si fanno in automatico ogni 6 ore (regola CALL3 di IC8TEM, 14/11/2026); in una tornata si
            // svuota la coda a gruppi di 40 nominativi al minuto
            if (_queue.IsEmpty || !_burst) { try { await Task.Delay(Period, ct); } catch { return; } _burst = true; }
            else { try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } catch { return; } }
            if (!Enabled || _queue.IsEmpty) { _burst = false; continue; }
            var batch = new List<string>();
            while (batch.Count < 40 && _queue.TryDequeue(out var c)) batch.Add(c);
            try
            {
                var url = Api + string.Join(",", batch.Select(Uri.EscapeDataString));
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) { LastStatus = $"G7VRD: HTTP {(int)resp.StatusCode}"; continue; }
                using var doc = JsonDocument.Parse(await Compat.ReadString(resp.Content, ct));
                int n = 0;
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var loc = Pick(prop.Value);
                        if (loc == "") continue;
                        if (Found?.Invoke(prop.Name.ToUpperInvariant(), loc) == true) n++;
                    }
                if (n > 0) changed?.Invoke();
                LastStatus = $"G7VRD: {batch.Count} chiesti, {n} locatori a 6 caratteri";
            }
            catch (Exception ex) { LastStatus = "G7VRD: " + ex.Message; }
        }
    }

    /// <summary>Il valore può essere una stringa, un elenco o un oggetto: si prende il primo locatore a 6 caratteri utile.</summary>
    static string Pick(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                var s = (v.GetString() ?? "").Trim().ToUpperInvariant();
                return s.Length == 6 && Locator.IsValid(s) && !s.EndsWith("LL") && !s.EndsWith("MM") ? s : "";
            case JsonValueKind.Array:
                foreach (var e in v.EnumerateArray()) { var r = Pick(e); if (r != "") return r; }
                return "";
            case JsonValueKind.Object:
                foreach (var name in new[] { "locator", "grid", "maidenhead", "value" })
                    if (v.TryGetProperty(name, out var x)) { var r = Pick(x); if (r != "") return r; }
                foreach (var p in v.EnumerateObject()) { var r = Pick(p.Value); if (r != "") return r; }
                return "";
            default: return "";
        }
    }
}
