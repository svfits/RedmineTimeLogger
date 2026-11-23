using System.Globalization;

namespace RedmineTimeLogger.Domain;

public sealed class VacationFormatException : Exception
{
    public VacationFormatException(string message) : base(message) { }
}

/// <summary>
/// Период отпуска: дни внутри него не учитываются в норме рабочего времени.
/// Хранится и вводится как «2026-11-02..2026-11-06» или «02.11.2026..06.11.2026»;
/// одиночная дата означает отпуск в один день.
/// </summary>
public sealed record VacationPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;

    /// <summary>Форма хранения: 2026-11-02..2026-11-06.</summary>
    public string ToStorage() => From == To ? Iso(From) : $"{Iso(From)}..{Iso(To)}";

    /// <summary>Форма для человека: 02.11.2026–06.11.2026.</summary>
    public string ToDisplay() => From == To ? Display(From) : $"{Display(From)}–{Display(To)}";

    public override string ToString() => ToStorage();

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Display(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Разбирает один период (возможны «..», «-», «–», «—» между датами).</summary>
    public static VacationPeriod Parse(string text)
    {
        var value = (text ?? "").Trim().Trim('"', '\'', '«', '»');
        if (value.Length == 0) throw new VacationFormatException("Пустой период отпуска.");

        foreach (var separator in new[] { "..", " – ", " — ", " - ", "–", "—" })
        {
            var index = value.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0) continue;

            var from = ParseDate(value[..index]);
            var to = ParseDate(value[(index + separator.Length)..]);
            return from <= to ? new VacationPeriod(from, to) : new VacationPeriod(to, from);
        }

        var single = ParseDate(value);
        return new VacationPeriod(single, single);
    }

    /// <summary>Разбирает список периодов, разделённых запятыми или точкой с запятой.</summary>
    public static List<VacationPeriod> ParseMany(string text)
    {
        var result = new List<VacationPeriod>();
        foreach (var part in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) continue;
            result.Add(Parse(part));
        }

        if (result.Count == 0) throw new VacationFormatException($"Не удалось разобрать периоды отпуска «{text}».");
        return result;
    }

    private static DateOnly ParseDate(string raw)
    {
        var value = raw.Trim();
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yy", "d.M.yyyy", "d.M.yy" };
        if (!DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new VacationFormatException($"Не удалось разобрать дату «{value}». Ожидается 2026-11-02 или 02.11.2026.");

        return date;
    }
}
