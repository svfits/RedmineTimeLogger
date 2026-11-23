using System.Globalization;
using System.Text;
using RedmineTimeLogger.Cli;
using RedmineTimeLogger.Config;
using RedmineTimeLogger.Domain;
using RedmineTimeLogger.Redmine;
using RedmineTimeLogger.Ui;

namespace RedmineTimeLogger;

internal static class Program
{
    private static readonly string[] DayNames =
    {
        "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота",
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            return await RunAsync(args);
        }
        catch (CommandLineException ex)
        {
            ConsoleUi.Error(ex.Message);
            return 2;
        }
        catch (VacationFormatException ex)
        {
            ConsoleUi.Error(ex.Message);
            return 2;
        }
        catch (ConfigException ex)
        {
            ConsoleUi.Error(ex.Message);
            return 2;
        }
        catch (RedmineException ex)
        {
            ConsoleUi.Error(ex.Message);
            return 3;
        }
        catch (HttpRequestException ex)
        {
            ConsoleUi.Error($"Сетевая ошибка при обращении к Redmine: {ex.Message}");
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                ConsoleUi.Muted($"  причина: {inner.GetType().Name}: {inner.Message}");
            ConsoleUi.Muted("Проверьте адрес, прокси и сертификат (--insecure отключает проверку TLS).");
            return 4;
        }
        catch (TaskCanceledException)
        {
            ConsoleUi.Error("Redmine не ответил за отведённое время.");
            return 4;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var cli = CommandLine.Parse(args);
        if (cli.ShowHelp)
        {
            Help.Print();
            return 0;
        }

        var vacations = VacationStore.Load();

        if (cli.ClearVacations)
        {
            vacations.Clear();
            vacations.Save();
            ConsoleUi.Success($"Отпуска очищены: {vacations.FilePath}");
            return 0;
        }

        if (cli.ShowVacations)
        {
            PrintVacations(vacations);
            return 0;
        }

        if (cli.HasVacation)
        {
            var added = vacations.Add(cli.ParseVacations());
            vacations.Save();

            ConsoleUi.Success($"Отпуск сохранён: {vacations.FilePath}");
            foreach (var period in vacations.Periods)
                ConsoleUi.Info($"  • {period.ToDisplay()} ({period.Days} дн.)");
            ConsoleUi.Muted(added > 0
                ? $"  Добавлено дней: {added}. Эти дни не входят в норму рабочего времени."
                : "  Новых дней не добавилось — такой отпуск уже был учтён.");

            if (!cli.ReportOnly && !cli.HasIssue && !cli.Check)
            {
                ConsoleUi.Muted("  Запустите без --vacation, чтобы увидеть отчёт с учётом отпуска.");
                return 0;
            }
        }

        var config = AppConfig.Load(cli, vacations);
        using var client = new RedmineClient(config.BaseUrl, config.ApiKey, config.IgnoreCertificateErrors);

        ConsoleUi.Title("Redmine: списание времени");
        var me = await client.GetCurrentUserAsync();
        ConsoleUi.KeyValue("Redmine", config.BaseUrl);
        ConsoleUi.KeyValue("Пользователь", $"{me.DisplayName} ({me.Login}, id {me.Id})");
        ConsoleUi.KeyValue("API-ключ", $"из {config.ApiKeySource}");
        if (config.Vacations.Count > 0)
        {
            var days = VacationStore.ExpandDays(config.Vacations).Count();
            ConsoleUi.KeyValue("Отпуск", $"{days} календ. дн.: {string.Join(", ", config.Vacations.Select(p => p.ToDisplay()))}");
        }

        if (cli.Check)
        {
            var statuses = await client.GetStatusesAsync();
            ConsoleUi.Success($"  Подключение работает. Статусов в Redmine: {statuses.Count}.");
            return 0;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = cli.Month is null ? new DateOnly(today.Year, today.Month, 1) : CommandLine.ParseMonth(cli.Month);
        var monthEnd = month.AddMonths(1).AddDays(-1);

        var entries = await client.GetTimeEntriesAsync(me.Id, month, monthEnd);
        var report = MonthReport.Build(month, today, config.HoursPerDay, entries, config.Calendar);
        ConsoleUi.PrintReport(report);

        if (cli.ReportOnly) return 0;

        var interactive = !cli.HasIssue;
        if (interactive)
        {
            report = AskVacation(report, config, vacations, month, today, entries);
        }

        var issues = await client.GetOpenAssignedIssuesAsync(me.Id);
        var candidates = Candidates.Filter(issues);
        ConsoleUi.PrintCandidates(candidates);

        var issue = await ResolveIssueAsync(client, candidates, cli, interactive);

        var activities = await client.GetActivitiesAsync();
        if (activities.Count == 0) throw new RedmineException("Redmine не вернул список видов работ.");

        var hours = interactive
            ? ConsoleUi.AskHours(cli.Hours is null ? config.HoursPerDay : CommandLine.ParseHours(cli.Hours, "--hours"))
            : (cli.Hours is null ? config.HoursPerDay : CommandLine.ParseHours(cli.Hours, "--hours"));

        var defaultDate = cli.Date is not null
            ? CommandLine.ParseDate(cli.Date, "--date")
            : report.LastMissingWorkday ?? today;

        var date = interactive ? ConsoleUi.AskDate(defaultDate) : defaultDate;

        var activity = ResolveActivity(activities, cli, config, interactive);
        var comment = interactive && cli.Comment is null
            ? ConsoleUi.AskOptional("Комментарий (Enter — без комментария)")
            : cli.Comment;

        PrintPlan(issue, date, hours, activity, comment, config, existingHours: report.Days.FirstOrDefault(d => d.Date == date)?.Hours ?? 0);

        if (!cli.DryRun && !cli.SkipConfirmation)
        {
            var confirmed = ConsoleUi.AskYesNo("Списать время?", false);
            if (!confirmed)
            {
                ConsoleUi.Warn("Отменено. Ничего не списано.");
                return 1;
            }
        }
        else if (cli.DryRun)
        {
            ConsoleUi.Muted("  Режим --dry-run: запрос сформирован, но не отправлен.");
        }

        var request = new TimeEntryCreate
        {
            IssueId = issue.Id,
            SpentOn = date,
            Hours = hours,
            ActivityId = activity.Id,
            Comments = string.IsNullOrWhiteSpace(comment) ? null : comment,
        };

        if (cli.DryRun)
        {
            ConsoleUi.Title("--dry-run: запрос не отправлен");
            Console.WriteLine(RedmineClient.SerializePayload(request));
            return 0;
        }

        var created = await client.CreateTimeEntryAsync(request);
        ConsoleUi.Success($"Записано: #{created.Id} — {ConsoleUi.Fmt(created.Hours)} ч на #{issue.Id} за {ConsoleUi.FormatDate(created.SpentOn)}, вид работ «{created.Activity?.Name ?? activity.Name}».");

        await PrintAfterMathAsync(client, me, month, today, config);

        return 0;
    }

    /// <summary>Сколько несписанных рабочих дней считаем поводом спросить про отпуск.</summary>
    private const int VacationPromptMinMissingDays = 3;

    /// <summary>
    /// Если незаполненных рабочих дней больше трёх, спрашивает про отпуск:
    /// указанные периоды сохраняются и пересчитывают норму.
    /// </summary>
    private static MonthReport AskVacation(
        MonthReport report,
        AppConfig config,
        VacationStore store,
        DateOnly month,
        DateOnly today,
        IReadOnlyList<TimeEntry> entries)
    {
        var current = report;
        var missing = current.MissingWorkdays;
        if (missing.Count <= VacationPromptMinMissingDays) return current;

        ConsoleUi.Title("Отпуск");
        ConsoleUi.Info($"  Незаполненных рабочих дней: {missing.Count} ({ConsoleUi.Fmt(missing.Count * config.HoursPerDay)} ч) — больше {VacationPromptMinMissingDays}.");
        ConsoleUi.Muted("  Если часть этих дней была отпуском, укажите периоды — они выйдут из нормы.");
        ConsoleUi.Muted($"  Например: {ConsoleUi.FormatDate(missing[0])}..{ConsoleUi.FormatDate(missing[^1])}");

        if (!ConsoleUi.AskYesNo("Указать отпуск?", false)) return current;

        while (true)
        {
            var input = ConsoleUi.AskOptional("Период отпуска (Enter — закончить)");
            if (string.IsNullOrWhiteSpace(input)) break;

            List<VacationPeriod> periods;
            try
            {
                periods = VacationPeriod.ParseMany(input);
            }
            catch (VacationFormatException ex)
            {
                ConsoleUi.Error(ex.Message);
                continue;
            }

            var inMonth = periods
                .Where(p => p.To >= current.MonthStart && p.From <= current.Anchor)
                .ToList();

            if (inMonth.Count == 0)
            {
                ConsoleUi.Warn($"  В {current.Label} таких дней нет.");
                continue;
            }

            var days = inMonth
                .SelectMany(p => Enumerable.Range(0, p.Days).Select(i => p.From.AddDays(i)))
                .Where(d => d >= current.MonthStart && d <= current.Anchor)
                .ToList();

            var workdays = days.Count(d => !config.Calendar.IsWeekend(d));
            if (workdays == 0)
            {
                ConsoleUi.Warn("  Это только выходные — на норму они не влияют.");
                continue;
            }

            var alreadyLogged = current.Days
                .Where(d => days.Contains(d.Date) && d.Hours > 0)
                .Sum(d => d.Hours);

            var added = store.Add(inMonth);
            try
            {
                store.Save();
            }
            catch (ConfigException ex)
            {
                ConsoleUi.Warn($"  {ex.Message}");
                ConsoleUi.Muted("  Отпуск учтён только на этот запуск.");
            }

            config.ApplyVacations(store.Periods);
            current = MonthReport.Build(month, today, config.HoursPerDay, entries, config.Calendar);

            var label = string.Join(", ", inMonth.Select(p => p.ToDisplay()));
            ConsoleUi.Success(added > 0
                ? $"  Учтено: {label} (+{added} дн.)."
                : $"  Учтено: {label} — этот отпуск уже был сохранён.");
            if (alreadyLogged > 0)
                ConsoleUi.Warn($"  За эти дни уже списано {ConsoleUi.Fmt(alreadyLogged)} ч — проверьте, нет ли задвоения.");

            ConsoleUi.Info($"  Норма: {ConsoleUi.Fmt(current.NormByWeekdays)} ч, списано {ConsoleUi.Fmt(current.Logged)} ч, " +
                           $"недостача {ConsoleUi.Fmt(Math.Max(current.GapByWeekdays, 0))} ч, незаполненных дней {current.MissingWorkdays.Count}.");
        }

        return current;
    }

    private static async Task<Issue> ResolveIssueAsync(        RedmineClient client,
        IReadOnlyList<Issue> candidates,
        CommandLine cli,
        bool interactive)
    {
        if (!interactive)
        {
            var raw = cli.Issue!.Trim().TrimStart('#');
            if (!int.TryParse(raw, out var id) || id <= 0)
                throw new CommandLineException($"--issue: ожидается номер задачи, получено «{cli.Issue}».");

            return candidates.FirstOrDefault(i => i.Id == id) ?? await client.GetIssueAsync(id);
        }

        var choice = ConsoleUi.AskIssueChoice(candidates.Count).Trim();
        if (int.TryParse(choice.TrimStart('#'), out var number))
        {
            if (choice.StartsWith('#') || candidates.Count == 0 || number > candidates.Count)
                return await client.GetIssueAsync(number);

            return candidates[number - 1];
        }

        throw new CommandLineException($"Не удалось разобрать номер задачи «{choice}».");
    }

    private static IdName ResolveActivity(
        IReadOnlyList<IdName> activities,
        CommandLine cli,
        AppConfig config,
        bool interactive)
    {
        if (interactive) return ConsoleUi.ChooseActivity(activities, cli.Activity ?? config.DefaultActivityName);

        var wanted = cli.Activity ?? config.DefaultActivityName;

        if (int.TryParse(wanted, out var id))
        {
            var byId = activities.FirstOrDefault(a => a.Id == id);
            if (byId is not null) return byId;
        }

        var byName = activities.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;

        throw new CommandLineException(
            $"Вид работ «{wanted}» не найден. Доступны: {string.Join(", ", activities.Select(a => a.Name))}.");
    }

    private static void PrintPlan(
        Issue issue,
        DateOnly date,
        decimal hours,
        IdName activity,
        string? comment,
        AppConfig config,
        decimal existingHours)
    {
        ConsoleUi.Title("Запись о трудозатратах");

        ConsoleUi.KeyValue("Задача", $"#{issue.Id} [{issue.Status?.Name ?? "—"}] {issue.Subject}");
        ConsoleUi.KeyValue("Проект", issue.Project?.Name ?? "—");
        ConsoleUi.KeyValue("Дата", $"{ConsoleUi.FormatDate(date)} ({DayNames[(int)date.DayOfWeek]})");
        ConsoleUi.KeyValue("Часы", $"{ConsoleUi.Fmt(hours)} ч");
        ConsoleUi.KeyValue("Вид работ", activity.Name ?? "—");
        if (!string.IsNullOrWhiteSpace(comment)) ConsoleUi.KeyValue("Комментарий", comment);

        if (existingHours > 0)
        {
            var total = existingHours + hours;
            var message = $"  За этот день уже списано {ConsoleUi.Fmt(existingHours)} ч, после записи будет {ConsoleUi.Fmt(total)} ч.";
            if (total > config.HoursPerDay) ConsoleUi.Warn(message + " Это больше дневной нормы.");
            else ConsoleUi.Muted(message);
        }

        if (config.Calendar.IsWeekend(date))
            ConsoleUi.Warn("  Дата приходится на выходной.");
        else if (config.Calendar.IsHoliday(date))
            ConsoleUi.Warn("  Дата приходится на праздник.");
        else if (config.Calendar.IsVacation(date))
            ConsoleUi.Warn("  Дата приходится на отпуск: норма за этот день не начисляется.");
    }

    private static void PrintVacations(VacationStore store)
    {
        ConsoleUi.Title("Отпуск: дни вне нормы рабочего времени");

        if (store.Periods.Count == 0)
        {
            ConsoleUi.Muted("  Отпуск не задан.");
        }
        else
        {
            foreach (var period in store.Periods)
                ConsoleUi.Info($"  • {period.ToDisplay()} ({period.Days} календ. дн.)");
        }

        ConsoleUi.KeyValue("Файл", store.FilePath);

        var envRaw = Environment.GetEnvironmentVariable(VacationStore.EnvVar);
        if (!string.IsNullOrWhiteSpace(envRaw))
        {
            try
            {
                var extra = VacationPeriod.ParseMany(envRaw);
                ConsoleUi.KeyValue("Из окружения", string.Join(", ", extra.Select(p => p.ToDisplay())));
            }
            catch (VacationFormatException ex)
            {
                ConsoleUi.Warn($"  {VacationStore.EnvVar}: {ex.Message}");
            }
        }

        ConsoleUi.Muted("  Добавить: --vacation 02.11.2026..06.11.2026");
        ConsoleUi.Muted("  Убрать всё: --vacation-clear");
    }

    private static async Task PrintAfterMathAsync(
        RedmineClient client,
        User me,
        DateOnly month,
        DateOnly today,
        AppConfig config)
    {
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var entries = await client.GetTimeEntriesAsync(me.Id, month, monthEnd);
        var report = MonthReport.Build(month, today, config.HoursPerDay, entries, config.Calendar);

        Console.WriteLine();
        ConsoleUi.Info($"Итого за {report.Label}: списано {ConsoleUi.Fmt(report.Logged)} ч из {ConsoleUi.Fmt(report.NormByWeekdays)} ч " +
                       $"(недостача {ConsoleUi.Fmt(Math.Max(report.GapByWeekdays, 0))} ч).");

        var todayHours = report.Days.FirstOrDefault(d => d.Date == today)?.Hours ?? 0;
        ConsoleUi.Muted($"За сегодня ({ConsoleUi.FormatDate(today)}): {ConsoleUi.Fmt(todayHours)} ч.");
    }
}
