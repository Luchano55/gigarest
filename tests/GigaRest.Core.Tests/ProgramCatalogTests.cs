using GigaRest.Core.Programs;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public class ProgramCatalogTests
{
    private static readonly ShortcutInfo[] Shortcuts =
    [
        new("Битрикс24", @"C:\Users\Роман\AppData\Local\Programs\Bitrix24\Bitrix24.exe"),
        new("Удалить Битрикс24", @"C:\Users\Роман\AppData\Local\Programs\Bitrix24\Uninstall Bitrix24.exe"),
        new("Telegram", @"C:\Users\Роман\AppData\Roaming\Telegram Desktop\Telegram.exe"),
        new("Uninstall Telegram", @"C:\Users\Роман\AppData\Roaming\Telegram Desktop\unins000.exe"),
        new("Zen Browser", @"C:\Program Files\Zen Browser\zen.exe"),
        new("Zen", @"C:\Program Files\Zen Browser\zen.exe"),
        new("Сайт программы", @"C:\Program Files\X\site.url"),
        new("Проводник", @"C:\Windows\explorer.exe"),
        new("Документация", @"C:\Program Files\X\readme.txt"),
    ];

    [Fact]
    public void ShortcutsWithoutUninstallersDuplicatesAndSystem()
    {
        var clean = ProgramCatalog.CleanShortcuts(Shortcuts);
        Assert.Equal(new[] { "Битрикс24", "Telegram", "Zen" }, clean.Select(s => s.Name));
    }

    [Fact]
    public void RowsMergeRunningConfiguredAndInstalled()
    {
        var settings = new AppSettings
        {
            Programs =
            {
                new ProgramSetting { Id = "1cv8.exe", DisplayName = "1С", Action = ProgramAction.Close },
                new ProgramSetting { Id = "telegram.exe", DisplayName = "Telegram", Action = ProgramAction.SecondMonitor },
            },
        };
        var d = new FakeDesktop();
        d.AddProcess("Bitrix24.exe", 10);
        d.AddWindow(10, "Битрикс24");
        var running = ProgramScanner.Scan(d.GetProcesses(), d.GetWindows(), settings, 1);

        var rows = ProgramCatalog.BuildRows(settings, running, Shortcuts);

        // Настроенные и запущенные — сверху; дублей нет; имена известных программ — наши.
        Assert.Equal(new[] { "Битрикс24", "1С", "Telegram", "Zen" }, rows.Select(r => r.DisplayName));
        Assert.True(rows[0].IsRunning);
        Assert.True(rows[0].HidesToTray);
        Assert.Equal(ProgramAction.Keep, rows.Single(r => r.Id == "zen.exe").Action);
        Assert.Equal(@"C:\Program Files\Zen Browser\zen.exe", rows.Single(r => r.Id == "zen.exe").ExePath);
    }

    [Fact]
    public void SearchByNameOrExe()
    {
        var rows = ProgramCatalog.BuildRows(new AppSettings(), [], Shortcuts);
        Assert.Equal("Zen", Assert.Single(ProgramCatalog.Filter(rows, "ZEN")).DisplayName);
        Assert.Equal("Битрикс24", Assert.Single(ProgramCatalog.Filter(rows, "битр")).DisplayName);
        Assert.Equal(3, ProgramCatalog.Filter(rows, "  ").Count());
    }

    [Fact]
    public void SaveKeepsOnlyChosenNewPrograms()
    {
        var settings = new AppSettings();
        var rows = ProgramCatalog.BuildRows(settings, [], Shortcuts);
        rows.Single(r => r.Id == "bitrix24.exe").Action = ProgramAction.Close;
        rows.Single(r => r.Id == "bitrix24.exe").AllowTerminate = true;

        ProgramCatalog.Save(settings, rows);

        var saved = Assert.Single(settings.Programs);
        Assert.Equal("bitrix24.exe", saved.Id);
        Assert.Equal(ProgramAction.Close, saved.Action);
        Assert.True(saved.AllowTerminate);

        // Передумал: «Оставить» — запись остаётся, но с новым действием.
        rows.Single(r => r.Id == "bitrix24.exe").Action = ProgramAction.Keep;
        ProgramCatalog.Save(settings, rows);
        Assert.Equal(ProgramAction.Keep, settings.Programs.Single().Action);
    }
}
