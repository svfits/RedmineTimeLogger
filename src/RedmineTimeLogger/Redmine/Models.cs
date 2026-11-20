namespace RedmineTimeLogger.Redmine;

// ---------------------------------------------------------------------------
// DTOs. Все имена в JSON — snake_case (настраивается в RedmineJson.Options).
// Модели намеренно "терпимые": незнакомые поля Redmine игнорируются.
// ---------------------------------------------------------------------------

public sealed class User
{
    public int Id { get; set; }
    public string? Login { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
    public string? Mail { get; set; }

    public string DisplayName
    {
        get
        {
            var name = $"{Firstname} {Lastname}".Trim();
            return string.IsNullOrWhiteSpace(name) ? (Login ?? $"#{Id}") : name;
        }
    }
}

public sealed class IdName
{
    public int Id { get; set; }
    public string? Name { get; set; }

    public override string ToString() => Name ?? $"#{Id}";
}

public sealed class IssueRef
{
    public int Id { get; set; }
}

public sealed class CurrentUserResponse
{
    public User? User { get; set; }
}

public sealed class TimeEntry
{
    public int Id { get; set; }
    public IdName? Project { get; set; }
    public IssueRef? Issue { get; set; }
    public IdName? User { get; set; }
    public IdName? Activity { get; set; }
    public decimal Hours { get; set; }
    public string? Comments { get; set; }
    public DateOnly SpentOn { get; set; }
}

public sealed class TimeEntriesResponse
{
    public List<TimeEntry>? TimeEntries { get; set; }
    public int? TotalCount { get; set; }
}

public sealed class Issue
{
    public int Id { get; set; }
    public string? Subject { get; set; }
    public IdName? Project { get; set; }
    public IdName? Tracker { get; set; }
    public IdName? Status { get; set; }
    public IdName? Priority { get; set; }
    public User? Author { get; set; }
    public User? AssignedTo { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? SpentHours { get; set; }
    public int? DoneRatio { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? CreatedOn { get; set; }
    public DateTimeOffset? UpdatedOn { get; set; }
    public string? Description { get; set; }
}

public sealed class IssuesResponse
{
    public List<Issue>? Issues { get; set; }
    public int? TotalCount { get; set; }
}

public sealed class IssueStatusesResponse
{
    public List<IdName>? IssueStatuses { get; set; }
}

public sealed class ActivitiesResponse
{
    public List<IdName>? TimeEntryActivities { get; set; }
}

public sealed class TimeEntryResponse
{
    public TimeEntry? TimeEntry { get; set; }
}

// ---------------------------------------------------------------------------
// Тело запроса на создание трудозатраты.
// ---------------------------------------------------------------------------

public sealed class TimeEntryCreate
{
    public int IssueId { get; set; }
    public DateOnly SpentOn { get; set; }
    public decimal Hours { get; set; }
    public int ActivityId { get; set; }
    public string? Comments { get; set; }
}

public sealed class TimeEntryCreateEnvelope
{
    public TimeEntryCreate? TimeEntry { get; set; }
}
