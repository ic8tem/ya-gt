namespace YaGt.Core;

/// <summary>
/// Superficie di disegno comune alle due versioni: Avalonia (Windows 10/11) e GDI+ (Windows 7). La mappa (MapRenderer)
/// disegna solo con questi comandi, così è la stessa nelle due finestre. Colori ARGB 0xAARRGGBB.
/// </summary>
public interface ICanvas
{
    void FillRect(double x, double y, double w, double h, uint argb);
    void FillPolygon(P2[] pts, uint argb);
    void StrokePolygon(P2[] pts, uint argb, double width);
    void DrawPolyline(P2[] pts, uint argb, double width, bool dashed = false);
    void DrawLine(double x1, double y1, double x2, double y2, uint argb, double width);
    void FillEllipse(double cx, double cy, double rx, double ry, uint argb);
    void StrokeEllipse(double cx, double cy, double rx, double ry, uint argb, double width);
    void FillRoundRect(double x, double y, double w, double h, double r, uint argb);
    void StrokeRoundRect(double x, double y, double w, double h, double r, uint argb, double width, bool dashed = false);
    void DrawText(string s, double x, double y, double size, uint argb, bool bold = false);
    P2 MeasureText(string s, double size, bool bold = false);
    /// <summary>Immagine ARGB (non premoltiplicata) w×h disegnata nel rettangolo indicato, con smussatura.</summary>
    void DrawImage(uint[] argb, int w, int h, double x, double y, double dw, double dh);
    void PushClipEllipse(double cx, double cy, double rx, double ry);
    void PopClip();
}

/// <summary>Colori comuni (tema "chiaro" con l'aspetto di YA-NEC, e "notte").</summary>
public sealed class Theme
{
    public string Key = "chiaro";
    public bool Dark;
    public uint Page, Bar, Panel, Line, Text, Muted, Accent, AccentFg, Band, BandFg, MapSea, MapLand, MapCoast, MapBorder, MapGrid, MapGridLabel, Home, Night, Terminator;

    public static Theme Get(string key)
    {
        if (key == "notte")
            return new Theme
            {
                Key = "notte", Dark = true, Page = 0xFF0B1220, Bar = 0xFF0E1626, Panel = 0xFF111B2E, Line = 0xFF22314A, Text = 0xFFD9E2EE, Muted = 0xFF8D9BB0,
                Accent = 0xFF6B9BF2, AccentFg = 0xFF0B1220, Band = 0xFF1A2A4A, BandFg = 0xFFD9E6FF, MapSea = 0xFF0C1424, MapLand = 0xFF162238,
                MapCoast = 0xFF6F87AE, MapBorder = 0xFF33476A, MapGrid = 0xFF25406A, MapGridLabel = 0xFF34588F, Home = 0xFFE3A857, Night = 0xFF000000, Terminator = 0xFFE3A857
            };
        return new Theme
        {
            Key = "chiaro", Page = 0xFFF0F0F0, Bar = 0xFFE6E8F0, Panel = 0xFFFFFFFF, Line = 0xFFD5DCEA, Text = 0xFF111827, Muted = 0xFF6D6D6D,
            Accent = 0xFF1F6FD1, AccentFg = 0xFFFFFFFF, Band = 0xFF1E293B, BandFg = 0xFFE2E8F0, MapSea = 0xFFE2ECF8, MapLand = 0xFFFFFFFF,
            MapCoast = 0xFF1F3A66, MapBorder = 0xFFC3CBD5, MapGrid = 0xFF9CC3EA, MapGridLabel = 0xFF7FA9D6, Home = 0xFFD97B00, Night = 0xFF0B1A3A, Terminator = 0xFFD97B00
        };
    }

    public static uint WithAlpha(uint c, int a) => ((uint)Math.Max(0, Math.Min(255, a)) << 24) | (c & 0x00FFFFFF);
}

/// <summary>Coste e confini (Natural Earth 1:50 milioni, pubblico dominio), inclusi nel programma.</summary>
public static class BaseMap
{
    public static List<LatLon[]> Land = new List<LatLon[]>();
    public static List<LatLon[]> Borders = new List<LatLon[]>();
    static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Land = Read("land50.txt");
        Borders = Read("borders50.txt");
    }

    static List<LatLon[]> Read(string name)
    {
        var res = new List<LatLon[]>();
        var asm = typeof(BaseMap).Assembly;
        var res0 = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (res0 == null) return res;
        using (var s = asm.GetManifestResourceStream(res0))
        using (var r = new StreamReader(s))
        {
            string line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var pts = new List<LatLon>();
                foreach (var pp in line.Split(';'))
                {
                    int c = pp.IndexOf(',');
                    if (c < 0) continue;
                    if (double.TryParse(pp.Substring(0, c), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon) &&
                        double.TryParse(pp.Substring(c + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
                        pts.Add(new LatLon(lat, lon));
                }
                if (pts.Count >= 2) res.Add(pts.ToArray());
            }
        }
        return res;
    }
}
