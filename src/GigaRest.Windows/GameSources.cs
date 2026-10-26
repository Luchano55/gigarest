using System.ComponentModel;
using System.Diagnostics;
using GigaRest.Core.Games;
using GigaRest.Core.Logging;
using Microsoft.Win32;

namespace GigaRest.Windows;

/// <summary>
/// Где на этом компьютере лежат библиотеки игр (Steam, Epic, GOG, Battle.net) и как запустить игру. Только чтение, без прав администратора.
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
        try
        {
            var exe = BattleNetExe();
            if (exe is not null) games.AddRange(BattleNet.FromUninstall(UninstallEntries(), exe));
        }
        catch (Exception ex) { Log.Warn("Battle.net: " + ex.Message); }
        Log.Info($"Игры: Steam {games.Count(g => g.Store == "Steam")}, Epic {games.Count(g => g.Store == "Epic")}, GOG {games.Count(g => g.Store == "GOG")}, Battle.net {games.Count(g => g.Store == "Battle.net")}");
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

    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    // «Установка и удаление программ»: и для всех пользователей, и для текущего.
    private static List<UninstallEntry> UninstallEntries()
    {
        var list = new List<UninstallEntry>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var path in UninstallKeys)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                list.Add(new UninstallEntry(key.GetValue("DisplayName") as string, key.GetValue("Publisher") as string, key.GetValue("UninstallString") as string));
            }
        }
        return list;
    }

    private static string? BattleNetExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
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
                foreach (var a in game.LaunchArgs) psi.ArgumentList.Add(a);
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
