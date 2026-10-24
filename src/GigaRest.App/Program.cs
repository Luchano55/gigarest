using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GigaRest.App;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var screenshots = ScreenshotDir(args);
        SingleInstance? single = null;
        if (screenshots is null)
        {
            single = SingleInstance.TryStart();
            if (single is null) return 0; // уже запущена — показали её окно
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args, screenshots, single);
        });
        single?.Dispose();
        return App.ExitCode;
    }

    private static string? ScreenshotDir(string[] args)
    {
        var i = Array.IndexOf(args, "--screenshots");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

/// <summary>Одна копия программы. Вторая запускается — просит первую показать окно и выходит.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\GigaRest.Single";
    private const string ShowEventName = @"Local\GigaRest.Show";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    public static SingleInstance? TryStart()
    {
        var mutex = new Mutex(true, MutexName, out var created);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!created)
        {
            show.Set();
            show.Dispose();
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, show);
    }

    /// <summary>Ждём просьбы «покажись» в отдельном потоке; действие — в UI-потоке.</summary>
    public void Listen(DispatcherQueue queue, Action onShow)
    {
        var thread = new Thread(() =>
        {
            var handles = new[] { _show, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
                queue.TryEnqueue(() => onShow());
        }) { IsBackground = true, Name = "GigaRest.SingleInstance" };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _show.Dispose();
    }
}
