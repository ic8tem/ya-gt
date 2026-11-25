using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

namespace YaGt.Core.Gui;

/// <summary>ICanvas sopra il DrawingContext di Avalonia.</summary>
public sealed class AvaloniaCanvas : ICanvas
{
    readonly DrawingContext _g;
    readonly Stack<IDisposable> _clips = new Stack<IDisposable>();
    static readonly Dictionary<uint, IImmutableSolidColorBrush> Brushes = new Dictionary<uint, IImmutableSolidColorBrush>();
    static readonly Typeface Regular = new Typeface("Segoe UI, Inter, DejaVu Sans, Arial", FontStyle.Normal, FontWeight.Normal);
    static readonly Typeface Bold = new Typeface("Segoe UI, Inter, DejaVu Sans, Arial", FontStyle.Normal, FontWeight.Bold);
    static readonly ImmutableDashStyle Dash = new ImmutableDashStyle(new double[] { 4, 3 }, 0);
    static WriteableBitmap _bmp;
    static uint[] _bmpSrc;

    public AvaloniaCanvas(DrawingContext g) { _g = g; }

    public static IImmutableSolidColorBrush Brush(uint c)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(c, out var b))
            {
                b = new ImmutableSolidColorBrush(Color.FromUInt32(c));
                if (Brushes.Count > 4000) Brushes.Clear();
                Brushes[c] = b;
            }
            return b;
        }
    }

    static IPen Pen(uint c, double w, bool dashed = false) => new ImmutablePen(Brush(c), w, dashed ? Dash : null, PenLineCap.Round, PenLineJoin.Round);

    static StreamGeometry Geometry(P2[] pts, bool closed)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), closed);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(new Point(pts[i].X, pts[i].Y));
            ctx.EndFigure(closed);
        }
        return geo;
    }

    public void FillRect(double x, double y, double w, double h, uint argb) => _g.FillRectangle(Brush(argb), new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));

    public void FillPolygon(P2[] pts, uint argb)
    {
        if (pts == null || pts.Length < 3) return;
        _g.DrawGeometry(Brush(argb), null, Geometry(pts, true));
    }

    public void StrokePolygon(P2[] pts, uint argb, double width)
    {
        if (pts == null || pts.Length < 2) return;
        _g.DrawGeometry(null, Pen(argb, width), Geometry(pts, true));
    }

    public void DrawPolyline(P2[] pts, uint argb, double width, bool dashed = false)
    {
        if (pts == null || pts.Length < 2) return;
        _g.DrawGeometry(null, Pen(argb, width, dashed), Geometry(pts, false));
    }

    public void DrawLine(double x1, double y1, double x2, double y2, uint argb, double width) => _g.DrawLine(Pen(argb, width), new Point(x1, y1), new Point(x2, y2));

    public void FillEllipse(double cx, double cy, double rx, double ry, uint argb) => _g.DrawEllipse(Brush(argb), null, new Point(cx, cy), rx, ry);

    public void StrokeEllipse(double cx, double cy, double rx, double ry, uint argb, double width) => _g.DrawEllipse(null, Pen(argb, width), new Point(cx, cy), rx, ry);

    public void FillRoundRect(double x, double y, double w, double h, double r, uint argb) => _g.DrawRectangle(Brush(argb), null, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), r, r);

    public void StrokeRoundRect(double x, double y, double w, double h, double r, uint argb, double width, bool dashed = false) =>
        _g.DrawRectangle(null, Pen(argb, width, dashed), new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), r, r);

    static FormattedText Text(string s, double size, bool bold, uint argb) =>
        new FormattedText(s ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Regular, size, Brush(argb));

    public void DrawText(string s, double x, double y, double size, uint argb, bool bold = false) => _g.DrawText(Text(s, size, bold, argb), new Point(x, y));

    public P2 MeasureText(string s, double size, bool bold = false)
    {
        var t = Text(s, size, bold, 0xFF000000);
        return new P2(t.Width, t.Height);
    }

    public void DrawImage(uint[] argb, int w, int h, double x, double y, double dw, double dh)
    {
        if (argb == null || w <= 0 || h <= 0) return;
        if (_bmp == null || _bmp.PixelSize.Width != w || _bmp.PixelSize.Height != h || !ReferenceEquals(_bmpSrc, argb))
        {
            if (_bmp == null || _bmp.PixelSize.Width != w || _bmp.PixelSize.Height != h)
                _bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = _bmp.Lock())
            {
                var row = new int[w];
                for (int yy = 0; yy < h; yy++)
                {
                    Buffer.BlockCopy(argb, yy * w * 4, row, 0, w * 4);
                    Marshal.Copy(row, 0, fb.Address + yy * fb.RowBytes, w);
                }
            }
            _bmpSrc = argb;
        }
        using (_g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            _g.DrawImage(_bmp, new Rect(0, 0, w, h), new Rect(x, y, dw, dh));
    }

    public void PushClipEllipse(double cx, double cy, double rx, double ry) =>
        _clips.Push(_g.PushGeometryClip(new EllipseGeometry(new Rect(cx - rx, cy - ry, 2 * rx, 2 * ry))));

    public void PopClip() { if (_clips.Count > 0) _clips.Pop().Dispose(); }
}
