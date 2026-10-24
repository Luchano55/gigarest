using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace GigaRest.App;

/// <summary>Цвета и заготовки элементов по макету «Переключатель».</summary>
internal static class Ui
{
    public static readonly Color PageBg = Hex("#F7F7FA");
    public static readonly Color Ink = Hex("#16171D");
    public static readonly Color Muted = Hex("#6A6D7C");
    public static readonly Color Lead = Hex("#4A4D5C");
    public static readonly Color Card = Hex("#FFFFFF");
    public static readonly Color Accent = Hex("#3B5BDB");
    public static readonly Color AccentDark = Hex("#2F4BC0");
    public static readonly Color Hover = Hex("#F1F2F6");

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

    public static Border CardBorder(UIElement child, double radius = 16, double padding = 18) => new()
    {
        Background = Brush(Card),
        CornerRadius = new CornerRadius(radius),
        Padding = new Thickness(padding),
        BorderBrush = Brush("#E6E7EE"),
        BorderThickness = new Thickness(1),
        Child = child,
    };

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
        b.Resources["ButtonBackgroundPressed"] = Brush(Hex("#E6E8EF"));
        b.Resources["ButtonBorderBrushPointerOver"] = Brush(Colors.Transparent);
        b.Resources["ButtonBorderBrushPressed"] = Brush(Colors.Transparent);
        return b;
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
