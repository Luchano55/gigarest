using System.Runtime.InteropServices;
using GigaRest.Core.Games;
using GigaRest.Core.Stats;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// «Статистика отдыха»: сколько отдыхали сегодня и за неделю, по дням, во что играли (запуски из GigaRest)
/// и сколько Steam насчитал за две недели. Всё хранится только на компьютере.
/// </summary>
public sealed class StatsWindow : Window
{
    public const double DesignWidth = 760, DesignHeight = 820;
    private readonly Grid _root;
    private readonly StackPanel _body;
    private bool _closingForGood;

    public FrameworkElement Root => _root;

    public StatsWindow()
    {
        Title = "GigaRest — статистика отдыха";
        _root = new Grid { Padding = new Thickness(44, 36, 44, 28), RowSpacing = 18 };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(Text("Статистика отдыха", 30, Hex("#F6F2EA"), FontWeights.SemiBold));
        _body = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(scroll);
        Content = _root;
        DarkWindow(AppWindow, _root);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (sender, e) => { if (_closingForGood) return; e.Cancel = true; sender.Hide(); };
        SizeToDesign();
    }

    public void Show(RestStats stats, IReadOnlyList<Game> games, DateTime nowLocal)
    {
        var s = stats.Summarize(nowLocal);
        _body.Children.Clear();

        var cards = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.Children.Add(Stat("Сегодня", RestStats.Format(s.Today), 0));
        cards.Children.Add(Stat("За неделю", RestStats.Format(s.Week), 1));
        cards.Children.Add(Stat("Вечеров отдыха", s.Evenings > 0 ? $"{s.Evenings} из 7" : "—", 2));
        _body.Children.Add(cards);

        // Столбики по дням.
        var chart = new StackPanel { Spacing = 10 };
        chart.Children.Add(Text("По дням", 15, Ink, FontWeights.SemiBold));
        var bars = new Grid { ColumnSpacing = 12, Height = 170 };
        var max = Math.Max(60, s.Days.Max(d => d.Minutes));
        var ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        for (var i = 0; i < s.Days.Count; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var (day, minutes) = s.Days[i];
            var col = new Grid { RowSpacing = 6 };
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var value = Text(minutes >= 1 ? RestStats.Format(TimeSpan.FromMinutes(minutes)) : "", 11, Muted);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(value);
            var bar = new Border
            {
                Height = Math.Max(3, 110 * minutes / max), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6, 6, 2, 2),
                Background = i == s.Days.Count - 1 ? Vertical(Hex("#F7CA8C"), Hex("#E8A05B")) : Vertical(global::Windows.UI.Color.FromArgb(90, 238, 179, 108), global::Windows.UI.Color.FromArgb(40, 238, 179, 108)),
            };
            Grid.SetRow(bar, 1);
            col.Children.Add(bar);
            var label = Text(day.ToString("ddd", ru), 12, i == s.Days.Count - 1 ? Ink : Muted);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(label, 2);
            col.Children.Add(label);
            Grid.SetColumn(col, i);
            bars.Children.Add(col);
        }
        chart.Children.Add(bars);
        _body.Children.Add(CardBorder(chart, radius: 14, padding: 18));

        // Игры.
        var played = new StackPanel { Spacing = 8 };
        played.Children.Add(Text("Во что играли за месяц", 15, Ink, FontWeights.SemiBold));
        if (s.TopGames.Count == 0) played.Children.Add(Text("Пока ничего не запускали из «Играть».", 13, Muted));
        foreach (var (name, launches) in s.TopGames)
            played.Children.Add(Line(name, GigaRest.Core.Text.Sizes.Plural(launches, "запуск", "запуска", "запусков")));
        _body.Children.Add(CardBorder(played, radius: 14, padding: 18));

        var steam = games.Where(g => g.PlayMinutes2Weeks is > 0).OrderByDescending(g => g.PlayMinutes2Weeks).Take(5).ToList();
        if (steam.Count > 0)
        {
            var box = new StackPanel { Spacing = 8 };
            box.Children.Add(Text("Steam за две недели", 15, Ink, FontWeights.SemiBold));
            foreach (var g in steam)
                box.Children.Add(Line(g.Name, RestStats.Format(TimeSpan.FromMinutes(g.PlayMinutes2Weeks ?? 0)) + (g.PlayMinutes is { } all ? $" · всего {all / 60} ч" : "")));
            _body.Children.Add(CardBorder(box, radius: 14, padding: 18));
        }

        _body.Children.Add(Text("Статистика хранится только на этом компьютере (%AppData%\\GigaRest\\stats.json) и никуда не отправляется.", 12, Muted, wrap: true));
    }

    private static Border Stat(string title, string value, int column)
    {
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(Text(title, 12, Muted));
        s.Children.Add(Text(value, 22, Hex("#F3C58C"), FontWeights.SemiBold));
        var card = CardBorder(s, radius: 14, padding: 14);
        Grid.SetColumn(card, column);
        return card;
    }

    private static Grid Line(string name, string value)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Text(name, 14, Ink));
        var v = Text(value, 13, Muted);
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    public void SizeToDesign(bool fitScreen = true)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var w = (int)(fitScreen ? Math.Min(DesignWidth * scale, area.Width * 0.95) : DesignWidth * scale);
        var h = (int)(fitScreen ? Math.Min(DesignHeight * scale, area.Height * 0.95) : DesignHeight * scale);
        AppWindow.Resize(new SizeInt32(w, h));
        AppWindow.Move(new PointInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2));
    }

    public void UseDesignSizeForCapture()
    {
        _root.Width = DesignWidth;
        _root.Height = DesignHeight;
        Content = new Canvas { Children = { _root } };
    }

    public void Present()
    {
        AppWindow.Show();
        Activate();
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
