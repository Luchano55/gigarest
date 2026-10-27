using System.Globalization;
using System.Runtime.InteropServices;
using GigaRest.Core.Desktop;
using GigaRest.Core.Games;
using GigaRest.Core.Media;
using GigaRest.Core.Scenes;
using GigaRest.Core.Sounds;
using GigaRest.Core.Text;
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
    private static readonly Color TextColor = Hex("#E0FFFFFF"); // 88% — бережём OLED

    private readonly Grid _root;
    private readonly Grid _sceneHost = new();
    private UIElement? _current;
    private VideoLoop? _video;
    private readonly EffectsLayer _effects = new();
    private readonly IReadOnlyList<Scene> _scenes;
    private SceneEffect _effect;
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

    /// <summary>Таймеры простоя (затемнение, выключение экрана) — из настроек.</summary>
    public QuietTimers Timers { get; set; } = new();
    private bool _cursorHidden, _screenOff;
    private Border _offCard = null!;
    private TextBlock _offText = null!;
    private static readonly string[] WordList = ["Продолжить", "Играть", "Смотреть", "Слушать", "Сцены"];
    private int _wordIndex = -1;

    private DateTime _lastShift = DateTime.Now;
    private string? _openWord;
    private int _sceneIndex;
    private bool _paused;
    private bool _closing;
    private readonly Random _random = new();

    public event Action? ReturnRequested;
    /// <summary>Выбрали сцену или эффект — запомнить в настройках.</summary>
    public event Action<Scene, SceneEffect>? SceneChanged;
    public event Action? OpenMyScenesRequested;
    public event Action? DetailsRequested;
    /// <summary>Щёлкнули по «не беспокоить» (если Windows режим не приняла — открыть настройки).</summary>
    public event Action? DndClicked;
    private readonly Button _dnd;

    /// <summary>
    /// «Не беспокоить»: true — включено; false — не получилось, надпись зовёт включить вручную; null — выключено в настройках.
    /// </summary>
    public void SetDnd(bool? on)
    {
        _dnd.Visibility = on is null ? Visibility.Collapsed : Visibility.Visible;
        ((TextBlock)_dnd.Content).Text = on == false ? L.T("включить «Не беспокоить»", "turn on Do not disturb") : L.T("не беспокоить", "do not disturb");
        _dnd.Opacity = on == false ? 1 : 0.6;
    }

    /// <summary>Щёлкнули по игре в «Играть» или «Продолжить».</summary>
    public event Action<Game>? PlayRequested;

    // null — библиотеки ещё читаются.
    private IReadOnlyList<Game>? _games;
    private IReadOnlyList<Film>? _films;
    private Film? _lastFilm;
    private IReadOnlyDictionary<string, double> _positions = new Dictionary<string, double>();
    private string _filmsFolderName = L.T("Видео", "Videos");
    private AppChoice? _musicApp;
    private IReadOnlyList<AppChoice> _players = [];
    private string? _player; // null — встроенный

    public event Action<AppChoice>? MusicAppRequested;
    public event Action? MusicAppPickRequested;
    public event Action? FilmsFolderPickRequested;
    public event Action<string?>? PlayerChosen;
    public event Action? PlayerPickRequested;
    /// <summary>Встроенный плеер закрыт: фильм, где остановились и длительность (секунды).</summary>
    public event Action<Film, double, double>? FilmClosed;
    /// <summary>Встроенный плеер файл не открыл.</summary>
    public event Action<Film>? FilmFailed;

    public void SetMusicApp(AppChoice? app) { _musicApp = app; Refresh("Слушать"); }

    private List<WebService> _services = WebServices.Defaults();
    private IReadOnlyList<AppChoice> _browsers = [];

    /// <summary>Список сервисов поменялся (добавили, убрали, сменили браузер) — сохранить настройки.</summary>
    public event Action? ServicesChanged;
    /// <summary>Сервис открывается не здесь, а в браузере: сервис и адрес.</summary>
    public event Action<WebService, string>? OpenInBrowserRequested;

    /// <summary>Сайты для «Смотреть» и «Слушать» — тот же список, что в настройках (меняется на месте).</summary>
    public void SetServices(List<WebService> services) { _services = services; Refresh("Смотреть", "Слушать"); }

    public void SetBrowsers(IReadOnlyList<AppChoice> browsers) { _browsers = browsers; }

    public void SetPlayers(IReadOnlyList<AppChoice> players, string? current)
    {
        _players = players;
        _player = current;
        Refresh("Смотреть", "Плеер");
    }

    /// <summary>Раздел (ключ — русское слово) → надпись на языке интерфейса.</summary>
    private static string Display(string word) => word switch
    {
        "Продолжить" => L.T("Продолжить", "Continue"),
        "Играть" => L.T("Играть", "Play"),
        "Смотреть" => L.T("Смотреть", "Watch"),
        "Слушать" => L.T("Слушать", "Listen"),
        "Сцены" => L.T("Сцены", "Scenes"),
        "Плеер" => L.T("Плеер", "Player"),
        _ => word,
    };

    /// <summary>Магазин (ключ) → надпись: «Свои» — игры, добавленные вручную.</summary>
    private static string StoreName(string store) => store == "Свои" ? L.T("Свои", "My games") : store;

    private void Refresh(params string[] words)
    {
        if (_openWord is { } w && words.Contains(w)) { _openWord = null; OnWord(w); }
    }

    public event Action<Film>? FilmRequested;
    public event Action? OpenFilmsFolderRequested;

    /// <summary>Что играет — по умолчанию из Windows; для скриншотов подменяется.</summary>
    internal Func<Task<NowPlaying.Current?>> Media { get; set; } = NowPlaying.CurrentAsync;
    internal Func<MediaCommand, Task> MediaControl { get; set; } = NowPlaying.ControlAsync;

    /// <summary>Фильмы из папки (новые сверху) и последний открытый из GigaRest.</summary>
    public void SetFilms(IReadOnlyList<Film> films, Film? lastOpened, IReadOnlyDictionary<string, double> positions, string folderName)
    {
        _films = films;
        _lastFilm = lastOpened;
        _positions = positions;
        _filmsFolderName = folderName;
        Refresh("Смотреть", "Продолжить");
    }

    /// <summary>Установленные игры: сначала недавние. Вызывать из потока окна.</summary>
    // ---------- звуки природы и «Продолжить» ----------

    private readonly NaturePlayer _nature = new();
    private NatureSound? _lastNature;
    private (string Url, string Title, DateTime Utc)? _lastYouTube;

    /// <summary>Выбрали звук природы (null — тишина) или громкость — запомнить.</summary>
    public event Action<NatureSound?, double>? NatureChanged;
    /// <summary>В YouTube открыли ролик — запомнить для «Продолжить».</summary>
    public event Action<string, string>? YouTubeWatched;

    public void InitNature(NatureSound? last, double volume)
    {
        _lastNature = last;
        _nature.Volume = volume;
    }

    public void SetLastYouTube(string? url, string? title, DateTime? utc)
    {
        _lastYouTube = url is not null && utc is { } t ? (url, title ?? L.T("ролик", "video"), t) : null;
    }

    public event Action? AddGameRequested;
    public event Action<Game>? RemoveGameRequested;
    public event Action? OpenCoversRequested;

    public void SetGames(IReadOnlyList<Game> games)
    {
        _games = games;
        if (_library is not null) ShowLibrary(); // библиотека открыта — обновить (добавили или убрали игру)
        if (_openWord is "Играть" or "Продолжить") { var w = _openWord; _openWord = null; OnWord(w); }
    }
    public FrameworkElement Root => _root;

    public QuietWindow(IReadOnlyList<Scene> scenes, string? sceneId = null, SceneEffect? effect = null)
    {
        _scenes = scenes;
        Title = L.T("GigaRest — отдых", "GigaRest — rest");
        _root = new Grid { Background = Brush("#05060A"), RequestedTheme = ElementTheme.Dark };

        // Сцена с медленным движением (Ken Burns) — анимацию считает системный композитор.
        _sceneHost.RenderTransform = _sceneMove;
        _root.Children.Add(_sceneHost);

        var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        shade.GradientStops.Add(new GradientStop { Color = Hex("#59040509"), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#00040509"), Offset = 0.22 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#00040509"), Offset = 0.62 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#B8040509"), Offset = 1 });
        _root.Children.Add(new Border { Background = shade });
        _root.Children.Add(_effects.Element);

        // Всё, что поверх сцены, — в одном слое: его и сдвигаем для защиты OLED.
        _overlay = new Grid { Padding = new Thickness(64, 52, 64, 52), RenderTransform = _shift };

        var clockBox = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _clock = new TextBlock { FontFamily = new FontFamily("Georgia"), FontSize = 64, Foreground = Brush(TextColor) };
        _date = Text("", 14, Hex("#99FFFFFF"));
        clockBox.Children.Add(_clock);
        clockBox.Children.Add(_date);
        _overlay.Children.Add(clockBox);

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
        _dnd = QuietButton(L.T("не беспокоить", "do not disturb"), 13);
        _dnd.Opacity = 0.6;
        _dnd.Visibility = Visibility.Collapsed;
        _dnd.Click += (_, _) => DndClicked?.Invoke();
        top.Children.Add(_dnd);
        var back = QuietButton(L.T("Вернуться к работе", "Back to work"), 13);
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
            var b = QuietButton(Display(w), 17);
            b.Tag = w;
            b.Click += (_, _) => OnWord(w);
            _words.Children.Add(b);
        }
        bottom.Children.Add(_words);
        _sceneName = Text("", 13, Hex("#73FFFFFF"));
        _sceneName.Visibility = Visibility.Collapsed; // название сцены видно только в списке «Сцены»
        _sceneName.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_sceneName, 1);
        bottom.Children.Add(_sceneName);
        _overlay.Children.Add(bottom);

        // Итог перехода — ненадолго, сверху по центру.
        _toastText = Text("", 15, TextColor, wrap: true);
        _toastDetails = QuietButton(L.T("Подробнее", "Details"), 13);
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

        // «Экран выключится через 60 с» — после долгого простоя (защита OLED).
        _offText = Text("", 15, TextColor, wrap: true);
        var offNow = QuietButton(L.T("Выключить сейчас", "Turn off now"), 13);
        offNow.Click += (_, _) => TurnScreenOff();
        var offBox = new StackPanel { Spacing = 6 };
        offBox.Children.Add(_offText);
        offBox.Children.Add(Text(L.T("Пошевелите мышью или нажмите любую клавишу — экран останется включённым.", "Move the mouse or press any key to keep the screen on."), 12, Hex("#99FFFFFF"), wrap: true));
        offBox.Children.Add(offNow);
        _offCard = new Border
        {
            Background = Brush("#CC0C0D12"), BorderBrush = Brush("#14FFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 16, 22, 14), MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed, Child = offBox,
        };

        // Затемнение после 15 минут без действий (сцена — до 10%).
        _dim = new Border { Background = Brush(Colors.Black), Opacity = 0, IsHitTestVisible = false };
        _root.Children.Add(_dim);
        _root.Children.Add(_offCard); // над затемнением — его видно

        Content = _root;
        _root.PointerMoved += (_, _) => Wake();
        _root.PointerPressed += (_, _) => Wake();
        _root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        _tick.Tick += (_, _) => OnTick();
        var start = sceneId is null ? 0 : Math.Max(0, _scenes.ToList().FindIndex(x => x.Id == sceneId));
        SetScene(start, effect);
        _root.SizeChanged += (_, _) => _effects.Set(_effect, _root.ActualWidth, _root.ActualHeight);
        OnTick();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (_, e) => { if (!_closing) e.Cancel = true; };
    }

    /// <summary>Медленное движение к точке фокуса сцены; скорость — из настроек сцены.</summary>
    private void SetMotion(Scene scene)
    {
        _kenBurns.Stop();
        _kenBurns.Children.Clear();
        _sceneHost.RenderTransformOrigin = new global::Windows.Foundation.Point(scene.FocusX, scene.FocusY);
        var seconds = 45 / scene.Speed;
        AddMove("ScaleX", 1.03, 1.09, seconds);
        AddMove("ScaleY", 1.03, 1.09, seconds);
        AddMove("TranslateX", 0, -(scene.FocusX - 0.5) * 60 - 12, seconds);
        AddMove("TranslateY", 0, -(scene.FocusY - 0.5) * 30 - 5, seconds);
        if (!_paused) _kenBurns.Begin();
    }

    private void AddMove(string property, double from, double to, double seconds)
    {
        var a = new DoubleAnimation
        {
            From = from, To = to, Duration = new Duration(TimeSpan.FromSeconds(seconds)), AutoReverse = true,
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

    private void SetScene(int index, SceneEffect? effect = null)
    {
        if (_scenes.Count == 0) return;
        _sceneIndex = (index + _scenes.Count) % _scenes.Count;
        var scene = _scenes[_sceneIndex];
        _effect = effect ?? scene.Effect;

        // Новая сцена проступает поверх старой за секунду — без моргания.
        UIElement next;
        VideoLoop? video = null;
        if (scene.IsVideo)
        {
            next = video = new VideoLoop(new Uri(scene.Path));
        }
        else
        {
            // 4K-обои — в полном разрешении, без пережатия.
            next = new Image { Source = new BitmapImage(new Uri(scene.Path)), Stretch = Stretch.UniformToFill };
        }
        var previous = _current;
        var previousVideo = _video;
        _current = next;
        _video = video;
        _sceneHost.Children.Add(next);
        if (previous is null)
        {
            next.Opacity = 1;
        }
        else
        {
            next.Opacity = 0;
            var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(1.2), EasingFunction = new SineEase() };
            Storyboard.SetTarget(fade, next);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var board = new Storyboard();
            board.Children.Add(fade);
            board.Completed += (_, _) =>
            {
                _sceneHost.Children.Remove(previous);
                previousVideo?.Dispose();
            };
            board.Begin();
        }
        SetMotion(scene);
        var fx = SceneCatalog.EffectName(_effect);
        _sceneName.Text = scene.Name + (fx.Length > 0 ? " · " + fx : "");
        _effects.Set(_effect, _root.ActualWidth, _root.ActualHeight);
    }

    private void OnWord(string word)
    {
        Wake();
        if (_openWord == word) { ClosePanel(); return; }
        _openWord = word;
        _panelContent.Children.Clear();
        if (word == "Сцены")
        {
            _panel.Width = 760;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var i = 0; i < _scenes.Count; i++)
            {
                var index = i;
                var scene = _scenes[i];
                var current = i == _sceneIndex;
                UIElement picture = scene.IsVideo
                    ? new Grid { Background = Brush("#33FFFFFF"), Children = { new TextBlock { Text = L.T("▶ видео", "▶ video"), Foreground = Brush("#CCFFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } }
                    : new Image { Source = new BitmapImage(new Uri(scene.Path)) { DecodePixelWidth = 300 }, Stretch = Stretch.UniformToFill };
                var thumb = new StackPanel { Spacing = 8, Width = 150 };
                thumb.Children.Add(new Border
                {
                    Width = 150, Height = 84, CornerRadius = new CornerRadius(10), Opacity = current ? 1 : 0.7,
                    BorderBrush = Brush(current ? "#FFFFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(1), Child = picture,
                });
                var label = Text(scene.Name, 13, Hex(current ? "#FFFFFFFF" : "#8CFFFFFF"));
                thumb.Children.Add(label);
                var b = new Button { Content = thumb, Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
                b.Click += (_, _) => { SetScene(index); SceneChanged?.Invoke(_scenes[_sceneIndex], _effect); _openWord = null; OnWord("Сцены"); };
                row.Children.Add(b);
            }
            _panelContent.Children.Add(new ScrollViewer
            {
                Content = row, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 10),
            });

            // Эффект поверх — один за раз.
            var effects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 10, 0, 0) };
            var over = Text(L.T("Поверх", "Overlay"), 13, Hex("#80FFFFFF"));
            over.VerticalAlignment = VerticalAlignment.Center;
            over.Margin = new Thickness(0, 0, 8, 0);
            effects.Children.Add(over);
            foreach (var (fx, label) in new[] { (SceneEffect.None, L.T("Ничего", "None")), (SceneEffect.Snow, L.T("Снег", "Snow")), (SceneEffect.Fog, L.T("Туман", "Fog")), (SceneEffect.Lights, L.T("Огоньки", "Lights")), (SceneEffect.Rain, L.T("Дождь", "Rain")) })
            {
                var selected = fx == _effect;
                var seg = new Button
                {
                    Content = new TextBlock { Text = label, FontSize = 13, Foreground = Brush(selected ? "#FF0B0C10" : "#B3FFFFFF") },
                    Background = Brush(selected ? "#E6FFFFFF" : "#00FFFFFF"), BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 12, 6),
                };
                seg.Click += (_, _) =>
                {
                    _effect = fx;
                    _effects.Set(_effect, _root.ActualWidth, _root.ActualHeight);
                    var name = SceneCatalog.EffectName(_effect);
                    _sceneName.Text = _scenes[_sceneIndex].Name + (name.Length > 0 ? " · " + name : "");
                    SceneChanged?.Invoke(_scenes[_sceneIndex], _effect);
                    _openWord = null;
                    OnWord("Сцены");
                };
                effects.Children.Add(seg);
            }
            _panelContent.Children.Add(effects);

            var folder = QuietButton(L.T("Свои картинки и видео — папка «Мои сцены» (открыть)", "Your own pictures and videos — the “My scenes” folder (open)"), 12);
            folder.Margin = new Thickness(0, 8, 0, 0);
            folder.Click += (_, _) => OpenMyScenesRequested?.Invoke();
            _panelContent.Children.Add(folder);
        }
        else if (word == "Играть")
        {
            _panel.Width = _games is { Count: > 0 } ? 820 : 520;
            _panelContent.Children.Add(Text(Display("Играть"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            if (_games is null || _games.Count == 0)
            {
                var note = Text(_games is null ? L.T("Ищем игры…", "Looking for games…") : L.T("Игр не нашлось. GigaRest смотрит в Steam, Epic, GOG и Battle.net.", "No games found. GigaRest looks in Steam, Epic, GOG and Battle.net."), 13, Hex("#99FFFFFF"), wrap: true);
                note.Margin = new Thickness(0, 6, 0, 4);
                _panelContent.Children.Add(note);
                if (_games is not null)
                {
                    var open = QuietButton(L.T("Открыть библиотеку и добавить игру без магазина →", "Open the library and add a game from outside a store →"), 13);
                    open.Click += (_, _) => ShowLibrary();
                    _panelContent.Children.Add(open);
                }
            }
            else
            {
                // Сверху — недавние обложками, ниже — все игры списком.
                var recent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 12, 0, 4) };
                foreach (var game in _games.Take(5)) recent.Children.Add(GameCard(game));
                _panelContent.Children.Add(recent);
                if (_games.Count > 5)
                {
                    var all = Text(L.T("Все игры", "All games"), 13, Hex("#99FFFFFF"));
                    all.Margin = new Thickness(0, 12, 0, 0);
                    _panelContent.Children.Add(all);
                    var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 4, -12, 0) };
                    foreach (var game in _games.Skip(5)) list.Children.Add(GameListRow(game));
                    _panelContent.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                }
                var library = QuietButton(L.T($"Вся библиотека — {Sizes.Plural(_games.Count, "игра", "игры", "игр")} →", $"Whole library — {L.Plural(_games.Count, "игра", "игры", "игр", "game", "games")} →"), 13);
                library.Margin = new Thickness(0, 8, 0, 0);
                library.Click += (_, _) => ShowLibrary();
                _panelContent.Children.Add(library);
            }
        }
        else if (word == "Продолжить")
        {
            _panel.Width = 540;
            _panelContent.Children.Add(Text(Display("Продолжить"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            if (_games?.FirstOrDefault(g => g.LastPlayedUtc is not null) is { } last) rows.Children.Add(GameRow(last, big: true));
            if (_lastFilm is { } film) rows.Children.Add(FilmRow(film, big: true));
            if (_lastYouTube is { } yt && DateTime.UtcNow - yt.Utc < TimeSpan.FromDays(14))
                rows.Children.Add(LinkRow("▶  " + yt.Title, "YouTube · " + GameLibraries.LastPlayedText(yt.Utc, DateTime.Now),
                    () => ShowWeb(YouTube, "YouTube", yt.Url, navigate: true), big: true));
            if (_lastNature is { } sound && _nature.Current is null)
                rows.Children.Add(LinkRow(L.T("♫  Звуки: ", "♫  Sounds: ") + NatureSounds.Name(sound), L.T("как в прошлый раз", "like last time"), () => PlayNature(sound), big: true));
            _panelContent.Children.Add(rows);
            var music = new StackPanel { Margin = new Thickness(-12, 0, -12, 0) };
            _panelContent.Children.Add(music);
            _ = FillContinueMusicAsync(music);
            if (rows.Children.Count == 0)
            {
                var empty = Text(_games is null ? L.T("Ищем, что продолжить…", "Looking for something to continue…") : L.T("Здесь будут последняя игра, фильм, ролик YouTube и музыка — как только вы их откроете в GigaRest.", "Your last game, film, YouTube video and music will show up here once you open them in GigaRest."), 13, Hex("#99FFFFFF"), wrap: true);
                empty.Margin = new Thickness(0, 6, 0, 4);
                _panelContent.Children.Add(empty);
            }
        }
        else if (word == "Слушать")
        {
            _panel.Width = 520;
            _panelContent.Children.Add(Text(Display("Слушать"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var listen = _services.Where(x => x.Kind == ServiceKind.Listen).ToList();
            var web = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            foreach (var service in listen) web.Children.Add(ServiceRow(service, big: listen.Count <= 2));
            _panelContent.Children.Add(listen.Count > 3 ? new ScrollViewer { Content = web, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : web);
            _panelContent.Children.Add(AddServiceLink(ServiceKind.Listen));
            var box = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            box.Children.Add(Text("…", 13, Hex("#99FFFFFF")));
            _panelContent.Children.Add(box);
            _ = FillListenAsync(box);
            _panelContent.Children.Add(NatureSection());
        }
        else if (word == "Смотреть")
        {
            _panel.Width = 560;
            _panelContent.Children.Add(Text(Display("Смотреть"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            foreach (var service in _services.Where(x => x.Kind == ServiceKind.Watch))
            {
                list.Children.Add(ServiceRow(service));
                if (WebServices.IsYouTube(service) && service.OpenIn is null)
                    list.Children.Add(ServiceRow(service, lastWatched: true));
            }
            if (_films is { Count: > 0 })
                foreach (var film in _films.Take(30)) list.Children.Add(FilmRow(film));
            _panelContent.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            if (_films is { Count: 0 })
            {
                var empty = Text(L.T($"В папке «{_filmsFolderName}» видео нет. Выберите папку, где лежат фильмы.", $"No videos in the “{_filmsFolderName}” folder. Choose the folder where your films are."), 13, Hex("#99FFFFFF"), wrap: true);
                empty.Margin = new Thickness(0, 6, 0, 0);
                _panelContent.Children.Add(empty);
            }
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 6, 0, 0) };
            var folder = QuietButton(_films is null ? L.T("Ищем фильмы…", "Looking for films…") : L.T($"Папка: {_filmsFolderName} · выбрать", $"Folder: {_filmsFolderName} · choose"), 12);
            folder.Click += (_, _) => FilmsFolderPickRequested?.Invoke();
            footer.Children.Add(folder);
            var open = QuietButton(L.T("открыть", "open"), 12);
            open.Click += (_, _) => OpenFilmsFolderRequested?.Invoke();
            footer.Children.Add(open);
            footer.Children.Add(AddServiceLink(ServiceKind.Watch, inline: true));
            var player = QuietButton(L.T($"Плеер: {PlayerName} · сменить", $"Player: {PlayerName} · change"), 12);
            player.Click += (_, _) => { _openWord = null; OnWord("Плеер"); };
            footer.Children.Add(player);
            _panelContent.Children.Add(footer);
        }
        else if (word.StartsWith(AddServiceWord, StringComparison.Ordinal))
        {
            FillAddService(word == AddServiceWord + nameof(ServiceKind.Watch) ? ServiceKind.Watch : ServiceKind.Listen);
        }
        else if (word == "Плеер")
        {
            _panel.Width = 560;
            _panelContent.Children.Add(Text(L.T("Чем открывать фильмы", "How to open films"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            list.Children.Add(ChoiceRow(L.T("Встроенный плеер GigaRest", "GigaRest built-in player"), L.T("фильм открывается прямо здесь", "the film opens right here"), _player is null, () => PlayerChosen?.Invoke(null)));
            foreach (var p in _players)
                list.Children.Add(ChoiceRow(p.Name, L.T("ваш плеер, на весь экран", "your player, full screen"), string.Equals(_player, p.Path, StringComparison.OrdinalIgnoreCase), () => PlayerChosen?.Invoke(p.Path)));
            if (_player is not null && !_players.Any(p => string.Equals(p.Path, _player, StringComparison.OrdinalIgnoreCase)))
                list.Children.Add(ChoiceRow(Apps.NameOf(_player), L.T("выбран вами", "chosen by you"), true, () => { }));
            list.Children.Add(ChoiceRow(L.T("Другой плеер…", "Another player…"), L.T("выбрать программу (.exe)", "choose a program (.exe)"), false, () => PlayerPickRequested?.Invoke()));
            _panelContent.Children.Add(list);
            var back = QuietButton("← " + Display("Смотреть"), 12);
            back.Margin = new Thickness(0, 6, 0, 0);
            back.Click += (_, _) => { _openWord = null; OnWord("Смотреть"); };
            _panelContent.Children.Add(back);
        }
        else
        {
            _panel.Width = 460;
            var (title, text) = word switch
            {
                "Продолжить" => (Display("Продолжить"), _games is null ? L.T("Ищем последнюю игру…", "Looking for your last game…") : L.T("Здесь будут последняя игра и последний фильм — как только вы запустите их отсюда, из «Играть» или «Смотреть».", "Your last game and film will show up here once you start them from Play or Watch.")),
                _ => (Display(word), ""),
            };
            _panelContent.Children.Add(Text(title, 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var body = Text(text, 13, Hex("#99FFFFFF"), wrap: true);
            body.Margin = new Thickness(0, 6, 0, 4);
            _panelContent.Children.Add(body);
        }
        _panel.Visibility = Visibility.Visible;
        HighlightWords();
    }

    /// <summary>Строка игры: название слева, магазин и «когда играли» справа; щелчок — запуск.</summary>
    private Button GameRow(Game game, bool big = false)
    {
        if (big) return GameListRow(game, big: true);
        var when = GameLibraries.LastPlayedText(game.LastPlayedUtc, DateTime.Now);
        return LinkRow(game.Name, when.Length > 0 ? $"{StoreName(game.Store)} · {when}" : StoreName(game.Store), () => PlayRequested?.Invoke(game));
    }

    private static string GameMeta(Game game)
    {
        var when = GameLibraries.LastPlayedText(game.LastPlayedUtc, DateTime.Now);
        return when.Length > 0 ? $"{StoreName(game.Store)} · {when}" : StoreName(game.Store);
    }

    /// <summary>Карточка недавней игры: вертикальная обложка, название, магазин.</summary>
    private Button GameCard(Game game)
    {
        var box = new StackPanel { Spacing = 6, Width = 136 };
        box.Children.Add(Cover(game, 136, 204));
        var name = Text(game.Name, 13, Hex("#E6FFFFFF"), wrap: true);
        name.MaxLines = 2;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        box.Children.Add(name);
        box.Children.Add(Text(GameMeta(game), 11, Hex("#73FFFFFF")));
        return GameButton(box, game, new Thickness(0));
    }

    /// <summary>Строка игры с маленькой обложкой; big — для «Продолжить».</summary>
    private Button GameListRow(Game game, bool big = false)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Cover(game, big ? 64 : 30, big ? 96 : 45));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var name = Text(big ? $"▶  {game.Name}" : game.Name, big ? 19 : 15, Hex("#E6FFFFFF"), big ? FontWeights.SemiBold : null);
        text.Children.Add(name);
        text.Children.Add(Text(GameMeta(game), 12, Hex("#73FFFFFF")));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return GameButton(grid, game, new Thickness(12, big ? 10 : 6, 12, big ? 10 : 6));
    }

    private Button GameButton(UIElement content, Game game, Thickness padding)
    {
        var b = new Button
        {
            Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12), Padding = padding,
        };
        b.Resources["ButtonBackgroundPointerOver"] = Brush("#1AFFFFFF");
        b.Resources["ButtonBackgroundPressed"] = Brush("#26FFFFFF");
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        b.Click += (_, _) => { ClosePanel(); PlayRequested?.Invoke(game); };
        return b;
    }

    /// <summary>
    /// Обложка: из кэша магазина; нет — значок игры на тёмной подложке; нет и его — название на подложке (ТЗ, раздел 8).
    /// </summary>
    private static Border Cover(Game game, double width, double height)
    {
        var tint = ProgramColor(game.Name);
        var bg = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        bg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, (byte)(tint.R / 3), (byte)(tint.G / 3), (byte)(tint.B / 3)), Offset = 0 });
        bg.GradientStops.Add(new GradientStop { Color = Hex("#FF14151C"), Offset = 1 });
        var frame = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(width > 60 ? 10 : 5), Background = bg, BorderBrush = Brush("#1AFFFFFF"), BorderThickness = new Thickness(1) };
        if (game.CoverPath is { } cover && File.Exists(cover))
        {
            frame.Child = new Image { Source = new BitmapImage(new Uri(cover)) { DecodePixelWidth = (int)(width * 2) }, Stretch = Stretch.UniformToFill };
            return frame;
        }
        var fallback = new Grid();
        if (width > 60)
        {
            var title = Text(game.Name, 13, Hex("#CCFFFFFF"), FontWeights.SemiBold, wrap: true);
            title.TextAlignment = TextAlignment.Center;
            title.Margin = new Thickness(10);
            title.VerticalAlignment = VerticalAlignment.Bottom;
            fallback.Children.Add(title);
        }
        frame.Child = fallback;
        if (game.IconPath is { } icon && File.Exists(icon)) _ = LoadIconAsync(fallback, icon, width > 60 ? 56 : 22);
        return frame;
    }

    // Значок exe — так же, как его показывает проводник.
    private static async Task LoadIconAsync(Grid into, string path, double size)
    {
        try
        {
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(global::Windows.Storage.FileProperties.ThumbnailMode.SingleItem, (uint)(size * 2));
            if (thumb is null) return;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(thumb);
            into.Children.Add(new Image { Source = bmp, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        }
        catch (Exception ex)
        {
            GigaRest.Core.Logging.Log.Warn("Значок игры: " + ex.Message);
        }
    }

    private async Task FillListenAsync(StackPanel box)
    {
        var current = await Media();
        if (_openWord != "Слушать") return;
        box.Children.Clear();
        if (current is null)
        {
            if (_musicApp is { } app)
            {
                var start = LinkRow(L.T($"▶  Открыть {app.Name}", $"▶  Open {app.Name}"), "", () => MusicAppRequested?.Invoke(app), big: true);
                start.Margin = new Thickness(-12, 0, -12, 0);
                box.Children.Add(start);
                box.Children.Add(Text(L.T("Включите музыку — здесь появятся кнопки «назад», «пауза», «дальше».", "Start some music and the back, pause and next buttons will appear here."), 12, Hex("#73FFFFFF"), wrap: true));
            }
            else
            {
                box.Children.Add(Text(L.T("Сейчас ничего не играет, а программу для музыки GigaRest не нашёл.", "Nothing is playing, and GigaRest didn’t find a music app."), 13, Hex("#99FFFFFF"), wrap: true));
            }
            box.Children.Add(MusicAppLink());
            return;
        }
        var track = Text(current.Text, 19, Hex("#E6FFFFFF"), FontWeights.SemiBold, wrap: true);
        box.Children.Add(track);
        if (current.Source.Length > 0) box.Children.Add(Text(current.Source + (current.Playing ? L.T(" · играет", " · playing") : L.T(" · пауза", " · paused")), 12, Hex("#73FFFFFF")));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(-8, 4, 0, 0) };
        foreach (var (command, glyph) in new[] { (MediaCommand.Previous, "\uE892"), (MediaCommand.PlayPause, current.Playing ? "\uE769" : "\uE768"), (MediaCommand.Next, "\uE893") })
        {
            var b = new Button
            {
                Content = new FontIcon { Glyph = glyph, FontSize = command == MediaCommand.PlayPause ? 22 : 16, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = Brush("#E6FFFFFF") },
                Width = 52, Height = 44, Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12),
            };
            b.Resources["ButtonBackgroundPointerOver"] = Brush("#1AFFFFFF");
            b.Resources["ButtonBackgroundPressed"] = Brush("#26FFFFFF");
            b.Click += async (_, _) =>
            {
                Wake();
                await MediaControl(command);
                await Task.Delay(600); // программа музыки отвечает не сразу
                if (_openWord == "Слушать") await FillListenAsync(box);
            };
            buttons.Children.Add(b);
        }
        box.Children.Add(buttons);
        box.Children.Add(MusicAppLink());
    }

    private Button MusicAppLink()
    {
        var link = QuietButton(_musicApp is { } app ? L.T($"Программа: {app.Name} · сменить", $"App: {app.Name} · change") : L.T("Выбрать программу для музыки…", "Choose a music app…"), 12);
        link.Click += (_, _) => MusicAppPickRequested?.Invoke();
        return link;
    }

    private string PlayerName => _player is null ? L.T("встроенный", "built-in") : _players.FirstOrDefault(p => string.Equals(p.Path, _player, StringComparison.OrdinalIgnoreCase))?.Name ?? Apps.NameOf(_player);

    private Button ChoiceRow(string title, string meta, bool selected, Action click) =>
        LinkRow((selected ? "✓  " : "     ") + title, meta, () => { click(); }, keepOpen: true);

    private Button FilmRow(Film film, bool big = false)
    {
        var added = GameLibraries.LastPlayedText(film.ModifiedUtc, DateTime.Now);
        var meta = _positions.TryGetValue(film.Path, out var pos) ? L.T("с ", "from ") + Apps.Position(pos)
            : big ? L.T("фильм", "film") : added.Length > 0 ? L.T("добавлен ", "added ") + added : "";
        return LinkRow(big ? $"▶  {film.Name}" : film.Name, meta, () => FilmRequested?.Invoke(film), big);
    }

    // ---------- свои сервисы: сайты с видео и музыкой, добавляются как свои игры ----------

    private const string AddServiceWord = "Добавить сервис:";

    private string BrowserName(string? openIn) => openIn switch
    {
        null => "",
        WebServices.DefaultBrowser => L.T("браузер", "browser"),
        _ => _browsers.FirstOrDefault(b => string.Equals(b.Path, openIn, StringComparison.OrdinalIgnoreCase))?.Name ?? Apps.NameOf(openIn),
    };

    /// <summary>Строка сервиса; правый щелчок — где открывать и «Убрать». lastWatched — вторая строка YouTube.</summary>
    private Button ServiceRow(WebService s, bool big = false, bool lastWatched = false)
    {
        var glyph = s.Kind == ServiceKind.Watch ? "▶  " : "♫  ";
        var open = _web.ContainsKey(WebKey(s));
        string title, meta;
        if (lastWatched) (title, meta) = (glyph + s.Name + L.T(" · последний ролик", " · last video"), L.T("тот, что смотрели последним", "the one you watched last"));
        else if (s.OpenIn is not null) (title, meta) = (glyph + s.Name, L.T("откроется: ", "opens in: ") + BrowserName(s.OpenIn) + " ↗");
        else if (open) (title, meta) = (glyph + L.T("Вернуться к ", "Back to ") + s.Name, L.T("продолжить, где были", "pick up where you left off"));
        else (title, meta) = (glyph + s.Name, WebServices.IsYouTube(s) ? L.T("главная, прямо здесь", "home page, right here") : L.T("прямо здесь", "right here"));
        var row = LinkRow(title, meta, () => OpenService(s, lastWatched), big);

        var menu = new MenuFlyout();
        void Choice(string text, string? openIn)
        {
            var item = new MenuFlyoutItem { Text = (string.Equals(s.OpenIn, openIn, StringComparison.OrdinalIgnoreCase) ? "✓  " : "     ") + text };
            item.Click += (_, _) =>
            {
                s.OpenIn = openIn;
                ServicesChanged?.Invoke();
                Refresh("Смотреть", "Слушать");
            };
            menu.Items.Add(item);
        }
        Choice(L.T("Открывать здесь, в GigaRest", "Open here, in GigaRest"), null);
        Choice(L.T("Открывать в браузере по умолчанию", "Open in the default browser"), WebServices.DefaultBrowser);
        foreach (var b in _browsers) Choice(L.T("Открывать в ", "Open in ") + b.Name, b.Path);
        menu.Items.Add(new MenuFlyoutSeparator());
        var remove = new MenuFlyoutItem { Text = L.T("Убрать из списка", "Remove from list") };
        remove.Click += (_, _) =>
        {
            if (_web.ContainsKey(WebKey(s)) && _services.Count(x => WebKey(x) == WebKey(s)) == 1) CloseWeb(WebKey(s));
            _services.Remove(s);
            ServicesChanged?.Invoke();
            Refresh("Смотреть", "Слушать");
        };
        menu.Items.Add(remove);
        row.ContextFlyout = menu;
        ToolTipService.SetToolTip(row, L.T("Правый щелчок — где открывать или убрать", "Right-click to choose where it opens or remove it"));
        return row;
    }

    private Button AddServiceLink(ServiceKind kind, bool inline = false)
    {
        var b = QuietButton(inline ? L.T("+ сервис", "+ service") : L.T("+ Добавить сервис…", "+ Add a service…"), 12);
        if (!inline) b.Margin = new Thickness(0, 2, 0, 0);
        b.Click += (_, _) => { _openWord = null; OnWord(AddServiceWord + kind); };
        return b;
    }

    /// <summary>Панель «Добавить сервис»: готовые одной кнопкой или свой адрес.</summary>
    private void FillAddService(ServiceKind kind)
    {
        var section = kind == ServiceKind.Watch ? "Смотреть" : "Слушать";
        _panel.Width = 560;
        _panelContent.Children.Add(Text(L.T($"Добавить в «{section}»", $"Add to {Display(section)}"), 17, Hex("#E6FFFFFF"), FontWeights.Medium));

        void Added(WebService service)
        {
            WebServices.Add(_services, service);
            ServicesChanged?.Invoke();
            _openWord = null;
            OnWord(section);
        }

        var presets = WebServices.Presets.Where(p => p.Kind == kind && !_services.Any(x => x.Kind == kind && WebServices.SameSite(x.Url, p.Url))).ToList();
        if (presets.Count > 0)
        {
            var label = Text(L.T("Готовые", "Ready to add"), 13, Hex("#99FFFFFF"));
            label.Margin = new Thickness(0, 10, 0, 6);
            _panelContent.Children.Add(label);
            var rows = new StackPanel { Spacing = 8 };
            foreach (var chunk in presets.Chunk(4))
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var p in chunk) line.Children.Add(Chip(p.Protected ? p.Name + " ↗" : p.Name, false, () => Added(WebServices.FromPreset(p))));
                rows.Children.Add(line);
            }
            _panelContent.Children.Add(rows);
            if (presets.Any(p => p.Protected))
            {
                var note = Text(L.T("↗ — фильмы и музыка по подписке защищены от копирования и во встроенном окне обычно не играют. Такие сервисы откроются в вашем браузере; правым щелчком по сервису это можно поменять.", "↗ — subscription films and music are copy-protected and usually won’t play in the built-in window. These services open in your browser; right-click a service to change that."), 12, Hex("#73FFFFFF"), wrap: true);
                note.Margin = new Thickness(0, 8, 0, 0);
                _panelContent.Children.Add(note);
            }
        }

        var own = Text(L.T("Свой сайт", "Your own site"), 13, Hex("#99FFFFFF"));
        own.Margin = new Thickness(0, 16, 0, 6);
        _panelContent.Children.Add(own);
        var address = new TextBox { PlaceholderText = L.T("адрес, например kino.example.ru", "address, e.g. kino.example.com"), RequestedTheme = ElementTheme.Dark, FontSize = 14 };
        var name = new TextBox { PlaceholderText = L.T("название — можно не писать", "name — optional"), RequestedTheme = ElementTheme.Dark, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) };
        var error = Text("", 12, Hex("#FFE8A05B"), wrap: true);
        error.Visibility = Visibility.Collapsed;
        _panelContent.Children.Add(address);
        _panelContent.Children.Add(name);
        _panelContent.Children.Add(error);
        void Submit()
        {
            if (WebServices.NormalizeUrl(address.Text) is not { } url)
            {
                error.Text = L.T("Это не похоже на адрес сайта. Пример: kino.example.ru", "That doesn’t look like a website address. Example: kino.example.com");
                error.Visibility = Visibility.Visible;
                return;
            }
            var preset = WebServices.PresetFor(url);
            Added(new WebService
            {
                Url = url, Kind = kind,
                Name = string.IsNullOrWhiteSpace(name.Text) ? WebServices.NameFromUrl(url) : name.Text.Trim(),
                OpenIn = preset?.Protected == true ? WebServices.DefaultBrowser : null,
            });
        }
        address.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; Submit(); } };
        name.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; Submit(); } };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 8, 0, 0) };
        var add = QuietButton(L.T("Добавить", "Add"), 13);
        add.Click += (_, _) => Submit();
        footer.Children.Add(add);
        var back = QuietButton("← " + Display(section), 12);
        back.Click += (_, _) => { _openWord = null; OnWord(section); };
        footer.Children.Add(back);
        _panelContent.Children.Add(footer);
    }

    /// <summary>Строка списка: название слева, пояснение справа, щелчок — действие.</summary>
    private Button LinkRow(string title, string meta, Action click, bool big = false, bool keepOpen = false)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Text(title, big ? 19 : 15, Hex("#E6FFFFFF"), big ? FontWeights.SemiBold : null);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);
        var m = Text(meta, 12, Hex("#73FFFFFF"));
        m.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(m, 1);
        grid.Children.Add(m);
        var b = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, big ? 12 : 8, 12, big ? 12 : 8),
        };
        b.Resources["ButtonBackgroundPointerOver"] = Brush("#1AFFFFFF");
        b.Resources["ButtonBackgroundPressed"] = Brush("#26FFFFFF");
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        b.Click += (_, _) => { if (!keepOpen) ClosePanel(); click(); };
        return b;
    }

    /// <summary>Музыка на паузе — «Продолжить: исполнитель — песня».</summary>
    private async Task FillContinueMusicAsync(StackPanel into)
    {
        var current = await Media();
        if (_openWord != "Продолжить" || current is null || current.Playing) return;
        into.Children.Add(LinkRow("♫  " + current.Text, current.Source.Length > 0 ? current.Source + L.T(" · пауза", " · paused") : L.T("на паузе", "paused"), async () =>
        {
            await MediaControl(MediaCommand.PlayPause);
        }, big: true));
    }

    /// <summary>«Звуки природы»: дождь, море, костёр, ветер — один за раз, и громкость.</summary>
    private StackPanel NatureSection()
    {
        var box = new StackPanel { Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        box.Children.Add(Text(L.T("Звуки природы", "Nature sounds"), 13, Hex("#99FFFFFF"), FontWeights.SemiBold));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var sound in Enum.GetValues<NatureSound>())
        {
            var s = sound;
            chips.Children.Add(NatureChip(NatureSounds.Name(s), _nature.Current == s, () => PlayNature(s)));
        }
        chips.Children.Add(NatureChip(L.T("Тишина", "Silence"), _nature.Current is null, StopNature));
        box.Children.Add(chips);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = _nature.Volume * 100, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        volume.Resources["SliderTrackValueFill"] = Brush("#E8A05B");
        volume.Resources["SliderTrackValueFillPointerOver"] = Brush("#F0B46C");
        volume.Resources["SliderThumbBackground"] = Brush("#F7CA8C");
        volume.ValueChanged += (_, e) => { _nature.Volume = e.NewValue / 100; NatureChanged?.Invoke(_nature.Current ?? _lastNature, _nature.Volume); };
        ToolTipService.SetToolTip(volume, L.T("Громкость звуков природы", "Nature sounds volume"));
        box.Children.Add(volume);
        return box;
    }

    private static Button NatureChip(string text, bool selected, Action click)
    {
        var b = new Button
        {
            Content = Text(text, 12.5, selected ? Hex("#24170C") : Hex("#D0D3DB"), selected ? FontWeights.SemiBold : FontWeights.Normal),
            Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(999),
            Background = selected ? Vertical(Hex("#F7CA8C"), Hex("#E8A05B")) : Brush("#14FFFFFF"),
            BorderBrush = Brush(selected ? "#99784620" : "#1FFFFFFF"), BorderThickness = new Thickness(1),
        };
        b.Resources["ButtonBackgroundPointerOver"] = selected ? Vertical(Hex("#F9D39C"), Hex("#EDAA68")) : Brush("#24FFFFFF");
        b.Resources["ButtonBackgroundPressed"] = Brush("#33FFFFFF");
        b.Click += (_, _) => click();
        return b;
    }

    private async void PlayNature(NatureSound sound)
    {
        _lastNature = sound;
        NatureChanged?.Invoke(sound, _nature.Volume);
        var reopen = _openWord;
        await _nature.PlayAsync(sound);
        if (reopen == "Слушать" && _openWord == "Слушать") { _openWord = null; OnWord("Слушать"); }
    }

    private void StopNature()
    {
        _nature.Stop();
        if (_openWord == "Слушать") { _openWord = null; OnWord("Слушать"); }
    }

    /// <summary>Для скриншотов: открыть раздел нижней строки.</summary>
    public void OpenWordForCapture(string word) { _openWord = null; OnWord(word); }

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
            tb.Foreground = Brush(_openWord == child.Tag as string ? "#FFFFFFFF" : _openWord is null ? "#BFFFFFFF" : "#80FFFFFF");
        }
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        Wake();
        if (_film is not null)
        {
            var session = _film.Value.Player.PlaybackSession;
            switch (e.Key)
            {
                case global::Windows.System.VirtualKey.Escape: CloseFilm(); e.Handled = true; return;
                case global::Windows.System.VirtualKey.Space when e.OriginalSource is not Button:
                    if (session.PlaybackState == global::Windows.Media.Playback.MediaPlaybackState.Playing) _film.Value.Player.Pause(); else _film.Value.Player.Play();
                    e.Handled = true; return;
                case global::Windows.System.VirtualKey.Left: session.Position -= TimeSpan.FromSeconds(10); e.Handled = true; return;
                case global::Windows.System.VirtualKey.Right: session.Position += TimeSpan.FromSeconds(10); e.Handled = true; return;
            }
            return;
        }
        if (WebVisible) return; // клавиши — сайту
        if (e.OriginalSource is TextBox)
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape) { ClosePanel(); e.Handled = true; }
            return; // стрелки — полю ввода
        }
        if (_library is not null)
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape) { CloseLibrary(); e.Handled = true; }
            return; // стрелки — поиску и сетке
        }
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Escape: if (_openWord is not null) { ClosePanel(); e.Handled = true; } break;
            case global::Windows.System.VirtualKey.Left: Navigate(Dir.Left); e.Handled = true; break;
            case global::Windows.System.VirtualKey.Right: Navigate(Dir.Right); e.Handled = true; break;
            case global::Windows.System.VirtualKey.Up: Navigate(Dir.Up); e.Handled = true; break;
            case global::Windows.System.VirtualKey.Down: Navigate(Dir.Down); e.Handled = true; break;
        }
    }

    // ---------- клавиатура: стрелки — по разделам и спискам, Enter — выбрать, Esc — назад ----------

    private enum Dir { Left, Right, Up, Down }

    private void StepWord(int delta)
    {
        var current = _openWord is not null ? Array.IndexOf(WordList, _openWord) : _wordIndex;
        _wordIndex = current < 0 ? (delta >= 0 ? 0 : WordList.Length - 1) : (current + delta + WordList.Length) % WordList.Length;
        _openWord = null;
        OnWord(WordList[_wordIndex]);
        FocusPanel();
    }

    private void FocusPanel()
    {
        if (FocusManager.FindFirstFocusableElement(_panelContent) is Control first) first.Focus(FocusState.Keyboard);
    }

    private bool FocusInPanel() =>
        FocusManager.GetFocusedElement(_root.XamlRoot) is DependencyObject d && IsInside(d, _panel);

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    private void Navigate(Dir direction)
    {
        Wake();
        try
        {
            if (_openWord is null)
            {
                StepWord(direction == Dir.Left ? -1 : direction == Dir.Right ? 1 : 0);
                return;
            }
            if (!FocusInPanel()) { FocusPanel(); return; }
            // Только направленный переход внутри панели (Next/Previous с границей поиска Windows не поддерживает — от этого и были вылеты).
            var options = new FindNextElementOptions { SearchRoot = _panel };
            var moved = FocusManager.TryMoveFocus(direction switch
            {
                Dir.Up => FocusNavigationDirection.Up,
                Dir.Down => FocusNavigationDirection.Down,
                Dir.Left => FocusNavigationDirection.Left,
                _ => FocusNavigationDirection.Right,
            }, options);
            // Влево-вправо дальше некуда — соседний раздел.
            if (!moved && direction is Dir.Left or Dir.Right) StepWord(direction == Dir.Left ? -1 : 1);
        }
        catch (Exception ex)
        {
            GigaRest.Core.Logging.Log.Warn("Клавиатура на «Тихом» экране: " + ex.Message);
        }
    }

    // ---------- курсор и выключение экрана ----------

    private void SetCursorHidden(bool hide)
    {
        if (hide == _cursorHidden) return;
        _cursorHidden = hide;
        ShowCursor(!hide);
    }

    private void TurnScreenOff()
    {
        _screenOff = true;
        _offCard.Visibility = Visibility.Collapsed;
        // Выключить монитор (как по таймеру Windows); любое движение мыши или клавиша включат его.
        SendMessageW(RestScreens.Hwnd(this), 0x0112 /* WM_SYSCOMMAND */, 0xF170 /* SC_MONITORPOWER */, 2);
    }

    [DllImport("user32.dll")] private static extern int ShowCursor(bool show);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>Сколько компьютер без ввода вообще — мышь и клавиатура на любом мониторе.</summary>
    private static TimeSpan SystemIdle()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    private void Wake()
    {
        _lastInput = DateTime.Now;
        _words.Opacity = 1;
        _dim.Opacity = 0;
        _offCard.Visibility = Visibility.Collapsed;
        _screenOff = false;
        SetCursorHidden(false);
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        _clock.Text = now.ToString("HH:mm", L.Culture);
        _date.Text = now.ToString(L.En ? "dddd, MMMM d" : "dddd, d MMMM", L.Culture);
        // Простой «Тихого» экрана — по вводу в нём; затемнение и выключение — по вводу вообще
        // (работаете на втором мониторе — экран не гаснет). Смотрят фильм или сайт — не гасим.
        var watching = _film is not null || WebVisible || _paused || _library is not null;
        if (WebVisible || _paused || _library is not null) _lastInput = now;
        var own = Timers.At(now - _lastInput);
        SetCursorHidden(own.HideCursor && !WebVisible);
        if (own.CloseList && _film is null)
        {
            if (_openWord is not null) ClosePanel();
            _words.Opacity = 0.4;
        }
        var system = Timers.At(watching ? TimeSpan.Zero : Min(now - _lastInput, SystemIdle()));
        _dim.Opacity = system.DimScene ? 0.9 : 0;
        if (system.ScreenOffInSeconds is int left && !_screenOff)
        {
            _offText.Text = L.T($"Экран выключится через {Sizes.Plural(left, "секунду", "секунды", "секунд")}", $"The screen will turn off in {L.Plural(left, "секунду", "секунды", "секунд", "second", "seconds")}");
            _offCard.Visibility = Visibility.Visible;
        }
        else _offCard.Visibility = Visibility.Collapsed;
        if (system.ScreenOffNow && !_screenOff) TurnScreenOff();

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
        var busy = _monitor is not null && UserState.CoversMonitor(_monitor);
        if (busy && !_paused) { _kenBurns.Pause(); _effects.Pause(true); _video?.Pause(); _paused = true; }
        else if (!busy && _paused) { _kenBurns.Resume(); _effects.Pause(false); _video?.Resume(); _paused = false; }
    }

    // ---------- библиотека игр: все установленные из всех магазинов, на весь «Тихий» экран ----------

    private Grid? _library;
    private string? _libraryStore;
    private string _libraryQuery = "";
    private bool _libraryByName;

    /// <summary>
    /// «Вся библиотека»: сверху — последняя игра крупно, ниже — поиск, магазины, порядок и сетка обложек.
    /// Только установленные игры. Esc или «Закрыть» — обратно к сцене.
    /// </summary>
    public void ShowLibrary()
    {
        ClosePanel();
        Wake();
        if (_library is not null) _root.Children.Remove(_library);
        var games = _games ?? [];
        var host = new Grid
        {
            Margin = new Thickness(56, 40, 56, 40), CornerRadius = new CornerRadius(20), Padding = new Thickness(28, 22, 28, 0),
            Background = Vertical(Hex("#F2191B22"), Hex("#F70F1116")), BorderBrush = Brush("#1FFFFFFF"), BorderThickness = new Thickness(1),
            RowSpacing = 16,
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // заголовок
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // последняя игра
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // фильтры
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // сетка

        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(L.T("Библиотека", "Library"), 26, Hex("#F6F2EA"), FontWeights.SemiBold);
        head.Children.Add(title);
        var count = Text(L.Plural(games.Count, "игра установлена", "игры установлено", "игр установлено", "game installed", "games installed"), 13, Hex("#8A909C"));
        count.VerticalAlignment = VerticalAlignment.Bottom;
        count.Margin = new Thickness(0, 0, 0, 5);
        Grid.SetColumn(count, 1);
        head.Children.Add(count);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        var add = QuietButton(L.T("+ Добавить игру…", "+ Add a game…"), 13);
        ToolTipService.SetToolTip(add, L.T("Игра без магазина: выберите её файл запуска (.exe)", "A game from outside a store: choose its launch file (.exe)"));
        add.Click += (_, _) => AddGameRequested?.Invoke();
        actions.Children.Add(add);
        var covers = QuietButton(L.T("Папка обложек", "Covers folder"), 13);
        ToolTipService.SetToolTip(covers, L.T("Положите картинку с названием игры («Diablo IV.jpg») — она станет обложкой. «Diablo IV шапка.jpg» — широкая картинка для «Продолжить».", "Drop in a picture named after the game (“Diablo IV.jpg”) and it becomes the cover. “Diablo IV шапка.jpg” is the wide picture for Continue."));
        covers.Click += (_, _) => OpenCoversRequested?.Invoke();
        actions.Children.Add(covers);
        var close = QuietButton(L.T("Закрыть · Esc", "Close · Esc"), 13);
        close.Click += (_, _) => CloseLibrary();
        actions.Children.Add(close);
        Grid.SetColumn(actions, 2);
        head.Children.Add(actions);
        host.Children.Add(head);

        // Последняя игра — широкой картинкой (у Steam — своя шапка игры).
        if (games.FirstOrDefault(g => g.LastPlayedUtc is not null) is { } last)
        {
            var hero = HeroCard(last);
            Grid.SetRow(hero, 1);
            host.Children.Add(hero);
        }

        var filters = new Grid { ColumnSpacing = 10 };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new TextBox { PlaceholderText = L.T("Найти игру…", "Find a game…"), Text = _libraryQuery, FontSize = 14 };
        filters.Children.Add(search);
        var stores = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stores, 1);
        filters.Children.Add(stores);
        var order = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(order, 2);
        filters.Children.Add(order);
        Grid.SetRow(filters, 2);
        host.Children.Add(filters);

        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 172, ItemHeight = 300 };
        var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 24) };
        Grid.SetRow(scroll, 3);
        host.Children.Add(scroll);

        void Fill()
        {
            grid.Children.Clear();
            var shown = GameLibraries.Filter(games, _libraryStore, _libraryQuery, _libraryByName);
            foreach (var game in shown) grid.Children.Add(LibraryCard(game));
            if (shown.Count == 0)
                grid.Children.Add(Text(games.Count == 0 ? L.T("Игр не нашлось. GigaRest смотрит в Steam, Epic, GOG и Battle.net; игру без магазина — «+ Добавить игру…».", "No games found. GigaRest looks in Steam, Epic, GOG and Battle.net; for a game from outside a store, use “+ Add a game…”.") : L.T("Ничего не нашлось.", "Nothing found."), 14, Hex("#8A909C")));
            // Фильтры — подсвечиваем выбранное.
            stores.Children.Clear();
            stores.Children.Add(Chip(L.T($"Все · {games.Count}", $"All · {games.Count}"), _libraryStore is null, () => { _libraryStore = null; Fill(); }));
            foreach (var g in games.GroupBy(g => g.Store).OrderByDescending(g => g.Count()))
            {
                var store = g.Key;
                stores.Children.Add(Chip($"{StoreName(store)} · {g.Count()}", _libraryStore == store, () => { _libraryStore = store; Fill(); }));
            }
            order.Children.Clear();
            order.Children.Add(Chip(L.T("Недавние", "Recent"), !_libraryByName, () => { _libraryByName = false; Fill(); }));
            order.Children.Add(Chip(L.T("А–Я", "A–Z"), _libraryByName, () => { _libraryByName = true; Fill(); }));
        }
        search.TextChanged += (_, _) => { _libraryQuery = search.Text; Fill(); };
        Fill();

        _library = host;
        _root.Children.Add(host);
        _overlay.Visibility = Visibility.Collapsed;
        search.Focus(FocusState.Programmatic);
    }

    /// <summary>Для скриншотов.</summary>
    public void CloseLibraryForCapture() => CloseLibrary();

    private void CloseLibrary()
    {
        if (_library is null) return;
        _root.Children.Remove(_library);
        _library = null;
        if (!WebVisible) _overlay.Visibility = Visibility.Visible;
        Wake();
    }

    /// <summary>Кнопка-«таблетка» фильтра.</summary>
    private static Button Chip(string text, bool selected, Action click)
    {
        var b = new Button
        {
            Content = Text(text, 12.5, selected ? Hex("#24170C") : Hex("#C9CDD6"), selected ? FontWeights.SemiBold : FontWeights.Normal),
            Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(999),
            Background = selected ? Vertical(Hex("#F7CA8C"), Hex("#E8A05B")) : Brush("#14FFFFFF"),
            BorderBrush = Brush(selected ? "#99784620" : "#1FFFFFFF"), BorderThickness = new Thickness(1),
        };
        b.Resources["ButtonBackgroundPointerOver"] = selected ? Vertical(Hex("#F9D39C"), Hex("#EDAA68")) : Brush("#24FFFFFF");
        b.Resources["ButtonBackgroundPressed"] = selected ? Vertical(Hex("#EDB878"), Hex("#DC944F")) : Brush("#33FFFFFF");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Карточка в сетке: обложка, название, магазин и «когда играли». Щелчок — играть.</summary>
    private Button LibraryCard(Game game)
    {
        var box = new StackPanel { Spacing = 6, Width = 156 };
        box.Children.Add(Cover(game, 156, 234));
        var name = Text(game.Name, 13, Hex("#E6FFFFFF"), FontWeights.SemiBold, wrap: true);
        name.MaxLines = 2;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        box.Children.Add(name);
        box.Children.Add(Text(GameMeta(game), 11.5, Hex("#80FFFFFF")));
        if (game.PlayMinutes is { } minutes) box.Children.Add(Text(GameLibraries.PlaytimeText(minutes), 11.5, Hex("#66FFFFFF")));
        var b = new Button
        {
            Content = box, Padding = new Thickness(6), CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top,
            Background = Brush(Colors.Transparent), BorderThickness = new Thickness(0),
        };
        b.Resources["ButtonBackgroundPointerOver"] = Brush("#14FFFFFF");
        b.Resources["ButtonBackgroundPressed"] = Brush("#24FFFFFF");
        b.Click += (_, _) => { CloseLibrary(); PlayRequested?.Invoke(game); };
        if (game.Store == "Свои")
        {
            var menu = new MenuFlyout();
            var remove = new MenuFlyoutItem { Text = L.T("Убрать из библиотеки", "Remove from library") };
            remove.Click += (_, _) => RemoveGameRequested?.Invoke(game);
            menu.Items.Add(remove);
            b.ContextFlyout = menu;
            ToolTipService.SetToolTip(b, L.T("Правый щелчок — убрать из библиотеки", "Right-click to remove from library"));
        }
        return b;
    }

    /// <summary>Последняя игра — широкой картинкой с кнопкой «Играть».</summary>
    private Border HeroCard(Game game)
    {
        var grid = new Grid { Height = 190 };
        if (game.HeroPath is { } hero && File.Exists(hero))
            grid.Children.Add(new Image { Source = new BitmapImage(new Uri(hero)) { DecodePixelWidth = 1920 }, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center });
        else
            grid.Children.Add(new Border { Background = Vertical(ProgramColorDim(game.Name), Hex("#FF14151C")) });
        var shade = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        shade.GradientStops.Add(new GradientStop { Color = Hex("#E6101217"), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Hex("#00101217"), Offset = 0.75 });
        grid.Children.Add(new Border { Background = shade });
        var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
        text.Children.Add(Text(Display("Продолжить"), 12, Hex("#B3FFFFFF"), FontWeights.SemiBold));
        text.Children.Add(Text(game.Name, 28, Hex("#F6F2EA"), FontWeights.SemiBold));
        text.Children.Add(Text(GameMeta(game), 13, Hex("#99FFFFFF")));
        var play = WarmButton("▶  " + Display("Играть"), 15);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        play.Margin = new Thickness(0, 6, 0, 0);
        play.Click += (_, _) => { CloseLibrary(); PlayRequested?.Invoke(game); };
        text.Children.Add(play);
        grid.Children.Add(text);
        return new Border { CornerRadius = new CornerRadius(16), Child = grid, BorderBrush = Brush("#1FFFFFFF"), BorderThickness = new Thickness(1) };
    }

    private static Color ProgramColorDim(string name)
    {
        var c = ProgramColor(name);
        return Color.FromArgb(255, (byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3));
    }

    // ---------- сайты внутри «Тихого» экрана (Яндекс Музыка, YouTube): WebView2, свой профиль — вход запоминается ----------

    private const string YouTube = "youtube";
    private const string YouTubeUrl = "https://www.youtube.com/";
    private const string YouTubeHistoryUrl = "https://www.youtube.com/feed/history";
    /// <summary>Встроенный сайт: панель, сам браузер, кнопка «Свернуть/Развернуть», состояние.</summary>
    private sealed class WebPanel
    {
        public required Grid Host { get; init; }
        public required WebView2 Web { get; init; }
        public required TextBlock Toggle { get; init; }
        public required Grid Bar { get; init; }
        public bool Mini { get; set; }
        public bool Full { get; set; }
    }

    private readonly Dictionary<string, WebPanel> _web = [];

    /// <summary>Папка профиля встроенного браузера: вход в Яндекс и YouTube, настройки сайтов. Удаляется вместе с программой.</summary>
    public static string WebProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GigaRest", "WebView2");

    /// <summary>Есть развёрнутая панель — её смотрят, экран не гасим и слова не показываем.</summary>
    private bool WebVisible => _web.Values.Any(w => !w.Mini);

    private static string WebKey(WebService s) => WebServices.IsYouTube(s) ? YouTube : s.Url;

    /// <summary>Открыть сервис: здесь (панелью) или в браузере — как выбрано у сервиса.</summary>
    private void OpenService(WebService s, bool lastWatched = false)
    {
        if (s.OpenIn is not null)
        {
            ClosePanel();
            OpenInBrowserRequested?.Invoke(s, WebServices.IsYouTube(s) && lastWatched ? YouTubeHistoryUrl : s.Url);
            return;
        }
        if (WebServices.IsYouTube(s)) ShowYouTube(lastWatched);
        else ShowWeb(WebKey(s), s.Name, s.Url);
    }

    private void ShowYouTube(bool lastWatched)
    {
        // Уже открыт и просто свёрнут — разворачиваем, страницу не трогаем (ролик продолжается).
        if (!lastWatched && _web.ContainsKey(YouTube)) { Expand(YouTube); return; }
        ShowWeb(YouTube, "YouTube", lastWatched ? YouTubeHistoryUrl : YouTubeUrl, navigate: true, openLastWatched: lastWatched);
    }

    /// <summary>
    /// Открыть сайт панелью поверх сцены. Уже открыт — развернуть (navigate — перейти на адрес).
    /// openLastWatched — на странице истории YouTube сразу открыть верхний (последний просмотренный) ролик.
    /// </summary>
    private async void ShowWeb(string key, string titleText, string url, bool navigate = false, bool openLastWatched = false)
    {
        Wake();
        ClosePanel();
        if (_web.TryGetValue(key, out var open))
        {
            Expand(key);
            if (navigate && open.Web.CoreWebView2 is { } core)
            {
                _openLastWatched = openLastWatched;
                core.Navigate(url);
            }
            return;
        }
        var web = new WebView2();
        var host = new Grid
        {
            CornerRadius = new CornerRadius(20), Background = Brush("#FF0C0D12"),
            BorderBrush = Brush("#1AFFFFFF"), BorderThickness = new Thickness(1),
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid { Padding = new Thickness(20, 8, 14, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(titleText, 14, Hex("#E6FFFFFF"), FontWeights.Medium);
        title.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(title);
        var toggle = QuietButton(L.T("Свернуть", "Minimize"), 13);
        Grid.SetColumn(toggle, 1);
        bar.Children.Add(toggle);
        var close = QuietButton(L.T("Закрыть", "Close"), 13);
        close.Margin = new Thickness(14, 0, 0, 0);
        close.Click += (_, _) => CloseWeb(key);
        Grid.SetColumn(close, 2);
        bar.Children.Add(close);
        host.Children.Add(bar);
        Grid.SetRow(web, 1);
        host.Children.Add(web);

        var panel = new WebPanel { Host = host, Web = web, Toggle = (TextBlock)toggle.Content, Bar = bar };
        toggle.Click += (_, _) => { if (panel.Mini) Expand(key); else Minimize(key); };
        _web[key] = panel;
        _root.Children.Add(host);
        Expand(key);
        try
        {
            Directory.CreateDirectory(WebProfileFolder);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, WebProfileFolder, new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
            await web.EnsureCoreWebView2Async(env);
            // Окна входа и ссылки «в новой вкладке» — здесь же, без отдельного браузера.
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; web.CoreWebView2.Navigate(e.Uri); };
            // Кнопка «во весь экран» в самом ролике — панель на весь монитор, без рамки и полосы.
            web.CoreWebView2.ContainsFullScreenElementChanged += (_, _) => SetFull(panel, web.CoreWebView2.ContainsFullScreenElement);
            if (key == YouTube)
            {
                web.CoreWebView2.NavigationCompleted += async (_, _) => await OpenLastWatchedAsync(web);
                // Открыли ролик — запомнить адрес и название для «Продолжить».
                web.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    var src = web.CoreWebView2.Source;
                    var title = web.CoreWebView2.DocumentTitle;
                    if (!src.Contains("/watch?v=", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(title) || title == "YouTube") return;
                    title = title.EndsWith(" - YouTube", StringComparison.Ordinal) ? title[..^10] : title;
                    _lastYouTube = (src, title, DateTime.UtcNow);
                    YouTubeWatched?.Invoke(src, title);
                };
            }
            _openLastWatched = openLastWatched;
            web.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            GigaRest.Core.Logging.Log.Error($"Встроенный сайт {titleText}", ex);
            CloseWeb(key);
            ShowSummary(L.T($"{titleText} не открылся: в Windows нет компонента Microsoft Edge WebView2. Его можно поставить с сайта Microsoft.", $"{titleText} didn’t open: Windows is missing the Microsoft Edge WebView2 component. You can install it from the Microsoft website."), hasProblems: true);
        }
    }

    /// <summary>Развернуть панель на большую часть экрана; остальные — свернуть в уголок.</summary>
    private void Expand(string key)
    {
        foreach (var (k, w) in _web) if (k != key && !w.Mini) SetMini(w, true);
        if (_web.TryGetValue(key, out var panel)) SetMini(panel, false);
        Layout();
        Wake();
    }

    private void Minimize(string key)
    {
        if (!_web.TryGetValue(key, out var panel)) return;
        if (panel.Full) panel.Web.CoreWebView2?.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
        SetMini(panel, true);
        Layout();
        Wake();
    }

    private static void SetMini(WebPanel panel, bool mini)
    {
        panel.Mini = mini;
        panel.Toggle.Text = mini ? L.T("Развернуть", "Expand") : L.T("Свернуть", "Minimize");
    }

    private void SetFull(WebPanel panel, bool full)
    {
        panel.Full = full;
        if (full) SetMini(panel, false);
        Layout();
    }

    /// <summary>
    /// Где стоят панели: развёрнутая — почти на весь экран (или ровно на весь, если ролик во весь экран),
    /// свёрнутые — маленькими окошками в правом нижнем углу, одно над другим: видно, что играет, и можно развернуть.
    /// </summary>
    private void Layout()
    {
        var miniIndex = 0;
        foreach (var w in _web.Values)
        {
            if (w.Mini)
            {
                w.Host.Width = 400;
                w.Host.Height = 260;
                w.Host.HorizontalAlignment = HorizontalAlignment.Right;
                w.Host.VerticalAlignment = VerticalAlignment.Bottom;
                w.Host.Margin = new Thickness(0, 0, 40, 40 + miniIndex * 276);
                w.Host.CornerRadius = new CornerRadius(14);
                w.Bar.Visibility = Visibility.Visible;
                miniIndex++;
            }
            else
            {
                w.Host.Width = double.NaN;
                w.Host.Height = double.NaN;
                w.Host.HorizontalAlignment = HorizontalAlignment.Stretch;
                w.Host.VerticalAlignment = VerticalAlignment.Stretch;
                w.Host.Margin = w.Full ? new Thickness(0) : new Thickness(56, 40, 56, 40);
                w.Host.CornerRadius = new CornerRadius(w.Full ? 0 : 20);
                w.Host.BorderThickness = new Thickness(w.Full ? 0 : 1);
                w.Bar.Visibility = w.Full ? Visibility.Collapsed : Visibility.Visible;
                // Развёрнутая — поверх свёрнутых.
                _root.Children.Remove(w.Host);
                _root.Children.Add(w.Host);
            }
        }
        _overlay.Visibility = WebVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _openLastWatched;

    // На странице истории верхний ролик — последний просмотренный: открываем его (ждём, пока список загрузится, до 10 с).
    private async Task OpenLastWatchedAsync(WebView2 web)
    {
        if (!_openLastWatched || web.CoreWebView2 is not { } core || !core.Source.Contains("/feed/history", StringComparison.Ordinal)) return;
        _openLastWatched = false;
        const string script = """
            (function () {
              let tries = 0;
              const timer = setInterval(() => {
                const link = document.querySelector('a#thumbnail[href*="/watch"], a[href*="/watch?v="]');
                if (link) { clearInterval(timer); location.href = link.href; }
                else if (++tries > 40) clearInterval(timer);
              }, 250);
            })();
            """;
        try { await core.ExecuteScriptAsync(script); }
        catch (Exception ex) { GigaRest.Core.Logging.Log.Warn("YouTube, последний ролик: " + ex.Message); }
    }

    private void CloseWeb(string key)
    {
        if (!_web.Remove(key, out var w)) return;
        try { w.Web.Close(); } catch (Exception ex) { GigaRest.Core.Logging.Log.Warn("Встроенный сайт, закрытие: " + ex.Message); }
        _root.Children.Remove(w.Host);
        Layout();
        Wake();
    }

    // ---------- встроенный плеер: системный MediaPlayer с аппаратным декодированием, на весь «Тихий» экран ----------

    private (Film Film, global::Windows.Media.Playback.MediaPlayer Player, Grid View)? _film;

    /// <summary>Открыть фильм прямо здесь. start — с какой секунды (продолжить просмотр).</summary>
    public void PlayFilm(Film film, double start)
    {
        if (_film is not null) CloseFilm();
        ClosePanel();
        var player = new global::Windows.Media.Playback.MediaPlayer { AutoPlay = true };
        var element = new MediaPlayerElement { AreTransportControlsEnabled = true, Stretch = Stretch.Uniform };
        element.SetMediaPlayer(player);
        element.TransportControls.IsZoomButtonVisible = false;
        element.TransportControls.IsSeekBarVisible = true;
        element.TransportControls.IsSeekEnabled = true;

        var view = new Grid { Background = Brush("#FF000000") };
        view.Children.Add(element);
        var bar = new Grid { VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(28, 18, 28, 18), Background = Brush("#80000000") };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text(film.Name, 15, Hex("#E6FFFFFF"));
        title.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(title);
        var close = QuietButton(L.T("Закрыть · Esc", "Close · Esc"), 13);
        close.Click += (_, _) => CloseFilm();
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);
        view.Children.Add(bar);
        // Полоса с названием прячется, когда мышь не двигается.
        var lastMove = DateTime.Now;
        view.PointerMoved += (_, _) => { lastMove = DateTime.Now; bar.Opacity = 1; };
        var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        hide.Tick += (_, _) => { if (_film?.View != view) { hide.Stop(); return; } bar.Opacity = DateTime.Now - lastMove > TimeSpan.FromSeconds(3) ? 0 : 1; };
        hide.Start();

        player.MediaOpened += (s, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (start > 0) s.PlaybackSession.Position = TimeSpan.FromSeconds(start);
        });
        player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
        {
            GigaRest.Core.Logging.Log.Warn($"Встроенный плеер: {args.Error} {args.ErrorMessage}");
            if (_film?.Film != film) return;
            CloseFilm(notify: false);
            FilmFailed?.Invoke(film);
        });
        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (_film?.Film == film) CloseFilm(); });
        player.Source = global::Windows.Media.Core.MediaSource.CreateFromUri(new Uri(film.Path));

        _film = (film, player, view);
        _root.Children.Add(view);
        _overlay.Visibility = Visibility.Collapsed;
        // Сцена за фильмом не видна — останавливаем её, видеокарта занята фильмом.
        _kenBurns.Pause(); _effects.Pause(true); _video?.Pause();
        element.Focus(FocusState.Programmatic);
    }

    private void CloseFilm(bool notify = true)
    {
        if (_film is not { } f) return;
        _film = null;
        var position = f.Player.PlaybackSession.Position.TotalSeconds;
        var duration = f.Player.PlaybackSession.NaturalDuration.TotalSeconds;
        f.Player.Pause();
        if (f.View.Children.FirstOrDefault() is MediaPlayerElement e) e.SetMediaPlayer(null);
        f.Player.Dispose();
        _root.Children.Remove(f.View);
        _overlay.Visibility = Visibility.Visible;
        if (!_paused) { _kenBurns.Resume(); _effects.Pause(false); _video?.Resume(); }
        Wake();
        if (notify) FilmClosed?.Invoke(f.Film, position, duration);
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

    /// <summary>Для скриншотов: показать сцену с эффектом.</summary>
    public void ShowSceneForCapture(string sceneId, SceneEffect effect)
    {
        var i = _scenes.ToList().FindIndex(x => x.Id == sceneId);
        SetScene(Math.Max(0, i), effect);
        HideSummary();
    }

    /// <summary>Для скриншотов: запустить часы и анимацию без перехода на весь экран.</summary>
    public void StartForCapture()
    {
        _tick.Start();
        OnTick();
    }

    private MonitorInfo? _monitor;

    public void ShowOn(MonitorInfo? monitor)
    {
        _monitor = monitor;
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
        _nature.Dispose();
        SetCursorHidden(false);
        CloseFilm(); // где остановились — запомнится
        foreach (var key in _web.Keys.ToList()) CloseWeb(key);
        CloseLibrary();
        _closing = true;
        _tick.Stop();
        _kenBurns.Stop();
        _effects.Pause(true);
        _video?.Dispose();
        Close();
    }
}
