using GigaRest.Core.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static GigaRest.App.Ui;

namespace GigaRest.App;

/// <summary>
/// Небольшое окно поверх всех в правом нижнем углу: идёт обновление — сколько скачано, что сейчас делаем.
/// Закрывается само: программа перезапускается новой версией. Ошибка — показываем её и кнопку «Закрыть».
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly TextBlock _title, _status;
    private readonly ProgressBar _bar;
    private readonly Button _close;
    private readonly Grid _root;

    public FrameworkElement Root => _root;

    public UpdateWindow(string version)
    {
        Title = L.T("Обновление GigaRest", "GigaRest update");
        _root = new Grid { Padding = new Thickness(22, 18, 22, 18), RowSpacing = 10 };
        for (var i = 0; i < 4; i++) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _title = Text(L.T($"Обновление до {version}", $"Updating to {version}"), 16, Ink, Microsoft.UI.Text.FontWeights.SemiBold);
        _root.Children.Add(_title);
        _bar = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true, Height = 6, Foreground = Brush(Warm) };
        Grid.SetRow(_bar, 1);
        _root.Children.Add(_bar);
        _status = Text(L.T("Готовимся…", "Getting ready…"), 13, Lead, wrap: true);
        Grid.SetRow(_status, 2);
        _root.Children.Add(_status);
        _close = ColoredButton(L.T("Закрыть", "Close"), Hover, Hex("#2EFFFFFF"), Ink);
        _close.Visibility = Visibility.Collapsed;
        _close.HorizontalAlignment = HorizontalAlignment.Right;
        _close.Click += (_, _) => Close();
        Grid.SetRow(_close, 3);
        _root.Children.Add(_close);
        Content = _root;
        DarkWindow(AppWindow, _root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"));

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        // Правый нижний угол основного монитора (над панелью задач), с учётом масштаба.
        var area = DisplayArea.Primary.WorkArea;
        var scale = GetDpiForWindow(RestScreens.Hwnd(this)) / 96.0;
        int w = (int)(420 * scale), h = (int)(170 * scale), m = (int)(16 * scale);
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(area.X + area.Width - w - m, area.Y + area.Height - h - m, w, h));
    }

    /// <summary>Ход: текст и процент (меньше 0 — без процента).</summary>
    public void Report(string text, int percent)
    {
        _status.Text = text;
        _bar.IsIndeterminate = percent < 0;
        if (percent >= 0) _bar.Value = percent;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);

    public void Fail(string message)
    {
        _title.Text = L.T("Обновление не получилось", "Update failed");
        _bar.Visibility = Visibility.Collapsed;
        _status.Text = message;
        _close.Visibility = Visibility.Visible;
    }
}
