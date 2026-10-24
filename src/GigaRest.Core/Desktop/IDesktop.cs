namespace GigaRest.Core.Desktop;

/// <summary>
/// Всё, что ядру нужно от Windows. Настоящая реализация — GigaRest.Windows,
/// в тестах — подделка. Методы быстрые и не бросают исключений: ошибка = false/null.
/// </summary>
public interface IDesktop
{
    IReadOnlyList<ProcessInfo> GetProcesses();
    /// <summary>Окна верхнего уровня, включая диалоги с владельцем.</summary>
    IReadOnlyList<WindowInfo> GetWindows();
    IReadOnlyList<MonitorInfo> GetMonitors();
    int CurrentProcessId { get; }

    /// <summary>Вежливо закрыть окно — как крестиком (WM_CLOSE).</summary>
    bool PostClose(long window);
    void BringToFront(long window);
    /// <summary>Завершить процесс, только если это всё ещё тот же процесс (PID + время старта).</summary>
    bool Terminate(ProcessKey process);
    bool IsAlive(ProcessKey process);

    /// <summary>Свободная физическая память, байты.</summary>
    long GetAvailableMemoryBytes();

    WindowPlacementData? GetPlacement(long window);
    bool SetPlacement(long window, WindowPlacementData placement, MonitorInfo? monitor);
    /// <summary>Перенести окно на монитор и развернуть на нём.</summary>
    bool MoveToMonitorMaximized(long window, MonitorInfo monitor);

    /// <summary>Запустить программу. null — успех, иначе текст ошибки.</summary>
    string? Launch(LaunchSpec spec);
}

public interface IClock
{
    DateTime UtcNow { get; }
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime UtcNow => DateTime.UtcNow;
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
