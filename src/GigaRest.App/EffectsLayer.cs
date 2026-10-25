using GigaRest.Core.Scenes;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Эффект поверх сцены — снег, туман или огоньки (один за раз). Всё на анимациях композитора:
/// движение считает система с частотой монитора, наш процесс почти не нагружается.
/// </summary>
internal sealed class EffectsLayer
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly List<Storyboard> _boards = [];
    private readonly Random _random = new(42);
    private SceneEffect _effect;
    private Size _size;
    private bool _paused;

    public UIElement Element => _canvas;

    public void Set(SceneEffect effect, double width, double height)
    {
        if (effect == _effect && Math.Abs(width - _size.Width) < 1 && Math.Abs(height - _size.Height) < 1 && _canvas.Children.Count > 0) return;
        _effect = effect;
        _size = new Size(width, height);
        Clear();
        if (width < 10 || height < 10) return;
        switch (effect)
        {
            case SceneEffect.Snow: Snow(width, height); break;
            case SceneEffect.Fog: Fog(width, height); break;
            case SceneEffect.Lights: Lights(width, height); break;
        }
        foreach (var b in _boards) { b.Begin(); if (_paused) b.Pause(); }
    }

    public void Pause(bool pause)
    {
        _paused = pause;
        foreach (var b in _boards) { if (pause) b.Pause(); else b.Resume(); }
    }

    private void Clear()
    {
        foreach (var b in _boards) b.Stop();
        _boards.Clear();
        _canvas.Children.Clear();
    }

    /// <summary>Снег: хлопья разного размера падают с разной скоростью и чуть сносятся ветром.</summary>
    private void Snow(double w, double h)
    {
        var count = (int)Math.Clamp(w * h / 14000, 80, 220);
        for (var i = 0; i < count; i++)
        {
            var size = 2 + _random.NextDouble() * (i % 7 == 0 ? 5 : 3);
            var flake = new Ellipse { Width = size, Height = size, Fill = Brush(Colors.White), Opacity = 0.35 + _random.NextDouble() * 0.45 };
            var move = new TranslateTransform();
            flake.RenderTransform = move;
            Canvas.SetLeft(flake, _random.NextDouble() * w);
            _canvas.Children.Add(flake);

            // Падение по кругу: старт с разной высоты, у нижнего края — мгновенно наверх.
            var speed = 40 + size * 14 + _random.NextDouble() * 20; // пикс/с
            var total = (h + 80) / speed;
            var startY = _random.NextDouble() * (h + 80) - 40;
            var toBottom = (h + 40 - startY) / speed;
            var fall = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(total) };
            fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = startY, KeyTime = TimeSpan.Zero });
            fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = h + 40, KeyTime = TimeSpan.FromSeconds(toBottom) });
            fall.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = -40, KeyTime = TimeSpan.FromSeconds(Math.Min(toBottom + 0.001, total)) });
            fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = startY, KeyTime = TimeSpan.FromSeconds(total) });
            var drift = new DoubleAnimation
            {
                From = -20, To = 20 + _random.NextDouble() * 30, Duration = TimeSpan.FromSeconds(4 + _random.NextDouble() * 6),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Add(move, "Y", fall);
            Add(move, "X", drift);
        }
    }

    /// <summary>Туман: два широких мягких облака медленно плывут у земли навстречу друг другу.</summary>
    private void Fog(double w, double h)
    {
        for (var i = 0; i < 3; i++)
        {
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop { Color = Hex(i == 1 ? "#33D2D6EA" : "#2AC8CDE6"), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Hex("#00C8CDE6"), Offset = 1 });
            var cloud = new Ellipse { Width = w * 1.1, Height = h * (0.45 + i * 0.08), Fill = brush };
            var move = new TranslateTransform();
            cloud.RenderTransform = move;
            Canvas.SetLeft(cloud, -w * 0.3 + i * w * 0.35);
            Canvas.SetTop(cloud, h * (0.55 + i * 0.07));
            _canvas.Children.Add(cloud);
            Add(move, "X", new DoubleAnimation
            {
                From = i % 2 == 0 ? -w * 0.15 : w * 0.15, To = i % 2 == 0 ? w * 0.15 : -w * 0.15,
                Duration = TimeSpan.FromSeconds(70 + i * 25), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
            Add(cloud, "Opacity", new DoubleAnimation
            {
                From = 0.6, To = 1, Duration = TimeSpan.FromSeconds(20 + i * 7), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            });
        }
    }

    /// <summary>Огоньки: тёплые светлячки медленно гаснут и загораются, чуть плавая.</summary>
    private void Lights(double w, double h)
    {
        var count = (int)Math.Clamp(w / 55, 20, 40);
        for (var i = 0; i < count; i++)
        {
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop { Color = Hex("#FFFFE3A8"), Offset = 0 });
            glow.GradientStops.Add(new GradientStop { Color = Hex("#66FFD68C"), Offset = 0.25 });
            glow.GradientStops.Add(new GradientStop { Color = Hex("#00FFD68C"), Offset = 1 });
            var size = 14 + _random.NextDouble() * 10;
            var dot = new Ellipse { Width = size, Height = size, Fill = glow, Opacity = 0.1 };
            var move = new TranslateTransform();
            dot.RenderTransform = move;
            Canvas.SetLeft(dot, _random.NextDouble() * w);
            Canvas.SetTop(dot, h * 0.45 + _random.NextDouble() * h * 0.45);
            _canvas.Children.Add(dot);
            Add(dot, "Opacity", new DoubleAnimation
            {
                From = 0.1, To = 0.9, Duration = TimeSpan.FromSeconds(2 + _random.NextDouble() * 3), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(_random.NextDouble() * 5),
            });
            Add(move, "X", Wander());
            Add(move, "Y", Wander());
        }
    }

    private DoubleAnimation Wander() => new()
    {
        From = -(10 + _random.NextDouble() * 25), To = 10 + _random.NextDouble() * 25,
        Duration = TimeSpan.FromSeconds(6 + _random.NextDouble() * 8), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };

    private void Add(DependencyObject target, string property, Timeline animation)
    {
        var board = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
        _boards.Add(board);
    }

    private readonly record struct Size(double Width, double Height);
}
