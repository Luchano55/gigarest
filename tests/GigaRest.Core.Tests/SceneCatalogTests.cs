using GigaRest.Core.Scenes;

namespace GigaRest.Core.Tests;

public sealed class SceneCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", "Мои сцены " + Guid.NewGuid().ToString("N")[..6]);
    private readonly string _builtIn;
    private readonly string _mine;

    public SceneCatalogTests()
    {
        _builtIn = Path.Combine(_dir, "встроенные");
        _mine = Path.Combine(_dir, "мои");
        Directory.CreateDirectory(_builtIn);
        Directory.CreateDirectory(_mine);
        File.WriteAllText(Path.Combine(_builtIn, "mountains.jpg"), "x");
        File.WriteAllText(Path.Combine(_builtIn, "winter.jpg"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void BuiltInOnlyThoseThatExistWithDefaultEffects()
    {
        var scenes = SceneCatalog.Load(_builtIn, null);
        Assert.Equal(new[] { "Горы на рассвете", "Зимние холмы" }, scenes.Select(s => s.Name));
        Assert.Equal(SceneEffect.Snow, scenes[1].Effect);
        Assert.All(scenes, s => Assert.True(s.BuiltIn));
    }

    [Fact]
    public void MyScenesPickedUpWithSidecarSettings()
    {
        File.WriteAllText(Path.Combine(_mine, "Город ночью.jpg"), "x");
        File.WriteAllText(Path.Combine(_mine, "Город ночью.json"), """{ "name": "Город", "effect": "снег", "focus": [0.25, 1.4], "speed": 10 }""");
        File.WriteAllText(Path.Combine(_mine, "камин.mp4"), "x");
        File.WriteAllText(Path.Combine(_mine, "заметки.txt"), "x");
        File.WriteAllText(Path.Combine(_mine, "битый.png"), "x");
        File.WriteAllText(Path.Combine(_mine, "битый.json"), "{ не json");

        var mine = SceneCatalog.Load(_builtIn, _mine).Where(s => !s.BuiltIn).ToList();

        Assert.Equal(3, mine.Count);
        var storm = mine.Single(s => s.Name == "Город");
        Assert.Equal(SceneEffect.Snow, storm.Effect);
        Assert.Equal(0.25, storm.FocusX);
        Assert.Equal(1, storm.FocusY);   // за пределами — к краю
        Assert.Equal(3, storm.Speed);     // не быстрее втрое
        Assert.True(mine.Single(s => s.Name == "камин").IsVideo);
        Assert.Equal(SceneEffect.None, mine.Single(s => s.Name == "битый").Effect);
    }

    [Theory]
    [InlineData("snow", SceneEffect.Snow)]
    [InlineData("Туман", SceneEffect.Fog)]
    [InlineData("огоньки", SceneEffect.Lights)]
    [InlineData("ничего", SceneEffect.None)]
    [InlineData("Дождь", SceneEffect.Rain)]
    [InlineData("град", null)]
    public void ParsesEffects(string text, SceneEffect? expected) => Assert.Equal(expected, SceneCatalog.ParseEffect(text));
}
