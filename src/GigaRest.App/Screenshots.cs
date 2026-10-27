using GigaRest.Core.Closing;
using GigaRest.Core.Demo;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GigaRest.App;

/// <summary>
/// GigaRest.exe --screenshots &lt;папка&gt;: окно на правдоподобных данных во всех состояниях → PNG.
/// Так CI на Windows показывает интерфейс без Битрикс24 и Outlook.
/// </summary>
internal static class Screenshots
{
    public static async Task<bool> RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var (processes, windows, _) = DemoData.Create();
        var programs = ProgramScanner.Scan(processes, windows, new AppSettings(), selfPid: 1);

        var window = new SwitcherWindow();
        window.Present();
        window.SizeToDesign(fitScreen: false);
        window.UseDesignSizeForCapture();

        window.ShowPlan(programs, hasSecondMonitor: true, notice: null);
        await Capture(window.Root, Path.Combine(dir, "1-perehod.png"));

        window.ShowProgress(programs);
        window.UpdateProgress(new SwitchProgress("Закрываем Outlook…", 2, 4));
        await Capture(window.Root, Path.Combine(dir, "2-progress.png"));

        var results = programs.Where(p => p.Action == ProgramAction.Close).Select(p => p.Id switch
        {
            "bitrix24.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Terminated, false),
            "yandexdisk2.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.InBackground, false),
            _ => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Closed, p.Id == "outlook.exe"),
        }).ToList();
        var report = new RestReport(results, 4, (long)(4.3 * (1L << 30)),
            results.Where(r => !r.IsGone).Select(r => r.Message).ToList());
        window.ShowRest(report);
        await Capture(window.Root, Path.Combine(dir, "3-otdyh.png"));

        window.ShowWork(new ReturnReport(["Битрикс24", "Outlook", "Chrome"], ["1С:Предприятие"], 4));
        await Capture(window.Root, Path.Combine(dir, "4-rabota.png"));

        // Окно «Рабочие программы» с ярлыками меню «Пуск».
        var programsWindow = new ProgramsWindow();
        programsWindow.Present();
        programsWindow.SizeToDesign(fitScreen: false);
        programsWindow.UseDesignSizeForCapture();
        var settings = new AppSettings
        {
            Programs = { new ProgramSetting { Id = "1cv8.exe", DisplayName = "1С:Предприятие", Action = ProgramAction.Close } },
        };
        var running = ProgramScanner.Scan(processes, windows, settings, selfPid: 1);
        running.Single(p => p.Id == "bitrix24.exe").AllowTerminate = true;
        programsWindow.SetRows(ProgramCatalog.BuildRows(settings, running, DemoData.Shortcuts));
        await Capture(programsWindow.Root, Path.Combine(dir, "5-programmy.png"));

        // Первая настройка: подсказки уже расставлены.
        programsWindow.SetSetupMode(true);
        var fresh = ProgramScanner.Scan(processes, windows, new AppSettings(), selfPid: 1);
        foreach (var p in fresh) p.Action = ProgramAction.Keep; // как у нового пользователя: ещё ничего не выбрано
        var demoBackground = BackgroundScanner.Find(processes, windows, selfPid: 1);
        programsWindow.SetRows(ProgramCatalog.BuildRows(new AppSettings(), fresh, DemoData.Shortcuts, demoBackground));
        await Capture(programsWindow.Root, Path.Combine(dir, "9-pervaya-nastroyka.png"));
        // Тот же список, прокрученный к фоновым процессам: только они.
        programsWindow.SetRows(ProgramCatalog.BuildRows(new AppSettings(), [], [], demoBackground));
        await Capture(programsWindow.Root, Path.Combine(dir, "10-fonovye.png"));
        // Пробный режим: четыре тестовых окна.
        var testProcesses = TestPrograms.All.Select((t, i) => new GigaRest.Core.Desktop.ProcessInfo
        {
            Pid = 5000 + i, ExeName = t.ExeName, ExePath = $@"C:\GigaRest\{t.ExeName}",
            StartTimeUtc = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc), PrivateBytes = 12L << 20,
        }).ToList();
        var testWindows = TestPrograms.All.Select((t, i) => new GigaRest.Core.Desktop.WindowInfo
        {
            Handle = 0x50000 + i, Pid = 5000 + i, Title = t.Title, IsVisible = true,
        }).ToList();
        var tests = TestPrograms.Pick(ProgramScanner.Scan(testProcesses, testWindows, new AppSettings(), selfPid: 1));
        window.ShowPlan(tests, hasSecondMonitor: true, notice: null, test: true);
        await Capture(window.Root, Path.Combine(dir, "8-proba.png"));

        // Настройки с включённым расписанием.
        var settingsWindow = new SettingsWindow();
        settingsWindow.Present();
        settingsWindow.SizeToDesign(fitScreen: false);
        settingsWindow.UseDesignSizeForCapture();
        settingsWindow.Load(new AppSettings
        {
            ReturnSchedule = new DaySchedule { Enabled = true, Time = "09:00" },
            RestOffer = new DaySchedule { Enabled = true, Time = "19:30" },
        }, autostart: true, hotkeyProblem: null);
        await Capture(settingsWindow.Root, Path.Combine(dir, "6-nastroyki.png"));

        // Предложение по расписанию.
        window.ShowReturnOffer("по будням в 09:00", [("bitrix24.exe", "Битрикс24"), ("chrome.exe", "Chrome"), ("1cv8.exe", "1С:Предприятие")]);
        await Capture(window.Root, Path.Combine(dir, "7-pora.png"));

        // «Тихий» экран на основном мониторе.
        var scenes = GigaRest.Core.Scenes.SceneCatalog.Load(RestScreens.ScenesDir, null);
        var quiet = new QuietWindow(scenes, "builtin:winter.jpg");
        quiet.UseDesignSizeForCapture();
        quiet.StartForCapture();
        quiet.ShowSummary("Закрыто: 3 программы, на второй монитор: 4 окна, освободилось 4,3 ГБ.", hasProblems: false);
        await Task.Delay(1500); // картинка сцены грузится с диска
        await Capture(quiet.Root, Path.Combine(dir, "11-tihiy.png"));
        foreach (var (scene, fx, file) in new[]
        {
            ("builtin:mountains.jpg", GigaRest.Core.Scenes.SceneEffect.Fog, "14-tuman.png"),
            ("builtin:lake.jpg", GigaRest.Core.Scenes.SceneEffect.Lights, "15-ogonki.png"),
            ("builtin:dunes.jpg", GigaRest.Core.Scenes.SceneEffect.Rain, "16-dozhd.png"),
        })
        {
            quiet.ShowSceneForCapture(scene, fx);
            await Task.Delay(2500); // смена сцены — плавная, 1,2 с
            await Capture(quiet.Root, Path.Combine(dir, file));
        }

        // «Играть» и «Продолжить»: игры из Steam, Epic и GOG.
        quiet.ShowSceneForCapture("builtin:winter.jpg", GigaRest.Core.Scenes.SceneEffect.Snow);
        var now = DateTime.UtcNow;
        quiet.SetGames(GigaRest.Core.Games.GameLibraries.Merge(
        [
            new() { Id = "steam:1", Name = "Тихая гавань", Store = "Steam", LastPlayedUtc = now.AddDays(-1) },
            new() { Id = "steam:2", Name = "Северный маршрут", Store = "Steam", LastPlayedUtc = now.AddDays(-4) },
            new() { Id = "epic:3", Name = "Дом у озера", Store = "Epic" },
            new() { Id = "gog:4", Name = "Старый маяк", Store = "GOG", LastPlayedUtc = now.AddDays(-16) },
            new() { Id = "steam:5", Name = "Звёздная ферма", Store = "Steam" },
        ], new Dictionary<string, DateTime>()));
        var films = new List<GigaRest.Core.Media.Film>
        {
            new(@"C:\Users\Роман\Videos\Мой отпуск 2026.mp4", "Мой отпуск 2026", now.AddDays(-2), 2L << 30),
            new(@"C:\Users\Роман\Videos\Горы, таймлапс.mp4", "Горы, таймлапс", now.AddDays(-9), 1L << 30),
            new(@"C:\Users\Роман\Videos\Новый год.mkv", "Новый год", now.AddDays(-40), 3L << 30),
        };
        quiet.SetFilms(films, films[0], new Dictionary<string, double> { [films[0].Path] = 2412 }, "Видео");
        quiet.SetMusicApp(new GigaRest.Core.Media.AppChoice("Яндекс Музыка", @"C:\Яндекс Музыка.exe"));
        quiet.SetPlayers([new GigaRest.Core.Media.AppChoice("VLC", @"C:\Program Files\VideoLAN\VLC\vlc.exe")], null);
        quiet.SetDnd(true);
        quiet.Media = () => Task.FromResult<NowPlaying.Current?>(new("Земфира — Небо Лондона", "Яндекс Музыка", true));
        await Task.Delay(2500);
        quiet.OpenWordForCapture("Играть");
        await Capture(quiet.Root, Path.Combine(dir, "17-igrat.png"));
        quiet.OpenWordForCapture("Продолжить");
        await Capture(quiet.Root, Path.Combine(dir, "18-prodolzhit.png"));
        quiet.OpenWordForCapture("Слушать");
        await Capture(quiet.Root, Path.Combine(dir, "19-slushat.png"));
        quiet.OpenWordForCapture("Смотреть");
        await Capture(quiet.Root, Path.Combine(dir, "20-smotret.png"));
        quiet.OpenWordForCapture("Плеер");
        await Capture(quiet.Root, Path.Combine(dir, "21-pleer.png"));
        quiet.Media = () => Task.FromResult<NowPlaying.Current?>(null);
        quiet.OpenWordForCapture("Слушать");
        await Capture(quiet.Root, Path.Combine(dir, "22-muzyka.png"));

        // Плитки на втором мониторе (на сервере сборки живых копий нет — заглушки).
        var tilesWindow = new TilesWindow();
        tilesWindow.UseDesignSizeForCapture();
        tilesWindow.SetTiles(
        [
            new TileInfo(0, "claude.exe", "Claude", null),
            new TileInfo(0, "telegram.exe", "Telegram", null),
            new TileInfo(0, "zen.exe", "Zen", null),
            new TileInfo(0, "яндекс музыка.exe", "Яндекс Музыка", null),
        ], liveCopies: false);
        tilesWindow.SetStatusForCapture(1, "3 непрочитанных");
        tilesWindow.SetStatusForCapture(2, "Рецепт пирога с яблоками");
        tilesWindow.SetStatusForCapture(3, "♪ Земфира — Небо Лондона");
        await Task.Delay(1500);
        await Capture(tilesWindow.Root, Path.Combine(dir, "12-plitki.png"));

        // Окно хода обновления.
        var update = new UpdateWindow("0.2.1");
        update.Activate();
        update.Report("Скачиваем: 42% (25 из 60 МБ)", 42);
        await Capture(update.Root, Path.Combine(dir, "23-obnovlenie.png"));

        // Последнее закрытое окно завершает программу — поэтому закрываем оба только в конце.
        programsWindow.CloseForGood();
        settingsWindow.CloseForGood();
        quiet.CloseForGood();
        tilesWindow.CloseForGood();
        update.Close();
        window.CloseForGood();
        return true;
    }

    private static async Task Capture(FrameworkElement root, string path)
    {
        // Дать разметке и отрисовке пройти.
        for (var i = 0; i < 3; i++) await Task.Delay(300);
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var pixels = await rtb.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
