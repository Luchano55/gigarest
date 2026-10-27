using System.Runtime.InteropServices;
using GigaRest.Core.Input;
using GigaRest.Core.Settings;
using GigaRest.Core.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using static GigaRest.App.Ui;

namespace GigaRest.App;

public sealed record SettingsResult(DaySchedule Schedule, DaySchedule RestOffer, string Hotkey, string TilesHotkey, bool ConfirmBeforeRest, bool Autostart, bool DoNotDisturb, string FilmsFolder, string UpdateChannel,
    bool TilesEnabled, int TilesCount, int DimMinutes, int ScreenOffMinutes, string Language = L.Auto);

/// <summary>Настройки: расписание возврата, горячая клавиша, окно перехода, автозапуск.</summary>
public sealed class SettingsWindow : Window
{
    public const double DesignWidth = 720, DesignHeight = 1180;

    private readonly Grid _root;
    private readonly ToggleSwitch _confirm, _autostart, _dnd;
    private readonly TextBox _films;
    private readonly ComboBox _channel, _language, _tilesCount, _dim, _screenOff;
    private readonly ToggleSwitch _tiles;
    private readonly ScheduleEditor _return, _rest;
    private readonly TextBox _hotkey, _tilesHotkey;
    private readonly TextBlock _hotkeyHint;
    private bool _closingForGood;

    public event Action<SettingsResult>? Saved;
    public FrameworkElement Root => _root;

    public SettingsWindow()
    {
        Title = L.T("GigaRest — настройки", "GigaRest — Settings");
        _root = new Grid { Padding = new Thickness(48, 40, 48, 32), RowSpacing = 20 };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _root.Children.Add(Text(L.T("Настройки", "Settings"), 32, Hex("#F6F2EA"), FontWeights.SemiBold));

        var body = new StackPanel { Spacing = 16 };

        // Вечером — мягко предложить отдых, утром — вернуться к работе.
        _rest = new ScheduleEditor(L.T("Предложение отдыха", "Rest reminder"),
            L.T("Мягко: вечером у значка в трее появится уведомление «Перейти в отдых?». Не щёлкнули — ничего не произойдёт. " +
            "Во время игры или фильма на весь экран подождём.",
            "A gentle nudge: in the evening a “Time to rest?” notification appears by the tray icon. Ignore it and nothing happens. " +
            "If a game or film is running full screen, we'll wait."),
            L.T("Предлагать отдых", "Suggest rest"), L.T("Предложим отдых", "We'll suggest rest"));
        body.Children.Add(_rest.Card);
        _return = new ScheduleEditor(L.T("Возврат к работе", "Back to work"),
            L.T("Кнопка и горячая клавиша работают всегда. По расписанию GigaRest не переключит молча, " +
            "а предложит: «Пора за работу · Вернуться / Через 15 минут».",
            "The button and hotkey always work. On schedule, GigaRest won't switch silently — " +
            "it will ask: “Time to work · Go back / In 15 minutes”."),
            L.T("Возвращать по расписанию", "Return on schedule"), L.T("Предложим вернуться", "We'll suggest going back"));
        body.Children.Add(_return.Card);

        // Горячая клавиша
        var keys = new StackPanel { Spacing = 10 };
        keys.Children.Add(Text(L.T("Горячая клавиша «Работа ↔ Отдых»", "Work ↔ Rest hotkey"), 18, Ink, FontWeights.SemiBold));
        _hotkey = new TextBox { PlaceholderText = "Ctrl+Alt+Home", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        _hotkey.TextChanged += (_, _) => UpdateHotkeyHint();
        keys.Children.Add(_hotkey);
        _hotkeyHint = Text("", 13, Muted, wrap: true);
        keys.Children.Add(_hotkeyHint);
        keys.Children.Add(Text(L.T("Обратно в плитки (во время отдыха)", "Back to tiles (during rest)"), 14, Ink, FontWeights.SemiBold));
        _tilesHotkey = new TextBox { PlaceholderText = "Ctrl+Alt+End", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        keys.Children.Add(_tilesHotkey);
        body.Children.Add(CardBorder(keys, radius: 18, padding: 22));

        // Отдых: плитки и защита OLED
        var rest = new StackPanel { Spacing = 6 };
        rest.Children.Add(Text(L.T("Отдых", "Rest"), 18, Ink, FontWeights.SemiBold));
        _tiles = new ToggleSwitch { OnContent = L.T("Плитки на втором мониторе", "Tiles on the second monitor"), OffContent = L.T("Без плиток — второй монитор не трогать", "No tiles — leave the second monitor alone") };
        rest.Children.Add(_tiles);
        _tilesCount = Choice(L.T("Сколько плиток", "Number of tiles"), [(1, "1"), (2, "2"), (3, "3"), (4, "4")], rest);
        _dim = Choice(L.T("Затемнять «Тихий» экран без дела через", "Dim the Quiet screen when idle after"),
            [(5, L.T("5 минут", "5 minutes")), (10, L.T("10 минут", "10 minutes")), (15, L.T("15 минут", "15 minutes")), (30, L.T("30 минут", "30 minutes")), (60, L.T("1 час", "1 hour"))], rest);
        _screenOff = Choice(L.T("Выключать экран без дела через", "Turn the screen off when idle after"),
            [(30, L.T("30 минут", "30 minutes")), (60, L.T("1 час", "1 hour")), (90, L.T("1,5 часа", "1.5 hours")), (120, L.T("2 часа", "2 hours")), (0, L.T("Не выключать", "Never"))], rest);
        _tiles.Toggled += (_, _) => _tilesCount.IsEnabled = _tiles.IsOn;
        body.Children.Add(CardBorder(rest, radius: 18, padding: 22));

        // Прочее
        var other = new StackPanel { Spacing = 6 };
        other.Children.Add(Text(L.T("Разное", "Other"), 18, Ink, FontWeights.SemiBold));
        _confirm = new ToggleSwitch { OnContent = L.T("Перед отдыхом показывать окно перехода", "Show the switch window before rest"), OffContent = L.T("Переходить сразу, без вопросов", "Switch right away, no questions") };
        _autostart = new ToggleSwitch { OnContent = L.T("Запускать вместе с Windows (в трее)", "Start with Windows (in the tray)"), OffContent = L.T("Не запускать вместе с Windows", "Don't start with Windows") };
        _dnd = new ToggleSwitch { OnContent = L.T("В отдыхе — «Не беспокоить» (только приоритетные уведомления)", "During rest — Do not disturb (priority notifications only)"), OffContent = L.T("Уведомления в отдыхе не трогать", "Leave notifications as they are during rest") };
        other.Children.Add(_confirm);
        other.Children.Add(_dnd);
        other.Children.Add(_autostart);
        var filmsLabel = Text(L.T("Папка с фильмами (для «Смотреть»)", "Films folder (for Watch)"), 14, Ink, FontWeights.SemiBold);
        filmsLabel.Margin = new Thickness(0, 10, 0, 0);
        other.Children.Add(filmsLabel);
        _films = new TextBox { PlaceholderText = GigaRest.Core.Media.FilmLibrary.DefaultFolder(), Width = 460, HorizontalAlignment = HorizontalAlignment.Left };
        other.Children.Add(_films);
        var channelLabel = Text(L.T("Обновления", "Updates"), 14, Ink, FontWeights.SemiBold);
        channelLabel.Margin = new Thickness(0, 10, 0, 0);
        other.Children.Add(channelLabel);
        _channel = new ComboBox { Width = 460, HorizontalAlignment = HorizontalAlignment.Left };
        _channel.Items.Add(new ComboBoxItem { Content = L.T("Как при установке", "As installed") + $" ({GigaRest.Core.Updates.UpdateChannels.Describe(GigaRest.Core.Updates.UpdateChannels.Resolve(null, GigaRest.Core.Updates.AppVersion.Display))})", Tag = GigaRest.Core.Updates.UpdateChannels.Auto });
        _channel.Items.Add(new ComboBoxItem { Content = L.T("Стабильные версии — проверенные выпуски", "Stable versions — tested releases"), Tag = GigaRest.Core.Updates.UpdateChannels.Stable });
        _channel.Items.Add(new ComboBoxItem { Content = L.T("Тестовые сборки — всё новое сразу, могут быть ошибки", "Test builds — everything new right away, may have bugs"), Tag = GigaRest.Core.Updates.UpdateChannels.Dev });
        other.Children.Add(_channel);
        var languageLabel = Text("Язык / Language", 14, Ink, FontWeights.SemiBold);
        languageLabel.Margin = new Thickness(0, 10, 0, 0);
        other.Children.Add(languageLabel);
        _language = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        _language.Items.Add(new ComboBoxItem { Content = L.T("Как в Windows", "As in Windows"), Tag = L.Auto });
        _language.Items.Add(new ComboBoxItem { Content = "Русский", Tag = L.Russian });
        _language.Items.Add(new ComboBoxItem { Content = "English", Tag = L.English });
        other.Children.Add(_language);
        other.Children.Add(Text(L.T("Язык поменяется после перезапуска GigaRest.", "The language changes after GigaRest restarts."), 13, Muted, wrap: true));
        body.Children.Add(CardBorder(other, radius: 18, padding: 22));

        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(scroll);

        var save = WarmButton(L.T("Сохранить", "Save"), 16);
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Padding = new Thickness(40, 12, 40, 12);
        save.Click += (_, _) => OnSave();
        Grid.SetRow(save, 2);
        _root.Children.Add(save);

        Content = _root;
        DarkWindow(AppWindow, _root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (sender, e) =>
        {
            if (_closingForGood) return;
            e.Cancel = true;
            sender.Hide();
        };
        SizeToDesign();
    }

    public void Load(AppSettings settings, bool autostart, string? hotkeyProblem)
    {
        _return.Load(settings.ReturnSchedule);
        _rest.Load(settings.RestOffer);
        _hotkey.Text = settings.Hotkey;
        _tilesHotkey.Text = settings.TilesHotkey;
        _confirm.IsOn = settings.ConfirmBeforeRest;
        _autostart.IsOn = autostart;
        _dnd.IsOn = settings.DoNotDisturb;
        _films.Text = settings.FilmsFolder ?? "";
        _tiles.IsOn = settings.TilesEnabled;
        _tilesCount.IsEnabled = settings.TilesEnabled;
        Select(_tilesCount, settings.TilesCount);
        Select(_dim, settings.DimMinutes);
        Select(_screenOff, settings.ScreenOffMinutes);
        _channel.SelectedItem = _channel.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.UpdateChannel) ?? _channel.Items[0];
        _language.SelectedItem = _language.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == L.Normalize(settings.Language)) ?? _language.Items[0];
        UpdateHotkeyHint(hotkeyProblem);
    }

    private void UpdateHotkeyHint(string? problem = null)
    {
        _hotkeyHint.Text = problem ?? (Hotkey.TryParse(_hotkey.Text, out var h)
            ? L.T($"Будет: {h}. Например: Ctrl+Alt+Home, Ctrl+Shift+F12. Win+Esc не подходит — она занята Windows.",
                  $"Will be: {h}. For example: Ctrl+Alt+Home, Ctrl+Shift+F12. Win+Esc won't work — Windows uses it.")
            : L.T("Так не получится: нужен хотя бы один из Ctrl, Alt, Shift, Win и клавиша — например, Ctrl+Alt+Home.",
                  "That won't work: you need at least one of Ctrl, Alt, Shift, Win plus a key — for example, Ctrl+Alt+Home."));
    }

    private static ComboBox Choice(string label, (int Value, string Text)[] options, StackPanel into)
    {
        var title = Text(label, 14, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 8, 0, 0);
        into.Children.Add(title);
        var box = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (value, text) in options) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        into.Children.Add(box);
        return box;
    }

    private static void Select(ComboBox box, int value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value)
                           ?? box.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((int)i.Tag - value)).First();

    private static int Picked(ComboBox box, int fallback) => box.SelectedItem is ComboBoxItem { Tag: int v } ? v : fallback;

    private void OnSave()
    {
        if (!Hotkey.TryParse(_hotkey.Text, out var hotkey))
        {
            UpdateHotkeyHint();
            return;
        }
        var tiles = Hotkey.TryParse(_tilesHotkey.Text, out var t) && t != hotkey ? t.ToString() : "Ctrl+Alt+End";
        Saved?.Invoke(new SettingsResult(_return.Current(), _rest.Current(), hotkey.ToString(), tiles, _confirm.IsOn, _autostart.IsOn, _dnd.IsOn, _films.Text, (_channel.SelectedItem as ComboBoxItem)?.Tag as string ?? GigaRest.Core.Updates.UpdateChannels.Auto,
            _tiles.IsOn, Picked(_tilesCount, 4), Picked(_dim, 15), Picked(_screenOff, 60), (_language.SelectedItem as ComboBoxItem)?.Tag as string ?? L.Auto));
        AppWindow.Hide();
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

/// <summary>Карточка расписания: переключатель, дни недели, время и подсказка «ближайший раз».</summary>
internal sealed class ScheduleEditor
{
    private readonly ToggleSwitch _on;
    private readonly Dictionary<DayOfWeek, ToggleButton> _days = [];
    private readonly TimePicker _time;
    private readonly TextBlock _hint;
    private readonly string _verb;

    public Border Card { get; }

    public ScheduleEditor(string title, string description, string toggle, string verb)
    {
        _verb = verb;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text(title, 18, Ink, FontWeights.Bold));
        panel.Children.Add(Text(description, 13, Lead, wrap: true));
        _on = new ToggleSwitch { OnContent = toggle + L.T(" — включено", ": on"), OffContent = toggle + L.T(" — выключено", ": off") };
        _on.Toggled += (_, _) => UpdateHint();
        panel.Children.Add(_on);
        var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var d in DaySchedule.Order)
        {
            var b = new ToggleButton { Content = DaySchedule.Short(d), MinWidth = 48, CornerRadius = new CornerRadius(999) };
            b.Checked += (_, _) => UpdateHint();
            b.Unchecked += (_, _) => UpdateHint();
            _days[d] = b;
            days.Children.Add(b);
        }
        panel.Children.Add(days);
        _time = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 5, Header = L.T("Время", "Time") };
        _time.SelectedTimeChanged += (_, _) => UpdateHint();
        panel.Children.Add(_time);
        _hint = Text("", 13, Muted, wrap: true);
        panel.Children.Add(_hint);
        Card = CardBorder(panel, radius: 18, padding: 22);
    }

    public void Load(DaySchedule s)
    {
        _on.IsOn = s.Enabled;
        foreach (var (d, b) in _days) b.IsChecked = s.Days.Contains(d);
        _time.SelectedTime = s.TimeOfDay;
        UpdateHint();
    }

    public DaySchedule Current() => new()
    {
        Enabled = _on.IsOn,
        Days = _days.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
        Time = (_time.SelectedTime ?? new TimeSpan(9, 0, 0)).ToString(@"hh\:mm"),
    };

    private void UpdateHint()
    {
        var s = Current();
        var next = s.NextAfter(DateTime.Now);
        _hint.Text = !s.Enabled ? L.T("Выключено.", "Off.")
            : s.Days.Count == 0 ? L.T("Выберите хотя бы один день.", "Pick at least one day.")
            : $"{_verb} {s.Describe()}. " + L.T("Ближайший раз — ", "Next time: ") + $"{next?.ToString(L.T("dddd, d MMMM, HH:mm", "dddd, MMMM d, HH:mm"), L.Culture)}.";
    }
}
