using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace YaGt.Core.Win7;

/// <summary>ICanvas sopra GDI+ (System.Drawing), per la versione Windows 7.</summary>
public sealed class GdiCanvas : ICanvas
{
    readonly Graphics _g;
    readonly Stack<GraphicsState> _states = new Stack<GraphicsState>();
    static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
    static Bitmap _bmp;
    static uint[] _bmpSrc;

    public GdiCanvas(Graphics g)
    {
        _g = g;
        _g.SmoothingMode = SmoothingMode.AntiAlias;
        _g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        _g.PixelOffsetMode = PixelOffsetMode.Half;
    }

    static Color C(uint c) => Color.FromArgb(unchecked((int)c));

    static Font F(double size, bool bold)
    {
        var key = size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + (bold ? "b" : "");
        lock (Fonts)
        {
            if (!Fonts.TryGetValue(key, out var f))
                Fonts[key] = f = new Font("Segoe UI", (float)size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            return f;
        }
    }

    static PointF[] Pts(P2[] p)
    {
        var r = new PointF[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = new PointF((float)p[i].X, (float)p[i].Y);
        return r;
    }

    static GraphicsPath Round(double x, double y, double w, double h, double r)
    {
        var path = new GraphicsPath();
        float d = (float)Math.Max(0.5, Math.Min(r * 2, Math.Min(w, h)));
        float fx = (float)x, fy = (float)y, fw = (float)Math.Max(1, w), fh = (float)Math.Max(1, h);
        path.AddArc(fx, fy, d, d, 180, 90);
        path.AddArc(fx + fw - d, fy, d, d, 270, 90);
        path.AddArc(fx + fw - d, fy + fh - d, d, d, 0, 90);
        path.AddArc(fx, fy + fh - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void FillRect(double x, double y, double w, double h, uint argb)
    {
        using (var b = new SolidBrush(C(argb))) _g.FillRectangle(b, (float)x, (float)y, (float)Math.Max(0, w), (float)Math.Max(0, h));
    }

    public void FillPolygon(P2[] pts, uint argb)
    {
        if (pts == null || pts.Length < 3) return;
        using (var b = new SolidBrush(C(argb))) _g.FillPolygon(b, Pts(pts));
    }

    public void StrokePolygon(P2[] pts, uint argb, double width)
    {
        if (pts == null || pts.Length < 2) return;
        using (var p = new Pen(C(argb), (float)width)) _g.DrawPolygon(p, Pts(pts));
    }

    public void DrawPolyline(P2[] pts, uint argb, double width, bool dashed = false)
    {
        if (pts == null || pts.Length < 2) return;
        using (var p = new Pen(C(argb), (float)width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            if (dashed) p.DashPattern = new[] { 3f, 2f };
            _g.DrawLines(p, Pts(pts));
        }
    }

    public void DrawLine(double x1, double y1, double x2, double y2, uint argb, double width)
    {
        using (var p = new Pen(C(argb), (float)width)) _g.DrawLine(p, (float)x1, (float)y1, (float)x2, (float)y2);
    }

    public void FillEllipse(double cx, double cy, double rx, double ry, uint argb)
    {
        using (var b = new SolidBrush(C(argb))) _g.FillEllipse(b, (float)(cx - rx), (float)(cy - ry), (float)(2 * rx), (float)(2 * ry));
    }

    public void StrokeEllipse(double cx, double cy, double rx, double ry, uint argb, double width)
    {
        using (var p = new Pen(C(argb), (float)width)) _g.DrawEllipse(p, (float)(cx - rx), (float)(cy - ry), (float)(2 * rx), (float)(2 * ry));
    }

    public void FillRoundRect(double x, double y, double w, double h, double r, uint argb)
    {
        using (var path = Round(x, y, w, h, r))
        using (var b = new SolidBrush(C(argb))) _g.FillPath(b, path);
    }

    public void StrokeRoundRect(double x, double y, double w, double h, double r, uint argb, double width, bool dashed = false)
    {
        using (var path = Round(x, y, w, h, r))
        using (var p = new Pen(C(argb), (float)width))
        {
            if (dashed) p.DashPattern = new[] { 3f, 2f };
            _g.DrawPath(p, path);
        }
    }

    public void DrawText(string s, double x, double y, double size, uint argb, bool bold = false)
    {
        using (var b = new SolidBrush(C(argb))) _g.DrawString(s ?? "", F(size, bold), b, (float)x, (float)y, StringFormat.GenericTypographic);
    }

    public P2 MeasureText(string s, double size, bool bold = false)
    {
        var f = F(size, bold);
        var sz = _g.MeasureString(s ?? "", f, PointF.Empty, StringFormat.GenericTypographic);
        return new P2(sz.Width + 1, f.GetHeight(_g));
    }

    public void DrawImage(uint[] argb, int w, int h, double x, double y, double dw, double dh)
    {
        if (argb == null || w <= 0 || h <= 0) return;
        if (_bmp == null || _bmp.Width != w || _bmp.Height != h || !ReferenceEquals(_bmpSrc, argb))
        {
            if (_bmp == null || _bmp.Width != w || _bmp.Height != h) { _bmp?.Dispose(); _bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb); }
            var data = _bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int yy = 0; yy < h; yy++)
                {
                    Buffer.BlockCopy(argb, yy * w * 4, row, 0, w * 4);
                    Marshal.Copy(row, 0, data.Scan0 + yy * data.Stride, w);
                }
            }
            finally { _bmp.UnlockBits(data); }
            _bmpSrc = argb;
        }
        var im = _g.InterpolationMode;
        _g.InterpolationMode = InterpolationMode.Bilinear;
        _g.DrawImage(_bmp, new RectangleF((float)x, (float)y, (float)dw, (float)dh), new RectangleF(0, 0, w, h), GraphicsUnit.Pixel);
        _g.InterpolationMode = im;
    }

    public void PushClipEllipse(double cx, double cy, double rx, double ry)
    {
        _states.Push(_g.Save());
        using (var path = new GraphicsPath())
        {
            path.AddEllipse((float)(cx - rx), (float)(cy - ry), (float)(2 * rx), (float)(2 * ry));
            _g.SetClip(path, CombineMode.Intersect);
        }
    }

    public void PopClip() { if (_states.Count > 0) _g.Restore(_states.Pop()); }
}
