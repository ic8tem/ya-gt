using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace YaGt.Core.Gui;

/// <summary>Finestra di avvio: nominativo e locatore a 6 caratteri (centro della mappa azimutale, distanze e azimut degli spot).</summary>
public sealed class StartWindow : Window
{
    public bool Accepted { get; private set; }
    readonly AppState _st;
    readonly TextBox _call, _loc;
    readonly TextBlock _err;

    public StartWindow(AppState st)
    {
        _st = st;
        Title = "YA-GT — avvio";
        Icon = Ui.WindowIcon();
        Width = 460; Height = 330;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.B(Ui.Th.Page);

        _call = new TextBox { Text = st.MyCall != "" ? st.MyCall : "IC8TEM", FontSize = 16, Width = 200, MaxLength = 16, HorizontalAlignment = HorizontalAlignment.Left };
        _loc = new TextBox { Text = Locator.IsValid6(st.MyLoc) ? st.MyLoc : "JN70cn", FontSize = 16, Width = 120, MaxLength = 6, HorizontalAlignment = HorizontalAlignment.Left };
        _err = Ui.Label("", 12, false, 0xFFB22222);

        var head = new Border
        {
            Background = Ui.B(Ui.Th.Band), Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children =
                {
                    new Image { Source = Ui.Icon(), Width = 40, Height = 40 },
                    new StackPanel
                    {
                        Children =
                        {
                            Ui.Label("YA-GT", 20, true, Ui.Th.BandFg),
                            Ui.Label("Spot, LoTW, QLog e decodifiche sulla mappa — di Costantino IC8TEM", 11, false, Ui.Th.BandFg),
                        }
                    }
                }
            }
        };
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(18, 14) };
        void Row(int r, string t, Control c) { var l = Ui.Label(t, 13); Grid.SetRow(l, r); form.Children.Add(l); Grid.SetRow(c, r); Grid.SetColumn(c, 1); c.Margin = new Thickness(0, 4); form.Children.Add(c); }
        Row(0, "Nominativo", _call);
        Row(1, "Locatore (6 caratteri)", _loc);
        var note = Ui.Label("Il locatore fa da centro della mappa azimutale e serve a calcolare distanza e azimut di ogni spot.", 11, false, Ui.Th.Muted);
        Grid.SetRow(note, 2); Grid.SetColumnSpan(note, 2); note.Margin = new Thickness(0, 6, 0, 0); form.Children.Add(note);
        Grid.SetRow(_err, 3); Grid.SetColumnSpan(_err, 2); form.Children.Add(_err);

        var ok = Ui.Button("Avvia", Accept, true);
        ok.IsDefault = true;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 12),
            Children = { Ui.Button("Esci", Close), ok }
        };
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(form);
        Content = root;
        Opened += (_, _) => { _call.Focus(); _call.SelectAll(); };
    }

    void Accept()
    {
        var call = (_call.Text ?? "").Trim().ToUpperInvariant();
        var loc = (_loc.Text ?? "").Trim();
        if (call.Length < 3 || !call.Any(char.IsDigit) || !call.Any(char.IsLetter) || call.Any(ch => !char.IsLetterOrDigit(ch) && ch != '/'))
        { _err.Text = "Nominativo non valido."; return; }
        if (!Locator.IsValid6(loc)) { _err.Text = "Il locatore deve avere 6 caratteri, es. JN70cn."; return; }
        _st.SetStation(call, loc);
        Accepted = true;
        Close();
    }
}
