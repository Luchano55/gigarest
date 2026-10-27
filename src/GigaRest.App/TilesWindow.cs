using GigaRest.Core.Desktop;
using GigaRest.Core.Text;
using GigaRest.Core.Tiles;
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
    // Окно, открытое с плитки: его копия остаётся на весь экран (под окном её не видно).
    // Свернули окно — под ним уже та же картинка, и она плавно уменьшается в плитку, без скачка.
    private long _openHwnd;
    private bool _liveCaptions, _refreshing;
    private int _ticks;

    public event Action<TileInfo>? OpenRequested;
    private readonly Image _background;

    /// <summary>
    /// Фон — та же сцена, что на «Тихом» экране, приглушённая (ТЗ, раздел 6). Видео-сцена — встроенный лес:
    /// второй видеопоток только ради фона под плитками не нужен.
    /// </summary>
    public void SetBackground(string? imagePath)
    {
        try
        {
            var uri = imagePath is not null && File.Exists(imagePath) ? new Uri(imagePath) : RestScreens.SceneUri("forest.jpg");
            _background.Source = new BitmapImage(uri) { DecodePixelWidth = 2560 };
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            GigaRest.Core.Logging.Log.Warn("Фон плиток: " + ex.Message);
        }
    }
    public FrameworkElement Root => _root;

    public TilesWindow()
    {
        Title = L.T("GigaRest — второй монитор", "GigaRest — second monitor");
        _root = new Grid { Background = Brush("#05060A"), RequestedTheme = ElementTheme.Dark };
        _background = new Image
        {
            Source = new BitmapImage(RestScreens.SceneUri("forest.jpg")), Stretch = Stretch.UniformToFill, Opacity = 0.85,
        };
        _root.Children.Add(_background);
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
        var hint = _hint = Text(L.T("«Свернуть» или Ctrl+Alt+End — обратно в плитки", "Minimize or Ctrl+Alt+End to go back to tiles"), 13, Hex("#8CFFFFFF"));
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
        _timer.Tick += (_, _) =>
        {
            _clock.Text = DateTime.Now.ToString("HH:mm");
            if (_liveCaptions && ++_ticks % 2 == 0) RefreshCaptions();
        };
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
        _liveCaptions = liveCopies;
        if (liveCopies) RefreshCaptions();
        _caption.Text = L.T($"второй монитор · {L.Plural(shown.Count, "окно", "окна", "окон", "window", "windows")} · щёлкните, чтобы открыть", $"second monitor · {L.Plural(shown.Count, "окно", "окна", "окон", "window", "windows")} · click to open");
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

        var status = Text(DefaultStatus, 12, Hex("#8CFFFFFF"));
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.MaxLines = 1;
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

    private static string DefaultStatus => L.T("щёлкните, чтобы открыть", "click to open");

    /// <summary>
    /// Подписи под плитками раз в 2 секунды: что играет, сколько непрочитанных в Telegram, какая страница в браузере.
    /// </summary>
    private async void RefreshCaptions()
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        try
        {
            var media = await NowPlaying.ReadAsync();
            foreach (var (info, _, status, _) in _tiles)
            {
                if (!WindowTools.IsAlive(info.Hwnd)) continue; // закрытую отмечает MarkClosed
                var playing = media.FirstOrDefault(m => TileCaptions.MediaMatches(m.Aumid, info.ProgramId, info.ExePath)).Text;
                status.Text = TileCaptions.For(info.ProgramId, info.Name, WindowTools.Title(info.Hwnd), playing) ?? DefaultStatus;
            }
        }
        catch (Exception ex)
        {
            GigaRest.Core.Logging.Log.Warn("Подписи плиток: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Для скриншотов: подпись плитки без настоящих окон.</summary>
    public void SetStatusForCapture(int index, string text) => _tiles[index].Status.Text = text;

    /// <summary>Копии окон — ровно в прямоугольники плиток (в настоящих пикселях этого монитора).</summary>
    private void PlaceThumbnails()
    {
        if (_root.XamlRoot is null) return;
        var scale = _root.XamlRoot.RasterizationScale;
        foreach (var (info, slot, _, thumb) in _tiles)
        {
            if (thumb is null) continue;
            if (info.Hwnd == _openHwnd)
            {
                thumb.Place(0, 0, (int)(_root.ActualWidth * scale), (int)(_root.ActualHeight * scale));
                continue;
            }
            if (slot.ActualWidth <= 0) continue;
            var p = slot.TransformToVisual(_root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            thumb.Place((int)(p.X * scale), (int)(p.Y * scale), (int)(slot.ActualWidth * scale), (int)(slot.ActualHeight * scale));
        }
    }

    // ---------- плавное открытие и возврат ----------

    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(260);
    private EventHandler<object>? _frame;

    /// <summary>
    /// Плитка плавно «вырастает» до всего монитора; настоящее окно уже стоит прямо под экраном плиток,
    /// экран плиток растворяется — и окно на месте. Без «повторного открытия» и вспышки.
    /// </summary>
    public void AnimateOpen(long hwnd, Action done) => Animate(hwnd, opening: true, () =>
    {
        var tiles = Handle;
        WindowTools.PlaceUnder(hwnd, tiles);
        var started = DateTime.UtcNow;
        EventHandler<object>? fade = null;
        fade = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 180);
            WindowTools.SetAlpha(tiles, (byte)(254 * (1 - t)));
            if (t < 1) return;
            CompositionTarget.Rendering -= fade;
            done();                                   // окно — на передний план (оно уже видно, ничего не меняется)
            WindowTools.PutBelow(tiles, hwnd);        // плитки — под ним
            WindowTools.SetAlpha(tiles, 254);
        };
        CompositionTarget.Rendering += fade;
    });

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
        _openHwnd = opening ? hwnd : 0;

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
            if (_openHwnd == hwnd) _openHwnd = 0;
            t.Thumb?.Dispose();
            _tiles[i] = (t.Info, t.Slot, t.Status, null);
            t.Status.Text = L.T($"{t.Info.Name} закрыта · щёлкните, чтобы открыть", $"{t.Info.Name} is closed · click to open");
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

    public void SetHotkeyHint(string hotkey) => _hint.Text = L.T($"«Свернуть» или {hotkey} — обратно в плитки", $"Minimize or {hotkey} to go back to tiles");

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
