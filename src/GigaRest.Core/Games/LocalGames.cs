namespace GigaRest.Core.Games;

/// <summary>Игра без магазина, добавленная вручную («Добавить игру…» в библиотеке).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// Свои игры и свои обложки. Обложки — картинки в папке «Обложки» (Изображения\GigaRest\Обложки):
/// имя файла — название игры («Diablo IV.jpg»), шапка для «Продолжить» — «Diablo IV шапка.jpg».
/// Регистр, пробелы и знаки в названии не важны. Своя обложка главнее обложки магазина.
/// </summary>
public static class LocalGames
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private const string HeroSuffix = "шапка";

    public static string DefaultCoversFolder() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "GigaRest", Text.L.T("Обложки", "Covers"));

    public static Game FromCustom(CustomGame c) => new()
    {
        Id = "custom:" + c.Path.ToLowerInvariant(), Name = c.Name, Store = "Свои",
        ExePath = c.Path, IconPath = c.Path, WorkingDirectory = System.IO.Path.GetDirectoryName(c.Path),
    };

    /// <summary>«C:\Games\Hollow Knight\hollow_knight.exe» → «Hollow Knight» (имя папки понятнее имени exe).</summary>
    public static string NameFromExe(string exePath)
    {
        var dir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exePath) ?? "");
        var stem = System.IO.Path.GetFileNameWithoutExtension(exePath);
        var generic = new[] { "bin", "binaries", "win64", "x64", "game", "games" };
        var name = !string.IsNullOrWhiteSpace(dir) && !generic.Contains(dir.ToLowerInvariant()) ? dir : stem;
        return name.Replace('_', ' ').Trim();
    }

    public static string Key(string name) => new(name.ToLowerInvariant().Replace('ё', 'е').Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Подставить свои обложки и шапки из папки.</summary>
    public static List<Game> ApplyCovers(IEnumerable<Game> games, string folder)
    {
        var list = games.ToList();
        if (!Directory.Exists(folder)) return list;
        var covers = new Dictionary<string, string>();
        var heroes = new Dictionary<string, string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!ImageExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) continue;
                var stem = System.IO.Path.GetFileNameWithoutExtension(file).Trim();
                if (stem.EndsWith(HeroSuffix, StringComparison.OrdinalIgnoreCase))
                    heroes[Key(stem[..^HeroSuffix.Length])] = file;
                else covers[Key(stem)] = file;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        return list.Select(g =>
        {
            var key = Key(g.Name);
            return g with
            {
                CoverPath = covers.TryGetValue(key, out var c) ? c : g.CoverPath,
                HeroPath = heroes.TryGetValue(key, out var h) ? h : g.HeroPath,
            };
        }).ToList();
    }
}
