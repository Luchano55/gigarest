using GigaRest.Core.Games;
using GigaRest.Core.Media;
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
    [InlineData("ru.yandex.desktop.music", "Яндекс Музыка")]
    [InlineData("Яндекс Музыка.exe", "Яндекс Музыка")]
    [InlineData("chrome.exe", "Chrome")]
    [InlineData("foobar2000.exe", "Foobar2000")]
    [InlineData(null, "")]
    public void MediaSourceName_Friendly(string? aumid, string expected) => Assert.Equal(expected, TileCaptions.MediaSourceName(aumid));

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

public class FilmLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest фильмы " + Guid.NewGuid().ToString("N"));

    public FilmLibraryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Make(string relative, long sizeMb, DateTime modified)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var f = File.Create(path)) f.SetLength(sizeMb << 20);
        File.SetLastWriteTimeUtc(path, modified);
    }

    [Fact]
    public void Scan_NewestFirst_SkipsSamplesSmallAndOther()
    {
        Make("Брат 2 (2000).mkv", 10, new DateTime(2026, 10, 1));
        Make(Path.Combine("Сериалы", "Сезон 1", "Серия 1.mp4"), 10, new DateTime(2026, 10, 20));
        Make(Path.Combine("Сериалы", "Сезон 1", "Глубоко", "Серия 2.mp4"), 10, new DateTime(2026, 10, 21)); // глубже двух уровней
        Make("Movie.2019.1080p-sample.mkv", 10, new DateTime(2026, 10, 25));
        Make("клип.mp4", 1, new DateTime(2026, 10, 25));
        Make("обложка.jpg", 10, new DateTime(2026, 10, 25));

        var films = FilmLibrary.Scan(_dir);

        Assert.Equal(["Серия 1", "Брат 2 (2000)"], films.Select(f => f.Name));
    }

    [Theory]
    [InlineData("Brat.2.2000.1080p.BluRay.x264", "Brat 2 (2000)")]
    [InlineData("Мой_отпуск", "Мой отпуск")]
    [InlineData("Интерстеллар (2014) 2160p", "Интерстеллар (2014)")]
    [InlineData("Show S01E02 1080p WEB-DL", "Show S01E02")]
    public void Title_Cleaned(string stem, string expected) => Assert.Equal(expected, FilmLibrary.Title(stem));

    [Fact]
    public void Scan_NoFolder_Empty() => Assert.Empty(FilmLibrary.Scan(Path.Combine(_dir, "нет")));
}

public class BattleNetTests
{
    [Fact]
    public void FromUninstall_KnownAndUnknownGames()
    {
        const string un = @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --lang=ruRU";
        var games = BattleNet.FromUninstall(
        [
            new("Diablo IV", "Blizzard Entertainment", un + @" --uid=fenris --displayname=""Diablo IV"""),
            new("Новая игра", "Blizzard Entertainment", un + " --uid=newgame"),
            new("Battle.net", "Blizzard Entertainment", @"""C:\Program Files (x86)\Battle.net\Battle.net.exe"" --uid=battle.net"),
            new("Notepad++", "Notepad++ Team", "uninst.exe"),
            new("Diablo IV", "Blizzard Entertainment", un + " --uid=fenris"),
        ], @"C:\Program Files (x86)\Battle.net\Battle.net.exe");

        Assert.Equal(2, games.Count);
        var d4 = games[0];
        Assert.Equal("bnet:fenris", d4.Id);
        Assert.Equal(["--exec=launch Fen"], d4.LaunchArgs);
        Assert.Equal(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", d4.ExePath);
        Assert.Empty(games[1].LaunchArgs); // неизвестная — просто открыть Battle.net
    }

    [Fact]
    public void FromUninstall_LocaleSuffixAndLastPlayed()
    {
        const string un = @"""C:\ProgramData\Battle.net\Agent\Blizzard Uninstaller.exe"" --lang=ruRU";
        var last = BattleNet.LastPlayed("""
            { "Client": {}, "Games": { "wow": { "LastActioned": "1761400000", "ServerUid": "wow_ruru" }, "hs_beta": { "Resumable": "false" } } }
            """);
        var games = BattleNet.FromUninstall(
        [
            new("World of Warcraft", "Blizzard Entertainment", un + @" --uid=wow_ruru --displayname=""World of Warcraft"""),
            new("Hearthstone", "Blizzard Entertainment", un + " --uid=hs_beta"),
        ], "bnet.exe", last);

        var wow = games.Single(g => g.Name == "World of Warcraft");
        Assert.Equal("bnet:wow", wow.Id);
        Assert.Equal(["--exec=launch WoW"], wow.LaunchArgs);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1761400000).UtcDateTime, wow.LastPlayedUtc);
        Assert.Equal(["--exec=launch WTCG"], games.Single(g => g.Name == "Hearthstone").LaunchArgs);
        Assert.Empty(BattleNet.LastPlayed("не json"));
    }
}

public class AppsTests
{
    private static readonly GigaRest.Core.Programs.ShortcutInfo[] Shortcuts =
    [
        new("Яндекс Музыка", @"C:\Users\Роман\AppData\Local\Programs\YandexMusic\Яндекс Музыка.exe"),
        new("VLC media player", @"C:\Program Files\VideoLAN\VLC\vlc.exe"),
        new("PotPlayer 64 bit", @"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe"),
        new("Справка VLC", @"C:\Program Files\VideoLAN\VLC\Documentation.url"),
        new("Блокнот", @"C:\Windows\notepad.exe"),
    ];

    [Fact]
    public void FindsMusicAndPlayers()
    {
        Assert.Equal("Яндекс Музыка", Apps.FindMusic(Shortcuts)!.Name);
        Assert.Equal(["VLC", "PotPlayer"], Apps.FindPlayers(Shortcuts).Select(p => p.Name));
        Assert.Null(Apps.FindMusic([new("Блокнот", @"C:\Windows\notepad.exe")]));
    }

    [Theory]
    [InlineData(4350, "1:12:30")]
    [InlineData(725, "12:05")]
    public void Position_Readable(double seconds, string expected) => Assert.Equal(expected, Apps.Position(seconds));

    [Theory]
    [InlineData(30, 7200, false)]
    [InlineData(3600, 7200, true)]
    [InlineData(7100, 7200, false)]
    [InlineData(600, 0, true)]
    public void WorthResuming(double pos, double duration, bool expected) => Assert.Equal(expected, Apps.WorthResuming(pos, duration));
}
