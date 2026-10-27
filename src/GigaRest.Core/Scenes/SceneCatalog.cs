using System.Text.Json;
using GigaRest.Core.Logging;
using GigaRest.Core.Text;

namespace GigaRest.Core.Scenes;

/// <summary>Эффект поверх сцены — один за раз (ТЗ, раздел 5).</summary>
public enum SceneEffect { None, Snow, Fog, Lights, Rain }

/// <summary>
/// Сцена «Тихого» экрана: картинка или видео, название, эффект по умолчанию,
/// точка фокуса (0..1 по ширине и высоте — к ней «наезжает» медленное движение) и скорость движения (1 — обычная).
/// </summary>
public sealed record Scene(string Id, string Name, string Path, bool IsVideo, SceneEffect Effect, bool BuiltIn,
    double FocusX = 0.5, double FocusY = 0.5, double Speed = 1);

/// <summary>
/// Встроенные сцены (assets/scenes, свои) + папка «Мои сцены» (по умолчанию «Изображения\GigaRest»):
/// картинки jpg/png/webp/avif и видео mp4 появляются сами. Рядом можно положить «имя.json»:
/// { "name": "Подпись", "effect": "snow", "focus": [0.3, 0.6], "speed": 0.5 }.
/// </summary>
public static class SceneCatalog
{
    public static IReadOnlyList<(string File, string Name, SceneEffect Effect)> BuiltIn =>
    [
        ("mountains.jpg", L.T("Горы на рассвете", "Mountains at dawn"), SceneEffect.Fog),
        ("lake.jpg", L.T("Озеро ночью", "Lake at night"), SceneEffect.Lights),
        ("winter.jpg", L.T("Зимние холмы", "Winter hills"), SceneEffect.Snow),
        ("aurora.jpg", L.T("Северное сияние", "Northern lights"), SceneEffect.None),
        ("dunes.jpg", L.T("Дюны на закате", "Dunes at sunset"), SceneEffect.None),
        ("forest.jpg", L.T("Лес", "Forest"), SceneEffect.Lights),
        ("city.jpg", L.T("Город зимой", "City in winter"), SceneEffect.Snow),
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
            var side = ReadSidecar(System.IO.Path.Combine(myScenesDir, stem + ".json"));
            result.Add(new Scene("my:" + System.IO.Path.GetFileName(path), side.Name ?? stem, path, isVideo, side.Effect ?? SceneEffect.None, false,
                side.FocusX, side.FocusY, side.Speed));
        }
        return result;
    }

    internal sealed record Sidecar(string? Name, SceneEffect? Effect, double FocusX = 0.5, double FocusY = 0.5, double Speed = 1);

    /// <summary>«имя.json» рядом со сценой: подпись, эффект, точка фокуса, скорость. Испорчен — просто не учитываем.</summary>
    internal static Sidecar ReadSidecar(string path)
    {
        if (!File.Exists(path)) return new(null, null);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            SceneEffect? effect = root.TryGetProperty("effect", out var e) && e.ValueKind == JsonValueKind.String ? ParseEffect(e.GetString()) : null;
            double fx = 0.5, fy = 0.5, speed = 1;
            if (root.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.Array && f.GetArrayLength() == 2
                && f[0].TryGetDouble(out var x) && f[1].TryGetDouble(out var y))
            {
                fx = Math.Clamp(x, 0, 1);
                fy = Math.Clamp(y, 0, 1);
            }
            if (root.TryGetProperty("speed", out var sp) && sp.TryGetDouble(out var v))
                speed = Math.Clamp(v, 0.25, 3);
            return new(name, effect, fx, fy, speed);
        }
        catch (JsonException ex)
        {
            Log.Warn($"Настройки сцены не прочитаны: {System.IO.Path.GetFileName(path)} ({ex.Message})");
            return new(null, null);
        }
    }

    public static SceneEffect? ParseEffect(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "none" or "ничего" or "" => SceneEffect.None,
        "snow" or "снег" => SceneEffect.Snow,
        "fog" or "туман" => SceneEffect.Fog,
        "lights" or "fireflies" or "огоньки" => SceneEffect.Lights,
        "rain" or "дождь" => SceneEffect.Rain,
        _ => null,
    };

    public static string EffectName(SceneEffect e) => e switch
    {
        SceneEffect.Snow => L.T("снег", "snow"), SceneEffect.Fog => L.T("туман", "fog"), SceneEffect.Lights => L.T("огоньки", "fireflies"), SceneEffect.Rain => L.T("дождь", "rain"), _ => "",
    };
}
