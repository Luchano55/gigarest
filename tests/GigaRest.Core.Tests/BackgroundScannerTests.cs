using GigaRest.Core.Closing;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;

namespace GigaRest.Core.Tests;

public class BackgroundScannerTests
{
    private static FakeDesktop Computer()
    {
        var d = new FakeDesktop();
        d.AddProcess("explorer.exe", 2, path: @"C:\Windows\explorer.exe");
        // Открытая программа и её помощник — не фоновые.
        d.AddProcess("chrome.exe", 10, parent: 2);
        d.AddWindow(10, "Chrome");
        d.AddProcess("crashpad_handler.exe", 11, parent: 10);
        // Фоновая программа из автозагрузки с помощником.
        d.AddProcess("updater.exe", 20, parent: 2, privateBytes: 150 << 20,
            commandLine: "\"C:\\Users\\Роман\\AppData\\Local\\Updater\\updater.exe\" --autostart --minimized",
            path: @"C:\Users\Роман\AppData\Local\Updater\updater.exe");
        d.AddProcess("updater-helper.exe", 21, parent: 20, privateBytes: 50 << 20,
            path: @"C:\Users\Роман\AppData\Local\Updater\updater-helper.exe");
        // Системное, драйверы, антивирус, администратор, без пути — не трогаем.
        d.AddProcess("ctfmon.exe", 30, parent: 2, path: @"C:\Windows\System32\ctfmon.exe");
        d.AddProcess("RtkAudUService64.exe", 31, parent: 2, path: @"C:\Program Files\Realtek\Audio\RtkAudUService64.exe");
        d.AddProcess("NVIDIA Share.exe", 32, parent: 2, path: @"C:\Program Files\NVIDIA Corporation\NVIDIA app\NVIDIA Share.exe");
        d.AddProcess("avp.exe", 33, parent: 2, path: @"C:\Program Files\Kaspersky Lab\avp.exe");
        d.AddProcess("admintool.exe", 34, parent: 2, elevated: true);
        d.Processes.Add(new Desktop.ProcessInfo { Pid = 35, ParentPid = 2, ExeName = "mystery.exe" });
        return d;
    }

    [Fact]
    public void FindsOnlySafeRelaunchableBackgroundRoots()
    {
        var d = Computer();
        var found = BackgroundScanner.Find(d.GetProcesses(), d.GetWindows(), selfPid: 1);

        var updater = Assert.Single(found);
        Assert.Equal("updater.exe", updater.Id);
        Assert.True(updater.IsBackground);
        Assert.Equal(200L << 20, updater.PrivateBytes); // с помощником
        Assert.Equal("фоновый процесс", updater.Note);
    }

    [Fact]
    public void RowsShowBackgroundWithRelaunchInfoAndSaveAsAllowed()
    {
        var d = Computer();
        var settings = new AppSettings();
        var running = ProgramScanner.Scan(d.GetProcesses(), d.GetWindows(), settings, 1);
        var bg = BackgroundScanner.Find(d.GetProcesses(), d.GetWindows(), 1);

        var rows = ProgramCatalog.BuildRows(settings, running, [], bg);
        var row = rows.Single(r => r.Id == "updater.exe");
        Assert.True(row.IsBackground);
        Assert.Equal(ProgramAction.Keep, row.Action);
        Assert.Contains("--autostart --minimized", row.RelaunchInfo);

        row.Action = ProgramAction.Close;
        ProgramCatalog.Save(settings, rows);
        var saved = settings.Find("updater.exe")!;
        Assert.Equal(ProgramAction.Close, saved.Action);
        Assert.True(saved.AllowTerminate);
    }

    [Fact]
    public async Task ChosenBackgroundProcessIsStoppedAtOnceAndStartedWithSameArguments()
    {
        var d = Computer();
        var clock = new FakeClock();
        var settings = new AppSettings { Programs = { new ProgramSetting { Id = "updater.exe", Action = ProgramAction.Close, AllowTerminate = true } } };
        var dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", Guid.NewGuid().ToString("N"));
        var switcher = new RestSwitcher(d, clock, new SessionStore(Path.Combine(dir, "session.json")));

        var programs = ProgramScanner.Scan(d.GetProcesses(), d.GetWindows(), settings, 1);
        var updater = programs.Single(p => p.Id == "updater.exe");
        Assert.True(updater.IsBackground);
        var chrome = programs.Single(p => p.Id == "chrome.exe");
        chrome.Action = ProgramAction.Keep; // проверяем только фоновый
        var start = clock.UtcNow;

        var report = await switcher.GoToRestAsync(programs, null, default);

        Assert.Equal(CloseOutcome.Terminated, report.Results.Single().Outcome);
        Assert.True(clock.UtcNow - start < TimeSpan.FromSeconds(8)); // без лишнего ожидания (6 с — замер памяти)
        Assert.DoesNotContain(d.Terminated, k => k.Pid == 10);       // Chrome не тронут

        switcher.ReturnToWork(null, default);
        var launched = Assert.Single(d.Launched);
        Assert.Equal(@"C:\Users\Роман\AppData\Local\Updater\updater.exe", launched.FileName);
        Assert.Equal(new[] { "--autostart", "--minimized" }, launched.Arguments);
        Directory.Delete(dir, true);
    }
}

public class BackgroundRelaunchedBySelfTests
{
    [Fact]
    public void ProcessStartedByGigaRestIsStillFound()
    {
        var d = new FakeDesktop();
        d.AddProcess("gigarest.exe", 1, path: @"C:\GigaRest\GigaRest.exe");
        d.AddWindow(1, "GigaRest"); // окно GigaRest открыто
        d.AddProcess("updater.exe", 20, parent: 1, path: @"C:\Users\Роман\AppData\Local\Updater\updater.exe");
        var found = BackgroundScanner.Find(d.GetProcesses(), d.GetWindows(), selfPid: 1);
        Assert.Equal("updater.exe", Assert.Single(found).Id);
    }
}
