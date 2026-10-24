using GigaRest.Core.Closing;
using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public class CloseOrchestratorTests
{
    private readonly FakeDesktop _desktop = new();
    private readonly FakeClock _clock = new();

    private CloseOrchestrator Orchestrator() => new(_desktop, _clock);

    private RunningProgram Program(string id, bool allowTerminate = false) =>
        ProgramScanner.Scan(_desktop.GetProcesses(), _desktop.GetWindows(), new AppSettings(), 1)
            .Single(p => p.Id == id) is var p ? Configure(p, allowTerminate) : null!;

    private static RunningProgram Configure(RunningProgram p, bool allowTerminate)
    {
        p.AllowTerminate = allowTerminate;
        return p;
    }

    [Fact]
    public async Task SendsCloseToEveryWindowAndReportsClosed()
    {
        _desktop.AddProcess("chrome.exe", 10);
        _desktop.AddProcess("chrome.exe", 11, parent: 10, commandLine: "chrome.exe --type=renderer");
        var w1 = _desktop.AddWindow(10, "Работа");
        var w2 = _desktop.AddWindow(10, "Почта");
        // Процесс выходит, когда закрыто последнее окно.
        _desktop.OnClose[w1.Handle] = () => _desktop.RemoveWindow(w1.Handle);
        _desktop.OnClose[w2.Handle] = () => { _desktop.RemoveWindow(w2.Handle); _desktop.Kill(10); _desktop.Kill(11); };

        var result = await Orchestrator().CloseAsync(Program("chrome.exe"), null, default);

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Equal(new[] { w1.Handle, w2.Handle }, _desktop.ClosedPosts.Order());
        Assert.Empty(_desktop.Terminated);
    }

    [Fact]
    public async Task SaveQuestionPausesTimerAndNeverTerminates()
    {
        _desktop.AddProcess("winword.exe", 20);
        var main = _desktop.AddWindow(20, "Документ1");
        long dialog = 0;
        _desktop.OnClose[main.Handle] = () =>
        {
            _desktop.SetEnabled(main.Handle, false);
            dialog = _desktop.AddWindow(20, "Сохранить?", owner: main.Handle, cls: "#32770").Handle;
        };
        // Пользователь думает 2 минуты (больше 30 с таймаута), потом отвечает «Не сохранять».
        var askedAt = (DateTime?)null;
        _clock.Ticked += () =>
        {
            if (dialog == 0) return;
            askedAt ??= _clock.UtcNow;
            if (_clock.UtcNow - askedAt > TimeSpan.FromMinutes(2)) _desktop.Kill(20);
        };

        var stages = new List<CloseStage>();
        var program = Program("winword.exe", allowTerminate: true);
        var result = await Orchestrator().CloseAsync(program, new Tests.SyncProgress<CloseProgress>(p => stages.Add(p.Stage)), default);

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.True(result.WasAsked);
        Assert.Contains(CloseStage.Asking, stages);
        Assert.Equal(dialog, _desktop.Fronted.Single());
        Assert.Empty(_desktop.Terminated);
    }

    [Fact]
    public async Task UserCancelledSaveQuestionLeavesProgramOpen()
    {
        _desktop.AddProcess("notepad.exe", 21);
        var main = _desktop.AddWindow(21, "Без имени");
        long dialog = 0;
        _desktop.OnClose[main.Handle] = () =>
        {
            _desktop.SetEnabled(main.Handle, false);
            dialog = _desktop.AddWindow(21, "Сохранить?", owner: main.Handle, cls: "#32770").Handle;
        };
        // «Отмена» через 5 с: диалог исчезает, окно остаётся.
        var ticks = 0;
        _clock.Ticked += () =>
        {
            if (dialog != 0 && ++ticks == 20)
            {
                _desktop.RemoveWindow(dialog);
                _desktop.SetEnabled(main.Handle, true);
            }
        };

        var result = await Orchestrator().CloseAsync(Program("notepad.exe", allowTerminate: true), null, default);

        Assert.Equal(CloseOutcome.StillOpen, result.Outcome);
        Assert.Empty(_desktop.Terminated);
        Assert.Single(_desktop.ClosedPosts); // повторно WM_CLOSE не шлём
    }

    [Fact]
    public async Task TrayProgramTerminatedOnlyWithPermission()
    {
        _desktop.AddProcess("bitrix24.exe", 30);
        var w = _desktop.AddWindow(30, "Битрикс24");
        _desktop.OnClose[w.Handle] = () => _desktop.RemoveWindow(w.Handle); // прячется в трей

        var result = await Orchestrator().CloseAsync(Program("bitrix24.exe", allowTerminate: true), null, default);

        Assert.Equal(CloseOutcome.Terminated, result.Outcome);
        Assert.Equal(30, _desktop.Terminated.Single().Pid);
    }

    [Fact]
    public async Task TrayProgramWithoutPermissionStaysInBackground()
    {
        _desktop.AddProcess("bitrix24.exe", 31);
        var w = _desktop.AddWindow(31, "Битрикс24");
        _desktop.OnClose[w.Handle] = () => _desktop.RemoveWindow(w.Handle);
        var start = _clock.UtcNow;

        var result = await Orchestrator().CloseAsync(Program("bitrix24.exe"), null, default);

        Assert.Equal(CloseOutcome.InBackground, result.Outcome);
        Assert.Empty(_desktop.Terminated);
        Assert.True(_clock.UtcNow - start < TimeSpan.FromSeconds(10), "известная программа трея не должна держать 30 с");
    }

    [Fact]
    public async Task ProgramThatIgnoresCloseIsReportedAfterTimeout()
    {
        _desktop.AddProcess("stubborn.exe", 40);
        _desktop.AddWindow(40, "Не закрываюсь");
        var start = _clock.UtcNow;

        var result = await Orchestrator().CloseAsync(Program("stubborn.exe", allowTerminate: true), null, default);

        Assert.Equal(CloseOutcome.StillOpen, result.Outcome);
        Assert.Contains("не закрылась", result.Message);
        Assert.InRange((_clock.UtcNow - start).TotalSeconds, 30, 31);
        Assert.Empty(_desktop.Terminated);
    }

    [Fact]
    public async Task SlowExitingUnknownProgramGetsFullTimeout()
    {
        // Outlook: окно закрылось, процесс ещё 20 с сохраняет почту.
        _desktop.AddProcess("outlook.exe", 50);
        var w = _desktop.AddWindow(50, "Входящие");
        var closedAt = (DateTime?)null;
        _desktop.OnClose[w.Handle] = () => { _desktop.RemoveWindow(w.Handle); closedAt = _clock.UtcNow; };
        _clock.Ticked += () => { if (closedAt is not null && _clock.UtcNow - closedAt > TimeSpan.FromSeconds(20)) _desktop.Kill(50); };

        var result = await Orchestrator().CloseAsync(Program("outlook.exe"), null, default);

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
        Assert.Empty(_desktop.Terminated);
    }

    [Fact]
    public async Task ElevatedProgramIsNotTouched()
    {
        _desktop.AddProcess("admin.exe", 60, elevated: true);
        _desktop.AddWindow(60, "Админ");

        var result = await Orchestrator().CloseAsync(Program("admin.exe", allowTerminate: true), null, default);

        Assert.Equal(CloseOutcome.Elevated, result.Outcome);
        Assert.Empty(_desktop.ClosedPosts);
        Assert.Contains("от администратора", result.Message);
    }

    [Fact]
    public async Task TrayOnlyProgramTerminatedWithPermission()
    {
        _desktop.AddProcess("YandexDisk2.exe", 70);

        var program = Program("yandexdisk2.exe", allowTerminate: true);
        var result = await Orchestrator().CloseAsync(program, null, default);

        Assert.Equal(CloseOutcome.Terminated, result.Outcome);
    }

    [Fact]
    public async Task CancellationStopsWaiting()
    {
        _desktop.AddProcess("stubborn.exe", 80);
        _desktop.AddWindow(80, "Не закрываюсь");
        using var cts = new CancellationTokenSource();
        var ticks = 0;
        _clock.Ticked += () => { if (++ticks == 3) cts.Cancel(); };

        var result = await Orchestrator().CloseAsync(Program("stubborn.exe"), null, cts.Token);

        Assert.Equal(CloseOutcome.Cancelled, result.Outcome);
    }
}

public sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
