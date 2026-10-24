using System.Globalization;

namespace GigaRest.Core.Settings;

/// <summary>
/// Расписание «дни недели + время» (решение D-SCHEDULE). Им заданы и возврат к работе,
/// и мягкое вечернее предложение отдыха. Ничего не переключает само — только предлагает.
/// </summary>
public sealed class DaySchedule
{
    public bool Enabled { get; set; }
    public List<DayOfWeek> Days { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    /// <summary>«09:00».</summary>
    public string Time { get; set; } = "09:00";

    public TimeSpan TimeOfDay =>
        TimeSpan.TryParseExact(Time, @"hh\:mm", CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
            ? t : new TimeSpan(9, 0, 0);

    /// <summary>Ближайшее срабатывание строго после <paramref name="after"/> (местное время). null — выключено.</summary>
    public DateTime? NextAfter(DateTime after)
    {
        if (!Enabled || Days.Count == 0) return null;
        for (var d = 0; d <= 7; d++)
        {
            var day = after.Date.AddDays(d);
            if (!Days.Contains(day.DayOfWeek)) continue;
            var at = day + TimeOfDay;
            if (at > after) return at;
        }
        return null;
    }

    /// <summary>
    /// Пора ли предложить возврат: срабатывание попало в промежуток (previousCheck; now].
    /// Если компьютер спал и проснулся позже — тоже пора (предложение после пробуждения),
    /// но не позже чем через 12 часов: вчерашнее утро сегодня вечером не предлагаем.
    /// </summary>
    public bool IsDue(DateTime previousCheck, DateTime now)
    {
        var next = NextAfter(previousCheck);
        return next is not null && next <= now && now - next < TimeSpan.FromHours(12);
    }

    /// <summary>«по будням в 09:00», «пн, ср в 08:30», «каждый день в 10:00».</summary>
    public string Describe()
    {
        if (!Enabled || Days.Count == 0) return "выключено";
        var set = Days.ToHashSet();
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        string days;
        if (set.Count == 7) days = "каждый день";
        else if (set.SetEquals(weekdays)) days = "по будням";
        else if (set.SetEquals([DayOfWeek.Saturday, DayOfWeek.Sunday])) days = "по выходным";
        else days = string.Join(", ", Order.Where(set.Contains).Select(Short));
        return $"{days} в {TimeOfDay:hh\\:mm}";
    }

    public static readonly DayOfWeek[] Order =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static string Short(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "пн", DayOfWeek.Tuesday => "вт", DayOfWeek.Wednesday => "ср", DayOfWeek.Thursday => "чт",
        DayOfWeek.Friday => "пт", DayOfWeek.Saturday => "сб", _ => "вс",
    };
}
