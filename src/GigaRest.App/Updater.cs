using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using GigaRest.Core.Logging;
using GigaRest.Core.Text;
using GigaRest.Core.Updates;

namespace GigaRest.App;

/// <summary>
/// Самообновление (как в DropWheel): через 30 секунд после запуска, раз в 6 часов и по кнопке читаем version.json
/// выпуска «свежая сборка»; сборка новее — качаем архив, сверяем SHA-256, раскладываем файлы и перезапускаемся.
/// Единственное обращение программы в сеть.
/// </summary>
internal sealed class Updater : IDisposable
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private int _busy;

    public Updater()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GigaRest-updater");
    }

    public UpdateManifest? Available { get; private set; }

    /// <summary>Канал обновлений (stable или dev) — из настроек, «как при установке» — по версии программы.</summary>
    public string Channel { get; set; } = UpdateChannels.Resolve(null, AppVersion.Display);
    public static string AppDir => AppContext.BaseDirectory;

    /// <returns>Найденное обновление или null. Ручная проверка пробрасывает ошибку сети.</returns>
    public async Task<UpdateManifest?> CheckAsync(bool manual)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var json = await _http.GetStringAsync(UpdateManifest.ManifestUrl(Channel), cts.Token);
            var manifest = UpdateManifest.Parse(json);
            Log.Info($"Обновления ({Channel}): у нас {AppVersion.CurrentBuild} ({AppVersion.Display}), в выпуске {manifest?.Build} ({manifest?.Version})");
            Available = manifest is not null && manifest.IsNewerThan(AppVersion.CurrentBuild) ? manifest : null;
            return Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warn("Обновления: проверка не удалась: " + ex.Message);
            if (manual) throw;
            return null;
        }
    }

    /// <summary>Скачать, проверить, разложить файлы, запустить новую версию. Старая после этого выходит.</summary>
    public async Task InstallAsync(UpdateManifest manifest, IProgress<(string Text, int Percent)>? status, Action quit)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var work = Path.Combine(Path.GetTempPath(), "GigaRest-update-" + manifest.Build);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            var staging = Directory.CreateDirectory(Path.Combine(work, "staging")).FullName;
            var zip = Path.Combine(work, manifest.Zip);
            await DownloadAsync(manifest.ZipUrl(Channel), zip, status);

            status?.Report((L.T("Проверяем архив…", "Checking the download…"), -1));
            await using (var check = File.OpenRead(zip))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(check));
                if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("Архив обновления повреждён (не сходится контрольная сумма). Попробуйте ещё раз.", "The update download is damaged (checksum doesn't match). Please try again."));
            }

            status?.Report((L.T("Раскладываем файлы…", "Putting files in place…"), -1));
            var unpack = Path.Combine(work, "unpack");
            ZipFile.ExtractToDirectory(zip, unpack, overwriteFiles: true);
            var root = Directory.Exists(Path.Combine(unpack, "GigaRest")) ? Path.Combine(unpack, "GigaRest") : unpack;
            foreach (var src in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var dst = Path.Combine(staging, Path.GetRelativePath(root, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Move(src, dst, overwrite: true);
            }
            var changed = UpdateApplier.Apply(staging, AppDir);
            Log.Info($"Обновление до сборки {manifest.Build} ({manifest.Version}): файлов {changed.Count}");
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }

            // Новая версия сама попросит эту выйти (одна копия программы); выходим и сами.
            var psi = new ProcessStartInfo(Path.Combine(AppDir, "GigaRest.exe")) { UseShellExecute = false, WorkingDirectory = AppDir };
            psi.ArgumentList.Add("--after-update");
            Process.Start(psi)?.Dispose();
            status?.Report((L.T("Готово — перезапускаемся…", "Done, restarting…"), 100));
            quit();
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task DownloadAsync(string url, string path, IProgress<(string Text, int Percent)>? status)
    {
        Log.Info("Обновления: скачиваю " + url);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(path);
        var buffer = new byte[256 * 1024];
        long done = 0;
        var last = -1;
        while (true)
        {
            using var stall = new CancellationTokenSource(StallTimeout);
            int read;
            try { read = await source.ReadAsync(buffer, stall.Token); }
            catch (OperationCanceledException) { throw new IOException(L.T("Загрузка остановилась: нет данных больше минуты. Проверьте интернет и повторите.", "The download stalled: no data for over a minute. Check your internet connection and try again.")); }
            if (read == 0) break;
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            var percent = total > 0 ? (int)(done * 100 / total) : -1;
            if (percent != last && percent >= 0)
            {
                last = percent;
                status?.Report((L.T($"Скачиваем: {percent}% ({done / 1048576} из {total / 1048576} МБ)", $"Downloading: {percent}% ({done / 1048576} of {total / 1048576} MB)"), percent));
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
