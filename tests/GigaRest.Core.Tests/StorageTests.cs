using GigaRest.Core.Logging;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gigarest-tests", "Мои настройки " + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void SettingsRoundTripInCyrillicFolder()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var s = store.Load();
        Assert.Equal("Ctrl+Alt+Home", s.Hotkey);
        s.Programs.Add(new ProgramSetting { Id = "bitrix24.exe", DisplayName = "Битрикс24", Action = ProgramAction.Close, AllowTerminate = true });
        store.Save(s);

        var text = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.Contains("Битрикс24", text);          // кириллица читаема
        Assert.Contains("\"close\"", text);           // действия — словами

        var back = store.Load();
        Assert.Equal(ProgramAction.Close, back.Find("BITRIX24.EXE")!.Action);
        Assert.True(back.Programs[0].AllowTerminate);
    }

    [Fact]
    public void CorruptSettingsFallBackToDefaultsAndKeepCopy()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ это не json");

        var s = new SettingsStore(path).Load();

        Assert.Equal(30, s.CloseTimeoutSeconds);
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void NormalizeFixesBadValuesAndDuplicates()
    {
        var s = new AppSettings
        {
            CloseTimeoutSeconds = 0, Hotkey = " ",
            Programs = { new() { Id = "a.exe" }, new() { Id = "A.EXE", Action = ProgramAction.Close }, new() { Id = "" } },
        };
        SettingsStore.Normalize(s);
        Assert.Equal(30, s.CloseTimeoutSeconds);
        Assert.Equal("Ctrl+Alt+Home", s.Hotkey);
        Assert.Equal(ProgramAction.Keep, Assert.Single(s.Programs).Action);
    }

    [Fact]
    public void SessionClearKeepsLastCopy()
    {
        var path = Path.Combine(_dir, "session.json");
        var store = new SessionStore(path);
        store.Save(new RestSession { Closed = { new ClosedProgram { Id = "outlook.exe", Gone = true } } });
        Assert.True(store.Exists);

        store.Clear();

        Assert.False(store.Exists);
        Assert.True(File.Exists(path + ".last"));
    }

    [Fact]
    public void OldLogsAreRemoved()
    {
        var logs = Path.Combine(_dir, "logs");
        Directory.CreateDirectory(logs);
        var now = new DateTime(2026, 10, 24, 12, 0, 0);
        File.WriteAllText(Path.Combine(logs, Log.FileName(now.AddDays(-8))), "старый");
        File.WriteAllText(Path.Combine(logs, Log.FileName(now.AddDays(-6))), "свежий");
        File.WriteAllText(Path.Combine(logs, "чужой.txt"), "не трогаем");

        Log.Cleanup(logs, now);

        Assert.Equal(2, Directory.GetFiles(logs).Length);
        Assert.False(File.Exists(Path.Combine(logs, Log.FileName(now.AddDays(-8)))));
    }
}
