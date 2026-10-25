using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GigaRest.Core.Updates;

/// <summary>
/// version.json в выпуске «свежая сборка» (dev-build): номер сборки, версия, коммит, архив и его SHA-256.
/// Пишет CI, читает программа при проверке обновлений (как в DropWheel).
/// </summary>
public sealed record UpdateManifest(int Build, string Version, string Commit, string Zip, string Sha256)
{
    public const string ManifestUrl = "https://github.com/Luchano55/gigarest/releases/download/dev-build/version.json";
    public string ZipUrl => "https://github.com/Luchano55/gigarest/releases/download/dev-build/" + Zip;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>null — файл не наш или битый.</summary>
    public static UpdateManifest? Parse(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<UpdateManifest>(json, Json);
            return m is { Build: > 0 } && !string.IsNullOrWhiteSpace(m.Zip) && !string.IsNullOrWhiteSpace(m.Sha256)
                   && !m.Zip.Contains('/') && !m.Zip.Contains('\\') ? m : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Есть ли что ставить. Сборка разработчика (0) не обновляется.</summary>
    public bool IsNewerThan(int currentBuild) => currentBuild > 0 && Build > currentBuild;
}

/// <summary>
/// Версия программы. Номер сборки — третья часть версии сборки (0.2.&lt;номер запуска CI&gt;):
/// по нему сравниваются сборки. Для людей — «0.2.0-dev.28».
/// </summary>
public static class AppVersion
{
    public static int CurrentBuild => FromVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    public static string Display => DisplayFrom(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? "разработчика";

    public static int FromVersion(Version? version) => version is { Build: > 0 } v ? v.Build : 0;

    /// <summary>«0.2.0-dev.28+abc123» → «0.2.0-dev.28».</summary>
    public static string? DisplayFrom(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return null;
        var plus = informational.IndexOf('+');
        var v = (plus >= 0 ? informational[..plus] : informational).Trim();
        return v.Length == 0 ? null : v;
    }
}

/// <summary>
/// Раскладывает скачанные файлы поверх программы. Занятый файл (запущенный GigaRest.exe) Windows не даёт
/// перезаписать, но даёт переименовать: старый → «имя.old», новый — на его место. Остатки *.old
/// удаляются при следующем запуске (<see cref="CleanupOld"/>). Ничего не удаляет у пользователя — только свои файлы.
/// </summary>
public static class UpdateApplier
{
    public const string OldSuffix = ".old";

    public static IReadOnlyList<string> Apply(string stagingDir, string targetDir)
    {
        var changed = new List<string>();
        var root = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var source in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingDir, source);
            var target = Path.GetFullPath(Path.Combine(targetDir, relative));
            // Защита: файл из архива не может лечь вне папки программы.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь вне папки программы: " + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) File.Move(target, FreeOldName(target));
            File.Move(source, target);
            changed.Add(relative);
        }
        return changed;
    }

    private static string FreeOldName(string target)
    {
        var old = target + OldSuffix;
        if (!File.Exists(old)) return old;
        try
        {
            File.Delete(old);
            return old;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{target}.{Guid.NewGuid().ToString("N")[..8]}{OldSuffix}";
        }
    }

    public static int CleanupOld(string targetDir)
    {
        var removed = 0;
        foreach (var old in Directory.EnumerateFiles(targetDir, "*" + OldSuffix, SearchOption.AllDirectories))
        {
            try { File.Delete(old); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
