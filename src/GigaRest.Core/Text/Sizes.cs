using System.Globalization;

namespace GigaRest.Core.Text;

public static class Sizes
{
    /// <summary>1288490188 → «1,2 ГБ», 671088640 → «640 МБ».</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0) bytes = 0;
        const double mb = 1024 * 1024, gb = mb * 1024;
        if (bytes >= gb)
            return (bytes / gb).ToString("0.0", L.Culture) + L.T(" ГБ", " GB");
        return Math.Round(bytes / mb).ToString("0", L.Culture) + L.T(" МБ", " MB");
    }

    /// <summary>«1 программа», «3 программы», «5 программ».</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14 ? many : mod10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
        return $"{n} {word}";
    }
}
