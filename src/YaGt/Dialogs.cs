using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YaGt.Core.Gui;

/// <summary>Impostazioni: UDP (porta d'ascolto e inoltro), database di QLog.</summary>
public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppState st, Action changed)
    {
        Title = "YA-GT — impostazioni";
        Icon = Ui.WindowIcon();
        Width = 580; Height = 590; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B(Ui.Th.Page);

        var addr = new TextBox { Text = st.Udp.ListenAddress, Width = 160 };
        var port = new TextBox { Text = st.Udp.ListenPort.ToString(CultureInfo.InvariantCulture), Width = 80 };
        var fwd = new CheckBox { Content = "Rimbalzo UDP: inoltra i pacchetti e riporta indietro le risposte", IsChecked = st.Udp.Forward };
        var fwdTo = new TextBox { Text = st.Udp.ForwardTo, Width = 330, Watermark = "127.0.0.1:2238, 192.168.1.20:2237" };
        var qlog = new TextBox { Text = st.QLogPath, Width = 380, Watermark = QLogDb.DefaultPath() };
        var err = Ui.Label("", 12, false, 0xFFB22222);

        var p = new StackPanel { Margin = new Thickness(16, 8), Spacing = 6 };
        p.Children.Add(Ui.Section("Decodifiche UDP (protocollo WSJT-X: WSJT-X, JTDX, MSHV)"));
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("Ascolta su"), addr, Ui.Label("porta"), port } });
        p.Children.Add(Ui.Label("Per l'indirizzo multicast (es. 224.0.0.1) YA-GT si unisce al gruppo; con 127.0.0.1 la porta non può essere la stessa di un altro programma in ascolto.", 11, false, Ui.Th.Muted));
        p.Children.Add(fwd);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Label("Verso"), fwdTo } });
        p.Children.Add(Ui.Label("Uno o più host:porta separati da virgola (es. QLog 127.0.0.1:2238, GridTracker, JTAlert). I pacchetti passano identici, QSO registrati compresi, quindi QLog scrive il QSO da solo; le risposte (doppio clic, evidenziazioni) tornano al programma digitale. In QLog metti la porta WSJT-X a 2238.", 11, false, Ui.Th.Muted));

        p.Children.Add(Ui.Section("Log di QLog"));
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                qlog,
                Ui.Button("Sfoglia…", async () =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Database di QLog (qlog.db)", FileTypeFilter = new[] { new FilePickerFileType("qlog.db") { Patterns = new[] { "*.db" } } } });
                    var f = files.FirstOrDefault()?.TryGetLocalPath();
                    if (f != null) qlog.Text = f;
                }),
            }
        });
        p.Children.Add(Ui.Label("Vuoto = posizione standard di QLog (%LOCALAPPDATA%\\hamradio\\QLog\\qlog.db). Il file di QLog non viene mai modificato: si legge una copia.", 11, false, Ui.Th.Muted));
        p.Children.Add(Ui.Section("Aggiornamento automatico"));
        var auto = new CheckBox { Content = "Aggiornamento automatico dal GitHub (ogni " + Updater.IntervalMinutes + " minuti)", IsChecked = Updater.Enabled };
        var upd = Ui.Label("Stato: " + Updater.Status, 11, false, Ui.Th.Muted);
        Updater.Changed += m => Dispatcher.UIThread.Post(() => upd.Text = "Stato: " + m);
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { auto, Ui.Button("Controlla adesso", async () => await Updater.CheckAsync(true)) } });
        p.Children.Add(upd);
        p.Children.Add(err);

        var ok = Ui.Button("Salva", () =>
        {
            if (!int.TryParse(port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr) || pr <= 0 || pr > 65535) { err.Text = "Porta UDP non valida."; return; }
            if (!System.Net.IPAddress.TryParse((addr.Text ?? "").Trim(), out _)) { err.Text = "Indirizzo d'ascolto non valido."; return; }
            WsjtxUdp.ParseTargets(fwdTo.Text ?? "", out var e2);
            if (fwd.IsChecked == true && e2 != "") { err.Text = "Inoltro: " + e2; return; }
            st.Udp.ListenAddress = addr.Text.Trim(); st.Udp.ListenPort = pr; st.Udp.Forward = fwd.IsChecked == true; st.Udp.ForwardTo = (fwdTo.Text ?? "").Trim();
            st.QLogPath = (qlog.Text ?? "").Trim();
            bool wasOn = Updater.Enabled;
            Updater.Enabled = auto.IsChecked == true;
            if (Updater.Enabled && !wasOn) Updater.Start(); else if (!Updater.Enabled) Updater.Stop();
            st.SaveSettings();
            st.RestartUdp();
            if (st.LayerQLog) st.RefreshNow();
            changed?.Invoke();
            Close();
        }, true);
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Annulla", Close), ok } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(p);
        Content = root;
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
            if ((user.Text ?? "").Trim() == "" || (pass.Text ?? "") == "") { info.Text = "Inserisci nome utente e password di LoTW."; info.Foreground = Ui.B(0xFFB22222); return; }
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
            catch (Exception ex) { info.Text = "Errore di collegamento: " + ex.Message; info.Foreground = Ui.B(0xFFB22222); go.IsEnabled = true; }
        }, true);
        Closed += (_, _) => { try { cts.Cancel(); } catch { } };
        var p = new StackPanel { Children = { grid, new Border { Margin = new Thickness(16, 6), Child = remember }, new Border { Margin = new Thickness(16, 4), Child = info } } };
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Annulla", Close), go } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(p);
        Content = root;
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
            p.Children.Add(u == "" ? (Control)Ui.Label("• " + t, 11) : Link("• " + t, u));
        p.Children.Add(Ui.Label("I dati restano accanto al programma (YA-GT.ini, CALL3.TXT, cartella Dati): nulla in AppData. La password LoTW non viene mai salvata.", 11, false, Ui.Th.Muted));
        var root = new DockPanel();
        var close = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12), Children = { Ui.Button("Chiudi", Close, true) } };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = p });
        Content = root;
    }
}
