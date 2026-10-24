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

        window.ShowPlan(programs, hasSecondMonitor: true, notice: null);
        await Capture(window, Path.Combine(dir, "1-perehod.png"));

        window.ShowProgress(programs);
        window.UpdateProgress(new SwitchProgress("Закрываем Outlook…", 2, 4));
        await Capture(window, Path.Combine(dir, "2-progress.png"));

        var results = programs.Where(p => p.Action == ProgramAction.Close).Select(p => p.Id switch
        {
            "bitrix24.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Terminated, false),
            "yandexdisk2.exe" => new CloseResult(p.Id, p.DisplayName, CloseOutcome.InBackground, false),
            _ => new CloseResult(p.Id, p.DisplayName, CloseOutcome.Closed, p.Id == "outlook.exe"),
        }).ToList();
        var report = new RestReport(results, 4, (long)(4.3 * (1L << 30)),
            results.Where(r => !r.IsGone).Select(r => r.Message).ToList());
        window.ShowRest(report);
        await Capture(window, Path.Combine(dir, "3-otdyh.png"));

        window.ShowWork(new ReturnReport(["Битрикс24", "Outlook", "Chrome"], ["1С:Предприятие"], 4));
        await Capture(window, Path.Combine(dir, "4-rabota.png"));

        window.CloseForGood();
        return true;
    }

    private static async Task Capture(SwitcherWindow window, string path)
    {
        // Дать разметке и отрисовке пройти.
        for (var i = 0; i < 3; i++) await Task.Delay(300);
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(window.Root);
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
