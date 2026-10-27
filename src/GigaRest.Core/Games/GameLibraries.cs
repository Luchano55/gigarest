using System.Text.Json;
using GigaRest.Core.Logging;
using GigaRest.Core.Text;

namespace GigaRest.Core.Games;

/// <summary>Разбор библиотек игр: Steam (libraryfolders.vdf + appmanifest_*.acf) и Epic (*.item). Только чтение.</summary>
public static class GameLibraries
{
    // Не игры: служебные пакеты Steam.
    private static readonly HashSet<string> SteamTools = ["228980", "1070560", "1391110", "1628350", "1493710", "250820", "1826330"];

    /// <summary>Игры Steam. steamRoot — папка Steam (из реестра HKCU\Software\Valve\Steam, SteamPath).</summary>
    public static List<Game> Steam(string steamRoot)
    {
        var games = new List<Game>();
        var libraries = new List<string> { steamRoot };
        var foldersFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(foldersFile))
        {
            try
            {
                var root = VdfNode.Parse(File.ReadAllText(foldersFile));
                var folders = root.Child("libraryfolders") ?? root;
                foreach (var f in folders.Children.Values)
                    if (f["path"] is { Length: > 0 } p) libraries.Add(p.Replace(@"\\", @"\"));
                foreach (var v in folders.Values) // старый формат: "1" "D:\\Games\\Steam"
                    if (v.Key.All(char.IsDigit) && v.Value.Length > 0) libraries.Add(v.Value);
            }
            catch (IOException ex) { Log.Warn("Steam, список библиотек: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log.Warn("Steam, список библиотек: " + ex.Message); }
        }

        var seen = new HashSet<string>();
        Dictionary<string, (int Total, int TwoWeeks)>? playtime = null;
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            string[] manifests;
            try { manifests = Directory.GetFiles(apps, "appmanifest_*.acf"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in manifests)
            {
                try
                {
                    var state = VdfNode.Parse(File.ReadAllText(file)).Child("AppState");
                    var id = state?["appid"];
                    var name = state?["name"];
                    if (id is null || string.IsNullOrWhiteSpace(name) || SteamTools.Contains(id) || !seen.Add(id)) continue;
                    if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)) continue;
                    // StateFlags: 4 — установлена полностью (бывают и вместе с другими флагами, например обновление).
                    if (int.TryParse(state!["StateFlags"], out var flags) && (flags & 4) == 0) continue;
                    DateTime? last = long.TryParse(state["LastPlayed"], out var unix) && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null;
                    playtime ??= SteamPlaytime(steamRoot);
                    var played = playtime.TryGetValue(id, out var pt) ? pt : (0, 0);
                    games.Add(new Game
                    {
                        Id = "steam:" + id, Name = name.Trim(), Store = "Steam", LastPlayedUtc = last, LaunchUri = "steam://rungameid/" + id,
                        PlayMinutes = played.Item1 > 0 ? played.Item1 : null, PlayMinutes2Weeks = played.Item2 > 0 ? played.Item2 : null,
                        CoverPath = SteamCover(steamRoot, id),
                        HeroPath = SteamCacheFile(steamRoot, id, "library_hero.jpg"),
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Steam, {Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        return games;
    }

    // Порядок важен: сначала вертикальная обложка, потом шапка.
    private static readonly string[] SteamCoverNames = ["library_600x900.jpg", "library_600x900_2x.jpg", "header.jpg", "library_header.jpg"];

    /// <summary>
    /// Обложка из кэша Steam: appcache\librarycache. Старый вид — «570_library_600x900.jpg» прямо в папке,
    /// новый — папка «570» (иногда с подпапкой) и «library_600x900.jpg» в ней.
    /// </summary>
    public static string? SteamCover(string steamRoot, string appId)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        if (!Directory.Exists(cache)) return null;
        foreach (var name in SteamCoverNames)
        {
            var flat = Path.Combine(cache, $"{appId}_{name}");
            if (File.Exists(flat)) return flat;
        }
        var folder = Path.Combine(cache, appId);
        if (!Directory.Exists(folder)) return null;
        try
        {
            var files = Directory.GetFiles(folder, "*.jpg", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true });
            foreach (var name in SteamCoverNames)
            {
                var hit = files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// Сколько наиграно в Steam: userdata\&lt;id&gt;\config\localconfig.vdf → apps → &lt;appid&gt; → Playtime, Playtime2wks (минуты).
    /// Если на компьютере несколько учётных записей Steam — берём наибольшее.
    /// </summary>
    public static Dictionary<string, (int Total, int TwoWeeks)> SteamPlaytime(string steamRoot)
    {
        var result = new Dictionary<string, (int, int)>();
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata)) return result;
        try
        {
            foreach (var user in Directory.GetDirectories(userdata))
            {
                var file = Path.Combine(user, "config", "localconfig.vdf");
                if (!File.Exists(file)) continue;
                var apps = VdfNode.Parse(File.ReadAllText(file)).Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps");
                if (apps is null) continue;
                foreach (var (id, app) in apps.Children)
                {
                    int.TryParse(app["Playtime"], out var total);
                    int.TryParse(app["Playtime2wks"], out var two);
                    if (total == 0 && two == 0) continue;
                    var had = result.TryGetValue(id, out var h) ? h : (0, 0);
                    result[id] = (Math.Max(had.Item1, total), Math.Max(had.Item2, two));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Steam, время в играх: " + ex.Message); }
        return result;
    }

    /// <summary>«12 ч в игре», «40 мин в игре».</summary>
    public static string PlaytimeText(int minutes) => minutes >= 60
        ? L.T($"{minutes / 60} ч в игре", $"{minutes / 60} h played")
        : L.T($"{minutes} мин в игре", $"{minutes} min played");

    /// <summary>Файл из кэша Steam по имени — в старом («570_имя») или новом («570\\…\\имя») виде.</summary>
    public static string? SteamCacheFile(string steamRoot, string appId, string name)
    {
        var cache = Path.Combine(steamRoot, "appcache", "librarycache");
        var flat = Path.Combine(cache, $"{appId}_{name}");
        if (File.Exists(flat)) return flat;
        var folder = Path.Combine(cache, appId);
        if (!Directory.Exists(folder)) return null;
        try
        {
            return Directory.GetFiles(folder, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Библиотека: магазин (null — все), поиск по названию (без регистра, «ё» = «е»), порядок — недавние или по алфавиту.
    /// </summary>
    public static List<Game> Filter(IEnumerable<Game> games, string? store, string? query, bool byName)
    {
        static string Norm(string s) => s.ToLowerInvariant().Replace('ё', 'е');
        var q = string.IsNullOrWhiteSpace(query) ? null : Norm(query.Trim());
        var list = games.Where(g => store is null || g.Store == store)
            .Where(g => q is null || Norm(g.Name).Contains(q));
        return (byName
            ? list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            : list.OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Игры Epic. manifestsDir — %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests.</summary>
    public static List<Game> Epic(string manifestsDir)
    {
        var games = new List<Game>();
        if (!Directory.Exists(manifestsDir)) return games;
        string[] files;
        try { files = Directory.GetFiles(manifestsDir, "*.item"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return games; }
        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                bool B(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
                var name = S("DisplayName");
                var app = S("AppName");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(app) || B("bIsIncompleteInstall")) continue;
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
                    && !cats.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games")) continue;
                var ns = S("CatalogNamespace");
                var item = S("CatalogItemId");
                var id = ns is not null && item is not null ? $"{ns}%3A{item}%3A{app}" : app;
                var location = S("InstallLocation");
                var exe = S("LaunchExecutable");
                games.Add(new Game
                {
                    Id = "epic:" + app, Name = name.Trim(), Store = "Epic",
                    IconPath = location is not null && exe is not null ? Path.Combine(location, exe) : null,
                    LaunchUri = $"com.epicgames.launcher://apps/{id}?action=launch&silent=true",
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Log.Warn($"Epic, {Path.GetFileName(file)}: {ex.Message}"); }
        }
        return games;
    }

    /// <summary>
    /// Общий список: сначала то, во что играли недавно (по данным магазина или по запускам из GigaRest), потом по алфавиту.
    /// </summary>
    public static List<Game> Merge(IEnumerable<Game> games, IReadOnlyDictionary<string, DateTime> playedFromGigaRest)
    {
        return games
            .GroupBy(g => g.Id).Select(g => g.First())
            .Select(g => playedFromGigaRest.TryGetValue(g.Id, out var ours) && (g.LastPlayedUtc is null || ours > g.LastPlayedUtc) ? g with { LastPlayedUtc = ours } : g)
            .OrderByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>«сегодня», «вчера», «3 дня назад», «2 недели назад», «12 марта».</summary>
    public static string LastPlayedText(DateTime? lastUtc, DateTime nowLocal)
    {
        if (lastUtc is null) return "";
        var days = (nowLocal.Date - lastUtc.Value.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => L.T("сегодня", "today"),
            1 => L.T("вчера", "yesterday"),
            < 7 => L.Plural(days, "день", "дня", "дней", "day", "days") + L.T(" назад", " ago"),
            < 14 => L.T("неделю назад", "a week ago"),
            < 31 => L.Plural(days / 7, "неделю", "недели", "недель", "week", "weeks") + L.T(" назад", " ago"),
            _ => lastUtc.Value.ToLocalTime().ToString(lastUtc.Value.Year == nowLocal.Year ? L.T("d MMMM", "MMMM d") : L.T("d MMMM yyyy", "MMMM d, yyyy"), L.Culture),
        };
    }
}
