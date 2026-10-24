using GigaRest.Core.Logging;

namespace GigaRest.Core.Settings;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        var settings = JsonFiles.Read<AppSettings>(_path, Log.Warn) ?? new AppSettings();
        Normalize(settings);
        return settings;
    }

    public void Save(AppSettings settings) => JsonFiles.Write(_path, settings);

    internal static void Normalize(AppSettings s)
    {
        s.Programs ??= [];
        s.Programs.RemoveAll(p => string.IsNullOrWhiteSpace(p.Id));
        // Дубликаты по ключу: оставляем первую запись.
        s.Programs = s.Programs
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (s.CloseTimeoutSeconds is < 5 or > 600)
            s.CloseTimeoutSeconds = 30;
        if (string.IsNullOrWhiteSpace(s.Hotkey))
            s.Hotkey = "Ctrl+Alt+Home";
    }
}
