using System.Text.RegularExpressions;
using GigaRest.Core.Text;

namespace GigaRest.Core.Tiles;

/// <summary>
/// Строка под плиткой: что играет (музыка), сколько непрочитанных (Telegram) или заголовок окна (браузер).
/// </summary>
public static partial class TileCaptions
{
    /// <summary>Подпись плитки; null — обычная «щёлкните, чтобы открыть».</summary>
    public static string? For(string programId, string displayName, string? windowTitle, string? nowPlaying)
    {
        if (!string.IsNullOrWhiteSpace(nowPlaying)) return "♪ " + nowPlaying;
        if (IsMessenger(programId))
        {
            var unread = Unread(windowTitle);
            return unread is > 0 ? Sizes.Plural(unread.Value, "непрочитанное", "непрочитанных", "непрочитанных") : "нет новых сообщений";
        }
        var title = CleanTitle(windowTitle, displayName);
        return title;
    }

    public static bool IsMessenger(string programId) => programId.Contains("telegram", StringComparison.OrdinalIgnoreCase);

    /// <summary>«Telegram (5)» → 5; «Telegram» → 0; нет заголовка → null.</summary>
    public static int? Unread(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;
        var m = UnreadRegex().Match(windowTitle);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    /// <summary>«Музыка — Исполнитель — Название»: исполнитель и название трека для подписи.</summary>
    public static string? NowPlaying(string? title, string? artist)
    {
        title = title?.Trim();
        artist = artist?.Trim();
        if (string.IsNullOrEmpty(title)) return null;
        return string.IsNullOrEmpty(artist) ? title : $"{artist} — {title}";
    }

    /// <summary>
    /// Чья это музыка: источник воспроизведения Windows (его код приложения, AUMID) против программы на плитке.
    /// Сравниваем без регистра, пробелов и знаков: «Яндекс Музыка.exe» ≈ «яндексмузыка»; у Яндекс Музыки код бывает латиницей.
    /// </summary>
    public static bool MediaMatches(string? sourceAumid, string programId, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(sourceAumid)) return false;
        var source = Squash(sourceAumid);
        var stems = new List<string> { Squash(StripExe(programId)) };
        if (exePath is not null) stems.Add(Squash(Path.GetFileNameWithoutExtension(exePath.Replace('\\', '/').Split('/')[^1])));
        foreach (var stem in stems.Where(s => s.Length >= 3).Distinct())
        {
            if (source.Contains(stem) || stem.Contains(source)) return true;
            if (stem.Contains("яндексмузык") && source.Contains("yandex") && source.Contains("music")) return true;
        }
        return false;
    }

    /// <summary>Понятное имя источника музыки: «ru.yandex.desktop.music» → «Яндекс Музыка», «chrome.exe» → «Chrome».</summary>
    public static string MediaSourceName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        var s = Squash(aumid);
        if ((s.Contains("yandex") && s.Contains("music")) || s.Contains("яндексмузык")) return "Яндекс Музыка";
        if (s.Contains("gigarest") || s.Contains("msedgewebview2")) return "GigaRest"; // встроенная Яндекс Музыка или плеер
        if (s.Contains("chrome")) return "Chrome";
        if (s.Contains("zen")) return "Zen";
        if (s.Contains("firefox")) return "Firefox";
        if (s.Contains("msedge")) return "Edge";
        if (s.Contains("telegram")) return "Telegram";
        if (s.Contains("spotify")) return "Spotify";
        var name = aumid.Split('!')[0].Split('\\').Last();
        name = StripExe(name);
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1 && !name.Contains(' ')) name = name[(dot + 1)..];
        return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : "";
    }

    private static string StripExe(string id) => id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;

    private static string Squash(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>«Почта — Google Chrome» → «Почта»; заголовок, совпадающий с названием программы, не показываем.</summary>
    public static string? CleanTitle(string? windowTitle, string displayName)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;
        var t = windowTitle.Trim();
        foreach (var tail in new[] { " — Zen Browser", " - Zen Browser", " — Zen", " - Google Chrome", " — Google Chrome", " - Mozilla Firefox", " — Mozilla Firefox", " - Yandex Browser", " — Яндекс Браузер" })
            if (t.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) { t = t[..^tail.Length].Trim(); break; }
        if (t.Length == 0 || string.Equals(t, displayName, StringComparison.OrdinalIgnoreCase)) return null;
        return t.Length > 80 ? t[..79] + "…" : t;
    }

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex UnreadRegex();
}
