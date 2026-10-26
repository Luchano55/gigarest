using System.Runtime.InteropServices;
using GigaRest.Core.Input;
using GigaRest.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using static GigaRest.App.Ui;

namespace GigaRest.App;

public sealed record SettingsResult(DaySchedule Schedule, DaySchedule RestOffer, string Hotkey, string TilesHotkey, bool ConfirmBeforeRest, bool Autostart, bool DoNotDisturb, string FilmsFolder);

/// <summary>Настройки: расписание возврата, горячая клавиша, окно перехода, автозапуск.</summary>
public sealed class SettingsWindow : Window
{
    public const double DesignWidth = 720, DesignHeight = 1180;

    private readonly Grid _root;
    private readonly ToggleSwitch _confirm, _autostart, _dnd;
    private readonly TextBox _films;
    private readonly ScheduleEditor _return, _rest;
    private readonly TextBox _hotkey, _tilesHotkey;
    private readonly TextBlock _hotkeyHint;
    private bool _closingForGood;

    public event Action<SettingsResult>? Saved;
    public FrameworkElement Root => _root;

    public SettingsWindow()
    {
        Title = "GigaRest — настройки";
        _root = new Grid { Background = Brush(PageBg), Padding = new Thickness(48, 40, 48, 32), RowSpacing = 20, RequestedTheme = ElementTheme.Light };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _root.Children.Add(Text("Настройки", 32, Ink, FontWeights.ExtraBold));

        var body = new StackPanel { Spacing = 16 };

        // Вечером — мягко предложить отдых, утром — вернуться к работе.
        _rest = new ScheduleEditor("Предложение отдыха",
            "Мягко: вечером у значка в трее появится уведомление «Перейти в отдых?». Не щёлкнули — ничего не произойдёт. " +
            "Во время игры или фильма на весь экран подождём.",
            "Предлагать отдых", "Предложим отдых");
        body.Children.Add(_rest.Card);
        _return = new ScheduleEditor("Возврат к работе",
            "Кнопка и горячая клавиша работают всегда. По расписанию GigaRest не переключит молча, " +
            "а предложит: «Пора за работу · Вернуться / Через 15 минут».",
            "Возвращать по расписанию", "Предложим вернуться");
        body.Children.Add(_return.Card);

        // Горячая клавиша
        var keys = new StackPanel { Spacing = 10 };
        keys.Children.Add(Text("Горячая клавиша «Работа ↔ Отдых»", 18, Ink, FontWeights.Bold));
        _hotkey = new TextBox { PlaceholderText = "Ctrl+Alt+Home", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        _hotkey.TextChanged += (_, _) => UpdateHotkeyHint();
        keys.Children.Add(_hotkey);
        _hotkeyHint = Text("", 13, Muted, wrap: true);
        keys.Children.Add(_hotkeyHint);
        keys.Children.Add(Text("Обратно в плитки (во время отдыха)", 14, Ink, FontWeights.SemiBold));
        _tilesHotkey = new TextBox { PlaceholderText = "Ctrl+Alt+End", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        keys.Children.Add(_tilesHotkey);
        body.Children.Add(CardBorder(keys, radius: 18, padding: 22));

        // Прочее
        var other = new StackPanel { Spacing = 6 };
        other.Children.Add(Text("Разное", 18, Ink, FontWeights.Bold));
        _confirm = new ToggleSwitch { OnContent = "Перед отдыхом показывать окно перехода", OffContent = "Переходить сразу, без вопросов" };
        _autostart = new ToggleSwitch { OnContent = "Запускать вместе с Windows (в трее)", OffContent = "Не запускать вместе с Windows" };
        _dnd = new ToggleSwitch { OnContent = "В отдыхе — «Не беспокоить» (только приоритетные уведомления)", OffContent = "Уведомления в отдыхе не трогать" };
        other.Children.Add(_confirm);
        other.Children.Add(_dnd);
        other.Children.Add(_autostart);
        var filmsLabel = Text("Папка с фильмами (для «Смотреть»)", 14, Ink, FontWeights.SemiBold);
        filmsLabel.Margin = new Thickness(0, 10, 0, 0);
        other.Children.Add(filmsLabel);
        _films = new TextBox { PlaceholderText = GigaRest.Core.Media.FilmLibrary.DefaultFolder(), Width = 460, HorizontalAlignment = HorizontalAlignment.Left };
        other.Children.Add(_films);
        body.Children.Add(CardBorder(other, radius: 18, padding: 22));

        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(scroll);

        var save = ColoredButton(Text("Сохранить", 16, Colors.White, FontWeights.Bold), Accent, AccentDark, Colors.White);
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Padding = new Thickness(36, 12, 36, 12);
        save.Click += (_, _) => OnSave();
        Grid.SetRow(save, 2);
        _root.Children.Add(save);

        Content = _root;
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
        UpdateHotkeyHint(hotkeyProblem);
    }

    private void UpdateHotkeyHint(string? problem = null)
    {
        _hotkeyHint.Text = problem ?? (Hotkey.TryParse(_hotkey.Text, out var h)
            ? $"Будет: {h}. Например: Ctrl+Alt+Home, Ctrl+Shift+F12. Win+Esc не подходит — она занята Windows."
            : "Так не получится: нужен хотя бы один из Ctrl, Alt, Shift, Win и клавиша — например, Ctrl+Alt+Home.");
    }

    private void OnSave()
    {
        if (!Hotkey.TryParse(_hotkey.Text, out var hotkey))
        {
            UpdateHotkeyHint();
            return;
        }
        var tiles = Hotkey.TryParse(_tilesHotkey.Text, out var t) && t != hotkey ? t.ToString() : "Ctrl+Alt+End";
        Saved?.Invoke(new SettingsResult(_return.Current(), _rest.Current(), hotkey.ToString(), tiles, _confirm.IsOn, _autostart.IsOn, _dnd.IsOn, _films.Text));
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
        _on = new ToggleSwitch { OnContent = toggle + " — включено", OffContent = toggle + " — выключено" };
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
        _time = new TimePicker { ClockIdentifier = "24HourClock", MinuteIncrement = 5, Header = "Время" };
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
        _hint.Text = !s.Enabled ? "Выключено."
            : s.Days.Count == 0 ? "Выберите хотя бы один день."
            : $"{_verb} {s.Describe()}. Ближайший раз — {next?.ToString("dddd, d MMMM, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))}.";
    }
}
