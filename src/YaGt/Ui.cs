using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace YaGt.Core.Gui;

/// <summary>Piccoli aiuti per costruire le finestre in codice, con i colori di YA-NEC.</summary>
public static class Ui
{
    public static Theme Th = Theme.Get("chiaro");
    public static IBrush B(uint c) => AvaloniaCanvas.Brush(c);

    public static TextBlock Label(string t, double size = 12, bool bold = false, uint? color = null) => new TextBlock
    {
        Text = t, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Foreground = B(color ?? Th.Text),
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Section(string t) => new TextBlock
    {
        Text = t, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = B(Th.Accent), Margin = new Thickness(0, 10, 0, 2),
    };

    public static Button Button(string t, Action click, bool accent = false)
    {
        var b = new Button
        {
            Content = t, Padding = new Thickness(10, 4), Margin = new Thickness(2), FontSize = 12,
            Background = B(accent ? Th.Accent : 0xFFFFFFFF), Foreground = B(accent ? Th.AccentFg : Th.Text),
            BorderBrush = B(Th.Line), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static CheckBox Check(string t, bool value, Action<bool> changed, string tip = null)
    {
        var c = new CheckBox { Content = t, IsChecked = value, FontSize = 12, Foreground = B(Th.Text), Margin = new Thickness(0, 0, 0, -4) };
        c.IsCheckedChanged += (_, _) => changed(c.IsChecked == true);
        if (tip != null) ToolTip.SetTip(c, tip);
        return c;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> changed, double width = 0)
    {
        var c = new ComboBox { FontSize = 12, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = Math.Max(0, selected);
        if (width > 0) c.Width = width;
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
        return c;
    }

    public static Bitmap Icon()
    {
        try
        {
            var asm = typeof(Ui).Assembly;
            var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("ya-gt-256.png", StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
            using (var s = asm.GetManifestResourceStream(n)) return new Bitmap(s);
        }
        catch { return null; }
    }

    public static WindowIcon WindowIcon()
    {
        var b = Icon();
        return b == null ? null : new WindowIcon(b);
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
