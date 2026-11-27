using System.Globalization;
using System.Text.Json;

namespace YaGt.Core;

/// <summary>
/// Mappa di propagazione di prop.kc2g.com (Andrew Rodland KC2G): curve di livello della MUF(3000) o della foF2 in GeoJSON e
/// valori misurati dalle ionosonde. Le curve sono in latitudine/longitudine, quindi si ridisegnano in Mercatore e in azimutale.
/// </summary>
public sealed class Kc2gData
{
    public const string Base = "https://prop.kc2g.com/renders/current/";

    public sealed class Contour
    {
        public uint Color = 0xFF1F6FD1;
        public string Label = "";
        public double Value;
        public List<LatLon[]> Lines = new List<LatLon[]>();
    }

    public sealed class Station
    {
        public string Name = "";
        public LatLon Pos;
        public double Value;
        public double Confidence;
    }

    public string Metric = "mufd";   // mufd o fof2
    public List<Contour> Contours = new List<Contour>();
    public List<Station> Stations = new List<Station>();
    public DateTime Loaded = DateTime.MinValue;
    public string Status = "";

    public async Task LoadAsync(HttpClient http, string metric, CancellationToken ct)
    {
        metric = metric == "fof2" ? "fof2" : "mufd";
        try
        {
            var geo = await Compat.ReadString((await http.GetAsync(Base + metric + "-normal-now.geojson", ct)).EnsureSuccessStatusCode().Content, ct);
            var contours = ParseContours(geo);
            var stations = new List<Station>();
            try
            {
                var js = await Compat.ReadString((await http.GetAsync(Base + metric + "-normal-now_station.json", ct)).EnsureSuccessStatusCode().Content, ct);
                stations = ParseStations(js, metric);
            }
            catch { }
            Contours = contours; Stations = stations; Metric = metric;
            Loaded = DateTime.UtcNow;
            Status = L.F("KC2G {0}: {1} curve, {2} ionosonde", metric == "fof2" ? "foF2" : "MUF", contours.Count, stations.Count);
        }
        catch (Exception ex) { Status = "KC2G: " + ex.Message; }
    }

    static uint ParseColor(string s, uint def)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return 0xFF000000 | v;
        return def;
    }

    static IEnumerable<LatLon[]> Lines(JsonElement geom)
    {
        var type = geom.TryGetProperty("type", out var t) ? t.GetString() : "";
        if (!geom.TryGetProperty("coordinates", out var c)) yield break;
        if (type == "LineString") yield return Pts(c);
        else if (type == "MultiLineString" || type == "Polygon") foreach (var l in c.EnumerateArray()) yield return Pts(l);
        else if (type == "MultiPolygon") foreach (var p in c.EnumerateArray()) foreach (var l in p.EnumerateArray()) yield return Pts(l);
    }

    static LatLon[] Pts(JsonElement arr)
    {
        var list = new List<LatLon>();
        foreach (var p in arr.EnumerateArray())
        {
            if (p.GetArrayLength() < 2) continue;
            list.Add(new LatLon(p[1].GetDouble(), p[0].GetDouble()));
        }
        return list.ToArray();
    }

    public static List<Contour> ParseContours(string json)
    {
        var res = new Dictionary<string, Contour>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("features", out var feats)) return new List<Contour>();
            foreach (var f in feats.EnumerateArray())
            {
                if (!f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) continue;
                string label = ""; double val = double.NaN; uint col = 0xFF1F6FD1;
                if (f.TryGetProperty("properties", out var pr) && pr.ValueKind == JsonValueKind.Object)
                {
                    if (pr.TryGetProperty("stroke", out var st) && st.ValueKind == JsonValueKind.String) col = ParseColor(st.GetString(), col);
                    if (pr.TryGetProperty("level-value", out var lv) && lv.ValueKind == JsonValueKind.Number) val = lv.GetDouble();
                    if (pr.TryGetProperty("title", out var ti) && ti.ValueKind == JsonValueKind.String) label = (ti.GetString() ?? "").Trim();
                }
                if (double.IsNaN(val))
                {
                    var num = new string(label.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
                    double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                }
                var key = val.ToString("0.###", CultureInfo.InvariantCulture);
                if (!res.TryGetValue(key, out var c)) res[key] = c = new Contour { Color = col, Value = val, Label = val.ToString("0.#", CultureInfo.InvariantCulture) };
                foreach (var l in Lines(g)) if (l.Length >= 2) c.Lines.Add(l);
            }
        }
        return res.Values.OrderBy(c => c.Value).ToList();
    }

    static double Num(JsonElement o, params string[] names)
    {
        foreach (var n in names)
        {
            if (o.TryGetProperty(n, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
                if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            }
        }
        return double.NaN;
    }

    public static List<Station> ParseStations(string json, string metric)
    {
        var res = new List<Station>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return res;
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                double lat = Num(o, "station.latitude", "latitude", "lat"), lon = Num(o, "station.longitude", "longitude", "lon");
                if ((double.IsNaN(lat) || double.IsNaN(lon)) && o.TryGetProperty("station", out var st) && st.ValueKind == JsonValueKind.Object)
                { lat = Num(st, "latitude"); lon = Num(st, "longitude"); }
                double v = Num(o, metric);
                if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsNaN(v)) continue;
                if (lon > 180) lon -= 360;
                string name = "";
                if (o.TryGetProperty("station.name", out var nm) && nm.ValueKind == JsonValueKind.String) name = nm.GetString();
                double cs = Num(o, "cs");
                res.Add(new Station { Name = name ?? "", Pos = new LatLon(lat, lon), Value = v, Confidence = double.IsNaN(cs) ? 1 : cs });
            }
        }
        return res;
    }
}

/// <summary>
/// Ovale aurorale del NOAA SWPC (modello OVATION Prime, previsione a 30-90 minuti): probabilità di aurora 0-100 su una
/// griglia di 1° (longitudine 0-359, latitudine -90…90), ridisegnata nella proiezione della mappa.
/// </summary>
public sealed class AuroraData
{
    public const string Url = "https://services.swpc.noaa.gov/json/ovation_aurora_latest.json";
    public byte[] Grid = new byte[360 * 181];   // [lon * 181 + (lat + 90)]
    public bool Valid;
    public string ForecastTime = "";
    public DateTime Loaded = DateTime.MinValue;
    public string Status = "";
    public long Version;

    public async Task LoadAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var js = await Compat.ReadString((await http.GetAsync(Url, ct)).EnsureSuccessStatusCode().Content, ct);
            var grid = new byte[360 * 181];
            int n = 0;
            using (var doc = JsonDocument.Parse(js))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("Forecast Time", out var ft) && ft.ValueKind == JsonValueKind.String) ForecastTime = ft.GetString();
                foreach (var p in root.GetProperty("coordinates").EnumerateArray())
                {
                    int lon = (int)Math.Round(p[0].GetDouble()), lat = (int)Math.Round(p[1].GetDouble());
                    int v = (int)Math.Round(p[2].GetDouble());
                    lon = ((lon % 360) + 360) % 360;
                    if (lat < -90 || lat > 90) continue;
                    grid[lon * 181 + lat + 90] = (byte)Math.Max(0, Math.Min(100, v));
                    n++;
                }
            }
            Grid = grid; Valid = n > 1000; Loaded = DateTime.UtcNow; Version++;
            Status = L.F("Aurora NOAA: previsione {0}", ForecastTime);
        }
        catch (Exception ex) { Status = "Aurora NOAA: " + ex.Message; }
    }

    /// <summary>Probabilità 0-100 in un punto (interpolazione bilineare sulla griglia di 1°).</summary>
    public double At(double lat, double lon)
    {
        if (!Valid) return 0;
        double x = ((lon % 360) + 360) % 360, y = lat + 90;
        if (y < 0 || y > 180) return 0;
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        int x1 = (x0 + 1) % 360, y1 = Math.Min(180, y0 + 1);
        double a = Grid[x0 * 181 + y0], b = Grid[x1 * 181 + y0], c = Grid[x0 * 181 + y1], d = Grid[x1 * 181 + y1];
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
    }
}
