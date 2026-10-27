using System.Runtime.InteropServices;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// «Рабочие программы»: все программы — запущенные, уже настроенные и установленные (меню «Пуск»),
/// поиск и выбор действия для каждой. Сохраняется кнопкой «Готово».
/// </summary>
public sealed class ProgramsWindow : Window
{
    public const double DesignWidth = 980, DesignHeight = 900;

    private readonly Grid _root;
    private readonly StackPanel _list;
    private readonly TextBox _search;
    private readonly TextBlock _summary;
    private readonly TextBlock _title, _lead;
    private readonly Button _done;
    private readonly TextBlock _doneText;
    private List<SelectorRow> _rows = [];
    private bool _closingForGood;

    public event Action<IReadOnlyList<SelectorRow>>? Saved;
    public FrameworkElement Root => _root;

    public ProgramsWindow()
    {
        Title = "GigaRest — рабочие программы";
        _root = new Grid { Background = Brush(PageBg), Padding = new Thickness(48, 40, 48, 32), RowSpacing = 18, RequestedTheme = ElementTheme.Light };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Spacing = 8 };
        _title = Text("Рабочие программы", 32, Ink, FontWeights.ExtraBold, wrap: true);
        _lead = Text("", 15, Lead, wrap: true);
        head.Children.Add(_title);
        head.Children.Add(_lead);
        _root.Children.Add(head);

        var tools = new Grid { ColumnSpacing = 12 };
        tools.ColumnDefinitions.Add(new ColumnDefinition());
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = new TextBox { PlaceholderText = "Найти программу…", FontSize = 15, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8) };
        _search.TextChanged += (_, _) => Render();
        tools.Children.Add(_search);
        var suggest = new Button { Content = "Подсказать автоматически", CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8) };
        ToolTipService.SetToolTip(suggest, "Почта, Битрикс, офис, 1С, звонки — закрыть; мессенджеры, музыка, личный браузер — на второй монитор; игры — оставить. " +
                                           "Меняются только программы, для которых вы ещё ничего не выбрали.");
        suggest.Click += (_, _) =>
        {
            var n = ProgramHints.Apply(_rows);
            Render();
            _summary.Text = (n > 0 ? $"Подсказано для {n} — проверьте и поправьте. " : "Подсказывать нечего — всё уже выбрано. ") + _summary.Text;
        };
        Grid.SetColumn(suggest, 1);
        tools.Children.Add(suggest);
        Grid.SetRow(tools, 1);
        _root.Children.Add(tools);
        SetSetupMode(false);

        _list = new StackPanel { Spacing = 2 };
        var card = CardBorder(new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) }, radius: 20, padding: 12);
        Grid.SetRow(card, 2);
        _root.Children.Add(card);

        var footer = new Grid { ColumnSpacing = 16 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _summary = Text("", 14, Muted, wrap: true);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_summary);
        _doneText = Text("Готово", 16, Colors.White, FontWeights.Bold);
        _done = ColoredButton(_doneText, Accent, AccentDark, Colors.White);
        _done.HorizontalAlignment = HorizontalAlignment.Right;
        _done.Padding = new Thickness(36, 12, 36, 12);
        _done.Click += (_, _) => { Saved?.Invoke(_rows); AppWindow.Hide(); };
        Grid.SetColumn(_done, 1);
        footer.Children.Add(_done);
        Grid.SetRow(footer, 3);
        _root.Children.Add(footer);

        Content = _root;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (sender, e) =>
        {
            if (_closingForGood) return;
            e.Cancel = true; // крестик — спрятать без сохранения
            sender.Hide();
        };
        SizeToDesign();
    }

    /// <summary>Первая настройка: другой заголовок и подсказки уже расставлены.</summary>
    public bool IsSetup { get; private set; }

    public void SetSetupMode(bool setup)
    {
        IsSetup = setup;
        Title = setup ? "GigaRest — первая настройка" : "GigaRest — рабочие программы";
        _title.Text = setup ? "Первая настройка" : "Рабочие программы";
        _lead.Text = setup
            ? "Отметьте, что закрывать при переходе в отдых, а что — на второй монитор. Мы уже подсказали по видам программ — " +
              "проверьте и поправьте. Потом это можно изменить в меню значка → «Рабочие программы»."
            : "Выберите, что делать с каждой программой при переходе в отдых. Можно настроить и те, что сейчас не запущены.";
        if (_doneText is not null) _doneText.Text = setup ? "Сохранить и начать" : "Готово";
    }

    public void ShowLoading()
    {
        _list.Children.Clear();
        _list.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(24) });
        _summary.Text = "Ищем установленные программы…";
    }

    public void SetRows(List<SelectorRow> rows)
    {
        _rows = rows;
        if (IsSetup) ProgramHints.Apply(_rows);
        else foreach (var r in _rows) r.Hint = ProgramHints.Suggest(r.Id, r.ExePath)?.Reason;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        var shown = ProgramCatalog.Filter(_rows, _search.Text).ToList();
        var background = shown.Where(r => r.IsBackground).OrderByDescending(r => r.PrivateBytes).ToList();
        shown = shown.Where(r => !r.IsBackground).ToList();
        var chosen = shown.Where(r => r.Action != ProgramAction.Keep).ToList();
        var rest = shown.Where(r => r.Action == ProgramAction.Keep).ToList();
        if (chosen.Count > 0)
        {
            _list.Children.Add(SectionTitle("Выбраны"));
            foreach (var r in chosen) _list.Children.Add(Row(r));
        }
        if (rest.Count > 0)
        {
            _list.Children.Add(SectionTitle(chosen.Count > 0 ? "Остальные — не трогаем" : "Все программы — не трогаем"));
            foreach (var r in rest) _list.Children.Add(Row(r));
        }
        if (background.Count > 0)
        {
            _list.Children.Add(SectionTitle("Фоновые процессы — без окон"));
            var note = Text("Можно завершать на время отдыха — освободится память. При возврате запустим их снова той же командой. " +
                            "Системное, драйверы, антивирус и то, что не сможем запустить обратно, здесь не показываем.", 12, Muted, wrap: true);
            note.Margin = new Thickness(14, 0, 14, 6);
            _list.Children.Add(note);
            foreach (var r in background) _list.Children.Add(Row(r));
        }
        if (shown.Count + background.Count == 0)
            _list.Children.Add(Text("Ничего не нашлось.", 14, Muted));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var close = _rows.Count(r => r.Action == ProgramAction.Close && !r.IsBackground);
        var second = _rows.Count(r => r.Action == ProgramAction.SecondMonitor);
        var bg = _rows.Where(r => r.IsBackground && r.Action == ProgramAction.Close).ToList();
        _summary.Text = $"Закрывать: {close} · на второй монитор: {second} · фоновых завершать: {bg.Count}" +
                        (bg.Count > 0 ? $" (≈{GigaRest.Core.Text.Sizes.Format(bg.Sum(r => r.PrivateBytes))})" : "");
    }

    private static TextBlock SectionTitle(string text)
    {
        var t = Text(text.ToUpperInvariant(), 12, Muted, FontWeights.Bold);
        t.CharacterSpacing = 80;
        t.Margin = new Thickness(14, 14, 0, 4);
        return t;
    }

    private UIElement Row(SelectorRow r)
    {
        var box = new StackPanel();
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(14, 8, 14, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = Brush(ProgramColor(r.Id)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = r.DisplayName.Length > 0 ? char.ToUpper(r.DisplayName[0]).ToString() : "?", FontSize = 16,
                FontWeight = FontWeights.Bold, Foreground = Brush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(avatar);

        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(r.DisplayName, 15, Ink, FontWeights.Bold, wrap: true));
        if (r.IsBackground)
        {
            var info = $"≈{GigaRest.Core.Text.Sizes.Format(r.PrivateBytes)}" + (r.Hint is null ? "" : " · " + r.Hint) + $" · запустим обратно: {r.RelaunchInfo}";
            names.Children.Add(Text(info, 12, Muted, wrap: true));
        }
        else
        {
            var status = r.IsRunning ? "запущена" : "сейчас не запущена";
            if (r.Hint is not null) status += " · " + r.Hint;
            names.Children.Add(Text(status, 12, r.IsRunning ? Hex("#1E7A44") : Muted, wrap: true));
        }
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var choices = r.IsBackground
            ? new[] { (ProgramAction.Close, "Завершать в отдыхе"), (ProgramAction.Keep, "Не трогать") }
            : new[] { (ProgramAction.Close, "Закрыть"), (ProgramAction.SecondMonitor, "Второй монитор"), (ProgramAction.Keep, "Оставить") };
        foreach (var (action, label) in choices)
            seg.Children.Add(Segment(r, action, label, box));
        Grid.SetColumn(seg, 2);
        grid.Children.Add(seg);
        box.Children.Add(grid);

        if (r.Action == ProgramAction.Close && !r.IsBackground)
        {
            var forever = new CheckBox
            {
                Content = Text("закрыть насовсем — не открывать снова после отдыха", 13, Lead, wrap: true),
                IsChecked = r.NoRelaunch, Margin = new Thickness(64, -6, 0, 4), MinHeight = 28,
            };
            forever.Checked += (_, _) => r.NoRelaunch = true;
            forever.Unchecked += (_, _) => r.NoRelaunch = false;
            box.Children.Add(forever);
        }
        if (r.Action == ProgramAction.Close && !r.IsBackground)
        {
            var force = new CheckBox
            {
                Content = Text("завершать принудительно, даже если есть несохранённое — оно пропадёт", 13, Hex("#B42318"), wrap: true),
                IsChecked = r.ForceKill, Margin = new Thickness(64, -6, 0, 4), MinHeight = 28,
            };
            ToolTipService.SetToolTip(force, "Для программ вроде ShareX: сначала попросим закрыться, через 5 секунд завершим. Вопрос «Сохранить?» ждать не будем.");
            force.Checked += (_, _) => r.ForceKill = true;
            force.Unchecked += (_, _) => r.ForceKill = false;
            box.Children.Add(force);
        }
        if (r.Action == ProgramAction.Close && r.HidesToTray && !r.IsBackground)
        {
            var check = new CheckBox
            {
                Content = Text("если вместо закрытия уйдёт в значок у часов — завершить", 13, Lead, wrap: true),
                IsChecked = r.AllowTerminate, Margin = new Thickness(64, -6, 0, 4), MinHeight = 28,
            };
            check.Checked += (_, _) => r.AllowTerminate = true;
            check.Unchecked += (_, _) => r.AllowTerminate = false;
            box.Children.Add(check);
        }
        return box;
    }

    private Button Segment(SelectorRow r, ProgramAction action, string label, StackPanel box)
    {
        var selected = r.Action == action;
        var (bg, fg) = !selected ? ("#F1F2F6", "#4A4D5C") : action switch
        {
            ProgramAction.Close => ("#FDE8E8", "#B42318"),
            ProgramAction.SecondMonitor => ("#E6EDFF", "#2948B8"),
            _ => ("#EAF5EE", "#1E7A44"),
        };
        var b = ColoredButton(Text(label, 12, Hex(fg), selected ? FontWeights.Bold : FontWeights.Normal), Hex(bg), Hex(selected ? bg : "#E6E8EF"), Hex(fg));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(12, 6, 12, 6);
        b.CornerRadius = new CornerRadius(999);
        b.FontSize = 12;
        b.Click += (_, _) =>
        {
            if (r.Action == action) return;
            r.Action = action;
            // Строка остаётся на месте, пока окно открыто: перерисовываем только её.
            var parent = (StackPanel)box.Parent;
            var index = parent.Children.IndexOf(box);
            if (index >= 0) parent.Children[index] = Row(r);
            UpdateSummary();
        };
        return b;
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
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op) op.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
