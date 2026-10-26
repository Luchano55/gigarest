using GigaRest.Core.Games;
using GigaRest.Core.Tiles;
using Xunit;

namespace GigaRest.Core.Tests;

public class TileCaptionsTests
{
    [Theory]
    [InlineData("Telegram (5)", 5)]
    [InlineData("Telegram", 0)]
    [InlineData("Telegram (12)", 12)]
    public void Unread_FromTelegramTitle(string title, int expected) => Assert.Equal(expected, TileCaptions.Unread(title));

    [Fact]
    public void Telegram_ShowsUnreadOrQuiet()
    {
        Assert.Equal("3 непрочитанных", TileCaptions.For("telegram.exe", "Telegram", "Telegram (3)", null));
        Assert.Equal("1 непрочитанное", TileCaptions.For("telegram.exe", "Telegram", "Telegram (1)", null));
        Assert.Equal("нет новых сообщений", TileCaptions.For("telegram.exe", "Telegram", "Telegram", null));
    }

    [Fact]
    public void Music_ShowsTrack()
    {
        Assert.Equal("♪ Земфира — Небо Лондона", TileCaptions.For("яндекс музыка.exe", "Яндекс Музыка", "Яндекс Музыка", TileCaptions.NowPlaying("Небо Лондона", "Земфира")));
        Assert.Null(TileCaptions.NowPlaying("  ", "Земфира"));
        Assert.Equal("Небо Лондона", TileCaptions.NowPlaying("Небо Лондона", ""));
    }

    [Fact]
    public void Browser_ShowsPageTitleWithoutBrowserName()
    {
        Assert.Equal("Почта", TileCaptions.For("chrome.exe", "Chrome", "Почта - Google Chrome", null));
        Assert.Equal("YouTube", TileCaptions.For("zen.exe", "Zen", "YouTube — Zen Browser", null));
        Assert.Null(TileCaptions.For("claude.exe", "Claude", "Claude", null));
        Assert.Null(TileCaptions.For("claude.exe", "Claude", "", null));
    }

    [Theory]
    [InlineData("Яндекс Музыка.exe", "яндекс музыка.exe", @"C:\Users\Роман\AppData\Local\Programs\YandexMusic\Яндекс Музыка.exe", true)]
    [InlineData("ru.yandex.desktop.music", "яндекс музыка.exe", null, true)]
    [InlineData("chrome.exe", "chrome.exe", null, true)]
    [InlineData("Chrome", "chrome.exe", null, true)]
    [InlineData("Spotify.exe", "яндекс музыка.exe", null, false)]
    [InlineData("", "chrome.exe", null, false)]
    public void Media_MatchesTileProgram(string aumid, string programId, string? exe, bool expected) =>
        Assert.Equal(expected, TileCaptions.MediaMatches(aumid, programId, exe));
}

public class GameLibrariesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest игры " + Guid.NewGuid().ToString("N"));

    public GameLibrariesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Acf(string id, string name, long lastPlayed, int flags = 4) => $$"""
        "AppState"
        {
        	"appid"		"{{id}}"
        	"name"		"{{name}}"
        	"StateFlags"		"{{flags}}"
        	"LastPlayed"		"{{lastPlayed}}"
        }
        """;

    [Fact]
    public void Steam_ReadsAllLibraries_SkipsToolsAndUnfinished()
    {
        var steam = Path.Combine(_dir, "Steam");
        var second = Path.Combine(_dir, "Игры на D");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(second, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"{{steam.Replace("\\", "\\\\")}}"
            		"apps" { "570" "123" }
            	}
            	"1"
            	{
            		"path"		"{{second.Replace("\\", "\\\\")}}"
            	}
            }
            """);
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_570.acf"), Acf("570", "Dota 2", 1761400000));
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_228980.acf"), Acf("228980", "Steamworks Common Redistributables", 0));
        File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_1245620.acf"), Acf("1245620", "ELDEN RING", 0));
        File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_999.acf"), Acf("999", "Качается", 0, flags: 1026));

        var games = GameLibraries.Steam(steam);

        Assert.Equal(["Dota 2", "ELDEN RING"], games.Select(g => g.Name).OrderBy(n => n));
        var dota = games.Single(g => g.Id == "steam:570");
        Assert.Equal("steam://rungameid/570", dota.LaunchUri);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1761400000).UtcDateTime, dota.LastPlayedUtc);
        Assert.Null(games.Single(g => g.Id == "steam:1245620").LastPlayedUtc);
    }

    [Fact]
    public void Steam_NoFolder_Empty() => Assert.Empty(GameLibraries.Steam(Path.Combine(_dir, "нет")));

    [Fact]
    public void Epic_ReadsGames_SkipsIncompleteAndNonGames()
    {
        File.WriteAllText(Path.Combine(_dir, "a.item"), """
            { "DisplayName": "Alan Wake 2", "AppName": "Dill", "CatalogNamespace": "dc9d", "CatalogItemId": "4a5f", "AppCategories": ["public", "games", "applications"] }
            """);
        File.WriteAllText(Path.Combine(_dir, "b.item"), """{ "DisplayName": "Недокачано", "AppName": "X", "bIsIncompleteInstall": true }""");
        File.WriteAllText(Path.Combine(_dir, "c.item"), """{ "DisplayName": "Unreal Plugin", "AppName": "P", "AppCategories": ["plugins"] }""");
        File.WriteAllText(Path.Combine(_dir, "d.item"), "не json");

        var games = GameLibraries.Epic(_dir);

        var g = Assert.Single(games);
        Assert.Equal("epic:Dill", g.Id);
        Assert.Equal("com.epicgames.launcher://apps/dc9d%3A4a5f%3ADill?action=launch&silent=true", g.LaunchUri);
    }

    [Fact]
    public void Merge_RecentFirst_UsesOurLaunches()
    {
        var games = new[]
        {
            new Game { Id = "steam:1", Name = "Б", Store = "Steam", LastPlayedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Game { Id = "epic:a", Name = "А", Store = "Epic" },
            new Game { Id = "gog:2", Name = "В", Store = "GOG" },
            new Game { Id = "steam:3", Name = "Г", Store = "Steam" },
        };
        var ours = new Dictionary<string, DateTime> { ["gog:2"] = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) };

        var list = GameLibraries.Merge(games, ours);

        Assert.Equal(["В", "Б", "А", "Г"], list.Select(g => g.Name));
    }

    [Fact]
    public void LastPlayedText_Russian()
    {
        var now = new DateTime(2026, 10, 26, 20, 0, 0);
        DateTime Ago(int days) => now.AddDays(-days).ToUniversalTime();
        Assert.Equal("сегодня", GameLibraries.LastPlayedText(Ago(0), now));
        Assert.Equal("вчера", GameLibraries.LastPlayedText(Ago(1), now));
        Assert.Equal("3 дня назад", GameLibraries.LastPlayedText(Ago(3), now));
        Assert.Equal("2 недели назад", GameLibraries.LastPlayedText(Ago(15), now));
        Assert.Equal("", GameLibraries.LastPlayedText(null, now));
    }

    [Fact]
    public void Vdf_EscapesAndComments()
    {
        var n = VdfNode.Parse("// комментарий\n\"a\" { \"path\" \"C:\\\\Games\\\\Steam\" \"q\" \"он сказал \\\"да\\\"\" }");
        Assert.Equal(@"C:\Games\Steam", n.Child("A")!["PATH"]);
        Assert.Equal("он сказал \"да\"", n.Child("a")!["q"]);
    }
}
