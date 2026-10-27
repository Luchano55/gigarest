using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;

namespace GigaRest.Core.Session;

/// <summary>Как запустить программу обратно: тот же exe, те же аргументы, Store — по AUMID.</summary>
public static class RelaunchPlanner
{
    public static LaunchSpec? Plan(ProcessInfo root, KnownProgram? known)
    {
        if (root.Aumid is { Length: > 0 } aumid)
            return new LaunchSpec { Aumid = aumid };

        var args = CommandLine.Split(root.CommandLine);
        var exe = root.ExePath ?? (args.Count > 0 ? args[0] : null);
        if (string.IsNullOrWhiteSpace(exe))
            return null;

        var rest = args.Skip(1).ToList();
        if (known is not null)
        {
            rest.RemoveAll(a => known.RelaunchDropArgs.Contains(a, StringComparer.OrdinalIgnoreCase));
            foreach (var add in known.RelaunchAddArgs)
                if (!rest.Contains(add, StringComparer.OrdinalIgnoreCase))
                    rest.Add(add);
        }

        return new LaunchSpec
        {
            FileName = exe,
            Arguments = rest,
            WorkingDirectory = DirectoryOf(exe),
        };
    }

    /// <summary>Папка exe. Пути Windows разбираем сами — ядро тестируется и на Linux.</summary>
    internal static string? DirectoryOf(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        if (i <= 0) return null;
        // «C:\app.exe» → «C:\», а не «C:».
        return i == 2 && path[1] == ':' ? path[..3] : path[..i];
    }
}
