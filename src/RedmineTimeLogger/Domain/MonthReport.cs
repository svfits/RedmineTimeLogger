using RedmineTimeLogger.Redmine;

namespace RedmineTimeLogger.Domain;

public sealed record DayLine(DateOnly Date, decimal Hours, bool IsWeekend, bool IsHoliday, bool IsVacation)
{
    public bool IsWorkday => !IsWeekend && !IsHoliday && !IsVacation;
}

/// <summary>
/// Итоги месяца: норма (будни × часов в день за вычетом отпуска и праздников),
/// списано, недостача, незаполненные рабочие дни.
/// </summary>
public sealed class MonthReport
{
    private static readonly string[] MonthNames =
    {
        "январь", "февраль", "март", "апрель", "май", "июнь",
        "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь",
    };

    public required DateOnly MonthStart { get; init; }
    public required DateOnly Anchor { get; init; }
    public required decimal HoursPerDay { get; init; }
    public required IReadOnlyList<DayLine> Days { get; init; }
    public required IReadOnlyList<TimeEntry> Entries { get; init; }

    public decimal Logged => Entries.Sum(e => e.Hours);

    /// <summary>Все будни месяца до опорной даты включительно (включая отпуск).</summary>
    public int WeekdayCount => Days.Count(d => !d.IsWeekend);

    /// <summary>Будни, попавшие в отпуск: за них норма не начисляется.</summary>
    public int VacationDayCount => Days.Count(d => !d.IsWeekend && d.IsVacation);

    /// <summary>Будни без отпуска — база нормы «только будни».</summary>
    public int NormWeekdayCount => WeekdayCount - VacationDayCount;

    /// <summary>Рабочие дни: будни минус праздники и отпуск.</summary>
    public int WorkdayCount => Days.Count(d => d.IsWorkday);

    /// <summary>Праздники в будни, не попавшие в отпуск (иначе они уже не в норме).</summary>
    public int HolidayCountOnWeekdays => Days.Count(d => d.IsHoliday && !d.IsWeekend && !d.IsVacation);

    public decimal NormByWeekdays => NormWeekdayCount * HoursPerDay;

    public decimal NormByHolidays => WorkdayCount * HoursPerDay;

    /// <summary>Недостача к норме «только будни».</summary>
    public decimal GapByWeekdays => NormByWeekdays - Logged;

    /// <summary>Недостача к норме с учётом праздников.</summary>
    public decimal GapByHolidays => NormByHolidays - Logged;

    public IReadOnlyList<DateOnly> MissingWorkdays =>
        Days.Where(d => d.IsWorkday && d.Hours == 0).Select(d => d.Date).ToList();

    /// <summary>Последний незаполненный рабочий день — предлагается как дата списания по умолчанию.</summary>
    public DateOnly? LastMissingWorkday => MissingWorkdays.Count == 0 ? null : MissingWorkdays[^1];

    /// <summary>Непрерывные отрезки отпуска, попавшие в отчётный период.</summary>
    public IReadOnlyList<(DateOnly From, DateOnly To)> VacationRuns => BuildRuns();

    public string Label => $"{MonthNames[MonthStart.Month - 1]} {MonthStart.Year}";

    public static MonthReport Build(
        DateOnly monthStart,
        DateOnly today,
        decimal hoursPerDay,
        IReadOnlyList<TimeEntry> entries,
        WorkCalendar calendar)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var anchor = today < monthStart ? monthStart : (today > monthEnd ? monthEnd : today);

        var byDay = entries
            .GroupBy(e => e.SpentOn)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));

        var days = new List<DayLine>();
        for (var date = monthStart; date <= anchor; date = date.AddDays(1))
        {
            days.Add(new DayLine(
                date,
                byDay.GetValueOrDefault(date),
                calendar.IsWeekend(date),
                calendar.IsHoliday(date),
                calendar.IsVacation(date)));
        }

        return new MonthReport
        {
            MonthStart = monthStart,
            Anchor = anchor,
            HoursPerDay = hoursPerDay,
            Days = days,
            Entries = entries.OrderBy(e => e.SpentOn).ToList(),
        };
    }

    private List<(DateOnly From, DateOnly To)> BuildRuns()
    {
        var runs = new List<(DateOnly From, DateOnly To)>();
        DateOnly? from = null;
        DateOnly? to = null;

        foreach (var day in Days)
        {
            if (!day.IsVacation)
            {
                if (from is not null && to is not null) runs.Add((from.Value, to.Value));
                from = null;
                to = null;
                continue;
            }

            from ??= day.Date;
            to = day.Date;
        }

        if (from is not null && to is not null) runs.Add((from.Value, to.Value));
        return runs;
    }
}
