using GigaRest.Core.Desktop;
using GigaRest.Core.Logging;
using GigaRest.Core.Programs;

namespace GigaRest.Core.Closing;

/// <summary>
/// Вежливое закрытие программ (ТЗ, раздел 4):
/// WM_CLOSE каждому окну → ждём; вопрос «Сохранить?» — окно вперёд, таймер стоит;
/// окна спрятались в трей — завершаем только по галочке и только если ни разу не спрашивала.
/// </summary>
public sealed class CloseOrchestrator
{
    private readonly IDesktop _desktop;
    private readonly IClock _clock;

    public CloseOrchestrator(IDesktop desktop, IClock clock)
    {
        _desktop = desktop;
        _clock = clock;
    }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Для программ без правила: окна спрятались, но процесс жив (Outlook долго выходит).</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Закрыть все программы параллельно.</summary>
    public async Task<IReadOnlyList<CloseResult>> CloseAllAsync(
        IReadOnlyList<RunningProgram> programs, IProgress<CloseProgress>? progress, CancellationToken ct)
    {
        var tasks = programs.Select(p => CloseAsync(p, progress, ct)).ToList();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<CloseResult> CloseAsync(RunningProgram program, IProgress<CloseProgress>? progress, CancellationToken ct)
    {
        var name = program.DisplayName;
        void Report(CloseStage stage, CloseOutcome? outcome = null) =>
            progress?.Report(new CloseProgress(program.Id, name, stage, outcome));
        CloseResult Finish(CloseOutcome outcome, bool asked)
        {
            Log.Info($"Закрытие {program.Id}: {outcome}{(asked ? " (спрашивала)" : "")}");
            Report(CloseStage.Done, outcome);
            return new CloseResult(program.Id, name, outcome, asked);
        }

        if (program.IsElevated)
            return Finish(CloseOutcome.Elevated, false);

        Report(CloseStage.Closing);
        var roots = program.Roots.Select(r => r.Key).ToList();
        var sent = new HashSet<long>();
        var everAsked = false;
        var elapsed = TimeSpan.Zero;          // время без вопросов
        TimeSpan? hiddenFor = null;           // сколько окон нет, а процесс жив
        long? lastDialog = null;
        var last = _clock.UtcNow;
        var hideWait = program.Known is { HidesToTray: true } known
            ? TimeSpan.FromSeconds(known.TrayWaitSeconds)
            : ExitGrace;

        while (true)
        {
            if (ct.IsCancellationRequested)
                return Finish(CloseOutcome.Cancelled, everAsked);

            var alive = roots.Where(_desktop.IsAlive).ToList();
            if (alive.Count == 0)
                return Finish(CloseOutcome.Closed, everAsked);

            var pids = AlivePids(program, alive);
            var windows = _desktop.GetWindows().Where(w => pids.Contains(w.Pid)).ToList();
            var dialog = windows.FirstOrDefault(w => w.IsDialog);
            var mainWindows = windows.Where(w => w.IsAppWindow).ToList();
            // Главное окно отключено — значит, открыт модальный вопрос, даже если это не стандартный диалог.
            var blocked = mainWindows.Any(w => !w.IsEnabled);

            var now = _clock.UtcNow;
            var dt = now - last;
            last = now;

            if (dialog is not null || blocked)
            {
                everAsked = true;
                var front = dialog?.Handle ?? mainWindows.First(w => !w.IsEnabled).Handle;
                if (lastDialog != front)
                {
                    _desktop.BringToFront(front);
                    lastDialog = front;
                    Report(CloseStage.Asking);
                }
                hiddenFor = null;
            }
            else
            {
                if (lastDialog is not null)
                {
                    lastDialog = null;
                    Report(CloseStage.Closing);
                }

                // Каждому видимому окну — WM_CLOSE, по одному разу. Новые окна (второе окно Chrome) — тоже.
                foreach (var w in mainWindows.Where(w => sent.Add(w.Handle)))
                    _desktop.PostClose(w.Handle);

                if (mainWindows.Count > 0)
                {
                    hiddenFor = null;
                }
                else
                {
                    // Окон нет, а процесс жив: прячется в трей или долго выходит.
                    hiddenFor = (hiddenFor ?? TimeSpan.Zero) + dt;
                    if (hiddenFor >= hideWait)
                    {
                        if (program.AllowTerminate && !everAsked)
                        {
                            // Последний взгляд перед завершением: ни окон, ни вопросов.
                            if (!_desktop.GetWindows().Any(w => pids.Contains(w.Pid) && (w.IsAppWindow || w.IsDialog)))
                            {
                                foreach (var p in alive)
                                    _desktop.Terminate(p);
                                return await WaitExit(alive, ct).ConfigureAwait(false)
                                    ? Finish(CloseOutcome.Terminated, everAsked)
                                    : Finish(CloseOutcome.StillOpen, everAsked);
                            }
                        }
                        else if (program.Known is { HidesToTray: true })
                        {
                            return Finish(CloseOutcome.InBackground, everAsked);
                        }
                    }
                }

                elapsed += dt;
                if (elapsed >= Timeout)
                    return Finish(mainWindows.Count > 0 ? CloseOutcome.StillOpen : CloseOutcome.InBackground, everAsked);
            }

            try
            {
                await _clock.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Finish(CloseOutcome.Cancelled, everAsked);
            }
        }
    }

    private HashSet<int> AlivePids(RunningProgram program, IReadOnlyList<ProcessKey> aliveRoots)
    {
        var pids = program.Processes.Where(p => _desktop.IsAlive(p.Key)).Select(p => p.Pid).ToHashSet();
        foreach (var r in aliveRoots) pids.Add(r.Pid);
        return pids;
    }

    private async Task<bool> WaitExit(IReadOnlyList<ProcessKey> processes, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            if (!processes.Any(_desktop.IsAlive))
                return true;
            try { await _clock.Delay(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return !processes.Any(_desktop.IsAlive);
    }
}
