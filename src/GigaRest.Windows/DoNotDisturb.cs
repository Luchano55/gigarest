using System.Diagnostics;
using System.Runtime.InteropServices;
using GigaRest.Core.Logging;

namespace GigaRest.Windows;

/// <summary>
/// «Не беспокоить» на время отдыха (D-DND). Публичного способа нет, поэтому так:
/// 1) режим «Только приоритетные» через системное состояние оболочки «тихие часы» (WNF) — без прав администратора;
///    приоритетные программы Роман выбирает сам в настройках Windows (можно добавить Telegram);
/// 2) проверяем, что Windows приняла режим (читаем состояние обратно);
/// 3) не вышло — вызывающий показывает подсказку и открывает страницу настроек уведомлений.
/// Что было до отдыха, запоминаем в файле — даже после перезагрузки вернём прежнее.
/// </summary>
public sealed class DoNotDisturb
{
    // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: 0 — выключено, 1 — только приоритетные, 2 — только будильники.
    private const ulong QuietHoursState = 0x0D83063EA3BF1C75;
    private const int PriorityOnly = 1;

    private readonly string _beforeFile;

    public DoNotDisturb(string dataDir)
    {
        _beforeFile = Path.Combine(dataDir, "dnd-before.txt");
    }

    /// <summary>Включить. true — Windows подтвердила режим.</summary>
    public bool TurnOn()
    {
        try
        {
            var current = Read();
            // Файл уже есть — значит, режим включали мы (до перезагрузки); «до отдыха» — то, что в файле.
            if (!File.Exists(_beforeFile)) File.WriteAllText(_beforeFile, (current ?? 0).ToString());
            if (current == PriorityOnly) return true;
            var status = Write(PriorityOnly);
            Thread.Sleep(300);
            var after = Read();
            Log.Info($"Не беспокоить: было {current?.ToString() ?? "?"}, запись 0x{status:X8}, стало {after?.ToString() ?? "?"}");
            return after == PriorityOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Не беспокоить: " + ex.Message);
            return false;
        }
    }

    /// <summary>Вернуть как было до отдыха.</summary>
    public void Restore()
    {
        try
        {
            if (!File.Exists(_beforeFile)) return;
            var before = int.TryParse(File.ReadAllText(_beforeFile).Trim(), out var b) ? b : 0;
            if (Read() != before) Write(before);
            File.Delete(_beforeFile);
            Log.Info($"Не беспокоить: возвращено {before}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Не беспокоить, возврат: " + ex.Message);
        }
    }

    /// <summary>Страница настроек, где переключатель «Не беспокоить» включают руками.</summary>
    public static void OpenSettings()
    {
        try { using var _ = Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warn("Настройки уведомлений: " + ex.Message); }
    }

    private static int? Read()
    {
        var name = QuietHoursState;
        var buffer = new byte[4];
        var size = buffer.Length;
        var status = NtQueryWnfStateData(ref name, 0, 0, out _, buffer, ref size);
        if (status != 0) return null;
        return size >= 4 ? BitConverter.ToInt32(buffer, 0) : 0;
    }

    private static int Write(int profile)
    {
        var name = QuietHoursState;
        var data = BitConverter.GetBytes(profile);
        return NtUpdateWnfStateData(ref name, data, data.Length, 0, 0, 0, 0);
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong stateName, nint typeId, nint explicitScope, out uint changeStamp, byte[] buffer, ref int bufferSize);

    [DllImport("ntdll.dll")]
    private static extern int NtUpdateWnfStateData(ref ulong stateName, byte[] buffer, int length, nint typeId, nint explicitScope, uint matchingChangeStamp, int checkStamp);
}
