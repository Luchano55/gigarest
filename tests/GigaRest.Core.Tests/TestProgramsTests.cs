using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;

namespace GigaRest.Core.Tests;

public class TestProgramsTests
{
    [Fact]
    public void PicksOnlyTestWindowsWithTheirActions()
    {
        var d = new FakeDesktop();
        var pid = 10;
        foreach (var spec in TestPrograms.All)
        {
            d.AddProcess(spec.ExeName, pid);
            d.AddWindow(pid++, spec.Title);
        }
        d.AddProcess("chrome.exe", 99);
        d.AddWindow(99, "Работа");
        var running = ProgramScanner.Scan(d.GetProcesses(), d.GetWindows(), new AppSettings(), 1);

        var picked = TestPrograms.Pick(running);

        Assert.Equal(4, picked.Count);
        Assert.DoesNotContain(picked, p => p.Id == "chrome.exe");
        Assert.True(picked.Single(p => p.Id == "тест 3 — трей.exe").AllowTerminate);
        Assert.Equal(ProgramAction.SecondMonitor, picked.Single(p => p.Id == "тест 4 — второй монитор.exe").Action);
        Assert.True(TestPrograms.IsTest("ТЕСТ 2 — СПРОСИТ.EXE"));
    }

    [Fact]
    public void ArgumentsRoundTripThroughCommandLine()
    {
        var spec = TestPrograms.All[1];
        var args = TestPrograms.Arguments(spec, 1);
        var line = "\"C:\\Мои программы\\GigaRest\\Тест 2 — спросит.exe\" " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        Assert.Equal(args, CommandLine.Split(line).Skip(1));
    }
}
