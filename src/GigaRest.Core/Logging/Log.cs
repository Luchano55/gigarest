using System.Globalization;

namespace GigaRest.Core.Logging;

/// <summary>
/// Журнал в %AppData%\GigaRest\logs, файл на день, храним 7 дней.
/// Заголовки окон сюда не пишем — только имена программ и коды.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _dir;
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(7);

    /// <summary>Последние строки — для тестов и окна «что произошло».</summary>
    public static event Action<string>? Written;

    public static void Init(string dir, DateTime? now = null)
    {
        _dir = dir;
        try
        {
            Directory.CreateDirectory(dir);
            Cleanup(dir, now ?? DateTime.Now);
        }
        catch (Exception) { /* журнал не должен ронять программу */ }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var now = DateTime.Now;
        var line = $"{now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {message}";
        Written?.Invoke(line);
        if (_dir is null)
            return;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_dir, FileName(now)), line + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }

    internal static string FileName(DateTime day) =>
        $"gigarest-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

    internal static void Cleanup(string dir, DateTime now)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "gigarest-*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(file)["gigarest-".Length..];
            if (DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && now.Date - day.Date >= Retention)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}
