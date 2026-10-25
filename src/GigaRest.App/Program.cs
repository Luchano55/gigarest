using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GigaRest.App;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Пробный режим: копия GigaRest.exe под другим именем рисует тестовое окно и больше ничего.
        if (args.Contains("--test-window"))
            return GigaRest.Windows.TestWindow.Run(args);

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

/// <summary>
/// Одна копия программы. Запускают ещё раз ту же сборку — она просит первую показать окно и выходит.
/// Запускают другую сборку (новую версию из другой папки) — она просит старую выйти и работает сама.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\GigaRest.Single";
    private const string ShowEventName = @"Local\GigaRest.Show";
    private const string QuitEventName = @"Local\GigaRest.Quit";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _quit;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle quit)
    {
        _mutex = mutex;
        _show = show;
        _quit = quit;
    }

    /// <summary>Файл, где запущенная копия пишет, откуда она и какой версии.</summary>
    private static string InstanceFile => Path.Combine(GigaRest.Core.AppPaths.Default().Root, "instance.txt");

    private static string Me => (Environment.ProcessPath ?? "") + "|" + typeof(SingleInstance).Assembly.GetName().Version;

    public static SingleInstance? TryStart()
    {
        var mutex = new Mutex(true, MutexName, out var created);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        if (!created)
        {
            string? running = null;
            try { running = File.ReadAllText(InstanceFile).Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (running is not null && string.Equals(running, Me, StringComparison.OrdinalIgnoreCase))
            {
                // Та же сборка — просто показать её окно.
                show.Set();
                show.Dispose(); quit.Dispose(); mutex.Dispose();
                return null;
            }
            // Другая сборка (или старая, которая о себе не пишет): просим выйти и ждём.
            quit.Set();
            var taken = false;
            try { taken = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { taken = true; }
            if (!taken)
            {
                // Старая версия не умеет выходить по просьбе — покажем её окно, а человеку подскажем.
                show.Set();
                show.Dispose(); quit.Dispose(); mutex.Dispose();
                ShowMessage("Уже запущена другая версия GigaRest, и она не закрылась сама.\n\n" +
                            "Закройте её: правый щелчок по значку-месяцу в трее → «Выход» " +
                            "(или Диспетчер задач → GigaRest → «Снять задачу»), затем запустите эту версию снова.");
                return null;
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InstanceFile)!);
            File.WriteAllText(InstanceFile, Me);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new SingleInstance(mutex, show, quit);
    }

    /// <summary>Ждём просьб «покажись» и «выйди» в отдельном потоке; действия — в UI-потоке.</summary>
    public void Listen(DispatcherQueue queue, Action onShow, Action onQuit)
    {
        var thread = new Thread(() =>
        {
            var handles = new[] { _show, _quit, _stop.Token.WaitHandle };
            while (true)
            {
                var i = WaitHandle.WaitAny(handles);
                if (i == 0) queue.TryEnqueue(() => onShow());
                else if (i == 1) { queue.TryEnqueue(() => onQuit()); return; }
                else return;
            }
        }) { IsBackground = true, Name = "GigaRest.SingleInstance" };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _show.Dispose();
        _quit.Dispose();
    }

    private static void ShowMessage(string text) => MessageBoxW(0, text, "GigaRest", 0x40 /* MB_ICONINFORMATION */);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(nint hwnd, string text, string caption, int type);
}
