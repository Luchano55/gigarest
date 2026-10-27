using GigaRest.Core.Games;
using GigaRest.Core.Logging;

namespace GigaRest.Core.Stats;

/// <summary>Отрезок отдыха (UTC).</summary>
public sealed class RestPeriod
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
}

/// <summary>Запуск игры из GigaRest.</summary>
public sealed class GameLaunch
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Utc { get; set; }
}

/// <summary>Статистика отдыха: хранится только у пользователя, в %AppData%\GigaRest\stats.json.</summary>
public sealed class RestStats
{
    public List<RestPeriod> Rests { get; set; } = [];
    public List<GameLaunch> Launches { get; set; } = [];

    /// <summary>Сводка: сегодня, за 7 дней, по дням (минуты), частые игры за 30 дней.</summary>
    public sealed record Summary(TimeSpan Today, TimeSpan Week, IReadOnlyList<(DateTime Day, double Minutes)> Days,
        IReadOnlyList<(string Name, int Launches)> TopGames, int Evenings);

    public void AddRest(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromDays(3)) return; // забытый отдых на выходные — не считаем
        Rests.Add(new RestPeriod { StartUtc = startUtc, EndUtc = endUtc });
        Trim(endUtc);
    }

    public void AddLaunch(Game game, DateTime utc)
    {
        Launches.Add(new GameLaunch { Id = game.Id, Name = game.Name, Utc = utc });
        Trim(utc);
    }

    // Храним год — дальше не нужно.
    private void Trim(DateTime nowUtc)
    {
        Rests.RemoveAll(r => r.EndUtc < nowUtc.AddDays(-366));
        Launches.RemoveAll(l => l.Utc < nowUtc.AddDays(-366));
    }

    /// <summary>Сколько отдыха пришлось на местный день day (отрезок через полночь делится по дням).</summary>
    private double MinutesOn(DateTime dayLocal, TimeZoneInfo tz)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dayLocal.Date, DateTimeKind.Unspecified), tz);
        var to = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dayLocal.Date.AddDays(1), DateTimeKind.Unspecified), tz);
        return Rests.Sum(r =>
        {
            var a = r.StartUtc > from ? r.StartUtc : from;
            var b = r.EndUtc < to ? r.EndUtc : to;
            return b > a ? (b - a).TotalMinutes : 0;
        });
    }

    public Summary Summarize(DateTime nowLocal, TimeZoneInfo? tz = null)
    {
        tz ??= TimeZoneInfo.Local;
        var days = Enumerable.Range(0, 7).Select(i => nowLocal.Date.AddDays(i - 6)).Select(d => (d, MinutesOn(d, tz))).ToList();
        var nowUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified), tz);
        var top = Launches.Where(l => l.Utc >= nowUtc.AddDays(-30))
            .GroupBy(l => l.Id).Select(g => (g.Last().Name, g.Count()))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Name).Take(5).ToList();
        var evenings = Rests.Where(r => r.EndUtc >= nowUtc.AddDays(-7)).Select(r => TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc, tz).Date).Distinct().Count();
        return new Summary(TimeSpan.FromMinutes(days[^1].Item2), TimeSpan.FromMinutes(days.Sum(d => d.Item2)), days, top, evenings);
    }

    /// <summary>«2 ч 15 мин», «45 мин», «—».</summary>
    public static string Format(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "—";
        var h = (int)t.TotalHours;
        return h > 0 ? (t.Minutes > 0 ? $"{h} ч {t.Minutes} мин" : $"{h} ч") : $"{t.Minutes} мин";
    }
}

public sealed class StatsStore(string path)
{
    public RestStats Load() => JsonFiles.Read<RestStats>(path, Log.Warn) ?? new RestStats();
    public void Save(RestStats stats) => JsonFiles.Write(path, stats);
}
