using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YaGt.Core.Gui;

/// <summary>Impostazioni: UDP (porta d'ascolto e inoltro), database di QLog e altri log di stazione.</summary>
public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppState st, Action changed)
    {
        Title = "YA-GT — impostazioni";
        Icon = Ui.WindowIcon();
        Width = 620; Height = 760; CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B(Ui.Th.Page);

        var addr = new TextBox { Text = st.Udp.ListenAddress, Width = 160 };
        var port = new TextBox { Text = st.Udp.ListenPort.ToString(CultureInfo.InvariantCulture), Width = 80 };
        var fwd = new CheckBox { Content = "Rimbalzo UDP: inoltra i pacchetti e riporta indietro le risposte", IsChecked = st.Udp.Forward };
        var fwdHost = new TextBox { Text = st.Udp.ForwardHost, Width = 160 };
        var fwdPort = new TextBox { Text = st.Udp.ForwardPort.ToString(CultureInfo.InvariantCulture), Width = 80 };
        var fwdTo = new TextBox { Text = st.Udp.ForwardTo, Width = 330, Watermark = "facoltative: 127.0.0.1:2333, 192.168.1.20:2237" };
        var qlog = new TextBox { Text = st.QLogPath, Width = 380, Watermark = QLogDb.DefaultPath() };
        var err = Ui.Label("", 12, false, 0xFFB22222);

        var p = new StackPanel { Margin = new Thickness(16, 8), Spacing = 6 };
        p.Children.Add(Ui.Section("Decodifiche UDP (protocollo WSJT-X: WSJT-X, JTDX, MSHV)"));
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("Ricezione: ascolta su"), addr, Ui.Label("porta"), port } });
        p.Children.Add(Ui.Label("Per l'indirizzo multicast (es. 224.0.0.1) YA-GT si unisce al gruppo; con 127.0.0.1 la porta non può essere la stessa di un altro programma in ascolto.", 11, false, Ui.Th.Muted));
        p.Children.Add(fwd);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("Ritrasmissione: verso"), fwdHost, Ui.Label("porta"), fwdPort } });
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("Altre destinazioni"), fwdTo } });
        p.Children.Add(Ui.Label("La destinazione principale è di solito QLog; nelle altre puoi aggiungere host:porta separati da virgola (GridTracker, JTAlert). I pacchetti passano identici, QSO registrati compresi, quindi QLog scrive il QSO da solo; le risposte (doppio clic, evidenziazioni) tornano al programma digitale. In QLog metti come porta WSJT-X la stessa porta di ritrasmissione.", 11, false, Ui.Th.Muted));

        p.Children.Add(Ui.Section("Log di QLog"));
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                qlog,
                Ui.Button("Sfoglia…", async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L.T("Database di QLog (qlog.db)"), FileTypeFilter = new[] { new FilePickerFileType("qlog.db") { Patterns = new[] { "*.db" } } } });
                    var f = files.FirstOrDefault()?.TryGetLocalPath();
                    if (f != null) qlog.Text = f;
                }),
            }
        });
        p.Children.Add(Ui.Label("Vuoto = posizione standard di QLog (%LOCALAPPDATA%\\hamradio\\QLog\\qlog.db). Il file di QLog non viene mai modificato: si legge una copia.", 11, false, Ui.Th.Muted));
        var udpStart = new CheckBox { Content = "Controlla all'avvio le impostazioni UDP degli altri programmi", IsChecked = st.UdpCheckAtStart };
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Button("Verifica UDP…", () => new UdpCheckWindow(st).ShowDialog(this)), udpStart } });
        var u2 = new ComboBox { Width = 230, ItemsSource = new[] { L.T("non toccarla, solo avvisi"), L.T("mandala a"), L.T("spegnila") }, SelectedIndex = st.Udp2Mode == "set" ? 1 : st.Udp2Mode == "off" ? 2 : 0 };
        var u2h = new TextBox { Text = st.Udp2Host, Width = 130 };
        var u2p = new TextBox { Text = st.Udp2Port.ToString(CultureInfo.InvariantCulture), Width = 70 };
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("UDP secondaria (ADIF)"), u2, u2h, Ui.Label("porta"), u2p } });
        p.Children.Add(Ui.Label("Porta solo l'ADIF dei QSO a un altro log (Log4OM, N1MM, HRD…). Di base YA-GT non la tocca e segnala solo le scelte fuorvianti.", 11, false, Ui.Th.Muted));
        p.Children.Add(Ui.Section("Altri log di stazione"));
        var logs = new TextBox { Text = st.LogSources, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 110, Width = 540, FontFamily = new FontFamily("Consolas,DejaVu Sans Mono,monospace"), FontSize = 11 };
        void AddLine(string l) { var t = (logs.Text ?? "").TrimEnd(); if (!StationLogs.Lines(t).Contains(l)) logs.Text = (t == "" ? "" : t + "\n") + l; }
        p.Children.Add(logs);
        p.Children.Add(new WrapPanel
        {
            Children =
            {
                Ui.Button("Aggiungi file…", async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Log di stazione", AllowMultiple = true, FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Log (ADIF, Cabrillo, SQLite, Access)") { Patterns = new[] { "*.adi", "*.adif", "*.adx", "*.log", "*.cbr", "*.SQLite", "*.sqlite", "*.s3db", "*.db", "*.dat", "*.sql", "*.mdb", "*.accdb" } },
                        new FilePickerFileType("Tutti i file") { Patterns = new[] { "*" } },
                    } });
                    foreach (var f in files) { var lp = f.TryGetLocalPath(); if (lp != null) AddLine(lp); }
                }),
                Ui.Button(L.T("Trova i log sul computer"), () => { var k = StationLogs.FindKnown(); foreach (var f in k) AddLine(f); if (k.Count == 0) err.Text = L.T("Nessun log trovato nelle cartelle abituali."); }),
                Ui.Button("+ MySQL", () => AddLine("mysql: server=localhost;port=3306;database=NOME;user=UTENTE;password=PASSWORD")),
                Ui.Button("+ Wavelog / Cloudlog", () => AddLine("wavelog: https://INDIRIZZO CHIAVE_API ID_STAZIONE")),
                Ui.Button("+ QRZ", () => AddLine("qrz: CHIAVE_API_LOGBOOK")),
            }
        });
        p.Children.Add(Ui.Label("Uno per riga (# = spenta): file ADIF, Cabrillo, SQLite o Access, oppure mysql:, wavelog:, qrz:. I file si rileggono quando cambiano, i log in rete ogni 30 minuti. I file degli altri programmi non vengono mai modificati.", 11, false, Ui.Th.Muted));
        p.Children.Add(Ui.Section("Aggiornamento automatico"));
        var auto = new CheckBox { Content = L.F("Aggiornamento automatico dal GitHub (ogni {0} minuti)", Updater.IntervalMinutes), IsChecked = Updater.Enabled };
        var upd = Ui.Label(L.F("Stato: {0}", L.T(Updater.Status)), 11, false, Ui.Th.Muted);
        Updater.Changed += m => Dispatcher.UIThread.Post(() => upd.Text = L.F("Stato: {0}", L.T(m)));
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { auto, Ui.Button("Controlla adesso", async () => await Updater.CheckAsync(true)) } });
        p.Children.Add(upd);
        p.Children.Add(Ui.Section("Lingua"));
        var langs = L.All.Select(x => x[1]).ToList();
        var lang = new ComboBox { FontSize = 12, Width = 220, Margin = new Thickness(2) };
        foreach (var n in langs) lang.Items.Add(n);
        lang.SelectedIndex = Math.Max(0, L.IndexOf(L.Code));
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { lang, Ui.Label("La nuova lingua vale dal prossimo avvio del programma.", 11, false, Ui.Th.Muted) } });
        p.Children.Add(err);

        var ok = Ui.Button("Salva", () =>
        {
            if (!int.TryParse(port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr) || pr <= 0 || pr > 65535) { err.Text = L.T("Porta UDP non valida."); return; }
            if (!System.Net.IPAddress.TryParse((addr.Text ?? "").Trim(), out _)) { err.Text = L.T("Indirizzo d'ascolto non valido."); return; }
            if (!int.TryParse(fwdPort.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fp) || fp <= 0 || fp > 65535) { err.Text = L.T("Porta di ritrasmissione non valida."); return; }
            var fh = (fwdHost.Text ?? "").Trim();
            WsjtxUdp.ParseTargets(fh + ":" + fp + "," + (fwdTo.Text ?? ""), out var e2);
            if (fwd.IsChecked == true && e2 != "") { err.Text = L.F("Ritrasmissione: {0}", e2); return; }
            if (fwd.IsChecked == true && fp == pr && (fh == "127.0.0.1" || fh == "localhost" || fh == (addr.Text ?? "").Trim())) { err.Text = L.T("La porta di ritrasmissione deve essere diversa da quella di ricezione."); return; }
            st.Udp.ListenAddress = addr.Text.Trim(); st.Udp.ListenPort = pr; st.Udp.Forward = fwd.IsChecked == true;
            st.Udp.ForwardHost = fh; st.Udp.ForwardPort = fp; st.Udp.ForwardTo = (fwdTo.Text ?? "").Trim();
            st.QLogPath = (qlog.Text ?? "").Trim();
            if (lang.SelectedIndex >= 0) st.S.Set("lingua", L.All[lang.SelectedIndex][0]);
            st.LogSources = string.Join("\n", StationLogs.Lines(logs.Text ?? ""));
            st.UdpCheckAtStart = udpStart.IsChecked == true;
            if (u2.SelectedIndex == 1 && (!int.TryParse(u2p.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p2) || p2 <= 0 || p2 > 65535)) { err.Text = L.T("Porta della UDP secondaria non valida."); return; }
            st.Udp2Mode = u2.SelectedIndex == 1 ? "set" : u2.SelectedIndex == 2 ? "off" : "warn";
            st.Udp2Host = (u2h.Text ?? "").Trim() == "" ? "127.0.0.1" : (u2h.Text ?? "").Trim();
            if (int.TryParse(u2p.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p3) && p3 > 0 && p3 <= 65535) st.Udp2Port = p3;
            bool wasOn = Updater.Enabled;
            Updater.Enabled = auto.IsChecked == true;
            if (Updater.Enabled && !wasOn) Updater.Start(); else if (!Updater.Enabled) Updater.Stop();
            st.SaveSettings();
            st.RestartUdp();
            if (st.LayerQLog || st.LayerLotw) st.RefreshNow();
            changed?.Invoke();
            Close();
        }, true);
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Annulla", Close), ok } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = p, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
        Ui.Translate(this);
    }
}

/// <summary>Scaricamento del report ADIF da LoTW (la password non viene mai salvata), come in LoTW Grid Map v2.</summary>
public sealed class LotwWindow : Window
{
    public LotwWindow(AppState st)
    {
        Title = "Scarica da LoTW";
        Icon = Ui.WindowIcon();
        Width = 500; Height = 330; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B(Ui.Th.Page);
        var user = new TextBox { Text = st.S.Get("lotw_user", ""), Width = 240 };
        var pass = new TextBox { PasswordChar = '•', Width = 240 };
        var own = new TextBox { Text = st.S.Get("lotw_owncall", ""), Width = 240 };
        var remember = new CheckBox { Content = "Ricorda il nome utente (la password non viene mai salvata)", IsChecked = st.S.GetBool("lotw_remember", true) };
        var info = Ui.Label("Vengono scaricati tutti i QSO caricati su LoTW, con i dettagli delle conferme (locatore del corrispondente). Con log grandi LoTW può impiegare qualche minuto.", 11, false, Ui.Th.Muted);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(16, 12, 16, 0) };
        void Row(int r, string t, Control c) { var l = Ui.Label(t); Grid.SetRow(l, r); grid.Children.Add(l); Grid.SetRow(c, r); Grid.SetColumn(c, 1); c.Margin = new Thickness(0, 3); grid.Children.Add(c); }
        Row(0, "Nome utente LoTW", user);
        Row(1, "Password LoTW", pass);
        Row(2, "Solo nominativo (facoltativo)", own);
        var cts = new CancellationTokenSource();
        Button go = null;
        go = Ui.Button("Scarica", async () =>
        {
            if ((user.Text ?? "").Trim() == "" || (pass.Text ?? "") == "") { info.Text = L.T("Inserisci nome utente e password di LoTW."); info.Foreground = Ui.B(0xFFB22222); return; }
            go.IsEnabled = false;
            st.S.SetBool("lotw_remember", remember.IsChecked == true);
            st.S.Set("lotw_user", remember.IsChecked == true ? user.Text.Trim() : "");
            st.S.Set("lotw_owncall", (own.Text ?? "").Trim().ToUpperInvariant());
            st.S.Save();
            var prog = new Progress<string>(m => { info.Text = m; info.Foreground = Ui.B(Ui.Th.Muted); });
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
                {
                    var e = await st.Lotw.DownloadAsync(http, user.Text, pass.Text, own.Text, prog, cts.Token);
                    if (e != "") { info.Text = e; info.Foreground = Ui.B(0xFFB22222); go.IsEnabled = true; return; }
                }
                Close();
            }
            catch (Exception ex) { info.Text = L.F("Errore di collegamento: {0}", ex.Message); info.Foreground = Ui.B(0xFFB22222); go.IsEnabled = true; }
        }, true);
        Closed += (_, _) => { try { cts.Cancel(); } catch { } };
        var p = new StackPanel { Children = { grid, new Border { Margin = new Thickness(16, 6), Child = remember }, new Border { Margin = new Thickness(16, 4), Child = info } } };
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Annulla", Close), go } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(p);
        Content = root;
        Ui.Translate(this);
        Opened += (_, _) => { if ((user.Text ?? "") != "") pass.Focus(); else user.Focus(); };
    }
}

/// <summary>About: autore, contatti, riconoscimenti.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About YA-GT";
        Icon = Ui.WindowIcon();
        Width = 620; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B(Ui.Th.Page);
        var p = new StackPanel { Margin = new Thickness(18, 12), Spacing = 4 };
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children =
            {
                new Image { Source = Ui.Icon(), Width = 72, Height = 72 },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        Ui.Label("YA-GT " + AppState.Version, 22, true),
                        Ui.Label("Spot di un nominativo, LoTW, QLog e decodifiche UDP su una mappa a livelli", 12, false, Ui.Th.Muted),
                        Ui.Label("Costantino Cerrotta • IC8TEM", 13, true, Ui.Th.Accent),
                    }
                }
            }
        });
        Control Link(string text, string url)
        {
            var b = new Button { Content = text, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.B(Ui.Th.Accent), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), FontSize = 12 };
            b.Click += (_, _) => Ui.Open(url);
            return b;
        }
        p.Children.Add(Ui.Section("Contatti"));
        p.Children.Add(Link("ccerrotta@gmail.com", "mailto:ccerrotta@gmail.com"));
        p.Children.Add(Link("ic8tem@gmail.com", "mailto:ic8tem@gmail.com"));
        p.Children.Add(Link("www.qrz.com/db/ic8tem", "https://www.qrz.com/db/ic8tem"));
        p.Children.Add(Link("Sostieni il progetto: ko-fi.com/ic8tem", "https://ko-fi.com/ic8tem"));
        p.Children.Add(Ui.Section("Riconoscimenti (credits)"));
        foreach (var (t, u) in Credits.List)
            p.Children.Add(u == "" ? (Control)Ui.Label("• " + L.T(t), 11) : Link("• " + L.T(t), u));
        p.Children.Add(Ui.Label("I dati restano accanto al programma (YA-GT.ini, CALL3.TXT, cartella Dati): nulla in AppData. La password LoTW non viene mai salvata.", 11, false, Ui.Th.Muted));
        var root = new DockPanel();
        var close = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Chiudi", Close, true) } };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = p });
        Content = root;
        Ui.Translate(this);
    }
}

/// <summary>Verifica UDP: impostazioni di WSJT-X, JTDX, MSHV, MadModem, Aurora, N1MM Logger+ e QLog confrontate con quelle di YA-GT, con la correzione.</summary>
public sealed class UdpCheckWindow : Window
{
    readonly AppState _st;
    readonly StackPanel _list = new StackPanel { Spacing = 6 };
    readonly TextBlock _out;
    readonly Button _fix;
    List<UdpDoctor.Finding> _res = new List<UdpDoctor.Finding>();

    public UdpCheckWindow(AppState st, List<UdpDoctor.Finding> first = null)
    {
        _st = st;
        Title = "YA-GT — verifica UDP";
        Icon = Ui.WindowIcon();
        Width = 760; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B(Ui.Th.Page);
        var t = st.UdpTarget();
        _out = Ui.Label("", 12, false, Ui.Th.Text);
        _fix = Ui.Button("Correggi", Fix, true);
        var again = Ui.Button("Ricontrolla", () => Run());
        var close = Ui.Button("Chiudi", Close);
        var head = Ui.Label(L.F("YA-GT ascolta su {0}:{1}", t.ListenHost, t.ListenPort) + (t.Forward ? L.F(" e ritrasmette a {0}:{1}", t.ForwardHost, t.ForwardPort) : L.T(" (rimbalzo UDP spento)")) +
            ". " + L.T("\"Correggi\" mette questi valori nei programmi chiusi, dopo averne copiato le impostazioni nella cartella Dati\\copie-impostazioni. I programmi aperti vanno chiusi prima: alla chiusura riscrivono le loro impostazioni."), 12, false, Ui.Th.Muted);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Margin = new Thickness(12), Children = { again, _fix, close } };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var body = new StackPanel { Margin = new Thickness(16, 12), Spacing = 10, Children = { head, _list, _out } };
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
        if (first != null) Show(first); else Opened += (_, _) => Run();
    }

    async void Run()
    {
        _list.Children.Clear();
        _list.Children.Add(Ui.Label(L.T("Controllo in corso…"), 12, false, Ui.Th.Muted));
        _fix.IsEnabled = false;
        var r = await Task.Run(() => { try { return _st.CheckUdp(); } catch (Exception ex) { return new List<UdpDoctor.Finding> { new UdpDoctor.Finding { Program = "Verifica", Text = ex.Message } }; } });
        Show(r);
    }

    void Show(List<UdpDoctor.Finding> r)
    {
        _res = r;
        _list.Children.Clear();
        foreach (var f in r)
        {
            var line = new StackPanel { Spacing = 1 };
            line.Children.Add(Ui.Label((f.Ok ? "✔  " : "✘  ") + f.Program + (f.Running ? "  (" + L.T("aperto") + ")" : ""), 13, true, f.Ok ? 0xFF2E7D32u : 0xFFB22222u));
            line.Children.Add(Ui.Label("     " + f.Text, 12, false, Ui.Th.Text));
            if (f.File != "") line.Children.Add(Ui.Label("     " + f.File, 10, false, Ui.Th.Muted));
            _list.Children.Add(line);
        }
        _fix.IsEnabled = r.Any(x => x.Fix != null);
    }

    async void Fix()
    {
        _fix.IsEnabled = false;
        var done = await Task.Run(() => UdpDoctor.FixAll(_res));
        _out.Text = string.Join("\n", done);
        var r = await Task.Run(() => _st.CheckUdp());
        Show(r);
    }
}
