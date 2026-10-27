using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace GigaRest.App;

/// <summary>
/// Цвета и заготовки элементов по макету Романа («премиум-отделка»): тёмные окна с тёплым и холодным светом,
/// стеклянные карточки, тёплый акцент. Общие для окон перехода, настроек, «Рабочих программ» и обновления.
/// </summary>
internal static class Ui
{
    public static readonly Color PageBg = Hex("#14161D");
    public static readonly Color Ink = Hex("#F4F0E9");
    public static readonly Color Muted = Hex("#8A909C");
    public static readonly Color Lead = Hex("#AEB3BF");
    public static readonly Color Card = Hex("#1C1F27");
    public static readonly Color Accent = Hex("#E8A05B");
    public static readonly Color AccentDark = Hex("#D9914A");
    public static readonly Color AccentText = Hex("#24170C");
    public static readonly Color Warm = Hex("#EEB36C");
    public static readonly Color Good = Hex("#93C9A4");
    public static readonly Color Bad = Hex("#EE8E74");
    public static readonly Color Hover = Color.FromArgb(18, 255, 255, 255);

    public static LinearGradientBrush Vertical(Color top, Color bottom)
    {
        var g = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        g.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = bottom, Offset = 1 });
        return g;
    }

    /// <summary>Мягкое пятно света (cx, cy — центр в долях окна).</summary>
    public static RadialGradientBrush Glow(double cx, double cy, double rx, double ry, Color color)
    {
        var g = new RadialGradientBrush { Center = new(cx, cy), GradientOrigin = new(cx, cy), RadiusX = rx, RadiusY = ry };
        g.GradientStops.Add(new GradientStop { Color = color, Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 1 });
        return g;
    }

    /// <summary>
    /// Тёмное окно по макету: фон с тёплым светом снизу слева и холодным сверху справа, тёмный заголовок,
    /// тёплые галочки и переключатели. root — корневая сетка окна.
    /// </summary>
    public static void DarkWindow(Microsoft.UI.Windowing.AppWindow window, Grid root)
    {
        root.RequestedTheme = ElementTheme.Dark;
        root.Background = Vertical(Hex("#1B1E28"), Hex("#101217"));
        var warm = new Border { Background = Glow(0, 1, 0.65, 0.55, Color.FromArgb(56, 224, 152, 108)), IsHitTestVisible = false };
        var cool = new Border { Background = Glow(1, 0, 0.55, 0.45, Color.FromArgb(33, 110, 132, 210)), IsHitTestVisible = false };
        foreach (var glow in new[] { cool, warm })
        {
            Grid.SetRowSpan(glow, Math.Max(1, root.RowDefinitions.Count));
            Grid.SetColumnSpan(glow, Math.Max(1, root.ColumnDefinitions.Count));
            glow.Margin = new Thickness(-root.Padding.Left, -root.Padding.Top, -root.Padding.Right, -root.Padding.Bottom);
            root.Children.Insert(0, glow);
        }
        var accent = Brush(Accent);
        var dark = Brush(AccentText);
        foreach (var key in new[]
        {
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
            "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
            "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed",
        }) root.Resources[key] = accent;
        foreach (var key in new[]
        {
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed",
            "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
            "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
        }) root.Resources[key] = dark;
        root.Resources["HyperlinkButtonForeground"] = Brush(Warm);
        root.Resources["HyperlinkButtonForegroundPointerOver"] = Brush(Hex("#F6C88A"));
        DarkTitleBar(window);
    }

    /// <summary>Заголовок окна — в тон тёмному окну.</summary>
    public static void DarkTitleBar(Microsoft.UI.Windowing.AppWindow window)
    {
        try
        {
            var bar = window.TitleBar;
            bar.BackgroundColor = bar.InactiveBackgroundColor = Hex("#1B1E28");
            bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Hex("#1B1E28");
            bar.ForegroundColor = bar.ButtonForegroundColor = Hex("#C9C5BE");
            bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = Hex("#6E727C");
            bar.ButtonHoverBackgroundColor = Hex("#2A2D38");
            bar.ButtonHoverForegroundColor = Hex("#F6F2EA");
            bar.ButtonPressedBackgroundColor = Hex("#343846");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        var v = Convert.ToUInt32(hex, 16);
        return hex.Length == 8
            ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static SolidColorBrush Brush(Color c) => new(c);
    public static SolidColorBrush Brush(string hex) => new(Hex(hex));

    public static TextBlock Text(string text, double size, Color color, FontWeight? weight = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    /// <summary>Стеклянная карточка: лёгкий светлый градиент и подсвеченная кромка.</summary>
    public static Border CardBorder(UIElement child, double radius = 16, double padding = 18) => new()
    {
        Background = Vertical(Color.FromArgb(20, 255, 255, 255), Color.FromArgb(6, 255, 255, 255)),
        CornerRadius = new CornerRadius(radius),
        Padding = new Thickness(padding),
        BorderBrush = Vertical(Color.FromArgb(41, 255, 255, 255), Color.FromArgb(10, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Child = child,
    };

    /// <summary>Главная тёплая кнопка (как «Перейти в отдых»).</summary>
    public static Button WarmButton(string text, double size = 15)
    {
        var fill = Vertical(Hex("#F7CA8C"), Hex("#E8A05B"));
        var b = new Button
        {
            Content = Text(text, size, AccentText, FontWeights.SemiBold), Padding = new Thickness(28, 11, 28, 11), CornerRadius = new CornerRadius(12),
            Background = fill, BorderBrush = Brush(Color.FromArgb(153, 120, 70, 30)), BorderThickness = new Thickness(1),
        };
        b.Resources["ButtonBackground"] = fill;
        b.Resources["ButtonBackgroundPointerOver"] = Vertical(Hex("#F9D39C"), Hex("#EDAA68"));
        b.Resources["ButtonBackgroundPressed"] = Vertical(Hex("#EDB878"), Hex("#DC944F"));
        b.Resources["ButtonBackgroundDisabled"] = Vertical(Color.FromArgb(20, 255, 255, 255), Color.FromArgb(8, 255, 255, 255));
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Color.FromArgb(153, 120, 70, 30));
        b.Resources["ButtonBorderBrushPressed"] = Brush(Color.FromArgb(153, 120, 70, 30));
        return b;
    }

    /// <summary>Кнопка со своими цветами во всех состояниях (лёгкая стилизация через ресурсы).</summary>
    public static Button ColoredButton(object content, Color background, Color hover, Color foreground)
    {
        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(28, 18, 28, 18),
            CornerRadius = new CornerRadius(16),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Background = Brush(background),
            Foreground = Brush(foreground),
            BorderThickness = new Thickness(0),
        };
        b.Resources["ButtonBackground"] = Brush(background);
        b.Resources["ButtonBackgroundPointerOver"] = Brush(hover);
        b.Resources["ButtonBackgroundPressed"] = Brush(hover);
        b.Resources["ButtonBackgroundDisabled"] = Brush("#C9CCD8");
        b.Resources["ButtonForeground"] = Brush(foreground);
        b.Resources["ButtonForegroundPointerOver"] = Brush(foreground);
        b.Resources["ButtonForegroundPressed"] = Brush(foreground);
        b.Resources["ButtonForegroundDisabled"] = Brush(Colors.White);
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        return b;
    }

    /// <summary>Прозрачная кнопка-строка списка.</summary>
    public static Button RowButton(UIElement content)
    {
        var b = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(14),
            Background = Brush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        b.Resources["ButtonBackground"] = Brush(Colors.Transparent);
        b.Resources["ButtonBackgroundPointerOver"] = Brush(Hover);
        b.Resources["ButtonBackgroundPressed"] = Brush(Hex("#26FFFFFF"));
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        return b;
    }

    private static readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> Icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Квадратик программы: сразу — буква на её цвете, а когда Windows отдаст настоящий значок exe — значок.
    /// </summary>
    public static Border AppIcon(string name, string id, string? exePath, double size)
    {
        var letter = Text(name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?", size * 0.45, Colors.White, FontWeights.SemiBold);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4.5), Background = Brush(ProgramColor(id)), Child = letter };
        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) _ = LoadIconAsync(box, exePath, size);
        return box;
    }

    internal static async Task LoadIconAsync(Border box, string path, double size)
    {
        try
        {
            if (!Icons.TryGetValue(path, out var bmp))
            {
                var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using var thumb = await file.GetThumbnailAsync(global::Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 64);
                if (thumb is not null)
                {
                    bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    await bmp.SetSourceAsync(thumb);
                }
                Icons[path] = bmp;
            }
            if (bmp is null) return;
            box.Background = Brush(Colors.Transparent);
            box.Child = new Image { Source = bmp, Width = size, Height = size };
        }
        catch (Exception ex)
        {
            GigaRest.Core.Logging.Log.Warn("Значок программы: " + ex.Message);
        }
    }

    /// <summary>Цвет квадратика программы: свой для известных, для остальных — из палитры по имени.</summary>
    public static Color ProgramColor(string id)
    {
        var known = id.ToLowerInvariant() switch
        {
            "bitrix24.exe" => "#2FC6F6",
            "outlook.exe" or "olk.exe" => "#0F6CBD",
            "chrome.exe" => "#1A73E8",
            "telegram.exe" => "#27A7E7",
            "zen.exe" => "#E8A33D",
            "claude.exe" => "#D97757",
            "яндекс музыка.exe" => "#7A5AF8",
            "yandexdisk2.exe" => "#FC3F1D",
            "steam.exe" => "#1B2838",
            "battle.net.exe" => "#148EFF",
            _ => null,
        };
        if (known is not null) return Hex(known);
        string[] palette = ["#6C5CE7", "#00A3A3", "#E8B41A", "#E8590C", "#2B8A3E", "#C2255C", "#5C7CFA", "#495057"];
        var hash = 0;
        foreach (var ch in id) hash = hash * 31 + ch;
        return Hex(palette[Math.Abs(hash % palette.Length)]);
    }
}
