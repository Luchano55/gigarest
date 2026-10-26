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

    public static List<Game> FromUninstall(IEnumerable<UninstallEntry> entries, string battleNetExe)
    {
        var games = new List<Game>();
        foreach (var e in entries)
        {
            if (!string.Equals(e.Publisher?.Trim(), "Blizzard Entertainment", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(e.DisplayName) || e.UninstallString is null) continue;
            var m = UidRegex().Match(e.UninstallString);
            if (!m.Success) continue;
            var uid = m.Groups[1].Value.Trim('"');
            if (uid.StartsWith("battle.net", StringComparison.OrdinalIgnoreCase) || uid.Equals("bna", StringComparison.OrdinalIgnoreCase)) continue;
            if (games.Any(g => g.Id.Equals("bnet:" + uid, StringComparison.OrdinalIgnoreCase))) continue;
            games.Add(new Game
            {
                Id = "bnet:" + uid, Name = e.DisplayName.Trim(), Store = "Battle.net", ExePath = battleNetExe,
                LaunchArgs = LaunchCodes.TryGetValue(uid, out var code) ? ["--exec=launch " + code] : [],
            });
        }
        return games;
    }

    [GeneratedRegex(@"--uid=(""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex UidRegex();
}
