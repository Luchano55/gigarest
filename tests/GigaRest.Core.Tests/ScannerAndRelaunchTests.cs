using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public class ScannerAndRelaunchTests
{
    private readonly FakeDesktop _d = new();

    private List<RunningProgram> Scan(AppSettings? s = null) =>
        ProgramScanner.Scan(_d.GetProcesses(), _d.GetWindows(), s ?? new AppSettings(), selfPid: 1);

    [Fact]
    public void GroupsChromeProcessesUnderBrowserRoot()
    {
        _d.AddProcess("explorer.exe", 2);
        _d.AddProcess("chrome.exe", 10, parent: 2, commandLine: "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"", privateBytes: 300 << 20);
        _d.AddProcess("chrome.exe", 11, parent: 10, commandLine: "chrome.exe --type=gpu-process", privateBytes: 200 << 20);
        _d.AddProcess("chrome.exe", 12, parent: 10, commandLine: "chrome.exe --type=renderer", privateBytes: 500 << 20);
        _d.AddWindow(10, "Битрикс — Google Chrome");
        _d.AddWindow(2, "Проводник");

        var chrome = Assert.Single(Scan());
        Assert.Equal("chrome.exe", chrome.Id);
        Assert.Equal("Chrome", chrome.DisplayName);
        Assert.Equal(10, Assert.Single(chrome.Roots).Pid);
        Assert.Equal(1000L << 20, chrome.PrivateBytes);
        Assert.Equal(ProgramAction.Close, chrome.Action);
        Assert.Equal("1 окно", chrome.Note);
    }

    [Fact]
    public void ReusedParentPidIsNotParent()
    {
        // PID 10 — новый процесс, стартовал после «ребёнка»: это не родитель.
        _d.AddProcess("app.exe", 10, start: new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        _d.AddProcess("app.exe", 20, parent: 10, start: new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc));
        _d.AddWindow(20, "Окно");

        var app = Assert.Single(Scan());
        Assert.Equal(20, app.Roots.Single().Pid);
    }

    [Fact]
    public void SkipsSystemSelfHiddenAndToolWindows()
    {
        _d.AddProcess("gigarest.exe", 1);
        _d.AddWindow(1, "GigaRest");
        _d.AddProcess("textinputhost.exe", 3);
        _d.AddWindow(3, "Ввод");
        _d.AddProcess("tool.exe", 4);
        _d.Windows.Add(new WindowInfo { Handle = 9001, Pid = 4, Title = "Панель", IsVisible = true, IsToolWindow = true });
        _d.Windows.Add(new WindowInfo { Handle = 9002, Pid = 4, Title = "Скрытое", IsVisible = false });
        _d.Windows.Add(new WindowInfo { Handle = 9003, Pid = 4, Title = "На другом столе", IsVisible = true, IsCloaked = true });

        Assert.Empty(Scan());
    }

    [Fact]
    public void StoreAppsAreKeyedByAumidAndTrayOnlyProgramsShown()
    {
        _d.AddProcess("olk.exe", 10, aumid: "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows");
        _d.AddWindow(10, "Почта");
        _d.AddProcess("YandexDisk2.exe", 20);

        var list = Scan();
        Assert.Contains(list, p => p.Id == "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows");
        var disk = Assert.Single(list, p => p.Id == "yandexdisk2.exe");
        Assert.Equal("в трее", disk.Note);
        Assert.Equal("Яндекс Диск", disk.DisplayName);
    }

    [Fact]
    public void SettingsOverrideDefaultsAndNewProgramsAreRememberedAsKeep()
    {
        _d.AddProcess("chrome.exe", 10);
        _d.AddWindow(10, "Chrome");
        _d.AddProcess("1cv8.exe", 11);
        _d.AddWindow(11, "1С");
        var settings = new AppSettings
        {
            Programs = { new ProgramSetting { Id = "CHROME.EXE", DisplayName = "Chrome — работа", Action = ProgramAction.Keep, AllowTerminate = true } },
        };

        var list = Scan(settings);
        var chrome = list.Single(p => p.Id == "chrome.exe");
        Assert.Equal("Chrome — работа", chrome.DisplayName);
        Assert.Equal(ProgramAction.Keep, chrome.Action);
        Assert.True(chrome.AllowTerminate);
        Assert.Equal(ProgramAction.Keep, list.Single(p => p.Id == "1cv8.exe").Action);

        Assert.True(ProgramScanner.RememberNew(settings, list));
        Assert.Equal(2, settings.Programs.Count);
        Assert.False(ProgramScanner.RememberNew(settings, list));
    }

    [Fact]
    public void ListIsOrderedCloseFirstThenSecondMonitorThenKeep()
    {
        _d.AddProcess("steam.exe", 10); _d.AddWindow(10, "Steam");
        _d.AddProcess("telegram.exe", 11); _d.AddWindow(11, "Telegram");
        _d.AddProcess("outlook.exe", 12); _d.AddWindow(12, "Outlook");

        Assert.Equal(new[] { "outlook.exe", "telegram.exe", "steam.exe" }, Scan().Select(p => p.Id));
    }

    [Fact]
    public void RelaunchKeepsArgumentsWithSpacesAndCyrillic()
    {
        var root = _d.AddProcess("прог.exe", 10,
            commandLine: "\"C:\\Мои программы\\тест 1\\прог.exe\" \"D:\\Мои фото\\тест 1.heic\" --flag",
            path: @"C:\Мои программы\тест 1\прог.exe");

        var spec = RelaunchPlanner.Plan(root, null)!;
        Assert.Equal(@"C:\Мои программы\тест 1\прог.exe", spec.FileName);
        Assert.Equal(new[] { @"D:\Мои фото\тест 1.heic", "--flag" }, spec.Arguments);
        Assert.Equal(@"C:\Мои программы\тест 1", spec.WorkingDirectory);
    }

    [Fact]
    public void ChromeRestoresSessionAndDropsBackgroundFlag()
    {
        var root = _d.AddProcess("chrome.exe", 10, commandLine: "chrome.exe --no-startup-window --profile-directory=Default");
        var spec = RelaunchPlanner.Plan(root, KnownPrograms.Find("chrome.exe"))!;
        Assert.Equal(new[] { "--profile-directory=Default", "--restore-last-session" }, spec.Arguments);
    }

    [Fact]
    public void StoreAppRelaunchesByAumid()
    {
        var root = _d.AddProcess("olk.exe", 10, aumid: "Pkg!App");
        var spec = RelaunchPlanner.Plan(root, null)!;
        Assert.Equal("Pkg!App", spec.Aumid);
        Assert.Equal("", spec.FileName);
    }

    [Theory]
    [InlineData(@"C:\app.exe", @"C:\")]
    [InlineData(@"C:\Program Files\X\x.exe", @"C:\Program Files\X")]
    [InlineData("x.exe", null)]
    public void DirectoryOfWindowsPath(string path, string? expected) =>
        Assert.Equal(expected, RelaunchPlanner.DirectoryOf(path));
}
