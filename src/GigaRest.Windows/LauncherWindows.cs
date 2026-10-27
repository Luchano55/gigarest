using System.Diagnostics;
using GigaRest.Core.Logging;

namespace GigaRest.Windows;

/// <summary>
/// Steam и Battle.net при запуске игры показывают своё большое окно поверх «Тихого» экрана. Встроить их окна
/// в GigaRest нельзя (это чужие программы), поэтому первые полторы минуты после запуска игры сворачиваем
/// главное окно лаунчера, если оно появилось. Только главное: окна-вопросы («Обновить?», «Синхронизация») не трогаем,
/// иначе игра не запустится. Ничего не закрываем.
/// </summary>
public static class LauncherWindows
{
    private static readonly Dictionary<string, (string[] Processes, string Title)> Main = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = (["steam", "steamwebhelper"], "Steam"),
        ["Battle.net"] = (["Battle.net"], "Battle.net"),
    };

    private static CancellationTokenSource? _running;

    public static void KeepMinimized(string store)
    {
        if (!Main.TryGetValue(store, out var target)) return;
        _running?.Cancel();
        var cts = _running = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var until = DateTime.UtcNow.AddSeconds(90);
            var count = 0;
            while (DateTime.UtcNow < until && !cts.IsCancellationRequested)
            {
                foreach (var hwnd in FindMain(target.Processes, target.Title))
                {
                    Native.ShowWindow(hwnd, 7); // SW_SHOWMINNOACTIVE — свернуть, не отнимая фокус у игры
                    count++;
                }
                await Task.Delay(500);
            }
            if (count > 0) Log.Info($"{store}: окно лаунчера свёрнуто ({count})");
        });
    }

    private static List<nint> FindMain(string[] processNames, string title)
    {
        var pids = new HashSet<int>();
        foreach (var name in processNames)
            foreach (var p in Process.GetProcessesByName(name)) { pids.Add(p.Id); p.Dispose(); }
        var found = new List<nint>();
        if (pids.Count == 0) return found;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains(pid)) return true;
            if (string.Equals(WindowTools.Title(hwnd), title, StringComparison.Ordinal)) found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }
}
