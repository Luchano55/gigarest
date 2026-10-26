using System.Text.Json;
using GigaRest.Core.Logging;

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
                    games.Add(new Game { Id = "steam:" + id, Name = name.Trim(), Store = "Steam", LastPlayedUtc = last, LaunchUri = "steam://rungameid/" + id });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Steam, {Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        return games;
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
                games.Add(new Game
                {
                    Id = "epic:" + app, Name = name.Trim(), Store = "Epic",
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
            <= 0 => "сегодня",
            1 => "вчера",
            < 7 => Text.Sizes.Plural(days, "день", "дня", "дней") + " назад",
            < 31 => Text.Sizes.Plural(days / 7, "неделю", "недели", "недель") + " назад",
            _ => lastUtc.Value.ToLocalTime().ToString(lastUtc.Value.Year == nowLocal.Year ? "d MMMM" : "d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
        };
    }
}
