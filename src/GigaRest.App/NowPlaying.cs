using GigaRest.Core.Logging;
using GigaRest.Core.Tiles;
using Windows.Media.Control;

namespace GigaRest.App;

/// <summary>
/// Что сейчас играет — из системных «медиа-сеансов» Windows (то же, что в окошке громкости).
/// Только чтение: ничего не переключаем и не ставим на паузу.
/// </summary>
internal static class NowPlaying
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static bool _warned;

    /// <summary>Играющие сейчас: код приложения-источника и «Исполнитель — Название».</summary>
    public static async Task<List<(string Aumid, string Text)>> ReadAsync()
    {
        var result = new List<(string, string)>();
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var session in _manager.GetSessions())
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                var props = await session.TryGetMediaPropertiesAsync();
                if (TileCaptions.NowPlaying(props?.Title, props?.Artist) is { } text) result.Add((session.SourceAppUserModelId ?? "", text));
            }
        }
        catch (Exception ex)
        {
            if (!_warned) Log.Warn("Что играет — не прочитать: " + ex.Message);
            _warned = true;
        }
        return result;
    }
}
