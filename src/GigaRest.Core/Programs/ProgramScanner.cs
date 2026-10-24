using GigaRest.Core.Desktop;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Programs;

/// <summary>Собирает из процессов и окон список программ для окна «Переход в отдых».</summary>
public static class ProgramScanner
{
    public static List<RunningProgram> Scan(
        IReadOnlyList<ProcessInfo> processes, IReadOnlyList<WindowInfo> windows, AppSettings settings, int selfPid)
    {
        var tree = new ProcessTree(processes);
        var appWindows = windows.Where(w => w.IsAppWindow && w.Pid != selfPid).ToList();

        // Ключ программы: AUMID (Store) или имя exe.
        var groups = new Dictionary<string, (string exe, List<ProcessInfo> roots)>(StringComparer.OrdinalIgnoreCase);
        void AddRoot(ProcessInfo root)
        {
            var id = ProgramId(root);
            if (!groups.TryGetValue(id, out var g))
                groups[id] = g = (root.ExeName, []);
            if (!g.roots.Any(r => r.Key == root.Key))
                g.roots.Add(root);
        }

        foreach (var w in appWindows)
        {
            var p = tree.Get(w.Pid);
            if (p is null || IsSystem(p) || p.Pid == selfPid)
                continue;
            AddRoot(tree.RootOf(p));
        }

        // Программы только в трее (Яндекс Диск) — по известному списку.
        foreach (var p in processes)
        {
            var known = KnownPrograms.Find(p.ExeName);
            if (known is { TrayOnly: true } && tree.RootOf(p) == p)
                AddRoot(p);
        }

        var result = new List<RunningProgram>();
        foreach (var (id, (exe, roots)) in groups)
        {
            var all = roots.SelectMany(tree.Descendants).DistinctBy(p => p.Key).ToList();
            var pids = all.Select(p => p.Pid).ToHashSet();
            var known = KnownPrograms.Find(exe);
            var setting = settings.Find(id);
            result.Add(new RunningProgram
            {
                Id = id,
                ExeName = exe,
                Known = known,
                DisplayName = setting?.DisplayName is { Length: > 0 } name ? name : known?.DisplayName ?? FriendlyName(exe),
                Roots = roots,
                Processes = all,
                Windows = appWindows.Where(w => pids.Contains(w.Pid)).ToList(),
                Action = setting?.Action ?? known?.DefaultAction ?? ProgramAction.Keep,
                AllowTerminate = setting?.AllowTerminate ?? false,
            });
        }

        // Сначала то, что закрываем, потом второй монитор, потом остальное; внутри — по памяти.
        return result
            .OrderBy(p => p.Action switch { ProgramAction.Close => 0, ProgramAction.SecondMonitor => 1, _ => 2 })
            .ThenByDescending(p => p.PrivateBytes)
            .ToList();
    }

    public static string ProgramId(ProcessInfo root) => root.Aumid ?? root.ExeName.ToLowerInvariant();

    private static bool IsSystem(ProcessInfo p) => KnownPrograms.System.Contains(p.ExeName);

    /// <summary>«notepad++.exe» → «notepad++».</summary>
    public static string FriendlyName(string exe) =>
        exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;

    /// <summary>
    /// Новые программы попадают в настройки с действием по умолчанию («Оставить», если программа неизвестна).
    /// Возвращает true, если настройки изменились.
    /// </summary>
    public static bool RememberNew(AppSettings settings, IEnumerable<RunningProgram> programs)
    {
        var changed = false;
        foreach (var p in programs)
        {
            if (settings.Find(p.Id) is not null)
                continue;
            settings.Programs.Add(new ProgramSetting
            {
                Id = p.Id, DisplayName = p.DisplayName, Action = p.Action, AllowTerminate = p.AllowTerminate,
            });
            changed = true;
        }
        return changed;
    }

    /// <summary>Сохранить выбор из окна перехода в настройки.</summary>
    public static void Apply(AppSettings settings, RunningProgram program)
    {
        var s = settings.Find(program.Id);
        if (s is null)
        {
            s = new ProgramSetting { Id = program.Id, DisplayName = program.DisplayName };
            settings.Programs.Add(s);
        }
        s.Action = program.Action;
        s.AllowTerminate = program.AllowTerminate;
    }
}
