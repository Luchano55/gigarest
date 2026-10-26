using GigaRest.Core.Logging;
using GigaRest.Core.Tiles;
using Windows.Media.Control;

namespace GigaRest.App;

/// <summary>
/// Что сейчас играет — из системных «медиа-сеансов» Windows (то же, что в окошке громкости).
/// Только чтение: ничего не переключаем и не ставим на паузу.
/// </summary>
internal enum MediaCommand { Previous, PlayPause, Next }

internal static class NowPlaying
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static bool _warned;

    /// <summary>Главный медиа-сеанс Windows: что играет (или стоит на паузе) и где.</summary>
    public sealed record Current(string Text, string Source, bool Playing);

    public static async Task<Current?> CurrentAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession() ?? _manager.GetSessions().FirstOrDefault();
            if (session is null) return null;
            var props = await session.TryGetMediaPropertiesAsync();
            var text = TileCaptions.NowPlaying(props?.Title, props?.Artist);
            if (text is null) return null;
            var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new Current(text, TileCaptions.MediaSourceName(session.SourceAppUserModelId), playing);
        }
        catch (Exception ex)
        {
            if (!_warned) Log.Warn("Что играет — не прочитать: " + ex.Message);
            _warned = true;
            return null;
        }
    }

    /// <summary>Кнопки «назад», «играть/пауза», «дальше» — как на клавиатуре с медиаклавишами.</summary>
    public static async Task ControlAsync(MediaCommand command)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _manager.GetCurrentSession() ?? _manager.GetSessions().FirstOrDefault();
            if (session is null) return;
            _ = command switch
            {
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                _ => await session.TryTogglePlayPauseAsync(),
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Кнопка музыки: " + ex.Message);
        }
    }

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
