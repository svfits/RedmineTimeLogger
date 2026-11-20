using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedmineTimeLogger.Redmine;

/// <summary>Ошибка Redmine API (HTTP-статус + текст ответа).</summary>
public sealed class RedmineException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    public RedmineException(string message, HttpStatusCode? statusCode = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public static RedmineException FromResponse(string what, HttpStatusCode status, string body) =>
        new($"{what}: HTTP {(int)status} {status}. {ExtractErrors(body)}", status, body);

    private static string ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "пустой ответ";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s));
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrWhiteSpace(joined)) return joined;
            }
        }
        catch (JsonException)
        {
            // не JSON — отдадим текст как есть
        }

        return body.Length > 600 ? body[..600] + "…" : body;
    }
}

/// <summary>HTTP-клиент Redmine REST API (только то, что нужно приложению).</summary>
public sealed class RedmineClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = RedmineJson.Options;
    private readonly HttpClient _http;

    public string BaseUrl { get; }

    public RedmineClient(string baseUrl, string apiKey, bool ignoreCertificateErrors = false)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Пустой API-ключ Redmine.", nameof(apiKey));

        BaseUrl = baseUrl.TrimEnd('/');

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (ignoreCertificateErrors)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(60),
        };

        _http.DefaultRequestHeaders.Add("X-Redmine-API-Key", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RedmineTimeLogger/1.0");
    }

    public async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<CurrentUserResponse>("users/current.json", ct);
        return response?.User ?? throw new RedmineException("Redmine не вернул текущего пользователя.");
    }

    public async Task<List<TimeEntry>> GetTimeEntriesAsync(int userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = new List<TimeEntry>();
        var offset = 0;

        while (true)
        {
            var path = $"time_entries.json?user_id={userId}&from={Dates.Iso(from)}&to={Dates.Iso(to)}&limit=100&offset={offset}";
            var page = await GetAsync<TimeEntriesResponse>(path, ct);
            var items = page?.TimeEntries;
            if (items is null || items.Count == 0) break;

            result.AddRange(items);
            offset += items.Count;
            if (page!.TotalCount is null || offset >= page.TotalCount) break;
        }

        return result;
    }

    /// <summary>Открытые задачи, назначенные пользователю.</summary>
    public async Task<List<Issue>> GetOpenAssignedIssuesAsync(int userId, CancellationToken ct = default)
    {
        var result = new List<Issue>();
        var offset = 0;

        while (true)
        {
            var path = $"issues.json?assigned_to_id={userId}&status_id=open&sort=updated_on:desc&limit=100&offset={offset}";
            var page = await GetAsync<IssuesResponse>(path, ct);
            var items = page?.Issues;
            if (items is null || items.Count == 0) break;

            result.AddRange(items);
            offset += items.Count;
            if (page!.TotalCount is null || offset >= page.TotalCount) break;
        }

        return result;
    }

    public async Task<Issue> GetIssueAsync(int issueId, CancellationToken ct = default)
    {
        var response = await GetAsync<IssueResponse>($"issues/{issueId}.json", ct);
        return response?.Issue ?? throw new RedmineException($"Задача #{issueId} не найдена.");
    }

    public async Task<List<IdName>> GetStatusesAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<IssueStatusesResponse>("issue_statuses.json", ct);
        return response?.IssueStatuses ?? new List<IdName>();
    }

    public async Task<List<IdName>> GetActivitiesAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<ActivitiesResponse>("enumerations/time_entry_activities.json", ct);
        return response?.TimeEntryActivities ?? new List<IdName>();
    }

    public async Task<TimeEntry> CreateTimeEntryAsync(TimeEntryCreate request, CancellationToken ct = default)
    {
        var envelope = new TimeEntryCreateEnvelope { TimeEntry = request };
        var json = JsonSerializer.Serialize(envelope, Json);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("time_entries.json", content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw RedmineException.FromResponse($"POST time_entries.json (задача #{request.IssueId})", response.StatusCode, text);

        var created = JsonSerializer.Deserialize<TimeEntryResponse>(text, Json);
        return created?.TimeEntry ?? throw new RedmineException("Redmine принял запрос, но вернул пустой ответ.");
    }

    public static string SerializePayload(TimeEntryCreate request) =>
        JsonSerializer.Serialize(new TimeEntryCreateEnvelope { TimeEntry = request }, RedmineJson.PrettyOptions);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw RedmineException.FromResponse($"GET {path}", response.StatusCode, text);

        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class IssueResponse
{
    public Issue? Issue { get; set; }
}

public static class RedmineJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // Кириллицу в теле запроса не экранируем: Redmine принимает UTF-8 как есть.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public static readonly JsonSerializerOptions PrettyOptions = new(Options) { WriteIndented = true };
}

public static class Dates
{
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
}
