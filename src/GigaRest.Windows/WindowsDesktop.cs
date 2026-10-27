using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GigaRest.Core.Desktop;
using GigaRest.Core.Logging;
using GigaRest.Core.Text;
using static GigaRest.Windows.Native;

namespace GigaRest.Windows;

/// <summary>Настоящая Windows для ядра. Ошибки не бросает: недоступно — значит false/null.</summary>
public sealed class WindowsDesktop : IDesktop
{
    private readonly int _sessionId;
    private readonly bool _selfElevated;

    public WindowsDesktop()
    {
        CurrentProcessId = Environment.ProcessId;
        ProcessIdToSessionId(CurrentProcessId, out _sessionId);
        _selfElevated = Environment.IsPrivilegedProcess;
    }

    public int CurrentProcessId { get; }

    // ---------- процессы ----------

    public IReadOnlyList<ProcessInfo> GetProcesses()
    {
        var result = new List<ProcessInfo>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == InvalidHandle) return result;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32FirstW(snapshot, ref entry)) return result;
            do
            {
                var pid = entry.th32ProcessID;
                if (pid <= 4) continue;
                // Только наш сеанс: чужие сеансы и службы не трогаем.
                if (!ProcessIdToSessionId(pid, out var session) || session != _sessionId) continue;
                var info = Describe(pid, entry.th32ParentProcessID, entry.szExeFile);
                if (info is not null) result.Add(info);
            } while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    private ProcessInfo? Describe(int pid, int parent, string exeName)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0)
        {
            // Даже ограниченный доступ закрыт — защищённый процесс. Показываем как «от администратора».
            return new ProcessInfo { Pid = pid, ParentPid = parent, ExeName = exeName, IsElevated = !_selfElevated };
        }
        try
        {
            return new ProcessInfo
            {
                Pid = pid,
                ParentPid = parent,
                ExeName = exeName,
                StartTimeUtc = StartTime(h),
                ExePath = ImagePath(h),
                CommandLine = ReadCommandLine(h),
                PrivateBytes = PrivateBytes(h),
                Aumid = Aumid(h),
                IsElevated = !_selfElevated && IsElevatedProcess(h),
            };
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static DateTime StartTime(nint h) =>
        GetProcessTimes(h, out var creation, out _, out _, out _) ? DateTime.FromFileTimeUtc(creation) : default;

    private static string? ImagePath(nint h)
    {
        var size = 1024;
        var sb = new StringBuilder(size);
        return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
    }

    private static long PrivateBytes(nint h) =>
        GetProcessMemoryInfo(h, out var c, Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>()) ? (long)c.PrivateUsage : 0;

    private static string? Aumid(nint h)
    {
        var length = 0;
        // ERROR_INSUFFICIENT_BUFFER = 122: программа упакована; иначе APPMODEL_ERROR_NO_APPLICATION.
        if (GetApplicationUserModelId(h, ref length, null) != 122 || length <= 0) return null;
        var sb = new StringBuilder(length);
        return GetApplicationUserModelId(h, ref length, sb) == 0 ? sb.ToString() : null;
    }

    private static bool IsElevatedProcess(nint h)
    {
        // Токен процесса администратора обычной программе не открыть — это тоже ответ.
        if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return true;
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>Командная строка через NtQueryInformationProcess(ProcessCommandLineInformation), Windows 8.1+.</summary>
    private static string? ReadCommandLine(nint h)
    {
        NtQueryInformationProcess(h, ProcessCommandLineInformation, 0, 0, out var needed);
        if (needed <= 0 || needed > 1 << 20) return null;
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buffer, needed, out _) != 0) return null;
            // UNICODE_STRING: Length (байты), MaximumLength, указатель на буфер.
            var length = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return length == 0 || text == 0 ? null : Marshal.PtrToStringUni(text, length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool IsAlive(ProcessKey process)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, process.Pid);
        if (h == 0) return false;
        try
        {
            return WaitForSingleObject(h, 0) == WAIT_TIMEOUT && SameStart(h, process);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public bool Terminate(ProcessKey process)
    {
        var h = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, process.Pid);
        if (h == 0) return false;
        try
        {
            if (!SameStart(h, process)) return false; // PID уже у другого процесса
            Log.Info($"Завершаю процесс {process.Pid} (разрешено галочкой)");
            return TerminateProcess(h, 1);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static bool SameStart(nint h, ProcessKey process) =>
        process.StartTimeUtc == default || Math.Abs((StartTime(h) - process.StartTimeUtc).TotalMilliseconds) < 1;

    public long GetAvailableMemoryBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
    }

    // ---------- окна ----------

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        using var dpi = new DpiScope();
        var handles = new List<nint>();
        EnumWindows((hwnd, _) => { handles.Add(hwnd); return true; }, 0);
        var result = new List<WindowInfo>(handles.Count);
        foreach (var hwnd in handles)
        {
            var w = DescribeWindow(hwnd);
            if (w is not null) result.Add(w);
        }
        return result;
    }

    private WindowInfo? DescribeWindow(nint hwnd)
    {
        if (!IsWindow(hwnd)) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        var visible = IsWindowVisible(hwnd);
        var cls = ClassName(hwnd);
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var tool = (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0;
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
        GetWindowRect(hwnd, out var r);

        // Программа из Store: окно-рамка у ApplicationFrameHost, сама программа — в дочернем CoreWindow.
        if (cls == "ApplicationFrameWindow")
        {
            var inner = 0;
            EnumChildWindows(hwnd, (child, _) =>
            {
                GetWindowThreadProcessId(child, out var childPid);
                if (childPid != pid && ClassName(child) == "Windows.UI.Core.CoreWindow") { inner = childPid; return false; }
                return true;
            }, 0);
            if (inner != 0) pid = inner;
        }

        return new WindowInfo
        {
            Handle = hwnd,
            Pid = pid,
            ClassName = cls,
            Title = visible ? Title(hwnd) : "",
            IsVisible = visible,
            IsEnabled = IsWindowEnabled(hwnd),
            OwnerHandle = GetWindow(hwnd, GW_OWNER),
            IsToolWindow = tool,
            IsCloaked = cloaked != 0,
            IsMinimized = IsIconic(hwnd),
            Bounds = new Core.Desktop.Rect(r.Left, r.Top, r.Right, r.Bottom),
        };
    }

    private static string ClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string Title(nint hwnd)
    {
        var len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public bool PostClose(long window) => PostMessageW((nint)window, WM_CLOSE, 0, 0);

    public void BringToFront(long window)
    {
        var hwnd = (nint)window;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    // ---------- мониторы и положение ----------

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        using var dpi = new DpiScope();
        var result = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref RECT _, nint _) =>
        {
            var m = DescribeMonitor(monitor);
            if (m is not null) result.Add(m);
            return true;
        }, 0);
        return result;
    }

    private static MonitorInfo? DescribeMonitor(nint monitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoW(monitor, ref info)) return null;
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? (int)Math.Round(dpiX * 100.0 / 96) : 100;
        return new MonitorInfo
        {
            Id = info.szDevice,
            IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            Bounds = ToRect(info.rcMonitor),
            WorkArea = ToRect(info.rcWork),
            ScalePercent = scale,
        };
    }

    private static Core.Desktop.Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
    private static RECT ToRECT(Core.Desktop.Rect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    public WindowPlacementData? GetPlacement(long window)
    {
        using var dpi = new DpiScope();
        var hwnd = (nint)window;
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return null;
        var monitor = DescribeMonitor(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
        return new WindowPlacementData
        {
            NormalBounds = ToRect(wp.rcNormalPosition),
            Maximized = wp.showCmd == SW_SHOWMAXIMIZED || (wp.showCmd == SW_SHOWMINIMIZED && (wp.flags & WPF_RESTORETOMAXIMIZED) != 0),
            Minimized = wp.showCmd == SW_SHOWMINIMIZED,
            MonitorId = monitor?.Id,
        };
    }

    public bool SetPlacement(long window, WindowPlacementData placement, MonitorInfo? monitor)
    {
        using var dpi = new DpiScope();
        var hwnd = (nint)window;
        if (!IsWindow(hwnd)) return false;
        // Сначала «обычное» состояние, иначе развёрнутое окно не переедет на другой монитор.
        ShowWindow(hwnd, SW_RESTORE);
        var wp = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = placement.Minimized ? SW_SHOWMINNOACTIVE : placement.Maximized ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL,
            flags = placement.Minimized && placement.Maximized ? WPF_RESTORETOMAXIMIZED : 0,
            rcNormalPosition = ToRECT(placement.NormalBounds),
        };
        return SetWindowPlacement(hwnd, ref wp);
    }

    public bool MoveToMonitorMaximized(long window, MonitorInfo monitor)
    {
        using var dpi = new DpiScope();
        var hwnd = (nint)window;
        if (!IsWindow(hwnd)) return false;
        ShowWindow(hwnd, SW_RESTORE);
        GetWindowRect(hwnd, out var r);
        var wa = monitor.WorkArea;
        var width = Math.Min(r.Right - r.Left, wa.Width - 80);
        var height = Math.Min(r.Bottom - r.Top, wa.Height - 80);
        // Переносим в обычном виде, разворачиваем уже там: окно само подстроится под масштаб монитора.
        if (!SetWindowPos(hwnd, 0, wa.Left + 40, wa.Top + 40, width, height, SWP_NOZORDER | SWP_NOACTIVATE))
            return false;
        ShowWindow(hwnd, SW_MAXIMIZE);
        return true;
    }

    // ---------- запуск ----------

    public string? Launch(LaunchSpec spec)
    {
        try
        {
            ProcessStartInfo psi;
            if (!string.IsNullOrEmpty(spec.Aumid))
            {
                psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(@"shell:AppsFolder\" + spec.Aumid);
            }
            else
            {
                if (!File.Exists(spec.FileName)) return L.T("нет файла программы", "program file not found");
                psi = new ProcessStartInfo(spec.FileName) { UseShellExecute = false };
                foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);
                if (spec.WorkingDirectory is { } dir && Directory.Exists(dir)) psi.WorkingDirectory = dir;
            }
            using var _ = Process.Start(psi);
            return null;
        }
        catch (Win32Exception ex)
        {
            return ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
}
