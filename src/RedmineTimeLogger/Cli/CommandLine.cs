using System.Globalization;
using RedmineTimeLogger.Domain;

namespace RedmineTimeLogger.Cli;

public sealed class CommandLineException : Exception
{
    public CommandLineException(string message) : base(message) { }
}

/// <summary>
/// Разбор аргументов командной строки. Всё необязательное:
/// без аргументов приложение работает в интерактивном режиме.
/// </summary>
public sealed class CommandLine
{
    public bool ShowHelp { get; private set; }
    public bool Check { get; private set; }
    public bool ReportOnly { get; private set; }
    public bool DryRun { get; private set; }
    public bool SkipConfirmation { get; private set; }
    public bool Insecure { get; private set; }

    public string? Url { get; private set; }
    public string? Issue { get; private set; }
    public string? Hours { get; private set; }
    public string? Date { get; private set; }
    public string? Activity { get; private set; }
    public string? Comment { get; private set; }
    public string? Month { get; private set; }
    public string? Holidays { get; private set; }

    /// <summary>Периоды отпуска, добавленные в этом запуске (сохраняются в файл).</summary>
    public List<string> Vacations { get; } = new();

    public bool ShowVacations { get; private set; }
    public bool ClearVacations { get; private set; }

    public bool HasIssue => !string.IsNullOrWhiteSpace(Issue);

    /// <summary>Задан хотя бы один период отпуска.</summary>
    public bool HasVacation => Vacations.Count > 0;

    public List<VacationPeriod> ParseVacations()
    {
        var result = new List<VacationPeriod>();
        foreach (var value in Vacations)
        {
            try
            {
                result.AddRange(VacationPeriod.ParseMany(value));
            }
            catch (VacationFormatException ex)
            {
                throw new CommandLineException($"--vacation: {ex.Message}");
            }
        }

        return result;
    }

    public static CommandLine Parse(string[] args)
    {
        var result = new CommandLine();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next(string name)
            {
                if (i + 1 >= args.Length) throw new CommandLineException($"Для {name} нужно значение.");
                return args[++i];
            }

            switch (arg)
            {
                case "-h" or "--help" or "/?": result.ShowHelp = true; break;
                case "--check": result.Check = true; break;
                case "--report": result.ReportOnly = true; break;
                case "--dry-run": result.DryRun = true; break;
                case "-y" or "--yes": result.SkipConfirmation = true; break;
                case "--insecure": result.Insecure = true; break;
                case "--url": result.Url = Next("--url"); break;
                case "--issue": result.Issue = Next("--issue"); break;
                case "--hours": result.Hours = Next("--hours"); break;
                case "--date": result.Date = Next("--date"); break;
                case "--activity": result.Activity = Next("--activity"); break;
                case "--comment": result.Comment = Next("--comment"); break;
                case "--month": result.Month = Next("--month"); break;
                case "--holidays": result.Holidays = Next("--holidays"); break;
                case "--vacation": result.Vacations.Add(Next("--vacation")); break;
                case "--vacations": result.ShowVacations = true; break;
                case "--vacation-clear" or "--vacations-clear": result.ClearVacations = true; break;
                default:
                    throw new CommandLineException($"Неизвестный аргумент «{arg}». Справка: --help");
            }
        }

        return result;
    }

    public static DateOnly ParseDate(string value, string optionName)
    {
        if (!DateOnly.TryParseExact(value, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new CommandLineException($"{optionName}: не удалось разобрать дату «{value}». Ожидается ГГГГ-ММ-ДД.");
        }

        return date;
    }

    public static decimal ParseHours(string value, string optionName)
    {
        var normalized = value.Replace(',', '.').Trim();
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours))
            throw new CommandLineException($"{optionName}: не удалось разобрать число «{value}».");
        return hours;
    }

    public static DateOnly ParseMonth(string value)
    {
        if (!DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new CommandLineException($"--month: ожидается ГГГГ-ММ, получено «{value}».");
        return date;
    }
}
