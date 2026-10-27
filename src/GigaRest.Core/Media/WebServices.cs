namespace GigaRest.Core.Media;

/// <summary>Где сервис в «Тихом» экране: «Смотреть» или «Слушать».</summary>
public enum ServiceKind { Watch, Listen }

/// <summary>Сайт с видео или музыкой: встроенный по умолчанию или добавленный вручную («Добавить сервис…»).</summary>
public sealed class WebService
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public ServiceKind Kind { get; set; }
    /// <summary>Где открывать: пусто — прямо в GigaRest; <see cref="WebServices.DefaultBrowser"/> — браузер Windows по умолчанию; иначе путь к exe браузера.</summary>
    public string? OpenIn { get; set; }
}

/// <summary>Готовый сервис для быстрого добавления. Protected — защищённое видео или музыка: во встроенном окне обычно не играет.</summary>
public sealed record ServicePreset(string Name, string Url, ServiceKind Kind, bool Protected = false);

public static class WebServices
{
    public const string DefaultBrowser = "browser";

    public static readonly IReadOnlyList<ServicePreset> Presets =
    [
        new("YouTube", "https://www.youtube.com/", ServiceKind.Watch),
        new("VK Видео", "https://vkvideo.ru/", ServiceKind.Watch),
        new("Rutube", "https://rutube.ru/", ServiceKind.Watch),
        new("Twitch", "https://www.twitch.tv/", ServiceKind.Watch),
        new("Кинопоиск", "https://hd.kinopoisk.ru/", ServiceKind.Watch, Protected: true),
        new("Okko", "https://okko.tv/", ServiceKind.Watch, Protected: true),
        new("Иви", "https://www.ivi.ru/", ServiceKind.Watch, Protected: true),
        new("Netflix", "https://www.netflix.com/", ServiceKind.Watch, Protected: true),
        new("Яндекс Музыка", "https://music.yandex.ru/", ServiceKind.Listen),
        new("YouTube Music", "https://music.youtube.com/", ServiceKind.Listen),
        new("VK Музыка", "https://vk.com/music", ServiceKind.Listen),
        new("SoundCloud", "https://soundcloud.com/", ServiceKind.Listen),
        new("Звук", "https://zvuk.com/", ServiceKind.Listen),
        new("Spotify", "https://open.spotify.com/", ServiceKind.Listen, Protected: true),
        new("Apple Music", "https://music.apple.com/", ServiceKind.Listen, Protected: true),
    ];

    /// <summary>Что есть с первого запуска: YouTube в «Смотреть», Яндекс Музыка в «Слушать».</summary>
    public static List<WebService> Defaults() =>
    [
        FromPreset(Presets.First(p => p.Name == "YouTube")),
        FromPreset(Presets.First(p => p.Name == "Яндекс Музыка")),
    ];

    public static WebService FromPreset(ServicePreset p) => new()
    {
        Name = p.Name, Url = p.Url, Kind = p.Kind, OpenIn = p.Protected ? DefaultBrowser : null,
    };

    /// <summary>Готовый сервис по адресу (тот же сайт) — чтобы знать, защищён ли он.</summary>
    public static ServicePreset? PresetFor(string url) =>
        Presets.FirstOrDefault(p => SameSite(p.Url, url));

    /// <summary>«spotify.com», «https://open.spotify.com/…» → https-адрес; не адрес — null.</summary>
    public static string? NormalizeUrl(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Contains(' ')) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (!uri.Host.Contains('.') || uri.Host.StartsWith('.') || uri.Host.EndsWith('.')) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>Название по адресу, если его не ввели: «https://www.twitch.tv/» → «Twitch», «open.spotify.com» → «Spotify».</summary>
    public static string NameFromUrl(string url)
    {
        if (PresetFor(url) is { } preset) return preset.Name;
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1) parts.RemoveAt(parts.Count - 1); // .com, .ru
        string[] skip = ["www", "m", "open", "music", "hd", "app", "web", "play"];
        var main = parts.LastOrDefault(p => !skip.Contains(p.ToLowerInvariant())) ?? parts.LastOrDefault() ?? host;
        return main.Length == 0 ? host : char.ToUpperInvariant(main[0]) + main[1..];
    }

    /// <summary>Тот же сайт: хосты без «www.» совпадают и путь одного начинается с пути другого.</summary>
    public static bool SameSite(string a, string b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var x) || !Uri.TryCreate(b, UriKind.Absolute, out var y)) return false;
        static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        if (!Host(x).Equals(Host(y), StringComparison.OrdinalIgnoreCase)) return false;
        var px = x.AbsolutePath.TrimEnd('/');
        var py = y.AbsolutePath.TrimEnd('/');
        return px.StartsWith(py, StringComparison.OrdinalIgnoreCase) || py.StartsWith(px, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsYouTube(WebService s) =>
        Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && (u.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Добавить, если такого сайта в этом разделе ещё нет. Вернёт добавленный (или уже бывший) сервис.</summary>
    public static WebService Add(List<WebService> list, WebService service)
    {
        var existing = list.FirstOrDefault(s => s.Kind == service.Kind && SameSite(s.Url, service.Url));
        if (existing is not null) return existing;
        list.Add(service);
        return service;
    }

    /// <summary>Пустые и сломанные записи — прочь, дубликаты — прочь; адреса — в https-вид.</summary>
    public static List<WebService> Normalize(List<WebService>? list)
    {
        if (list is null) return Defaults();
        var result = new List<WebService>();
        foreach (var s in list)
        {
            if (s is null || NormalizeUrl(s.Url) is not { } url) continue;
            s.Url = url;
            if (string.IsNullOrWhiteSpace(s.Name)) s.Name = NameFromUrl(url);
            if (s.OpenIn is { Length: 0 }) s.OpenIn = null;
            Add(result, s);
        }
        return result;
    }
}
