using System.Globalization;
using System.Runtime.InteropServices;
using GigaRest.Core.Desktop;
using GigaRest.Core.Games;
using GigaRest.Core.Media;
using GigaRest.Core.Scenes;
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
        ((TextBlock)_dnd.Content).Text = on == false ? "включить «Не беспокоить»" : "не беспокоить";
        _dnd.Opacity = on == false ? 1 : 0.6;
    }

    /// <summary>Щёлкнули по игре в «Играть» или «Продолжить».</summary>
    public event Action<Game>? PlayRequested;

    // null — библиотеки ещё читаются.
    private IReadOnlyList<Game>? _games;
    private IReadOnlyList<Film>? _films;
    private Film? _lastFilm;
    private IReadOnlyDictionary<string, double> _positions = new Dictionary<string, double>();
    private string _filmsFolderName = "Видео";
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

    public void SetPlayers(IReadOnlyList<AppChoice> players, string? current)
    {
        _players = players;
        _player = current;
        Refresh("Смотреть", "Плеер");
    }

    private void Refresh(params string[] words)
    {
        if (_openWord is { } w && words.Contains(w)) { _openWord = null; OnWord(w); }
    }

    public event Action<Film>? FilmRequested;
    public event Action? WatchLaterRequested;
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
    public void SetGames(IReadOnlyList<Game> games)
    {
        _games = games;
        if (_openWord is "Играть" or "Продолжить") { var w = _openWord; _openWord = null; OnWord(w); }
    }
    public FrameworkElement Root => _root;

    public QuietWindow(IReadOnlyList<Scene> scenes, string? sceneId = null, SceneEffect? effect = null)
    {
        _scenes = scenes;
        Title = "GigaRest — отдых";
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
        _dnd = QuietButton("не беспокоить", 13);
        _dnd.Opacity = 0.6;
        _dnd.Visibility = Visibility.Collapsed;
        _dnd.Click += (_, _) => DndClicked?.Invoke();
        top.Children.Add(_dnd);
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
        _sceneName.Visibility = Visibility.Collapsed; // название сцены видно только в списке «Сцены»
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
                    ? new Grid { Background = Brush("#33FFFFFF"), Children = { new TextBlock { Text = "▶ видео", Foreground = Brush("#CCFFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } }
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
            var over = Text("Поверх", 13, Hex("#80FFFFFF"));
            over.VerticalAlignment = VerticalAlignment.Center;
            over.Margin = new Thickness(0, 0, 8, 0);
            effects.Children.Add(over);
            foreach (var (fx, label) in new[] { (SceneEffect.None, "Ничего"), (SceneEffect.Snow, "Снег"), (SceneEffect.Fog, "Туман"), (SceneEffect.Lights, "Огоньки"), (SceneEffect.Rain, "Дождь") })
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

            var folder = QuietButton("Свои картинки и видео — папка «Мои сцены» (открыть)", 12);
            folder.Margin = new Thickness(0, 8, 0, 0);
            folder.Click += (_, _) => OpenMyScenesRequested?.Invoke();
            _panelContent.Children.Add(folder);
        }
        else if (word == "Играть")
        {
            _panel.Width = 520;
            _panelContent.Children.Add(Text("Играть", 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            if (_games is null || _games.Count == 0)
            {
                var note = Text(_games is null ? "Ищем игры…" : "Игр не нашлось. GigaRest смотрит в Steam, Epic, GOG и Battle.net.", 13, Hex("#99FFFFFF"), wrap: true);
                note.Margin = new Thickness(0, 6, 0, 4);
                _panelContent.Children.Add(note);
            }
            else
            {
                var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
                foreach (var game in _games) list.Children.Add(GameRow(game));
                _panelContent.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                var count = Text(GigaRest.Core.Text.Sizes.Plural(_games.Count, "игра", "игры", "игр") + " · Steam, Epic, GOG, Battle.net", 12, Hex("#66FFFFFF"));
                count.Margin = new Thickness(0, 8, 0, 0);
                _panelContent.Children.Add(count);
            }
        }
        else if (word == "Продолжить" && (_games?.FirstOrDefault(g => g.LastPlayedUtc is not null) is not null || _lastFilm is not null))
        {
            _panel.Width = 520;
            _panelContent.Children.Add(Text("Продолжить", 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            if (_games?.FirstOrDefault(g => g.LastPlayedUtc is not null) is { } last) rows.Children.Add(GameRow(last, big: true));
            if (_lastFilm is { } film) rows.Children.Add(FilmRow(film, big: true));
            _panelContent.Children.Add(rows);
        }
        else if (word == "Слушать")
        {
            _panel.Width = 520;
            _panelContent.Children.Add(Text("Слушать", 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var box = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            box.Children.Add(Text("…", 13, Hex("#99FFFFFF")));
            _panelContent.Children.Add(box);
            _ = FillListenAsync(box);
        }
        else if (word == "Смотреть")
        {
            _panel.Width = 560;
            _panelContent.Children.Add(Text("Смотреть", 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            list.Children.Add(LinkRow("YouTube · Смотреть позже", "в браузере", () => WatchLaterRequested?.Invoke()));
            if (_films is { Count: > 0 })
                foreach (var film in _films.Take(30)) list.Children.Add(FilmRow(film));
            _panelContent.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            if (_films is { Count: 0 })
            {
                var empty = Text($"В папке «{_filmsFolderName}» видео нет. Выберите папку, где лежат фильмы.", 13, Hex("#99FFFFFF"), wrap: true);
                empty.Margin = new Thickness(0, 6, 0, 0);
                _panelContent.Children.Add(empty);
            }
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 6, 0, 0) };
            var folder = QuietButton(_films is null ? "Ищем фильмы…" : $"Папка: {_filmsFolderName} · выбрать", 12);
            folder.Click += (_, _) => FilmsFolderPickRequested?.Invoke();
            footer.Children.Add(folder);
            var open = QuietButton("открыть", 12);
            open.Click += (_, _) => OpenFilmsFolderRequested?.Invoke();
            footer.Children.Add(open);
            var player = QuietButton($"Плеер: {PlayerName} · сменить", 12);
            player.Click += (_, _) => { _openWord = null; OnWord("Плеер"); };
            footer.Children.Add(player);
            _panelContent.Children.Add(footer);
        }
        else if (word == "Плеер")
        {
            _panel.Width = 560;
            _panelContent.Children.Add(Text("Чем открывать фильмы", 17, Hex("#E6FFFFFF"), FontWeights.Medium));
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(-12, 6, -12, 0) };
            list.Children.Add(ChoiceRow("Встроенный плеер GigaRest", "фильм открывается прямо здесь", _player is null, () => PlayerChosen?.Invoke(null)));
            foreach (var p in _players)
                list.Children.Add(ChoiceRow(p.Name, "ваш плеер, на весь экран", string.Equals(_player, p.Path, StringComparison.OrdinalIgnoreCase), () => PlayerChosen?.Invoke(p.Path)));
            if (_player is not null && !_players.Any(p => string.Equals(p.Path, _player, StringComparison.OrdinalIgnoreCase)))
                list.Children.Add(ChoiceRow(Apps.NameOf(_player), "выбран вами", true, () => { }));
            list.Children.Add(ChoiceRow("Другой плеер…", "выбрать программу (.exe)", false, () => PlayerPickRequested?.Invoke()));
            _panelContent.Children.Add(list);
            var back = QuietButton("← Смотреть", 12);
            back.Margin = new Thickness(0, 6, 0, 0);
            back.Click += (_, _) => { _openWord = null; OnWord("Смотреть"); };
            _panelContent.Children.Add(back);
        }
        else
        {
            _panel.Width = 460;
            var (title, text) = word switch
            {
                "Продолжить" => ("Продолжить", _games is null ? "Ищем последнюю игру…" : "Здесь будут последняя игра и последний фильм — как только вы запустите их отсюда, из «Играть» или «Смотреть»."),
                _ => (word, ""),
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
        var when = GameLibraries.LastPlayedText(game.LastPlayedUtc, DateTime.Now);
        return LinkRow(big ? $"▶  {game.Name}" : game.Name, when.Length > 0 ? $"{game.Store} · {when}" : game.Store, () => PlayRequested?.Invoke(game), big);
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
                var start = LinkRow($"▶  Открыть {app.Name}", "", () => MusicAppRequested?.Invoke(app), big: true);
                start.Margin = new Thickness(-12, 0, -12, 0);
                box.Children.Add(start);
                box.Children.Add(Text("Включите музыку — здесь появятся кнопки «назад», «пауза», «дальше».", 12, Hex("#73FFFFFF"), wrap: true));
            }
            else
            {
                box.Children.Add(Text("Сейчас ничего не играет, а программу для музыки GigaRest не нашёл.", 13, Hex("#99FFFFFF"), wrap: true));
            }
            box.Children.Add(MusicAppLink());
            return;
        }
        var track = Text(current.Text, 19, Hex("#E6FFFFFF"), FontWeights.SemiBold, wrap: true);
        box.Children.Add(track);
        if (current.Source.Length > 0) box.Children.Add(Text(current.Source + (current.Playing ? " · играет" : " · пауза"), 12, Hex("#73FFFFFF")));
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
        var link = QuietButton(_musicApp is { } app ? $"Программа: {app.Name} · сменить" : "Выбрать программу для музыки…", 12);
        link.Click += (_, _) => MusicAppPickRequested?.Invoke();
        return link;
    }

    private string PlayerName => _player is null ? "встроенный" : _players.FirstOrDefault(p => string.Equals(p.Path, _player, StringComparison.OrdinalIgnoreCase))?.Name ?? Apps.NameOf(_player);

    private Button ChoiceRow(string title, string meta, bool selected, Action click) =>
        LinkRow((selected ? "✓  " : "     ") + title, meta, () => { click(); }, keepOpen: true);

    private Button FilmRow(Film film, bool big = false)
    {
        var added = GameLibraries.LastPlayedText(film.ModifiedUtc, DateTime.Now);
        var meta = _positions.TryGetValue(film.Path, out var pos) ? "с " + Apps.Position(pos)
            : big ? "фильм" : added.Length > 0 ? "добавлен " + added : "";
        return LinkRow(big ? $"▶  {film.Name}" : film.Name, meta, () => FilmRequested?.Invoke(film), big);
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
            tb.Foreground = Brush(_openWord == tb.Text ? "#FFFFFFFF" : _openWord is null ? "#BFFFFFFF" : "#80FFFFFF");
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
        if (_film is not null) _lastInput = now; // фильм смотрят — не гасим экран
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
        var busy = _monitor is not null && UserState.CoversMonitor(_monitor);
        if (busy && !_paused) { _kenBurns.Pause(); _effects.Pause(true); _video?.Pause(); _paused = true; }
        else if (!busy && _paused) { _kenBurns.Resume(); _effects.Pause(false); _video?.Resume(); _paused = false; }
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
        var close = QuietButton("Закрыть · Esc", 13);
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
        CloseFilm(); // где остановились — запомнится
        _closing = true;
        _tick.Stop();
        _kenBurns.Stop();
        _effects.Pause(true);
        _video?.Dispose();
        Close();
    }
}
