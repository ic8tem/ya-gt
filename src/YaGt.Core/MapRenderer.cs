using System.Globalization;

namespace YaGt.Core;

/// <summary>Area sensibile al mouse (fumetto, punto, quadrato) con il testo del suggerimento.</summary>
public sealed class HitArea
{
    public double X, Y, W, H;
    public string Text = "";
    public bool Contains(double x, double y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
}

/// <summary>
/// Disegno della mappa a livelli, uguale per le due versioni (Avalonia e Windows 7). Ogni livello si accende e si spegne
/// dalle caselle: base (terre, coste, confini), griglia Maidenhead, greyline, KC2G, aurora NOAA, quadrati LoTW, QSO di QLog,
/// percorsi, chi mi sente, chi sento io, le mie decodifiche UDP.
/// </summary>
public sealed class MapRenderer
{
    public readonly Projection Proj = new Projection();
    public readonly List<HitArea> Hits = new List<HitArea>();
    public static readonly uint ColHeardMe = 0xFF1F6FD1;   // bordo dei fumetti "mi sentono" (blu YA-NEC)
    public static readonly uint ColIHeard = 0xFF8E24AA;    // bordo dei fumetti "sento io" (viola)
    public static readonly uint ColDecode = 0xFF0F766E;    // bordo delle nuvolette delle mie decodifiche (verde petrolio)

    // --- cache delle geometrie in coordinate mondo (cambiano solo con proiezione e stazione)
    ProjKind _cacheKind = (ProjKind)(-1);
    LatLon _cacheHome;
    readonly List<(P2[] ring, double x0, double y0, double x1, double y1)> _land = new List<(P2[], double, double, double, double)>();
    readonly List<(P2[] line, double x0, double y0, double x1, double y1)> _coast = new List<(P2[], double, double, double, double)>();
    readonly List<(P2[] line, double x0, double y0, double x1, double y1)> _borders = new List<(P2[], double, double, double, double)>();

    // --- cache del raster (notte + aurora)
    uint[] _raster; int _rw, _rh; string _rasterKey = "";

    static (double, double, double, double) Box(IList<P2> pts)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in pts) { if (p.X < x0) x0 = p.X; if (p.Y < y0) y0 = p.Y; if (p.X > x1) x1 = p.X; if (p.Y > y1) y1 = p.Y; }
        return (x0, y0, x1, y1);
    }

    void EnsureGeometry()
    {
        if (_cacheKind == Proj.Kind && _cacheHome.Lat == Proj.Home.Lat && _cacheHome.Lon == Proj.Home.Lon) return;
        _cacheKind = Proj.Kind; _cacheHome = Proj.Home;
        _land.Clear(); _coast.Clear(); _borders.Clear();
        foreach (var ring in BaseMap.Land)
        {
            var r = Proj.ProjectRing(ring);
            if (r != null) { var b = Box(r); _land.Add((r, b.Item1, b.Item2, b.Item3, b.Item4)); }
            foreach (var seg in Proj.ProjectLine(ring, true)) { var a = seg.ToArray(); var b = Box(a); _coast.Add((a, b.Item1, b.Item2, b.Item3, b.Item4)); }
        }
        foreach (var line in BaseMap.Borders)
            foreach (var seg in Proj.ProjectLine(line)) { var a = seg.ToArray(); var b = Box(a); _borders.Add((a, b.Item1, b.Item2, b.Item3, b.Item4)); }
    }

    bool Visible(double x0, double y0, double x1, double y1, double off)
    {
        var a = Proj.ToScreen(x0 + off, y0); var b = Proj.ToScreen(x1 + off, y1);
        return !(b.X < 0 || a.X > Proj.Width || b.Y < 0 || a.Y > Proj.Height);
    }

    static double Lum(uint c) => ((c >> 16) & 255) * 0.299 + ((c >> 8) & 255) * 0.587 + (c & 255) * 0.114;

    // ================================================================== disegno

    public void Render(ICanvas g, AppState st, Theme th, double width, double height)
    {
        Hits.Clear();
        Proj.Width = width; Proj.Height = height;
        if (Proj.Kind != st.Proj || Proj.Home.Lat != st.Home.Lat || Proj.Home.Lon != st.Home.Lon)
        {
            Proj.Kind = st.Proj;
            Proj.SetHome(st.Home);
            Proj.Fit();
        }
        EnsureGeometry();
        var f = st.Current;
        bool merc = Proj.Kind == ProjKind.Mercator;
        var offs = Proj.WorldCopies().ToList();

        // --- 1. fondo e terre
        g.FillRect(0, 0, width, height, merc ? th.MapSea : th.Page);
        P2 c0 = Proj.ToScreen(0, 0);
        double rad = Proj.Scale;
        if (!merc)
        {
            g.FillEllipse(c0.X, c0.Y, rad, rad, th.MapSea);
            g.PushClipEllipse(c0.X, c0.Y, rad, rad);
        }
        foreach (var off in offs)
            foreach (var l in _land)
                if (Visible(l.x0, l.y0, l.x1, l.y1, off)) g.FillPolygon(Proj.ToScreen(l.ring, off), th.MapLand);
        foreach (var off in offs)
        {
            foreach (var l in _borders)
                if (Visible(l.x0, l.y0, l.x1, l.y1, off)) g.DrawPolyline(Proj.ToScreen(l.line, off), th.MapBorder, 0.8);
            foreach (var l in _coast)
                if (Visible(l.x0, l.y0, l.x1, l.y1, off)) g.DrawPolyline(Proj.ToScreen(l.line, off), th.MapCoast, 0.9);
        }

        // --- 2. raster: notte (greyline) e aurora
        if (st.LayerGreyline || st.LayerAurora && st.Aurora.Valid) DrawRaster(g, st, th, f.Utc);

        // --- 3. KC2G
        if (st.LayerKc2g) DrawKc2g(g, st, th, offs);

        // --- 4. griglia Maidenhead
        if (st.LayerGrid) DrawGrid(g, th, offs);

        // --- 5. quadrati LoTW
        if (st.LayerLotw) DrawSquares(g, st, f, offs);

        // --- 6. terminatore
        if (st.LayerGreyline) DrawTerminator(g, th, f.Utc, offs);

        // --- 7. percorsi
        if (st.LayerPaths)
        {
            if (st.LayerHeardMe) foreach (var s in f.HeardMe) DrawPath(g, st.Home, s.Pos, Theme.WithAlpha(ColHeardMe, 160), offs);
            if (st.LayerIHeard) foreach (var s in f.IHeard) DrawPath(g, st.Home, s.Pos, Theme.WithAlpha(ColIHeard, 160), offs);
            if (st.LayerDecodes) foreach (var d in f.Decodes) DrawPath(g, st.Home, d.Pos, Theme.WithAlpha(ColDecode, 160), offs);
        }

        // --- 8. QLog
        if (st.LayerQLog) DrawQLog(g, f, offs);

        if (!merc)
        {
            g.PopClip();
            g.StrokeEllipse(c0.X, c0.Y, rad, rad, th.MapCoast, 1.2);
            DrawAzimuthRing(g, th, c0, rad);
        }

        // --- 9. fumetti: prima le decodifiche (nuvolette), poi gli spot; i più forti hanno la precedenza sul posto
        var placed = new List<(double x, double y, double w, double h)>();
        var home = Proj.Screen(st.Home);
        placed.Add((home.X - 8, home.Y - 8, 16, 16));
        if (st.LayerDecodes) DrawDecodes(g, st, f, offs, placed);
        var spots = new List<SpotItem>();
        if (st.LayerHeardMe) spots.AddRange(f.HeardMe);
        if (st.LayerIHeard) spots.AddRange(f.IHeard);
        DrawSpots(g, st, spots.OrderByDescending(s => s.Snr ?? -99).ToList(), offs, placed);

        // --- 10. stazione
        foreach (var off in offs)
        {
            Proj.Forward(st.Home.Lat, st.Home.Lon, out var hx, out var hy);
            var h = Proj.ToScreen(hx + off, hy);
            if (h.X < -20 || h.X > width + 20) continue;
            g.FillEllipse(h.X, h.Y, 6.5, 6.5, 0xFFFFFFFF);
            g.FillEllipse(h.X, h.Y, 5, 5, th.Home);
            g.StrokeEllipse(h.X, h.Y, 6.5, 6.5, 0xFF000000, 1);
            var lbl = st.MyCall + " " + st.MyLoc;
            var sz = g.MeasureText(lbl, 11, true);
            g.FillRoundRect(h.X + 9, h.Y - sz.Y / 2 - 2, sz.X + 8, sz.Y + 4, 3, 0xE6FFFFFF);
            g.DrawText(lbl, h.X + 13, h.Y - sz.Y / 2, 11, 0xFF111827, true);
        }

        DrawLegend(g, st, th, width, height);
        if (st.LayerStats) DrawStats(g, st, f, width);
    }

    // ------------------------------------------------------------------ riquadro delle statistiche (in alto a destra)

    void DrawStats(ICanvas g, AppState st, Frame f, double width)
    {
        var s = f.Stats;
        var lines = new List<(string text, uint col, bool bold, double size)>();
        void Ln(string t, uint c = 0xFF111827, bool b = false, double sz = 10.5) => lines.Add((t, c, b, sz));
        string Snr(int? v) => v.HasValue ? (v.Value > 0 ? "+" : "") + v.Value + " dB" : "—";
        void Block(string title, uint col, DirStats d, bool show)
        {
            if (!show) return;
            Ln(L.T(title), col, true, 11);
            if (d.Stations == 0) { Ln("  " + L.T("nessuna stazione"), 0xFF6D6D6D); return; }
            Ln("  " + L.F("{0} stazioni · {1} quadrati · {2} paesi", d.Stations, d.Squares, d.Countries));
            Ln("  " + L.F("più lontana: {0} {1:N0} km", d.MaxCall, d.MaxKm));
            Ln("  " + L.F("migliore: {0} {1}", d.BestCall, Snr(d.BestSnr)));
        }
        Ln(L.F("Statistiche · {0} · ultimi {1}", s.Band, st.AgeMin < 60 ? st.AgeMin + " min" : st.AgeMin / 60 + " h"), 0xFF1E293B, true, 11.5);
        Block("Mi sentono", ColHeardMe, s.HeardMe, st.LayerHeardMe);
        Block("Sento io", ColIHeard, s.IHeard, st.LayerIHeard);
        Block("Mie decodifiche (UDP)", ColDecode, s.Decodes, st.LayerDecodes);
        if (s.BySource.Count > 0) Ln(L.T("Fonti:") + " " + string.Join(", ", s.BySource.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)), 0xFF374151);
        if (st.LayerLotw)
        {
            Ln(L.F("Quadrati · {0}", f.SquaresTitle), 0xFF1E293B, true, 11);
            if (!s.HasSquares) Ln("  " + L.T("nessun QSO (scarica da LoTW, apri un ADIF o attiva QLog)"), 0xFF6D6D6D);
            else
            {
                Ln("  " + L.F("lavorati {0} · confermati {1} ({2:0}%)", s.SqWorked, s.SqConfirmed, s.SqWorked > 0 ? 100.0 * s.SqConfirmed / s.SqWorked : 0));
                Ln("  " + L.F("LoTW {0} · solo QSL {1} · non confermati {2}", s.SqLotw, s.SqConfirmed - s.SqLotw, s.SqWorked - s.SqConfirmed));
                if (f.SquaresGrey) Ln("  " + L.F("confermati su tutte le bande 80-10 m: {0}", s.SqAllHf));
            }
        }
        Ln(L.F("Aggiornato {0:HH:mm} UTC", f.Utc), 0xFF6D6D6D, false, 9.5);
        double w = 0, h = 8;
        foreach (var l in lines) { var m = g.MeasureText(l.text, l.size, l.bold); w = Math.Max(w, m.X); h += m.Y + 1; }
        w += 16;
        double x = width - w - 10, y = 10;
        g.FillRoundRect(x, y, w, h, 6, 0xEDFFFFFF);
        g.StrokeRoundRect(x, y, w, h, 6, 0xFFC3CBD5, 1);
        double cy = y + 4;
        foreach (var l in lines)
        {
            g.DrawText(l.text, x + 8, cy, l.size, l.col, l.bold);
            cy += g.MeasureText(l.text, l.size, l.bold).Y + 1;
        }
    }

    // ------------------------------------------------------------------ raster (notte, aurora)

    void DrawRaster(ICanvas g, AppState st, Theme th, DateTime utc)
    {
        const int step = 3;
        int w = Math.Max(1, (int)Math.Ceiling(Proj.Width / step)), h = Math.Max(1, (int)Math.Ceiling(Proj.Height / step));
        var key = string.Join("|", Proj.Kind, Proj.Home.Lat, Proj.Home.Lon, Proj.ViewCx.ToString("R", CultureInfo.InvariantCulture), Proj.ViewCy.ToString("R", CultureInfo.InvariantCulture),
            Proj.Scale.ToString("R", CultureInfo.InvariantCulture), w, h, st.LayerGreyline, st.LayerAurora, st.Aurora.Version, st.AuroraAlpha, utc.ToString("yyyyMMddHHmm"), th.Key);
        if (key != _rasterKey || _raster == null)
        {
            var sub = Sun.Subsolar(utc);
            var px = new uint[w * h];
            uint night = th.Night & 0x00FFFFFF;
            double aMax = st.AuroraAlpha / 100.0;
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    Proj.ToWorld(x * step + step / 2.0, y * step + step / 2.0, out var wx, out var wy);
                    if (!Proj.Inverse(wx, wy, out var lat, out var lon)) continue;
                    double a = 0; uint col = night; double r = 0, gg = 0, b = 0;
                    if (st.LayerGreyline)
                    {
                        double el = Sun.Elevation(sub, lat, lon);
                        if (el < 0.0)
                        {
                            a = 0.16 + 0.30 * Math.Min(1.0, -el / 18.0);
                            r = (night >> 16) & 255; gg = (night >> 8) & 255; b = night & 255;
                        }
                    }
                    if (st.LayerAurora && st.Aurora.Valid)
                    {
                        double v = st.Aurora.At(lat, lon);
                        if (v >= 3)
                        {
                            // verde → giallo → rosso con la probabilità
                            double t = Math.Min(1.0, (v - 3) / 60.0);
                            double ar = t < 0.5 ? 40 + 2 * t * 215 : 255, ag = t < 0.5 ? 220 : 220 - (t - 0.5) * 2 * 180, ab = 60 * (1 - t);
                            double aa = Math.Min(1.0, 0.25 + v / 70.0) * aMax;
                            // composizione "sopra" la notte
                            double na = aa + a * (1 - aa);
                            if (na > 0)
                            {
                                r = (ar * aa + r * a * (1 - aa)) / na; gg = (ag * aa + gg * a * (1 - aa)) / na; b = (ab * aa + b * a * (1 - aa)) / na;
                                a = na;
                            }
                        }
                    }
                    if (a <= 0) continue;
                    px[y * w + x] = ((uint)(a * 255) << 24) | ((uint)r << 16) | ((uint)gg << 8) | (uint)b;
                }
            });
            _raster = px; _rw = w; _rh = h; _rasterKey = key;
        }
        g.DrawImage(_raster, _rw, _rh, 0, 0, _rw * step, _rh * step);
    }

    void DrawTerminator(ICanvas g, Theme th, DateTime utc, List<double> offs)
    {
        var sub = Sun.Subsolar(utc);
        var pts = new List<LatLon>();
        for (int az = 0; az <= 360; az += 2) pts.Add(Geo.Destination(sub, az, Math.PI / 2 * Geo.EarthKm));
        foreach (var seg in Proj.ProjectLine(pts))
            foreach (var off in offs) g.DrawPolyline(Proj.ToScreen(seg, off), Theme.WithAlpha(th.Terminator, 200), 1.6, true);
        // punto subsolare
        foreach (var off in offs)
        {
            Proj.Forward(sub.Lat, sub.Lon, out var x, out var y);
            var p = Proj.ToScreen(x + off, y);
            g.FillEllipse(p.X, p.Y, 6, 6, 0xFFFFC107);
            g.StrokeEllipse(p.X, p.Y, 6, 6, 0xFFB26A00, 1.2);
        }
    }

    // ------------------------------------------------------------------ KC2G

    void DrawKc2g(ICanvas g, AppState st, Theme th, List<double> offs)
    {
        var k = st.Kc2g;
        int alpha = (int)(st.Kc2gAlpha / 100.0 * 255);
        foreach (var c in k.Contours)
        {
            var col = Theme.WithAlpha(c.Color, alpha);
            foreach (var line in c.Lines)
            {
                foreach (var seg in Proj.ProjectLine(line))
                {
                    foreach (var off in offs)
                    {
                        var sp = Proj.ToScreen(seg, off);
                        g.DrawPolyline(sp, col, 1.6);
                        // etichetta al centro dei tratti lunghi
                        if (sp.Length > 40)
                        {
                            var m = sp[sp.Length / 2];
                            if (m.X > 0 && m.X < Proj.Width && m.Y > 0 && m.Y < Proj.Height)
                            {
                                var sz = g.MeasureText(c.Label, 10, true);
                                g.FillRoundRect(m.X - sz.X / 2 - 3, m.Y - sz.Y / 2 - 1, sz.X + 6, sz.Y + 2, 3, Theme.WithAlpha(0xFFFFFFFF, (int)(alpha * 0.85)));
                                g.DrawText(c.Label, m.X - sz.X / 2, m.Y - sz.Y / 2, 10, Theme.WithAlpha(c.Color, Math.Max(alpha, 200)), true);
                            }
                        }
                    }
                }
            }
        }
        foreach (var s in k.Stations)
        {
            foreach (var off in offs)
            {
                Proj.Forward(s.Pos.Lat, s.Pos.Lon, out var x, out var y);
                var p = Proj.ToScreen(x + off, y);
                if (p.X < -10 || p.X > Proj.Width + 10 || p.Y < -10 || p.Y > Proj.Height + 10) continue;
                var txt = ((int)Math.Round(s.Value)).ToString(CultureInfo.InvariantCulture);
                var sz = g.MeasureText(txt, 9, true);
                double r = Math.Max(sz.X, sz.Y) / 2 + 3;
                g.FillEllipse(p.X, p.Y, r, r, Theme.WithAlpha(0xFFFFFFFF, (int)(alpha * (0.4 + 0.6 * s.Confidence))));
                g.StrokeEllipse(p.X, p.Y, r, r, Theme.WithAlpha(0xFF374151, alpha), 1);
                g.DrawText(txt, p.X - sz.X / 2, p.Y - sz.Y / 2, 9, Theme.WithAlpha(0xFF111827, Math.Max(alpha, 180)), true);
                Hits.Add(new HitArea { X = p.X - r, Y = p.Y - r, W = 2 * r, H = 2 * r, Text = L.F("KC2G ionosonda {0}: {1} {2:0.0} MHz (affidabilità {3:0.00})", s.Name, k.Metric == "fof2" ? "foF2" : "MUF(D)", s.Value, s.Confidence) });
            }
        }
    }

    // ------------------------------------------------------------------ griglia

    void DrawGrid(ICanvas g, Theme th, List<double> offs)
    {
        // campi 20°×10°; quadrati 2°×1° quando la scala lo permette
        Proj.Forward(0, 2, out var ax, out _); Proj.Forward(0, 0, out var bx, out _);
        bool squares = Proj.Kind == ProjKind.Mercator ? Math.Abs(ax - bx) * Proj.Scale > 14 : Proj.Scale > 2500;
        var colF = Theme.WithAlpha(th.MapGrid, 200);
        var colS = Theme.WithAlpha(th.MapGrid, 90);
        double lonStep = squares ? 2 : 20, latStep = squares ? 1 : 10;
        for (double lon = -180; lon <= 180; lon += lonStep)
        {
            bool field = Math.Abs(lon % 20) < 1e-9;
            var pts = new List<LatLon>();
            for (double lat = -84; lat <= 84; lat += 2) pts.Add(new LatLon(lat, lon));
            foreach (var seg in Proj.ProjectLine(pts)) foreach (var off in offs) g.DrawPolyline(Proj.ToScreen(seg, off), field ? colF : colS, field ? 0.9 : 0.5);
        }
        for (double lat = -80; lat <= 80; lat += latStep)
        {
            bool field = Math.Abs(lat % 10) < 1e-9;
            var pts = new List<LatLon>();
            for (double lon = -180; lon <= 180; lon += 2) pts.Add(new LatLon(lat, lon));
            foreach (var seg in Proj.ProjectLine(pts)) foreach (var off in offs) g.DrawPolyline(Proj.ToScreen(seg, off), field ? colF : colS, field ? 0.9 : 0.5);
        }
        // etichette dei campi
        for (int fx = 0; fx < 18; fx++)
            for (int fy = 1; fy < 17; fy++)
            {
                var lbl = new string(new[] { (char)('A' + fx), (char)('A' + fy) });
                double lon = fx * 20 - 180 + 10, lat = fy * 10 - 90 + 5;
                foreach (var off in offs)
                {
                    Proj.Forward(lat, lon, out var x, out var y);
                    var p = Proj.ToScreen(x + off, y);
                    if (p.X < 0 || p.X > Proj.Width || p.Y < 0 || p.Y > Proj.Height) continue;
                    var sz = g.MeasureText(lbl, 11, true);
                    g.DrawText(lbl, p.X - sz.X / 2, p.Y - sz.Y / 2, 11, Theme.WithAlpha(th.MapGridLabel, 150), true);
                }
            }
    }

    static List<LatLon> SquareRing(double lon0, double lat0, double w, double h)
    {
        var r = new List<LatLon>();
        for (int i = 0; i <= 4; i++) r.Add(new LatLon(lat0, lon0 + w * i / 4));
        for (int i = 1; i <= 2; i++) r.Add(new LatLon(lat0 + h * i / 2, lon0 + w));
        for (int i = 3; i >= 0; i--) r.Add(new LatLon(lat0 + h, lon0 + w * i / 4));
        r.Add(new LatLon(lat0 + h / 2, lon0));
        return r;
    }

    void DrawSquares(ICanvas g, AppState st, Frame f, List<double> offs)
    {
        foreach (var si in f.Squares.Values)
        {
            if (!Locator.SquareOrigin(si.Grid, out var lon0, out var lat0)) continue;
            var ring = Proj.ProjectRing(SquareRing(lon0, lat0, 2, 1));
            if (ring == null) continue;
            var col = st.Lotw.ColorOf(si, f.SquaresGrey);
            foreach (var off in offs)
            {
                var sp = Proj.ToScreen(ring, off);
                var b = Box(sp);
                if (b.Item3 < 0 || b.Item1 > Proj.Width || b.Item4 < 0 || b.Item2 > Proj.Height) continue;
                g.FillPolygon(sp, col);
                if (b.Item3 - b.Item1 > 26)
                {
                    var sz = g.MeasureText(si.Grid, 9, false);
                    if (sz.X < b.Item3 - b.Item1 - 2)
                        g.DrawText(si.Grid, (b.Item1 + b.Item3) / 2 - sz.X / 2, (b.Item2 + b.Item4) / 2 - sz.Y / 2, 9, 0xC0000000, false);
                }
                var stato = L.T(f.SquaresGrey && si.AllHf ? "confermato su tutte le bande 80-10 m" : si.Lotw ? "confermato LoTW" : si.Qsl ? "confermato con QSL" : "lavorato, non confermato");
                Hits.Add(new HitArea { X = b.Item1, Y = b.Item2, W = b.Item3 - b.Item1, H = b.Item4 - b.Item2, Text = $"LoTW {si.Grid}: {si.Qsos} QSO, {stato}" });
            }
        }
    }

    void DrawPath(ICanvas g, LatLon a, LatLon b, uint col, List<double> offs)
    {
        var pts = Geo.GreatCircle(a, b, 48);
        foreach (var seg in Proj.ProjectLine(pts))
            foreach (var off in offs) g.DrawPolyline(Proj.ToScreen(seg, off), col, 1.5);
    }

    void DrawQLog(ICanvas g, Frame f, List<double> offs)
    {
        foreach (var q in f.QLog)
        {
            uint col = q.Lotw ? 0xFF00AF00 : q.Qsl ? 0xFF1E5AFF : 0xFFFF8C00;
            foreach (var off in offs)
            {
                Proj.Forward(q.Pos.Lat, q.Pos.Lon, out var x, out var y);
                var p = Proj.ToScreen(x + off, y);
                if (p.X < -5 || p.X > Proj.Width + 5 || p.Y < -5 || p.Y > Proj.Height + 5) continue;
                g.FillRect(p.X - 3, p.Y - 3, 6, 6, Theme.WithAlpha(col, 210));
                g.StrokePolygon(new[] { new P2(p.X - 3, p.Y - 3), new P2(p.X + 3, p.Y - 3), new P2(p.X + 3, p.Y + 3), new P2(p.X - 3, p.Y + 3) }, 0xFF1F2937, 0.8);
                Hits.Add(new HitArea { X = p.X - 4, Y = p.Y - 4, W = 8, H = 8, Text = $"QLog {q.Call}: {q.Qsos} QSO ({q.Bands}){(q.Lotw ? ", LoTW" : q.Qsl ? ", QSL" : "")}{(q.Approx ? " — " + L.T("posizione del paese (cty.dat)") : "")}" });
            }
        }
    }

    void DrawAzimuthRing(ICanvas g, Theme th, P2 c, double rad)
    {
        // tacche ogni 10° e cardinali, distanze ogni 5000 km
        for (int az = 0; az < 360; az += 10)
        {
            double a = az * Geo.D2R;
            double l = az % 90 == 0 ? 10 : 5;
            g.DrawLine(c.X + Math.Sin(a) * rad, c.Y - Math.Cos(a) * rad, c.X + Math.Sin(a) * (rad + l), c.Y - Math.Cos(a) * (rad + l), th.MapCoast, 1);
            if (az % 30 == 0)
            {
                var t = az.ToString(CultureInfo.InvariantCulture) + "°";
                var sz = g.MeasureText(t, 10, false);
                g.DrawText(t, c.X + Math.Sin(a) * (rad + 20) - sz.X / 2, c.Y - Math.Cos(a) * (rad + 20) - sz.Y / 2, 10, th.Text, false);
            }
        }
        for (int km = 5000; km < 20000; km += 5000)
        {
            double r = km / (Math.PI * Geo.EarthKm) * rad;
            g.StrokeEllipse(c.X, c.Y, r, r, Theme.WithAlpha(th.MapGrid, 120), 0.8);
            g.DrawText(km.ToString("N0", CultureInfo.GetCultureInfo("it-IT")) + " km", c.X + 3, c.Y - r + 1, 9, Theme.WithAlpha(th.MapGridLabel, 220), false);
        }
    }

    // ------------------------------------------------------------------ fumetti

    static bool Overlaps(List<(double x, double y, double w, double h)> placed, double x, double y, double w, double h)
    {
        foreach (var r in placed) if (x < r.x + r.w && x + w > r.x && y < r.y + r.h && y + h > r.y) return true;
        return false;
    }

    /// <summary>Cerca un posto libero per un'etichetta w×h vicino al punto (sopra, sopra a destra, sopra a sinistra, a destra…).</summary>
    bool FindSpot(List<(double x, double y, double w, double h)> placed, P2 p, double w, double h, out double bx, out double by)
    {
        var cand = new[]
        {
            (p.X - w / 2, p.Y - h - 12), (p.X + 6, p.Y - h - 10), (p.X - w - 6, p.Y - h - 10), (p.X + 12, p.Y - h / 2),
            (p.X - w - 12, p.Y - h / 2), (p.X - w / 2, p.Y + 12), (p.X + 6, p.Y + 10), (p.X - w - 6, p.Y + 10),
            (p.X - w / 2, p.Y - h - 30), (p.X + 24, p.Y - h / 2), (p.X - w - 24, p.Y - h / 2), (p.X - w / 2, p.Y + 30),
        };
        foreach (var c in cand)
        {
            if (c.Item1 < 2 || c.Item2 < 2 || c.Item1 + w > Proj.Width - 2 || c.Item2 + h > Proj.Height - 2) continue;
            if (!Overlaps(placed, c.Item1, c.Item2, w, h)) { bx = c.Item1; by = c.Item2; return true; }
        }
        bx = by = 0;
        return false;
    }

    static P2 Nearest(double x, double y, double w, double h, P2 p)
    {
        double cx = Math.Max(x, Math.Min(x + w, p.X)), cy = Math.Max(y, Math.Min(y + h, p.Y));
        return new P2(cx, cy);
    }

    void DrawSpots(ICanvas g, AppState st, List<SpotItem> spots, List<double> offs, List<(double x, double y, double w, double h)> placed)
    {
        // prima tutti i punti, poi i fumetti sopra
        var todo = new List<(SpotItem s, P2 p)>();
        foreach (var s in spots)
            foreach (var off in offs)
            {
                Proj.Forward(s.Pos.Lat, s.Pos.Lon, out var x, out var y);
                var p = Proj.ToScreen(x + off, y);
                if (Proj.Kind == ProjKind.Azimuthal && Math.Sqrt(x * x + y * y) > 1.0001) continue;
                if (p.X < -5 || p.X > Proj.Width + 5 || p.Y < -5 || p.Y > Proj.Height + 5) continue;
                var border = s.Dir == SpotDir.HeardMe ? ColHeardMe : ColIHeard;
                g.FillEllipse(p.X, p.Y, 3.5, 3.5, SnrScale.Color(s.Snr, s.Source, s.Mode));
                g.StrokeEllipse(p.X, p.Y, 3.5, 3.5, border, 1.4);
                todo.Add((s, p));
            }
        foreach (var t in todo)
        {
            var s = t.s; var p = t.p;
            var border = s.Dir == SpotDir.HeardMe ? ColHeardMe : ColIHeard;
            var fill = SnrScale.Color(s.Snr, s.Source, s.Mode);
            var fg = Lum(fill) > 150 ? 0xFF111827 : 0xFFFFFFFF;
            var snr = SnrScale.Text(s.Snr) + (s.Snr.HasValue ? " dB" : "");
            var call = s.Other;
            var sz1 = g.MeasureText(snr, 12, true);
            var sz2 = st.BubbleCalls ? g.MeasureText(call, 9, false) : new P2(0, 0);
            double w = Math.Max(sz1.X, sz2.X) + 10, h = sz1.Y + (st.BubbleCalls ? sz2.Y : 0) + 5;
            var tip = $"{L.T(s.Dir == SpotDir.HeardMe ? "Mi sente" : "Sento")} {call} — {SnrScale.Text(s.Snr)} dB · {s.Band} {s.Mode} {s.FreqKHz:0.0} kHz · {s.Source} · {s.Time:HH:mm} UTC · {s.Km:N0} km, az {s.Az:0}°" +
                      (s.PosApprox ? " · " + L.T("posizione del paese (cty.dat)") : s.PosSource == "call3" ? " · " + L.T("posizione da CALL3.TXT") : "") + (s.OtherGrid != "" ? " · " + s.OtherGrid : "");
            if (!FindSpot(placed, p, w, h, out var bx, out var by))
            {
                Hits.Add(new HitArea { X = p.X - 5, Y = p.Y - 5, W = 10, H = 10, Text = tip });
                continue;
            }
            placed.Add((bx, by, w, h));
            // coda del fumetto: triangolo dal bordo più vicino al punto
            var n = Nearest(bx, by, w, h, p);
            double dx = p.X - n.X, dy = p.Y - n.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1)
            {
                double ux = -dy / len * 4, uy = dx / len * 4;
                double ix = n.X - dx / len * 3, iy = n.Y - dy / len * 3;
                var tri = new[] { new P2(ix + ux, iy + uy), new P2(p.X, p.Y), new P2(ix - ux, iy - uy) };
                g.FillPolygon(tri, border);
            }
            g.FillRoundRect(bx, by, w, h, 4, fill);
            g.StrokeRoundRect(bx, by, w, h, 4, border, 1.8, s.PosApprox);
            g.DrawText(snr, bx + (w - sz1.X) / 2, by + 2, 12, fg, true);
            if (st.BubbleCalls) g.DrawText(call, bx + (w - sz2.X) / 2, by + 2 + sz1.Y - 1, 9, fg, false);
            Hits.Add(new HitArea { X = bx, Y = by, W = w, H = h, Text = tip });
            Hits.Add(new HitArea { X = p.X - 5, Y = p.Y - 5, W = 10, H = 10, Text = tip });
        }
    }

    /// <summary>Le mie decodifiche: nuvoletta ovale con le bollicine (forma diversa dai fumetti degli spot).</summary>
    void DrawDecodes(ICanvas g, AppState st, Frame f, List<double> offs, List<(double x, double y, double w, double h)> placed)
    {
        foreach (var d in f.Decodes.OrderByDescending(x => x.Snr))
            foreach (var off in offs)
            {
                Proj.Forward(d.Pos.Lat, d.Pos.Lon, out var x, out var y);
                var p = Proj.ToScreen(x + off, y);
                if (Proj.Kind == ProjKind.Azimuthal && Math.Sqrt(x * x + y * y) > 1.0001) continue;
                if (p.X < -5 || p.X > Proj.Width + 5 || p.Y < -5 || p.Y > Proj.Height + 5) continue;
                var fill = SnrScale.Color(d.Snr, "UDP", d.Mode);
                var fg = Lum(fill) > 150 ? 0xFF111827 : 0xFFFFFFFF;
                // rombo sul punto
                g.FillPolygon(new[] { new P2(p.X, p.Y - 5), new P2(p.X + 5, p.Y), new P2(p.X, p.Y + 5), new P2(p.X - 5, p.Y) }, fill);
                g.StrokePolygon(new[] { new P2(p.X, p.Y - 5), new P2(p.X + 5, p.Y), new P2(p.X, p.Y + 5), new P2(p.X - 5, p.Y) }, ColDecode, 1.4);
                var snr = SnrScale.Text(d.Snr);
                var sz1 = g.MeasureText(snr, 12, true);
                var sz2 = st.BubbleCalls ? g.MeasureText(d.Call, 9, false) : new P2(0, 0);
                double w = Math.Max(sz1.X, sz2.X) + 22, h = sz1.Y + (st.BubbleCalls ? sz2.Y : 0) + 8;
                var tip = $"{L.T("Mia decodifica")} {d.Call} — {snr} dB · {d.Band} {d.Mode} · {d.Time:HH:mm} UTC · {d.Km:N0} km, az {d.Az:0}° · \"{d.Message}\"" + (d.PosApprox ? " · " + L.T("posizione del paese (cty.dat)") : "");
                if (!FindSpot(placed, p, w, h + 8, out var bx, out var by))
                {
                    Hits.Add(new HitArea { X = p.X - 6, Y = p.Y - 6, W = 12, H = 12, Text = tip });
                    continue;
                }
                placed.Add((bx, by, w, h + 8));
                // la nuvoletta sta nella parte del riquadro lontana dal punto, le bollicine verso il punto
                bool above = by + (h + 8) / 2 < p.Y;
                double ey = above ? by : by + 8;
                var n = Nearest(bx, ey, w, h, p);
                for (int i = 1; i <= 2; i++)
                {
                    double t = i / 3.0;
                    double cx = p.X + (n.X - p.X) * t, cy = p.Y + (n.Y - p.Y) * t;
                    double r = 1.6 + i * 1.2;
                    g.FillEllipse(cx, cy, r, r, fill);
                    g.StrokeEllipse(cx, cy, r, r, ColDecode, 1.2);
                }
                g.FillEllipse(bx + w / 2, ey + h / 2, w / 2, h / 2, fill);
                g.StrokeEllipse(bx + w / 2, ey + h / 2, w / 2, h / 2, ColDecode, 1.8);
                g.DrawText(snr, bx + (w - sz1.X) / 2, ey + 4, 12, fg, true);
                if (st.BubbleCalls) g.DrawText(d.Call, bx + (w - sz2.X) / 2, ey + 3 + sz1.Y, 9, fg, false);
                Hits.Add(new HitArea { X = bx, Y = ey, W = w, H = h, Text = tip });
                Hits.Add(new HitArea { X = p.X - 6, Y = p.Y - 6, W = 12, H = 12, Text = tip });
            }
    }

    // ------------------------------------------------------------------ legenda

    void DrawLegend(ICanvas g, AppState st, Theme th, double width, double height)
    {
        var items = new List<(uint col, string txt)>
        {
            (0xFFD62839, "≥ 0"), (0xFFF08A3C, "-1…-6"), (0xFFE8D24A, "-7…-12"), (0xFF5BC07A, "-13…-17"), (0xFF27A08A, "-18…-21"), (0xFF3B82C4, "< -21"),
        };
        double x = 10, y = height - 10;
        double boxH = 16 + 16 + 18 + (st.LayerLotw ? 16 : 0);
        double boxW = 8;
        foreach (var it in items) boxW += 14 + g.MeasureText(it.txt, 10, false).X + 8;
        boxW = Math.Max(boxW, 330);
        y -= boxH;
        g.FillRoundRect(x, y, boxW, boxH, 5, 0xE6FFFFFF);
        g.StrokeRoundRect(x, y, boxW, boxH, 5, 0xFFC3CBD5, 1);
        g.DrawText(L.T("SNR dB (RBN CW: dB sul rumore)"), x + 6, y + 2, 10, 0xFF374151, true);
        double cx = x + 6;
        foreach (var it in items)
        {
            g.FillRoundRect(cx, y + 19, 11, 11, 2, it.col);
            g.DrawText(it.txt, cx + 14, y + 18, 10, 0xFF111827, false);
            cx += 14 + g.MeasureText(it.txt, 10, false).X + 8;
        }
        cx = x + 6;
        double ly = y + 34;
        void Item(uint border, string t, bool cloud)
        {
            if (cloud) { g.FillEllipse(cx + 7, ly + 6, 7, 5, 0xFFFFFFFF); g.StrokeEllipse(cx + 7, ly + 6, 7, 5, border, 1.6); }
            else { g.FillRoundRect(cx, ly + 1, 14, 10, 2, 0xFFFFFFFF); g.StrokeRoundRect(cx, ly + 1, 14, 10, 2, border, 1.6); }
            t = L.T(t);
            g.DrawText(t, cx + 18, ly - 1, 10, 0xFF111827, false);
            cx += 18 + g.MeasureText(t, 10, false).X + 12;
        }
        if (st.LayerHeardMe) Item(ColHeardMe, "mi sentono", false);
        if (st.LayerIHeard) Item(ColIHeard, "sento io", false);
        if (st.LayerDecodes) Item(ColDecode, "mie decodifiche (UDP)", true);
        if (st.LayerLotw)
        {
            ly += 16; cx = x + 6;
            void Sq(uint col, string t) { t = L.T(t); g.FillRect(cx, ly + 1, 12, 10, col | 0x60000000); g.DrawText(t, cx + 16, ly - 1, 10, 0xFF111827, false); cx += 16 + g.MeasureText(t, 10, false).X + 10; }
            Sq(LotwLog.ColWorked, "lavorato"); Sq(LotwLog.ColLotw, "LoTW"); Sq(LotwLog.ColQsl, "QSL");
            if (st.Current.SquaresGrey) Sq(LotwLog.ColAllHf, "tutte 80-10 m");
        }
    }
}
