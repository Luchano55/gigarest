using System.Runtime.InteropServices;
using GigaRest.Core.Logging;

namespace GigaRest.Windows;

/// <summary>Устройство вывода звука: наушники, колонки, монитор.</summary>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Куда идёт звук. Список — через Core Audio (MMDevice API); смена устройства по умолчанию — через IPolicyConfig:
/// официального способа нет, этим же путём меняют устройство известные переключатели звука. Без прав администратора.
/// </summary>
public static class AudioDevices
{
    /// <summary>Включённые устройства вывода; у текущего IsDefault. Ошибка — пустой список.</summary>
    public static List<AudioDevice> List()
    {
        var result = new List<AudioDevice>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out var def) == 0 && def is not null)
            {
                def.GetId(out defaultId);
                Marshal.ReleaseComObject(def);
            }
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(DataFlowRender, DeviceStateActive, out var devices));
            devices.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0 || device is null) continue;
                try
                {
                    device.GetId(out var id);
                    result.Add(new AudioDevice(id, FriendlyName(device) ?? id, id == defaultId));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Устройства звука: " + ex.Message);
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Сделать устройство основным для музыки, игр и фильмов (как «Выбор устройства вывода» в Windows).</summary>
    public static bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            var hr = policy.SetDefaultEndpoint(id, RoleConsole);
            if (hr == 0) hr = policy.SetDefaultEndpoint(id, RoleMultimedia);
            Marshal.ReleaseComObject(policy);
            if (hr != 0) Log.Warn($"Устройство звука не сменилось: 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Устройство звука: " + ex.Message);
            return false;
        }
    }

    private static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) != 0 || store is null) return null;
        try
        {
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0) return null;
            try { return value.vt == VtLpwstr ? Marshal.PtrToStringUni(value.p) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private const int DataFlowRender = 0;
    private const int RoleConsole = 0, RoleMultimedia = 1;
    private const uint DeviceStateActive = 1;
    private const uint StgmRead = 0;
    private const ushort VtLpwstr = 31;
    private static readonly PropertyKey FriendlyNameKey = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    // Порядок методов важен (таблица COM): первые десять не нужны, но должны стоять на своих местах.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
