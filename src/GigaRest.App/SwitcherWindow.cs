using System.Runtime.InteropServices;
using GigaRest.Core.Closing;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;
using GigaRest.Core.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Окно «Работа → Отдых» по макету Романа («премиум-отделка»: тёмное окно с тёплым и холодным светом,
/// стеклянные панели, оранжевая кнопка). Слева — мониторы, цифры и кнопка, справа — что будет с каждой программой.
/// Крестик прячет окно, программа остаётся в трее.
/// </summary>
public sealed class SwitcherWindow : Window
{
    public const double DesignWidth = 1440, DesignHeight = 900;

    // ---------- цвета макета ----------
    private static readonly Color TextHi = Hex("#F6F2EA");
    private static readonly Color TextMain = Hex("#ECE8E1");
    private static readonly Color TextSoft = Hex("#A9AEBB");
    private static readonly Color TextMuted = Hex("#8A909C");
    private static readonly Color TextDim = Hex("#7F8590");
    private static readonly Color WarmText = Hex("#F8D3A0");
    private static readonly Color Amber = Hex("#F0B46C");
    private static readonly Color Blue = Hex("#8FB4F0");

    private readonly Grid _root;
    private readonly TextBlock _headline, _lead, _badge;
    private readonly Grid _mainScreen;
    private readonly Grid _secondScreen;
    private readonly TextBlock _mainCaption, _secondCaption, _secondHint;
    private readonly TextBlock _ramTitle, _ramValue, _dndTitle, _dndValue, _tilesTitle, _tilesValue;
    private readonly Button _go;
    private readonly TextBlock _goText;
    private readonly StackPanel _progressBox;
    private readonly TextBlock _progressText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancel;
    private readonly Border _offer;
    private readonly TextBlock _offerText;
    private readonly Border _error;
    private readonly TextBlock _errorText;
    private readonly TextBlock _notice;
    private readonly HyperlinkButton _test;
    private readonly TextBlock _note;
    private readonly TextBlock _panelTitle, _panelCount, _panelSummary;
    private readonly StackPanel _list;
    private readonly HyperlinkButton _choose;
    private Mode _mode;
    private bool _closingForGood;
    private bool _hasSecondMonitor = true;

    /// <summary>Сколько плиток разрешено настройками: 0 — плитки выключены, 1–4.</summary>
    public int MaxTiles { get; set; } = TilePlan.Max;
    private bool TilesAvailable => _hasSecondMonitor && MaxTiles > 0;
    private readonly List<(RunningProgram P, Grid Status)> _progressRows = [];

    private enum Mode { Plan, Progress, Rest, Returning, Work, Error }

    public event Action<RunningProgram>? ActionChanged;
    public event Action? GoRequested;
    public event Action? ReturnRequested;
    public event Action? CancelRequested;
    public event Action? ProgramsRequested;
    public event Action? TestRequested;
    public event Action? SnoozeRequested;
    /// <summary>«Завершить» у программы, которая спряталась в трей.</summary>
    public event Action<string, bool>? TerminateRequested;

    public IReadOnlyList<RunningProgram> Programs { get; private set; } = [];
    /// <summary>Включено ли «Не беспокоить» на время отдыха (настройка) — для подписи.</summary>
    public bool DoNotDisturb { get; set; } = true;
    /// <summary>Пробный режим: в списке только тестовые окна.</summary>
    public bool IsTest { get; private set; }
    public FrameworkElement Root => _root;

    public SwitcherWindow()
    {
        Title = "GigaRest";
        _root = new Grid { RequestedTheme = ElementTheme.Dark, Background = WindowBackground() };
        // Тёплый свет снизу слева и холодный сверху справа.
        _root.Children.Add(new Border { Background = Glow(0, 1, 0.65, 0.55, Color.FromArgb(56, 224, 152, 108)), IsHitTestVisible = false });
        _root.Children.Add(new Border { Background = Glow(1, 0, 0.55, 0.45, Color.FromArgb(33, 110, 132, 210)), IsHitTestVisible = false });

        var layout = new Grid { Padding = new Thickness(40, 30, 40, 30), ColumnSpacing = 32 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        _root.Children.Add(layout);

        // ---------- левая колонка ----------
        var left = new Grid { RowSpacing = 0 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // заголовок
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // мониторы
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // цифры, кнопка, ссылки

        var head = new StackPanel { Spacing = 10 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        _headline = Text(L.T("Работа → Отдых", "Work → Rest"), 36, TextHi, FontWeights.SemiBold);
        _headline.CharacterSpacing = -22;
        titleRow.Children.Add(_headline);
        _badge = Text(L.T("ПРОБНЫЙ РЕЖИМ", "TEST MODE"), 11, Warm, FontWeights.Bold);
        _badge.CharacterSpacing = 60;
        var badgeBox = new Border
        {
            Child = _badge, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(999), BorderBrush = Brush(Color.FromArgb(110, 238, 179, 108)), BorderThickness = new Thickness(1),
            Background = Brush(Color.FromArgb(30, 238, 179, 108)),
        };
        titleRow.Children.Add(badgeBox);
        _badge.Tag = badgeBox;
        head.Children.Add(titleRow);
        _lead = Text("", 14.5, TextSoft, wrap: true);
        _lead.LineHeight = 21;
        _lead.MaxWidth = 520;
        _lead.HorizontalAlignment = HorizontalAlignment.Left;
        head.Children.Add(_lead);
        left.Children.Add(head);

        // Мониторы: основной поменьше, второй побольше — с плитками.
        var monitors = new Grid { ColumnSpacing = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 20, 0, 20) };
        monitors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        monitors.ColumnDefinitions.Add(new ColumnDefinition());
        _mainScreen = new Grid();
        _mainCaption = Caption(L.T("Основной", "Main"), L.T("Рабочий стол", "Desktop"));
        var main = MonitorFigure(_mainScreen, 136, 86, _mainCaption, null, rotate: 10);
        main.VerticalAlignment = VerticalAlignment.Bottom;
        monitors.Children.Add(main);
        _secondScreen = new Grid { RowSpacing = 6, ColumnSpacing = 6, Padding = new Thickness(6) };
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        _secondCaption = Caption(L.T("Второй", "Second"), L.T("Живые плитки", "Live tiles"));
        _secondHint = Text("", 12, TextMuted, wrap: true);
        var second = MonitorFigure(_secondScreen, 330, 206, _secondCaption, _secondHint, rotate: -4);
        Grid.SetColumn(second, 1);
        monitors.Children.Add(second);
        Grid.SetRow(monitors, 1);
        left.Children.Add(monitors);

        var bottom = new StackPanel { Spacing = 14 };
        var stats = new Grid { ColumnSpacing = 10 };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        (_ramTitle, _ramValue) = (Text(L.T("Освободится памяти", "Memory to free"), 12, TextMuted), Text("", 22, Hex("#F3C58C"), FontWeights.SemiBold));
        (_dndTitle, _dndValue) = (Text(L.T("Уведомления", "Notifications"), 12, TextMuted), Text("", 22, TextHi, FontWeights.SemiBold));
        (_tilesTitle, _tilesValue) = (Text(L.T("Плитки", "Tiles"), 12, TextMuted), Text("", 22, TextHi, FontWeights.SemiBold));
        stats.Children.Add(StatCard(_ramTitle, _ramValue, 0));
        stats.Children.Add(StatCard(_dndTitle, _dndValue, 1));
        stats.Children.Add(StatCard(_tilesTitle, _tilesValue, 2));
        bottom.Children.Add(stats);

        _goText = Text(L.T("Перейти в отдых", "Start resting"), 17, Hex("#24170C"), FontWeights.SemiBold);
        _go = GoButton(_goText);
        _go.Click += (_, _) => OnGo();
        bottom.Children.Add(_go);

        // Ход перехода: колёсико, текст, «Отменить», тёплая полоса.
        _progressBox = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var progressRow = new Grid { ColumnSpacing = 10 };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition());
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressRow.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14, Foreground = Brush(Warm), VerticalAlignment = VerticalAlignment.Center });
        _progressText = Text("", 13, TextMain);
        _progressText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_progressText, 1);
        progressRow.Children.Add(_progressText);
        _cancel = SmallButton(L.T("Отменить", "Cancel"));
        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        Grid.SetColumn(_cancel, 2);
        progressRow.Children.Add(_cancel);
        _progressBox.Children.Add(progressRow);
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, Foreground = Brush(Warm), Background = Brush(Color.FromArgb(30, 255, 255, 255)) };
        _progressBox.Children.Add(_progressBar);
        bottom.Children.Add(_progressBox);

        // «Пора за работу» по расписанию.
        _offerText = Text("", 13, TextSoft, wrap: true);
        var offerBox = new StackPanel { Spacing = 10 };
        offerBox.Children.Add(Text(L.T("Пора за работу.", "Time to get back to work."), 16, TextHi, FontWeights.SemiBold));
        offerBox.Children.Add(_offerText);
        var offerButtons = new Grid { ColumnSpacing = 8 };
        offerButtons.ColumnDefinitions.Add(new ColumnDefinition());
        offerButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = GoButton(Text(L.T("Вернуться к работе", "Back to work"), 14, Hex("#24170C"), FontWeights.SemiBold));
        back.Height = 40;
        back.CornerRadius = new CornerRadius(10);
        back.Click += (_, _) => ReturnRequested?.Invoke();
        offerButtons.Children.Add(back);
        var snooze = SmallButton(L.T("Через 15 минут", "In 15 minutes"));
        snooze.Height = 40;
        snooze.Click += (_, _) => { SnoozeRequested?.Invoke(); AppWindow.Hide(); };
        Grid.SetColumn(snooze, 1);
        offerButtons.Children.Add(snooze);
        offerBox.Children.Add(offerButtons);
        _offer = new Border
        {
            Child = offerBox, Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Visibility = Visibility.Collapsed,
            Background = Vertical(Color.FromArgb(46, 238, 179, 108), Color.FromArgb(14, 238, 179, 108)),
            BorderBrush = Brush(Color.FromArgb(70, 238, 179, 108)), BorderThickness = new Thickness(1),
        };
        bottom.Children.Add(_offer);

        // Ошибка.
        _errorText = Text("", 13, Hex("#F2C0B2"), wrap: true);
        var errorGrid = new Grid { ColumnSpacing = 10 };
        errorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        errorGrid.ColumnDefinitions.Add(new ColumnDefinition());
        errorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        errorGrid.Children.Add(Bang());
        Grid.SetColumn(_errorText, 1);
        errorGrid.Children.Add(_errorText);
        var errorClose = SmallButton(L.T("Закрыть", "Close"));
        errorClose.VerticalAlignment = VerticalAlignment.Center;
        errorClose.Click += (_, _) => AppWindow.Hide();
        Grid.SetColumn(errorClose, 2);
        errorGrid.Children.Add(errorClose);
        _error = new Border
        {
            Child = errorGrid, Padding = new Thickness(14, 10, 10, 10), CornerRadius = new CornerRadius(12), Visibility = Visibility.Collapsed,
            Background = Brush(Color.FromArgb(34, 232, 110, 90)), BorderBrush = Brush(Color.FromArgb(90, 232, 110, 90)), BorderThickness = new Thickness(1),
        };
        bottom.Children.Add(_error);

        _notice = Text("", 12.5, TextSoft, wrap: true);
        bottom.Children.Add(_notice);
        _test = new HyperlinkButton { Content = Text(L.T("Попробовать на тестовых окнах — ваши программы не тронем", "Try it on test windows — your apps stay untouched"), 13.5, Warm), Padding = new Thickness(0) };
        _test.Click += (_, _) => TestRequested?.Invoke();
        bottom.Children.Add(_test);
        _note = Text(L.T("Закрываем вежливо, как крестиком: если где-то не сохранено — программа спросит сама.", "We close apps politely, just like the X button: if something isn't saved, the app will ask you itself."), 12, TextDim, wrap: true);
        _note.Margin = new Thickness(0, -8, 0, 0);
        bottom.Children.Add(_note);
        var version = typeof(SwitcherWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
        var plus = version.IndexOf('+');
        bottom.Children.Add(Text("GigaRest " + (plus > 0 ? version[..plus] : version), 10.5, Hex("#4E535E")));
        Grid.SetRow(bottom, 2);
        left.Children.Add(bottom);
        layout.Children.Add(left);

        // ---------- правая колонка: стеклянная панель «Программы» ----------
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            Padding = new Thickness(24, 18, 24, 14), ColumnSpacing = 10,
            Background = Vertical(Color.FromArgb(8, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
            BorderBrush = Brush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 0, 1),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        _panelTitle = Text(L.T("Программы", "Apps"), 16, Hex("#F4F0E9"), FontWeights.SemiBold);
        _panelTitle.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_panelTitle);
        _panelCount = Text("", 12.5, TextMuted);
        _panelCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_panelCount, 1);
        header.Children.Add(_panelCount);
        _panelSummary = Text("", 12.5, TextMuted);
        _panelSummary.HorizontalAlignment = HorizontalAlignment.Right;
        _panelSummary.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_panelSummary, 2);
        header.Children.Add(_panelSummary);
        panel.Children.Add(header);

        _list = new StackPanel { Spacing = 2, Padding = new Thickness(12, 8, 16, 8) };
        var scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);

        _choose = new HyperlinkButton { Content = Text(L.T("Выбрать рабочие программы, в том числе незапущенные…", "Choose work apps, including ones that aren't running…"), 13, Warm), Margin = new Thickness(24, 6, 24, 14), Padding = new Thickness(0) };
        _choose.Click += (_, _) => ProgramsRequested?.Invoke();
        Grid.SetRow(_choose, 2);
        panel.Children.Add(_choose);

        var panelCard = new Border
        {
            Child = panel, CornerRadius = new CornerRadius(18),
            Background = Vertical(Color.FromArgb(209, 32, 35, 45), Color.FromArgb(224, 19, 21, 27)),
            BorderBrush = Vertical(Color.FromArgb(46, 255, 255, 255), Color.FromArgb(14, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        Grid.SetColumn(panelCard, 1);
        layout.Children.Add(panelCard);

        Content = _root;

        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        DarkTitleBar(AppWindow);
        AppWindow.Closing += (sender, e) =>
        {
            if (_closingForGood) return;
            e.Cancel = true; // крестик — спрятать в трей
            sender.Hide();
        };
        SizeToDesign();
    }

    // ---------- состояния ----------

    public void ShowPlan(IReadOnlyList<RunningProgram> programs, bool hasSecondMonitor, string? notice, bool test = false)
    {
        _mode = Mode.Plan;
        Programs = programs;
        _hasSecondMonitor = hasSecondMonitor;
        IsTest = test;
        _headline.Text = L.T("Работа → Отдых", "Work → Rest");
        ((Border)_badge.Tag).Visibility = test ? Visibility.Visible : Visibility.Collapsed;
        _lead.Text = test
            ? L.T("Всё как по-настоящему, но только с четырьмя тестовыми окнами. Ваши программы не тронем.", "Everything works for real, but only with four test windows. Your apps stay untouched.")
            : L.T("Рабочие программы закроются и освободят память, на втором мониторе останется только то, что вы разрешили", "Work apps will close and free up memory; the second monitor keeps only what you've allowed");
        SetMainScreen(rest: false);
        SetGo(L.T("Перейти в отдых", "Start resting"), enabled: true);
        _notice.Text = notice ?? "";
        ShowExtras(progress: false, offer: false, error: false);
        _test.Content = Text(test ? L.T("Выйти из пробного режима", "Leave test mode") : L.T("Попробовать на тестовых окнах — ваши программы не тронем", "Try it on test windows — your apps stay untouched"), 13.5, Warm);
        _test.Visibility = Visibility.Visible;
        _note.Visibility = Visibility.Visible;
        _choose.Visibility = test ? Visibility.Collapsed : Visibility.Visible;
        _panelCount.Text = test ? L.Plural(programs.Count, "тестовое окно", "тестовых окна", "тестовых окон", "test window", "test windows") : L.T($"{programs.Count} запущено", $"{programs.Count} running");
        RenderPlanList();
        RefreshForecast();
    }

    public void ShowProgress(IReadOnlyList<RunningProgram> programs)
    {
        _mode = Mode.Progress;
        _panelTitle.Text = L.T("Программы", "Apps");
        Programs = programs;
        SetGo(L.T("Переходим…", "Switching…"), enabled: false);
        _notice.Text = "";
        ShowExtras(progress: true, offer: false, error: false);
        _cancel.Visibility = Visibility.Visible;
        _progressText.Text = L.T("Готовим отдых…", "Getting rest ready…");
        _progressBar.Value = 0;
        _test.Visibility = Visibility.Collapsed;
        _choose.Visibility = Visibility.Collapsed;
        _secondHint.Text = L.T("Плитки откроются, когда всё закроется", "Tiles will open once everything is closed");
        RenderSecondScreen(TilePlan.Ordered(programs).ToList(), interactive: false);

        _list.Children.Clear();
        _progressRows.Clear();
        foreach (var p in programs)
        {
            var status = new Grid();
            if (p.Action == ProgramAction.Close) { SetStatus(status, L.T("в очереди", "waiting"), TextDim, dim: true); _progressRows.Add((p, status)); }
            else if (p.Action == ProgramAction.SecondMonitor) SetStatus(status, L.T("на второй монитор", "to second monitor"), Blue);
            else SetStatus(status, L.T("не трогаем", "left alone"), TextMuted);
            _list.Children.Add(Row(p.DisplayName, p.Id, p.ExePath, Detail(p), status));
        }
        UpdateProgress(new SwitchProgress(L.T("Готовим отдых…", "Getting rest ready…"), 0, _progressRows.Count));
    }

    public void UpdateProgress(SwitchProgress p)
    {
        _progressText.Text = p.Total > 0 && _mode == Mode.Progress ? L.T($"{p.Text} {Math.Min(p.Done + 1, p.Total)} из {p.Total}", $"{p.Text} {Math.Min(p.Done + 1, p.Total)} of {p.Total}") : p.Text;
        _progressBar.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
        if (_mode != Mode.Progress) return;
        _panelSummary.Text = L.T($"закрыто {p.Done} из {_progressRows.Count}", $"{p.Done} of {_progressRows.Count} closed");
        // По порядку: закрытые, та, что закрываем сейчас, остальные в очереди.
        for (var i = 0; i < _progressRows.Count; i++)
        {
            var status = _progressRows[i].Status;
            if (i < p.Done) SetStatus(status, L.T("закрыта", "closed"), Good);
            else if (i == p.Done) SetStatus(status, L.T("закрываем…", "closing…"), Warm, spinner: true);
            else SetStatus(status, L.T("в очереди", "waiting"), TextDim, dim: true);
        }
    }

    public void ShowRest(RestReport report)
    {
        _mode = Mode.Rest;
        _panelTitle.Text = L.T("Программы", "Apps");
        _headline.Text = L.T("Отдых. Рабочее закрыто.", "Rest. Work is closed.");
        ((Border)_badge.Tag).Visibility = Visibility.Collapsed;
        _lead.Text = L.T("На основном мониторе — тихий экран, на втором — ваши плитки.", "A quiet screen on the main monitor, your tiles on the second one.") + (DoNotDisturb ? L.T(" Уведомления молчат.", " Notifications are muted.") : "");
        SetMainScreen(rest: true);
        _ramTitle.Text = L.T("Освободилось", "Memory freed");
        _ramValue.Text = report.FreedBytes > 0 ? "+" + Sizes.Format(report.FreedBytes) : "—";
        _ramValue.Foreground = Brush(Good);
        SetGo(L.T("Вернуться к работе — запустить закрытое", "Back to work — reopen closed apps"), enabled: true);
        _notice.Text = "";
        ShowExtras(progress: false, offer: false, error: false);
        _test.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
        _choose.Visibility = Visibility.Collapsed;
        var tiles = TilePlan.Ordered(Programs).ToList();
        _secondHint.Text = tiles.Count > 0 ? L.T("Сейчас на втором мониторе только эти окна", "Only these windows are on the second monitor now") : "";
        RenderSecondScreen(tiles, interactive: false);
        _tilesValue.Text = tiles.Count > 0 ? L.T($"{Math.Min(tiles.Count, Math.Max(1, MaxTiles))} из {Math.Max(1, MaxTiles)}", $"{Math.Min(tiles.Count, Math.Max(1, MaxTiles))} of {Math.Max(1, MaxTiles)}") : "—";

        _list.Children.Clear();
        var closed = report.Results.Count(r => r.IsGone);
        _panelSummary.Text = L.T($"закрыто {closed} · осталось {Math.Max(0, Programs.Count - closed)}", $"{closed} closed · {Math.Max(0, Programs.Count - closed)} left");
        foreach (var r in report.Results)
        {
            var p = Programs.FirstOrDefault(x => x.Id == r.ProgramId);
            var detail = p is null ? null : Detail(p);
            var status = new Grid();
            switch (r.Outcome)
            {
                case CloseOutcome.InBackground:
                    SetStatus(status, L.T("спряталась в трей", "hid in the tray"), Amber);
                    _list.Children.Add(TrayRow(r, p, detail, status));
                    continue;
                case CloseOutcome.StillOpen:
                    SetStatus(status, L.T("не закрылась", "didn't close"), Bad);
                    _list.Children.Add(ForceRow(r, p, detail, status));
                    continue;
                case CloseOutcome.Closed: SetStatus(status, L.T("закрыта", "closed"), Good); break;
                case CloseOutcome.Terminated: SetStatus(status, L.T("завершена", "ended"), Good); break;
                case CloseOutcome.Elevated: SetStatus(status, L.T("осталась · от администратора", "still open · runs as admin"), TextMuted); break;
                default: SetStatus(status, L.T("осталась", "still open"), TextMuted); break;
            }
            _list.Children.Add(Row(r.DisplayName, r.ProgramId, p?.ExePath, detail, status));
        }
        foreach (var p in Programs.Where(p => report.Results.All(r => r.ProgramId != p.Id)))
        {
            var status = new Grid();
            if (p.Action == ProgramAction.SecondMonitor) SetStatus(status, L.T("на втором мониторе", "on second monitor"), Blue);
            else SetStatus(status, L.T("осталась", "still open"), TextMuted);
            _list.Children.Add(Row(p.DisplayName, p.Id, p.ExePath, Detail(p), status));
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(Empty(L.T("Закрывать ничего не пришлось.", "Nothing needed closing.")));
    }

    /// <summary>Отдых продолжается (например, после перезагрузки).</summary>
    public void ShowRestWaiting(IReadOnlyList<(string Id, string Name)>? closed = null)
    {
        _mode = Mode.Rest;
        _headline.Text = L.T("Отдых", "Rest");
        ((Border)_badge.Tag).Visibility = Visibility.Collapsed;
        _lead.Text = L.T("Рабочие программы закрыты. Кнопка ниже запустит их обратно — так же, как было.", "Work apps are closed. The button below will bring them back, just as they were.");
        SetMainScreen(rest: true);
        _ramTitle.Text = L.T("Освободилось", "Memory freed");
        _ramValue.Text = "—";
        _tilesValue.Text = "—";
        SetGo(L.T("Вернуться к работе — запустить закрытое", "Back to work — reopen closed apps"), enabled: true);
        _notice.Text = "";
        ShowExtras(progress: false, offer: false, error: false);
        _test.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
        _choose.Visibility = Visibility.Collapsed;
        RenderSecondScreen([], interactive: false);
        _secondHint.Text = "";
        _panelTitle.Text = L.T("Откроется снова", "Will reopen");
        _panelCount.Text = "";
        _panelSummary.Text = "";
        _list.Children.Clear();
        foreach (var (id, name) in closed ?? [])
        {
            var status = new Grid();
            SetStatus(status, L.T("закрыта", "closed"), TextMuted);
            _list.Children.Add(Row(name, id, null, null, status));
        }
        if (closed is null || closed.Count == 0)
            _list.Children.Add(Empty(L.T("Закрытых программ нет — открывать ничего не придётся.", "No apps were closed, so there's nothing to reopen.")));
    }

    /// <summary>Расписание: «Пора за работу» — вернуться или отложить на 15 минут.</summary>
    public void ShowReturnOffer(string when, IReadOnlyList<(string Id, string Name)>? closed = null)
    {
        ShowRestWaiting(closed);
        _headline.Text = L.T("Отдых. Рабочее закрыто.", "Rest. Work is closed.");
        _offerText.Text = L.T($"По расписанию ({when}). Вернуть рабочие программы сейчас?", $"Scheduled ({when}). Bring your work apps back now?");
        ShowExtras(progress: false, offer: true, error: false);
        _go.Visibility = Visibility.Collapsed;
    }

    public void ShowReturning()
    {
        _mode = Mode.Returning;
        _headline.Text = L.T("Отдых → Работа", "Rest → Work");
        _lead.Text = L.T("Открываем закрытые программы и ставим окна на свои места.", "Reopening closed apps and putting windows back in place.");
        SetGo(L.T("Возвращаемся…", "Returning…"), enabled: false);
        _notice.Text = "";
        ShowExtras(progress: true, offer: false, error: false);
        _cancel.Visibility = Visibility.Collapsed;
        _progressText.Text = L.T("Открываем…", "Opening…");
        _progressBar.Value = 0;
        _panelSummary.Text = "";
    }

    public void ShowWork(ReturnReport report)
    {
        _mode = Mode.Work;
        _headline.Text = L.T("Снова работа", "Back at work");
        _lead.Text = report.Summary;
        SetMainScreen(rest: false);
        var total = report.Launched.Count + report.Failed.Count;
        _ramTitle.Text = L.T("Открыто", "Opened");
        _ramValue.Text = total > 0 ? L.T($"{report.Launched.Count} из {total}", $"{report.Launched.Count} of {total}") : "—";
        _ramValue.Foreground = Brush(report.Failed.Count == 0 ? Good : Amber);
        _dndValue.Text = L.T("Включены", "On");
        _tilesValue.Text = L.T("Убраны", "Removed");
        SetGo(L.T("Готово", "Done"), enabled: true);
        _notice.Text = "";
        ShowExtras(progress: false, offer: false, error: false);
        _test.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
        _choose.Visibility = Visibility.Collapsed;
        _secondScreen.Children.Clear();
        _secondScreen.Children.Add(BigWindow());
        _secondCaption.Inlines.Clear();
        AddCaption(_secondCaption, L.T("Второй", "Second"), L.T("Рабочий стол", "Desktop"));
        _secondHint.Text = L.T("Плитки убраны, окна на своих местах", "Tiles removed, windows back in place");
        _panelTitle.Text = L.T("Программы", "Apps");
        _panelCount.Text = "";
        _panelSummary.Text = total > 0 ? L.T($"открыто {report.Launched.Count} из {total}", $"{report.Launched.Count} of {total} opened") : "";
        _list.Children.Clear();
        foreach (var name in report.Launched)
        {
            var status = new Grid();
            SetStatus(status, L.T("открыта", "opened"), Good);
            _list.Children.Add(Row(name, name, null, null, status));
        }
        foreach (var name in report.Failed)
        {
            var status = new Grid();
            SetStatus(status, L.T("не открылась — откройте вручную", "didn't open — open it yourself"), Bad);
            _list.Children.Add(Row(name, name, null, null, status));
        }
        if (total == 0) _list.Children.Add(Empty(L.T("Запускать ничего не пришлось.", "Nothing needed opening.")));
    }

    /// <summary>Сообщение под кнопкой, не меняя состояния окна.</summary>
    public void SetNotice(string text) => _notice.Text = text;

    public void ShowError(string text)
    {
        _mode = Mode.Error;
        _errorText.Text = text;
        ShowExtras(progress: false, offer: false, error: true);
        SetGo(_go.IsEnabled ? _goText.Text : L.T("Перейти в отдых", "Start resting"), enabled: true);
    }

    // ---------- список программ (состояние «План») ----------

    private void RenderPlanList()
    {
        _panelTitle.Text = L.T("Программы", "Apps");
        _list.Children.Clear();
        if (Programs.Count == 0)
        {
            _list.Children.Add(Empty(L.T("Открытых программ нет.", "No apps are open.")));
            return;
        }
        foreach (var p in Programs) _list.Children.Add(PlanRow(p));
    }

    private static string Detail(RunningProgram p) => $"{p.Note} · {Sizes.Format(p.PrivateBytes)}";

    /// <summary>Строка программы: значок, имя, «окна · память», справа — переключатель действия.</summary>
    private UIElement PlanRow(RunningProgram p)
    {
        FrameworkElement right;
        if (p.IsElevated)
        {
            right = new Border
            {
                Child = Text(L.T("Запущена от администратора — закроете только вы", "Runs as administrator — only you can close it"), 12, Hex("#CBBB9E"), wrap: true),
                Width = 290, Padding = new Thickness(11, 7, 11, 7), CornerRadius = new CornerRadius(9),
                Background = Vertical(Color.FromArgb(26, 203, 187, 158), Color.FromArgb(10, 203, 187, 158)),
                BorderBrush = Brush(Color.FromArgb(46, 203, 187, 158)), BorderThickness = new Thickness(1),
            };
        }
        else
        {
            var tilesFull = p.Action != ProgramAction.SecondMonitor && TilePlan.Ordered(Programs).Count() >= MaxTiles;
            var options = p.IsBackground
                ? new[] { (ProgramAction.Close, L.T("Завершить в отдыхе", "End during rest"), true), (ProgramAction.Keep, L.T("Не трогать", "Leave alone"), true) }
                : new[]
                {
                    (ProgramAction.Close, L.T("Закрыть", "Close"), true),
                    (ProgramAction.SecondMonitor, L.T("На второй монитор", "Second monitor"), TilesAvailable && !tilesFull),
                    (ProgramAction.Keep, L.T("Не трогать", "Leave alone"), true),
                };
            right = Segmented(p, options);
        }
        var box = new StackPanel();
        var row = Row(p.DisplayName, p.Id, p.ExePath, Detail(p), right, wrap: false);
        box.Children.Add(row);

        if (p.Action == ProgramAction.Close && !p.IsBackground && !p.IsElevated)
        {
            // «Насовсем» — короткой ссылкой в строке подписи: «2 окна · 3,0 ГБ · откроется после отдыха».
            var names = (StackPanel)row.Children[1];
            var meta = (TextBlock)names.Children[1];
            names.Children.Remove(meta);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(meta);
            line.Children.Add(Text("·", 12, Hex("#5A5F6A")));
            var toggle = new HyperlinkButton
            {
                Content = Text(p.NoRelaunch ? L.T("не откроется после отдыха", "won't reopen after rest") : L.T("откроется после отдыха", "reopens after rest"), 12, p.NoRelaunch ? Warm : Hex("#8F95A1")),
                Padding = new Thickness(0), MinHeight = 0,
            };
            ToolTipService.SetToolTip(toggle, p.NoRelaunch
                ? L.T("Закроем насовсем: при возврате к работе не откроем. Щёлкните, чтобы открывать снова", "Closed for good: it won't reopen when you're back at work. Click to reopen it again")
                : L.T("Щёлкните, чтобы закрыть насовсем: при возврате к работе не открывать", "Click to close for good: don't reopen when you're back at work"));
            toggle.Click += (_, _) => { p.NoRelaunch = !p.NoRelaunch; ActionChanged?.Invoke(p); RenderPlanList(); };
            line.Children.Add(toggle);
            names.Children.Add(line);
            if (p.Known?.HidesToTray == true || p.Windows.Count == 0)
                box.Children.Add(Check(L.T("если вместо закрытия уйдёт в значок у часов — завершить", "if it hides next to the clock instead of closing, end it"), p.AllowTerminate,
                    v => { p.AllowTerminate = v; ActionChanged?.Invoke(p); },
                    p.Known?.TerminateHint ?? L.T("Завершим, только если у программы не осталось окон и она ни о чём не спрашивает", "We'll end it only if the app has no windows left and isn't asking anything")));
        }
        return box;
    }

    /// <summary>Переключатель «Закрыть · На второй монитор · Не трогать»: тёмная дорожка, выбранный — подсвечен.</summary>
    private FrameworkElement Segmented(RunningProgram p, (ProgramAction Action, string Text, bool Enabled)[] options)
    {
        var track = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (action, text, enabled) in options)
        {
            var selected = p.Action == action;
            var label = Text(text, 12.5, selected ? SelectedText(action) : enabled ? Hex("#9AA0AC") : Hex("#5A5F6A"), selected ? FontWeights.SemiBold : FontWeights.Medium);
            var b = new Button
            {
                Content = label, Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(selected ? 1 : 0), IsEnabled = enabled || selected,
                Background = selected ? SelectedFill(action) : Brush(Colors.Transparent),
                BorderBrush = selected ? Brush(SelectedEdge(action)) : Brush(Colors.Transparent),
            };
            b.Resources["ButtonBackgroundPointerOver"] = selected ? SelectedFill(action) : Brush(Color.FromArgb(18, 255, 255, 255));
            b.Resources["ButtonBackgroundPressed"] = selected ? SelectedFill(action) : Brush(Color.FromArgb(26, 255, 255, 255));
            b.Resources["ButtonBackgroundDisabled"] = Brush(Colors.Transparent);
            b.Resources["ButtonBorderBrushPointerOver"] = selected ? Brush(SelectedEdge(action)) : Brush(Colors.Transparent);
            b.Resources["ButtonBorderBrushPressed"] = selected ? Brush(SelectedEdge(action)) : Brush(Colors.Transparent);
            b.Resources["ButtonBorderBrushDisabled"] = Brush(Colors.Transparent);
            if (!enabled && action == ProgramAction.SecondMonitor)
                ToolTipService.SetToolTip(b, !_hasSecondMonitor ? L.T("Второй монитор не подключён", "Second monitor isn't connected")
                    : MaxTiles == 0 ? L.T("Плитки выключены в настройках", "Tiles are turned off in Settings")
                    : L.T($"На втором мониторе уже {Sizes.Plural(MaxTiles, "плитка", "плитки", "плиток")} — уберите одну или разрешите больше в настройках", $"The second monitor already has {L.Plural(MaxTiles, "плитка", "плитки", "плиток", "tile", "tiles")}. Remove one or allow more in Settings"));
            b.Click += (_, _) =>
            {
                if (selected) return;
                SetAction(p, action);
                RenderPlanList(); // галочки появляются и пропадают, у других строк меняется «есть ли место»
            };
            track.Children.Add(b);
        }
        return new Border
        {
            Child = track, Padding = new Thickness(3), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center,
            Background = Vertical(Color.FromArgb(107, 0, 0, 0), Color.FromArgb(71, 0, 0, 0)),
            BorderBrush = Brush(Color.FromArgb(14, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
    }

    private static Color SelectedText(ProgramAction a) => a switch
    {
        ProgramAction.Close => WarmText,
        ProgramAction.SecondMonitor => Hex("#DCE7FF"),
        _ => Hex("#F1EEE8"),
    };

    private static Brush SelectedFill(ProgramAction a) => a switch
    {
        ProgramAction.Close => Vertical(Color.FromArgb(82, 246, 199, 135), Color.FromArgb(41, 234, 165, 96)),
        ProgramAction.SecondMonitor => Vertical(Color.FromArgb(110, 98, 124, 178), Color.FromArgb(70, 70, 92, 140)),
        _ => Vertical(Color.FromArgb(46, 255, 255, 255), Color.FromArgb(26, 255, 255, 255)),
    };

    private static Color SelectedEdge(ProgramAction a) => a switch
    {
        ProgramAction.Close => Color.FromArgb(97, 238, 179, 108),
        ProgramAction.SecondMonitor => Color.FromArgb(110, 143, 180, 240),
        _ => Color.FromArgb(40, 255, 255, 255),
    };

    /// <summary>Новое действие программы; плитки второго монитора перенумеровываются, выбор запоминается.</summary>
    private void SetAction(RunningProgram p, ProgramAction action)
    {
        var tilesBefore = TilePlan.Ordered(Programs).ToList();
        p.Action = action;
        if (action != ProgramAction.Close) p.NoRelaunch = false;
        if (action == ProgramAction.SecondMonitor) { tilesBefore.Add(p); TilePlan.Renumber(tilesBefore); }
        else { p.TileOrder = null; tilesBefore.Remove(p); TilePlan.Renumber(tilesBefore); }
        foreach (var t in tilesBefore.Append(p).Distinct()) ActionChanged?.Invoke(t);
        _notice.Text = "";
        RefreshForecast();
    }

    /// <summary>Общая строка списка: значок, имя, подпись, справа — переключатель или статус.</summary>
    private static Grid Row(string name, string id, string? exePath, string? detail, FrameworkElement right, bool wrap = false)
    {
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(10, 8, 6, 8), MinHeight = 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Avatar(name, id, exePath, 30);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var title = Text(name, 14.5, Hex("#F4F0E9"), FontWeights.SemiBold, wrap: wrap);
        names.Children.Add(title);
        if (detail is not null) names.Children.Add(Text(detail, 12, Hex("#8F95A1")));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Статус справа: цветная точка и текст (или колёсико, пока закрываем).</summary>
    private static void SetStatus(Grid host, string text, Color color, bool spinner = false, bool dim = false)
    {
        host.Children.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Opacity = dim ? 0.6 : 1 };
        if (spinner) row.Children.Add(new ProgressRing { IsActive = true, Width = 11, Height = 11, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center });
        else row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Brush(color), VerticalAlignment = VerticalAlignment.Center });
        var label = Text(text, 12.5, color);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        host.Children.Add(row);
    }

    /// <summary>Программа спряталась в трей: «Завершить» — завершить сейчас и разрешить в следующий раз.</summary>
    private UIElement TrayRow(CloseResult r, RunningProgram? p, string? detail, Grid status)
    {
        var box = new StackPanel();
        box.Children.Add(Row(r.DisplayName, r.ProgramId, p?.ExePath, detail, status));
        var button = SmallButton(L.T("Завершить", "End"));
        button.Margin = new Thickness(54, -6, 0, 8);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTipService.SetToolTip(button, L.T("Завершим сейчас и запомним, что её можно завершать", "We'll end it now and remember that it's OK to end it"));
        button.Click += (_, _) =>
        {
            TerminateRequested?.Invoke(r.ProgramId, false);
            button.Visibility = Visibility.Collapsed;
            SetStatus(status, L.T("завершена", "ended"), Good);
        };
        box.Children.Add(button);
        return box;
    }

    /// <summary>Не закрылась (окна или несохранённое): «Завершить принудительно» — по явной просьбе, с предупреждением.</summary>
    private UIElement ForceRow(CloseResult r, RunningProgram? p, string? detail, Grid status)
    {
        var box = new StackPanel();
        box.Children.Add(Row(r.DisplayName, r.ProgramId, p?.ExePath, detail, status));
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(Bang(16));
        content.Children.Add(Text(L.T("Завершить принудительно — несохранённое пропадёт", "Force end — unsaved work will be lost"), 12.5, Hex("#F2B3A0"), FontWeights.SemiBold));
        var button = new Button
        {
            Content = content, Margin = new Thickness(54, -6, 0, 8), Padding = new Thickness(10, 5, 12, 5), CornerRadius = new CornerRadius(8),
            Background = Brush(Color.FromArgb(34, 232, 110, 90)), BorderBrush = Brush(Color.FromArgb(100, 232, 110, 90)), BorderThickness = new Thickness(1),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Brush(Color.FromArgb(56, 232, 110, 90));
        button.Resources["ButtonBackgroundPressed"] = Brush(Color.FromArgb(70, 232, 110, 90));
        button.Click += (_, _) =>
        {
            TerminateRequested?.Invoke(r.ProgramId, true);
            button.Visibility = Visibility.Collapsed;
            SetStatus(status, L.T("завершена · запомнили на будущее", "ended · remembered for next time"), Good);
        };
        box.Children.Add(button);
        return box;
    }

    // ---------- второй монитор: плитки ----------

    private void RefreshForecast()
    {
        if (_mode != Mode.Plan) return;
        var close = Programs.Where(p => p.Action == ProgramAction.Close && !p.IsElevated).ToList();
        _ramTitle.Text = L.T("Освободится памяти", "Memory to free");
        _ramValue.Text = close.Count > 0 ? "≈" + Sizes.Format(close.Sum(p => p.PrivateBytes)) : "—";
        _ramValue.Foreground = Brush(Hex("#F3C58C"));
        ToolTipService.SetToolTip(_ramValue, L.T("Примерно освободится. Точную цифру покажем после перехода.", "An estimate. We'll show the exact amount after switching."));
        _dndTitle.Text = L.T("Уведомления", "Notifications");
        _dndValue.Text = DoNotDisturb ? L.T("Не беспокоить", "Do not disturb") : L.T("Как сейчас", "As now");
        _tilesTitle.Text = L.T("Плитки", "Tiles");

        var tiles = TilePlan.Ordered(Programs).ToList();
        _tilesValue.Text = !_hasSecondMonitor ? L.T("нет монитора", "no monitor") : MaxTiles == 0 ? L.T("выключены", "off") : L.T($"{Math.Min(tiles.Count, MaxTiles)} из {MaxTiles}", $"{Math.Min(tiles.Count, MaxTiles)} of {MaxTiles}");
        var second = Programs.Count(p => p.Action == ProgramAction.SecondMonitor);
        var keep = Programs.Count(p => p.Action == ProgramAction.Keep || p.IsElevated);
        _panelSummary.Text = L.T($"закроются {close.Count} · на второй монитор {second} · не трогаем {keep}", $"{close.Count} to close · {second} to second monitor · {keep} left alone");

        _secondCaption.Inlines.Clear();
        AddCaption(_secondCaption, L.T("Второй", "Second"), L.T("Живые плитки", "Live tiles"));
        if (!TilesAvailable)
        {
            _secondScreen.Children.Clear();
            var box = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14) };
            box.Children.Add(Text(_hasSecondMonitor ? L.T("Плитки выключены", "Tiles are off") : L.T("Второй монитор не подключён", "Second monitor isn't connected"), 13, TextMain, FontWeights.SemiBold));
            box.Children.Add(Text(_hasSecondMonitor ? L.T("Включить можно в настройках. Окна на втором мониторе останутся как есть.", "You can turn them on in Settings. Windows on the second monitor stay as they are.")
                : L.T("Отдых работает и без него. Плитки появятся, когда подключите.", "Rest works without it too. Tiles will appear once you connect one."), 11.5, TextMuted, wrap: true));
            Grid.SetRowSpan(box, 2);
            Grid.SetColumnSpan(box, 2);
            _secondScreen.Children.Add(box);
            _secondHint.Text = "";
            return;
        }
        RenderSecondScreen(tiles, interactive: true);
        _secondHint.Text = tiles.Count == 0 ? L.T("Пусто. «+» — поставить программу", "Empty. Use “+” to add an app") : L.T("Перетащите плитку или щёлкните две, чтобы поменять местами", "Drag a tile, or click two to swap them");
    }

    private int _picked = -1; // плитка, выбранная первым щелчком (для обмена местами)

    /// <summary>2×2 места: плитки можно перетаскивать или менять двумя щелчками, «×» — убрать, «+» — поставить программу.</summary>
    private void RenderSecondScreen(IReadOnlyList<RunningProgram> tiles, bool interactive)
    {
        _secondScreen.Children.Clear();
        for (var slot = 0; slot < TilePlan.Max; slot++)
        {
            // Места сверх разрешённого настройками — пустые и неактивные.
            FrameworkElement cell = slot >= MaxTiles ? EmptyCell(false)
                : slot < tiles.Count ? TileCell(tiles[slot], slot, interactive) : EmptyCell(interactive);
            if (slot >= MaxTiles) cell.Opacity = 0.35;
            Grid.SetRow(cell, slot / 2);
            Grid.SetColumn(cell, slot % 2);
            _secondScreen.Children.Add(cell);
        }
    }

    private FrameworkElement TileCell(RunningProgram p, int slot, bool interactive)
    {
        var picked = interactive && slot == _picked;
        var content = new Grid { Padding = new Thickness(10, 9, 6, 9) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        var icon = Avatar(p.DisplayName, p.Id, p.ExePath, 22);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(icon);
        var name = Text(p.DisplayName, 12.5, TextMain, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(name, 1);
        content.Children.Add(name);
        Button? remove = null;
        if (interactive)
        {
            remove = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 9, Foreground = Brush(TextMuted) }, Padding = new Thickness(5), MinWidth = 0, MinHeight = 0,
                Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -2, 0),
            };
            ToolTipService.SetToolTip(remove, L.T("Убрать со второго монитора", "Remove from second monitor"));
            remove.Click += (_, _) => { _picked = -1; SetAction(p, ProgramAction.Keep); RenderPlanList(); };
            content.Children.Add(remove);
        }

        var edge = picked ? Warm : Color.FromArgb(28, 255, 255, 255);
        var cell = new Border
        {
            CornerRadius = new CornerRadius(10), Child = content,
            Background = Vertical(Color.FromArgb(28, 255, 255, 255), Color.FromArgb(11, 255, 255, 255)),
            BorderBrush = Brush(edge), BorderThickness = new Thickness(picked ? 1.5 : 1),
            CanDrag = interactive, AllowDrop = interactive,
        };
        if (picked) cell.Background = Vertical(Color.FromArgb(46, 238, 179, 108), Color.FromArgb(14, 238, 179, 108));
        if (!interactive) return cell;

        ToolTipService.SetToolTip(cell, p.DisplayName);
        cell.Tapped += (_, e) =>
        {
            if (remove is not null && e.OriginalSource is DependencyObject d && IsInside(d, remove)) return;
            if (_picked < 0) _picked = slot;
            else if (_picked == slot) _picked = -1;
            else { Swap(_picked, slot); _picked = -1; }
            RefreshForecast();
        };
        cell.DragStarting += (_, e) => { e.Data.SetText(slot.ToString()); e.Data.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move; };
        cell.DragOver += (_, e) => { e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move; cell.BorderBrush = Brush(Warm); };
        cell.DragLeave += (_, _) => cell.BorderBrush = Brush(edge);
        cell.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            if (int.TryParse(await e.DataView.GetTextAsync(), out var from)) { _picked = -1; Swap(from, slot); RefreshForecast(); }
        };
        return cell;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    /// <summary>Пустое место: пунктирная рамка; «+» — выбрать программу из открытых.</summary>
    private FrameworkElement EmptyCell(bool interactive)
    {
        var grid = new Grid();
        grid.Children.Add(new Rectangle
        {
            RadiusX = 10, RadiusY = 10, Stroke = Brush(Color.FromArgb(56, 255, 255, 255)), StrokeThickness = 1, StrokeDashArray = [4, 3],
            Fill = Brush(Color.FromArgb(40, 0, 0, 0)),
        });
        if (!interactive) return grid;
        var candidates = Programs.Where(p => !p.IsBackground && !p.IsElevated && p.Windows.Count > 0 && p.Action != ProgramAction.SecondMonitor).ToList();
        var plus = new Button
        {
            Content = Text("+", 24, Hex("#AEB3BF"), FontWeights.Light),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10),
            IsEnabled = candidates.Count > 0,
        };
        plus.Resources["ButtonBackgroundPointerOver"] = Brush(Color.FromArgb(16, 238, 179, 108));
        plus.Resources["ButtonBackgroundPressed"] = Brush(Color.FromArgb(28, 238, 179, 108));
        plus.Resources["ButtonBackgroundDisabled"] = Brush(Colors.Transparent);
        ToolTipService.SetToolTip(plus, L.T("Добавить на второй монитор", "Add to second monitor"));
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = L.T("Добавить на второй монитор", "Add to second monitor"), IsEnabled = false });
        foreach (var p in candidates)
        {
            var item = new MenuFlyoutItem { Text = p.DisplayName };
            item.Click += (_, _) => { SetAction(p, ProgramAction.SecondMonitor); RenderPlanList(); };
            menu.Items.Add(item);
        }
        plus.Flyout = menu;
        grid.Children.Add(plus);
        return grid;
    }

    private void Swap(int a, int b)
    {
        TilePlan.Swap(Programs, a, b);
        foreach (var t in TilePlan.Ordered(Programs)) ActionChanged?.Invoke(t);
    }

    // ---------- кнопка и мелочи ----------

    private void OnGo()
    {
        switch (_mode)
        {
            case Mode.Plan: GoRequested?.Invoke(); break;
            case Mode.Rest: ReturnRequested?.Invoke(); break;
            default: AppWindow.Hide(); break;
        }
    }

    private void SetGo(string text, bool enabled)
    {
        _go.Visibility = Visibility.Visible;
        _goText.Text = text;
        _go.IsEnabled = enabled;
        _goText.Foreground = Brush(enabled ? Hex("#24170C") : TextMuted);
    }

    private void ShowExtras(bool progress, bool offer, bool error)
    {
        _progressBox.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        _offer.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
        _error.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Основной монитор: рабочий стол с окнами или тихий экран с часами.</summary>
    private void SetMainScreen(bool rest)
    {
        _mainScreen.Children.Clear();
        _mainCaption.Inlines.Clear();
        if (rest)
        {
            var sky = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            sky.GradientStops.Add(new GradientStop { Color = Hex("#3A3560"), Offset = 0 });
            sky.GradientStops.Add(new GradientStop { Color = Hex("#7A5E86"), Offset = 0.55 });
            sky.GradientStops.Add(new GradientStop { Color = Hex("#D49A78"), Offset = 1 });
            _mainScreen.Background = sky;
            var figure = new PathFigure { StartPoint = new(0, 16), IsClosed = true };
            foreach (var (x, y) in new[] { (28.0, 6.0), (52, 12), (80, 0), (106, 9), (130, 4), (130, 34), (0, 34) })
                figure.Segments.Add(new LineSegment { Point = new(x, y) });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            var hills = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry, Fill = Brush(Hex("#2A2440")), Stretch = Stretch.Fill, VerticalAlignment = VerticalAlignment.Bottom, Height = 30,
            };
            _mainScreen.Children.Add(hills);
            var clock = Text(DateTime.Now.ToString("HH:mm"), 15, Hex("#F4EDE6"), FontWeights.Normal);
            clock.Margin = new Thickness(8, 6, 0, 0);
            _mainScreen.Children.Add(clock);
            AddCaption(_mainCaption, L.T("Основной", "Main"), L.T("Тихий экран", "Quiet screen"));
        }
        else
        {
            _mainScreen.Background = Vertical(Hex("#3B4256"), Hex("#262A36"));
            _mainScreen.Children.Add(new Border { Width = 56, Height = 34, CornerRadius = new CornerRadius(3), Background = Brush(Color.FromArgb(46, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 10, 0, 0) });
            _mainScreen.Children.Add(new Border { Width = 62, Height = 38, CornerRadius = new CornerRadius(3), Background = Brush(Color.FromArgb(56, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(44, 26, 0, 0) });
            var dock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
            for (var i = 0; i < 4; i++) dock.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(1), Background = Brush(Color.FromArgb(120, 255, 255, 255)) });
            _mainScreen.Children.Add(dock);
            AddCaption(_mainCaption, L.T("Основной", "Main"), L.T("Рабочий стол", "Desktop"));
            _dndValue.Text = DoNotDisturb ? L.T("Не беспокоить", "Do not disturb") : L.T("Как сейчас", "As now");
        }
    }

    private static Border BigWindow() => new()
    {
        Margin = new Thickness(26, 18, 26, 22), CornerRadius = new CornerRadius(4), Background = Brush(Color.FromArgb(40, 255, 255, 255)),
        BorderBrush = Brush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(1),
    };

    /// <summary>Монитор: рамка с бликом, экран, подставка; подпись «Основной · Рабочий стол».</summary>
    private static StackPanel MonitorFigure(Grid screen, double width, double height, TextBlock caption, TextBlock? hint, double rotate)
    {
        var glare = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        glare.GradientStops.Add(new GradientStop { Color = Color.FromArgb(30, 255, 255, 255), Offset = 0 });
        glare.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 0.45 });
        var face = new Grid();
        face.Children.Add(screen);
        face.Children.Add(new Border { Background = glare, IsHitTestVisible = false });
        var frame = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(10), Padding = new Thickness(5),
            Background = Vertical(Hex("#2E313B"), Hex("#17191F")), BorderBrush = Brush(Color.FromArgb(36, 255, 255, 255)), BorderThickness = new Thickness(1),
            Child = new Border { CornerRadius = new CornerRadius(6), Child = face, Background = Brush(Hex("#0E1015")) },
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var stand = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Width = width };
        stand.Children.Add(new Border { Width = Math.Max(14, width * 0.1), Height = 10, Background = Vertical(Hex("#3A3D47"), Hex("#22242B")), HorizontalAlignment = HorizontalAlignment.Center });
        stand.Children.Add(new Border { Width = width * 0.42, Height = 4, CornerRadius = new CornerRadius(2), Background = Vertical(Hex("#4A4D57"), Hex("#2A2C33")), HorizontalAlignment = HorizontalAlignment.Center });
        var monitor = new StackPanel { Projection = new PlaneProjection { RotationY = rotate } };
        monitor.Children.Add(frame);
        monitor.Children.Add(stand);
        var figure = new StackPanel { Spacing = 10 };
        figure.Children.Add(monitor);
        var captions = new StackPanel { Spacing = 3 };
        captions.Children.Add(caption);
        if (hint is not null) captions.Children.Add(hint);
        figure.Children.Add(captions);
        return figure;
    }

    private static TextBlock Caption(string strong, string rest)
    {
        var t = new TextBlock { FontSize = 12.5, Foreground = Brush(TextMain) };
        AddCaption(t, strong, rest);
        return t;
    }

    private static void AddCaption(TextBlock t, string strong, string rest)
    {
        t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = strong, FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + rest, Foreground = Brush(TextMuted) });
    }

    private static Border StatCard(TextBlock title, TextBlock value, int column)
    {
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(title);
        s.Children.Add(value);
        var card = new Border
        {
            Child = s, Padding = new Thickness(14), CornerRadius = new CornerRadius(14),
            Background = Vertical(Color.FromArgb(17, 255, 255, 255), Color.FromArgb(5, 255, 255, 255)),
            BorderBrush = Vertical(Color.FromArgb(41, 255, 255, 255), Color.FromArgb(8, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        Grid.SetColumn(card, column);
        return card;
    }

    /// <summary>Большая тёплая кнопка: градиент, светлая кромка сверху.</summary>
    private static Button GoButton(TextBlock text)
    {
        var fill = Vertical(Hex("#F7CA8C"), Hex("#E8A05B"));
        var hover = Vertical(Hex("#F9D39C"), Hex("#EDAA68"));
        var pressed = Vertical(Hex("#EDB878"), Hex("#DC944F"));
        var b = new Button
        {
            Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Height = 58, CornerRadius = new CornerRadius(13), Background = fill,
            BorderBrush = Brush(Color.FromArgb(153, 120, 70, 30)), BorderThickness = new Thickness(1),
        };
        b.Resources["ButtonBackground"] = fill;
        b.Resources["ButtonBackgroundPointerOver"] = hover;
        b.Resources["ButtonBackgroundPressed"] = pressed;
        b.Resources["ButtonBackgroundDisabled"] = Vertical(Color.FromArgb(20, 255, 255, 255), Color.FromArgb(8, 255, 255, 255));
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Color.FromArgb(153, 120, 70, 30));
        b.Resources["ButtonBorderBrushPressed"] = Brush(Color.FromArgb(153, 120, 70, 30));
        b.Resources["ButtonBorderBrushDisabled"] = Brush(Color.FromArgb(24, 255, 255, 255));
        return b;
    }

    private static Button SmallButton(string text)
    {
        var b = new Button
        {
            Content = Text(text, 12.5, TextMain, FontWeights.SemiBold), Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(8),
            Background = Vertical(Color.FromArgb(26, 255, 255, 255), Color.FromArgb(10, 255, 255, 255)),
            BorderBrush = Brush(Color.FromArgb(36, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        b.Resources["ButtonBackgroundPointerOver"] = Brush(Color.FromArgb(36, 255, 255, 255));
        b.Resources["ButtonBackgroundPressed"] = Brush(Color.FromArgb(46, 255, 255, 255));
        return b;
    }

    private static UIElement Check(string text, bool value, Action<bool> changed, string tip)
    {
        var check = new CheckBox
        {
            Content = Text(text, 12.5, Hex("#AEB3BF"), wrap: true), IsChecked = value,
            Margin = new Thickness(54, -6, 0, 2), MinHeight = 26, Padding = new Thickness(6, 0, 0, 0),
        };
        foreach (var key in new[] { "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed" })
            check.Resources[key] = Brush(Hex("#E8A05B"));
        foreach (var key in new[] { "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed" })
            check.Resources[key] = Brush(Hex("#24170C"));
        check.Checked += (_, _) => changed(true);
        check.Unchecked += (_, _) => changed(false);
        ToolTipService.SetToolTip(check, tip);
        return check;
    }

    private static Border Bang(double size = 18) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brush(Hex("#E8836B")), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "!", FontSize = size * 0.65, FontWeight = FontWeights.Bold, Foreground = Brush(Hex("#24170C")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static TextBlock Empty(string text)
    {
        var t = Text(text, 13.5, TextMuted, wrap: true);
        t.Margin = new Thickness(12, 12, 12, 12);
        return t;
    }

    /// <summary>Значок программы: две буквы на её цвете с объёмом; когда Windows отдаст настоящий значок — он.</summary>
    private static Border Avatar(string name, string id, string? exePath, double size)
    {
        var baseColor = ProgramColor(id);
        var light = Color.FromArgb(255, (byte)Math.Min(255, baseColor.R + 40), (byte)Math.Min(255, baseColor.G + 40), (byte)Math.Min(255, baseColor.B + 40));
        var letters = Text(Initials(name), size * 0.4, Colors.White, FontWeights.SemiBold);
        letters.HorizontalAlignment = HorizontalAlignment.Center;
        letters.VerticalAlignment = VerticalAlignment.Center;
        var box = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Child = letters,
            Background = Vertical(light, baseColor), BorderBrush = Brush(Color.FromArgb(46, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) _ = LoadIconAsync(box, exePath, size);
        return box;
    }

    /// <summary>«Microsoft Outlook» → «Ou», «Visual Studio Code» → «VS», «1С:Предприятие» → «1С».</summary>
    private static string Initials(string name)
    {
        var words = name.Split([' ', ':', '-', '—'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
        if (words.Count == 0) return "?";
        if (words.Count >= 2 && char.IsUpper(words[0][0]) && char.IsUpper(words[1][0]) && words[0].Length > 1 && !char.IsUpper(words[0][1]))
            return $"{words[0][0]}{words[1][0]}";
        var w = words[0];
        return w.Length >= 2 ? $"{char.ToUpper(w[0])}{(char.IsLetter(w[1]) ? char.ToLower(w[1]) : w[1])}" : w.ToUpper();
    }

    private static Brush WindowBackground() => Vertical(Hex("#1B1E28"), Hex("#101217"));

    /// <summary>Размер по макету (1440×900 при 100%), с учётом масштаба монитора, по центру.</summary>
    public void SizeToDesign(bool fitScreen = true)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        // Для скриншотов — ровно размер макета, даже если экран меньше.
        var w = (int)(fitScreen ? Math.Min(DesignWidth * scale, area.Width * 0.95) : DesignWidth * scale);
        var h = (int)(fitScreen ? Math.Min(DesignHeight * scale, area.Height * 0.95) : DesignHeight * scale);
        AppWindow.Resize(new SizeInt32(w, h));
        AppWindow.Move(new PointInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2));
    }

    /// <summary>Для скриншотов: корень ровно по макету, даже если окно меньше (холст не обрезает).</summary>
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
