namespace GigaRest.Core.Scenes;

/// <summary>Что делать «Тихому» экрану при простое (ТЗ, раздел 5, защита OLED).</summary>
public sealed record QuietState(bool HideCursor, bool CloseList, bool DimWords, bool DimScene, int? ScreenOffInSeconds, bool ScreenOffNow);

/// <summary>
/// Таймеры простоя «Тихого» экрана: 20 с — список закрывается, строка приглушается, курсор прячется;
/// N минут — сцена темнеет; M минут — «Экран выключится через 60 с», потом выключается. M = 0 — не выключать.
/// </summary>
public sealed class QuietTimers
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(60);

    public TimeSpan DimAfter { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan? ScreenOffAfter { get; init; } = TimeSpan.FromMinutes(60);

    public static QuietTimers From(Settings.AppSettings s) => new()
    {
        DimAfter = TimeSpan.FromMinutes(Math.Clamp(s.DimMinutes, 1, 240)),
        ScreenOffAfter = s.ScreenOffMinutes > 0 ? TimeSpan.FromMinutes(Math.Clamp(s.ScreenOffMinutes, 1, 600)) : null,
    };

    public QuietState At(TimeSpan idle)
    {
        var quiet = idle >= Quiet;
        int? countdown = null;
        var off = false;
        if (ScreenOffAfter is { } after && idle >= after)
        {
            var left = Countdown - (idle - after);
            if (left > TimeSpan.Zero) countdown = (int)Math.Ceiling(left.TotalSeconds);
            else off = true;
        }
        return new QuietState(quiet, quiet, quiet, idle >= DimAfter, countdown, off);
    }
}
