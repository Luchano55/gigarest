using GigaRest.Core.Updates;

namespace GigaRest.Core.Tests;

public sealed class UpdatesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", "Обновление " + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void ParsesManifestAndComparesBuilds()
    {
        var m = UpdateManifest.Parse("""{ "build": "31", "version": "0.2.0-dev.31", "commit": "abc", "zip": "GigaRest-win-x64.zip", "sha256": "AA" }""")!;
        Assert.Equal(31, m.Build);
        Assert.True(m.IsNewerThan(28));
        Assert.False(m.IsNewerThan(31));
        Assert.False(m.IsNewerThan(0)); // сборка разработчика не обновляется
        Assert.EndsWith("/dev-build/GigaRest-win-x64.zip", m.ZipUrl(UpdateChannels.Dev));
        Assert.EndsWith("/releases/latest/download/GigaRest-win-x64.zip", m.ZipUrl(UpdateChannels.Stable));
    }

    [Theory]
    [InlineData(null, "0.2.0", "stable")]
    [InlineData("auto", "0.2.0-dev.40", "dev")]
    [InlineData("stable", "0.2.0-dev.40", "stable")]
    [InlineData("dev", "0.2.0", "dev")]
    [InlineData("что-то", "0.3.1", "stable")]
    public void ChannelFromSettingOrVersion(string? setting, string version, string expected) =>
        Assert.Equal(expected, UpdateChannels.Resolve(setting, version));

    [Theory]
    [InlineData("не json")]
    [InlineData("""{ "build": 0, "zip": "a.zip", "sha256": "AA" }""")]
    [InlineData("""{ "build": 5, "zip": "../evil.zip", "sha256": "AA" }""")]
    [InlineData("""{ "build": 5, "zip": "a.zip" }""")]
    public void RejectsBadManifest(string json) => Assert.Null(UpdateManifest.Parse(json));

    [Fact]
    public void VersionFromAssembly()
    {
        Assert.Equal(28, AppVersion.FromVersion(new Version(0, 2, 28, 0)));
        Assert.Equal(0, AppVersion.FromVersion(new Version(0, 2, 0, 0)));
        Assert.Equal("0.2.0-dev.28", AppVersion.DisplayFrom("0.2.0-dev.28+ffbe209"));
    }

    [Fact]
    public void ApplyReplacesBusyFilesByRenamingAndCleansUp()
    {
        var app = Path.Combine(_dir, "Мои программы", "GigaRest");
        var staging = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(Path.Combine(app, "Assets"));
        Directory.CreateDirectory(Path.Combine(staging, "Assets"));
        File.WriteAllText(Path.Combine(app, "GigaRest.exe"), "старая");
        File.WriteAllText(Path.Combine(app, "settings-like.txt"), "чужое — не трогаем");
        File.WriteAllText(Path.Combine(staging, "GigaRest.exe"), "новая");
        File.WriteAllText(Path.Combine(staging, "Assets", "fog.png"), "png");

        var changed = UpdateApplier.Apply(staging, app);

        Assert.Equal(2, changed.Count);
        Assert.Equal("новая", File.ReadAllText(Path.Combine(app, "GigaRest.exe")));
        Assert.Equal("старая", File.ReadAllText(Path.Combine(app, "GigaRest.exe.old")));
        Assert.True(File.Exists(Path.Combine(app, "settings-like.txt")));
        Assert.Equal(1, UpdateApplier.CleanupOld(app));
    }
}
