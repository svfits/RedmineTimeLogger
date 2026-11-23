namespace RedmineTimeLogger.Domain;

/// <summary>
/// Производственный календарь: выходные — суббота/воскресенье, праздники — из набора,
/// отпуск — периоды, которые не входят в норму рабочего времени.
/// Переносы выходных по постановлениям правительства здесь не учитываются;
/// их можно передать явно (см. AppConfig.Holidays).
/// </summary>
public sealed class WorkCalendar
{
    private readonly HashSet<DateOnly> _holidays;
    private readonly HashSet<DateOnly> _vacationDays;
    private readonly List<VacationPeriod> _vacations;

    public WorkCalendar(IEnumerable<DateOnly> holidays, IEnumerable<VacationPeriod>? vacations = null)
    {
        _holidays = holidays.Distinct().ToHashSet();
        _vacations = (vacations ?? Enumerable.Empty<VacationPeriod>())
            .OrderBy(p => p.From)
            .ToList();
        _vacationDays = _vacations
            .SelectMany(p => Enumerable.Range(0, p.Days).Select(i => p.From.AddDays(i)))
            .ToHashSet();
    }

    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    public IReadOnlyList<VacationPeriod> Vacations => _vacations;

    public bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <summary>День отпуска: норма за него не начисляется.</summary>
    public bool IsVacation(DateOnly date) => _vacationDays.Contains(date);

    public bool IsWorkday(DateOnly date) => !IsWeekend(date) && !IsHoliday(date) && !IsVacation(date);

    /// <summary>Государственные праздники РФ с фиксированной датой (без переносов).</summary>
    public static IEnumerable<DateOnly> FederalHolidays(int year)
    {
        for (var day = 1; day <= 8; day++) yield return new DateOnly(year, 1, day); // новогодние + Рождество
        yield return new DateOnly(year, 2, 23);  // День защитника Отечества
        yield return new DateOnly(year, 3, 8);   // 8 Марта
        yield return new DateOnly(year, 5, 1);   // Праздник Весны и Труда
        yield return new DateOnly(year, 5, 9);   // День Победы
        yield return new DateOnly(year, 6, 12);  // День России
        yield return new DateOnly(year, 11, 4);  // День народного единства
    }
}
