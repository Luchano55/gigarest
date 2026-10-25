using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GigaRest.App;

/// <summary>
/// Видео-сцена по кругу без шва (решение D-VIDEO): два плеера; за секунду до конца второй начинает
/// сначала и плавно проступает поверх первого. Без звука, аппаратное декодирование системного плеера.
/// </summary>
internal sealed class VideoLoop : Grid, IDisposable
{
    private static readonly TimeSpan Crossfade = TimeSpan.FromSeconds(1.0);
    private readonly MediaPlayer[] _players = new MediaPlayer[2];
    private readonly MediaPlayerElement[] _views = new MediaPlayerElement[2];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _active;
    private bool _fading;
    private bool _paused;

    public VideoLoop(Uri source)
    {
        for (var i = 0; i < 2; i++)
        {
            _players[i] = new MediaPlayer { IsMuted = true, AutoPlay = false, Source = MediaSource.CreateFromUri(source) };
            _views[i] = new MediaPlayerElement { Stretch = Stretch.UniformToFill, AreTransportControlsEnabled = false, Opacity = i == 0 ? 1 : 0 };
            _views[i].SetMediaPlayer(_players[i]);
            Children.Add(_views[i]);
        }
        _timer.Tick += (_, _) => Tick();
        _players[0].Play();
        _timer.Start();
    }

    private void Tick()
    {
        if (_paused || _fading) return;
        var session = _players[_active].PlaybackSession;
        var duration = session.NaturalDuration;
        if (duration <= TimeSpan.Zero) return;
        // Совсем короткий ролик — обычный повтор, плавность не успеть.
        if (duration < TimeSpan.FromSeconds(3))
        {
            _players[_active].IsLoopingEnabled = true;
            return;
        }
        if (session.Position < duration - Crossfade - TimeSpan.FromMilliseconds(150)) return;

        _fading = true;
        var next = 1 - _active;
        _players[next].PlaybackSession.Position = TimeSpan.Zero;
        _players[next].Play();
        Canvas.SetZIndex(_views[next], 1);
        Canvas.SetZIndex(_views[_active], 0);
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = Crossfade };
        Storyboard.SetTarget(fade, _views[next]);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        var old = _active;
        board.Completed += (_, _) =>
        {
            _players[old].Pause();
            _players[old].PlaybackSession.Position = TimeSpan.Zero;
            _views[old].Opacity = 0;
            _active = next;
            _fading = false;
        };
        board.Begin();
    }

    public void Pause()
    {
        _paused = true;
        foreach (var p in _players) p.Pause();
    }

    public void Resume()
    {
        _paused = false;
        _players[_active].Play();
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var p in _players)
        {
            p.Pause();
            p.Source = null;
            p.Dispose();
        }
    }
}
