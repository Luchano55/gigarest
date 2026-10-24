using GigaRest.Core.Closing;
using GigaRest.Core.Demo;
using GigaRest.Core.Programs;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GigaRest.App;

/// <summary>
/// GigaRest.exe --screenshots &lt;папка&gt;: окно на правдоподобных данных во всех состояниях → PNG.
/// Так CI на Windows показывает интерфейс без Битрикс24 и Outlook.
/// </summary>
internal static class Screenshots
{
    public static async Task<bool> RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var (processes, windows, _) = DemoData.Create();
        var programs = ProgramScanner.Scan(processes, windows, new AppSettings(), selfPid: 1);

        var window = new SwitcherWindow();
        window.Present();
        window.SizeToDesign(fitScreen: false);
        window.UseDesignSizeForCapture();

        window.ShowPlan(programs, hasSecondMonitor: true, notice: null);
        await Capture(window.Root, Path.Combine(dir, "1-perehod.png"));

        window.ShowProgress(programs);
        window.UpdateProgress(new SwitchProgress("Закрываем Outlook…", 2, 4));
        await Capture(window.Root, Path.Combine(dir, "2-progress.png"));

        var results = programs.Where(p => p.Action == ProgramAction.Close).Select(p => p.Id switch
        {
            "bitrix24.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Terminated, false),
            "yandexdisk2.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.InBackground, false),
            _ => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Closed, p.Id == "outlook.exe"),
        }).ToList();
        var report = new RestReport(results, 4, (long)(4.3 * (1L << 30)),
            results.Where(r => !r.IsGone).Select(r => r.Message).ToList());
        window.ShowRest(report);
        await Capture(window.Root, Path.Combine(dir, "3-otdyh.png"));

        window.ShowWork(new ReturnReport(["Битрикс24", "Outlook", "Chrome"], ["1С:Предприятие"], 4));
        await Capture(window.Root, Path.Combine(dir, "4-rabota.png"));

        // Окно «Рабочие программы» с ярлыками меню «Пуск».
        var programsWindow = new ProgramsWindow();
        programsWindow.Present();
        programsWindow.SizeToDesign(fitScreen: false);
        programsWindow.UseDesignSizeForCapture();
        var settings = new AppSettings
        {
            Programs = { new ProgramSetting { Id = "1cv8.exe", DisplayName = "1С:Предприятие", Action = ProgramAction.Close } },
        };
        var running = ProgramScanner.Scan(processes, windows, settings, selfPid: 1);
        running.Single(p => p.Id == "bitrix24.exe").AllowTerminate = true;
        programsWindow.SetRows(ProgramCatalog.BuildRows(settings, running, DemoData.Shortcuts));
        await Capture(programsWindow.Root, Path.Combine(dir, "5-programmy.png"));
        // Настройки с включённым расписанием.
        var settingsWindow = new SettingsWindow();
        settingsWindow.Present();
        settingsWindow.SizeToDesign(fitScreen: false);
        settingsWindow.UseDesignSizeForCapture();
        settingsWindow.Load(new AppSettings { ReturnSchedule = new ReturnSchedule { Enabled = true, Time = "09:00" } }, autostart: true, hotkeyProblem: null);
        await Capture(settingsWindow.Root, Path.Combine(dir, "6-nastroyki.png"));

        // Предложение по расписанию.
        window.ShowReturnOffer("по будням в 09:00", [("bitrix24.exe", "Битрикс24"), ("chrome.exe", "Chrome"), ("1cv8.exe", "1С:Предприятие")]);
        await Capture(window.Root, Path.Combine(dir, "7-pora.png"));

        // Последнее закрытое окно завершает программу — поэтому закрываем оба только в конце.
        programsWindow.CloseForGood();
        settingsWindow.CloseForGood();
        window.CloseForGood();
        return true;
    }

    private static async Task Capture(FrameworkElement root, string path)
    {
        // Дать разметке и отрисовке пройти.
        for (var i = 0; i < 3; i++) await Task.Delay(300);
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var pixels = await rtb.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
