using GigaRest.Core.Desktop;
using GigaRest.Core.Session;

namespace GigaRest.Core.Programs;

/// <summary>
/// Фоновые процессы без окон, которые можно завершать на время отдыха и запустить обратно.
/// Правило: показываем только то, что точно сможем запустить заново (известны exe и аргументы),
/// и никогда — системное, службы, антивирусы, драйверы и процессы администратора.
/// </summary>
public static class BackgroundScanner
{
    /// <summary>Части имени или пути, по которым процесс считается «железным» или защитным — не трогаем.</summary>
    private static readonly string[] Protected =
    [
        "nvidia", "nvcontainer", "nvdisplay", "nvsphelper", "amd", "radeon", "realtek", "rtkaud", "igfx", "intel",
        "logi", "razer", "corsair", "icue", "steelseries", "synaptics", "elan", "wacom", "asus", "armoury", "dell", "lenovo",
        "hp", "msi center", "dragon center", "defender", "securityhealth", "kaspersky", "avp", "eset", "ekrn", "avast",
        "avg", "bitdefender", "drweb", "norton", "mcafee", "malwarebytes", "windowsapps\\microsoft.", "driver", "audio",
        "bluetooth", "wifi", "vpn", "openvpn", "wireguard", "touchpad", "keyboard", "mouse", "printer", "scanner",
    ];

    public static bool IsProtected(ProcessInfo p)
    {
        if (KnownPrograms.System.Contains(p.ExeName)) return true;
        var path = p.ExePath ?? "";
        if (path.Length == 0) return true; // не знаем, откуда он, — не трогаем
        var windows = Environment.GetEnvironmentVariable("WINDIR");
        if (path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase)
            || windows is { Length: > 0 } && path.StartsWith(windows.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            return true;
        var name = p.ExeName;
        return Protected.Any(word =>
            name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || (word.Contains('\\') && path.Contains(word, StringComparison.OrdinalIgnoreCase))
            || (word.Length >= 5 && path.Contains("\\" + word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Кандидаты: корневые процессы нашего сеанса без окон, не системные, не от администратора,
    /// не помощники программ с окнами и с понятной командой запуска.
    /// </summary>
    public static List<RunningProgram> Find(
        IReadOnlyList<ProcessInfo> processes, IReadOnlyList<WindowInfo> windows, int selfPid)
    {
        var tree = new ProcessTree(processes);
        // Окна самого GigaRest не в счёт: иначе всё, что он перезапустил, считалось бы «частью открытой программы».
        var withWindows = windows.Where(w => (w.IsAppWindow || w.IsDialog) && w.Pid != selfPid).Select(w => w.Pid).ToHashSet();

        // Процессы программ с окнами (и всех их потомков) — не фоновые: это части открытых программ.
        var busy = new HashSet<int>();
        foreach (var pid in withWindows)
        {
            var p = tree.Get(pid);
            if (p is null) continue;
            foreach (var d in tree.Descendants(tree.RootOf(p))) busy.Add(d.Pid);
        }
        // Себя не трогаем. Но то, что GigaRest запустил при возврате к работе, — обычные программы, они в списке.
        busy.Add(selfPid);

        // Первый проход — подходящие корни; второй — убрать помощников других фоновых программ.
        var candidates = new List<ProcessInfo>();
        foreach (var p in processes)
        {
            if (busy.Contains(p.Pid) || p.IsElevated || IsProtected(p)) continue;
            if (tree.RootOf(p) != p) continue;                     // дочерний процесс той же программы
            var parent = tree.Parent(p);
            if (parent is not null && parent.Pid != selfPid && busy.Contains(parent.Pid)) continue; // помощник открытой программы
            if (RelaunchPlanner.Plan(p, null) is null) continue;     // не сможем запустить обратно — не предлагаем
            candidates.Add(p);
        }
        var candidatePids = candidates.Select(c => c.Pid).ToHashSet();

        var result = new Dictionary<string, (ProcessInfo root, List<ProcessInfo> all)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in candidates)
        {
            var parent = tree.Parent(p);
            if (parent is not null && parent.Pid != selfPid && !IsLauncher(parent) && candidatePids.Contains(parent.Pid)) continue;
            var id = ProgramScanner.ProgramId(p);
            if (result.ContainsKey(id)) continue;
            result[id] = (p, tree.Descendants(p).ToList());
        }

        return result.Select(kv => new RunningProgram
            {
                Id = kv.Key,
                ExeName = kv.Value.root.ExeName,
                Known = KnownPrograms.Find(kv.Value.root.ExeName),
                DisplayName = KnownPrograms.Find(kv.Value.root.ExeName)?.DisplayName ?? ProgramScanner.FriendlyName(kv.Value.root.ExeName),
                Roots = [kv.Value.root],
                Processes = kv.Value.all,
                Windows = [],
                IsBackground = true,
            })
            .OrderByDescending(p => p.PrivateBytes)
            .ToList();
    }

    /// <summary>Кто обычно запускает фоновые программы: проводник (автозагрузка), планировщик, другая программа.</summary>
    private static bool IsLauncher(ProcessInfo p) =>
        p.ExeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
        || p.ExeName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)
        || p.ExeName.Equals("taskhostw.exe", StringComparison.OrdinalIgnoreCase)
        || p.ExeName.Equals("sihost.exe", StringComparison.OrdinalIgnoreCase);
}
