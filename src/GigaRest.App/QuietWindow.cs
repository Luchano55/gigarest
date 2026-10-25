using System.Globalization;
using System.Runtime.InteropServices;
using GigaRest.Core.Desktop;
using GigaRest.Windows;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Color = global::Windows.UI.Color;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Основной монитор — «Тихий» (ТЗ, раздел 5): сцена на весь экран с медленным движением,
/// часы, «не беспокоить · Вернуться к работе», строка разделов. Защита OLED: всё сдвигается,
/// текст не чисто белый, без действий — приглушаем.
/// </summary>
public sealed class QuietWindow : Window
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Color TextColor = Hex("#E0FFFFFF"); // 88% — бережём OLED

    private readonly Grid _root;
    private readonly Image _scene;
    private readonly CompositeTransform _sceneMove = new() { CenterX = 0.5, CenterY = 0.5 };
    private readonly Storyboard _kenBurns = new();
    private readonly Grid _overlay;
    private readonly TranslateTransform _shift = new();
    private readonly TextBlock _clock, _date, _sceneName;
    private readonly StackPanel _words;
    private readonly Border _panel;
    private readonly StackPanel _panelContent;
    private readonly Border _dim;
    private readonly Border _toast;
    private readonly TextBlock _toastText;
    private readonly Button _toastDetails;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastInput = DateTime.Now;
    private DateTime _lastShift = DateTime.Now;
    private string? _openWord;
    private int _sceneIndex;
    private bool _paused;
    private bool _closing;
    private readonly Random _random = new();

    public event Action? ReturnRequested;
    public event Action? DetailsRequested;
    public FrameworkElement Root => _root;

    public QuietWindow()
    {
        Title = "GigaRest — отдых";
        _root = new Grid { Background = Brush("#05060A"), RequestedTheme = ElementTheme.Dark };

        // Сцена с медленным движением (Ken Burns) — анимацию считает системный композитор.
        _scene = new Image { Stretch = Stretch.UniformToFill, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5), RenderTransform = _sceneMove };
        _root.Children.Add(_scene);
        AddMove("ScaleX", 1.03, 1.09);
        AddMove("ScaleY", 1.03, 1.09);
        AddMove("TranslateX", 0, -24);
        AddMove("TranslateY", 0, -10);

        var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        shade.GradientStops.Add(new GradientStop { Color = Hex("#59040509"), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#00040509"), Offset = 0.22 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#00040509"), Offset = 0.62 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#B8040509"), Offset = 1 });
        _root.Children.Add(new Border { Background = shade });

        // Всё, что поверх сцены, — в одном слое: его и сдвигаем для защиты OLED.
        _overlay = new Grid { Padding = new Thickness(64, 52, 64, 52), RenderTransform = _shift };

        var clockBox = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _clock = new TextBlock { FontFamily = new FontFamily("Georgia"), FontSize = 64, Foreground = Brush(TextColor) };
        _date = Text("", 14, Hex("#99FFFFFF"));
        clockBox.Children.Add(_clock);
        clockBox.Children.Add(_date);
        _overlay.Children.Add(clockBox);

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
        var dnd = Text("не беспокоить", 13, Hex("#80FFFFFF"));
        dnd.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(dnd);
        var back = QuietButton("Вернуться к работе", 13);
        back.Click += (_, _) => ReturnRequested?.Invoke();
        top.Children.Add(back);
        _overlay.Children.Add(top);

        // Короткий список над строкой — матовое стекло.
        _panelContent = new StackPanel { Spacing = 0 };
        _panel = new Border
        {
            Background = Brush("#990C0D12"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(24, 16, 24, 16), Width = 480,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 66),
            Visibility = Visibility.Collapsed, Child = _panelContent,
        };
        _overlay.Children.Add(_panel);

        var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 36 };
        foreach (var w in new[] { "Продолжить", "Играть", "Смотреть", "Слушать", "Сцены" })
        {
            var b = QuietButton(w, 17);
            b.Click += (_, _) => OnWord(w);
            _words.Children.Add(b);
        }
        bottom.Children.Add(_words);
        _sceneName = Text("", 13, Hex("#73FFFFFF"));
        _sceneName.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_sceneName, 1);
        bottom.Children.Add(_sceneName);
        _overlay.Children.Add(bottom);

        // Итог перехода — ненадолго, сверху по центру.
        _toastText = Text("", 15, TextColor, wrap: true);
        _toastDetails = QuietButton("Подробнее", 13);
        _toastDetails.Click += (_, _) => DetailsRequested?.Invoke();
        var toastBox = new StackPanel { Spacing = 6 };
        toastBox.Children.Add(_toastText);
        toastBox.Children.Add(_toastDetails);
        _toast = new Border
        {
            Background = Brush("#990C0D12"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 14, 20, 12), MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed, Child = toastBox,
        };
        _overlay.Children.Add(_toast);
        _root.Children.Add(_overlay);

        // Затемнение после 15 минут без действий (сцена — до 10%).
        _dim = new Border { Background = Brush(Colors.Black), Opacity = 0, IsHitTestVisible = false };
        _root.Children.Add(_dim);

        Content = _root;
        _root.PointerMoved += (_, _) => Wake();
        _root.PointerPressed += (_, _) => Wake();
        _root.KeyDown += OnKey;
        _tick.Tick += (_, _) => OnTick();
        SetScene(0);
        OnTick();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (_, e) => { if (!_closing) e.Cancel = true; };
    }

    private void AddMove(string property, double from, double to)
    {
        var a = new DoubleAnimation
        {
            From = from, To = to, Duration = new Duration(TimeSpan.FromSeconds(45)), AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(a, _sceneMove);
        Storyboard.SetTargetProperty(a, property);
        _kenBurns.Children.Add(a);
    }

    private static Button QuietButton(string text, double size)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, FontSize = size, Foreground = Brush("#BFFFFFFF") },
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(2, 8, 2, 8),
        };
        b.Resources["ButtonBackground"] = Brush(Colors.Transparent);
        b.Resources["ButtonBackgroundPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBackgroundPressed"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        b.PointerEntered += (_, _) => ((TextBlock)b.Content).Foreground = Brush("#FFFFFFFF");
        b.PointerExited += (_, _) => ((TextBlock)b.Content).Foreground = Brush("#BFFFFFFF");
        return b;
    }

    private void SetScene(int index)
    {
        _sceneIndex = (index + RestScreens.Scenes.Length) % RestScreens.Scenes.Length;
        var (file, name) = RestScreens.Scenes[_sceneIndex];
        _scene.Source = new BitmapImage(RestScreens.SceneUri(file));
        _sceneName.Text = name;
    }

    private void OnWord(string word)
    {
        Wake();
        if (_openWord == word) { ClosePanel(); return; }
        _openWord = word;
        _panelContent.Children.Clear();
        if (word == "Сцены")
        {
            _panel.Width = 700;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var i = 0; i < RestScreens.Scenes.Length; i++)
            {
                var index = i;
                var (file, name) = RestScreens.Scenes[i];
                var thumb = new StackPanel { Spacing = 8 };
                thumb.Children.Add(new Border
                {
                    Width = 150, Height = 84, CornerRadius = new CornerRadius(10), Opacity = i == _sceneIndex ? 1 : 0.7,
                    BorderBrush = Brush(i == _sceneIndex ? "#FFFFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(1),
                    Child = new Image { Source = new BitmapImage(RestScreens.SceneUri(file)), Stretch = Stretch.UniformToFill },
                });
                thumb.Children.Add(Text(name, 13, Hex(i == _sceneIndex ? "#FFFFFFFF" : "#8CFFFFFF")));
                var b = new Button { Content = thumb, Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
                b.Click += (_, _) => { SetScene(index); _openWord = null; OnWord("Сцены"); };
                row.Children.Add(b);
            }
            _panelContent.Children.Add(row);
            var more = Text("Свои картинки и видео — папка «Мои сцены» (этап 2–3).", 12, Hex("#73FFFFFF"));
            more.Margin = new Thickness(0, 12, 0, 0);
            _panelContent.Children.Add(more);
        }
        else
        {
            _panel.Width = 460;
            var (title, text) = word switch
            {
                "Продолжить" => ("Продолжить", "Последняя игра, недосмотренное и плейлист появятся здесь, когда GigaRest научится читать библиотеки игр (этап 5)."),
                "Играть" => ("Играть", "Последние игры из Steam, Battle.net, Epic и GOG — этап 5."),
                "Смотреть" => ("Смотреть", "«Смотреть позже» в личном браузере и папка с фильмами — этап 5."),
                _ => ("Слушать", "Плейлисты и звуки природы — этап 5. Яндекс Музыка уже на втором мониторе."),
            };
            _panelContent.Children.Add(Text(title, 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var body = Text(text, 13, Hex("#99FFFFFF"), wrap: true);
            body.Margin = new Thickness(0, 6, 0, 4);
            _panelContent.Children.Add(body);
        }
        _panel.Visibility = Visibility.Visible;
        HighlightWords();
    }

    private void ClosePanel()
    {
        _openWord = null;
        _panel.Visibility = Visibility.Collapsed;
        HighlightWords();
    }

    private void HighlightWords()
    {
        foreach (var child in _words.Children.OfType<Button>())
        {
            var tb = (TextBlock)child.Content;
            tb.Foreground = Brush(_openWord == tb.Text ? "#FFFFFFFF" : _openWord is null ? "#BFFFFFFF" : "#80FFFFFF");
        }
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        Wake();
        if (e.Key == global::Windows.System.VirtualKey.Escape && _openWord is not null) { ClosePanel(); e.Handled = true; }
    }

    private void Wake()
    {
        _lastInput = DateTime.Now;
        _words.Opacity = 1;
        _dim.Opacity = 0;
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString("HH:mm", Ru);
        _date.Text = now.ToString("dddd, d MMMM", Ru);
        var idle = now - _lastInput;

        // 20 секунд без действий — список закрывается, строка приглушается до 40%.
        if (idle > TimeSpan.FromSeconds(20))
        {
            if (_openWord is not null) ClosePanel();
            _words.Opacity = 0.4;
        }
        // 15 минут — сцена плавно темнеет до 10%.
        _dim.Opacity = idle > TimeSpan.FromMinutes(15) ? 0.9 : 0;

        // Ничего не стоит на месте дольше 2 минут: сдвигаем весь слой на 10–30 px.
        if (now - _lastShift > TimeSpan.FromMinutes(2))
        {
            _lastShift = now;
            var angle = _random.NextDouble() * Math.PI * 2;
            var radius = 10 + _random.NextDouble() * 20;
            _shift.X = Math.Cos(angle) * radius;
            _shift.Y = Math.Sin(angle) * radius;
        }

        // Игра или фильм на весь экран поверх — останавливаем движение, видеокарта свободна.
        var busy = UserState.ShouldNotDisturb();
        if (busy && !_paused) { _kenBurns.Pause(); _paused = true; }
        else if (!busy && _paused) { _kenBurns.Resume(); _paused = false; }
    }

    /// <summary>Итог перехода поверх сцены. С проблемами — висит, пока не закроют; без — исчезает через 8 с.</summary>
    public async void ShowSummary(string text, bool hasProblems)
    {
        _toastText.Text = text;
        _toastDetails.Visibility = hasProblems ? Visibility.Visible : Visibility.Collapsed;
        _toast.Visibility = Visibility.Visible;
        if (hasProblems) return;
        await Task.Delay(8000);
        _toast.Visibility = Visibility.Collapsed;
    }

    public void HideSummary() => _toast.Visibility = Visibility.Collapsed;

    /// <summary>Для скриншотов: запустить часы и анимацию без перехода на весь экран.</summary>
    public void StartForCapture()
    {
        _tick.Start();
        OnTick();
    }

    public void ShowOn(MonitorInfo? monitor)
    {
        RestScreens.FullScreenOn(this, monitor);
        RestScreens.BringToFront(this);
        _tick.Start();
        _kenBurns.Begin();
        Wake();
    }

    /// <summary>Для скриншотов: корень 1440×900, без полноэкранного режима.</summary>
    public void UseDesignSizeForCapture()
    {
        _root.Width = 1440;
        _root.Height = 900;
        Content = new Canvas { Children = { _root } };
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1440, 900));
        Activate();
    }

    public void CloseForGood()
    {
        _closing = true;
        _tick.Stop();
        _kenBurns.Stop();
        Close();
    }
}
