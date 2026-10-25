using System.Text.Json;
using GigaRest.Core.Logging;

namespace GigaRest.Core.Scenes;

/// <summary>Эффект поверх сцены — один за раз (ТЗ, раздел 5).</summary>
public enum SceneEffect { None, Snow, Fog, Lights }

/// <summary>Сцена «Тихого» экрана: картинка или видео, название, эффект по умолчанию.</summary>
public sealed record Scene(string Id, string Name, string Path, bool IsVideo, SceneEffect Effect, bool BuiltIn);

/// <summary>
/// Встроенные сцены (assets/scenes, свои) + папка «Мои сцены» (по умолчанию «Изображения\GigaRest»):
/// картинки jpg/png/webp/avif и видео mp4 появляются сами. Рядом можно положить «имя.json»:
/// { "name": "Подпись", "effect": "snow" }.
/// </summary>
public static class SceneCatalog
{
    public static readonly IReadOnlyList<(string File, string Name, SceneEffect Effect)> BuiltIn =
    [
        ("mountains.jpg", "Горы на рассвете", SceneEffect.Fog),
        ("lake.jpg", "Озеро ночью", SceneEffect.Lights),
        ("winter.jpg", "Зимние холмы", SceneEffect.Snow),
        ("aurora.jpg", "Северное сияние", SceneEffect.None),
        ("dunes.jpg", "Дюны на закате", SceneEffect.None),
        ("forest.jpg", "Лес", SceneEffect.Lights),
        ("city.jpg", "Город зимой", SceneEffect.Snow),
    ];

    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".webp", ".avif", ".bmp"];
    private static readonly string[] VideoExt = [".mp4", ".m4v", ".mov", ".mkv", ".webm"];

    public static string DefaultMyScenesFolder() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "GigaRest");

    public static List<Scene> Load(string builtInDir, string? myScenesDir)
    {
        var result = new List<Scene>();
        foreach (var (file, name, effect) in BuiltIn)
        {
            var path = System.IO.Path.Combine(builtInDir, file);
            if (File.Exists(path)) result.Add(new Scene("builtin:" + file, name, path, false, effect, true));
        }
        if (myScenesDir is null || !Directory.Exists(myScenesDir)) return result;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(myScenesDir).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return result; }

        foreach (var path in files)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var isVideo = VideoExt.Contains(ext);
            if (!isVideo && !ImageExt.Contains(ext)) continue;
            var stem = System.IO.Path.GetFileNameWithoutExtension(path);
            var (name, effect) = ReadSidecar(System.IO.Path.Combine(myScenesDir, stem + ".json"));
            result.Add(new Scene("my:" + System.IO.Path.GetFileName(path), name ?? stem, path, isVideo, effect ?? SceneEffect.None, false));
        }
        return result;
    }

    /// <summary>«имя.json» рядом со сценой: подпись и эффект. Испорчен — просто не учитываем.</summary>
    internal static (string? Name, SceneEffect? Effect) ReadSidecar(string path)
    {
        if (!File.Exists(path)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            SceneEffect? effect = root.TryGetProperty("effect", out var e) && e.ValueKind == JsonValueKind.String ? ParseEffect(e.GetString()) : null;
            return (name, effect);
        }
        catch (JsonException ex)
        {
            Log.Warn($"Настройки сцены не прочитаны: {System.IO.Path.GetFileName(path)} ({ex.Message})");
            return (null, null);
        }
    }

    public static SceneEffect? ParseEffect(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "none" or "ничего" or "" => SceneEffect.None,
        "snow" or "снег" => SceneEffect.Snow,
        "fog" or "туман" => SceneEffect.Fog,
        "lights" or "fireflies" or "огоньки" => SceneEffect.Lights,
        _ => null,
    };

    public static string EffectName(SceneEffect e) => e switch
    {
        SceneEffect.Snow => "снег", SceneEffect.Fog => "туман", SceneEffect.Lights => "огоньки", _ => "",
    };
}
