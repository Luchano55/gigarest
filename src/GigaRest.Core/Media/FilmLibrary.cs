using System.Text.RegularExpressions;

namespace GigaRest.Core.Media;

/// <summary>Фильм или ролик из папки с фильмами.</summary>
public sealed record Film(string Path, string Name, DateTime ModifiedUtc, long Size);

/// <summary>
/// «Смотреть»: видео из папки с фильмами (и её подпапок на два уровня). Только чтение, открывает их сама Windows —
/// плеером по умолчанию.
/// </summary>
public static partial class FilmLibrary
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".webm", ".wmv", ".ts", ".m2ts",
    };

    /// <summary>Папка по умолчанию — «Видео» пользователя.</summary>
    public static string DefaultFolder() => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

    /// <summary>Новые сверху. Отрывки («sample») и совсем маленькие файлы (меньше 5 МБ) не показываем.</summary>
    public static List<Film> Scan(string folder, int max = 100)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var films = new List<Film>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (!Extensions.Contains(System.IO.Path.GetExtension(path))) continue;
                var info = new FileInfo(path);
                if (info.Length < 5L << 20) continue;
                var stem = System.IO.Path.GetFileNameWithoutExtension(path);
                if (SampleRegex().IsMatch(stem)) continue;
                films.Add(new Film(path, Title(stem), info.LastWriteTimeUtc, info.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logging.Log.Warn("Фильмы: " + ex.Message); }
        return films.OrderByDescending(f => f.ModifiedUtc).Take(max).ToList();
    }

    /// <summary>«Brat.2.2000.1080p.BluRay.x264» → «Brat 2 (2000)»; «Мой_отпуск» → «Мой отпуск».</summary>
    public static string Title(string stem)
    {
        var s = stem.Replace('_', ' ');
        if (!s.Contains(' ')) s = s.Replace('.', ' ');
        var year = YearRegex().Match(s);
        if (year.Success && year.Index > 0)
        {
            var name = s[..year.Index].Trim(' ', '-', '(', '[');
            if (name.Length > 0) return $"{name} ({year.Groups[1].Value})";
        }
        var junk = JunkRegex().Match(s);
        if (junk.Success && junk.Index > 0) s = s[..junk.Index];
        return s.Trim(' ', '-');
    }

    [GeneratedRegex(@"(?:^|[\s.\-_])sample(?:$|[\s.\-_])", RegexOptions.IgnoreCase)]
    private static partial Regex SampleRegex();

    [GeneratedRegex(@"[\s(\[]((?:19|20)\d{2})(?:[\s)\]]|$)")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\s(?:2160p|1080p|720p|480p|4k|web-?dl|webrip|bluray|bdrip|hdrip|x264|x265|hevc)\b", RegexOptions.IgnoreCase)]
    private static partial Regex JunkRegex();
}
