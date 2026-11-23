using System.Text.Json;
using RedmineTimeLogger.Domain;

namespace RedmineTimeLogger.Config;

/// <summary>
/// Хранилище отпусков. Отпуск — это дни, которые не входят в норму рабочего
/// времени: за них нечего списывать, и «незаполненными» они не считаются.
///
/// Периоды хранятся в JSON-файле (по умолчанию
/// %APPDATA%\RedmineTimeLogger\vacations.json) и задаются извне:
///   • --vacation 02.11.2026..06.11.2026  (сохраняется в файл)
///   • REDMINE_VACATIONS=02.11.2026..06.11.2026        (разово, не сохраняется)
///   • REDMINE_VACATIONS_FILE=путь\к\vacations.json    (другой файл)
/// </summary>
public sealed class VacationStore
{
    public const string EnvVar = "REDMINE_VACATIONS";
    public const string FileEnvVar = "REDMINE_VACATIONS_FILE";

    private static readonly JsonSerializerOptions FileJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly List<VacationPeriod> _periods;

    public string FilePath { get; }

    public IReadOnlyList<VacationPeriod> Periods => _periods;

    private VacationStore(string filePath, List<VacationPeriod> periods)
    {
        FilePath = filePath;
        _periods = periods;
    }

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RedmineTimeLogger",
            "vacations.json");

    public static VacationStore Load()
    {
        var path = Environment.GetEnvironmentVariable(FileEnvVar)?.Trim();
        if (string.IsNullOrWhiteSpace(path)) path = DefaultFilePath;

        if (!File.Exists(path)) return new VacationStore(path, new List<VacationPeriod>());

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Не удалось прочитать файл отпусков {path}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(json)) return new VacationStore(path, new List<VacationPeriod>());

        FileModel? model;
        try
        {
            model = JsonSerializer.Deserialize<FileModel>(json, FileJson);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Файл отпусков {path} повреждён: {ex.Message}");
        }

        var periods = new List<VacationPeriod>();
        foreach (var raw in model?.Vacations ?? new List<string>())
        {
            try
            {
                periods.Add(VacationPeriod.Parse(raw));
            }
            catch (VacationFormatException ex)
            {
                throw new ConfigException($"Файл отпусков {path}: {ex.Message}");
            }
        }

        return new VacationStore(path, Merge(periods));
    }

    /// <summary>Добавляет периоды; возвращает, сколько действительно новых дней.</summary>
    public int Add(IEnumerable<VacationPeriod> periods)
    {
        var existing = _periods.ToList();
        var before = ExpandDays(existing).Count();
        var merged = Merge(existing.Concat(periods).ToList());

        _periods.Clear();
        _periods.AddRange(merged);

        return ExpandDays(merged).Count() - before;
    }

    public void Clear() => _periods.Clear();

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var model = new FileModel { Vacations = _periods.Select(p => p.ToStorage()).ToList() };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(model, FileJson) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Не удалось сохранить отпуск в {FilePath}: {ex.Message}");
        }
    }

    /// <summary>Дни отпуска для календаря: сохранённые + разовые извне + из командной строки.</summary>
    public static List<VacationPeriod> Combine(IEnumerable<VacationPeriod> saved, params IEnumerable<VacationPeriod>[] extra) =>
        Merge(saved.Concat(extra.SelectMany(e => e)).ToList());

    public static IEnumerable<DateOnly> ExpandDays(IEnumerable<VacationPeriod> periods)
    {
        foreach (var period in periods)
            for (var date = period.From; date <= period.To; date = date.AddDays(1))
                yield return date;
    }

    /// <summary>Склеивает пересекающиеся и соседние периоды, сортирует по дате.</summary>
    public static List<VacationPeriod> Merge(IEnumerable<VacationPeriod> periods)
    {
        var ordered = periods.OrderBy(p => p.From).ThenBy(p => p.To).ToList();
        var result = new List<VacationPeriod>();

        foreach (var period in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(period);
                continue;
            }

            var last = result[^1];
            if (period.From <= last.To.AddDays(1))
            {
                var to = period.To > last.To ? period.To : last.To;
                result[^1] = new VacationPeriod(last.From, to);
            }
            else
            {
                result.Add(period);
            }
        }

        return result;
    }

    private sealed class FileModel
    {
        public List<string>? Vacations { get; set; }
    }
}
