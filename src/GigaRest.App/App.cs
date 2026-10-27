using GigaRest.Core;
using GigaRest.Core.Desktop;
using GigaRest.Core.Input;
using GigaRest.Core.Logging;
using GigaRest.Core.Media;
using GigaRest.Core.Programs;
using GigaRest.Core.Scenes;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;
using GigaRest.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace GigaRest.App;

/// <summary>
/// Программа в трее: значок, горячая клавиша, окно «Работа → Отдых». Состояние «отдых» — это session.json.
/// </summary>
public sealed partial class App : Application, IXamlMetadataProvider
{
    private const int HotkeyToggle = 1, HotkeyTiles = 2;
    private const int MenuToggle = 1, MenuShow = 2, MenuPrograms = 4, MenuTest = 5, MenuTestClose = 6, MenuAutostart = 7, MenuSettings = 8, MenuUpdate = 10, MenuLogs = 3, MenuExit = 9, MenuStats = 11;

    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private readonly string[] _args;
    private readonly string? _screenshots;
    private readonly SingleInstance? _single;
    private AppPaths _paths = null!;
    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private IDesktop _desktop = null!;
    private RestSwitcher _switcher = null!;
    private TrayAndHotkeys? _tray;
    private SwitcherWindow? _window;
    private ProgramsWindow? _programs;
    private SettingsWindow? _settingsWindow;
    private QuietWindow? _quiet;
    private Updater? _updater;
    private DispatcherQueueTimer? _updateTimer;
    private bool _updateBalloon;
    private TilesWindow? _tilesWindow;
    private WindowEvents? _windowEvents;
    private bool _tilesHintShown;
    private DispatcherQueue _queue = null!;
    private DispatcherQueueTimer? _scheduleTimer;
    private DateTime _lastScheduleCheck = DateTime.Now;
    private DateTime? _snoozeUntil;
    private DateTime? _restOfferRetry;
    private DateTime? _restOfferUntil;
    private bool _busy;
    private CancellationTokenSource? _cts;
    private string? _hotkeyProblem;

    public static int ExitCode { get; private set; }

    public App(string[] args, string? screenshots, SingleInstance? single)
    {
        _args = args;
        _screenshots = screenshots;
        _single = single;
        UnhandledException += (_, e) =>
        {
            Log.Error("Необработанная ошибка", e.Exception);
            e.Handled = _screenshots is null;
            if (_screenshots is not null) ExitCode = 3;
        };
    }

    // Метаданные стандартных элементов: интерфейс собран в коде, без XAML-разметки.
    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        RequestedTheme = ApplicationTheme.Light;

        if (_screenshots is not null)
        {
            _ = RunScreenshotsAsync(_screenshots);
            return;
        }

        _paths = AppPaths.Default();
        Log.Init(_paths.LogsDir);
        Log.Info($"GigaRest {typeof(App).Assembly.GetName().Version} запущена");
        _settingsStore = new SettingsStore(_paths.SettingsFile);
        _settings = _settingsStore.Load();
        _desktop = new WindowsDesktop();
        _switcher = new RestSwitcher(_desktop, SystemClock.Instance, new SessionStore(_paths.SessionFile))
        {
            CloseTimeout = TimeSpan.FromSeconds(_settings.CloseTimeoutSeconds),
        };

        var queue = DispatcherQueue.GetForCurrentThread();
        _tray = new TrayAndHotkeys();
        _tray.ShowIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"), TrayTip);
        _tray.TrayClicked += () => queue.TryEnqueue(ShowWindow);
        _tray.BalloonClicked += () => queue.TryEnqueue(() =>
        {
            if (_updateBalloon && _updater?.Available is { } m) { _updateBalloon = false; _ = InstallUpdateAsync(m); }
        });
        _tray.BalloonClicked += () => queue.TryEnqueue(OnBalloonClicked);
        _queue = queue;
        _tray.HotkeyPressed += id =>
        {
            if (id == HotkeyToggle) queue.TryEnqueue(Toggle);
            else if (id == HotkeyTiles) queue.TryEnqueue(BackToTiles);
        };
        _tray.MenuItems = MenuItems;
        _tray.MenuCommand += id => queue.TryEnqueue(() => OnMenu(id));
        RegisterHotkey();
        _single?.Listen(queue, ShowWindow, () =>
        {
            // Запустили новую версию — уступаем ей. Если идёт отдых, состояние в session.json — новая версия продолжит.
            Log.Info("Запущена другая версия — выходим, чтобы она работала вместо нас");
            Quit();
        });

        // Самообновление: через 30 секунд после запуска и раз в 6 часов. Остатки прошлого обновления (*.old) — убрать.
        _ = Task.Run(() => GigaRest.Core.Updates.UpdateApplier.CleanupOld(Updater.AppDir));
        _updater = new Updater { Channel = GigaRest.Core.Updates.UpdateChannels.Resolve(_settings.UpdateChannel, GigaRest.Core.Updates.AppVersion.Display) };
        _updateTimer = queue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromSeconds(30);
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (await _updater.CheckAsync(manual: false) is { } m) OfferUpdate(m);
        };
        if (!_args.Contains("--selftest")) _updateTimer.Start();
        if (_args.Contains("--after-update")) _tray.ShowBalloon("GigaRest обновлён", $"Версия {GigaRest.Core.Updates.AppVersion.Display}. Что нового — в docs/CHANGELOG.md.");

        // Расписание возврата: проверяем раз в 30 секунд (и сразу после пробуждения ПК — по разнице времени).
        _scheduleTimer = queue.CreateTimer();
        _scheduleTimer.Interval = TimeSpan.FromSeconds(30);
        _scheduleTimer.Tick += (_, _) => CheckSchedule();
        _scheduleTimer.Start();

        if (_args.Contains("--selftest"))
        {
            _ = SelfTestAsync();
        }
        else if (!_settings.SetupDone && !_switcher.IsResting)
        {
            // Первый запуск: сначала настройка — что закрывать, что на второй монитор (с подсказками).
            _ = ShowProgramsAsync(setup: true);
        }
        else if (_switcher.IsResting)
        {
            // Отдых продолжается (например, после перезагрузки): снова «Тихий» экран и плитки.
            EnterRestScreens(null);
        }
        else if (!_args.Contains("--tray"))
        {
            ShowWindow();
        }
    }

    // ---------- экраны отдыха: «Тихий» на основном мониторе, плитки на втором ----------

    /// <summary>Что не получилось при показе экранов отдыха — показываем в окне, а не молчим.</summary>
    private string? _restScreensProblem;

    private void EnterRestScreens(RestReport? report)
    {
        _restScreensProblem = null;
        IReadOnlyList<MonitorInfo> monitors = [];
        try { monitors = _desktop.GetMonitors(); }
        catch (Exception ex) { Log.Error("Мониторы", ex); }
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        var second = RestSwitcher.PickSecondMonitor(monitors);
        var tileCount = 0;

        // Плитки — отдельно: если с ними что-то не так, «Тихий» экран всё равно откроется.
        try
        {
            var session = new SessionStore(_paths.SessionFile).Load();
            var processes = _desktop.GetProcesses();
            var tiles = (session?.Moved ?? [])
                .Where(m => WindowTools.IsAlive(m.Handle))
                .GroupBy(m => m.ProgramId).Select(g => g.First())
                .Select(m => new TileInfo(m.Handle, m.ProgramId, m.DisplayName, processes.FirstOrDefault(p => p.Pid == m.Pid)?.ExePath))
                .ToList();
            Log.Info($"Плитки: второй монитор {(second is null ? "нет" : second.Id)}, окон для плиток {tiles.Count}");
            if (second is not null && tiles.Count > 0)
            {
                _windowEvents ??= new WindowEvents();
                _windowEvents.Clear();
                foreach (var t in tiles)
                {
                    _windowEvents.Watch(t.Hwnd);
                    var hr = WindowTools.SetTransitions(t.Hwnd, enabled: false);
                    if (hr != 0) Log.Info($"Плитки: системную анимацию сворачивания у «{t.Name}» не выключить (0x{hr:X8}) — наша идёт поверх");
                }
                _windowEvents.Minimized += OnTileMinimized;
                _windowEvents.Destroyed += OnTileDestroyed;
                _tilesWindow = new TilesWindow();
                _tilesWindow.OpenRequested += OpenTile;
                _tilesWindow.SetTiles(tiles);
                _tilesWindow.SetHotkeyHint(_settings.TilesHotkey);
                _tilesWindow.ShowOn(second);
                tileCount = tiles.Count;
                if (Hotkey.TryParse(_settings.TilesHotkey, out var tilesKey) && _tray?.RegisterHotkey(HotkeyTiles, tilesKey) is { } problem)
                {
                    Log.Warn("Клавиша плиток: " + problem);
                    _restScreensProblem = $"Клавиша «обратно в плитки» {problem}. Поменяйте её в настройках; пока — кнопка «Свернуть».";
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Плитки", ex);
            _restScreensProblem = "Плитки не открылись: " + ex.Message;
            try { _tilesWindow?.CloseForGood(); } catch (Exception) { }
            _tilesWindow = null;
        }

        try
        {
            var myScenes = string.IsNullOrWhiteSpace(_settings.MyScenesFolder) ? SceneCatalog.DefaultMyScenesFolder() : _settings.MyScenesFolder!;
            try { Directory.CreateDirectory(myScenes); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Папка «Мои сцены»: " + ex.Message); }
            var scenes = SceneCatalog.Load(RestScreens.ScenesDir, myScenes);
            _quiet = new QuietWindow(scenes, _settings.SceneId, _settings.SceneEffect) { Timers = GigaRest.Core.Scenes.QuietTimers.From(_settings) };
            _tilesWindow?.SetBackground(scenes.FirstOrDefault(s => s.Id == _settings.SceneId) is { IsVideo: false } current ? current.Path : scenes.FirstOrDefault(s => !s.IsVideo)?.Path);
            _quiet.SceneChanged += (scene, effect) =>
            {
                _tilesWindow?.SetBackground(scene.IsVideo ? null : scene.Path);
                _settings.SceneId = scene.Id;
                _settings.SceneEffect = effect;
                SaveSettings();
            };
            _quiet.OpenMyScenesRequested += () =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(myScenes);
                System.Diagnostics.Process.Start(psi)?.Dispose();
            };
            _quiet.ReturnRequested += () => _ = ReturnToWorkAsync();
            _quiet.PlayRequested += PlayGame;
            _quiet.InitNature(_settings.LastNatureSound, _settings.NatureVolume);
            _quiet.SetLastYouTube(_settings.LastYouTubeUrl, _settings.LastYouTubeTitle, _settings.LastYouTubeUtc);
            _quiet.NatureChanged += (sound, volume) => { _settings.LastNatureSound = sound; _settings.NatureVolume = volume; SaveSettings(); };
            _quiet.YouTubeWatched += (url, title) =>
            {
                _settings.LastYouTubeUrl = url;
                _settings.LastYouTubeTitle = title;
                _settings.LastYouTubeUtc = DateTime.UtcNow;
                SaveSettings();
            };
            _quiet.AddGameRequested += () => _ = AddGameAsync();
            _quiet.RemoveGameRequested += game =>
            {
                _settings.CustomGames.RemoveAll(c => GigaRest.Core.Games.LocalGames.FromCustom(c).Id == game.Id);
                SaveSettings();
                if (_quiet is { } q) LoadGames(q);
            };
            _quiet.OpenCoversRequested += () =>
            {
                var folder = GigaRest.Core.Games.LocalGames.DefaultCoversFolder();
                try { Directory.CreateDirectory(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Папка обложек: " + ex.Message); }
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                System.Diagnostics.Process.Start(psi)?.Dispose();
            };
            _quiet.DndClicked += DoNotDisturb.OpenSettings;
            TurnOnDoNotDisturb(_quiet);
            LoadGames(_quiet);
            LoadFilms(_quiet);
            _quiet.FilmRequested += OpenFilm;
            _quiet.FilmClosed += OnFilmClosed;
            _quiet.FilmFailed += OnFilmFailed;
            _quiet.FilmsFolderPickRequested += () => _ = PickFilmsFolderAsync();
            _quiet.PlayerChosen += ChoosePlayer;
            _quiet.PlayerPickRequested += () => _ = PickPlayerAsync();
            _quiet.MusicAppRequested += OpenMusicApp;
            _quiet.MusicAppPickRequested += () => _ = PickMusicAppAsync();
            LoadApps(_quiet);
            _quiet.OpenFilmsFolderRequested += () =>
            {
                var folder = FilmsFolder;
                try { Directory.CreateDirectory(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Папка с фильмами: " + ex.Message); }
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                System.Diagnostics.Process.Start(psi)?.Dispose();
            };
            _quiet.DetailsRequested += () => { _quiet?.HideSummary(); _window?.Present(); };
            _quiet.ShowOn(primary);
            _window?.AppWindow.Hide();
            var text = report?.Summary ?? "Отдых.";
            var problems = (report?.Problems ?? []).ToList();
            if (_restScreensProblem is not null) problems.Add(_restScreensProblem);
            if (report is not null || problems.Count > 0)
                _quiet.ShowSummary(text + (problems.Count > 0 ? "\n" + string.Join("\n", problems) : ""), problems.Count > 0);
            Log.Info($"Экраны отдыха: тихий экран показан, плиток {tileCount}");
            RestScreensShown?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Тихий экран", ex);
            _restScreensProblem = (_restScreensProblem is null ? "" : _restScreensProblem + "\n") + "«Тихий» экран не открылся: " + ex.Message;
            try { _quiet?.CloseForGood(); } catch (Exception) { }
            _quiet = null;
            _window?.SetNotice(_restScreensProblem + " Пришлите, пожалуйста, журнал (меню значка → «Открыть журнал»).");
            _window?.Present();
        }
    }

    /// <summary>
    /// Самопроверка для CI (GigaRest.exe --selftest): пробный режим целиком на настоящей Windows —
    /// тестовые окна → отдых → «Тихий» экран (CI снимает экран по файлу-флагу) → возврат → выход.
    /// Код выхода 0 — экраны отдыха показались и всё вернулось.
    /// </summary>
    private async Task SelfTestAsync()
    {
        var flag = Path.Combine(_paths.Root, "rest-shown.flag");
        var shown = false;
        RestScreensShown += () => { shown = true; File.WriteAllText(flag, DateTime.Now.ToString("O")); };
        try
        {
            _settings.SetupDone = true;
            Log.Info("Самопроверка: старт");
            await StartTestAsync();
            await Task.Delay(1500);
            // Тест 2 спросит «Сохранить?» — отвечаем «Нет» через 3 секунды, как ответил бы человек.
            using var answering = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!answering.IsCancellationRequested)
                {
                    await Task.Delay(3000);
                    var testPids = _desktop.GetProcesses().Where(p => TestPrograms.IsTest(p.ExeName)).Select(p => p.Pid).ToHashSet();
                    foreach (var w in _desktop.GetWindows().Where(w => w.IsDialog && testPids.Contains(w.Pid)))
                    {
                        Log.Info("Самопроверка: отвечаем «Нет» на вопрос тестового окна");
                        WindowTools.AnswerNo(w.Handle);
                    }
                }
            });
            await GoToRestAsync(Window().Programs);
            answering.Cancel();
            await Task.Delay(10000);           // CI в это время снимает экран
            await ReturnToWorkAsync();
            await Task.Delay(4000);
            CloseTestWindows();
            Log.Info($"Самопроверка: конец, экраны отдыха {(shown ? "показаны" : "НЕ показаны")}");
            ExitCode = shown && _restScreensProblem is null ? 0 : 4;
        }
        catch (Exception ex)
        {
            Log.Error("Самопроверка", ex);
            ExitCode = 5;
        }
        Quit();
    }

    /// <summary>Для самопроверки в CI: экраны отдыха показаны.</summary>
    private event Action? RestScreensShown;

    private void ExitRestScreens()
    {
        _tray?.UnregisterHotkey(HotkeyTiles);
        new DoNotDisturb(_paths.Root).Restore();
        if (_windowEvents is not null)
        {
            _windowEvents.Minimized -= OnTileMinimized;
            _windowEvents.Destroyed -= OnTileDestroyed;
            foreach (var hwnd in _windowEvents.Watched)
                if (WindowTools.IsAlive(hwnd)) WindowTools.SetTransitions(hwnd, enabled: true);
            _windowEvents.Clear();
        }
        _tilesWindow?.CloseForGood();
        _tilesWindow = null;
        _quiet?.CloseForGood();
        _quiet = null;
    }

    /// <summary>Щелчок по плитке: окно программы — сверху, на весь второй монитор. Закрыта — открыть снова.</summary>
    private void OpenTile(TileInfo tile)
    {
        if (WindowTools.IsAlive(tile.Hwnd))
        {
            if (_tilesWindow is not null) _tilesWindow.AnimateOpen(tile.Hwnd, () => WindowTools.Activate(tile.Hwnd));
            else WindowTools.Activate(tile.Hwnd);
            if (!_tilesHintShown)
            {
                _tilesHintShown = true;
                _tray?.ShowBalloon(tile.Name, $"«Свернуть» или {_settings.TilesHotkey} — обратно в плитки.");
            }
        }
        else if (tile.ExePath is not null)
        {
            var error = _desktop.Launch(new LaunchSpec { FileName = tile.ExePath, WorkingDirectory = Path.GetDirectoryName(tile.ExePath) });
            if (error is not null) _tray?.ShowBalloon(tile.Name, "Не открылась: " + error);
        }
    }

    // Окно с плитки свернули — сразу поднимаем плитки «поверх всех» (системное сворачивание уходит под них),
    // копия окна уменьшается в свою плитку, а само окно потом тихо разворачивается под плитками.
    private void OnTileMinimized(long hwnd) => _queue.TryEnqueue(() => ReturnToTiles(hwnd));

    private async void ReturnToTiles(long hwnd)
    {
        if (_tilesWindow is not { } tiles) return;
        try
        {
            WindowTools.SetTopmost(tiles.Handle, true);
            tiles.AnimateClose(hwnd);
            await Task.Delay(300); // Windows досворачивает окно — иначе наш возврат перебивается её анимацией
            if (_tilesWindow != tiles) return;
            WindowTools.ParkUnderTiles(hwnd, tiles.Handle);
        }
        catch (Exception ex)
        {
            Log.Error("Обратно в плитки", ex);
        }
    }

    // Библиотеки игр читаются в фоне — «Тихий» экран не ждёт.
    private void LoadGames(QuietWindow quiet)
    {
        var played = new Dictionary<string, DateTime>(_settings.GamesPlayed);
        var custom = _settings.CustomGames.Where(c => File.Exists(c.Path)).Select(GigaRest.Core.Games.LocalGames.FromCustom).ToList();
        _ = Task.Run(() =>
        {
            List<GigaRest.Core.Games.Game> games;
            try
            {
                var all = GameSources.FindInstalled().Concat(custom);
                all = GigaRest.Core.Games.LocalGames.ApplyCovers(all, GigaRest.Core.Games.LocalGames.DefaultCoversFolder());
                games = GigaRest.Core.Games.GameLibraries.Merge(all, played);
            }
            catch (Exception ex) { Log.Error("Игры", ex); games = []; }
            _queue.TryEnqueue(() => { if (_quiet == quiet) quiet.SetGames(games); });
        });
    }

    private string FilmsFolder => string.IsNullOrWhiteSpace(_settings.FilmsFolder) ? GigaRest.Core.Media.FilmLibrary.DefaultFolder() : _settings.FilmsFolder!;

    // Папка с фильмами читается в фоне, как и игры.
    private void LoadFilms(QuietWindow quiet)
    {
        var folder = FilmsFolder;
        var opened = new Dictionary<string, DateTime>(_settings.FilmsOpened);
        var positions = new Dictionary<string, double>(_settings.FilmsPosition, StringComparer.OrdinalIgnoreCase);
        _ = Task.Run(() =>
        {
            List<Film> films;
            try { films = FilmLibrary.Scan(folder); }
            catch (Exception ex) { Log.Error("Фильмы", ex); films = []; }
            Film? last = null;
            foreach (var (path, _) in opened.OrderByDescending(p => p.Value))
            {
                if (!File.Exists(path)) continue;
                var info = new FileInfo(path);
                last = new Film(path, FilmLibrary.Title(Path.GetFileNameWithoutExtension(path)), info.LastWriteTimeUtc, info.Length);
                break;
            }
            Log.Info($"Фильмы: {films.Count}");
            var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            _queue.TryEnqueue(() => { if (_quiet == quiet) quiet.SetFilms(films, last, positions, name.Length > 0 ? name : folder); });
        });
    }

    // Музыкальная программа и плееры — по ярлыкам меню «Пуск» (в фоне).
    private void LoadApps(QuietWindow quiet)
    {
        var musicSetting = _settings.MusicApp;
        var player = _settings.FilmPlayer;
        _ = Task.Run(() =>
        {
            List<GigaRest.Core.Programs.ShortcutInfo> shortcuts;
            try { shortcuts = StartMenu.ReadShortcuts(); }
            catch (Exception ex) { Log.Error("Меню «Пуск»", ex); shortcuts = []; }
            var music = !string.IsNullOrEmpty(musicSetting) && File.Exists(musicSetting)
                ? new AppChoice(Apps.NameOf(musicSetting), musicSetting)
                : Apps.FindMusic(shortcuts);
            var players = Apps.FindPlayers(shortcuts);
            Log.Info($"Музыка: {music?.Name ?? "не найдена"}; плееры: {(players.Count == 0 ? "нет" : string.Join(", ", players.Select(p => p.Name)))}");
            _queue.TryEnqueue(() =>
            {
                if (_quiet != quiet) return;
                quiet.SetMusicApp(music);
                quiet.SetPlayers(players, player);
                _players = players;
            });
        });
    }

    private List<AppChoice> _players = [];

    private void OpenFilm(Film film)
    {
        if (_settings.FilmPlayer is { } player)
        {
            var spec = new LaunchSpec { FileName = player, WorkingDirectory = Path.GetDirectoryName(player) };
            spec.Arguments.Add(film.Path);
            if (_desktop.Launch(spec) is { } error)
            {
                _quiet?.ShowSummary($"Плеер не открылся: {error}", hasProblems: true);
                return;
            }
        }
        else
        {
            var start = _settings.FilmsPosition.TryGetValue(film.Path, out var pos) ? pos : 0;
            _quiet?.PlayFilm(film, start);
        }
        _settings.FilmsOpened[film.Path] = DateTime.UtcNow;
        // Помним только последние 20.
        foreach (var old in _settings.FilmsOpened.OrderByDescending(p => p.Value).Skip(20).Select(p => p.Key).ToList())
        {
            _settings.FilmsOpened.Remove(old);
            _settings.FilmsPosition.Remove(old);
        }
        SaveSettings();
    }

    private void OnFilmClosed(Film film, double position, double duration)
    {
        if (Apps.WorthResuming(position, duration)) _settings.FilmsPosition[film.Path] = Math.Round(position);
        else _settings.FilmsPosition.Remove(film.Path);
        SaveSettings();
        Log.Info($"Фильм закрыт на {Apps.Position(position)} из {Apps.Position(duration)}");
        if (_quiet is { } q) LoadFilms(q);
    }

    private void OnFilmFailed(Film film)
    {
        var error = OpenShell(film.Path);
        _quiet?.ShowSummary(error is null
            ? $"«{film.Name}» встроенный плеер не открыл — открыли плеером Windows. Свой плеер можно выбрать в «Смотреть» → «Плеер»."
            : $"«{film.Name}» не открылся: {error}", hasProblems: error is not null);
    }

    // Выбор папки и программ — системными окнами Windows поверх «Тихого» экрана.
    private async Task<string?> PickFolderAsync()
    {
        if (_quiet is null) return null;
        var picker = new global::Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, RestScreens.Hwnd(_quiet));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickExeAsync()
    {
        if (_quiet is null) return null;
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, RestScreens.Hwnd(_quiet));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task PickFilmsFolderAsync()
    {
        try
        {
            if (await PickFolderAsync() is not { } path) return;
            _settings.FilmsFolder = path;
            SaveSettings();
            if (_quiet is { } q) LoadFilms(q);
        }
        catch (Exception ex) { Log.Error("Выбор папки с фильмами", ex); }
    }

    private async Task PickPlayerAsync()
    {
        try
        {
            if (await PickExeAsync() is not { } path) return;
            ChoosePlayer(path);
        }
        catch (Exception ex) { Log.Error("Выбор плеера", ex); }
    }

    private void ChoosePlayer(string? path)
    {
        _settings.FilmPlayer = path;
        SaveSettings();
        _quiet?.SetPlayers(_players, path);
    }

    private async Task PickMusicAppAsync()
    {
        try
        {
            if (await PickExeAsync() is not { } path) return;
            _settings.MusicApp = path;
            SaveSettings();
            _quiet?.SetMusicApp(new AppChoice(Apps.NameOf(path), path));
        }
        catch (Exception ex) { Log.Error("Выбор программы для музыки", ex); }
    }

    private void OpenMusicApp(AppChoice app)
    {
        if (_desktop.Launch(new LaunchSpec { FileName = app.Path, WorkingDirectory = Path.GetDirectoryName(app.Path) }) is { } error)
            _quiet?.ShowSummary($"{app.Name} не открылась: {error}", hasProblems: true);
    }

    /// <summary>Открыть файл или ссылку так, как открыла бы сама Windows (плеер, браузер по умолчанию).</summary>
    private static string? OpenShell(string target)
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn("Открыть: " + ex.Message);
            return ex.Message;
        }
    }

    // «Не беспокоить» — в фоне: Windows подтверждает режим не сразу.
    private void TurnOnDoNotDisturb(QuietWindow quiet)
    {
        if (!_settings.DoNotDisturb) { quiet.SetDnd(null); return; }
        var dnd = new DoNotDisturb(_paths.Root);
        _ = Task.Run(() =>
        {
            var on = dnd.TurnOn();
            _queue.TryEnqueue(() =>
            {
                if (_quiet != quiet) return;
                quiet.SetDnd(on);
                if (!on) _tray?.ShowBalloon("Не беспокоить", "Windows не дала включить режим сама. Щёлкните «включить „Не беспокоить“» вверху экрана.");
            });
        });
    }

    // Игра без магазина: выбрать её exe; название — по папке игры.
    private async Task AddGameAsync()
    {
        try
        {
            if (await PickExeAsync() is not { } path) return;
            if (_settings.CustomGames.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            _settings.CustomGames.Add(new GigaRest.Core.Games.CustomGame { Name = GigaRest.Core.Games.LocalGames.NameFromExe(path), Path = path });
            SaveSettings();
            Log.Info("Своя игра добавлена");
            if (_quiet is { } q) LoadGames(q);
        }
        catch (Exception ex) { Log.Error("Добавить игру", ex); }
    }

    private StatsWindow? _statsWindow;

    // Статистика: сводка из stats.json, время Steam — из его настроек (в фоне).
    private void ShowStats()
    {
        _statsWindow ??= new StatsWindow();
        var stats = new GigaRest.Core.Stats.StatsStore(_paths.StatsFile).Load();
        _statsWindow.Show(stats, [], DateTime.Now);
        _statsWindow.Present();
        var window = _statsWindow;
        _ = Task.Run(() =>
        {
            List<GigaRest.Core.Games.Game> games;
            try { games = GameSources.FindInstalled(); }
            catch (Exception ex) { Log.Error("Статистика: игры", ex); return; }
            _queue.TryEnqueue(() => window.Show(stats, games, DateTime.Now));
        });
    }

    private void RecordStats(Action<GigaRest.Core.Stats.RestStats> change)
    {
        try
        {
            var store = new GigaRest.Core.Stats.StatsStore(_paths.StatsFile);
            var stats = store.Load();
            change(stats);
            store.Save(stats);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Статистика: " + ex.Message); }
    }

    private void PlayGame(GigaRest.Core.Games.Game game)
    {
        Log.Info($"Играть: {game.Name} ({game.Id})");
        var error = GameSources.Launch(game);
        if (error is not null)
        {
            _quiet?.ShowSummary($"«{game.Name}» не запустилась: {error}", hasProblems: true);
            return;
        }
        _settings.GamesPlayed[game.Id] = DateTime.UtcNow;
        RecordStats(s => s.AddLaunch(game, DateTime.UtcNow));
        SaveSettings();
    }

    private void OnTileDestroyed(long hwnd) => _queue.TryEnqueue(() => _tilesWindow?.MarkClosed(hwnd));

    /// <summary>Клавиша «обратно в плитки»: окно, открытое с плитки, — вниз, плитки — сверху.</summary>
    private void BackToTiles()
    {
        if (_tilesWindow is null) return;
        var foreground = WindowTools.Foreground();
        if (foreground != 0 && _windowEvents?.IsWatched(foreground) == true)
        {
            ReturnToTiles(foreground);
        }
        else
        {
            WindowTools.RaiseAboveAll(_tilesWindow.Handle);
        }
    }

    private async Task RunScreenshotsAsync(string dir)
    {
        try
        {
            ExitCode = await Screenshots.RunAsync(dir) ? 0 : 2;
        }
        catch (Exception ex)
        {
            ExitCode = 2;
            File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
        }
        finally
        {
            Exit();
        }
    }

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GigaRest.exe");

    /// <summary>
    /// Раз в 30 секунд: вечером — мягкое предложение отдыха (уведомление у значка), утром — «Пора за работу».
    /// Во время игры или фильма на весь экран ничего не показываем — откладываем на 15 минут.
    /// </summary>
    /// <summary>
    /// «Завершить» в итоге: программа спряталась в трей и ни о чём не спрашивает. Завершаем по явной просьбе,
    /// запоминаем галочку «можно завершать» и отмечаем, что при возврате её надо открыть.
    /// </summary>
    private void TerminateFromSummary(string programId, bool force)
    {
        var program = ProgramScanner.Scan(_desktop.GetProcesses(), _desktop.GetWindows(), _settings, _desktop.CurrentProcessId)
            .FirstOrDefault(p => p.Id.Equals(programId, StringComparison.OrdinalIgnoreCase));
        if (program is not null)
        {
            if (!force && (program.Windows.Count > 0 || _desktop.GetWindows().Any(w => w.IsDialog && program.Processes.Any(p => p.Pid == w.Pid))))
            {
                _tray?.ShowBalloon(program.DisplayName, "У программы снова открыто окно — закройте её сами.");
                return;
            }
            foreach (var p in program.Processes) _desktop.Terminate(p.Key);
        }
        var setting = _settings.Find(programId) ?? new ProgramSetting { Id = programId, DisplayName = program?.DisplayName ?? programId, Action = ProgramAction.Close };
        if (!_settings.Programs.Contains(setting)) _settings.Programs.Add(setting);
        setting.AllowTerminate = true;
        if (force) setting.ForceKill = true;
        SaveSettings();
        _switcher.MarkClosed(programId);
        Log.Info($"Завершена из итога по просьбе: {programId}");
    }

    private void OfferUpdate(GigaRest.Core.Updates.UpdateManifest m)
    {
        _updateBalloon = true;
        _tray?.ShowBalloon("Доступна новая версия", $"GigaRest {m.Version}. Щёлкните здесь, чтобы обновиться (около минуты).");
    }

    private async Task UpdateFromMenuAsync()
    {
        if (_updater is null) return;
        if (_updater.Available is { } ready) { await InstallUpdateAsync(ready); return; }
        try
        {
            var m = await _updater.CheckAsync(manual: true);
            if (m is null) _tray?.ShowBalloon("Обновления", $"У вас последняя версия: {GigaRest.Core.Updates.AppVersion.Display}.");
            else OfferUpdate(m);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("Обновления", "Не удалось проверить: " + ex.Message);
        }
    }

    private async Task InstallUpdateAsync(GigaRest.Core.Updates.UpdateManifest m)
    {
        if (_updater is null || _busy) { _tray?.ShowBalloon("Обновление", "Сейчас идёт переход — обновим чуть позже."); return; }
        // Ход обновления — в окошке поверх всех в правом нижнем углу (и в подсказке значка).
        UpdateWindow? window = null;
        try { window = new UpdateWindow(m.Version); window.Activate(); }
        catch (Exception ex) { Log.Error("Окно обновления", ex); }
        try
        {
            var last = DateTime.MinValue;
            var progress = new Progress<(string Text, int Percent)>(p =>
            {
                window?.Report(p.Text, p.Percent);
                _tray?.SetTip("GigaRest — " + p.Text);
                if (DateTime.Now - last > TimeSpan.FromSeconds(15)) { last = DateTime.Now; Log.Info(p.Text); }
            });
            await Task.Run(() => _updater.InstallAsync(m, progress, () => _queue.TryEnqueue(Quit)));
        }
        catch (Exception ex)
        {
            Log.Error("Обновление", ex);
            _tray?.SetTip(TrayTip);
            if (window is not null) window.Fail(ex.Message);
            else _tray?.ShowBalloon("Обновление не получилось", ex.Message);
        }
    }

    private void CheckSchedule()
    {
        var now = DateTime.Now;
        var previous = _lastScheduleCheck;
        _lastScheduleCheck = now;
        if (_busy) return;

        if (_switcher.IsResting)
        {
            _restOfferUntil = null;
            var due = _snoozeUntil is { } until && now >= until || _settings.ReturnSchedule.IsDue(previous, now);
            if (!due) return;
            if (UserState.ShouldNotDisturb()) { _snoozeUntil = now.AddMinutes(15); Log.Info("Расписание: занят (игра/презентация), «Пора за работу» — через 15 минут"); return; }
            _snoozeUntil = null;
            Log.Info("Расписание: предлагаем вернуться к работе");
            var w = Window();
            w.ShowReturnOffer(_settings.ReturnSchedule.Describe(), ClosedInSession());
            w.Present();
        }
        else
        {
            _snoozeUntil = null;
            var due = _restOfferRetry is { } retry && now >= retry || _settings.RestOffer.IsDue(previous, now);
            if (!due) return;
            if (UserState.ShouldNotDisturb()) { _restOfferRetry = now.AddMinutes(15); return; }
            _restOfferRetry = null;
            // Мягко: одно уведомление. Не щёлкнули — до следующего раза по расписанию больше не напоминаем.
            _restOfferUntil = now.AddMinutes(30);
            Log.Info("Расписание: мягко предлагаем отдых");
            _tray?.ShowBalloon("Вечер", "Перейти в отдых? Щёлкните здесь — покажем, что закроется.");
        }
    }

    private void OnBalloonClicked()
    {
        if (_restOfferUntil is { } until && DateTime.Now <= until && !_switcher.IsResting)
        {
            _restOfferUntil = null;
            ShowWindow();
        }
    }

    /// <summary>Что закрыто в этом отдыхе — из session.json.</summary>
    private List<(string Id, string Name)> ClosedInSession() =>
        new SessionStore(_paths.SessionFile).Load()?.Closed.Where(c => c.Gone).Select(c => (c.Id, c.DisplayName)).ToList() ?? [];

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Saved += r =>
            {
                _settings.ReturnSchedule = r.Schedule;
                _settings.RestOffer = r.RestOffer;
                _settings.ConfirmBeforeRest = r.ConfirmBeforeRest;
                _settings.DoNotDisturb = r.DoNotDisturb;
                if (_window is not null) _window.DoNotDisturb = r.DoNotDisturb;
                _settings.FilmsFolder = string.IsNullOrWhiteSpace(r.FilmsFolder) ? null : r.FilmsFolder.Trim();
                _settings.UpdateChannel = r.UpdateChannel;
                _settings.TilesEnabled = r.TilesEnabled;
                _settings.TilesCount = r.TilesCount;
                _settings.DimMinutes = r.DimMinutes;
                _settings.ScreenOffMinutes = r.ScreenOffMinutes;
                if (_window is not null) _window.MaxTiles = r.TilesEnabled ? r.TilesCount : 0;
                if (_quiet is not null) _quiet.Timers = GigaRest.Core.Scenes.QuietTimers.From(_settings);
                if (_updater is not null) _updater.Channel = GigaRest.Core.Updates.UpdateChannels.Resolve(r.UpdateChannel, GigaRest.Core.Updates.AppVersion.Display);
                var hotkeyChanged = r.Hotkey != _settings.Hotkey;
                _settings.Hotkey = r.Hotkey;
                _settings.TilesHotkey = r.TilesHotkey;
                SaveSettings();
                if (hotkeyChanged) RegisterHotkey();
                try { Autostart.Set(ExePath, r.Autostart); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { Log.Error("Автозапуск", ex); }
                Log.Info($"Настройки сохранены: отдых {r.RestOffer.Describe()}, возврат {r.Schedule.Describe()}, клавиша {r.Hotkey}");
                _tray?.ShowBalloon("Настройки сохранены", _hotkeyProblem ?? $"Возврат: {r.Schedule.Describe()}. Клавиша: {r.Hotkey}.");
            };
        }
        _settingsWindow.Load(_settings, Autostart.IsEnabled(ExePath), _hotkeyProblem);
        _settingsWindow.Present();
    }

    private string TrayTip => _switcher.IsResting ? "GigaRest — отдых" : "GigaRest — работа";

    private void RegisterHotkey()
    {
        if (!Hotkey.TryParse(_settings.Hotkey, out var hotkey))
        {
            _hotkeyProblem = $"Горячая клавиша «{_settings.Hotkey}» записана с ошибкой — поправьте в settings.json";
        }
        else
        {
            _hotkeyProblem = _tray!.RegisterHotkey(HotkeyToggle, hotkey);
        }
        if (_hotkeyProblem is not null)
        {
            Log.Warn(_hotkeyProblem);
            _tray!.ShowBalloon("Горячая клавиша", _hotkeyProblem);
        }
    }

    private IReadOnlyList<TrayMenuItem?> MenuItems() =>
    [
        new TrayMenuItem(MenuToggle, _switcher.IsResting ? "Вернуться к работе" : "Перейти в отдых…", !_busy, Bold: true),
        new TrayMenuItem(MenuShow, "Показать окно"),
        new TrayMenuItem(MenuPrograms, "Рабочие программы…"),
        null,
        new TrayMenuItem(MenuTest, "Пробный режим — на тестовых окнах…", !_busy && !_switcher.IsResting),
        new TrayMenuItem(MenuTestClose, "Закрыть тестовые окна", !_busy),
        null,
        new TrayMenuItem(MenuSettings, "Настройки…"),
        new TrayMenuItem(MenuStats, "Статистика отдыха…"),
        _updater?.Available is { } update
            ? new TrayMenuItem(MenuUpdate, $"Обновить до {update.Version}", Bold: false)
            : new TrayMenuItem(MenuUpdate, "Проверить обновления"),
        new TrayMenuItem(MenuAutostart, "Запускать вместе с Windows", Checked: Autostart.IsEnabled(ExePath)),
        new TrayMenuItem(MenuLogs, "Открыть журнал"),
        null,
        new TrayMenuItem(MenuExit, "Выход"),
    ];

    private void OnMenu(int id)
    {
        switch (id)
        {
            case MenuToggle: Toggle(); break;
            case MenuShow: ShowWindow(); break;
            case MenuPrograms: _ = ShowProgramsAsync(); break;
            case MenuTest: _ = StartTestAsync(); break;
            case MenuTestClose: CloseTestWindows(); break;
            case MenuSettings: ShowSettings(); break;
            case MenuStats: ShowStats(); break;
            case MenuUpdate: _ = UpdateFromMenuAsync(); break;
            case MenuAutostart:
                var enable = !Autostart.IsEnabled(ExePath);
                try
                {
                    Autostart.Set(ExePath, enable);
                    Log.Info($"Автозапуск: {(enable ? "включён" : "выключен")}");
                    _tray?.ShowBalloon("Автозапуск", enable ? "GigaRest будет запускаться вместе с Windows, в трее." : "Автозапуск выключен.");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    Log.Error("Автозапуск", ex);
                    _tray?.ShowBalloon("Автозапуск", "Не получилось изменить автозапуск: " + ex.Message);
                }
                break;
            case MenuLogs:
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(_paths.LogsDir);
                System.Diagnostics.Process.Start(psi)?.Dispose();
                break;
            case MenuExit: Quit(); break;
        }
    }

    private SwitcherWindow Window()
    {
        if (_window is null)
        {
            _window = new SwitcherWindow { DoNotDisturb = _settings.DoNotDisturb, MaxTiles = _settings.TilesEnabled ? _settings.TilesCount : 0 };
            _window.ActionChanged += p =>
            {
                ProgramScanner.Apply(_settings, p);
                SaveSettings();
            };
            _window.GoRequested += () => _ = GoToRestAsync(_window.Programs);
            _window.ReturnRequested += () => _ = ReturnToWorkAsync();
            _window.CancelRequested += () => _cts?.Cancel();
            _window.ProgramsRequested += () => _ = ShowProgramsAsync();
            _window.TestRequested += () => _ = StartTestAsync();
            _window.TerminateRequested += TerminateFromSummary;
            _window.SnoozeRequested += () =>
            {
                _snoozeUntil = DateTime.Now.AddMinutes(15);
                Log.Info("Расписание: отложено на 15 минут");
            };
        }
        return _window;
    }

    private void ShowWindow()
    {
        var w = Window();
        if (!_busy)
        {
            if (_switcher.IsResting) w.ShowRestWaiting(ClosedInSession());
            else w.ShowPlan(Scan(), _desktop.GetMonitors().Count > 1, _hotkeyProblem);
        }
        w.Present();
    }

    /// <summary>Окно «Рабочие программы»: запущенные, настроенные и установленные (ярлыки «Пуск» читаем в фоне).</summary>
    private async Task ShowProgramsAsync(bool setup = false)
    {
        if (_programs is null)
        {
            _programs = new ProgramsWindow();
            _programs.Saved += rows =>
            {
                ProgramCatalog.Save(_settings, rows);
                var firstSetup = !_settings.SetupDone;
                _settings.SetupDone = true;
                SaveSettings();
                Log.Info($"Рабочие программы: закрывать {rows.Count(r => r.Action == ProgramAction.Close)}, на второй монитор {rows.Count(r => r.Action == ProgramAction.SecondMonitor)}");
                if (firstSetup) ShowWindow(); // после первой настройки — сразу окно перехода
                else if (!_busy && !_switcher.IsResting && _window is not null)
                    _window.ShowPlan(Scan(), _desktop.GetMonitors().Count > 1, _hotkeyProblem);
            };
        }
        _programs.SetSetupMode(setup);
        _programs.ShowLoading();
        _programs.Present();
        try
        {
            var running = Scan();
            var processes = _desktop.GetProcesses();
            var background = BackgroundScanner.Find(processes, _desktop.GetWindows(), _desktop.CurrentProcessId)
                .Where(b => !TestPrograms.IsTest(b.Id)).ToList();
            var shortcuts = await Task.Run(StartMenu.ReadShortcuts);
            _programs.SetRows(ProgramCatalog.BuildRows(_settings, running, shortcuts, background));
        }
        catch (Exception ex)
        {
            Log.Error("Список программ", ex);
            _programs.SetRows(ProgramCatalog.BuildRows(_settings, Scan(), []));
        }
    }

    /// <summary>
    /// Пробный режим: четыре тестовых окна (копии GigaRest.exe под своими именами) и окно перехода только с ними.
    /// </summary>
    private async Task StartTestAsync()
    {
        if (_busy || _switcher.IsResting) { ShowWindow(); return; }
        var w = Window();
        try
        {
            var appDir = AppContext.BaseDirectory;
            var source = Environment.ProcessPath ?? Path.Combine(appDir, "GigaRest.exe");
            var specs = TestPrograms.All;
            for (var i = 0; i < specs.Count; i++)
            {
                var exe = Path.Combine(appDir, specs[i].ExeName);
                if (!File.Exists(exe) || new FileInfo(exe).Length != new FileInfo(source).Length)
                    File.Copy(source, exe, overwrite: true);
                var running = _desktop.GetProcesses().Any(p => string.Equals(p.ExeName, specs[i].ExeName, StringComparison.OrdinalIgnoreCase));
                if (!running)
                {
                    var error = _desktop.Launch(new LaunchSpec { FileName = exe, Arguments = TestPrograms.Arguments(specs[i], i), WorkingDirectory = appDir });
                    if (error is not null) throw new IOException($"{specs[i].Title}: {error}");
                }
            }

            // Ждём, пока все четыре окна появятся.
            List<RunningProgram> picked = [];
            for (var attempt = 0; attempt < 40; attempt++)
            {
                picked = TestPrograms.Pick(ProgramScanner.Scan(_desktop.GetProcesses(), _desktop.GetWindows(), _settings, _desktop.CurrentProcessId))
                    .Where(p => p.Windows.Count > 0).ToList();
                if (picked.Count == specs.Count) break;
                await Task.Delay(250);
            }
            Log.Info($"Пробный режим: тестовых окон {picked.Count}");
            var hasSecond = _desktop.GetMonitors().Count > 1;
            w.ShowPlan(picked, hasSecond,
                hasSecond ? null : "Второго монитора сейчас нет — тест 4 просто останется на месте.", test: true);
            w.Present();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Пробный режим", ex);
            w.ShowPlan(Scan(), _desktop.GetMonitors().Count > 1,
                "Не получилось открыть тестовые окна: " + ex.Message + ". Если программа лежит в папке только для чтения — распакуйте её, например, в «Документы».");
            w.Present();
        }
    }

    /// <summary>Закрыть все тестовые окна — их процессы наши, завершаем без вопросов.</summary>
    private void CloseTestWindows()
    {
        var count = 0;
        foreach (var p in _desktop.GetProcesses().Where(p => TestPrograms.IsTest(p.ExeName)))
            if (_desktop.Terminate(p.Key)) count++;
        Log.Info($"Тестовые окна закрыты: {count}");
        _tray?.ShowBalloon("Пробный режим", count > 0 ? "Тестовые окна закрыты." : "Тестовых окон нет.");
    }

    private List<RunningProgram> Scan()
    {
        var programs = ProgramScanner.Scan(_desktop.GetProcesses(), _desktop.GetWindows(), _settings, _desktop.CurrentProcessId)
            .Where(p => !TestPrograms.IsTest(p.Id)).ToList();
        if (ProgramScanner.RememberNew(_settings, programs)) SaveSettings();
        return programs;
    }

    private void SaveSettings()
    {
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) { Log.Error("Не удалось сохранить настройки", ex); }
    }

    /// <summary>Горячая клавиша и пункт меню: работа → окно перехода (или сразу), отдых → вернуть всё.</summary>
    private void Toggle()
    {
        if (_busy) { _window?.Present(); return; }
        if (_switcher.IsResting) { _ = ReturnToWorkAsync(); return; }
        if (_settings.ConfirmBeforeRest) { ShowWindow(); return; }
        _ = GoToRestAsync(Scan());
    }

    private async Task GoToRestAsync(IReadOnlyList<RunningProgram> programs)
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        var w = Window();
        try
        {
            w.ShowProgress(programs);
            w.Present();
            var progress = new Progress<SwitchProgress>(w.UpdateProgress);
            _switcher.MaxTiles = _settings.TilesEnabled ? _settings.TilesCount : 0;
            var report = await Task.Run(() => _switcher.GoToRestAsync(programs, progress, _cts.Token));
            w.ShowRest(report);
            _tray?.SetTip(TrayTip);
            if (_switcher.IsResting) EnterRestScreens(report);
        }
        catch (Exception ex)
        {
            Log.Error("Переход в отдых", ex);
            w.ShowError("Не получилось перейти в отдых: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReturnToWorkAsync()
    {
        if (_busy) return;
        _busy = true;
        var w = Window();
        ExitRestScreens();
        try
        {
            w.ShowReturning();
            w.Present();
            var progress = new Progress<SwitchProgress>(w.UpdateProgress);
            var restStarted = new SessionStore(_paths.SessionFile).Load()?.StartedUtc;
            var report = await Task.Run(() => _switcher.ReturnToWork(progress, CancellationToken.None));
            if (restStarted is { } started && started > DateTime.MinValue) RecordStats(s => s.AddRest(started, DateTime.UtcNow));
            w.ShowWork(report);
            _tray?.SetTip(TrayTip);
            _tray?.ShowBalloon("Работа", report.Summary);
        }
        catch (Exception ex)
        {
            Log.Error("Возврат к работе", ex);
            w.ShowError("Не получилось вернуться к работе: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Quit()
    {
        _cts?.Cancel();
        _tray?.Dispose();
        _tray = null;
        _window?.CloseForGood();
        _programs?.CloseForGood();
        _settingsWindow?.CloseForGood();
        _statsWindow?.CloseForGood();
        ExitRestScreens();
        _windowEvents?.Dispose();
        _scheduleTimer?.Stop();
        _updateTimer?.Stop();
        _updater?.Dispose();
        Log.Info("Выход");
        Exit();
    }
}
