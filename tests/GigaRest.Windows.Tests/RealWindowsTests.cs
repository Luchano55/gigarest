using GigaRest.Core.Closing;
using GigaRest.Core.Desktop;
using GigaRest.Core.Input;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;

namespace GigaRest.Windows.Tests;

public class RealWindowsTests
{
    private static CloseOrchestrator Orchestrator(int timeoutSeconds = 10) => new(Dummy.Desktop, SystemClock.Instance)
    {
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        ExitGrace = TimeSpan.FromSeconds(2),
    };

    [WindowsFact]
    public async Task ClosesEveryWindowPolitely()
    {
        using var d = new Dummy("закрытие.exe", ["--windows", "3"]);
        d.WaitWindows(3);
        var program = d.Program();
        Assert.Equal(3, program.Windows.Count);

        var result = await Orchestrator().CloseAsync(program, null, default);

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.True(d.Process.WaitForExit(5000));
        Assert.Equal(0, d.Process.ExitCode); // вышла сама, не завершена
    }

    [WindowsFact]
    public async Task WaitsForSaveQuestionAndNeverKills()
    {
        using var d = new Dummy("вопрос.exe", ["--ask"]);
        d.WaitWindows();
        var asked = new TaskCompletionSource();
        var progress = new SyncProgress<CloseProgress>(p => { if (p.Stage == CloseStage.Asking) asked.TrySetResult(); });

        // Таймаут 3 с, а пользователь «думает» 6 с: пока открыт вопрос, время не идёт.
        var closing = Orchestrator(timeoutSeconds: 3).CloseAsync(d.Program(allowTerminate: true), progress, default);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var dialog = Dummy.WaitWindows(w => w.Pid == d.Process.Id && w.IsDialog).Single();
        await Task.Delay(6000);
        Assert.False(closing.IsCompleted);
        Dummy.PostMessageW((nint)dialog.Handle, Dummy.WM_COMMAND, Dummy.IDNO, 0); // «Нет» — не сохранять

        var result = await closing.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.True(result.WasAsked);
        Assert.Equal(0, d.Process.ExitCode);
    }

    [WindowsFact]
    public async Task CancelledSaveQuestionLeavesProgramRunning()
    {
        using var d = new Dummy("отмена.exe", ["--ask"]);
        d.WaitWindows();
        var asked = new TaskCompletionSource();
        var progress = new SyncProgress<CloseProgress>(p => { if (p.Stage == CloseStage.Asking) asked.TrySetResult(); });

        var closing = Orchestrator(timeoutSeconds: 3).CloseAsync(d.Program(allowTerminate: true), progress, default);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var dialog = Dummy.WaitWindows(w => w.Pid == d.Process.Id && w.IsDialog).Single();
        Dummy.PostMessageW((nint)dialog.Handle, Dummy.WM_COMMAND, Dummy.IDCANCEL, 0);

        var result = await closing.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(CloseOutcome.StillOpen, result.Outcome);
        Assert.False(d.Process.HasExited);
    }

    [WindowsFact]
    public async Task TrayProgramStaysWithoutPermission()
    {
        using var d = new Dummy("трей.exe", ["--tray"]);
        d.WaitWindows();

        var result = await Orchestrator(timeoutSeconds: 3).CloseAsync(d.Program(), null, default);

        Assert.Equal(CloseOutcome.InBackground, result.Outcome);
        Assert.False(d.Process.HasExited);
    }

    [WindowsFact]
    public async Task TrayProgramTerminatedWithPermission()
    {
        using var d = new Dummy("трей2.exe", ["--tray"]);
        d.WaitWindows();

        var result = await Orchestrator(timeoutSeconds: 3).CloseAsync(d.Program(allowTerminate: true), null, default);

        Assert.Equal(CloseOutcome.Terminated, result.Outcome);
        Assert.True(d.Process.WaitForExit(5000));
    }

    [WindowsFact]
    public void ReadsCommandLineWithCyrillicAndSpaces()
    {
        string[] args = ["--title", "Окно с пробелами", @"D:\Мои фото\тест 1.heic", "a\"b", @"C:\dir\"];
        using var d = new Dummy("аргументы.exe", args);
        d.WaitWindows();

        var p = Dummy.Desktop.GetProcesses().Single(x => x.Pid == d.Process.Id);
        Assert.Equal(d.ExePath, p.ExePath, ignoreCase: true);
        Assert.Equal(args, CommandLine.Split(p.CommandLine).Skip(1));
        Assert.NotEqual(default, p.StartTimeUtc);
        Assert.True(p.PrivateBytes > 0);
        Assert.False(p.IsElevated);
        Assert.True(Dummy.Desktop.IsAlive(p.Key));
        Assert.False(Dummy.Desktop.IsAlive(p.Key with { StartTimeUtc = p.StartTimeUtc.AddMinutes(-1) }));
    }

    [WindowsFact]
    public async Task FullCycleRelaunchesFromCyrillicFolderWithSameArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "GigaRest тесты", "Мои программы " + Guid.NewGuid().ToString("N")[..6]);
        var marker = Path.Combine(root, "Мои фото", "метка 1.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        string[] args = ["--marker", marker, "--title", "Работа тест", @"D:\Мои фото\тест 1.heic"];
        using var d = new Dummy("тест 1.exe", args, Path.Combine(root, "тест 1"));
        d.WaitWindows();
        Assert.Equal(args, File.ReadAllLines(marker));
        File.Delete(marker);

        var store = new SessionStore(Path.Combine(root, "session.json"));
        var switcher = new RestSwitcher(Dummy.Desktop, SystemClock.Instance, store) { MemorySettle = TimeSpan.FromSeconds(1) };
        var rest = await switcher.GoToRestAsync([d.Program()], null, default);
        Assert.Equal(CloseOutcome.Closed, rest.Results.Single().Outcome);
        Assert.True(d.Process.WaitForExit(5000));
        Assert.True(store.Exists);

        var back = switcher.ReturnToWork(null, default);
        Assert.Equal("Всё вернули.", back.Summary);
        for (var i = 0; i < 100 && !File.Exists(marker); i++) await Task.Delay(100);
        Assert.Equal(args, File.ReadAllLines(marker));
    }

    [WindowsFact]
    public void PlacementRoundTrip()
    {
        using var d = new Dummy("место.exe", []);
        var w = d.WaitWindows().Single();
        var desktop = Dummy.Desktop;
        var monitors = desktop.GetMonitors();
        Assert.Single(monitors, m => m.IsPrimary);

        var original = desktop.GetPlacement(w.Handle)!;
        var moved = original with { NormalBounds = new Rect(200, 150, 700, 550), Maximized = false };
        Assert.True(desktop.SetPlacement(w.Handle, moved, monitors[0]));
        Assert.Equal(moved.NormalBounds, desktop.GetPlacement(w.Handle)!.NormalBounds);

        Assert.True(desktop.MoveToMonitorMaximized(w.Handle, monitors.First(m => m.IsPrimary)));
        Assert.True(desktop.GetPlacement(w.Handle)!.Maximized);
    }

    [WindowsFact]
    public void BusyHotkeyIsReported()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+F11", out var hotkey));
        using var first = new TrayAndHotkeys();
        using var second = new TrayAndHotkeys();
        Assert.Null(first.RegisterHotkey(1, hotkey));
        Assert.Contains("уже занята", second.RegisterHotkey(1, hotkey));
    }

    [WindowsFact]
    public void FreeMemoryIsKnown()
    {
        Assert.True(Dummy.Desktop.GetAvailableMemoryBytes() > 100L << 20);
    }
}

public sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}

public class StartMenuTests
{
    [WindowsFact]
    public void ReadsStartMenuShortcuts()
    {
        var shortcuts = StartMenu.ReadShortcuts();
        Assert.NotEmpty(shortcuts);
        Assert.Contains(shortcuts, s => s.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s.TargetPath));
        Assert.NotEmpty(GigaRest.Core.Programs.ProgramCatalog.CleanShortcuts(shortcuts));
    }
}

public class AutostartTests
{
    [WindowsFact]
    public void TurnsOnAndOffInCurrentUserRun()
    {
        var exe = @"C:\Мои программы\GigaRest тест\GigaRest.exe";
        try
        {
            Autostart.Set(exe, true);
            Assert.True(Autostart.IsEnabled(exe));
            Assert.False(Autostart.IsEnabled(@"C:\другая\GigaRest.exe"));
        }
        finally
        {
            Autostart.Set(exe, false);
        }
        Assert.False(Autostart.IsEnabled(exe));
    }
}

public class BackgroundTests
{
    [WindowsFact]
    public async Task BackgroundProcessIsFoundStoppedAndStartedWithSameArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "GigaRest тесты", "Фон " + Guid.NewGuid().ToString("N")[..6]);
        var marker = Path.Combine(root, "метка фона.txt");
        Directory.CreateDirectory(root);
        string[] args = ["--windows", "0", "--marker", marker, "--title", "фон без окон"];
        using var d = new Dummy("фоновая программа.exe", args, Path.Combine(root, "программа"));
        for (var i = 0; i < 50 && !File.Exists(marker); i++) await Task.Delay(100);
        Assert.Equal(args, File.ReadAllLines(marker));
        File.Delete(marker);

        var desktop = Dummy.Desktop;
        var found = GigaRest.Core.Programs.BackgroundScanner.Find(desktop.GetProcesses(), desktop.GetWindows(), Environment.ProcessId);
        Assert.Contains(found, p => p.Id == "фоновая программа.exe");

        var settings = new AppSettings { Programs = { new ProgramSetting { Id = "фоновая программа.exe", Action = ProgramAction.Close, AllowTerminate = true } } };
        var programs = ProgramScanner.Scan(desktop.GetProcesses(), desktop.GetWindows(), settings, Environment.ProcessId)
            .Where(p => p.Id == "фоновая программа.exe").ToList();
        var switcher = new RestSwitcher(desktop, SystemClock.Instance, new SessionStore(Path.Combine(root, "session.json")))
        {
            MemorySettle = TimeSpan.FromSeconds(1),
        };

        var rest = await switcher.GoToRestAsync(programs, null, default);
        Assert.Equal(CloseOutcome.Terminated, rest.Results.Single().Outcome);
        Assert.True(d.Process.WaitForExit(5000));

        switcher.ReturnToWork(null, default);
        for (var i = 0; i < 100 && !File.Exists(marker); i++) await Task.Delay(100);
        Assert.Equal(args, File.ReadAllLines(marker));
    }
}
