using GigaRest.Core.Desktop;
using GigaRest.Windows;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>Плитка второго монитора: окно программы и как его открыть, если программа закрылась.</summary>
public sealed record TileInfo(long Hwnd, string ProgramId, string Name, string? ExePath);

/// <summary>
/// Второй монитор (макет «Второй монитор · живые плитки»): до четырёх живых копий окон.
/// Окна программ стоят развёрнутыми под этим окном («паркуются»): щелчок по плитке — окно сверху,
/// «Свернуть» или клавиша (по умолчанию Ctrl+Alt+End) — снова плитки.
/// </summary>
public sealed class TilesWindow : Window
{
    private readonly Grid _root;
    private readonly Grid _grid;
    private readonly TextBlock _clock, _caption, _hint;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(TileInfo Info, Border Slot, TextBlock Status, Thumbnail? Thumb)> _tiles = [];
    private bool _closing;

    public event Action<TileInfo>? OpenRequested;
    public FrameworkElement Root => _root;

    public TilesWindow()
    {
        Title = "GigaRest — второй монитор";
        _root = new Grid { Background = Brush("#05060A"), RequestedTheme = ElementTheme.Dark };
        _root.Children.Add(new Image
        {
            Source = new BitmapImage(RestScreens.SceneUri("forest.jpg")), Stretch = Stretch.UniformToFill, Opacity = 0.85,
        });
        _root.Children.Add(new Border { Background = Brush("#73040509") });

        var layout = new Grid { Padding = new Thickness(40, 28, 40, 40), RowSpacing = 20 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _clock = new TextBlock { FontFamily = new FontFamily("Georgia"), FontSize = 40, Foreground = Brush("#E6FFFFFF"), VerticalAlignment = VerticalAlignment.Bottom };
        header.Children.Add(_clock);
        _caption = Text("", 13, Hex("#8CFFFFFF"));
        _caption.Margin = new Thickness(16, 0, 0, 6);
        _caption.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_caption, 1);
        header.Children.Add(_caption);
        var hint = _hint = Text("«Свернуть» или Ctrl+Alt+End — обратно в плитки", 13, Hex("#8CFFFFFF"));
        hint.VerticalAlignment = VerticalAlignment.Bottom;
        hint.Margin = new Thickness(0, 0, 0, 6);
        Grid.SetColumn(hint, 2);
        header.Children.Add(hint);
        layout.Children.Add(header);

        _grid = new Grid { RowSpacing = 24, ColumnSpacing = 24 };
        Grid.SetRow(_grid, 1);
        layout.Children.Add(_grid);
        _root.Children.Add(layout);

        Content = _root;
        _root.SizeChanged += (_, _) => PlaceThumbnails();
        _root.Loaded += (_, _) => PlaceThumbnails();
        _timer.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("HH:mm");
        _clock.Text = DateTime.Now.ToString("HH:mm");
        _timer.Start();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));
        AppWindow.Closing += (_, e) => { if (!_closing) e.Cancel = true; };
    }

    public void SetTiles(IReadOnlyList<TileInfo> tiles, bool liveCopies = true)
    {
        foreach (var t in _tiles) t.Thumb?.Dispose();
        _tiles.Clear();
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        var shown = tiles.Take(4).ToList();
        var columns = shown.Count >= 2 ? 2 : 1;
        var rows = shown.Count > 2 ? 2 : 1;
        for (var c = 0; c < columns; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < rows; r++) _grid.RowDefinitions.Add(new RowDefinition());

        var hwnd = RestScreens.Hwnd(this);
        for (var i = 0; i < shown.Count; i++)
        {
            var info = shown[i];
            var (tile, slot, status) = Tile(info);
            Grid.SetRow(tile, i / columns);
            Grid.SetColumn(tile, i % columns);
            _grid.Children.Add(tile);
            Thumbnail? thumb = null;
            if (liveCopies && WindowTools.IsAlive(info.Hwnd))
            {
                thumb = new Thumbnail(hwnd, (nint)info.Hwnd);
                if (!thumb.IsValid) { thumb.Dispose(); thumb = null; }
            }
            _tiles.Add((info, slot, status, thumb));
        }
        _caption.Text = $"второй монитор · {GigaRest.Core.Text.Sizes.Plural(shown.Count, "окно", "окна", "окон")} · щёлкните, чтобы открыть";
        PlaceThumbnails();
    }

    private (Border Tile, Border Slot, TextBlock Status) Tile(TileInfo info)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        head.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(6), Background = Brush(ProgramColor(info.ProgramId)) });
        head.Children.Add(Text(info.Name, 14, Hex("#E6FFFFFF"), FontWeights.SemiBold));
        grid.Children.Add(head);

        // Сюда Windows рисует живую копию окна. Пока копии нет — спокойная заглушка.
        var placeholder = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 6 };
        placeholder.Children.Add(new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Background = Brush(ProgramColor(info.ProgramId)), HorizontalAlignment = HorizontalAlignment.Center });
        placeholder.Children.Add(Text(info.Name, 15, Hex("#B3FFFFFF")));
        var slot = new Border { Margin = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(8), Background = Brush("#14FFFFFF"), Child = placeholder };
        Grid.SetRow(slot, 1);
        grid.Children.Add(slot);

        var status = Text("щёлкните, чтобы открыть", 12, Hex("#8CFFFFFF"));
        status.Margin = new Thickness(16, 10, 16, 10);
        Grid.SetRow(status, 2);
        grid.Children.Add(status);

        var tile = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = Brush("#8C101117"),
            BorderBrush = Brush("#1AFFFFFF"),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
        tile.PointerEntered += (_, _) => tile.BorderBrush = Brush("#47FFFFFF");
        tile.PointerExited += (_, _) => tile.BorderBrush = Brush("#1AFFFFFF");
        tile.Tapped += (_, _) => OpenRequested?.Invoke(info);
        return (tile, slot, status);
    }

    /// <summary>Копии окон — ровно в прямоугольники плиток (в настоящих пикселях этого монитора).</summary>
    private void PlaceThumbnails()
    {
        if (_root.XamlRoot is null) return;
        var scale = _root.XamlRoot.RasterizationScale;
        foreach (var (_, slot, _, thumb) in _tiles)
        {
            if (thumb is null || slot.ActualWidth <= 0) continue;
            var p = slot.TransformToVisual(_root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            thumb.Place((int)(p.X * scale), (int)(p.Y * scale), (int)(slot.ActualWidth * scale), (int)(slot.ActualHeight * scale));
        }
    }

    // ---------- плавное открытие и возврат ----------

    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(260);
    private EventHandler<object>? _frame;

    /// <summary>Плитка плавно «вырастает» до всего монитора, потом поднимается настоящее окно.</summary>
    public void AnimateOpen(long hwnd, Action done) => Animate(hwnd, opening: true, done);

    /// <summary>Окно свернули: копия плавно «уменьшается» обратно в свою плитку.</summary>
    public void AnimateClose(long hwnd) => Animate(hwnd, opening: false, null);

    private void Animate(long hwnd, bool opening, Action? done)
    {
        var index = _tiles.FindIndex(t => t.Info.Hwnd == hwnd);
        if (index < 0 || _tiles[index].Thumb is null || _root.XamlRoot is null) { done?.Invoke(); return; }
        var (_, slot, _, thumb) = _tiles[index];
        var scale = _root.XamlRoot.RasterizationScale;
        var p = slot.TransformToVisual(_root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
        (double L, double T, double W, double H) tile = (p.X * scale, p.Y * scale, slot.ActualWidth * scale, slot.ActualHeight * scale);
        (double L, double T, double W, double H) full = (0, 0, _root.ActualWidth * scale, _root.ActualHeight * scale);
        var (from, to) = opening ? (tile, full) : (full, tile);

        // Остальные копии на время анимации прячем: эта — главная.
        for (var i = 0; i < _tiles.Count; i++) if (i != index) _tiles[i].Thumb?.Hide();
        if (_frame is not null) CompositionTarget.Rendering -= _frame;
        var started = DateTime.UtcNow;
        _frame = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / OpenDuration.TotalMilliseconds);
            var e = 1 - Math.Pow(1 - t, 3); // плавное замедление в конце
            thumb!.Place((int)(from.L + (to.L - from.L) * e), (int)(from.T + (to.T - from.T) * e),
                (int)(from.W + (to.W - from.W) * e), (int)(from.H + (to.H - from.H) * e));
            if (t < 1) return;
            CompositionTarget.Rendering -= _frame;
            _frame = null;
            done?.Invoke();
            // Когда настоящее окно уже сверху — вернуть копии на места.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (opening) await Task.Delay(400);
                PlaceThumbnails();
            });
        };
        CompositionTarget.Rendering += _frame;
    }

    /// <summary>Программа на плитке закрылась.</summary>
    public void MarkClosed(long hwnd)
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t.Info.Hwnd != hwnd) continue;
            t.Thumb?.Dispose();
            _tiles[i] = (t.Info, t.Slot, t.Status, null);
            t.Status.Text = $"{t.Info.Name} закрыта · щёлкните, чтобы открыть";
        }
    }

    public void ShowOn(MonitorInfo monitor)
    {
        AppWindow.IsShownInSwitchers = false;
        RestScreens.FullScreenOn(this, monitor);
        // До показа: Chrome, Claude, Zen не должны считать экран плиток закрывающим их окна (D-THUMBS).
        WindowTools.MakeNonOccluding(RestScreens.Hwnd(this));
        RestScreens.BringToFront(this);
        // Полноэкранный режим может переписать стили окна — повторяем, когда окно уже показано.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => WindowTools.MakeNonOccluding(RestScreens.Hwnd(this)));
        PlaceThumbnails();
    }

    public nint Handle => RestScreens.Hwnd(this);

    public void SetHotkeyHint(string hotkey) => _hint.Text = $"«Свернуть» или {hotkey} — обратно в плитки";

    /// <summary>Для скриншотов: корень 1440×900, без полноэкранного режима.</summary>
    public void UseDesignSizeForCapture()
    {
        _root.Width = 1440;
        _root.Height = 900;
        Content = new Canvas { Children = { _root } };
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1440, 900));
        Activate();
    }

    public void CloseForGood()
    {
        _closing = true;
        _timer.Stop();
        foreach (var t in _tiles) t.Thumb?.Dispose();
        _tiles.Clear();
        Close();
    }
}
