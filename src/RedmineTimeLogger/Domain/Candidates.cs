using RedmineTimeLogger.Redmine;

namespace RedmineTimeLogger.Domain;

/// <summary>
/// Отбор задач, в которые имеет смысл списывать время:
/// In-Progress, On review, On approve.
/// </summary>
public static class Candidates
{
    public static readonly (int Id, string Name, string Normalized)[] Statuses =
    {
        (3, "In-Progress", "inprogress"),
        (15, "On review", "onreview"),
        (12, "On approve", "onapprove"),
    };

    public static string Normalize(string? name) =>
        new string((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>0 — In-Progress, 1 — On review, 2 — On approve, -1 — не подходит.</summary>
    public static int Rank(IdName? status)
    {
        if (status is null) return -1;

        for (var i = 0; i < Statuses.Length; i++)
        {
            if (status.Id == Statuses[i].Id) return i;
            if (Normalize(status.Name) == Statuses[i].Normalized) return i;
        }

        return -1;
    }

    public static bool IsCandidate(Issue issue) => Rank(issue.Status) >= 0;

    public static List<Issue> Filter(IEnumerable<Issue> issues) =>
        issues
            .Where(IsCandidate)
            .OrderBy(i => Rank(i.Status))
            .ThenByDescending(i => i.UpdatedOn)
            .ToList();
}
