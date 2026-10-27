using GigaRest.Core.Logging;
using GigaRest.Core.Sounds;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace GigaRest.App;

/// <summary>
/// Звуки природы по кругу: звук считается один раз (в фоне) и играет системным MediaPlayer без шва.
/// В медиа-сеансы Windows не попадает — кнопки «Слушать» продолжают управлять музыкой.
/// </summary>
internal sealed class NaturePlayer : IDisposable
{
    private static readonly Dictionary<NatureSound, byte[]> Cache = [];
    private MediaPlayer? _player;
    private double _volume = 0.5;

    public NatureSound? Current { get; private set; }

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is not null) _player.Volume = _volume; }
    }

    public async Task PlayAsync(NatureSound sound)
    {
        Stop();
        Current = sound;
        try
        {
            if (!Cache.TryGetValue(sound, out var wav))
            {
                wav = await Task.Run(() => NatureSounds.ToWav(NatureSounds.Generate(sound)));
                Cache[sound] = wav;
            }
            if (Current != sound) return; // пока считали — выбрали другой
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(wav);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var player = new MediaPlayer { IsLoopingEnabled = true, Volume = _volume, AudioCategory = MediaPlayerAudioCategory.Media };
            player.CommandManager.IsEnabled = false;
            player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            player.Play();
            _player = player;
        }
        catch (Exception ex)
        {
            Log.Warn("Звуки природы: " + ex.Message);
            Current = null;
        }
    }

    public void Stop()
    {
        Current = null;
        if (_player is null) return;
        _player.Pause();
        _player.Dispose();
        _player = null;
    }

    public void Dispose() => Stop();
}
