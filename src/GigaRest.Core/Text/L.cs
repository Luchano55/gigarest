using System.Globalization;

namespace GigaRest.Core.Text;

/// <summary>
/// Язык интерфейса: русский или английский. Строки пишутся парой прямо в коде: <c>L.T("Отдых", "Rest")</c>.
/// Выбирается один раз при запуске (настройка «Язык»: авто, русский, English).
/// </summary>
public static class L
{
    public const string Auto = "auto", Russian = "ru", English = "en";

    /// <summary>Сейчас английский.</summary>
    public static bool En { get; private set; }

    public static CultureInfo Culture => En ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Настройка → язык. «auto» — как у Windows: русский для русского, украинского, белорусского и казахского, иначе английский.</summary>
    public static void Use(string? setting, CultureInfo? system = null)
    {
        En = Resolve(setting, system ?? CultureInfo.CurrentUICulture) == English;
    }

    public static string Resolve(string? setting, CultureInfo system) => Normalize(setting) switch
    {
        Russian => Russian,
        English => English,
        _ => system.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? Russian : English,
    };

    public static string Normalize(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        Russian => Russian,
        English => English,
        _ => Auto,
    };

    public static string T(string ru, string en) => En ? en : ru;

    /// <summary>«3 игры» / «3 games»: русские три формы и английские две.</summary>
    public static string Plural(int n, string one, string few, string many, string enOne, string enMany) =>
        En ? $"{n} {(n == 1 ? enOne : enMany)}" : Sizes.Plural(n, one, few, many);
}
