using GigaRest.Core.Settings;

namespace GigaRest.Core.Programs;

/// <summary>Ярлык из меню «Пуск»: название и куда он ведёт.</summary>
public sealed record ShortcutInfo(string Name, string TargetPath);

/// <summary>Строка окна «Рабочие программы».</summary>
public sealed class SelectorRow
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public string? ExePath { get; init; }
    public bool IsRunning { get; init; }
    public bool HidesToTray { get; init; }
    public ProgramAction Action { get; set; }
    public bool AllowTerminate { get; set; }
    /// <summary>Автоподсказка: «похоже на рабочую: почта».</summary>
    public string? Hint { get; set; }
}

/// <summary>
/// Выбор рабочих программ: запущенные, уже настроенные и установленные (ярлыки меню «Пуск»),
/// без дублей и без деинсталляторов.
/// </summary>
public static class ProgramCatalog
{
    private static readonly string[] JunkWords =
    [
        "uninstall", "удалить", "удаление", "деинстал", "setup", "install", "readme", "help", "справка",
        "license", "лиценз", "update", "обновл", "repair", "website", "сайт",
    ];

    /// <summary>Ярлыки → программы: только .exe, без деинсталляторов, один ярлык на exe.</summary>
    public static List<ShortcutInfo> CleanShortcuts(IEnumerable<ShortcutInfo> shortcuts) => shortcuts
        .Where(s => s.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        .Where(s => !KnownPrograms.System.Contains(ExeName(s.TargetPath)))
        .Where(s => !JunkWords.Any(j => s.Name.Contains(j, StringComparison.OrdinalIgnoreCase)
                                        || ExeName(s.TargetPath).Contains(j, StringComparison.OrdinalIgnoreCase))
                    && !ExeName(s.TargetPath).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
        .GroupBy(s => ExeName(s.TargetPath), StringComparer.OrdinalIgnoreCase)
        .Select(g => g.OrderBy(s => s.Name.Length).First())
        .ToList();

    /// <summary>Все строки: сначала настроенные (не «Оставить»), потом запущенные, потом установленные — по алфавиту.</summary>
    public static List<SelectorRow> BuildRows(
        AppSettings settings, IEnumerable<RunningProgram> running, IEnumerable<ShortcutInfo> shortcuts)
    {
        var rows = new Dictionary<string, SelectorRow>(StringComparer.OrdinalIgnoreCase);
        var runningList = running.ToList();

        foreach (var p in runningList)
        {
            rows[p.Id] = new SelectorRow
            {
                Id = p.Id, DisplayName = p.DisplayName, ExePath = p.Roots.FirstOrDefault()?.ExePath, IsRunning = true,
                HidesToTray = p.Known?.HidesToTray == true || p.Windows.Count == 0,
                Action = p.Action, AllowTerminate = p.AllowTerminate,
            };
        }

        foreach (var s in settings.Programs.Where(s => !rows.ContainsKey(s.Id)))
        {
            var known = KnownPrograms.Find(s.Id);
            rows[s.Id] = new SelectorRow
            {
                Id = s.Id, DisplayName = s.DisplayName.Length > 0 ? s.DisplayName : known?.DisplayName ?? ProgramScanner.FriendlyName(s.Id),
                HidesToTray = known?.HidesToTray == true, Action = s.Action, AllowTerminate = s.AllowTerminate,
            };
        }

        foreach (var sc in CleanShortcuts(shortcuts))
        {
            var id = ExeName(sc.TargetPath).ToLowerInvariant();
            if (rows.ContainsKey(id)) continue;
            var known = KnownPrograms.Find(id);
            rows[id] = new SelectorRow
            {
                Id = id, DisplayName = known?.DisplayName ?? sc.Name, ExePath = sc.TargetPath,
                HidesToTray = known?.HidesToTray == true, Action = ProgramAction.Keep,
            };
        }

        return rows.Values
            .OrderBy(r => r.Action == ProgramAction.Keep ? 1 : 0)
            .ThenBy(r => r.IsRunning ? 0 : 1)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Поиск по названию и имени exe, без учёта регистра.</summary>
    public static IEnumerable<SelectorRow> Filter(IEnumerable<SelectorRow> rows, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return rows;
        var q = query.Trim();
        return rows.Where(r => r.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                               || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Сохранить выбор: в настройки попадают все строки, где действие не «Оставить», и уже известные.</summary>
    public static void Save(AppSettings settings, IEnumerable<SelectorRow> rows)
    {
        foreach (var r in rows)
        {
            var s = settings.Find(r.Id);
            if (s is null)
            {
                if (r.Action == ProgramAction.Keep && !r.AllowTerminate) continue;
                s = new ProgramSetting { Id = r.Id };
                settings.Programs.Add(s);
            }
            s.DisplayName = r.DisplayName;
            s.Action = r.Action;
            s.AllowTerminate = r.AllowTerminate;
        }
    }

    public static string ExeName(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i >= 0 ? path[(i + 1)..] : path;
    }
}
