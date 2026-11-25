using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace YaGt.Core.Win7;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ErrorLog.Fatal(ex); };
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write("attività", e.Exception); e.SetObserved(); };
        Application.ThreadException += (_, e) => ErrorLog.Write("finestra", e.Exception);
        // Windows 7: TLS 1.2 va chiesto esplicitamente (LoTW, NOAA, KC2G, PSKReporter usano HTTPS)
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var st = new AppState();
            bool skip = Environment.GetEnvironmentVariable("YAGT_SKIPSTART") == "1" && st.MyCall != "" && Locator.IsValid6(st.MyLoc);
            if (!skip)
                using (var f = new StartForm(st))
                    if (f.ShowDialog() != DialogResult.OK) return;
            using (var m = new MainForm(st)) Application.Run(m);
            st.Shutdown();
        }
        catch (Exception ex) { ErrorLog.Fatal(ex); }
    }
}

/// <summary>Colori e aiuti per le finestre WinForms (tema di YA-NEC).</summary>
public static class W
{
    public static Theme Th = Theme.Get("chiaro");
    public static Color C(uint c) => Color.FromArgb(unchecked((int)c));
    public static readonly Font Ui = new Font("Segoe UI", 9f);
    public static readonly Font UiBold = new Font("Segoe UI", 9f, FontStyle.Bold);

    public static Icon AppIcon()
    {
        try
        {
            var asm = typeof(W).Assembly;
            var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("ya-gt.ico", StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
            using (var s = asm.GetManifestResourceStream(n)) return new Icon(s);
        }
        catch { return null; }
    }

    public static Image Logo()
    {
        try
        {
            var asm = typeof(W).Assembly;
            var n = asm.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("ya-gt-256.png", StringComparison.OrdinalIgnoreCase));
            if (n == null) return null;
            using (var s = asm.GetManifestResourceStream(n)) return new Bitmap(Image.FromStream(s));
        }
        catch { return null; }
    }

    public static Label Label(string t, bool bold = false, uint? color = null, int width = 0)
    {
        var l = new Label { Text = t, AutoSize = width == 0, Font = bold ? UiBold : Ui, ForeColor = C(color ?? Th.Text), Margin = new Padding(3, 6, 3, 3) };
        if (width > 0) { l.Width = width; l.Height = 32; }
        return l;
    }

    public static Button Button(string t, Action click, bool accent = false)
    {
        var b = new Button { Text = t, AutoSize = true, Font = Ui, FlatStyle = FlatStyle.Flat, BackColor = C(accent ? Th.Accent : 0xFFFFFFFF), ForeColor = C(accent ? Th.AccentFg : Th.Text), Margin = new Padding(3, 2, 3, 2), Padding = new Padding(6, 1, 6, 1) };
        b.FlatAppearance.BorderColor = C(Th.Line);
        b.Click += (_, _) => click();
        return b;
    }

    public static CheckBox Check(string t, bool value, Action<bool> changed, ToolTip tip = null, string tipText = null)
    {
        var c = new CheckBox { Text = t, Checked = value, AutoSize = true, Font = Ui, ForeColor = C(Th.Text), Margin = new Padding(3, 1, 3, 1) };
        c.CheckedChanged += (_, _) => changed(c.Checked);
        if (tip != null && tipText != null) tip.SetToolTip(c, tipText);
        return c;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> changed, int width)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Ui, Width = width, Margin = new Padding(3, 3, 3, 3) };
        foreach (var i in items) c.Items.Add(i);
        if (c.Items.Count > 0) c.SelectedIndex = Math.Max(0, Math.Min(c.Items.Count - 1, selected));
        c.SelectedIndexChanged += (_, _) => { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
        return c;
    }

    public static void Open(string url) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
}
