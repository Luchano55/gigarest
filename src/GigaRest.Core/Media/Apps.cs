using GigaRest.Core.Programs;

namespace GigaRest.Core.Media;

/// <summary>Программа, которую можно открыть из «Слушать» или выбрать плеером в «Смотреть».</summary>
public sealed record AppChoice(string Name, string Path);

/// <summary>Музыкальные программы и видеоплееры среди ярлыков меню «Пуск».</summary>
public static class Apps
{
    // Название ярлыка (без регистра) или имя exe → понятное имя. Порядок — приоритет.
    private static readonly (string[] Keys, string Name)[] Music =
    [
        (["яндекс музыка", "yandex music", "яндекс музыка.exe", "yandexmusic.exe"], "Яндекс Музыка"),
        (["vk музыка", "vk music"], "VK Музыка"),
        (["spotify", "spotify.exe"], "Spotify"),
        (["aimp", "aimp.exe"], "AIMP"),
        (["foobar2000", "foobar2000.exe"], "foobar2000"),
    ];

    private static readonly (string[] Keys, string Name)[] Players =
    [
        (["vlc media player", "vlc.exe"], "VLC"),
        (["mpc-hc", "mpc-hc64.exe", "mpc-hc.exe"], "MPC-HC"),
        (["mpc-be", "mpc-be64.exe", "mpc-be.exe"], "MPC-BE"),
        (["potplayer", "potplayer 64 bit", "potplayermini64.exe", "potplayermini.exe"], "PotPlayer"),
        (["kmplayer", "kmplayer64.exe", "kmplayer.exe"], "KMPlayer"),
        (["mpv", "mpv.exe"], "mpv"),
    ];

    // Браузеры: ярлык в «Пуске» или имя exe. Яндекс Браузер — по названию ярлыка (его exe — просто browser.exe).
    private static readonly (string[] Keys, string Name)[] Browsers =
    [
        (["google chrome", "chrome.exe"], "Chrome"),
        (["microsoft edge", "msedge.exe"], "Edge"),
        (["firefox", "mozilla firefox", "firefox.exe"], "Firefox"),
        (["yandex", "yandex browser", "яндекс браузер", "яндекс.браузер"], "Яндекс Браузер"),
        (["opera browser", "opera"], "Opera"),
        (["opera gx browser", "opera gx"], "Opera GX"),
        (["brave", "brave.exe"], "Brave"),
        (["vivaldi", "vivaldi.exe"], "Vivaldi"),
        (["zen browser", "zen", "zen.exe"], "Zen"),
        (["arc", "arc.exe"], "Arc"),
        (["librewolf", "librewolf.exe"], "LibreWolf"),
        (["floorp", "floorp.exe"], "Floorp"),
        (["waterfox", "waterfox.exe"], "Waterfox"),
    ];

    public static List<AppChoice> FindBrowsers(IEnumerable<ShortcutInfo> shortcuts) => Find(shortcuts, Browsers);

    public static AppChoice? FindMusic(IEnumerable<ShortcutInfo> shortcuts) => Find(shortcuts, Music).FirstOrDefault();

    public static List<AppChoice> FindPlayers(IEnumerable<ShortcutInfo> shortcuts) => Find(shortcuts, Players);

    private static List<AppChoice> Find(IEnumerable<ShortcutInfo> shortcuts, (string[] Keys, string Name)[] known)
    {
        var list = shortcuts.Where(s => s.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<AppChoice>();
        foreach (var (keys, name) in known)
        {
            var hit = list.FirstOrDefault(s => keys.Any(k => string.Equals(s.Name, k, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(System.IO.Path.GetFileName(s.TargetPath.Replace('\\', '/')), k, StringComparison.OrdinalIgnoreCase)));
            if (hit is not null) result.Add(new AppChoice(name, hit.TargetPath));
        }
        return result;
    }

    /// <summary>Имя для своего exe: «C:\…\Аудио плеер.exe» → «Аудио плеер».</summary>
    public static string NameOf(string exePath) => System.IO.Path.GetFileNameWithoutExtension(exePath.Replace('\\', '/'));

    /// <summary>«1:12:30», «12:05».</summary>
    public static string Position(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Досмотрели (последние 3 % или 5 минут) или только начали (меньше минуты) — с начала.</summary>
    public static bool WorthResuming(double position, double duration) =>
        position >= 60 && (duration <= 0 || position < duration - Math.Max(duration * 0.03, Math.Min(300, duration * 0.1)));
}
