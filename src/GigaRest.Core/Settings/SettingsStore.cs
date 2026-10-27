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
        s.GamesPlayed ??= [];
        s.FilmsOpened ??= [];
        s.CustomGames ??= [];
        s.CustomGames.RemoveAll(g => string.IsNullOrWhiteSpace(g.Path));
        s.FilmsPosition ??= [];
        s.WebServices = Media.WebServices.Normalize(s.WebServices);
        s.TilesCount = Math.Clamp(s.TilesCount, 1, 4);
        s.DimMinutes = Math.Clamp(s.DimMinutes, 1, 240);
        s.ScreenOffMinutes = Math.Clamp(s.ScreenOffMinutes, 0, 600);
        s.UpdateChannel = Updates.UpdateChannels.Normalize(s.UpdateChannel);
        s.ReturnSchedule ??= new DaySchedule();
        s.RestOffer ??= new DaySchedule { Time = "19:00" };
        foreach (var schedule in new[] { s.ReturnSchedule, s.RestOffer })
            schedule.Days = (schedule.Days ?? []).Distinct().ToList();
        s.Programs.RemoveAll(p => string.IsNullOrWhiteSpace(p.Id));
        // Яндекс Музыкой управляет «Слушать» на основном экране — на второй монитор её больше не переносим (один раз).
        if (!s.MusicOnMainScreen)
        {
            foreach (var p in s.Programs.Where(p => p.Id.Equals("яндекс музыка.exe", StringComparison.OrdinalIgnoreCase) && p.Action == ProgramAction.SecondMonitor))
                p.Action = ProgramAction.Keep;
            s.MusicOnMainScreen = true;
        }
        // Дубликаты по ключу: оставляем первую запись.
        s.Programs = s.Programs
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (s.CloseTimeoutSeconds is < 5 or > 600)
            s.CloseTimeoutSeconds = 30;
        if (string.IsNullOrWhiteSpace(s.Hotkey))
            s.Hotkey = "Ctrl+Alt+Home";
        if (string.IsNullOrWhiteSpace(s.TilesHotkey) || s.TilesHotkey.Equals(s.Hotkey, StringComparison.OrdinalIgnoreCase))
            s.TilesHotkey = "Ctrl+Alt+End";
    }
}
