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
    private readonly Button _snooze;
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
        _root = new Grid
        {
            Background = Brush("#F3F3F3"),
            Padding = new Thickness(40, 32, 40, 32),
            ColumnSpacing = 32,
            RequestedTheme = ElementTheme.Light,
        };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

        // ---------- левая колонка ----------
        var left = new StackPanel { Spacing = 20 };
        var head = new StackPanel { Spacing = 6 };
        var kicker = _kicker = Text("ОДНА КНОПКА", 12, Muted, FontWeights.SemiBold);
        kicker.CharacterSpacing = 80;
        _headline = Text("Работа → Отдых", 28, Ink, FontWeights.SemiBold, wrap: true);
        _lead = Text("", 14, Lead, wrap: true);
        _lead.MaxWidth = 560;
        _lead.HorizontalAlignment = HorizontalAlignment.Left;
        _lead.LineHeight = 20;
        head.Children.Add(kicker);
        head.Children.Add(_headline);
        head.Children.Add(_lead);
        left.Children.Add(head);

        // Мониторы
        var monitors = new Grid { ColumnSpacing = 16 };
        monitors.ColumnDefinitions.Add(new ColumnDefinition());
        monitors.ColumnDefinitions.Add(new ColumnDefinition());
        _mainLabel = Text("Рабочий стол", 14, Colors.White, FontWeights.SemiBold);
        _mainLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mainLabel.VerticalAlignment = VerticalAlignment.Center;
        _mainScreen = Screen(_mainLabel, 168);
        var mainFig = Figure(_mainScreen, Text("Основной монитор", 12, Muted));
        _secondScreen = new Grid { RowSpacing = 6, ColumnSpacing = 6, Padding = new Thickness(6) };
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.RowDefinitions.Add(new RowDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        _secondScreen.ColumnDefinitions.Add(new ColumnDefinition());
        var second = Screen(_secondScreen, 168);
        second.Background = Brush("#E4E6ED");
        _secondCaption = Text("Второй монитор", 12, Muted, wrap: true);
        var secondFig = Figure(second, _secondCaption);
        Grid.SetColumn(secondFig, 1);
        monitors.Children.Add(mainFig);
        monitors.Children.Add(secondFig);
        left.Children.Add(monitors);

        // Три карточки
        // Три цифры — одной карточкой, как в «Параметрах» Windows.
        var cards = new Grid { ColumnSpacing = 1 };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
        (_ramTitle, _ramValue) = (Text("Освободится памяти", 12, Muted, wrap: true), Text("", 18, Ink, FontWeights.SemiBold, wrap: true));
        _dndValue = Text("", 18, Ink, FontWeights.SemiBold, wrap: true);
        _secondValue = Text("", 18, Ink, FontWeights.SemiBold, wrap: true);
        cards.Children.Add(StatCard(_ramTitle, _ramValue, 0));
        cards.Children.Add(StatCard(Text("Уведомления", 12, Muted, wrap: true), _dndValue, 1));
        cards.Children.Add(StatCard(Text("Плитки на втором мониторе", 12, Muted, wrap: true), _secondValue, 2));
        left.Children.Add(CardBorder(cards, radius: 8, padding: 0));

        _goText = Text("Перейти в отдых", 15, Colors.White, FontWeights.SemiBold);
        _go = ColoredButton(_goText, Accent, AccentDark, Colors.White);
        _go.Padding = new Thickness(20, 11, 20, 11);
        _go.CornerRadius = new CornerRadius(8);
        _go.Click += (_, _) => OnGo();
        left.Children.Add(_go);
        _snooze = new Button { Content = "Через 15 минут", HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 9, 16, 9), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed };
        _snooze.Click += (_, _) => { _snooze.Visibility = Visibility.Collapsed; SnoozeRequested?.Invoke(); AppWindow.Hide(); };
        left.Children.Add(_snooze);

        _notice = Text("", 13, Lead, wrap: true);
        _notice.LineHeight = 19;
        left.Children.Add(_notice);
        var version = typeof(SwitcherWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
        var plus = version.IndexOf('+');
        left.Children.Add(Text("GigaRest " + (plus > 0 ? version[..plus] : version), 11, Hex("#9A9DAB")));
        // На маленьком экране левая колонка прокручивается, кнопка не теряется.
        var leftScroll = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        _root.Children.Add(leftScroll);

        // ---------- правая колонка ----------
        var panel = new Grid { RowSpacing = 12 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 4 };
        _panelTitle = Text("Что будет с программами", 20, Ink, FontWeights.SemiBold, wrap: true);
        _panelHint = Text("щёлкните, чтобы поменять", 12, Muted, wrap: true);
        header.Children.Add(_panelTitle);
        header.Children.Add(_panelHint);
        _choose = new HyperlinkButton { Content = "Выбрать рабочие программы, в том числе незапущенные…", Padding = new Thickness(0, 4, 0, 0), FontSize = 12 };
        _choose.Click += (_, _) => ProgramsRequested?.Invoke();
        header.Children.Add(_choose);
        _test = new HyperlinkButton { Content = "Попробовать на тестовых окнах — ваши программы не тронем", Padding = new Thickness(0, 0, 0, 0), FontSize = 12 };
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

        _list = new StackPanel { Spacing = 0 };
        var scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        _panelNote = Text("Закрываем вежливо, как крестиком: если где-то не сохранено — программа спросит сама. " +
                          "Кнопка «Вернуться к работе» запустит закрытое обратно.", 12, Muted, wrap: true);
        _panelNote.LineHeight = 18;
        Grid.SetRow(_panelNote, 3);
        panel.Children.Add(_panelNote);

        var panelCard = CardBorder(panel, radius: 8, padding: 20);
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
        _dndValue.Text = DoNotDisturb ? "Не беспокоить" : "Как сейчас";
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
            if (r.Outcome == CloseOutcome.InBackground)
            {
                _list.Children.Add(TrayRow(r));
                continue;
            }
            if (r.Outcome == CloseOutcome.StillOpen)
            {
                _list.Children.Add(ForceRow(r));
                continue;
            }
            _list.Children.Add(StatusRow(r.DisplayName, r.ProgramId, text, color, r.IsGone ? null : r.Message));
        }
        if (report.Results.Count == 0)
            _list.Children.Add(Text("Закрывать ничего не пришлось.", 14, Muted));
    }

    /// <summary>Отдых продолжается (например, после перезагрузки).</summary>
    public void ShowRestWaiting(IReadOnlyList<(string Id, string Name)>? closed = null)
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
        ShowPanel("Откроется снова", "", progress: false);
        _list.Children.Clear();
        foreach (var (id, name) in closed ?? [])
            _list.Children.Add(StatusRow(name, id, "закрыта", Muted));
        if (closed is null || closed.Count == 0)
            _list.Children.Add(Text("Закрытых программ нет — открывать ничего не придётся.", 14, Muted, wrap: true));
    }

    /// <summary>Расписание: «Пора за работу» — вернуться или отложить на 15 минут.</summary>
    public void ShowReturnOffer(string when, IReadOnlyList<(string Id, string Name)>? closed = null)
    {
        ShowRestWaiting(closed);
        _headline.Text = "Пора за работу";
        _lead.Text = $"По расписанию ({when}). Вернуть рабочие программы сейчас?";
        _snooze.Visibility = Visibility.Visible;
    }

    public void ShowReturning()
    {
        _snooze.Visibility = Visibility.Collapsed;
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

    /// <summary>Сообщение под кнопкой, не меняя состояния окна.</summary>
    public void SetNotice(string text) => _notice.Text = text;

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
        for (var i = 0; i < Programs.Count; i++)
        {
            if (i > 0) _list.Children.Add(Divider());
            _list.Children.Add(PlanRow(Programs[i]));
        }
    }

    private static Border Divider() => new() { Height = 1, Background = Brush("#ECECEF"), Margin = new Thickness(52, 0, 0, 0) };

    private static readonly (ProgramAction Action, string Text)[] Choices =
    [
        (ProgramAction.Close, "Закрыть"),
        (ProgramAction.SecondMonitor, "На второй монитор"),
        (ProgramAction.Keep, "Не трогать"),
    ];

    /// <summary>Строка программы в стиле «Параметров» Windows: значок, имя, окна и память, выбор действия.</summary>
    private UIElement PlanRow(RunningProgram p)
    {
        var box = new StackPanel { Padding = new Thickness(8, 6, 4, 6) };
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = AppIcon(p.DisplayName, p.Id, p.ExePath, 28);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(p.DisplayName, 14, Ink));
        var detail = p.IsElevated ? "от администратора — закроете только вы" : $"{p.Note} · {Sizes.Format(p.PrivateBytes)}";
        names.Children.Add(Text(detail, 12, Muted));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        var choice = new ComboBox { Width = 190, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var choices = p.IsBackground
            ? new[] { (ProgramAction.Close, "Завершить в отдыхе"), (ProgramAction.Keep, "Не трогать") }
            : Choices;
        foreach (var (action, text) in choices) choice.Items.Add(new ComboBoxItem { Content = text, Tag = action });
        choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (ProgramAction)i.Tag == p.Action) ?? choice.Items[^1];
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is not ComboBoxItem { Tag: ProgramAction action } || action == p.Action) return;
            if (action == ProgramAction.SecondMonitor && TilePlan.Ordered(Programs).Count() >= TilePlan.Max)
            {
                _notice.Text = "На втором мониторе уже четыре плитки — сначала уберите одну (крестик на плитке).";
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().First(i => (ProgramAction)i.Tag == p.Action);
                return;
            }
            SetAction(p, action);
            var index = _list.Children.IndexOf(box);
            if (index >= 0) _list.Children[index] = PlanRow(p); // галочка «завершить» появляется и пропадает
        };
        Grid.SetColumn(choice, 2);
        grid.Children.Add(choice);
        box.Children.Add(grid);

        // Галочка «завершать» — для программ, которые на крестик уходят в значок у часов.
        if (p.Action == ProgramAction.Close && !p.IsBackground && (p.Known?.HidesToTray == true || p.Windows.Count == 0))
        {
            var check = new CheckBox
            {
                Content = Text("если вместо закрытия уйдёт в значок у часов — завершить", 12, Lead, wrap: true),
                IsChecked = p.AllowTerminate,
                Margin = new Thickness(44, 2, 0, 0),
                MinHeight = 24,
                Padding = new Thickness(6, 0, 0, 0),
            };
            check.Checked += (_, _) => { p.AllowTerminate = true; ActionChanged?.Invoke(p); };
            check.Unchecked += (_, _) => { p.AllowTerminate = false; ActionChanged?.Invoke(p); };
            ToolTipService.SetToolTip(check, p.Known?.TerminateHint ??
                "Завершим, только если у программы не осталось окон и она ни о чём не спрашивает");
            box.Children.Add(check);
        }
        return box;
    }

    /// <summary>Новое действие программы; плитки второго монитора перенумеровываются, выбор запоминается.</summary>
    private void SetAction(RunningProgram p, ProgramAction action)
    {
        var tilesBefore = TilePlan.Ordered(Programs).ToList();
        p.Action = action;
        if (action == ProgramAction.SecondMonitor) { tilesBefore.Add(p); TilePlan.Renumber(tilesBefore); }
        else { p.TileOrder = null; tilesBefore.Remove(p); TilePlan.Renumber(tilesBefore); }
        foreach (var t in tilesBefore.Append(p).Distinct()) ActionChanged?.Invoke(t);
        _notice.Text = "";
        RefreshForecast();
    }

    private static Border Avatar(string name, string id) => AppIcon(name, id, null, 28);

    /// <summary>Программа спряталась в трей: «Завершить» — завершить сейчас и разрешить в следующий раз.</summary>
    private UIElement TrayRow(CloseResult r)
    {
        var box = new StackPanel();
        box.Children.Add(StatusRow(r.DisplayName, r.ProgramId, "в трее", Hex("#B26A00"),
            "Спряталась в трей и работает в фоне. «Завершить» — завершим сейчас и запомним, что её можно завершать."));
        var button = new Button { Content = "Завершить", Margin = new Thickness(52, 0, 0, 8), Padding = new Thickness(14, 5, 14, 5), CornerRadius = new CornerRadius(6) };
        button.Click += (_, _) =>
        {
            TerminateRequested?.Invoke(r.ProgramId, false);
            var index = _list.Children.IndexOf(box);
            if (index >= 0) _list.Children[index] = StatusRow(r.DisplayName, r.ProgramId, "завершена", Hex("#1E7A44"), "В следующий раз завершим сами");
        };
        box.Children.Add(button);
        return box;
    }

    /// <summary>Не закрылась (окна или несохранённое): «Завершить принудительно» — по явной просьбе, с предупреждением.</summary>
    private UIElement ForceRow(CloseResult r)
    {
        var box = new StackPanel();
        box.Children.Add(StatusRow(r.DisplayName, r.ProgramId, "осталась", Hex("#B42318"), r.Message));
        var button = new Button { Content = "Завершить принудительно — несохранённое пропадёт", Margin = new Thickness(52, 0, 0, 8), Padding = new Thickness(14, 5, 14, 5), CornerRadius = new CornerRadius(6) };
        button.Click += (_, _) =>
        {
            TerminateRequested?.Invoke(r.ProgramId, true);
            var index = _list.Children.IndexOf(box);
            if (index >= 0) _list.Children[index] = StatusRow(r.DisplayName, r.ProgramId, "завершена", Hex("#1E7A44"), "Запомнили: в следующий раз завершим принудительно");
        };
        box.Children.Add(button);
        return box;
    }

    private static UIElement StatusRow(string name, string id, string status, Color statusColor, string? detail = null)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 8, 8, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Avatar(name, id));
        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Text(name, 14, Ink));
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

        var tiles = TilePlan.Ordered(Programs).ToList();
        _secondValue.Text = !_hasSecondMonitor ? "нет монитора" : tiles.Count == 0 ? "нет" : $"{tiles.Count} из {TilePlan.Max}";
        RenderSecondScreen(tiles);
        _secondCaption.Text = !_hasSecondMonitor
            ? "Второй монитор не подключён — разрешённые программы останутся как есть"
            : tiles.Count == 0
                ? "Второй монитор — пусто. «+» — поставить программу"
                : "Второй монитор — перетащите плитку или щёлкните две, чтобы поменять местами";
    }

    private int _picked = -1; // плитка, выбранная первым щелчком (для обмена местами)

    /// <summary>
    /// Предпросмотр второго монитора: 2×2 места. Плитки можно перетаскивать или менять двумя щелчками,
    /// «×» — убрать с монитора, «+» на пустом месте — поставить программу.
    /// </summary>
    private void RenderSecondScreen(IReadOnlyList<RunningProgram> tiles)
    {
        _secondScreen.Children.Clear();
        for (var slot = 0; slot < TilePlan.Max; slot++)
        {
            var cell = slot < tiles.Count ? TileCell(tiles[slot], slot) : EmptyCell(slot);
            Grid.SetRow(cell, slot / 2);
            Grid.SetColumn(cell, slot % 2);
            _secondScreen.Children.Add(cell);
        }
    }

    private FrameworkElement TileCell(RunningProgram p, int slot)
    {
        var picked = slot == _picked;
        var content = new Grid { Padding = new Thickness(8, 6, 4, 6), ColumnSpacing = 6 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = AppIcon(p.DisplayName, p.Id, p.ExePath, 20);
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        var name = Text(p.DisplayName, 12, Ink, FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        content.Children.Add(name);
        var remove = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 9 }, Padding = new Thickness(5), MinWidth = 0, MinHeight = 0,
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTipService.SetToolTip(remove, "Убрать со второго монитора");
        remove.Click += (_, _) => { _picked = -1; SetAction(p, ProgramAction.Keep); RenderPlanList(); };
        Grid.SetColumn(remove, 2);
        content.Children.Add(remove);

        var cell = new Border
        {
            CornerRadius = new CornerRadius(6), Background = Brush(Colors.White),
            BorderBrush = Brush(picked ? Accent : Hex("#D9DBE3")), BorderThickness = new Thickness(picked ? 2 : 1),
            Child = content, CanDrag = true, AllowDrop = true,
        };
        ToolTipService.SetToolTip(cell, p.DisplayName);
        cell.Tapped += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && IsInside(fe, remove)) return;
            if (_picked < 0) _picked = slot;
            else if (_picked == slot) _picked = -1;
            else { Swap(_picked, slot); _picked = -1; }
            RefreshForecast();
        };
        cell.DragStarting += (_, e) => { e.Data.SetText(slot.ToString()); e.Data.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move; };
        cell.DragOver += (_, e) => { e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move; cell.BorderBrush = Brush(Accent); };
        cell.DragLeave += (_, _) => cell.BorderBrush = Brush(Hex("#D9DBE3"));
        cell.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            if (int.TryParse(await e.DataView.GetTextAsync(), out var from)) { _picked = -1; Swap(from, slot); RefreshForecast(); }
        };
        return cell;
    }

    private static bool IsInside(FrameworkElement element, FrameworkElement container)
    {
        for (DependencyObject? d = element; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    private FrameworkElement EmptyCell(int slot)
    {
        var candidates = Programs.Where(p => !p.IsBackground && p.Windows.Count > 0 && p.Action != ProgramAction.SecondMonitor).ToList();
        var plus = new Button
        {
            Content = new FontIcon { Glyph = "\uE710", FontSize = 12, Foreground = Brush(Muted) },
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush("#80FFFFFF"), BorderBrush = Brush("#C9CCD8"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), IsEnabled = _hasSecondMonitor && candidates.Count > 0,
        };
        ToolTipService.SetToolTip(plus, "Поставить программу на это место");
        var menu = new MenuFlyout();
        foreach (var p in candidates)
        {
            var item = new MenuFlyoutItem { Text = p.DisplayName };
            item.Click += (_, _) => { SetAction(p, ProgramAction.SecondMonitor); RenderPlanList(); };
            menu.Items.Add(item);
        }
        plus.Flyout = menu;
        return plus;
    }

    private void Swap(int a, int b)
    {
        TilePlan.Swap(Programs, a, b);
        foreach (var t in TilePlan.Ordered(Programs)) ActionChanged?.Invoke(t);
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
            _dndValue.Text = DoNotDisturb ? "Не беспокоить" : "Как сейчас";
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
        CornerRadius = new CornerRadius(10),
        BorderBrush = Brush("#2B2D36"),
        BorderThickness = new Thickness(6),
        Child = child,
    };

    private static StackPanel Figure(UIElement screen, UIElement caption)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(screen);
        s.Children.Add(caption);
        return s;
    }

    private static Border StatCard(TextBlock title, TextBlock value, int column)
    {
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(title);
        s.Children.Add(value);
        var card = new Border
        {
            Padding = new Thickness(16, 12, 16, 12), Child = s,
            BorderBrush = Brush("#ECECEF"), BorderThickness = new Thickness(column > 0 ? 1 : 0, 0, 0, 0),
        };
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
