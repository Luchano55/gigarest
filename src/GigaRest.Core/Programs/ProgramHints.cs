using GigaRest.Core.Settings;

namespace GigaRest.Core.Programs;

/// <summary>Подсказка для программы: что с ней, скорее всего, делать и почему.</summary>
public sealed record ProgramHint(ProgramAction Action, string Reason);

/// <summary>
/// Автоподсказка «рабочее или личное» по виду программы. Только предлагает — решает Роман
/// (первая настройка и кнопка «Подсказать» в окне «Рабочие программы»).
/// </summary>
public static class ProgramHints
{
    private static readonly Dictionary<string, string> Work = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bitrix24.exe"] = "рабочий чат и задачи",
        ["outlook.exe"] = "почта", ["olk.exe"] = "почта", ["thunderbird.exe"] = "почта",
        ["teams.exe"] = "видеозвонки", ["ms-teams.exe"] = "видеозвонки", ["zoom.exe"] = "видеозвонки",
        ["slack.exe"] = "рабочий чат", ["mattermost.exe"] = "рабочий чат", ["trueconf.exe"] = "видеозвонки",
        ["1cv8.exe"] = "1С", ["1cv8c.exe"] = "1С", ["1cv8s.exe"] = "1С",
        ["winword.exe"] = "документы", ["excel.exe"] = "таблицы", ["powerpnt.exe"] = "презентации",
        ["onenote.exe"] = "заметки", ["acrobat.exe"] = "документы", ["acrord32.exe"] = "документы",
        ["anydesk.exe"] = "удалённый доступ", ["teamviewer.exe"] = "удалённый доступ", ["rustdesk.exe"] = "удалённый доступ",
        ["sharex.exe"] = "скриншоты для работы", ["lightshot.exe"] = "скриншоты для работы",
        ["code.exe"] = "редактор кода", ["devenv.exe"] = "редактор кода", ["rider64.exe"] = "редактор кода",
        ["idea64.exe"] = "редактор кода", ["pycharm64.exe"] = "редактор кода", ["cursor.exe"] = "редактор кода",
        ["figma.exe"] = "дизайн", ["notion.exe"] = "заметки и задачи",
        ["yandexdisk2.exe"] = "облачный диск", ["dropbox.exe"] = "облачный диск", ["onedrive.exe"] = "облачный диск",
        ["windowsterminal.exe"] = "терминал", ["powershell.exe"] = "терминал", ["cmd.exe"] = "терминал",
    };

    private static readonly Dictionary<string, string> Personal = new(StringComparer.OrdinalIgnoreCase)
    {
        ["telegram.exe"] = "мессенджер", ["whatsapp.exe"] = "мессенджер", ["discord.exe"] = "мессенджер",
        ["zen.exe"] = "личный браузер", ["claude.exe"] = "ассистент",
        ["яндекс музыка.exe"] = "музыка", ["spotify.exe"] = "музыка", ["vlc.exe"] = "видео",
    };

    private static readonly Dictionary<string, string> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steam.exe"] = "игры", ["battle.net.exe"] = "игры", ["battle.net launcher.exe"] = "игры",
        ["epicgameslauncher.exe"] = "игры", ["galaxyclient.exe"] = "игры", ["ubisoftconnect.exe"] = "игры",
        ["obs64.exe"] = "запись и стримы",
    };

    /// <summary>Подсказка по имени exe; путь помогает узнать игры (папки steamapps, Games).</summary>
    public static ProgramHint? Suggest(string programId, string? exePath = null)
    {
        if (Work.TryGetValue(programId, out var w)) return new(ProgramAction.Close, "похоже на рабочую: " + w);
        if (Personal.TryGetValue(programId, out var p)) return new(ProgramAction.SecondMonitor, "похоже на личную: " + p);
        if (Games.TryGetValue(programId, out var g)) return new(ProgramAction.Keep, "не трогаем: " + g);
        if (exePath is not null && (exePath.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase)
                                    || exePath.Contains(@"\Epic Games\", StringComparison.OrdinalIgnoreCase)
                                    || exePath.Contains(@"\Games\", StringComparison.OrdinalIgnoreCase)))
            return new(ProgramAction.Keep, "не трогаем: похоже на игру");
        var known = KnownPrograms.Find(programId);
        if (known is not null && known.DefaultAction != ProgramAction.Keep)
            return new(known.DefaultAction, known.DefaultAction == ProgramAction.Close ? "похоже на рабочую" : "похоже на личную");
        return null;
    }

    /// <summary>
    /// Применить подсказки к строкам, которые ещё «Оставить». На второй монитор — не больше четырёх (столько плиток).
    /// Возвращает, сколько строк изменилось.
    /// </summary>
    public static int Apply(IList<SelectorRow> rows)
    {
        var changed = 0;
        var tiles = rows.Count(r => r.Action == ProgramAction.SecondMonitor);
        foreach (var r in rows.OrderByDescending(r => r.IsRunning))
        {
            var hint = Suggest(r.Id, r.ExePath);
            r.Hint = hint?.Reason;
            if (hint is null || r.Action != ProgramAction.Keep || hint.Action == ProgramAction.Keep) continue;
            if (hint.Action == ProgramAction.SecondMonitor)
            {
                if (tiles >= 4) continue;
                tiles++;
            }
            r.Action = hint.Action;
            if (r.Action == ProgramAction.Close && r.HidesToTray) r.AllowTerminate = true;
            changed++;
        }
        return changed;
    }
}
