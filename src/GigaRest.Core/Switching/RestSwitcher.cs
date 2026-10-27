using GigaRest.Core.Closing;
using GigaRest.Core.Desktop;
using GigaRest.Core.Logging;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Text;

namespace GigaRest.Core.Switching;

public sealed record RestReport(
    IReadOnlyList<CloseResult> Results, int MovedWindows, long FreedBytes, IReadOnlyList<string> Problems)
{
    public string Summary
    {
        get
        {
            var closed = Results.Count(r => r.IsGone);
            var parts = new List<string>();
            if (closed > 0) parts.Add($"Закрыто: {Sizes.Plural(closed, "программа", "программы", "программ")}");
            if (MovedWindows > 0) parts.Add($"на второй монитор: {Sizes.Plural(MovedWindows, "окно", "окна", "окон")}");
            if (FreedBytes > 0) parts.Add($"освободилось {Sizes.Format(FreedBytes)}");
            return parts.Count == 0 ? "Отдых. Ничего закрывать не пришлось." : string.Join(", ", parts) + ".";
        }
    }
}

public sealed record ReturnReport(IReadOnlyList<string> Launched, IReadOnlyList<string> Failed, int RestoredWindows)
{
    public string Summary => Failed.Count == 0
        ? "Всё вернули."
        : $"Всё вернули. Не открылось: {string.Join(", ", Failed)} — откройте вручную.";
}

public sealed record SwitchProgress(string Text, int Done, int Total);

/// <summary>Переход «Работа → Отдых» и обратно. Состояние — в session.json.</summary>
public sealed class RestSwitcher
{
    private readonly IDesktop _desktop;
    private readonly IClock _clock;
    private readonly SessionStore _sessions;

    public RestSwitcher(IDesktop desktop, IClock clock, SessionStore sessions)
    {
        _desktop = desktop;
        _clock = clock;
        _sessions = sessions;
    }

    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Через сколько после закрытия мерить свободную память (ТЗ: 5–10 с).</summary>
    public TimeSpan MemorySettle { get; init; } = TimeSpan.FromSeconds(6);
    public TimeSpan PlacementWait { get; init; } = TimeSpan.FromSeconds(20);

    public bool IsResting => _sessions.Exists;

    /// <summary>Второй монитор: не основной, лучше — справа от основного.</summary>
    /// <summary>Сколько окон переносить на второй монитор (настройка «Плитки»: 0 — выключены, 1–4).</summary>
    public int MaxTiles { get; set; } = TilePlan.Max;

    public static MonitorInfo? PickSecondMonitor(IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        var others = monitors.Where(m => !m.IsPrimary).ToList();
        if (primary is null || others.Count == 0) return null;
        return others.FirstOrDefault(m => m.Bounds.Left >= primary.Bounds.Right) ?? others[0];
    }

    /// <summary>Программу завершили вручную из итога («Завершить») — при возврате её надо открыть.</summary>
    public void MarkClosed(string programId)
    {
        var session = _sessions.Load();
        var entry = session?.Closed.FirstOrDefault(c => c.Id == programId);
        if (session is null || entry is null) return;
        entry.Gone = true;
        _sessions.Save(session);
    }

    public async Task<RestReport> GoToRestAsync(
        IReadOnlyList<RunningProgram> programs, IProgress<SwitchProgress>? progress, CancellationToken ct)
    {
        var problems = new List<string>();
        var availableBefore = _desktop.GetAvailableMemoryBytes();
        var session = _sessions.Load() ?? new RestSession { StartedUtc = _clock.UtcNow };

        var toClose = programs.Where(p => p.Action == ProgramAction.Close).ToList();
        var toMove = TilePlan.Ordered(programs).Take(MaxTiles).ToList(); // в порядке плиток: первая — левая верхняя

        // 1. Запоминаем, как запустить обратно, — до закрытия. Файл переживёт сбой посередине.
        foreach (var p in toClose.Where(p => !p.IsElevated))
        {
            var launch = p.Roots
                .Select(r => RelaunchPlanner.Plan(r, p.Known))
                .OfType<LaunchSpec>()
                .DistinctBy(l => l.Aumid ?? l.FileName + "\0" + string.Join("\0", l.Arguments))
                .ToList();
            if (launch.Count == 0 && !p.NoRelaunch)
                problems.Add($"{p.DisplayName}: не удалось узнать, как её запустить обратно");
            session.Closed.RemoveAll(c => c.Id == p.Id);
            session.Closed.Add(new ClosedProgram
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Launch = launch,
                Relaunch = !p.NoRelaunch,
                Roots = p.Roots.Select(r => r.Key).ToList(),
                Placements = p.Windows.Select(w => _desktop.GetPlacement(w.Handle)).OfType<WindowPlacementData>().ToList(),
            });
        }
        _sessions.Save(session);

        // 2. Второй монитор.
        var moved = 0;
        if (toMove.Count > 0)
        {
            var second = PickSecondMonitor(_desktop.GetMonitors());
            if (second is null)
            {
                Log.Info("Второго монитора нет — разрешённые программы остаются как есть");
            }
            else
            {
                foreach (var p in toMove)
                foreach (var w in p.Windows)
                {
                    var original = _desktop.GetPlacement(w.Handle);
                    if (original is null) continue;
                    var root = p.Processes.FirstOrDefault(x => x.Pid == w.Pid);
                    if (!_desktop.MoveToMonitorMaximized(w.Handle, second))
                    {
                        Log.Warn($"Не удалось перенести окно {p.Id}");
                        continue;
                    }
                    session.Moved.Add(new MovedWindow
                    {
                        ProgramId = p.Id, DisplayName = p.DisplayName, Handle = w.Handle,
                        Pid = w.Pid, ProcessStartUtc = root?.StartTimeUtc ?? default, Original = original,
                    });
                    moved++;
                }
                _sessions.Save(session);
            }
        }

        // 3. Вежливо закрываем.
        var total = toClose.Count;
        var done = 0;
        var pending = toClose.Select(p => p.DisplayName).ToList();
        var asking = new HashSet<string>();
        var gate = new object();
        void OnClose(CloseProgress cp)
        {
            string text;
            lock (gate)
            {
                if (cp.Stage == CloseStage.Done) { done++; pending.Remove(cp.DisplayName); asking.Remove(cp.DisplayName); }
                else if (cp.Stage == CloseStage.Asking) asking.Add(cp.DisplayName);
                else asking.Remove(cp.DisplayName);
                text = asking.Count > 0
                    ? $"{asking.First()} спрашивает — ответьте в её окне"
                    : pending.Count > 0 ? $"Закрываем {pending[0]}…" : "Освобождаем память…";
            }
            progress?.Report(new SwitchProgress(text, done, total));
        }
        progress?.Report(new SwitchProgress(total > 0 ? $"Закрываем {toClose[0].DisplayName}…" : "Готовим отдых…", 0, total));

        var orchestrator = new CloseOrchestrator(_desktop, _clock) { Timeout = CloseTimeout };
        var results = await orchestrator.CloseAllAsync(toClose, new SyncProgress<CloseProgress>(OnClose), ct).ConfigureAwait(false);

        foreach (var r in results)
        {
            var entry = session.Closed.FirstOrDefault(c => c.Id == r.ProgramId);
            if (entry is null) continue;
            if (r.IsGone) entry.Gone = true;
            // В трее или не закрылась — запись храним: если её завершат из итога или она выйдет сама, при возврате запустим.
            else if (r.Outcome is not (CloseOutcome.InBackground or CloseOutcome.StillOpen)) session.Closed.Remove(entry);
            if (!r.IsGone) problems.Add(r.Message);
        }
        _sessions.Save(session);

        // 4. Сколько памяти освободилось на самом деле — через несколько секунд.
        long freed = 0;
        if (results.Any(r => r.IsGone))
        {
            try { await _clock.Delay(MemorySettle, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            freed = Math.Max(0, _desktop.GetAvailableMemoryBytes() - availableBefore);
        }
        Log.Info($"Отдых: закрыто {results.Count(r => r.IsGone)} из {results.Count}, перенесено окон {moved}, освобождено {freed / (1024 * 1024)} МБ");
        return new RestReport(results, moved, freed, problems);
    }

    public ReturnReport ReturnToWork(IProgress<SwitchProgress>? progress, CancellationToken ct)
    {
        var session = _sessions.Load();
        if (session is null)
        {
            _sessions.Clear();
            return new ReturnReport([], [], 0);
        }

        var launched = new List<string>();
        var failed = new List<string>();
        var toLaunch = session.Closed
            .Where(c => c.Relaunch && (c.Gone || c.Roots.Count > 0 && !c.Roots.Any(_desktop.IsAlive)))
            .ToList();
        var startedUtc = _clock.UtcNow;
        var index = 0;
        foreach (var c in toLaunch)
        {
            progress?.Report(new SwitchProgress($"Открываем {c.DisplayName}…", index++, toLaunch.Count));
            var errors = c.Launch.Select(_desktop.Launch).Where(e => e is not null).ToList();
            if (c.Launch.Count == 0 || errors.Count == c.Launch.Count)
            {
                failed.Add(c.DisplayName);
                Log.Warn($"Не открылась {c.Id}: {string.Join("; ", errors)}");
            }
            else
            {
                launched.Add(c.DisplayName);
            }
        }

        // Перенесённые окна — на место.
        var monitors = _desktop.GetMonitors();
        var restored = 0;
        foreach (var m in session.Moved)
        {
            if (!_desktop.IsAlive(new ProcessKey(m.Pid, m.ProcessStartUtc))) continue;
            var monitor = monitors.FirstOrDefault(x => x.Id == m.Original.MonitorId);
            if (_desktop.SetPlacement(m.Handle, m.Original, monitor)) restored++;
        }

        _sessions.Clear();
        progress?.Report(new SwitchProgress("Готово", toLaunch.Count, toLaunch.Count));

        // Окно программы, открытой заново, ставим туда, где оно было (если окно было одно). Не задерживаем итог.
        var placeable = toLaunch.Where(c => c.Placements.Count == 1 && launched.Contains(c.DisplayName)).ToList();
        if (placeable.Count > 0)
            _ = Task.Run(() => PlaceRelaunchedAsync(placeable, startedUtc, monitors, ct), ct);

        Log.Info($"Работа: открыто {launched.Count}, не открылось {failed.Count}, окон на место {restored}");
        return new ReturnReport(launched, failed, restored);
    }

    internal async Task PlaceRelaunchedAsync(
        IReadOnlyList<ClosedProgram> programs, DateTime startedUtc, IReadOnlyList<MonitorInfo> monitors, CancellationToken ct)
    {
        var left = programs.ToList();
        var deadline = _clock.UtcNow + PlacementWait;
        while (left.Count > 0 && _clock.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var processes = _desktop.GetProcesses();
            var windows = _desktop.GetWindows();
            foreach (var c in left.ToList())
            {
                var pids = processes
                    .Where(p => ProgramScanner.ProgramId(p).Equals(c.Id, StringComparison.OrdinalIgnoreCase)
                                && p.StartTimeUtc >= startedUtc.AddSeconds(-2))
                    .Select(p => p.Pid).ToHashSet();
                var window = windows.FirstOrDefault(w => w.IsAppWindow && pids.Contains(w.Pid));
                if (window is null) continue;
                var placement = c.Placements[0];
                _desktop.SetPlacement(window.Handle, placement, monitors.FirstOrDefault(m => m.Id == placement.MonitorId));
                left.Remove(c);
            }
            try { await _clock.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>IProgress без перескока в другой поток — вызов сразу, на месте.</summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _action;
    public SyncProgress(Action<T> action) => _action = action;
    public void Report(T value) => _action(value);
}
