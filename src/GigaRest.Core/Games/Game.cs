namespace GigaRest.Core.Games;

/// <summary>
/// Установленная игра из библиотеки. Запуск — через сам магазин (ссылка steam://, com.epicgames.launcher://)
/// или напрямую exe (GOG, Battle.net).
/// </summary>
public sealed record Game
{
    /// <summary>«steam:570», «epic:Fortnite», «gog:1207658924».</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>«Steam», «Epic», «GOG».</summary>
    public required string Store { get; init; }
    public DateTime? LastPlayedUtc { get; init; }
    public string? LaunchUri { get; init; }
    public string? ExePath { get; init; }
    public string? WorkingDirectory { get; init; }
    /// <summary>Аргументы для ExePath (Battle.net: «--exec=launch Fen»).</summary>
    public IReadOnlyList<string> LaunchArgs { get; init; } = [];
    /// <summary>Обложка из кэша магазина (Steam: вертикальная 600×900 или горизонтальная шапка). Из интернета не качаем.</summary>
    public string? CoverPath { get; init; }
    /// <summary>Широкая картинка для шапки библиотеки (Steam: library_hero.jpg).</summary>
    public string? HeroPath { get; init; }
    /// <summary>Сколько наиграно, минут (Steam считает сам): всего и за две недели.</summary>
    public int? PlayMinutes { get; init; }
    public int? PlayMinutes2Weeks { get; init; }
    /// <summary>Файл, чей значок показать, если обложки нет (exe или ico игры).</summary>
    public string? IconPath { get; init; }
}
