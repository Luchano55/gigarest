using System.Diagnostics;
using System.Runtime.InteropServices;
using GigaRest.Core.Desktop;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;

namespace GigaRest.Windows.Tests;

/// <summary>Запуск подопытной программы под своим именем exe — у каждого теста своя «программа».</summary>
public sealed class Dummy : IDisposable
{
    public static string? SourceDir => Environment.GetEnvironmentVariable("GIGAREST_TESTDUMMY");

    public Dummy(string exeName, IEnumerable<string> args, string? dir = null)
    {
        Dir = dir ?? Path.Combine(Path.GetTempPath(), "GigaRest тесты", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Dir);
        foreach (var f in Directory.GetFiles(SourceDir!))
        {
            var name = Path.GetFileName(f);
            var target = name.Equals("TestDummy.exe", StringComparison.OrdinalIgnoreCase) ? exeName : name;
            File.Copy(f, Path.Combine(Dir, target), overwrite: true);
        }
        ExePath = Path.Combine(Dir, exeName);
        ExeName = exeName;
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process = Process.Start(psi)!;
    }

    public string Dir { get; }
    public string ExePath { get; }
    public string ExeName { get; }
    public Process Process { get; }
    public static readonly WindowsDesktop Desktop = new();

    public static IReadOnlyList<WindowInfo> WaitWindows(Func<WindowInfo, bool> match, int count = 1, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var found = Desktop.GetWindows().Where(match).ToList();
            if (found.Count >= count) return found;
            Thread.Sleep(100);
        }
        throw new TimeoutException("Окно не появилось");
    }

    public IReadOnlyList<WindowInfo> WaitWindows(int count = 1) =>
        WaitWindows(w => w.Pid == Process.Id && w.IsAppWindow, count);

    /// <summary>Программа, как её видит окно перехода, с нужным действием.</summary>
    public RunningProgram Program(bool allowTerminate = false)
    {
        var settings = new AppSettings
        {
            Programs = { new ProgramSetting { Id = ExeName.ToLowerInvariant(), Action = ProgramAction.Close, AllowTerminate = allowTerminate } },
        };
        return ProgramScanner.Scan(Desktop.GetProcesses(), Desktop.GetWindows(), settings, Environment.ProcessId)
            .Single(p => p.Id == ExeName.ToLowerInvariant());
    }

    public void Dispose()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
        }
        try { Directory.Delete(Dir, true); } catch (Exception) { }
    }

    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hwnd, int msg, nint wParam, nint lParam);
    public const int WM_COMMAND = 0x0111, IDCANCEL = 2, IDNO = 7;
}

/// <summary>Пропуск, если подопытной программы нет (локальный запуск без CI).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "только Windows";
        else if (string.IsNullOrEmpty(Dummy.SourceDir)) Skip = "нет GIGAREST_TESTDUMMY";
    }
}
