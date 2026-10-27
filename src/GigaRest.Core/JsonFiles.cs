using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GigaRest.Core;

/// <summary>Чтение и атомарная запись JSON: сначала во временный файл, потом замена.</summary>
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Кириллица в файле остаётся читаемой.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Нет файла — null. Файл испорчен — откладывается как *.bad, тоже null.</summary>
    public static T? Read<T>(string path, Action<string>? onCorrupt = null) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var bad = path + ".bad";
            try { File.Copy(path, bad, overwrite: true); } catch (IOException) { }
            onCorrupt?.Invoke($"{Path.GetFileName(path)} испорчен ({ex.GetType().Name}), копия — {Path.GetFileName(bad)}");
            return null;
        }
    }
}
