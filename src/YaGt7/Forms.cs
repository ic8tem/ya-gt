using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace YaGt.Core.Win7;

/// <summary>La mappa in WinForms (stesso disegno della versione Avalonia).</summary>
public sealed class MapPanel : Control
{
    public readonly MapRenderer Renderer = new MapRenderer();
    public AppState State;
    public Theme Theme = Theme.Get("chiaro");
    public event Action<string> HoverChanged;
    readonly ToolTip _tip = new ToolTip { InitialDelay = 200, ReshowDelay = 100, AutoPopDelay = 15000 };
    Point _last;
    bool _drag;
    string _hover = "";

    public MapPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (State == null) return;
        try { Renderer.Render(new GdiCanvas(e.Graphics), State, Theme, ClientSize.Width, ClientSize.Height); }
        catch (Exception ex) { ErrorLog.Write("disegno della mappa", ex); }
    }

    public void ResetView()
    {
        Renderer.Proj.Width = ClientSize.Width; Renderer.Proj.Height = ClientSize.Height;
        Renderer.Proj.Fit();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var p = Renderer.Proj;
        bool first = p.Width < 10 || p.Width == 800 && p.Height == 600;
        p.Width = ClientSize.Width; p.Height = ClientSize.Height;
        if (first && ClientSize.Width > 10) p.Fit(); else p.Clamp();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Renderer.Proj.ZoomAt(e.X, e.Y, e.Delta > 0 ? 1.25 : 0.8);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); ResetView(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) { _drag = true; _last = e.Location; Capture = true; }
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; Capture = false; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
        {
            Renderer.Proj.Pan(e.X - _last.X, e.Y - _last.Y);
            _last = e.Location;
            Invalidate();
            return;
        }
        string text = ""; bool hit = false;
        for (int i = Renderer.Hits.Count - 1; i >= 0; i--)
            if (Renderer.Hits[i].Contains(e.X, e.Y)) { text = Renderer.Hits[i].Text; hit = true; break; }
        if (!hit && State != null)
        {
            var p = Renderer.Proj;
            p.ToWorld(e.X, e.Y, out var wx, out var wy);
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
            if (hit) _tip.SetToolTip(this, text); else _tip.SetToolTip(this, null);
            HoverChanged?.Invoke(text);
        }
    }
}

/// <summary>Finestra di avvio: nominativo e locatore a 6 caratteri.</summary>
public sealed class StartForm : Form
{
    readonly AppState _st;
    readonly TextBox _call, _loc;
    readonly Label _err;

    public StartForm(AppState st)
    {
        _st = st;
        Text = "YA-GT — avvio";
        Icon = W.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 290);
        BackColor = W.C(W.Th.Page);
        Font = W.Ui;

        var head = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = W.C(W.Th.Band) };
        head.Controls.Add(new PictureBox { Image = W.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(12, 10, 44, 44) });
        head.Controls.Add(new Label { Text = "YA-GT", Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = W.C(W.Th.BandFg), AutoSize = true, Location = new Point(64, 8) });
        head.Controls.Add(new Label { Text = "Spot, LoTW, QLog e decodifiche sulla mappa — di Costantino IC8TEM", ForeColor = W.C(W.Th.BandFg), AutoSize = true, Location = new Point(66, 38) });
        Controls.Add(head);

        Controls.Add(new Label { Text = "Nominativo", AutoSize = true, Location = new Point(18, 88) });
        _call = new TextBox { Text = st.MyCall != "" ? st.MyCall : "IC8TEM", Font = new Font("Segoe UI", 12f), Location = new Point(190, 84), Width = 200, CharacterCasing = CharacterCasing.Upper, MaxLength = 16 };
        Controls.Add(_call);
        Controls.Add(new Label { Text = "Locatore (6 caratteri)", AutoSize = true, Location = new Point(18, 128) });
        _loc = new TextBox { Text = Locator.IsValid6(st.MyLoc) ? st.MyLoc : "JN70cn", Font = new Font("Segoe UI", 12f), Location = new Point(190, 124), Width = 120, MaxLength = 6 };
        Controls.Add(_loc);
        Controls.Add(new Label { Text = "Il locatore fa da centro della mappa azimutale e serve a calcolare distanza e azimut di ogni spot.", ForeColor = W.C(W.Th.Muted), Location = new Point(18, 162), Size = new Size(420, 34) });
        _err = new Label { ForeColor = Color.Firebrick, Location = new Point(18, 198), Size = new Size(420, 20) };
        Controls.Add(_err);
        var ok = new Button { Text = "Avvia", Location = new Point(352, 240), Size = new Size(90, 30), BackColor = W.C(W.Th.Accent), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "Esci", Location = new Point(254, 240), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }

    void Accept()
    {
        var call = _call.Text.Trim().ToUpperInvariant();
        var loc = _loc.Text.Trim();
        if (call.Length < 3 || !call.Any(char.IsDigit) || !call.Any(char.IsLetter) || call.Any(ch => !char.IsLetterOrDigit(ch) && ch != '/'))
        { _err.Text = "Nominativo non valido."; return; }
        if (!Locator.IsValid6(loc)) { _err.Text = "Il locatore deve avere 6 caratteri, es. JN70cn."; return; }
        _st.SetStation(call, loc);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Finestra principale della versione Windows 7.</summary>
public sealed class MainForm : Form
{
    readonly AppState _st;
    readonly MapPanel _map;
    readonly Label _status, _conn, _hover, _clock;
    readonly Timer _tick;
    readonly ToolTip _tips = new ToolTip();
    ComboBox _lotwTag;
    List<string> _lotwTags = new List<string>();

    public MainForm(AppState st)
    {
        _st = st;
        Text = $"YA-GT {AppState.Version} (Windows 7) — {st.MyCall} {st.MyLoc}";
        Icon = W.AppIcon();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1340, 740);
        MinimumSize = new Size(900, 560);
        BackColor = W.C(W.Th.Page);
        Font = W.Ui;
        if (Environment.GetEnvironmentVariable("YAGT_MAXIMIZED") == "1") WindowState = FormWindowState.Maximized;

        _map = new MapPanel { State = st, Theme = Theme.Get(st.ThemeKey), Dock = DockStyle.Fill };
        _status = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 18, ForeColor = W.C(W.Th.Muted) };
        _conn = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = 18 };
        _clock = new Label { AutoSize = true, Dock = DockStyle.Right, Font = W.UiBold };
        _hover = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 18 };
        _map.HoverChanged += t => _hover.Text = t;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = W.C(W.Th.Bar), Padding = new Padding(8, 3, 8, 3) };
        var row1 = new Panel { Dock = DockStyle.Top, Height = 18 };
        row1.Controls.Add(_conn); row1.Controls.Add(_clock);
        bottom.Controls.Add(_hover); bottom.Controls.Add(_status); bottom.Controls.Add(row1);

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 262, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = W.C(W.Th.Panel), Padding = new Padding(8, 4, 4, 8) };
        BuildLayers(left);

        Controls.Add(_map);
        Controls.Add(left);
        Controls.Add(BuildToolbar());
        Controls.Add(bottom);

        st.Updated += () => { try { BeginInvoke((Action)(() => { _map.Invalidate(); _status.Text = st.StatusText(); })); } catch { } };
        st.ConnectionsChanged += () => { try { BeginInvoke((Action)(() => _conn.Text = st.ConnectionsText())); } catch { } };
        _tick = new Timer { Interval = 1000 };
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Shown += (_, _) =>
        {
            st.Start(); _conn.Text = st.ConnectionsText(); StartTour();
            if (!string.IsNullOrEmpty(st.UpdateMessage)) _hover.Text = "YA-GT " + st.UpdateMessage;
        };
        Updater.Ready += v => { try { BeginInvoke((Action)(() => MessageBox.Show(this, $"È stata installata la versione {v} di YA-GT.\nEntra in funzione al prossimo avvio del programma.", "YA-GT — aggiornamento", MessageBoxButtons.OK, MessageBoxIcon.Information))); } catch { } };
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        var left = _st.NextRefresh - now;
        _clock.Text = $"{now:HH:mm:ss} UTC   ·   prossimo aggiornamento tra {(left.TotalSeconds > 0 ? $"{(int)left.TotalMinutes}:{left.Seconds:00}" : "—")}";
        if (now.Second == 0) _map.Invalidate();
    }

    Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = W.C(W.Th.Band), Padding = new Padding(6, 4, 6, 2), WrapContents = true, AutoScroll = false };
        bar.Controls.Add(new PictureBox { Image = W.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(28, 28), Margin = new Padding(2, 2, 4, 2) });
        bar.Controls.Add(new Label { Text = "YA-GT  " + _st.MyCall + " " + _st.MyLoc, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = W.C(W.Th.BandFg), AutoSize = true, Margin = new Padding(2, 8, 14, 2) });
        Label L(string t) => new Label { Text = t, ForeColor = W.C(W.Th.BandFg), AutoSize = true, Margin = new Padding(3, 8, 2, 2) };
        bar.Controls.Add(L("Proiezione"));
        bar.Controls.Add(W.Combo(new[] { "Mercatore", "Azimutale" }, _st.Proj == ProjKind.Azimuthal ? 1 : 0, i => { _st.Proj = i == 1 ? ProjKind.Azimuthal : ProjKind.Mercator; _st.SaveSettings(); _map.Invalidate(); }, 100));
        bar.Controls.Add(L("Aggiornamento"));
        bar.Controls.Add(W.Combo(AppState.RefreshChoices.Select(m => m + " min"), Array.IndexOf(AppState.RefreshChoices, _st.RefreshMin), i => _st.SetRefresh(AppState.RefreshChoices[i]), 70));
        bar.Controls.Add(L("Banda"));
        var bands = new[] { "Tutte" }.Concat(Bands.SpotFilter).ToList();
        bar.Controls.Add(W.Combo(bands, Math.Max(0, bands.IndexOf(_st.BandFilter)), i => { _st.BandFilter = i == 0 ? "" : bands[i]; _st.SaveSettings(); _st.Rebuild(); }, 70));
        bar.Controls.Add(L("Spot degli ultimi"));
        bar.Controls.Add(W.Combo(AppState.AgeChoices.Select(m => m < 60 ? m + " min" : m / 60 + " h"), Array.IndexOf(AppState.AgeChoices, _st.AgeMin), i => { _st.AgeMin = AppState.AgeChoices[i]; _st.SaveSettings(); _st.Rebuild(); }, 70));
        bar.Controls.Add(W.Button("Aggiorna ora", () => _st.RefreshNow(), true));
        bar.Controls.Add(W.Button("Vista intera", () => _map.ResetView()));
        bar.Controls.Add(W.Button("Impostazioni", () => { using (var f = new SettingsForm(_st)) f.ShowDialog(this); _map.Invalidate(); }));
        bar.Controls.Add(W.Button("About", () => { using (var f = new AboutForm()) f.ShowDialog(this); }));
        return bar;
    }

    void BuildLayers(FlowLayoutPanel p)
    {
        void Redraw() { _st.SaveSettings(); _map.Invalidate(); }
        void Rebuild() { _st.SaveSettings(); _st.Rebuild(); }
        Label Sec(string t) => new Label { Text = t, Font = W.UiBold, ForeColor = W.C(W.Th.Accent), AutoSize = true, Margin = new Padding(3, 10, 3, 2) };
        Control Ind(Control c) { c.Margin = new Padding(22, c.Margin.Top, 3, c.Margin.Bottom); return c; }

        p.Controls.Add(Sec("Livelli della mappa"));
        p.Controls.Add(W.Check("Statistiche (in alto a destra)", _st.LayerStats, v => { _st.LayerStats = v; Redraw(); }));
        p.Controls.Add(W.Check("Chi mi sente (spot)", _st.LayerHeardMe, v => { _st.LayerHeardMe = v; Redraw(); }, _tips, "Stazioni che ricevono il tuo segnale: fumetto con l'SNR, bordo blu"));
        p.Controls.Add(W.Check("Chi sento io (spot)", _st.LayerIHeard, v => { _st.LayerIHeard = v; Redraw(); }, _tips, "Stazioni che tu ricevi: bordo viola"));
        p.Controls.Add(W.Check("Le mie decodifiche (UDP)", _st.LayerDecodes, v => { _st.LayerDecodes = v; Redraw(); }, _tips, "Decodifiche di WSJT-X/JTDX/MSHV via UDP: nuvoletta ovale"));
        p.Controls.Add(W.Check("Nominativo nei fumetti", _st.BubbleCalls, v => { _st.BubbleCalls = v; Redraw(); }));
        p.Controls.Add(W.Check("Percorsi (cerchio massimo)", _st.LayerPaths, v => { _st.LayerPaths = v; Redraw(); }));
        p.Controls.Add(W.Check("Griglia Maidenhead", _st.LayerGrid, v => { _st.LayerGrid = v; Redraw(); }));
        p.Controls.Add(W.Check("Greyline (giorno/notte)", _st.LayerGreyline, v => { _st.LayerGreyline = v; Redraw(); }));
        p.Controls.Add(W.Check("Propagazione KC2G", _st.LayerKc2g, v => { _st.LayerKc2g = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.Invalidate(); }, _tips, "prop.kc2g.com: curve della MUF(3000) o della foF2"));
        p.Controls.Add(Ind(W.Combo(new[] { "MUF(3000)", "foF2" }, _st.Kc2gMetric == "fof2" ? 1 : 0, i => { _st.Kc2gMetric = i == 1 ? "fof2" : "mufd"; _st.SaveSettings(); if (_st.LayerKc2g) _st.RefreshNow(); }, 110)));
        p.Controls.Add(Ind(Track(_st.Kc2gAlpha, v => { _st.Kc2gAlpha = v; Redraw(); })));
        p.Controls.Add(W.Check("Aurora NOAA (OVATION)", _st.LayerAurora, v => { _st.LayerAurora = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.Invalidate(); }));
        p.Controls.Add(Ind(Track(_st.AuroraAlpha, v => { _st.AuroraAlpha = v; Redraw(); })));
        p.Controls.Add(W.Check("Quadrati (LoTW + QLog)", _st.LayerLotw, v => { _st.LayerLotw = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _st.Rebuild(); }, _tips, "Arancio = lavorato, verde = LoTW, blu = QSL, grigio = confermato su tutte le bande 80-10 m (esclusi i 60 m)"));
        _lotwTag = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        FillLotwTags();
        _lotwTag.SelectedIndexChanged += (_, _) => { if (_lotwTag.SelectedIndex >= 0 && _lotwTag.SelectedIndex < _lotwTags.Count) { _st.LotwTag = _lotwTags[_lotwTag.SelectedIndex]; Rebuild(); } };
        p.Controls.Add(Ind(_lotwTag));
        p.Controls.Add(Ind(W.Check("Solo confermati", _st.LotwOnlyConfirmed, v => { _st.LotwOnlyConfirmed = v; Rebuild(); })));
        p.Controls.Add(Ind(W.Check("Solo EME (PROP_MODE)", _st.LotwOnlyEme, v => { _st.LotwOnlyEme = v; Rebuild(); })));
        var lb = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        lb.Controls.Add(W.Button("Scarica da LoTW…", () => { using (var f = new LotwForm(_st)) f.ShowDialog(this); FillLotwTags(); _st.Rebuild(); }));
        lb.Controls.Add(W.Button("Apri ADIF…", OpenAdif));
        p.Controls.Add(Ind(lb));
        p.Controls.Add(W.Check("QSO del log QLog", _st.LayerQLog, v => { _st.LayerQLog = v; _st.SaveSettings(); if (v) _st.RefreshNow(); else _map.Invalidate(); }, _tips, "Legge qlog.db (copia in sola lettura)"));

        p.Controls.Add(Sec("Fonti degli spot"));
        p.Controls.Add(W.Check("PSKReporter (MQTT)", _st.SrcPsk, v => { _st.SrcPsk = v; _st.SaveSettings(); _st.ApplySources(); }));
        p.Controls.Add(W.Check("Reverse Beacon Network", _st.SrcRbn, v => { _st.SrcRbn = v; _st.SaveSettings(); _st.ApplySources(); }));
        p.Controls.Add(W.Check("DM4X (cluster telnet)", _st.SrcDm4x, v => { _st.SrcDm4x = v; _st.SaveSettings(); _st.ApplySources(); }));
        p.Controls.Add(W.Check("DXMAPS (2° cluster)", _st.SrcCl2, v => { _st.SrcCl2 = v; _st.SaveSettings(); _st.ApplySources(); }));
        p.Controls.Add(Ind(W.Check("Rete skimmer", _st.Cl2Skimmer, v => _st.SetSkimmer(v))));
        p.Controls.Add(W.Check("Decodifiche UDP (WSJT-X/MSHV)", _st.UdpEnabled, v => { _st.UdpEnabled = v; _st.SaveSettings(); _st.ApplySources(); }));
        p.Controls.Add(Sec("Tema"));
        p.Controls.Add(W.Combo(new[] { "Chiaro (YA-NEC)", "Notte" }, _st.ThemeKey == "notte" ? 1 : 0, i => { _st.ThemeKey = i == 1 ? "notte" : "chiaro"; _map.Theme = Theme.Get(_st.ThemeKey); Redraw(); }, 170));
    }

    TrackBar Track(int value, Action<int> changed)
    {
        var t = new TrackBar { Minimum = 10, Maximum = 100, TickFrequency = 10, Value = Math.Max(10, Math.Min(100, value)), Width = 170, Height = 30, AutoSize = false };
        _tips.SetToolTip(t, "Opacità");
        t.ValueChanged += (_, _) => changed(t.Value);
        return t;
    }

    void FillLotwTags()
    {
        var keep = _st.LotwTag;
        _lotwTag.Items.Clear(); _lotwTags.Clear();
        void Add(string tag, string title) { _lotwTags.Add(tag); _lotwTag.Items.Add(title); }
        Add("BAND", "Banda scelta in alto"); Add("ALL", "Tutte le bande"); Add("G:1", "HF 80-10 m"); Add("G:2", "VHF 6 m-1,25 m"); Add("G:3", "UHF e oltre");
        foreach (var b in _st.Lotw.BandsPresent(_st.QLog.Qsos)) Add("B:" + b, b);
        var idx = _lotwTags.IndexOf(keep);
        _lotwTag.SelectedIndex = idx >= 0 ? idx : 0;
    }

    void OpenAdif()
    {
        using (var d = new OpenFileDialog { Title = "Apri file ADIF del log", Filter = "ADIF (*.adi;*.adif)|*.adi;*.adif|Tutti i file|*.*", Multiselect = true })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in d.FileNames)
                try { _st.Lotw.Merge(File.ReadAllBytes(f), false, Path.GetFileName(f)); } catch (Exception ex) { _status.Text = "ADIF: " + ex.Message; }
        }
        FillLotwTags();
        _st.Rebuild();
    }

    void StartTour()
    {
        var env = Environment.GetEnvironmentVariable("YAGT_TOUR");
        if (string.IsNullOrEmpty(env) || !int.TryParse(env, out var sec) || sec <= 0) return;
        int step = 0;
        var t = new Timer { Interval = sec * 1000 };
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
            _map.Invalidate();
        };
        t.Start();
    }
}

/// <summary>Impostazioni: UDP e QLog.</summary>
public sealed class SettingsForm : Form
{
    public SettingsForm(AppState st)
    {
        Text = "YA-GT — impostazioni";
        Icon = W.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 500);
        Font = W.Ui;
        BackColor = W.C(W.Th.Page);
        int y = 12;
        Label Lab(string t, int x, int yy, bool bold = false, int w = 0) { var l = new Label { Text = t, Location = new Point(x, yy), AutoSize = w == 0, Font = bold ? W.UiBold : W.Ui }; if (w > 0) l.Size = new Size(w, 32); Controls.Add(l); return l; }
        Lab("Decodifiche UDP (protocollo WSJT-X: WSJT-X, JTDX, MSHV)", 14, y, true).ForeColor = W.C(W.Th.Accent); y += 26;
        Lab("Ascolta su", 14, y + 3);
        var addr = new TextBox { Text = st.Udp.ListenAddress, Location = new Point(100, y), Width = 140 }; Controls.Add(addr);
        Lab("porta", 252, y + 3);
        var port = new TextBox { Text = st.Udp.ListenPort.ToString(CultureInfo.InvariantCulture), Location = new Point(300, y), Width = 70 }; Controls.Add(port);
        y += 32;
        var fwd = new CheckBox { Text = "Ritrasmetti (inoltra) i pacchetti UDP ricevuti", Checked = st.Udp.Forward, Location = new Point(14, y), AutoSize = true }; Controls.Add(fwd);
        y += 28;
        Lab("Verso", 14, y + 3);
        var fwdTo = new TextBox { Text = st.Udp.ForwardTo, Location = new Point(100, y), Width = 420 }; Controls.Add(fwdTo);
        y += 28;
        Lab("Uno o più host:porta separati da virgola (es. QLog 127.0.0.1:2238, GridTracker, JTAlert).", 14, y, false, 520).ForeColor = W.C(W.Th.Muted);
        y += 44;
        Lab("Log di QLog", 14, y, true).ForeColor = W.C(W.Th.Accent); y += 26;
        var qlog = new TextBox { Text = st.QLogPath, Location = new Point(14, y), Width = 420 }; Controls.Add(qlog);
        var browse = new Button { Text = "Sfoglia…", Location = new Point(442, y - 2), Size = new Size(90, 26) }; Controls.Add(browse);
        browse.Click += (_, _) => { using (var d = new OpenFileDialog { Filter = "qlog.db|*.db|Tutti i file|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) qlog.Text = d.FileName; };
        y += 30;
        Lab("Vuoto = posizione standard di QLog (%LOCALAPPDATA%\\hamradio\\QLog\\qlog.db). Si legge una copia: il file di QLog non viene mai modificato.", 14, y, false, 520).ForeColor = W.C(W.Th.Muted);
        y += 44;
        Lab("Aggiornamento automatico", 14, y, true).ForeColor = W.C(W.Th.Accent); y += 24;
        var auto = new CheckBox { Text = "Aggiornamento automatico dal GitHub (ogni " + Updater.IntervalMinutes + " minuti)", Checked = Updater.Enabled, Location = new Point(14, y), AutoSize = true }; Controls.Add(auto);
        var now = new Button { Text = "Controlla adesso", Location = new Point(400, y - 3), Size = new Size(130, 26) }; Controls.Add(now);
        y += 28;
        var upd = Lab("Stato: " + Updater.Status, 14, y, false, 520); upd.ForeColor = W.C(W.Th.Muted);
        Action<string> onChange = m => { try { if (!upd.IsDisposed) upd.BeginInvoke((Action)(() => upd.Text = "Stato: " + m)); } catch { } };
        Updater.Changed += onChange;
        FormClosed += (_, _) => Updater.Changed -= onChange;
        now.Click += async (_, _) => await Updater.CheckAsync(true);
        y += 36;
        var err = Lab("", 14, y, false, 520); err.ForeColor = Color.Firebrick;
        var ok = new Button { Text = "Salva", Location = new Point(452, 456), Size = new Size(90, 30), BackColor = W.C(W.Th.Accent), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Annulla", Location = new Point(354, 456), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr) || pr <= 0 || pr > 65535) { err.Text = "Porta UDP non valida."; return; }
            if (!System.Net.IPAddress.TryParse(addr.Text.Trim(), out _)) { err.Text = "Indirizzo d'ascolto non valido."; return; }
            WsjtxUdp.ParseTargets(fwdTo.Text, out var e2);
            if (fwd.Checked && e2 != "") { err.Text = "Inoltro: " + e2; return; }
            st.Udp.ListenAddress = addr.Text.Trim(); st.Udp.ListenPort = pr; st.Udp.Forward = fwd.Checked; st.Udp.ForwardTo = fwdTo.Text.Trim();
            st.QLogPath = qlog.Text.Trim();
            bool wasOn = Updater.Enabled;
            Updater.Enabled = auto.Checked;
            if (Updater.Enabled && !wasOn) Updater.Start(); else if (!Updater.Enabled) Updater.Stop();
            st.SaveSettings();
            st.RestartUdp();
            if (st.LayerQLog) st.RefreshNow();
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}

/// <summary>Scaricamento del report ADIF da LoTW (la password non viene mai salvata).</summary>
public sealed class LotwForm : Form
{
    readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public LotwForm(AppState st)
    {
        Text = "Scarica da LoTW";
        Icon = W.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 268);
        Font = W.Ui;
        int y = 14;
        void Lab(string t, int yy) => Controls.Add(new Label { Text = t, AutoSize = true, Location = new Point(14, yy + 3) });
        Lab("Nome utente LoTW:", y);
        var user = new TextBox { Location = new Point(200, y), Width = 250, Text = st.S.Get("lotw_user", "") }; Controls.Add(user); y += 34;
        Lab("Password LoTW:", y);
        var pass = new TextBox { Location = new Point(200, y), Width = 250, UseSystemPasswordChar = true }; Controls.Add(pass); y += 34;
        Lab("Solo nominativo (facoltativo):", y);
        var own = new TextBox { Location = new Point(200, y), Width = 250, CharacterCasing = CharacterCasing.Upper, Text = st.S.Get("lotw_owncall", "") }; Controls.Add(own); y += 32;
        var remember = new CheckBox { Text = "Ricorda il nome utente (la password non viene mai salvata)", Location = new Point(14, y), Size = new Size(440, 24), Checked = st.S.GetBool("lotw_remember", true) }; Controls.Add(remember); y += 30;
        var info = new Label { Location = new Point(14, y), Size = new Size(440, 54), ForeColor = Color.DimGray, Text = "Vengono scaricati tutti i QSO caricati su LoTW, con i dettagli delle conferme (locatore del corrispondente). Con log grandi LoTW può impiegare qualche minuto." };
        Controls.Add(info); y += 62;
        var ok = new Button { Text = "Scarica", Location = new Point(254, y), Size = new Size(96, 30) };
        var cancel = new Button { Text = "Annulla", Location = new Point(356, y), Size = new Size(96, 30), DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        FormClosing += (_, _) => { try { _cts.Cancel(); } catch { } };
        ok.Click += async (_, _) =>
        {
            if (user.Text.Trim() == "" || pass.Text == "") { info.Text = "Inserisci nome utente e password di LoTW."; info.ForeColor = Color.DarkRed; return; }
            ok.Enabled = false;
            st.S.SetBool("lotw_remember", remember.Checked);
            st.S.Set("lotw_user", remember.Checked ? user.Text.Trim() : "");
            st.S.Set("lotw_owncall", own.Text.Trim());
            st.S.Save();
            var prog = new Progress<string>(m => { info.Text = m; info.ForeColor = Color.DimGray; });
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
                {
                    var e = await st.Lotw.DownloadAsync(http, user.Text, pass.Text, own.Text, prog, _cts.Token);
                    if (e != "") { info.Text = e; info.ForeColor = Color.DarkRed; ok.Enabled = true; return; }
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { if (!IsDisposed) { info.Text = "Errore di collegamento: " + ex.Message; info.ForeColor = Color.DarkRed; ok.Enabled = true; } }
        };
    }
}

/// <summary>About: autore, contatti, riconoscimenti.</summary>
public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About YA-GT";
        Icon = W.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 600);
        Font = W.Ui;
        BackColor = W.C(W.Th.Page);
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 10, 10, 10) };
        var head = new Panel { Size = new Size(570, 80) };
        head.Controls.Add(new PictureBox { Image = W.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(0, 4, 72, 72) });
        head.Controls.Add(new Label { Text = "YA-GT " + AppState.Version + " (Windows 7)", Font = new Font("Segoe UI", 15f, FontStyle.Bold), AutoSize = true, Location = new Point(84, 4) });
        head.Controls.Add(new Label { Text = "Spot di un nominativo, LoTW, QLog e decodifiche UDP su una mappa a livelli", ForeColor = W.C(W.Th.Muted), AutoSize = true, Location = new Point(86, 36) });
        head.Controls.Add(new Label { Text = "Costantino Cerrotta • IC8TEM", Font = W.UiBold, ForeColor = W.C(W.Th.Accent), AutoSize = true, Location = new Point(86, 56) });
        p.Controls.Add(head);
        Label Sec(string t) => new Label { Text = t, Font = W.UiBold, ForeColor = W.C(W.Th.Accent), AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        Control Link(string t, string url)
        {
            if (url == "") return new Label { Text = t, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 1, 3, 1) };
            var l = new LinkLabel { Text = t, AutoSize = true, MaximumSize = new Size(560, 0), LinkColor = W.C(W.Th.Accent), Margin = new Padding(3, 1, 3, 1) };
            l.LinkClicked += (_, _) => W.Open(url);
            return l;
        }
        p.Controls.Add(Sec("Contatti"));
        p.Controls.Add(Link("ccerrotta@gmail.com", "mailto:ccerrotta@gmail.com"));
        p.Controls.Add(Link("ic8tem@gmail.com", "mailto:ic8tem@gmail.com"));
        p.Controls.Add(Link("www.qrz.com/db/ic8tem", "https://www.qrz.com/db/ic8tem"));
        p.Controls.Add(Link("Sostieni il progetto: ko-fi.com/ic8tem", "https://ko-fi.com/ic8tem"));
        p.Controls.Add(Sec("Riconoscimenti (credits)"));
        foreach (var c in Credits.List) p.Controls.Add(Link("• " + c.text, c.url));
        p.Controls.Add(new Label { Text = "I dati restano accanto al programma (YA-GT.ini, CALL3.TXT, cartella Dati): nulla in AppData. La password LoTW non viene mai salvata.", AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = W.C(W.Th.Muted), Margin = new Padding(3, 10, 3, 3) });
        var close = new Button { Text = "Chiudi", DialogResult = DialogResult.OK, Size = new Size(90, 30), Margin = new Padding(470, 10, 3, 3) };
        p.Controls.Add(close);
        AcceptButton = close;
        Controls.Add(p);
    }
}
