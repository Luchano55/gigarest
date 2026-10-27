namespace GigaRest.Core;

/// <summary>Где программа хранит свои файлы: %AppData%\GigaRest.</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SessionFile => Path.Combine(Root, "session.json");
    public string LogsDir => Path.Combine(Root, "logs");

    /// <summary>Переменная GIGAREST_DATA подменяет папку (тесты, скриншоты в CI).</summary>
    public static AppPaths Default()
    {
        var overrideDir = Environment.GetEnvironmentVariable("GIGAREST_DATA");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return new AppPaths(overrideDir);
        return new AppPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GigaRest"));
    }
}
