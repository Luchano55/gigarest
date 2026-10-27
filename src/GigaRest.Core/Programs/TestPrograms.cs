using GigaRest.Core.Settings;
using GigaRest.Core.Text;

namespace GigaRest.Core.Programs;

/// <summary>
/// Пробный режим: четыре тестовых окна вместо настоящих программ. Каждое — копия GigaRest.exe
/// под своим именем, запущенная с --test-window, поэтому для GigaRest это четыре разные программы.
/// </summary>
public static class TestPrograms
{
    public sealed record Spec(string ExeName, string Mode, ProgramAction Action, bool AllowTerminate, string Title, bool Tile = false);

    public static IReadOnlyList<Spec> All =>
    [
        new("Тест 1 — закроется.exe", "normal", ProgramAction.Close, false, L.T("Тест 1 — закроется сама", "Test 1 — closes by itself")),
        new("Тест 2 — спросит.exe", "ask", ProgramAction.Close, false, L.T("Тест 2 — спросит «Сохранить?»", "Test 2 — asks “Save?”")),
        new("Тест 3 — трей.exe", "tray", ProgramAction.Close, true, L.T("Тест 3 — прячется в трей", "Test 3 — hides to the tray")),
        new("Тест 4 — второй монитор.exe", "normal", ProgramAction.SecondMonitor, false, L.T("Тест 4 — на второй монитор", "Test 4 — goes to the second monitor"), Tile: true),
    ];

    public static bool IsTest(string programId) =>
        All.Any(s => string.Equals(s.ExeName, programId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Аргументы запуска тестового окна (по одному — для ArgumentList).</summary>
    public static List<string> Arguments(Spec spec, int index)
    {
        var args = new List<string> { "--test-window", "--mode", spec.Mode, "--title", spec.Title, "--x", (160 + index * 60).ToString(), "--y", (160 + index * 60).ToString() };
        if (spec.Tile) args.Add("--tile");
        return args;
    }

    /// <summary>Из запущенных — только тестовые, с действиями пробного режима.</summary>
    public static List<RunningProgram> Pick(IEnumerable<RunningProgram> running)
    {
        var result = new List<RunningProgram>();
        foreach (var spec in All)
        {
            var p = running.FirstOrDefault(r => string.Equals(r.Id, spec.ExeName, StringComparison.OrdinalIgnoreCase));
            if (p is null) continue;
            p.Action = spec.Action;
            p.AllowTerminate = spec.AllowTerminate;
            result.Add(p);
        }
        return result;
    }
}
