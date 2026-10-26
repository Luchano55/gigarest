using System.ComponentModel;
using System.Diagnostics;
using GigaRest.Core.Games;
using GigaRest.Core.Logging;
using Microsoft.Win32;

namespace GigaRest.Windows;

/// <summary>
/// Где на этом компьютере лежат библиотеки игр (Steam, Epic, GOG) и как запустить игру. Только чтение, без прав администратора.
/// Battle.net свои игры в понятном виде не хранит — пока без него.
/// </summary>
public static class GameSources
{
    public static List<Game> FindInstalled()
    {
        var games = new List<Game>();
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam && steam.Length > 0)
                games.AddRange(GameLibraries.Steam(steam.Replace('/', '\\')));
        }
        catch (Exception ex) { Log.Warn("Steam: " + ex.Message); }
        try
        {
            games.AddRange(GameLibraries.Epic(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests")));
        }
        catch (Exception ex) { Log.Warn("Epic: " + ex.Message); }
        try { games.AddRange(Gog()); }
        catch (Exception ex) { Log.Warn("GOG: " + ex.Message); }
        Log.Info($"Игры: Steam {games.Count(g => g.Store == "Steam")}, Epic {games.Count(g => g.Store == "Epic")}, GOG {games.Count(g => g.Store == "GOG")}");
        return games;
    }

    // GOG Galaxy пишет установленные игры в реестр: HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id> (читать можно без прав администратора).
    private static IEnumerable<Game> Gog()
    {
        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root is null) yield break;
        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            if (key is null) continue;
            var name = key.GetValue("gameName") as string;
            var exe = key.GetValue("exe") as string;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) continue;
            if (key.GetValue("dependsOn") is string dep && dep.Length > 0) continue; // дополнение к игре
            yield return new Game
            {
                Id = "gog:" + id, Name = name.Trim(), Store = "GOG", ExePath = exe,
                WorkingDirectory = key.GetValue("workingDir") as string ?? Path.GetDirectoryName(exe),
            };
        }
    }

    /// <summary>Запустить игру. null — получилось, иначе — что не так.</summary>
    public static string? Launch(Game game)
    {
        try
        {
            ProcessStartInfo psi;
            if (game.LaunchUri is { } uri)
            {
                psi = new ProcessStartInfo(uri) { UseShellExecute = true }; // откроет Steam или Epic
            }
            else if (game.ExePath is { } exe && File.Exists(exe))
            {
                psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                if (game.WorkingDirectory is { } dir && Directory.Exists(dir)) psi.WorkingDirectory = dir;
            }
            else return "не найден файл игры";
            using var _ = Process.Start(psi);
            return null;
        }
        catch (Win32Exception ex) { return ex.Message; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }
}
