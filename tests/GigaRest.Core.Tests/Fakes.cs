using GigaRest.Core.Desktop;

namespace GigaRest.Core.Tests;

/// <summary>Время, которое идёт только когда программа ждёт: тесты на 30 с проходят мгновенно.</summary>
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);
    public event Action? Ticked;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UtcNow += delay;
        Ticked?.Invoke();
        return Task.CompletedTask;
    }
}

/// <summary>Поддельная Windows: процессы, окна и их реакция на WM_CLOSE задаются в тесте.</summary>
public sealed class FakeDesktop : IDesktop
{
    private long _nextHandle = 100;
    public List<ProcessInfo> Processes { get; } = [];
    public List<WindowInfo> Windows { get; } = [];
    public List<MonitorInfo> Monitors { get; } =
    [
        new() { Id = @"\\.\DISPLAY1", IsPrimary = true, Bounds = new Rect(0, 0, 3840, 2160), WorkArea = new Rect(0, 0, 3840, 2100), ScalePercent = 150 },
    ];
    public HashSet<int> Dead { get; } = [];
    public List<long> ClosedPosts { get; } = [];
    public List<long> Fronted { get; } = [];
    public List<ProcessKey> Terminated { get; } = [];
    public List<LaunchSpec> Launched { get; } = [];
    public Dictionary<long, WindowPlacementData> Placements { get; } = [];
    public List<(long Window, string MonitorId)> MovedTo { get; } = [];
    public List<(long Window, WindowPlacementData Placement)> PlacementsSet { get; } = [];
    public long AvailableMemory { get; set; } = 8L << 30;
    public Func<LaunchSpec, string?> LaunchBehavior { get; set; } = _ => null;

    /// <summary>Что делает окно на WM_CLOSE.</summary>
    public Dictionary<long, Action> OnClose { get; } = [];

    public int CurrentProcessId => 1;

    public ProcessInfo AddProcess(string exe, int pid, int parent = 0, string? commandLine = null, long privateBytes = 100 << 20,
        string? path = null, string? aumid = null, bool elevated = false, DateTime? start = null)
    {
        var p = new ProcessInfo
        {
            Pid = pid, ParentPid = parent, ExeName = exe, ExePath = path ?? $@"C:\Program Files\{exe[..^4]}\{exe}",
            CommandLine = commandLine ?? $"\"C:\\Program Files\\{exe[..^4]}\\{exe}\"",
            PrivateBytes = privateBytes, Aumid = aumid, IsElevated = elevated,
            StartTimeUtc = start ?? new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc).AddSeconds(pid),
        };
        Processes.Add(p);
        return p;
    }

    public WindowInfo AddWindow(int pid, string title = "Окно", long owner = 0, string cls = "AppWindow", bool enabled = true)
    {
        var w = new WindowInfo
        {
            Handle = _nextHandle++, Pid = pid, Title = title, OwnerHandle = owner, ClassName = cls,
            IsVisible = true, IsEnabled = enabled, Bounds = new Rect(100, 100, 900, 700),
        };
        Windows.Add(w);
        Placements[w.Handle] = new WindowPlacementData { NormalBounds = w.Bounds, MonitorId = @"\\.\DISPLAY1" };
        return w;
    }

    public void RemoveWindow(long handle) => Windows.RemoveAll(w => w.Handle == handle);
    public void Kill(int pid)
    {
        Dead.Add(pid);
        Windows.RemoveAll(w => w.Pid == pid);
    }
    public void SetEnabled(long handle, bool enabled)
    {
        var i = Windows.FindIndex(w => w.Handle == handle);
        Windows[i] = Windows[i] with { IsEnabled = enabled };
    }

    public IReadOnlyList<ProcessInfo> GetProcesses() => Processes.Where(p => !Dead.Contains(p.Pid)).ToList();
    public IReadOnlyList<WindowInfo> GetWindows() => Windows.ToList();
    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public bool PostClose(long window)
    {
        ClosedPosts.Add(window);
        if (OnClose.TryGetValue(window, out var action)) action();
        return true;
    }

    public void BringToFront(long window) => Fronted.Add(window);

    public bool Terminate(ProcessKey process)
    {
        Terminated.Add(process);
        Kill(process.Pid);
        return true;
    }

    public bool IsAlive(ProcessKey process) =>
        !Dead.Contains(process.Pid) && Processes.Any(p => p.Key == process);

    public long GetAvailableMemoryBytes() => AvailableMemory;
    public WindowPlacementData? GetPlacement(long window) => Placements.GetValueOrDefault(window);

    public bool SetPlacement(long window, WindowPlacementData placement, MonitorInfo? monitor)
    {
        PlacementsSet.Add((window, placement));
        return true;
    }

    public bool MoveToMonitorMaximized(long window, MonitorInfo monitor)
    {
        MovedTo.Add((window, monitor.Id));
        return true;
    }

    public string? Launch(LaunchSpec spec)
    {
        Launched.Add(spec);
        return LaunchBehavior(spec);
    }
}
