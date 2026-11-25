using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace YaGt.Core.Gui;

/// <summary>La mappa: rotella = zoom sul punto, trascinamento = spostamento, doppio clic = vista intera.</summary>
public sealed class MapControl : Control
{
    public readonly MapRenderer Renderer = new MapRenderer();
    public AppState State;
    public Theme MapTheme = YaGt.Core.Theme.Get("chiaro");
    /// <summary>Testo dell'elemento sotto il mouse ("" se niente).</summary>
    public event Action<string> HoverChanged;

    Point _last;
    bool _drag;
    string _hover = "";

    public MapControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (State == null) return;
        try
        {
            Renderer.Render(new AvaloniaCanvas(context), State, MapTheme, Bounds.Width, Bounds.Height);
        }
        catch (Exception ex) { ErrorLog.Write("disegno della mappa", ex); }
    }

    public void ResetView()
    {
        Renderer.Proj.Width = Bounds.Width; Renderer.Proj.Height = Bounds.Height;
        Renderer.Proj.Fit();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var p = Renderer.Proj;
        bool first = p.Width < 10 || e.PreviousSize.Width < 10;
        p.Width = e.NewSize.Width; p.Height = e.NewSize.Height;
        if (first) p.Fit(); else p.Clamp();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pt = e.GetPosition(this);
        Renderer.Proj.ZoomAt(pt.X, pt.Y, e.Delta.Y > 0 ? 1.25 : 0.8);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2) { ResetView(); return; }
        _last = e.GetPosition(this);
        _drag = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        if (_drag)
        {
            Renderer.Proj.Pan(pt.X - _last.X, pt.Y - _last.Y);
            _last = pt;
            InvalidateVisual();
            return;
        }
        string text = "";
        for (int i = Renderer.Hits.Count - 1; i >= 0; i--)
            if (Renderer.Hits[i].Contains(pt.X, pt.Y)) { text = Renderer.Hits[i].Text; break; }
        if (text == "" && State != null)
        {
            var p = Renderer.Proj;
            p.ToWorld(pt.X, pt.Y, out var wx, out var wy);
            if (p.Kind == ProjKind.Mercator) wx = ((wx + 1) % 2 + 2) % 2 - 1;
            if (p.Inverse(wx, wy, out var lat, out var lon))
            {
                var ll = new LatLon(lat, lon);
                text = $"{Locator.FromLatLon(lat, lon)}  {lat:0.00}°, {lon:0.00}°  ·  {Geo.DistKm(State.Home, ll):N0} km, az {Geo.Azimuth(State.Home, ll):0}°";
            }
        }
        if (text != _hover)
        {
            _hover = text;
            ToolTip.SetTip(this, text.Length > 0 && Renderer.Hits.Any(h => h.Text == text) ? text : null);
            ToolTip.SetIsOpen(this, text.Length > 0 && Renderer.Hits.Any(h => h.Text == text));
            HoverChanged?.Invoke(text);
        }
    }
}
