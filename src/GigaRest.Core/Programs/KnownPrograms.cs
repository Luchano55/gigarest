using GigaRest.Core.Settings;
using GigaRest.Core.Text;

namespace GigaRest.Core.Programs;

/// <summary>
/// Правило для известной программы: как она себя ведёт при закрытии и что с ней делать по умолчанию.
/// </summary>
public sealed record KnownProgram
{
    /// <summary>Имя exe в нижнем регистре.</summary>
    public required string Exe { get; init; }
    public required string DisplayName { get; init; }
    public ProgramAction DefaultAction { get; init; } = ProgramAction.Keep;
    /// <summary>На крестик только прячет окно в трей.</summary>
    public bool HidesToTray { get; init; }
    /// <summary>Сколько секунд ждать после того, как окна спрятались, прежде чем завершать (если разрешено).</summary>
    public int TrayWaitSeconds { get; init; } = 3;
    /// <summary>Работает без окна (только значок в трее) — показывать в списке, даже если окон нет.</summary>
    public bool TrayOnly { get; init; }
    /// <summary>Подсказка к галочке «можно завершать».</summary>
    public string? TerminateHint { get; init; }
    /// <summary>Chromium: корневой процесс — без --type=.</summary>
    public bool IsChromium { get; init; }
    /// <summary>Аргументы, которые нужно добавить при запуске обратно.</summary>
    public IReadOnlyList<string> RelaunchAddArgs { get; init; } = [];
    /// <summary>Аргументы, которые при запуске обратно не повторяем (например, «тихий» автозапуск).</summary>
    public IReadOnlyList<string> RelaunchDropArgs { get; init; } = [];
}

public static class KnownPrograms
{
    // Имена exe проверять на ПК Романа (docs/manual-tests.md). (?) — не подтверждено.
    public static IReadOnlyList<KnownProgram> All =>
    [
        new()
        {
            Exe = "bitrix24.exe", DisplayName = L.T("Битрикс24", "Bitrix24"), DefaultAction = ProgramAction.Close,
            HidesToTray = true, IsChromium = true,
            TerminateHint = L.T("Можно включить: данные Битрикс24 хранятся на сервере", "Safe to turn on: Bitrix24 keeps its data on the server"),
        },
        new() { Exe = "outlook.exe", DisplayName = "Outlook", DefaultAction = ProgramAction.Close },
        new() { Exe = "olk.exe", DisplayName = L.T("Outlook (новый)", "Outlook (new)"), DefaultAction = ProgramAction.Close, IsChromium = true },
        new()
        {
            Exe = "chrome.exe", DisplayName = "Chrome", DefaultAction = ProgramAction.Close, IsChromium = true,
            HidesToTray = true, TrayWaitSeconds = 10,
            // Вкладки и окна всех профилей откроются как были.
            RelaunchAddArgs = ["--restore-last-session"],
            RelaunchDropArgs = ["--no-startup-window"],
        },
        new()
        {
            Exe = "yandexdisk2.exe", DisplayName = L.T("Яндекс Диск", "Yandex Disk"), DefaultAction = ProgramAction.Close,
            HidesToTray = true, TrayOnly = true, TrayWaitSeconds = 1,
            TerminateHint = L.T("Без галочки останется работать в трее", "Without this tick it keeps running in the tray"),
        },
        new()
        {
            Exe = "telegram.exe", DisplayName = "Telegram", DefaultAction = ProgramAction.SecondMonitor,
            HidesToTray = true, RelaunchDropArgs = ["-autostart"],
        },
        new() { Exe = "zen.exe", DisplayName = "Zen", DefaultAction = ProgramAction.SecondMonitor },
        new() { Exe = "claude.exe", DisplayName = "Claude", DefaultAction = ProgramAction.SecondMonitor, IsChromium = true, HidesToTray = true },
        new()
        {
            // Музыкой управляем из «Слушать» на основном экране — по умолчанию не трогаем.
            Exe = "яндекс музыка.exe", DisplayName = L.T("Яндекс Музыка", "Yandex Music"), DefaultAction = ProgramAction.Keep,
            IsChromium = true, HidesToTray = true,
        },
        new() { Exe = "sharex.exe", DisplayName = "ShareX", HidesToTray = true, TrayWaitSeconds = 2 },
        new() { Exe = "steam.exe", DisplayName = "Steam", HidesToTray = true },
        new() { Exe = "battle.net.exe", DisplayName = "Battle.net", IsChromium = true, HidesToTray = true },
    ];

    /// <summary>
    /// Системные процессы и оболочка — их в списке не показываем и никогда не трогаем.
    /// </summary>
    public static readonly IReadOnlySet<string> System = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "applicationframehost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "searchhost.exe", "searchapp.exe", "textinputhost.exe", "lockapp.exe", "dwm.exe", "csrss.exe",
        "winlogon.exe", "sihost.exe", "taskmgr.exe", "systemsettings.exe", "gamebar.exe", "widgets.exe",
        "phoneexperiencehost.exe", "crossdeviceresume.exe", "gigarest.exe", "conhost.exe",
        "shellhost.exe", "rundll32.exe", "dllhost.exe",
    };

    public static KnownProgram? Find(string exeName) =>
        All.FirstOrDefault(k => string.Equals(k.Exe, exeName, StringComparison.OrdinalIgnoreCase));
}
