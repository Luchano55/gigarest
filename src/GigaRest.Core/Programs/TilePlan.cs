namespace GigaRest.Core.Programs;

/// <summary>
/// Порядок плиток на втором мониторе: Роман переставляет их на картинке в окне перехода.
/// Место хранится в настройках (TileOrder); у кого места нет — по порядку списка, после расставленных.
/// </summary>
public static class TilePlan
{
    public const int Max = 4;

    public static IEnumerable<RunningProgram> Ordered(IEnumerable<RunningProgram> programs) =>
        programs.Where(p => p.Action == Settings.ProgramAction.SecondMonitor)
            .Select((p, i) => (p, i))
            .OrderBy(x => x.p.TileOrder ?? int.MaxValue).ThenBy(x => x.i)
            .Select(x => x.p);

    /// <summary>Поменять местами плитки a и b (номера мест 0–3) и записать новые места всем.</summary>
    public static void Swap(IReadOnlyList<RunningProgram> programs, int a, int b)
    {
        var tiles = Ordered(programs).ToList();
        if (a < 0 || b < 0 || a >= tiles.Count || b >= tiles.Count || a == b) return;
        (tiles[a], tiles[b]) = (tiles[b], tiles[a]);
        Renumber(tiles);
    }

    /// <summary>Поставить программу на второй монитор последней плиткой (или убрать — Keep).</summary>
    public static void Renumber(IReadOnlyList<RunningProgram> programs)
    {
        var tiles = programs as List<RunningProgram> ?? Ordered(programs).ToList();
        for (var i = 0; i < tiles.Count; i++) tiles[i].TileOrder = i;
    }
}
