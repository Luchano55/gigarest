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
using Windows.Graphics;
using Windows.UI;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Окно «Работа → Отдых» по макету «Переключатель»: слева — мониторы, память и большая кнопка,
/// справа — что будет с каждой программой. Крестик прячет окно, программа остаётся в трее.
/// </summary>
public sealed class SwitcherWindow : Window
{
    public const double DesignWidth = 1440, DesignHeight = 900;

    private readonly Grid _root;
    private readonly TextBlock _kicker;
    private readonly TextBlock _headline;
    private readonly TextBlock _lead;
    private readonly Border _mainScreen;
    private readonly TextBlock _mainLabel;
    private readonly Grid _secondScreen;
    private readonly TextBlock _secondCaption;
    private readonly TextBlock _ramValue, _dndValue, _secondValue;
    private readonly TextBlock _ramTitle;
    private readonly Button _go;
    private readonly TextBlock _goText;
    private readonly TextBlock _notice;
    private readonly TextBlock _panelTitle, _panelHint, _panelNote;
    private readonly StackPanel _list;
    private readonly StackPanel _progressBox;
    private readonly TextBlock _progressText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancel;
    private readonly HyperlinkButton _choose;
    private readonly HyperlinkButton _test;
    private Mode _mode;
    private bool _closingForGood;
    private bool _hasSecondMonitor = true;

    private enum Mode { Plan, Progress, Rest, Returning, Work, Error }

    public event Action<RunningProgram>? ActionChanged;
    public event Action? GoRequested;
    public event Action? ReturnRequested;
    public event Action? CancelRequested;
    public event Action? ProgramsRequested;
    public event Action? TestRequested;

    public IReadOnlyList<RunningProgram> Programs { get; private set; } = [];
    /// <summary>Пробный режим: в списке только тестовые окна.</summary>
    public bool IsTest { get; private set; }
    public FrameworkElement Root => _root;

    public SwitcherWindow()
    {
        Title = "GigaRest";
        _root = new Grid
        {
            Background = Brush(PageBg),
            Padding = new Thickness(56, 44, 56, 44),
            ColumnSpacing = 48,
            RequestedTheme = ElementTheme.Light,
        };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---------- левая колонка ----------
        var left = new StackPanel { Spacing = 26 };
        var head = new StackPanel { Spacing = 8 };
        var kicker = _kicker = Text("ОДНА КНОПКА", 13, Muted, FontWeights.Bold);
        kicker.CharacterSpacing = 120;
        _headline = Text("Работа → Отдых", 40, Ink, FontWeights.ExtraBold, wrap: true);
        _lead = Text("", 16, Lead, wrap: true);
        _lead.MaxWidth = 600;
        _lead.HorizontalAlignment = HorizontalAlignment.Left;
        _lead.LineHeight = 24;
        head.Children.Add(kicker);
        head.Children.Add(_headline);
        head.Children.Add(_lead);
        left.Children.Add(head);

        // Мониторы
        var monitors = new Grid { ColumnSpacing = 20 };
        monitors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        monitors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mainLabel = Text("Рабочий стол", 18, Colors.White, FontWeights.Bold);
        _mainLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mainLabel.VerticalAlignment = VerticalAlignment.Center;
        _mainScreen = Screen(_mainLabel, 230);
        var mainFig = Figure(_mainScreen, Text("Основной монитор", 13, Muted));
        _secondScreen = new Grid { RowSpacing = 6, ColumnSpacing = 6, Padding = new Thickness(6) };
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        var second = Screen(_secondScreen, 164);
        second.Background = Brush("#E9EBF2");
        _secondCaption = Text("Второй монитор", 13, Muted, wrap: true);
        var secondFig = Figure(second, _secondCaption);
        secondFig.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(secondFig, 1);
        monitors.Children.Add(mainFig);
        monitors.Children.Add(secondFig);
        left.Children.Add(monitors);

        // Три карточки
        var cards = new Grid { ColumnSpacing = 16 };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
        (_ramTitle, _ramValue) = (Text("Оперативная память", 13, Muted, wrap: true), Text("", 26, Ink, FontWeights.ExtraBold, wrap: true));
        _dndValue = Text("", 26, Ink, FontWeights.ExtraBold, wrap: true);
        _secondValue = Text("", 26, Ink, FontWeights.ExtraBold, wrap: true);
        cards.Children.Add(StatCard(_ramTitle, _ramValue, 0));
        cards.Children.Add(StatCard(Text("Уведомления", 13, Muted, wrap: true), _dndValue, 1));
        cards.Children.Add(StatCard(Text("Второй монитор", 13, Muted, wrap: true), _secondValue, 2));
        left.Children.Add(cards);

        _goText = Text("Перейти в отдых", 17, Colors.White, FontWeights.Bold);
        _go = ColoredButton(_goText, Accent, AccentDark, Colors.White);
        _go.Click += (_, _) => OnGo();
        left.Children.Add(_go);

        _notice = Text("", 14, Lead, wrap: true);
        _notice.LineHeight = 21;
        left.Children.Add(_notice);
        // На маленьком экране левая колонка прокручивается, кнопка не теряется.
        var leftScroll = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        _root.Children.Add(leftScroll);

        // ---------- правая колонка ----------
        var panel = new Grid { RowSpacing = 16 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 4 };
        _panelTitle = Text("Что будет с программами", 20, Ink, FontWeights.ExtraBold, wrap: true);
        _panelHint = Text("щёлкните, чтобы поменять", 13, Muted, wrap: true);
        header.Children.Add(_panelTitle);
        header.Children.Add(_panelHint);
        _choose = new HyperlinkButton { Content = "Выбрать рабочие программы, в том числе незапущенные…", Padding = new Thickness(0, 4, 0, 0), FontSize = 13 };
        _choose.Click += (_, _) => ProgramsRequested?.Invoke();
        header.Children.Add(_choose);
        _test = new HyperlinkButton { Content = "Попробовать на тестовых окнах — ваши программы не тронем", Padding = new Thickness(0, 0, 0, 0), FontSize = 13 };
        _test.Click += (_, _) => TestRequested?.Invoke();
        header.Children.Add(_test);
        panel.Children.Add(header);

        _progressBox = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        _progressText = Text("", 15, Ink, FontWeights.SemiBold, wrap: true);
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, CornerRadius = new CornerRadius(3) };
        _cancel = new Button { Content = "Отменить", HorizontalAlignment = HorizontalAlignment.Left };
        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        _progressBox.Children.Add(_progressText);
        _progressBox.Children.Add(_progressBar);
        _progressBox.Children.Add(_cancel);
        Grid.SetRow(_progressBox, 1);
        panel.Children.Add(_progressBox);

        _list = new StackPanel { Spacing = 2 };
        var scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        _panelNote = Text("Закрываем вежливо, как крестиком: если где-то не сохранено — программа спросит сама. " +
                          "Кнопка «Вернуться к работе» запустит закрытое обратно.", 13, Muted, wrap: true);
        _panelNote.LineHeight = 20;
        Grid.SetRow(_panelNote, 3);
        panel.Children.Add(_panelNote);

        var panelCard = CardBorder(panel, radius: 24, padding: 28);
        Grid.SetColumn(panelCard, 1);
        _root.Children.Add(panelCard);

        Content = _root;

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
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
        _kicker.Text = test ? "ПРОБНЫЙ РЕЖИМ" : "ОДНА КНОПКА";
        _headline.Text = test ? "Проба на тестовых окнах" : "Работа → Отдых";
        _lead.Text = test
            ? "Ваши программы не трогаем — только четыре тестовых окна. Одно закроется само, второе спросит «Сохранить?», " +
              "третье спрячется в трей, четвёртое переедет на второй монитор. Нажмите кнопку и смотрите."
            : "Нажмите кнопку — рабочие программы закроются и освободят память, " +
              "на втором мониторе останется только то, что вы разрешили.";
        SetMainScreen(rest: false);
        _dndValue.Text = "Как сейчас";
        _goText.Text = "Перейти в отдых";
        SetGo(enabled: true, dark: false);
        _notice.Text = notice ?? "";
        ShowPanel("Что будет с программами", "щёлкните, чтобы поменять", progress: false);
        RenderPlanList();
        RefreshForecast();
    }

    public void ShowProgress(IReadOnlyList<RunningProgram> programs)
    {
        _mode = Mode.Progress;
        Programs = programs;
        _headline.Text = "Переходим в отдых…";
        _lead.Text = "Закрываем вежливо. Если программа спросит «Сохранить?» — ответьте ей, мы подождём.";
        SetGo(enabled: false, dark: false);
        _goText.Text = "Переходим…";
        _notice.Text = "";
        _progressText.Text = "Готовим отдых…";
        _progressBar.Value = 0;
        _cancel.Visibility = Visibility.Visible;
        ShowPanel("Переход в отдых", "", progress: true);
        _list.Children.Clear();
        foreach (var p in programs.Where(p => p.Action == ProgramAction.Close))
            _list.Children.Add(StatusRow(p.DisplayName, p.Id, "закрываем…", Muted));
    }

    public void UpdateProgress(SwitchProgress p)
    {
        _progressText.Text = p.Total > 0 && _mode == Mode.Progress ? $"{p.Text} {p.Done} из {p.Total}" : p.Text;
        _progressBar.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
    }

    public void ShowRest(RestReport report)
    {
        _mode = Mode.Rest;
        _headline.Text = "Отдых. Рабочее закрыто.";
        _lead.Text = "Основной экран — ваш. На втором — только разрешённое. " +
                     "Вернуться — эта кнопка, значок в трее или горячая клавиша.";
        SetMainScreen(rest: true);
        _ramTitle.Text = "Освободилось памяти";
        _ramValue.Text = report.FreedBytes > 0 ? "+" + Sizes.Format(report.FreedBytes) : "—";
        _secondValue.Text = report.MovedWindows > 0 ? Sizes.Plural(report.MovedWindows, "окно", "окна", "окон") : "—";
        _goText.Text = "Вернуться к работе — запустить закрытое";
        SetGo(enabled: true, dark: true);
        _notice.Text = report.Summary;
        ShowPanel("Итог", "", progress: false);
        _list.Children.Clear();
        foreach (var r in report.Results)
        {
            var (text, color) = r.Outcome switch
            {
                CloseOutcome.Closed => ("закрыта", Hex("#1E7A44")),
                CloseOutcome.Terminated => ("завершена", Hex("#1E7A44")),
                _ => ("осталась", Hex("#B42318")),
            };
            _list.Children.Add(StatusRow(r.DisplayName, r.ProgramId, text, color, r.IsGone ? null : r.Message));
        }
        if (report.Results.Count == 0)
            _list.Children.Add(Text("Закрывать ничего не пришлось.", 14, Muted));
    }

    /// <summary>Отдых продолжается (например, после перезагрузки).</summary>
    public void ShowRestWaiting()
    {
        _mode = Mode.Rest;
        _headline.Text = "Отдых";
        _lead.Text = "Рабочие программы закрыты. Кнопка ниже запустит их обратно — так же, как было.";
        SetMainScreen(rest: true);
        _ramTitle.Text = "Оперативная память";
        _ramValue.Text = "—";
        _secondValue.Text = "—";
        _goText.Text = "Вернуться к работе — запустить закрытое";
        SetGo(enabled: true, dark: true);
        _notice.Text = "";
        ShowPanel("Отдых", "", progress: false);
        _list.Children.Clear();
    }

    public void ShowReturning()
    {
        _mode = Mode.Returning;
        _headline.Text = "Возвращаемся к работе…";
        _lead.Text = "Открываем закрытое и ставим окна на место.";
        SetGo(enabled: false, dark: true);
        _progressText.Text = "Открываем…";
        _progressBar.Value = 0;
        _cancel.Visibility = Visibility.Collapsed;
        ShowPanel("Возврат", "", progress: true);
        _list.Children.Clear();
    }

    public void ShowWork(ReturnReport report)
    {
        _mode = Mode.Work;
        _headline.Text = "Работа";
        _lead.Text = report.Summary;
        SetMainScreen(rest: false);
        _goText.Text = "Готово";
        SetGo(enabled: true, dark: false);
        _notice.Text = "";
        ShowPanel("Открыто снова", "", progress: false);
        _list.Children.Clear();
        foreach (var name in report.Launched)
            _list.Children.Add(StatusRow(name, name, "открыта", Hex("#1E7A44")));
        foreach (var name in report.Failed)
            _list.Children.Add(StatusRow(name, name, "не открылась", Hex("#B42318"), "Откройте вручную"));
        if (report.Launched.Count + report.Failed.Count == 0)
            _list.Children.Add(Text("Запускать ничего не пришлось.", 14, Muted));
    }

    public void ShowError(string text)
    {
        _mode = Mode.Error;
        _notice.Text = text;
        SetGo(enabled: true, dark: false);
        _goText.Text = "Закрыть";
    }

    // ---------- список «Что будет с программами» ----------

    private void RenderPlanList()
    {
        _list.Children.Clear();
        if (Programs.Count == 0)
        {
            _list.Children.Add(Text("Открытых программ нет.", 14, Muted));
            return;
        }
        foreach (var p in Programs)
            _list.Children.Add(PlanRow(p));
    }

    private UIElement PlanRow(RunningProgram p)
    {
        var box = new StackPanel();
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Avatar(p.DisplayName, p.Id));
        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(p.DisplayName, 15, Ink, FontWeights.Bold, wrap: true));
        names.Children.Add(Text(p.IsElevated ? "от администратора — закроете только вы" : p.Note, 12, Muted));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var ram = Text(Sizes.Format(p.PrivateBytes), 12, Muted);
        ram.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ram, 2);
        grid.Children.Add(ram);
        var badge = Badge(p.Action);
        Grid.SetColumn(badge, 3);
        grid.Children.Add(badge);

        var row = RowButton(grid);
        row.Click += (_, _) =>
        {
            p.Action = Next(p.Action);
            ActionChanged?.Invoke(p);
            var index = _list.Children.IndexOf(box);
            if (index >= 0) _list.Children[index] = PlanRow(p);
            RefreshForecast();
        };
        box.Children.Add(row);

        // Галочка «можно завершать» — для программ, которые прячутся в трей.
        if (p.Action == ProgramAction.Close && (p.Known?.HidesToTray == true || p.Windows.Count == 0))
        {
            var check = new CheckBox
            {
                Content = Text("можно завершать, если спрячется в трей", 13, Lead, wrap: true),
                IsChecked = p.AllowTerminate,
                Margin = new Thickness(64, -4, 0, 4),
                MinHeight = 28,
            };
            check.Checked += (_, _) => { p.AllowTerminate = true; ActionChanged?.Invoke(p); };
            check.Unchecked += (_, _) => { p.AllowTerminate = false; ActionChanged?.Invoke(p); };
            ToolTipService.SetToolTip(check, p.Known?.TerminateHint ??
                "Завершим, только если у программы не осталось окон и она ни о чём не спрашивает");
            box.Children.Add(check);
        }
        return box;
    }

    private static ProgramAction Next(ProgramAction a) => a switch
    {
        ProgramAction.Close => ProgramAction.SecondMonitor,
        ProgramAction.SecondMonitor => ProgramAction.Keep,
        _ => ProgramAction.Close,
    };

    private static Border Badge(ProgramAction a)
    {
        var (text, bg, fg) = a switch
        {
            ProgramAction.Close => ("Закрыть", "#FDE8E8", "#B42318"),
            ProgramAction.SecondMonitor => ("Второй монитор", "#E6EDFF", "#2948B8"),
            _ => ("Оставить", "#EAF5EE", "#1E7A44"),
        };
        return new Border
        {
            Background = Brush(bg),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, 12, Hex(fg), FontWeights.Bold),
        };
    }

    private static Border Avatar(string name, string id)
    {
        var letter = Text(name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?", 16, Colors.White, FontWeights.Bold);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = Brush(ProgramColor(id)),
            Child = letter,
        };
    }

    private static UIElement StatusRow(string name, string id, string status, Color statusColor, string? detail = null)
    {
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(14, 10, 14, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Avatar(name, id));
        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(name, 15, Ink, FontWeights.Bold));
        if (detail is not null) names.Children.Add(Text(detail, 12, Muted, wrap: true));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        var s = Text(status, 13, statusColor, FontWeights.SemiBold);
        s.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(s, 2);
        grid.Children.Add(s);
        return grid;
    }

    private void RefreshForecast()
    {
        if (_mode != Mode.Plan) return;
        var close = Programs.Where(p => p.Action == ProgramAction.Close && !p.IsElevated).ToList();
        _ramTitle.Text = "Оперативная память";
        _ramValue.Text = close.Count > 0 ? "≈" + Sizes.Format(close.Sum(p => p.PrivateBytes)) : "—";
        ToolTipService.SetToolTip(_ramValue, "Примерно освободится. Точную цифру покажем после перехода.");

        var tiles = Programs.Where(p => p.Action == ProgramAction.SecondMonitor).ToList();
        _secondValue.Text = !_hasSecondMonitor ? "нет" : tiles.Count == 0 ? "пусто" : tiles.Count.ToString();
        _secondScreen.Children.Clear();
        var i = 0;
        foreach (var t in tiles.Take(4))
        {
            var tile = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Brush(ProgramColor(t.Id)),
                Padding = new Thickness(8),
                Child = new TextBlock
                {
                    Text = t.DisplayName, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetRow(tile, i / 2);
            Grid.SetColumn(tile, i % 2);
            _secondScreen.Children.Add(tile);
            i++;
        }
        _secondCaption.Text = !_hasSecondMonitor
            ? "Второй монитор не подключён — разрешённые программы останутся как есть"
            : "Второй монитор — " + (tiles.Count == 0 ? "пусто" : string.Join(", ", tiles.Take(4).Select(t => t.DisplayName)));
    }

    // ---------- мелочи ----------

    private void OnGo()
    {
        switch (_mode)
        {
            case Mode.Plan: GoRequested?.Invoke(); break;
            case Mode.Rest: ReturnRequested?.Invoke(); break;
            default: AppWindow.Hide(); break;
        }
    }

    private void SetGo(bool enabled, bool dark)
    {
        _go.IsEnabled = enabled;
        var bg = dark ? Ink : Accent;
        var hover = dark ? Hex("#2A2C35") : AccentDark;
        _go.Background = Brush(bg);
        _go.Resources["ButtonBackground"] = Brush(bg);
        _go.Resources["ButtonBackgroundPointerOver"] = Brush(hover);
        _go.Resources["ButtonBackgroundPressed"] = Brush(hover);
    }

    private void SetMainScreen(bool rest)
    {
        if (rest)
        {
            var g = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            g.GradientStops.Add(new GradientStop { Color = Hex("#3B2F9A"), Offset = 0 });
            g.GradientStops.Add(new GradientStop { Color = Hex("#0F766E"), Offset = 1 });
            _mainScreen.Background = g;
            _mainLabel.Text = "Режим отдыха";
            _dndValue.Text = "Позже";
            ToolTipService.SetToolTip(_dndValue, "«Не беспокоить» появится на этапе 5");
        }
        else
        {
            _mainScreen.Background = Brush("#C9CCD8");
            _mainLabel.Text = "Рабочий стол";
        }
    }

    private void ShowPanel(string title, string hint, bool progress)
    {
        _panelTitle.Text = title;
        _panelHint.Text = hint;
        _progressBox.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        _panelNote.Visibility = _mode == Mode.Plan ? Visibility.Visible : Visibility.Collapsed;
        _choose.Visibility = _mode == Mode.Plan && !IsTest ? Visibility.Visible : Visibility.Collapsed;
        _test.Visibility = _choose.Visibility;
    }

    private static Border Screen(UIElement child, double height) => new()
    {
        Height = height,
        CornerRadius = new CornerRadius(14),
        BorderBrush = Brush(Ink),
        BorderThickness = new Thickness(10),
        Child = child,
    };

    private static StackPanel Figure(UIElement screen, UIElement caption)
    {
        var s = new StackPanel { Spacing = 10 };
        s.Children.Add(screen);
        s.Children.Add(caption);
        return s;
    }

    private static Border StatCard(TextBlock title, TextBlock value, int column)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(title);
        s.Children.Add(value);
        var card = CardBorder(s);
        Grid.SetColumn(card, column);
        return card;
    }

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
