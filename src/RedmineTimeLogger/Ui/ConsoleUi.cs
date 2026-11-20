using System.Globalization;
using System.Text;
using RedmineTimeLogger.Domain;
using RedmineTimeLogger.Redmine;

namespace RedmineTimeLogger.Ui;

public static class ConsoleUi
{
    private static readonly string[] MonthNamesGenitive =
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    };

    private static bool Color => !Console.IsOutputRedirected;

    public static void Title(string text)
    {
        Console.WriteLine();
        WriteColored(text, ConsoleColor.Cyan);
        WriteColored(new string('─', Math.Min(text.Length, 78)), ConsoleColor.DarkCyan);
    }

    public static void Info(string text) => Console.WriteLine(text);

    public static void Muted(string text) => WriteColored(text, ConsoleColor.DarkGray);

    public static void Success(string text) => WriteColored(text, ConsoleColor.Green);

    public static void Warn(string text) => WriteColored(text, ConsoleColor.Yellow);

    public static void Error(string text) => WriteColored(text, ConsoleColor.Red);

    public static void KeyValue(string key, string value)
    {
        Console.Write($"  {key}: ");
        Console.WriteLine(value);
    }

    // ------------------------------------------------------------------ report

    public static void PrintReport(MonthReport report)
    {
        Title($"Трудозатраты: {report.Label} (по {FormatDate(report.Anchor)})");

        KeyValue("Будни месяца", $"{report.WeekdayCount} дн. × {Fmt(report.HoursPerDay)} ч = {Fmt(report.NormByWeekdays)} ч");
        if (report.HolidayCountOnWeekdays > 0)
            KeyValue("Праздники в будни", $"{report.HolidayCountOnWeekdays} дн. → норма с праздниками {report.WorkdayCount} дн. × {Fmt(report.HoursPerDay)} ч = {Fmt(report.NormByHolidays)} ч");
        KeyValue("Списано", $"{Fmt(report.Logged)} ч");

        var gapWeekdays = report.GapByWeekdays;
        var gapHolidays = report.GapByHolidays;
        var gapText = $"{Fmt(gapWeekdays)} ч (к норме «только будни»)";
        if (report.HolidayCountOnWeekdays > 0) gapText += $" / {Fmt(gapHolidays)} ч (с учётом праздников)";
        KeyValue("Недостача", gapText);

        if (gapWeekdays < 0) Warn($"  Внимание: списано больше нормы на {Fmt(-gapWeekdays)} ч.");

        var missing = report.MissingWorkdays;
        if (missing.Count == 0)
        {
            Success("  Незаполненных рабочих дней нет.");
        }
        else
        {
            KeyValue($"Незаполненные дни ({missing.Count})", string.Join(", ", missing.Select(FormatDate)));
        }

        Console.WriteLine();
        PrintDays(report);
        Console.WriteLine();
        PrintEntries(report);
    }

    private static void PrintDays(MonthReport report)
    {
        Muted("Календарь: «=» — часы списаны, «·» — рабочий день без записи, «п» — праздник.");

        var byDate = report.Days.ToDictionary(d => d.Date);
        var first = report.Days[0].Date;
        var monday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));

        Console.WriteLine("  " + string.Join("  ", new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }.Select(n => (" " + n).PadRight(8))));

        var builder = new StringBuilder("  ");
        for (var cursor = monday; cursor <= report.Anchor; cursor = cursor.AddDays(1))
        {
            if (cursor.DayOfWeek == DayOfWeek.Monday && cursor != monday)
            {
                Console.WriteLine(builder.ToString().TrimEnd());
                builder.Clear().Append("  ");
            }

            builder.Append(byDate.TryGetValue(cursor, out var day) ? FormatDayCell(day) : "        ").Append("  ");
        }

        if (builder.Length > 2) Console.WriteLine(builder.ToString().TrimEnd());
    }

    private static string FormatDayCell(DayLine day)
    {
        if (day.IsWeekend) return $"{day.Date.Day,2}      ";
        if (day.IsHoliday) return $"{day.Date.Day,2}    п ";
        if (day.Hours > 0) return $"{day.Date.Day,2} {Fmt(day.Hours),3}= ";
        return $"{day.Date.Day,2}    · ";
    }

    private static void PrintEntries(MonthReport report)
    {
        if (report.Entries.Count == 0)
        {
            Muted("Записей о трудозатратах нет.");
            return;
        }

        Muted("Записи месяца:");
        foreach (var entry in report.Entries)
        {
            var issue = entry.Issue is null ? "—" : $"#{entry.Issue.Id}";
            var activity = entry.Activity?.Name ?? "—";
            var comment = string.IsNullOrWhiteSpace(entry.Comments) ? "" : $" — {Truncate(entry.Comments!, 60)}";
            Console.WriteLine($"  {FormatDate(entry.SpentOn)}  {Fmt(entry.Hours),5} ч  {issue,-8} {activity}{comment}");
        }
    }

    // -------------------------------------------------------------- candidates

    public static void PrintCandidates(IReadOnlyList<Issue> issues)
    {
        Title("Задачи в работе (In-Progress / On review / On approve)");

        if (issues.Count == 0)
        {
            Warn("Подходящих задач нет. Можно ввести номер любой задачи вручную.");
            return;
        }

        var header = new[] { "№", "Задача", "Статус", "Списано", "Обновлена", "Тема" };
        var rows = new List<string[]>();
        for (var i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            rows.Add(new[]
            {
                (i + 1).ToString(),
                "#" + issue.Id,
                issue.Status?.Name ?? "—",
                Fmt(issue.SpentHours ?? 0) + " ч",
                issue.UpdatedOn?.ToLocalTime().ToString("dd.MM.yyyy") ?? "—",
                issue.Subject ?? "",
            });
        }

        PrintTable(header, rows, new[] { 3, 8, 12, 8, 11, 58 });
    }

    // ------------------------------------------------------------------ input

    public static string Ask(string prompt, string? defaultValue = null, Func<string, string?>? validate = null)
    {
        while (true)
        {
            Console.WriteLine();
            Console.Write(defaultValue is { Length: > 0 } ? $"{prompt} [{defaultValue}]: " : $"{prompt}: ");
            var input = Console.ReadLine();
            var value = string.IsNullOrWhiteSpace(input) ? defaultValue ?? "" : Normalize(input);

            if (string.IsNullOrWhiteSpace(value))
            {
                Error("Значение обязательно.");
                continue;
            }

            var problem = validate?.Invoke(value);
            if (problem is not null)
            {
                Error(problem);
                continue;
            }

            return value;
        }
    }

    /// <summary>Убирает BOM и невидимые символы: ввод может прийти из файла или пайпа.</summary>
    private static string Normalize(string input) =>
        input.Replace("\uFEFF", "").Replace("\u200B", "").Trim();

    /// <summary>Необязательный ввод: пустая строка допустима.</summary>
    public static string? AskOptional(string prompt, string? defaultValue = null)
    {
        Console.WriteLine();
        Console.Write(defaultValue is { Length: > 0 } ? $"{prompt} [{Truncate(defaultValue, 60)}]: " : $"{prompt}: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return defaultValue;
        return Normalize(input);
    }

    public static bool AskYesNo(string prompt, bool defaultValue = false)
    {
        var hint = defaultValue ? "[Д/н]" : "[д/Н]";
        while (true)
        {
            Console.WriteLine();
            Console.Write($"{prompt} {hint}: ");
            var input = Console.ReadLine();
            var answer = string.IsNullOrWhiteSpace(input) ? "" : Normalize(input).ToLowerInvariant();
            if (answer.Length == 0) return defaultValue;
            if (answer is "д" or "да" or "y" or "yes") return true;
            if (answer is "н" or "нет" or "n" or "no") return false;
            Error("Ответьте «д» или «н».");
        }
    }

    public static decimal AskHours(decimal defaultValue)
    {
        return Ask("Сколько часов списать", Fmt(defaultValue), value =>
        {
            var normalized = value.Replace(',', '.');
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours))
                return "Нужно число, например 8 или 4,5.";
            if (hours <= 0 || hours > 24) return "Часы должны быть больше 0 и не больше 24.";
            return null;
        })
        .Let(v => decimal.Parse(v.Replace(',', '.'), CultureInfo.InvariantCulture));
    }

    public static DateOnly AskDate(DateOnly defaultValue)
    {
        return Ask("Дата списания (ГГГГ-ММ-ДД или ДД.ММ.ГГГГ)", FormatDate(defaultValue), value =>
        {
            if (DateOnly.TryParseExact(value, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return null;
            return "Не удалось разобрать дату.";
        })
        .Let(v => DateOnly.ParseExact(v, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yy" }, CultureInfo.InvariantCulture));
    }

    /// <summary>Выбор задачи: номер из списка или номер задачи Redmine.</summary>
    public static string AskIssueChoice(int candidateCount)
    {
        var hint = candidateCount > 0
            ? $"Выберите задачу: 1–{candidateCount}, или введите номер задачи Redmine (#12345)"
            : "Введите номер задачи Redmine (#12345)";

        return Ask(hint, null, value =>
        {
            if (int.TryParse(value.TrimStart('#'), out var number))
            {
                if (value.TrimStart('#').Length > 0 && number > 0) return null;
                return "Нужен положительный номер.";
            }

            return "Введите номер пункта списка или номер задачи.";
        });
    }

    public static IdName ChooseActivity(IReadOnlyList<IdName> activities, string defaultName)
    {
        var defaultIndex = activities
            .Select((a, index) => (a, index))
            .FirstOrDefault(pair => string.Equals(pair.a.Name, defaultName, StringComparison.OrdinalIgnoreCase))
            .index;

        Console.WriteLine();
        for (var i = 0; i < activities.Count; i++)
        {
            var marker = i == defaultIndex ? " (по умолчанию)" : "";
            Console.WriteLine($"  {i + 1}) {activities[i].Name}{marker}");
        }

        while (true)
        {
            Console.WriteLine();
            Console.Write($"Вид работ [{(defaultIndex < 0 ? 1 : defaultIndex + 1)}]: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return activities[Math.Max(defaultIndex, 0)];
            input = Normalize(input);

            if (int.TryParse(input, out var number) && number >= 1 && number <= activities.Count)
                return activities[number - 1];

            var byName = activities.FirstOrDefault(a => string.Equals(a.Name, input, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;

            Error("Выберите номер из списка или точное название вида работ.");
        }
    }

    // ------------------------------------------------------------------ output

    public static void PrintTable(string[] headers, IReadOnlyList<string[]> rows, int[]? maxWidths = null)
    {
        var widths = new int[headers.Length];
        for (var c = 0; c < headers.Length; c++)
        {
            var max = headers[c].Length;
            foreach (var row in rows)
                if (c < row.Length) max = Math.Max(max, row[c].Length);
            if (maxWidths is not null && c < maxWidths.Length && maxWidths[c] > 0) max = Math.Min(max, maxWidths[c]);
            widths[c] = max;
        }

        string Line(string[] cells) =>
            "  " + string.Join("  ", cells.Select((cell, c) => Pad(Truncate(cell, widths[c]), widths[c])));

        Console.WriteLine(Line(headers));
        Console.WriteLine("  " + string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows) Console.WriteLine(Line(row));
    }

    public static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy");

    public static string FormatDateTime(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string Fmt(decimal value) =>
        value == decimal.Truncate(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string ShortMonth(DateOnly date) => $"{MonthNamesGenitive[date.Month - 1]} {date.Year}";

    private static string Pad(string text, int width) => text.Length >= width ? text : text.PadRight(width);

    public static string Truncate(string text, int width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length <= width) return text;
        return width <= 1 ? text[..width] : text[..(width - 1)] + "…";
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        if (!Color)
        {
            Console.WriteLine(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}

internal static class FunctionalExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> transform) => transform(value);
}
