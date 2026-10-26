using GigaRest.Core.Desktop;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Programs;

/// <summary>Открытая программа: все её процессы и окна.</summary>
public sealed class RunningProgram
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ExeName { get; init; }
    public KnownProgram? Known { get; init; }
    /// <summary>Корневые процессы — их командную строку запоминаем для запуска обратно.</summary>
    public required IReadOnlyList<ProcessInfo> Roots { get; init; }
    /// <summary>Все процессы программы (корни и их дочерние).</summary>
    public required IReadOnlyList<ProcessInfo> Processes { get; init; }
    /// <summary>Окна Alt+Tab.</summary>
    public required IReadOnlyList<WindowInfo> Windows { get; init; }
    public long PrivateBytes => Processes.Sum(p => p.PrivateBytes);
    public bool IsElevated => Roots.Any(p => p.IsElevated);
    public string? Aumid => Roots.Select(r => r.Aumid).FirstOrDefault(a => a is not null);

    /// <summary>Фоновый процесс без окон: «завершать в отдыхе» или «не трогать».</summary>
    public bool IsBackground { get; init; }

    public ProgramAction Action { get; set; }
    public bool AllowTerminate { get; set; }
    /// <summary>Завершать принудительно, даже с открытыми окнами и вопросами (по галочке Романа).</summary>
    public bool ForceKill { get; set; }
    /// <summary>Место плитки на втором мониторе (0–3); null — по порядку в списке.</summary>
    public int? TileOrder { get; set; }
    /// <summary>Путь к exe — для значка.</summary>
    public string? ExePath => Roots.Select(r => r.ExePath).FirstOrDefault(p => !string.IsNullOrEmpty(p));

    /// <summary>Описание под названием: «2 окна», «в трее».</summary>
    public string Note => IsBackground ? "фоновый процесс" : Windows.Count == 0 ? "в трее" : Text.Sizes.Plural(Windows.Count, "окно", "окна", "окон");
}
