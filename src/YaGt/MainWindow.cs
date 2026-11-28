using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YaGt.Core.Gui;

/// <summary>Finestra principale: barra in alto, livelli e fonti a sinistra, mappa al centro, stato in basso.</summary>
public sealed class MainWindow : Window
{
    readonly AppState _st;
    readonly MapControl _map;
    readonly TextBlock _status, _conn, _hover, _clock;
    readonly DispatcherTimer _tick;
    ComboBox _lotwTag;

    public MainWindow(AppState st)
    {
        _st = st;
        Title = $"YA-GT {AppState.Version} — {st.MyCall} {st.MyLoc}";
        Icon = Ui.WindowIcon();
        Width = 1340; Height = 740; MinWidth = 900; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.B(Ui.Th.Page);
        if (Environment.GetEnvironmentVariable("YAGT_MAXIMIZED") == "1") WindowState = WindowState.Maximized;

        _map = new MapControl { State = st, MapTheme = YaGt.Core.Theme.Get(st.ThemeKey) };
        _status = Ui.Label("", 11, false, Ui.Th.Muted);
        _conn = Ui.Label("", 11, false, Ui.Th.Text);
        _hover = Ui.Label("", 11, false, Ui.Th.Text);
        _clock = Ui.Label("", 11, true, Ui.Th.Text);
        _map.HoverChanged += t => _hover.Text = t;

        var root = new DockPanel();
        var top = BuildToolbar();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top); root.Children.Add(top);
        var bottom = new Border
        {
            Background = Ui.B(Ui.Th.Bar), BorderBrush = Ui.B(Ui.Th.Line), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 3),
            Child = new StackPanel { Children = { new DockPanel { Children = { Docked(_clock, Avalonia.Controls.Dock.Right), _conn } }, _status, _hover } }
        };
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom); root.Children.Add(bottom);
        var left = new Border
        {
            Background = Ui.B(Ui.Th.Panel), BorderBrush = Ui.B(Ui.Th.Line), BorderThickness = new Thickness(0, 0, 1, 0), Width = 250,
            Child = new ScrollViewer { Content = BuildLayers(), Padding = new Thickness(10, 4, 14, 10) }
        };
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left); root.Children.Add(left);
        root.Children.Add(new Border { Child = _map, BorderBrush = Ui.B(Ui.Th.Line), BorderThickness = new Thickness(0) });
        Content = root;

        st.Updated += () => Dispatcher.UIThread.Post(() => { _map.InvalidateVisual(); _status.Text = st.StatusText(); });
        st.ConnectionsChanged += () => Dispatcher.UIThread.Post(() => _conn.Text = st.ConnectionsText());
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Opened += (_, _) =>
        {
            st.Start(); _conn.Text = st.ConnectionsText();
            if (!string.IsNullOrEmpty(st.UpdateMessage)) _hover.Text = "YA-GT " + st.UpdateMessage;
        };
        st.UdpProblems += r => Dispatcher.UIThread.Post(() => { try { new UdpCheckWindow(st, r).ShowDialog(this); } catch { } });
        Updater.Ready += v => Dispatcher.UIThread.Post(() =>
        {
            var w = new Window
            {
                Title = L.T("YA-GT — aggiornamento"), Width = 420, Height = 160, CanResize = false, Icon = Ui.WindowIcon(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.B(Ui.Th.Page),
            };
            var ok = Ui.Button("OK", () => w.Close(), true);
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            w.Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 12,
                Children = { Ui.Label(L.F("È stata installata la versione {0} di YA-GT.\nEntra in funzione al prossimo avvio del programma.", v), 13), ok }
            };
            w.ShowDialog(this);
        });
        Ui.Translate(this);
        StartTour();
    }

    static Control Docked(Control c, Avalonia.Controls.Dock d) { DockPanel.SetDock(c, d); return c; }

    void Tick()
    {
        var now = DateTime.UtcNow;
        var left = _st.NextRefresh - now;
        _clock.Text = L.F("{0:HH:mm:ss} UTC   ·   prossimo aggiornamento tra {1}", now, left.TotalSeconds > 0 ? $"{(int)left.TotalMinutes}:{left.Seconds:00}" : "—");
        if (now.Second == 0) _map.InvalidateVisual();     // greyline: un ridisegno al minuto
    }

    // ------------------------------------------------------------------ barra in alto

    Control BuildToolbar()
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(new Image { Source = Ui.Icon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 4, 0) });
        bar.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Ui.Label("YA-GT", 16, true, Ui.Th.BandFg), Ui.Label(_st.MyCall + "  " + _st.MyLoc, 11, false, Ui.Th.BandFg) }
        });
        bar.Children.Add(new Border { Width = 14 });

        TextBlock L(string t) { var l = Ui.Label(t, 12, false, Ui.Th.BandFg); l.Margin = new Thickness(8, 0, 3, 0); return l; }
        bar.Children.Add(L("Proiezione"));
        bar.Children.Add(Ui.Combo(new[] { "Mercatore", "Azimutale" }, _st.Proj == ProjKind.Azimuthal ? 1 : 0, i =>
        {
            _st.Proj = i == 1 ? ProjKind.Azimuthal : ProjKind.Mercator; _st.SaveSettings(); _map.InvalidateVisual();
        }, 120));
        bar.Children.Add(L("Aggiornamento"));
        bar.Children.Add(Ui.Combo(AppState.RefreshChoices.Select(m => m + " min"), Array.IndexOf(AppState.RefreshChoices, _st.RefreshMin), i => _st.SetRefresh(AppState.RefreshChoices[i]), 90));
        bar.Children.Add(L("Banda"));
        var bands = new[] { "Tutte" }.Concat(Bands.SpotFilter).ToList();
        bar.Children.Add(Ui.Combo(bands, Math.Max(0, bands.IndexOf(_st.BandFilter)), i => { _st.BandFilter = i == 0 ? "" : bands[i]; _st.SaveSettings(); _st.Rebuild(); }, 90));
        bar.Children.Add(L("Spot degli ultimi"));
        bar.Children.Add(Ui.Combo(AppState.AgeChoices.Select(m => m < 60 ? m + " min" : m / 60 + " h"), Array.IndexOf(AppState.AgeChoices, _st.AgeMin), i => { _st.AgeMin = AppState.AgeChoices[i]; _st.SaveSettings(); _st.Rebuild(); }, 85));
        bar.Children.Add(Ui.Button("Aggiorna ora", () => _st.RefreshNow(), true));
        bar.Children.Add(Ui.Button("Vista intera", () => _map.ResetView()));
        bar.Children.Add(Ui.Button("Impostazioni", () => new SettingsWindow(_st, () => _map.InvalidateVisual()).ShowDialog(this)));
        bar.Children.Add(Ui.Button("About", () => new AboutWindow().ShowDialog(this)));
        return new Border { Background = Ui.B(Ui.Th.Band), Padding = new Thickness(8, 5), Child = bar };
    }

    // ------------------------------------------------------------------ livelli e fonti

    Control BuildLayers()
    {
        var p = new StackPanel { Spacing = 2 };
        void Redraw() { _st.SaveSettings(); _map.InvalidateVisual(); }
        void Rebuild() { _st.SaveSettings(); _st.Rebuild(); }

        p.Children.Add(Ui.Section("Livelli della mappa"));
        p.Children.Add(Ui.Check("Statistiche (in alto a destra)", _st.LayerStats, v => { _st.LayerStats = v; Redraw(); }));
        p.Children.Add(Ui.Check("Chi mi sente (spot)", _st.LayerHeardMe, v => { _st.LayerHeardMe = v; Redraw(); }, "Stazioni che ricevono il tuo segnale: fumetto con l'SNR, bordo blu"));
        p.Children.Add(Ui.Check("Chi sento io (spot)", _st.LayerIHeard, v => { _st.LayerIHeard = v; Redraw(); }, "Stazioni che tu ricevi (PSKReporter, tuo skimmer RBN): bordo viola"));
        p.Children.Add(Ui.Check("Le mie decodifiche (UDP)", _st.LayerDecodes, v => { _st.LayerDecodes = v; Redraw(); }, "Decodifiche di WSJT-X/JTDX/MSHV ricevute via UDP: nuvoletta ovale"));
        p.Children.Add(Ui.Check("Nominativo nei fumetti", _st.BubbleCalls, v => { _st.BubbleCalls = v; Redraw(); }));
        p.Children.Add(Ui.Check("Percorsi (cerchio massimo)", _st.LayerPaths, v => { _st.LayerPaths = v; Redraw(); }));
        p.Children.Add(Ui.Check("Griglia Maidenhead", _st.LayerGrid, v => { _st.LayerGrid = v; Redraw(); }));
        p.Children.Add(Ui.Check("Greyline (giorno/notte)", _st.LayerGreyline, v => { _st.LayerGreyline = v; Redraw(); }));

        p.Children.Add(Ui.Check("Propagazione KC2G", _st.LayerKc2g, v => { _st.LayerKc2g = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.InvalidateVisual(); }, "prop.kc2g.com: curve della MUF(3000) o della foF2 e valori delle ionosonde"));
        p.Children.Add(Indent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Ui.Combo(new[] { "MUF(3000)", "foF2" }, _st.Kc2gMetric == "fof2" ? 1 : 0, i => { _st.Kc2gMetric = i == 1 ? "fof2" : "mufd"; _st.SaveSettings(); if (_st.LayerKc2g) _st.RefreshNow(); }, 125),
                Slider(_st.Kc2gAlpha, v => { _st.Kc2gAlpha = v; Redraw(); }),
            }
        }));
        p.Children.Add(Ui.Check("Aurora NOAA (OVATION)", _st.LayerAurora, v => { _st.LayerAurora = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.InvalidateVisual(); }, "Probabilità di aurora del NOAA SWPC, ridisegnata in Mercatore e in azimutale"));
        p.Children.Add(Indent(new StackPanel { Orientation = Orientation.Horizontal, Children = { Ui.Label("Opacità", 11, false, Ui.Th.Muted), Slider(_st.AuroraAlpha, v => { _st.AuroraAlpha = v; Redraw(); }) } }));

        p.Children.Add(Ui.Check("Quadrati (LoTW + log)", _st.LayerLotw, v => { _st.LayerLotw = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _st.Rebuild(); }, "Arancio = lavorato, verde = LoTW, blu = QSL, grigio = confermato su tutte le bande 80-10 m (esclusi i 60 m)"));
        _lotwTag = new ComboBox { FontSize = 12, Width = 190, Margin = new Thickness(2) };
        FillLotwTags();
        _lotwTag.SelectionChanged += (_, _) => { if (_lotwTag.SelectedItem is ComboBoxItem it && it.Tag is string tag) { _st.LotwTag = tag; Rebuild(); } };
        p.Children.Add(Indent(_lotwTag));
        p.Children.Add(Indent(Ui.Check("Solo confermati", _st.LotwOnlyConfirmed, v => { _st.LotwOnlyConfirmed = v; Rebuild(); })));
        p.Children.Add(Indent(Ui.Check("Solo EME (PROP__MODE)", _st.LotwOnlyEme, v => { _st.LotwOnlyEme = v; Rebuild(); })));
        p.Children.Add(Indent(new WrapPanel
        {
            Children =
            {
                Ui.Button("Scarica da LoTW…", async () => { await new LotwWindow(_st).ShowDialog(this); FillLotwTags(); _st.Rebuild(); }),
                Ui.Button("Apri ADIF…", async () => await OpenAdif()),
            }
        }));

        p.Children.Add(Ui.Check("QSO dei log (QLog e altri)", _st.LayerQLog, v => { _st.LayerQLog = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.InvalidateVisual(); }, "Legge qlog.db e gli altri log scelti nelle Impostazioni (copie in sola lettura): quadratini verdi = LoTW, blu = QSL, arancio = lavorato"));
        p.Children.Add(Indent(Ui.Label((string.IsNullOrEmpty(_st.QLogPath) ? "qlog.db: posizione standard di QLog" : _st.QLogPath) + (StationLogs.Lines(_st.LogSources).Any() ? $" · altri log: {StationLogs.Lines(_st.LogSources).Count()}" : ""), 10, false, Ui.Th.Muted)));

        p.Children.Add(Ui.Section("Fonti degli spot"));
        p.Children.Add(Ui.Check("PSKReporter (MQTT)", _st.SrcPsk, v => { _st.SrcPsk = v; _st.SaveSettings(); _st.ApplySources(); }, "mqtt.pskreporter.info, più lo storico dell'ultima ora via HTTP"));
        p.Children.Add(Ui.Check("Reverse Beacon Network", _st.SrcRbn, v => { _st.SrcRbn = v; _st.SaveSettings(); _st.ApplySources(); }, "telnet.reversebeacon.net porte 7000 (CW/RTTY) e 7001 (FT8/FT4)"));
        p.Children.Add(Ui.Check("DM4X (cluster telnet)", _st.SrcDm4x, v => { _st.SrcDm4x = v; _st.SaveSettings(); _st.ApplySources(); }, "dm4x.ddns.net:8500, spot PSKReporter in forma di cluster"));
        p.Children.Add(Ui.Check("DXMAPS (2° cluster)", _st.SrcCl2, v => { _st.SrcCl2 = v; _st.SaveSettings(); _st.ApplySources(); }, "dxmaps.com:7300 (EA6VQ)"));
        p.Children.Add(Indent(Ui.Check("Rete skimmer", _st.Cl2Skimmer, v => _st.SetSkimmer(v), "Invia set/skimmer (o set/noskimmer) al secondo cluster")));
        p.Children.Add(Ui.Check("Decodifiche UDP (WSJT-X/MSHV)", _st.UdpEnabled, v => { _st.UdpEnabled = v; _st.SaveSettings(); _st.ApplySources(); }, "Porta e inoltro UDP in Impostazioni"));

        p.Children.Add(Ui.Section("Tema"));
        p.Children.Add(Ui.Combo(new[] { "Chiaro (YA-NEC)", "Notte" }, _st.ThemeKey == "notte" ? 1 : 0, i => { _st.ThemeKey = i == 1 ? "notte" : "chiaro"; _map.MapTheme = YaGt.Core.Theme.Get(_st.ThemeKey); Redraw(); }, 170));
        return p;
    }

    static Control Indent(Control c) { c.Margin = new Thickness(22, 0, 0, 2); return c; }

    static Slider Slider(int value, Action<int> changed)
    {
        var s = new Slider { Minimum = 10, Maximum = 100, Value = value, Width = 90, Margin = new Thickness(6, -6, 0, -6) };
        ToolTip.SetTip(s, "Opacità");
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) changed((int)s.Value); };
        return s;
    }

    void FillLotwTags()
    {
        var keep = _st.LotwTag;
        _lotwTag.Items.Clear();
        void Add(string tag, string title) { _lotwTag.Items.Add(new ComboBoxItem { Content = L.T(title), Tag = tag }); }
        Add("BAND", "Banda scelta in alto"); Add("ALL", "Tutte le bande"); Add("G:1", "HF 80-10 m"); Add("G:2", "VHF 6 m-1,25 m"); Add("G:3", "UHF e oltre");
        foreach (var b in _st.Lotw.BandsPresent(_st.LogQsos())) Add("B:" + b, b);
        var idx = _lotwTag.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == keep);
        _lotwTag.SelectedIndex = idx >= 0 ? idx : 0;
    }

    async Task OpenAdif()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Apri file ADIF del log"), AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("ADIF") { Patterns = new[] { "*.adi", "*.adif", "*.ADI" } }, FilePickerFileTypes.All },
        });
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (path == null) continue;
            try { _st.Lotw.Merge(File.ReadAllBytes(path), false, Path.GetFileName(path)); } catch (Exception ex) { _status.Text = "ADIF: " + ex.Message; }
        }
        FillLotwTags();
        _st.Rebuild();
    }

    // ------------------------------------------------------------------ prova automatica (GitHub): giro dei livelli con fotografie

    void StartTour()
    {
        var env = Environment.GetEnvironmentVariable("YAGT_TOUR");
        if (string.IsNullOrEmpty(env) || !int.TryParse(env, out var sec) || sec <= 0) return;
        int step = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        t.Tick += (_, _) =>
        {
            step++;
            switch (step)
            {
                case 1: _st.LayerKc2g = true; _st.LayerAurora = true; _st.RefreshNow(); break;
                case 2: _st.Proj = ProjKind.Azimuthal; break;
                case 3: _st.LayerPaths = true; break;
                case 4: _st.Proj = ProjKind.Mercator; _st.LayerLotw = true; _st.Rebuild(); break;
                default: t.Stop(); break;
            }
            _map.InvalidateVisual();
        };
        t.Start();
    }
}
