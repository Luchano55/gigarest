using System.Text.Json;
using System.Text.RegularExpressions;

namespace GigaRest.Core.Games;

/// <summary>Запись из «Установка и удаление программ» (реестр Uninstall).</summary>
public sealed record UninstallEntry(string? DisplayName, string? Publisher, string? UninstallString);

/// <summary>
/// Игры Battle.net. Свой список Battle.net хранит в закрытом виде, но каждая игра записана в «Установка и удаление программ»
/// с кодом продукта: «Blizzard Uninstaller.exe --uid=fenris». Код запуска для известных игр — из таблицы ниже;
/// для неизвестной игры просто открываем Battle.net (ТЗ: «если запустить не удалось — открыть лаунчер»).
/// </summary>
public static partial class BattleNet
{
    // Код в «Удалении программ» → код запуска Battle.net.exe --exec="launch <код>".
    private static readonly Dictionary<string, string> LaunchCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fenris"] = "Fen",          // Diablo IV
        ["diablo3"] = "D3",          // Diablo III
        ["osi"] = "OSI",             // Diablo II: Resurrected
        ["anbs"] = "ANBS",           // Diablo Immortal
        ["prometheus"] = "Pro",      // Overwatch 2
        ["hs_beta"] = "WTCG",        // Hearthstone
        ["heroes"] = "Hero",         // Heroes of the Storm
        ["s2"] = "S2",               // StarCraft II
        ["s1"] = "S1",               // StarCraft: Remastered
        ["w3"] = "W3",               // Warcraft III: Reforged
        ["wow"] = "WoW",             // World of Warcraft
        ["auks"] = "AUKS",           // Call of Duty (HQ, Warzone)
    };

    // Код в «Удалении программ» часто с языком: «wow_ruru», «diablo3_enus». Язык отрезаем.
    private static readonly HashSet<string> Locales = new(StringComparer.OrdinalIgnoreCase)
    {
        "enus", "engb", "ruru", "dede", "frfr", "eses", "esmx", "itit", "kokr", "ptbr", "ptpt", "zhcn", "zhtw", "plpl", "jajp", "thth", "trtr",
    };

    /// <summary>«wow_ruru» → «wow»; «hs_beta» остаётся.</summary>
    public static string ProductCode(string uid)
    {
        var i = uid.LastIndexOf('_');
        return i > 0 && Locales.Contains(uid[(i + 1)..]) ? uid[..i] : uid;
    }

    /// <summary>
    /// Когда играли: %AppData%\Battle.net\Battle.net.config (JSON), раздел «Games» — код продукта → «LastActioned» (время Unix).
    /// Файл не наш и может поменяться — берём, что прочиталось.
    /// </summary>
    public static Dictionary<string, DateTime> LastPlayed(string configJson)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("Games", out var games) || games.ValueKind != JsonValueKind.Object) return result;
            foreach (var game in games.EnumerateObject())
            {
                if (game.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in game.Value.EnumerateObject())
                {
                    if (!p.Name.Equals("LastActioned", StringComparison.OrdinalIgnoreCase) && !p.Name.Equals("LastPlayed", StringComparison.OrdinalIgnoreCase)) continue;
                    var text = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                    if (long.TryParse(text, out var unix) && unix > 1_000_000_000)
                    {
                        var when = DateTimeOffset.FromUnixTimeSeconds(unix > 100_000_000_000 ? unix / 1000 : unix).UtcDateTime;
                        var code = ProductCode(game.Name);
                        if (!result.TryGetValue(code, out var had) || when > had) result[code] = when;
                    }
                }
            }
        }
        catch (JsonException) { }
        return result;
    }

    public static List<Game> FromUninstall(IEnumerable<UninstallEntry> entries, string battleNetExe, IReadOnlyDictionary<string, DateTime>? lastPlayed = null)
    {
        var games = new List<Game>();
        foreach (var e in entries)
        {
            if (!string.Equals(e.Publisher?.Trim(), "Blizzard Entertainment", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(e.DisplayName) || e.UninstallString is null) continue;
            var m = UidRegex().Match(e.UninstallString);
            if (!m.Success) continue;
            var uid = ProductCode(m.Groups[1].Value.Trim('"'));
            if (uid.StartsWith("battle.net", StringComparison.OrdinalIgnoreCase) || uid.Equals("bna", StringComparison.OrdinalIgnoreCase)) continue;
            if (games.Any(g => g.Id.Equals("bnet:" + uid, StringComparison.OrdinalIgnoreCase))) continue;
            games.Add(new Game
            {
                Id = "bnet:" + uid, Name = e.DisplayName.Trim(), Store = "Battle.net", ExePath = battleNetExe,
                LaunchArgs = LaunchCodes.TryGetValue(uid, out var code) ? ["--exec=launch " + code] : [],
                LastPlayedUtc = lastPlayed is not null && lastPlayed.TryGetValue(uid, out var last) ? last : null,
            });
        }
        return games;
    }

    [GeneratedRegex(@"--uid=(""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UidRegex();
}
