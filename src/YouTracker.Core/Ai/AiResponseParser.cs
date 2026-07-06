using System.Text.Json;
using System.Text.Json.Serialization;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Ai;

/// <summary>
/// Parses and validates raw AI JSON. Unknown issue IDs are downgraded to unmatched —
/// they can never end up in a committable draft.
/// </summary>
public static class AiResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly string[] Confidences = ["high", "medium", "low"];

    public static WorkLogDraftResult ParseDrafts(
        string json,
        IReadOnlyList<Issue> knownIssues,
        DateOnly fallbackDate
    )
    {
        var response =
            JsonSerializer.Deserialize<DraftsDto>(json, Options)
            ?? throw new FormatException("AI returned empty draft response.");

        var byId = knownIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var drafts = new List<WorkLogDraft>();
        var unmatched = (response.Unmatched ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u.Text))
            .Select(u => new UnmatchedActivity(u.Text!, u.Reason ?? "unmatched"))
            .ToList();

        foreach (var d in response.Drafts ?? [])
        {
            if (string.IsNullOrWhiteSpace(d.IssueId) || !byId.TryGetValue(d.IssueId, out var issue))
            {
                unmatched.Add(
                    new UnmatchedActivity(
                        d.Comment ?? d.IssueId ?? "?",
                        $"unknown issue id '{d.IssueId}'"
                    )
                );
                continue;
            }
            if (d.Minutes is null or <= 0)
            {
                unmatched.Add(
                    new UnmatchedActivity($"{issue.Id}: {d.Comment}", "non-positive duration")
                );
                continue;
            }

            var date = DateOnly.TryParseExact(d.Date, "yyyy-MM-dd", out var parsed)
                ? parsed
                : fallbackDate;
            var confidence = Confidences.Contains(d.Confidence?.ToLowerInvariant())
                ? d.Confidence!.ToLowerInvariant()
                : "low";
            drafts.Add(
                new WorkLogDraft(
                    issue.Id,
                    issue.Summary,
                    confidence,
                    date,
                    d.Minutes.Value,
                    NullIfEmpty(d.WorkTypeName),
                    NullIfEmpty(d.Comment),
                    NullIfEmpty(d.Reasoning)
                )
            );
        }

        return new WorkLogDraftResult(drafts, unmatched);
    }

    public static TriageResult ParseTriage(
        string json,
        IReadOnlyList<Issue> knownIssues,
        IReadOnlyList<Issue>? poolIssues = null
    )
    {
        var response =
            JsonSerializer.Deserialize<TriageDto>(json, Options)
            ?? throw new FormatException("AI returned empty triage response.");

        return new TriageResult(
            Rank(response.Ranked, knownIssues),
            response.FocusSuggestion ?? string.Empty,
            Rank(response.SprintSuggestions, poolIssues ?? [])
        );
    }

    /// <summary>Validates AI-ranked entries against the allowed issue set and re-ranks sequentially.</summary>
    private static List<TriagedIssue> Rank(List<TriageItemDto>? items, IReadOnlyList<Issue> allowed)
    {
        var byId = allowed.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. (items ?? [])
                .Where(r => r.IssueId is not null && byId.ContainsKey(r.IssueId))
                .OrderBy(r => r.Rank)
                .Select(
                    (r, index) =>
                        new TriagedIssue(
                            byId[r.IssueId!].Id,
                            byId[r.IssueId!].Summary,
                            index + 1,
                            Math.Clamp(r.Score, 0, 100),
                            r.Reasons ?? []
                        )
                ),
        ];
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class DraftsDto
    {
        public List<DraftDto>? Drafts { get; set; }
        public List<UnmatchedDto>? Unmatched { get; set; }
    }

    private sealed class DraftDto
    {
        public string? IssueId { get; set; }
        public string? Confidence { get; set; }
        public string? Date { get; set; }
        public int? Minutes { get; set; }
        public string? WorkTypeName { get; set; }
        public string? Comment { get; set; }
        public string? Reasoning { get; set; }
    }

    private sealed class UnmatchedDto
    {
        public string? Text { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class TriageDto
    {
        public List<TriageItemDto>? Ranked { get; set; }
        public string? FocusSuggestion { get; set; }
        public List<TriageItemDto>? SprintSuggestions { get; set; }
    }

    private sealed class TriageItemDto
    {
        public string? IssueId { get; set; }
        public int Rank { get; set; }
        public int Score { get; set; }
        public List<string>? Reasons { get; set; }
    }
}
