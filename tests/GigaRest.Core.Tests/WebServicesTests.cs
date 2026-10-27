using GigaRest.Core.Media;
using Xunit;

namespace GigaRest.Core.Tests;

public class WebServicesTests
{
    [Theory]
    [InlineData("spotify.com", "https://spotify.com/")]
    [InlineData("  https://open.spotify.com/  ", "https://open.spotify.com/")]
    [InlineData("vk.com/music", "https://vk.com/music")]
    [InlineData("http://example.org/a", "http://example.org/a")]
    public void NormalizeUrl_MakesHttpsAddress(string text, string expected) =>
        Assert.Equal(expected, WebServices.NormalizeUrl(text));

    [Theory]
    [InlineData("")]
    [InlineData("просто текст")]
    [InlineData("localhost")]
    [InlineData("ftp://files.example.org/")]
    [InlineData("file:///C:/Фильмы/")]
    public void NormalizeUrl_RejectsNonSites(string text) => Assert.Null(WebServices.NormalizeUrl(text));

    [Theory]
    [InlineData("https://open.spotify.com/", "Spotify")]
    [InlineData("https://www.twitch.tv/", "Twitch")]
    [InlineData("https://kino.example.ru/", "Example")]
    [InlineData("https://music.mysite.org/", "Mysite")]
    public void NameFromUrl(string url, string name) => Assert.Equal(name, WebServices.NameFromUrl(url));

    [Fact]
    public void Defaults_YouTubeAndYandexMusicInside()
    {
        var list = WebServices.Normalize(null);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Kind == ServiceKind.Watch && WebServices.IsYouTube(s) && s.OpenIn is null);
        Assert.Contains(list, s => s.Kind == ServiceKind.Listen && s.Name == "Яндекс Музыка" && s.OpenIn is null);
    }

    [Fact]
    public void ProtectedPreset_OpensInBrowser()
    {
        var spotify = WebServices.FromPreset(WebServices.Presets.First(p => p.Name == "Spotify"));
        Assert.Equal(WebServices.DefaultBrowser, spotify.OpenIn);
        Assert.Equal(ServiceKind.Listen, spotify.Kind);
    }

    [Fact]
    public void Add_SkipsSameSiteInSameSection()
    {
        var list = WebServices.Defaults();
        var again = WebServices.Add(list, new WebService { Name = "YT", Url = "https://youtube.com", Kind = ServiceKind.Watch });
        Assert.Equal(2, list.Count);
        Assert.Equal("YouTube", again.Name);
        WebServices.Add(list, new WebService { Name = "YT", Url = "https://youtube.com", Kind = ServiceKind.Listen });
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Normalize_DropsBrokenAndFillsNames()
    {
        var list = WebServices.Normalize(
        [
            new WebService { Name = "", Url = "soundcloud.com", Kind = ServiceKind.Listen, OpenIn = "" },
            new WebService { Name = "Плохой", Url = "не адрес", Kind = ServiceKind.Watch },
        ]);
        var s = Assert.Single(list);
        Assert.Equal("SoundCloud", s.Name);
        Assert.Equal("https://soundcloud.com/", s.Url);
        Assert.Null(s.OpenIn);
    }

    [Fact]
    public void FindBrowsers_ByShortcutNameOrExe()
    {
        var shortcuts = new List<GigaRest.Core.Programs.ShortcutInfo>
        {
            new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            new("Yandex", @"C:\Users\Роман\AppData\Local\Yandex\YandexBrowser\Application\browser.exe"),
            new("Блокнот", @"C:\Windows\notepad.exe"),
        };
        var browsers = Apps.FindBrowsers(shortcuts);
        Assert.Equal(["Chrome", "Яндекс Браузер"], browsers.Select(b => b.Name));
    }
}
