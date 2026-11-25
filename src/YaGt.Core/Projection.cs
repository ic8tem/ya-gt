namespace YaGt.Core;

public enum ProjKind { Mercator, Azimuthal }

/// <summary>Punto sullo schermo (pixel).</summary>
public struct P2
{
    public double X, Y;
    public P2(double x, double y) { X = x; Y = y; }
}

/// <summary>
/// Proiezioni della mappa e vista (centro e scala). Coordinate "mondo":
///  Mercatore: x = lon/180 (-1…1), y = -ln(tan(π/4 + lat/2))/π (Nord in alto), latitudine limitata a ±85°;
///  azimutale equidistante centrata sulla stazione: raggio = distanza angolare / π (0 = stazione, 1 = antipodo), Nord in alto.
/// Nel Mercatore il mondo si ripete a destra e a sinistra (copie a ±2 unità) per spostarsi senza fine.
/// </summary>
public sealed class Projection
{
    public ProjKind Kind = ProjKind.Mercator;
    public LatLon Home;
    public double ViewCx, ViewCy;  // centro della vista (mondo)
    public double Scale = 300;     // pixel per unità di mondo
    public double Width = 800, Height = 600;
    public const double MaxLat = 85.0;

    double _sinH, _cosH;
    void Prep() { _sinH = Math.Sin(Home.Lat * Geo.D2R); _cosH = Math.Cos(Home.Lat * Geo.D2R); }

    public void SetHome(LatLon h) { Home = h; Prep(); }

    public bool Forward(double lat, double lon, out double wx, out double wy)
    {
        if (Kind == ProjKind.Mercator)
        {
            if (lat > MaxLat) lat = MaxLat; else if (lat < -MaxLat) lat = -MaxLat;
            wx = lon / 180.0;
            wy = -Math.Log(Math.Tan(Math.PI / 4 + lat * Geo.D2R / 2)) / Math.PI;
            return true;
        }
        double f = lat * Geo.D2R, dl = (lon - Home.Lon) * Geo.D2R;
        double sf = Math.Sin(f), cf = Math.Cos(f);
        double cosc = _sinH * sf + _cosH * cf * Math.Cos(dl);
        if (cosc > 1) cosc = 1; else if (cosc < -1) cosc = -1;
        double c = Math.Acos(cosc);
        double y = Math.Sin(dl) * cf;
        double x = _cosH * sf - _sinH * cf * Math.Cos(dl);
        double az = Math.Atan2(y, x);
        double r = c / Math.PI;
        wx = r * Math.Sin(az);
        wy = -r * Math.Cos(az);
        return true;
    }

    public bool Inverse(double wx, double wy, out double lat, out double lon)
    {
        if (Kind == ProjKind.Mercator)
        {
            lon = wx * 180.0;
            lon = ((lon + 180) % 360 + 360) % 360 - 180;
            lat = (2 * Math.Atan(Math.Exp(-wy * Math.PI)) - Math.PI / 2) / Geo.D2R;
            return wy >= -1.0001 && wy <= 1.0001;
        }
        double r = Math.Sqrt(wx * wx + wy * wy);
        if (r > 1) { lat = lon = 0; return false; }
        double c = r * Math.PI;
        double az = Math.Atan2(wx, -wy);
        var d = Geo.Destination(Home, az / Geo.D2R, c * Geo.EarthKm);
        lat = d.Lat; lon = d.Lon;
        return true;
    }

    public P2 ToScreen(double wx, double wy) => new P2(Width / 2 + (wx - ViewCx) * Scale, Height / 2 + (wy - ViewCy) * Scale);
    public void ToWorld(double sx, double sy, out double wx, out double wy) { wx = ViewCx + (sx - Width / 2) / Scale; wy = ViewCy + (sy - Height / 2) / Scale; }

    /// <summary>Punto geografico sullo schermo; nel Mercatore sceglie la copia del mondo più vicina al centro della vista.</summary>
    public P2 Screen(LatLon p)
    {
        Forward(p.Lat, p.Lon, out var wx, out var wy);
        if (Kind == ProjKind.Mercator) wx += WrapOffset(wx);
        return ToScreen(wx, wy);
    }

    public double WrapOffset(double wx)
    {
        double k = Math.Round((ViewCx - wx) / 2.0);
        return k * 2.0;
    }

    /// <summary>Copie del mondo da disegnare nel Mercatore (spostamenti in unità di mondo).</summary>
    public IEnumerable<double> WorldCopies()
    {
        if (Kind != ProjKind.Mercator) { yield return 0; yield break; }
        double left = ViewCx - Width / 2 / Scale, right = ViewCx + Width / 2 / Scale;
        int k0 = (int)Math.Floor((left + 1) / 2.0), k1 = (int)Math.Floor((right + 1) / 2.0);
        for (int k = k0; k <= k1; k++) yield return k * 2.0;
    }

    /// <summary>Vista iniziale: tutto il mondo (Mercatore centrato sulla longitudine della stazione).</summary>
    public void Fit()
    {
        if (Kind == ProjKind.Mercator)
        {
            Scale = Math.Max(Width / 2.0, Height / 2.0 * 1.0);
            Scale = Math.Min(Width / 2.0, Height / 1.6);
            if (Scale < 50) Scale = 50;
            Forward(Home.Lat, Home.Lon, out var hx, out _);
            ViewCx = hx; ViewCy = -0.1;
        }
        else
        {
            Scale = Math.Min(Width, Height) / 2.0 * 0.96;
            ViewCx = 0; ViewCy = 0;
        }
        Clamp();
    }

    public void Clamp()
    {
        double minScale = Kind == ProjKind.Mercator ? Height / 2.2 : Math.Min(Width, Height) / 2.0 * 0.5;
        if (Scale < minScale) Scale = minScale;
        if (Scale > 200000) Scale = 200000;
        double halfH = Height / 2 / Scale;
        if (Kind == ProjKind.Mercator)
        {
            if (halfH >= 1) ViewCy = 0;
            else ViewCy = Math.Max(-1 + halfH, Math.Min(1 - halfH, ViewCy));
            // la x resta libera (il mondo si ripete), ma si riporta vicino a 0
            while (ViewCx > 1) ViewCx -= 2;
            while (ViewCx < -1) ViewCx += 2;
        }
        else
        {
            ViewCx = Math.Max(-1.1, Math.Min(1.1, ViewCx));
            ViewCy = Math.Max(-1.1, Math.Min(1.1, ViewCy));
        }
    }

    public void ZoomAt(double sx, double sy, double factor)
    {
        ToWorld(sx, sy, out var wx, out var wy);
        Scale *= factor;
        Clamp();
        // il punto sotto il mouse resta fermo
        ViewCx = wx - (sx - Width / 2) / Scale;
        ViewCy = wy - (sy - Height / 2) / Scale;
        Clamp();
    }

    public void Pan(double dx, double dy) { ViewCx -= dx / Scale; ViewCy -= dy / Scale; Clamp(); }

    /// <summary>
    /// Proietta una linea in latitudine/longitudine e la spezza dove salta (antimeridiano nel Mercatore, bordo esterno
    /// nell'azimutale). Restituisce i tratti in coordinate mondo; skipCut salta i lati sull'antimeridiano (tagli di Natural Earth).
    /// </summary>
    public List<List<P2>> ProjectLine(IList<LatLon> pts, bool skipCut = false)
    {
        var res = new List<List<P2>>();
        var cur = new List<P2>();
        double px = 0, py = 0; bool has = false;
        double jump = Kind == ProjKind.Mercator ? 1.0 : 0.35;
        LatLon prev = default(LatLon);
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            Forward(p.Lat, p.Lon, out var wx, out var wy);
            bool cut = skipCut && has && Math.Abs(Math.Abs(p.Lon) - 180) < 1e-6 && Math.Abs(Math.Abs(prev.Lon) - 180) < 1e-6;
            if (has && (cut || Math.Abs(wx - px) > jump || Math.Abs(wy - py) > jump))
            {
                if (cur.Count >= 2) res.Add(cur);
                cur = new List<P2>();
            }
            cur.Add(new P2(wx, wy));
            px = wx; py = wy; has = true; prev = p;
        }
        if (cur.Count >= 2) res.Add(cur);
        return res;
    }

    /// <summary>Anello di un poligono in coordinate mondo; null se salta (non si può riempire senza errori).</summary>
    public P2[] ProjectRing(IList<LatLon> pts)
    {
        var res = new P2[pts.Count];
        double jump = Kind == ProjKind.Mercator ? 1.0 : 0.35;
        for (int i = 0; i < pts.Count; i++)
        {
            Forward(pts[i].Lat, pts[i].Lon, out var wx, out var wy);
            if (i > 0 && (Math.Abs(wx - res[i - 1].X) > jump || Math.Abs(wy - res[i - 1].Y) > jump)) return null;
            res[i] = new P2(wx, wy);
        }
        return res;
    }

    public P2[] ToScreen(IList<P2> world, double offset)
    {
        var r = new P2[world.Count];
        for (int i = 0; i < world.Count; i++) r[i] = ToScreen(world[i].X + offset, world[i].Y);
        return r;
    }
}
