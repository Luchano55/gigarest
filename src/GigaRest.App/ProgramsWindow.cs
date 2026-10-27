using System.Runtime.InteropServices;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;
using GigaRest.Core.Text;
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
        Title = L.T("GigaRest — рабочие программы", "GigaRest — Work programs");
        _root = new Grid { Padding = new Thickness(48, 40, 48, 32), RowSpacing = 18 };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Spacing = 8 };
        _title = Text(L.T("Рабочие программы", "Work programs"), 32, Ink, FontWeights.ExtraBold, wrap: true);
        _lead = Text("", 15, Lead, wrap: true);
        head.Children.Add(_title);
        head.Children.Add(_lead);
        _root.Children.Add(head);

        var tools = new Grid { ColumnSpacing = 12 };
        tools.ColumnDefinitions.Add(new ColumnDefinition());
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = new TextBox { PlaceholderText = L.T("Найти программу…", "Find a program…"), FontSize = 15, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8) };
        _search.TextChanged += (_, _) => Render();
        tools.Children.Add(_search);
        var suggest = new Button { Content = L.T("Подсказать автоматически", "Suggest automatically"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8) };
        ToolTipService.SetToolTip(suggest, L.T("Почта, Битрикс, офис, 1С, звонки — закрыть; мессенджеры, музыка, личный браузер — на второй монитор; игры — оставить. " +
                                           "Меняются только программы, для которых вы ещё ничего не выбрали.",
                                           "Mail, Bitrix, office apps, 1C, calls — close; messengers, music, personal browser — second monitor; games — keep. " +
                                           "Only programs you haven't set yet are changed."));
        suggest.Click += (_, _) =>
        {
            var n = ProgramHints.Apply(_rows);
            Render();
            _summary.Text = (n > 0 ? L.T($"Подсказано для {n} — проверьте и поправьте. ", $"Suggested for {n} — check and adjust. ") : L.T("Подсказывать нечего — всё уже выбрано. ", "Nothing to suggest — everything is already set. ")) + _summary.Text;
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
        _done = WarmButton(L.T("Готово", "Done"), 16);
        _doneText = (TextBlock)_done.Content;
        _done.HorizontalAlignment = HorizontalAlignment.Right;
        _done.Padding = new Thickness(40, 12, 40, 12);
        _done.Click += (_, _) => { Saved?.Invoke(_rows); AppWindow.Hide(); };
        Grid.SetColumn(_done, 1);
        footer.Children.Add(_done);
        Grid.SetRow(footer, 3);
        _root.Children.Add(footer);

        Content = _root;
        DarkWindow(AppWindow, _root);
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
        Title = setup ? L.T("GigaRest — первая настройка", "GigaRest — First setup") : L.T("GigaRest — рабочие программы", "GigaRest — Work programs");
        _title.Text = setup ? L.T("Первая настройка", "First setup") : L.T("Рабочие программы", "Work programs");
        _lead.Text = setup
            ? L.T("Отметьте, что закрывать при переходе в отдых, а что — на второй монитор. Мы уже подсказали по видам программ — " +
              "проверьте и поправьте. Потом это можно изменить в меню значка → «Рабочие программы».",
              "Mark what to close when you switch to rest, and what goes to the second monitor. We've already made suggestions by program type — " +
              "check and adjust. You can change this later from the tray icon menu → Work programs.")
            : L.T("Выберите, что делать с каждой программой при переходе в отдых. Можно настроить и те, что сейчас не запущены.",
              "Choose what happens to each program when you switch to rest. You can set up programs that aren't running right now, too.");
        if (_doneText is not null) _doneText.Text = setup ? L.T("Сохранить и начать", "Save and start") : L.T("Готово", "Done");
    }

    public void ShowLoading()
    {
        _list.Children.Clear();
        _list.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(24) });
        _summary.Text = L.T("Ищем установленные программы…", "Looking for installed programs…");
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
            _list.Children.Add(SectionTitle(L.T("Выбраны", "Chosen")));
            foreach (var r in chosen) _list.Children.Add(Row(r));
        }
        if (rest.Count > 0)
        {
            _list.Children.Add(SectionTitle(chosen.Count > 0 ? L.T("Остальные — не трогаем", "The rest — left alone") : L.T("Все программы — не трогаем", "All programs — left alone")));
            foreach (var r in rest) _list.Children.Add(Row(r));
        }
        if (background.Count > 0)
        {
            _list.Children.Add(SectionTitle(L.T("Фоновые процессы — без окон", "Background processes — no windows")));
            var note = Text(L.T("Можно завершать на время отдыха — освободится память. При возврате запустим их снова той же командой. " +
                            "Системное, драйверы, антивирус и то, что не сможем запустить обратно, здесь не показываем.",
                            "These can be ended during rest to free up memory. When you return, we start them again with the same command. " +
                            "System components, drivers, antivirus and anything we can't start again aren't shown here."), 12, Muted, wrap: true);
            note.Margin = new Thickness(14, 0, 14, 6);
            _list.Children.Add(note);
            foreach (var r in background) _list.Children.Add(Row(r));
        }
        if (shown.Count + background.Count == 0)
            _list.Children.Add(Text(L.T("Ничего не нашлось.", "Nothing found."), 14, Muted));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var close = _rows.Count(r => r.Action == ProgramAction.Close && !r.IsBackground);
        var second = _rows.Count(r => r.Action == ProgramAction.SecondMonitor);
        var bg = _rows.Where(r => r.IsBackground && r.Action == ProgramAction.Close).ToList();
        _summary.Text = L.T($"Закрывать: {close} · на второй монитор: {second} · фоновых завершать: {bg.Count}",
                            $"Close: {close} · second monitor: {second} · background to end: {bg.Count}") +
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

        var avatar = AppIcon(r.DisplayName, r.Id, r.ExePath, 32);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(avatar);

        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(r.DisplayName, 14.5, Ink, FontWeights.SemiBold, wrap: true));
        if (r.IsBackground)
        {
            var info = $"≈{GigaRest.Core.Text.Sizes.Format(r.PrivateBytes)}" + (r.Hint is null ? "" : " · " + r.Hint) + L.T($" · запустим обратно: {r.RelaunchInfo}", $" · will restart: {r.RelaunchInfo}");
            names.Children.Add(Text(info, 12, Muted, wrap: true));
        }
        else
        {
            var status = r.IsRunning ? L.T("запущена", "running") : L.T("сейчас не запущена", "not running now");
            if (r.Hint is not null) status += " · " + r.Hint;
            names.Children.Add(Text(status, 12, r.IsRunning ? Good : Muted, wrap: true));
        }
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var choices = r.IsBackground
            ? new[] { (ProgramAction.Close, L.T("Завершать в отдыхе", "End during rest")), (ProgramAction.Keep, L.T("Не трогать", "Leave alone")) }
            : new[] { (ProgramAction.Close, L.T("Закрыть", "Close")), (ProgramAction.SecondMonitor, L.T("Второй монитор", "Second monitor")), (ProgramAction.Keep, L.T("Оставить", "Keep")) };
        foreach (var (action, label) in choices)
            seg.Children.Add(Segment(r, action, label, box));
        // Тёмная дорожка, выбранное — подсвечено (как в окне перехода).
        var track = new Border
        {
            Child = seg, Padding = new Thickness(3), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center,
            Background = Vertical(global::Windows.UI.Color.FromArgb(107, 0, 0, 0), global::Windows.UI.Color.FromArgb(71, 0, 0, 0)),
            BorderBrush = Brush(global::Windows.UI.Color.FromArgb(14, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        Grid.SetColumn(track, 2);
        grid.Children.Add(track);
        box.Children.Add(grid);

        if (r.Action == ProgramAction.Close && !r.IsBackground)
        {
            var forever = new CheckBox
            {
                Content = Text(L.T("закрыть насовсем — не открывать снова после отдыха", "close for good — don't reopen after rest"), 13, Lead, wrap: true),
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
                Content = Text(L.T("завершать принудительно, даже если есть несохранённое — оно пропадёт", "force close even with unsaved work — it will be lost"), 13, Bad, wrap: true),
                IsChecked = r.ForceKill, Margin = new Thickness(64, -6, 0, 4), MinHeight = 28,
            };
            ToolTipService.SetToolTip(force, L.T("Для программ вроде ShareX: сначала попросим закрыться, через 5 секунд завершим. Вопрос «Сохранить?» ждать не будем.",
                "For programs like ShareX: we ask it to close first, then end it after 5 seconds. We won't wait for a “Save?” question."));
            force.Checked += (_, _) => r.ForceKill = true;
            force.Unchecked += (_, _) => r.ForceKill = false;
            box.Children.Add(force);
        }
        if (r.Action == ProgramAction.Close && r.HidesToTray && !r.IsBackground)
        {
            var check = new CheckBox
            {
                Content = Text(L.T("если вместо закрытия уйдёт в значок у часов — завершить", "if it hides to the tray instead of closing — end it"), 13, Lead, wrap: true),
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
        var (top, bottom, edge, fg) = !selected ? ("#00000000", "#00000000", "#00000000", "#9AA0AC") : action switch
        {
            ProgramAction.Close => ("#52F6C787", "#29EAA560", "#61EEB36C", "#F8D3A0"),
            ProgramAction.SecondMonitor => ("#6E627CB2", "#46465C8C", "#6E8FB4F0", "#DCE7FF"),
            _ => ("#2EFFFFFF", "#1AFFFFFF", "#28FFFFFF", "#F1EEE8"),
        };
        var fill = Vertical(Hex(top), Hex(bottom));
        var b = new Button
        {
            Content = Text(label, 12.5, Hex(fg), selected ? FontWeights.SemiBold : FontWeights.Medium),
            Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(7),
            Background = fill, BorderBrush = Brush(Hex(edge)), BorderThickness = new Thickness(selected ? 1 : 0),
        };
        b.Resources["ButtonBackgroundPointerOver"] = selected ? fill : Brush(global::Windows.UI.Color.FromArgb(18, 255, 255, 255));
        b.Resources["ButtonBackgroundPressed"] = selected ? fill : Brush(global::Windows.UI.Color.FromArgb(26, 255, 255, 255));
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Hex(edge));
        b.Resources["ButtonBorderBrushPressed"] = Brush(Hex(edge));
        b.Click += (_, _) =>
        {
            if (r.Action == action) return;
            r.Action = action;
            // Строка остаётся на месте, пока окно открыто: перерисовываем только её.
            var parent = (Panel)box.Parent;
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
