using GigaRest.Core.Desktop;

namespace GigaRest.Core.Demo;

/// <summary>
/// Правдоподобный набор программ для скриншотов в CI (там нет Битрикс24 и Outlook).
/// </summary>
public static class DemoData
{
    private static readonly (string Exe, string Title, long Mb, int Windows)[] Programs =
    [
        ("Bitrix24.exe", "Битрикс24", 820, 1),
        ("OUTLOOK.EXE", "Входящие — Outlook", 640, 1),
        ("chrome.exe", "Задачи — Google Chrome", 3100, 2),
        ("YandexDisk2.exe", "", 160, 0),
        ("Telegram.exe", "Telegram", 380, 1),
        ("zen.exe", "YouTube — Zen", 900, 1),
        ("claude.exe", "Claude", 450, 1),
        ("Яндекс Музыка.exe", "Яндекс Музыка", 300, 1),
        ("steam.exe", "Steam", 310, 1),
    ];

    /// <summary>Ярлыки меню «Пуск» для скриншота окна «Рабочие программы».</summary>
    public static readonly IReadOnlyList<Programs.ShortcutInfo> Shortcuts =
    [
        new("Яндекс Браузер", @"C:\Users\User\AppData\Local\Yandex\YandexBrowser\Application\browser.exe"),
        new("Visual Studio Code", @"C:\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe"),
        new("Блокнот", @"C:\Windows\System32\notepad.exe"),
        new("Zoom", @"C:\Users\User\AppData\Roaming\Zoom\bin\Zoom.exe"),
        new("Удалить Zoom", @"C:\Users\User\AppData\Roaming\Zoom\uninstall\Installer.exe"),
        new("Battle.net", @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe"),
    ];

    public static (List<ProcessInfo> Processes, List<WindowInfo> Windows, List<MonitorInfo> Monitors) Create()
    {
        var processes = new List<ProcessInfo>();
        var windows = new List<WindowInfo>();
        var pid = 1000;
        long handle = 0x10000;
        foreach (var (exe, title, mb, count) in Programs)
        {
            pid += 10;
            processes.Add(new ProcessInfo
            {
                Pid = pid, ExeName = exe, ExePath = $@"C:\Program Files\{exe[..^4]}\{exe}",
                StartTimeUtc = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc), PrivateBytes = mb << 20,
            });
            for (var i = 0; i < count; i++)
                windows.Add(new WindowInfo { Handle = handle++, Pid = pid, Title = title, IsVisible = true });
        }
        // Фоновые процессы без окон — для раздела «Фоновые процессы».
        foreach (var (exe, folder, args, mb) in new[]
        {
            ("OneDrive.exe", @"C:\Users\User\AppData\Local\Microsoft\OneDrive", "/background", 210L),
            ("Updater.exe", @"C:\Users\User\AppData\Local\Updater", "--autostart --minimized", 140L),
            ("AnyDesk.exe", @"C:\Program Files (x86)\AnyDesk", "--tray", 90L),
        })
        {
            pid += 10;
            var path = folder + "\\" + exe;
            processes.Add(new ProcessInfo
            {
                Pid = pid, ExeName = exe, ExePath = path, CommandLine = $"\"{path}\" {args}",
                StartTimeUtc = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc), PrivateBytes = mb << 20,
            });
        }

        var monitors = new List<MonitorInfo>
        {
            new() { Id = @"\\.\DISPLAY1", IsPrimary = true, Bounds = new Rect(0, 0, 3840, 2160), WorkArea = new Rect(0, 0, 3840, 2100), ScalePercent = 150 },
            new() { Id = @"\\.\DISPLAY2", Bounds = new Rect(3840, 0, 6400, 1440), WorkArea = new Rect(3840, 0, 6400, 1400), ScalePercent = 100 },
        };
        return (processes, windows, monitors);
    }
}
