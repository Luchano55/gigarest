using GigaRest.Core.Scenes;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Эффект поверх сцены — снег, туман, огоньки или дождь (один за раз), с глубиной: дальний слой мелкий и
/// медленный, ближний — крупный, мягкий и быстрый. Всё на анимациях композитора: движение считает система
/// с частотой монитора (165 Гц), наш процесс почти не нагружается.
/// </summary>
internal sealed class EffectsLayer
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly List<Storyboard> _boards = [];
    private readonly Random _random = new(42);
    private SceneEffect _effect;
    private double _width, _height;
    private bool _paused;

    public UIElement Element => _canvas;

    public static string FogTexture => System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "fog.png");

    public void Set(SceneEffect effect, double width, double height)
    {
        if (effect == _effect && Math.Abs(width - _width) < 1 && Math.Abs(height - _height) < 1 && _canvas.Children.Count > 0) return;
        _effect = effect;
        _width = width;
        _height = height;
        Clear();
        if (width < 10 || height < 10) return;
        // Всё, что вылетает за край (капли, хлопья, сдвинутый туман), — обрезаем по экрану.
        _canvas.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, width, height) };
        switch (effect)
        {
            case SceneEffect.Snow: Snow(width, height); break;
            case SceneEffect.Fog: Fog(width, height); break;
            case SceneEffect.Lights: Lights(width, height); break;
            case SceneEffect.Rain: Rain(width, height); break;
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

    private double R(double from, double to) => from + _random.NextDouble() * (to - from);

    // ---------- снег ----------

    private static Brush SoftDot(string center, string edge)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop { Color = Hex(center), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = Hex(center), Offset = 0.35 });
        b.GradientStops.Add(new GradientStop { Color = Hex(edge), Offset = 1 });
        return b;
    }

    /// <summary>Снег в три слоя: дальний мелкий и медленный, ближний крупный, мягкий, с ветром.</summary>
    private void Snow(double w, double h)
    {
        var area = w * h / (1920.0 * 1080);
        (int count, double size0, double size1, double speed0, double speed1, double op0, double op1, double sway)[] layers =
        [
            ((int)(150 * area), 1.5, 2.6, 22, 38, 0.30, 0.50, 12),   // дальний
            ((int)(90 * area), 3.0, 4.8, 48, 80, 0.55, 0.80, 24),    // средний
            ((int)(22 * area), 9.0, 16.0, 110, 170, 0.30, 0.50, 60), // ближний, «не в фокусе»
        ];
        foreach (var (count, s0, s1, v0, v1, o0, o1, sway) in layers)
        {
            var brush = SoftDot("#FFFFFFFF", "#00FFFFFF");
            for (var i = 0; i < Math.Max(count, 10); i++)
            {
                var size = R(s0, s1);
                var flake = new Ellipse { Width = size, Height = size, Fill = brush, Opacity = R(o0, o1) };
                var move = new TranslateTransform();
                flake.RenderTransform = move;
                Canvas.SetLeft(flake, R(-40, w + 40));
                _canvas.Children.Add(flake);
                Fall(move, h, R(v0, v1), size);
                Add(move, "X", Sway(sway, 3 + size / 3));
            }
        }
    }

    /// <summary>Падение по кругу: старт на случайной высоте, внизу — мгновенно наверх.</summary>
    private void Fall(TranslateTransform move, double h, double speed, double size)
    {
        var top = -size - 20;
        var bottom = h + 20;
        var total = (bottom - top) / speed;
        var start = R(top, bottom);
        var toBottom = (bottom - start) / speed;
        var fall = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(total) };
        fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = start, KeyTime = TimeSpan.Zero });
        fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = bottom, KeyTime = TimeSpan.FromSeconds(toBottom) });
        fall.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = top, KeyTime = TimeSpan.FromSeconds(Math.Min(toBottom + 0.001, total)) });
        fall.KeyFrames.Add(new LinearDoubleKeyFrame { Value = start, KeyTime = TimeSpan.FromSeconds(total) });
        Add(move, "Y", fall);
    }

    private DoubleAnimation Sway(double amplitude, double seconds) => new()
    {
        From = -amplitude * R(0.5, 1), To = amplitude * R(0.5, 1), Duration = TimeSpan.FromSeconds(seconds * R(0.8, 1.4)),
        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        BeginTime = TimeSpan.FromSeconds(R(0, 3)),
    };

    // ---------- туман ----------

    /// <summary>Туман: три слоя настоящей текстуры плывут у земли с разной скоростью и «дышат».</summary>
    private void Fog(double w, double h)
    {
        (double height, double top, double seconds, double opacity, bool reverse)[] layers =
        [
            (0.55, 0.47, 170, 0.32, false),
            (0.62, 0.44, 120, 0.40, true),
            (0.42, 0.64, 80, 0.30, false),
        ];
        foreach (var (lh, top, seconds, opacity, reverse) in layers)
        {
            // В текстуре две копии бесшовного тумана подряд: одна картинка шириной 2 экрана, сдвиг на экран — и сначала.
            var image = new Image
            {
                Source = new BitmapImage(new Uri(FogTexture)), Width = w * 2, Height = h * lh, Stretch = Stretch.Fill, Opacity = opacity,
            };
            var move = new TranslateTransform();
            image.RenderTransform = move;
            Canvas.SetTop(image, h * top);
            _canvas.Children.Add(image);
            Add(move, "X", new DoubleAnimation
            {
                From = reverse ? -w : 0, To = reverse ? 0 : -w, Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever,
            });
            Add(image, "Opacity", new DoubleAnimation
            {
                From = opacity * 0.6, To = opacity, Duration = TimeSpan.FromSeconds(R(14, 26)), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }

    // ---------- огоньки ----------

    /// <summary>Огоньки: размытые блики на заднем плане и тёплые светлячки, которые гаснут и загораются.</summary>
    private void Lights(double w, double h)
    {
        // Блики «не в фокусе» — крупные, едва заметные, медленно плывут.
        for (var i = 0; i < 12; i++)
        {
            var size = R(70, 180);
            var bokeh = new Ellipse { Width = size, Height = size, Fill = SoftDot("#55FFD9A0", "#00FFD9A0"), Opacity = R(0.08, 0.2) };
            var move = new TranslateTransform();
            bokeh.RenderTransform = move;
            Canvas.SetLeft(bokeh, R(0, w));
            Canvas.SetTop(bokeh, R(h * 0.2, h * 0.85));
            _canvas.Children.Add(bokeh);
            Add(move, "X", Sway(R(30, 80), R(18, 30)));
            Add(move, "Y", Sway(R(15, 40), R(20, 34)));
        }
        // Светлячки: яркая точка и мягкое свечение.
        var count = (int)Math.Clamp(w / 50, 24, 48);
        for (var i = 0; i < count; i++)
        {
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop { Color = Hex("#FFFFF1C8"), Offset = 0 });
            glow.GradientStops.Add(new GradientStop { Color = Hex("#AAFFD68C"), Offset = 0.18 });
            glow.GradientStops.Add(new GradientStop { Color = Hex("#33FFC86E"), Offset = 0.45 });
            glow.GradientStops.Add(new GradientStop { Color = Hex("#00FFC86E"), Offset = 1 });
            var size = R(12, 26);
            var dot = new Ellipse { Width = size, Height = size, Fill = glow, Opacity = 0.1 };
            var move = new TranslateTransform();
            dot.RenderTransform = move;
            Canvas.SetLeft(dot, R(0, w));
            Canvas.SetTop(dot, R(h * 0.4, h * 0.92));
            _canvas.Children.Add(dot);
            Add(dot, "Opacity", new DoubleAnimation
            {
                From = 0.05, To = R(0.7, 1), Duration = TimeSpan.FromSeconds(R(1.8, 4.5)), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(R(0, 6)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
            Add(move, "X", Sway(R(15, 45), R(5, 11)));
            Add(move, "Y", Sway(R(10, 30), R(6, 12)));
        }
    }

    // ---------- дождь ----------

    /// <summary>Дождь в два слоя: дальний — тонкие короткие штрихи, ближний — длиннее и заметнее; лёгкий наклон.</summary>
    private void Rain(double w, double h)
    {
        var area = w * h / (1920.0 * 1080);
        (int count, double thick, double len0, double len1, double speed, double opacity)[] layers =
        [
            ((int)(260 * area), 1, 14, 26, 900, 0.18),
            ((int)(70 * area), 1.6, 30, 52, 1500, 0.26),
        ];
        const double angle = 12; // градусов от вертикали
        var slant = Math.Tan(angle * Math.PI / 180);
        foreach (var (count, thick, l0, l1, speed, opacity) in layers)
        {
            var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            brush.GradientStops.Add(new GradientStop { Color = Hex("#00DCE6FF"), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Hex("#FFDCE6FF"), Offset = 1 });
            for (var i = 0; i < Math.Max(count, 10); i++)
            {
                var len = R(l0, l1);
                var drop = new Rectangle
                {
                    Width = thick, Height = len, Fill = brush, Opacity = opacity * R(0.6, 1.2),
                    RenderTransform = new CompositeTransform { Rotation = angle },
                };
                var move = (CompositeTransform)drop.RenderTransform;
                Canvas.SetLeft(drop, R(-h * slant, w));
                _canvas.Children.Add(drop);
                var v = speed * R(0.85, 1.15);
                var total = (h + len * 2) / v;
                var offset = R(0, total);
                // Падение с наклоном: X и Y по одному закону, с разным стартом у каждой капли.
                Add(move, "TranslateY", Loop(-len, h + len, total, offset));
                Add(move, "TranslateX", Loop(len * slant, -(h + len) * slant, total, offset));
            }
        }
    }

    private static DoubleAnimationUsingKeyFrames Loop(double from, double to, double total, double offset)
    {
        var mid = from + (to - from) * (offset / total);
        var toEnd = total - offset;
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(total) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame { Value = mid, KeyTime = TimeSpan.Zero });
        a.KeyFrames.Add(new LinearDoubleKeyFrame { Value = to, KeyTime = TimeSpan.FromSeconds(toEnd) });
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame { Value = from, KeyTime = TimeSpan.FromSeconds(Math.Min(toEnd + 0.0005, total)) });
        a.KeyFrames.Add(new LinearDoubleKeyFrame { Value = mid, KeyTime = TimeSpan.FromSeconds(total) });
        return a;
    }

    private void Add(DependencyObject target, string property, Timeline animation)
    {
        var board = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
        _boards.Add(board);
    }
}
