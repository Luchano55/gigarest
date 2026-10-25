using GigaRest.Core.Closing;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;

namespace GigaRest.Core.Tests;

public class ProgramHintsTests
{
    [Theory]
    [InlineData("Bitrix24.exe", ProgramAction.Close)]
    [InlineData("ShareX.exe", ProgramAction.Close)]
    [InlineData("1cv8c.exe", ProgramAction.Close)]
    [InlineData("Telegram.exe", ProgramAction.SecondMonitor)]
    [InlineData("Яндекс Музыка.exe", ProgramAction.SecondMonitor)]
    [InlineData("steam.exe", ProgramAction.Keep)]
    public void SuggestsByKind(string exe, ProgramAction expected) =>
        Assert.Equal(expected, ProgramHints.Suggest(exe)!.Action);

    [Fact]
    public void GamesByFolderAndUnknownGetNothing()
    {
        Assert.Equal(ProgramAction.Keep, ProgramHints.Suggest("wow.exe", @"D:\Games\World of Warcraft\_retail_\Wow.exe")!.Action);
        Assert.Null(ProgramHints.Suggest("notepad++.exe", @"C:\Program Files\Notepad++\notepad++.exe"));
    }

    [Fact]
    public void ApplyChangesOnlyUndecidedAndKeepsFourTiles()
    {
        var rows = new List<SelectorRow>
        {
            new() { Id = "bitrix24.exe", DisplayName = "Битрикс24", IsRunning = true, HidesToTray = true },
            new() { Id = "chrome.exe", DisplayName = "Chrome", Action = ProgramAction.SecondMonitor }, // решено вручную
            new() { Id = "telegram.exe", DisplayName = "Telegram" },
            new() { Id = "zen.exe", DisplayName = "Zen" },
            new() { Id = "claude.exe", DisplayName = "Claude" },
            new() { Id = "discord.exe", DisplayName = "Discord" },
            new() { Id = "notepad++.exe", DisplayName = "Notepad++" },
            new() { Id = "yandexdisk2.exe", DisplayName = "Яндекс Диск", IsBackground = true },
        };

        var changed = ProgramHints.Apply(rows);

        Assert.Equal(4, changed); // Битрикс + три плитки (четвёртая — Chrome)
        Assert.Equal(ProgramAction.Close, rows[0].Action);
        Assert.True(rows[0].AllowTerminate);
        Assert.Equal(4, rows.Count(r => r.Action == ProgramAction.SecondMonitor));
        Assert.Equal(ProgramAction.Keep, rows.Single(r => r.Id == "notepad++.exe").Action);
        Assert.Contains("почта", ProgramHints.Suggest("outlook.exe")!.Reason);
        Assert.NotNull(rows.Single(r => r.Id == "discord.exe").Hint);
        var disk = rows.Single(r => r.Id == "yandexdisk2.exe");
        Assert.Equal(ProgramAction.Keep, disk.Action); // фоновый — только подсказка
        Assert.Contains("облачный диск", disk.Hint);
    }

    [Fact]
    public async Task TrayProgramTerminatedFromSummaryIsRelaunched()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", Guid.NewGuid().ToString("N"));
        var store = new SessionStore(Path.Combine(dir, "session.json"));
        var d = new FakeDesktop();
        var clock = new FakeClock();
        d.AddProcess("ShareX.exe", 10);
        var w = d.AddWindow(10, "ShareX");
        d.OnClose[w.Handle] = () => d.RemoveWindow(w.Handle);
        var switcher = new RestSwitcher(d, clock, store);
        var programs = ProgramScanner.Scan(d.GetProcesses(), d.GetWindows(), new AppSettings(), 1);
        programs[0].Action = ProgramAction.Close;

        var report = await switcher.GoToRestAsync(programs, null, default);
        Assert.Equal(CloseOutcome.InBackground, report.Results.Single().Outcome);

        // Роман нажал «Завершить» в итоге — процесс жив (в фейке убиваем сами), отмечаем закрытой.
        d.Kill(10);
        switcher.MarkClosed("sharex.exe");
        Assert.True(store.Load()!.Closed.Single().Gone);
        switcher.ReturnToWork(null, default);
        Assert.Single(d.Launched);
        Directory.Delete(dir, true);
    }
}
