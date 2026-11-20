namespace RedmineTimeLogger.Cli;

public static class Help
{
    public static void Print()
    {
        Console.WriteLine("""
            RedmineTimeLogger — списание трудозатрат в Redmine.

            Программа показывает норму и недостачу часов за текущий месяц,
            находит задачи в статусах In-Progress / On review / On approve,
            спрашивает, на какую задачу и сколько часов списать, и только
            после подтверждения создаёт запись.

            Использование:
              RedmineTimeLogger [параметры]

            Без параметров — интерактивный режим.

            Параметры:
              --issue <N>          номер задачи; включает неинтерактивный режим
              --hours <H>          часы (например 8 или 4,5); по умолчанию — норма дня
              --date <ДД.ММ.ГГГГ>  дата списания; по умолчанию — последний незаполненный рабочий день
              --activity <название> вид работ; по умолчанию «Разработка»
              --comment <текст>    комментарий к записи
              --month <ГГГГ-ММ>    месяц для отчёта; по умолчанию текущий
              --holidays <даты>    дополнительные нерабочие дни через запятую (ГГГГ-ММ-ДД)
              --url <адрес>        адрес Redmine; по умолчанию https://rm.burworld.ru
              --report             только отчёт по месяцу, без списания
              --check              проверить подключение и ключ
              --dry-run            показать запись, но не отправлять её
              -y, --yes            не спрашивать подтверждение
              --insecure           не проверять TLS-сертификат сервера
              -h, --help           эта справка

            Ключ доступа (обязателен, извне):
              REDMINE_API_KEY        сам ключ
              REDMINE_API_KEY_FILE   путь к файлу с ключом
              по умолчанию — %USERPROFILE%\.dsh\redmine-api-key, если файл есть

            Прочие переменные окружения:
              REDMINE_URL               адрес Redmine
              REDMINE_HOURS_PER_DAY     норма часов в день (по умолчанию 8)
              REDMINE_HOLIDAYS          дополнительные нерабочие дни
              REDMINE_DEFAULT_ACTIVITY  вид работ по умолчанию
              REDMINE_IGNORE_CERT_ERRORS=1  не проверять TLS-сертификат

            Примеры:
              RedmineTimeLogger
              RedmineTimeLogger --report
              RedmineTimeLogger --issue 26422 --hours 8 --date 2026-11-19 --dry-run
              RedmineTimeLogger --issue 26422 --hours 8 --date 2026-11-19 -y --comment "MR !66"
            """);
    }
}
