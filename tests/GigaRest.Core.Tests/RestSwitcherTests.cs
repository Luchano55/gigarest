using GigaRest.Core.Closing;
using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;

namespace GigaRest.Core.Tests;

public sealed class RestSwitcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeDesktop _d = new();
    private readonly FakeClock _clock = new();
    private readonly SessionStore _store;

    public RestSwitcherTests()
    {
        _store = new SessionStore(Path.Combine(_dir, "session.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private RestSwitcher Switcher() => new(_d, _clock, _store);

    private List<RunningProgram> Scan() =>
        ProgramScanner.Scan(_d.GetProcesses(), _d.GetWindows(), new AppSettings(), selfPid: 1);

    private void AddSecondMonitor() => _d.Monitors.Add(new MonitorInfo
    {
        Id = @"\\.\DISPLAY2", Bounds = new Rect(3840, 0, 6400, 1440), WorkArea = new Rect(3840, 0, 6400, 1400), ScalePercent = 100,
    });

    private void ClosesPolitely(int pid, long window) =>
        _d.OnClose[window] = () => _d.Kill(pid);

    [Fact]
    public async Task FullCycleClosesMovesAndRestores()
    {
        AddSecondMonitor();
        _d.AddProcess("outlook.exe", 10, commandLine: "\"C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE\" /recycle",
            path: @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE", privateBytes: 600 << 20);
        ClosesPolitely(10, _d.AddWindow(10, "Входящие").Handle);
        _d.AddProcess("chrome.exe", 11, privateBytes: 3L << 30);
        ClosesPolitely(11, _d.AddWindow(11, "Работа").Handle);
        _d.AddProcess("telegram.exe", 12);
        var tg = _d.AddWindow(12, "Telegram");
        _d.AddProcess("steam.exe", 13);
        _d.AddWindow(13, "Steam");

        var freedOnClose = 0L;
        _clock.Ticked += () => { if (_d.Dead.Count == 2 && freedOnClose == 0) { freedOnClose = 3L << 30; _d.AvailableMemory += freedOnClose; } };

        var progress = new List<SwitchProgress>();
        var report = await Switcher().GoToRestAsync(Scan(), new Tests.SyncProgress<SwitchProgress>(progress.Add), default);

        Assert.Equal(2, report.Results.Count(r => r.Outcome == CloseOutcome.Closed));
        Assert.Equal(1, report.MovedWindows);
        Assert.Equal(3L << 30, report.FreedBytes);
        Assert.Equal((tg.Handle, @"\\.\DISPLAY2"), _d.MovedTo.Single());
        Assert.Contains(progress, p => p.Text.StartsWith("Закрываем"));
        Assert.Equal(2, progress.Last().Done);
        Assert.Equal("Закрыто: 2 программы, на второй монитор: 1 окно, освободилось 3,0 ГБ.", report.Summary);
        Assert.True(Switcher().IsResting);

        // Перезагрузка: новый экземпляр читает session.json.
        var back = new RestSwitcher(_d, _clock, new SessionStore(Path.Combine(_dir, "session.json"))).ReturnToWork(null, default);

        Assert.Equal("Всё вернули.", back.Summary);
        Assert.Equal(2, _d.Launched.Count);
        var outlook = _d.Launched.Single(l => l.FileName.EndsWith("OUTLOOK.EXE"));
        Assert.Equal(new[] { "/recycle" }, outlook.Arguments);
        Assert.Contains("--restore-last-session", _d.Launched.Single(l => l.FileName.EndsWith("chrome.exe")).Arguments);
        Assert.Equal(tg.Handle, _d.PlacementsSet.Single().Window);
        Assert.False(Switcher().IsResting);
    }

    [Fact]
    public async Task ProgramThatDidNotCloseIsNotRelaunched()
    {
        _d.AddProcess("outlook.exe", 10);
        _d.AddWindow(10, "Не закроюсь");
        _d.AddProcess("chrome.exe", 11);
        ClosesPolitely(11, _d.AddWindow(11, "Работа").Handle);

        var report = await Switcher().GoToRestAsync(Scan(), null, default);
        Assert.Contains(report.Problems, p => p.Contains("Outlook не закрылась"));

        Switcher().ReturnToWork(null, default);
        Assert.Single(_d.Launched);
    }

    [Fact]
    public async Task TrayProgramStillInTrayIsNotRelaunched()
    {
        _d.AddProcess("bitrix24.exe", 10);
        var w = _d.AddWindow(10, "Битрикс24");
        _d.OnClose[w.Handle] = () => _d.RemoveWindow(w.Handle); // спряталась в трей, галочки нет

        var report = await Switcher().GoToRestAsync(Scan(), null, default);
        Assert.Equal(CloseOutcome.InBackground, report.Results.Single().Outcome);

        // Всё ещё в трее — запускать не нужно.
        new RestSwitcher(_d, _clock, _store).ReturnToWork(null, default);
        Assert.Empty(_d.Launched);
    }

    [Fact]
    public async Task TrayProgramClosedByUserDuringRestIsRelaunched()
    {
        _d.AddProcess("bitrix24.exe", 10);
        var w = _d.AddWindow(10, "Битрикс24");
        _d.OnClose[w.Handle] = () => _d.RemoveWindow(w.Handle);
        await Switcher().GoToRestAsync(Scan(), null, default);
        _d.Kill(10); // вечером вышла из трея

        var back = Switcher().ReturnToWork(null, default);
        Assert.Single(_d.Launched);
        Assert.Equal("Всё вернули.", back.Summary);
    }

    [Fact]
    public async Task FailedLaunchIsReported()
    {
        _d.AddProcess("outlook.exe", 10);
        ClosesPolitely(10, _d.AddWindow(10, "Входящие").Handle);
        await Switcher().GoToRestAsync(Scan(), null, default);
        _d.LaunchBehavior = _ => "Файл не найден";

        var back = Switcher().ReturnToWork(null, default);
        Assert.Equal("Всё вернули. Не открылось: Outlook — откройте вручную.", back.Summary);
    }

    [Fact]
    public async Task SessionIsSavedBeforeClosing()
    {
        _d.AddProcess("outlook.exe", 10);
        var w = _d.AddWindow(10, "Входящие");
        RestSession? seenDuringClose = null;
        _d.OnClose[w.Handle] = () => { seenDuringClose = _store.Load(); _d.Kill(10); };

        await Switcher().GoToRestAsync(Scan(), null, default);

        Assert.NotNull(seenDuringClose);
        Assert.Equal("outlook.exe", seenDuringClose!.Closed.Single().Id);
    }

    [Fact]
    public async Task WithoutSecondMonitorAllowedProgramsStay()
    {
        _d.AddProcess("telegram.exe", 12);
        _d.AddWindow(12, "Telegram");

        var report = await Switcher().GoToRestAsync(Scan(), null, default);

        Assert.Equal(0, report.MovedWindows);
        Assert.Empty(_d.MovedTo);
    }

    [Fact]
    public void PicksMonitorToTheRight()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Id = "L", Bounds = new Rect(-1920, 0, 0, 1080) },
            new() { Id = "P", IsPrimary = true, Bounds = new Rect(0, 0, 3840, 2160) },
            new() { Id = "R", Bounds = new Rect(3840, 0, 6400, 1440) },
        };
        Assert.Equal("R", RestSwitcher.PickSecondMonitor(monitors)!.Id);
        Assert.Equal("L", RestSwitcher.PickSecondMonitor(monitors.Take(2).ToList())!.Id);
        Assert.Null(RestSwitcher.PickSecondMonitor(monitors.Skip(1).Take(1).ToList()));
    }

    [Fact]
    public async Task RelaunchedWindowReturnsToItsPlace()
    {
        _d.AddProcess("outlook.exe", 10);
        var w = _d.AddWindow(10, "Входящие");
        _d.Placements[w.Handle] = new WindowPlacementData { NormalBounds = new Rect(10, 20, 1000, 800), Maximized = true, MonitorId = @"\\.\DISPLAY1" };
        ClosesPolitely(10, w.Handle);
        await Switcher().GoToRestAsync(Scan(), null, default);
        var session = _store.Load()!;
        var started = _clock.UtcNow;
        // Программа открылась заново с новым PID.
        _d.AddProcess("outlook.exe", 99, start: started.AddSeconds(1));
        var neu = _d.AddWindow(99, "Входящие");

        await Switcher().PlaceRelaunchedAsync(session.Closed, started, _d.Monitors, default);

        var set = _d.PlacementsSet.Single();
        Assert.Equal(neu.Handle, set.Window);
        Assert.True(set.Placement.Maximized);
    }
}
