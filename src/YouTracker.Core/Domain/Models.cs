namespace YouTracker.Core.Domain;

public sealed record Issue(
    string Id,
    string Summary,
    string ProjectKey,
    string? Type,
    string? State,
    string? Priority,
    int? EstimateMinutes,
    int? SpentMinutes,
    DateTimeOffset Updated
);

public sealed record WorkItem(
    string Id,
    string IssueId,
    string IssueSummary,
    DateOnly Date,
    int Minutes,
    string? TypeId,
    string? TypeName,
    string? Text,
    string? AuthorLogin
);

public sealed record WorkItemType(string Id, string Name);

/// <summary>A direct subtask of an issue (Subtask link, OUTWARD direction).</summary>
public sealed record IssueChild(string Id, string Summary, string? Type, bool Resolved);

public sealed record IssueWithChildren(
    string Id,
    string Summary,
    string? Type,
    IReadOnlyList<IssueChild> Subtasks
);

public sealed record UserInfo(string Login, string FullName);

public static class DurationFormat
{
    /// <summary>Formats minutes as e.g. "6h 30m" (display only; not YouTrack server presentation).</summary>
    public static string ToPresentation(int minutes)
    {
        if (minutes <= 0)
            return "0m";
        var h = minutes / 60;
        var m = minutes % 60;
        return (h, m) switch
        {
            (0, _) => $"{m}m",
            (_, 0) => $"{h}h",
            _ => $"{h}h {m}m",
        };
    }

    /// <summary>Parses "1h 30m", "90m", "1.5h", "2h" or a plain number of minutes.</summary>
    public static bool TryParseMinutes(string? input, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var text = input.Trim().ToLowerInvariant();

        if (int.TryParse(text, out var plain) && plain > 0)
        {
            minutes = plain;
            return true;
        }

        double total = 0;
        var matched = false;
        foreach (
            System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                text,
                @"(\d+(?:[.,]\d+)?)\s*([hm])"
            )
        )
        {
            var value = double.Parse(
                match.Groups[1].Value.Replace(',', '.'),
                System.Globalization.CultureInfo.InvariantCulture
            );
            total += match.Groups[2].Value == "h" ? value * 60 : value;
            matched = true;
        }

        minutes = (int)Math.Round(total);
        return matched && minutes > 0;
    }
}
