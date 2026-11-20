using System.Globalization;
using RedmineTimeLogger.Cli;
using RedmineTimeLogger.Domain;

namespace RedmineTimeLogger.Config;

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}

/// <summary>
/// Конфигурация приложения. Секретов в коде нет: API-ключ Redmine
/// приходит извне — переменная окружения или файл, путь к которому
/// тоже задаётся извне.
/// </summary>
public sealed class AppConfig
{
    public const string DefaultUrl = "https://rm.burworld.ru";
    public const string KeyEnvVar = "REDMINE_API_KEY";
    public const string KeyFileEnvVar = "REDMINE_API_KEY_FILE";
    public const string HoursEnvVar = "REDMINE_HOURS_PER_DAY";
    public const string HolidaysEnvVar = "REDMINE_HOLIDAYS";
    public const string ActivityEnvVar = "REDMINE_DEFAULT_ACTIVITY";
    public const string InsecureEnvVar = "REDMINE_IGNORE_CERT_ERRORS";

    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    /// <summary>Откуда взят ключ — для диагностики (сам ключ не печатается).</summary>
    public required string ApiKeySource { get; init; }
    public required decimal HoursPerDay { get; init; }
    public required WorkCalendar Calendar { get; init; }
    public required string DefaultActivityName { get; init; }
    public bool IgnoreCertificateErrors { get; init; }

    public static AppConfig Load(CommandLine cli)
    {
        var url = FirstNonEmpty(cli.Url, Environment.GetEnvironmentVariable("REDMINE_URL"), DefaultUrl)!;
        var (key, source) = ResolveApiKey();

        var hours = 8m;
        var hoursRaw = Environment.GetEnvironmentVariable(HoursEnvVar);
        if (!string.IsNullOrWhiteSpace(hoursRaw))
        {
            var parsed = CommandLine.ParseHours(hoursRaw, HoursEnvVar);
            if (parsed is > 0 and <= 24) hours = parsed;
        }

        var holidays = BuildHolidays(cli.Holidays);
        var insecureRaw = Environment.GetEnvironmentVariable(InsecureEnvVar);
        var insecure = cli.Insecure || IsTruthy(insecureRaw);

        return new AppConfig
        {
            BaseUrl = url,
            ApiKey = key,
            ApiKeySource = source,
            HoursPerDay = hours,
            Calendar = new WorkCalendar(holidays),
            DefaultActivityName = FirstNonEmpty(Environment.GetEnvironmentVariable(ActivityEnvVar), "Разработка")!,
            IgnoreCertificateErrors = insecure,
        };
    }

    private static (string Key, string Source) ResolveApiKey()
    {
        var direct = Environment.GetEnvironmentVariable(KeyEnvVar)?.Trim();
        if (!string.IsNullOrWhiteSpace(direct))
            return (Clean(direct), $"переменной окружения {KeyEnvVar}");

        var filePath = Environment.GetEnvironmentVariable(KeyFileEnvVar)?.Trim();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            // Удобство для этого рабочего места: ключ, которым пользуется DSH.
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "redmine-api-key");
            if (File.Exists(fallback)) filePath = fallback;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (!File.Exists(filePath))
                throw new ConfigException($"Файл с ключом не найден: {filePath}");

            var text = File.ReadAllText(filePath).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ConfigException($"Файл с ключом пуст: {filePath}");

            return (Clean(text.Split('\n')[0]), $"файла {filePath}");
        }

        throw new ConfigException(
            "Не задан API-ключ Redmine. Способы передать его извне:\n" +
            $"  • переменная окружения {KeyEnvVar} — сам ключ;\n" +
            $"  • переменная окружения {KeyFileEnvVar} — путь к файлу с ключом;\n" +
            $"  • файл по умолчанию {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "redmine-api-key")}.\n" +
            "Ключ: Redmine → Моя учётная запись → API access key.");
    }

    private static string Clean(string raw)
    {
        var value = raw.Trim().Trim('"', '\'');
        if (!value.All(c => char.IsLetterOrDigit(c)))
            throw new ConfigException("API-ключ содержит недопустимые символы (ожидается hex-строка).");
        return value;
    }

    private static IEnumerable<DateOnly> BuildHolidays(string? cliHolidays)
    {
        var year = DateTime.Today.Year;
        var set = new HashSet<DateOnly>(WorkCalendar.FederalHolidays(year));
        foreach (var yearOffset in new[] { -1, 1 })
            set.UnionWith(WorkCalendar.FederalHolidays(year + yearOffset));

        foreach (var source in new[] { Environment.GetEnvironmentVariable(HolidaysEnvVar), cliHolidays })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            foreach (var part in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (DateOnly.TryParseExact(part, new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    set.Add(date);
            }
        }

        return set;
    }

    private static bool IsTruthy(string? value) =>
        value is not null && (value.Equals("1") || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
