using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application;

/// <summary>
/// The single place the "bookings never land on a Feature" rule lives. Pure mapping from
/// an issue (plus its subtasks) to a booking target; used by the pre-flight query the UI
/// calls AND the write-side safety net in the command handlers.
/// </summary>
public static class BookingTargetResolver
{
    public static BookingTarget Resolve(
        string requestedId,
        IssueWithChildren? issue,
        IReadOnlyList<string> featureTypes,
        IReadOnlyList<string> taskTypes
    )
    {
        // Unknown/invisible issue: never block — the actual write produces YouTrack's own error.
        if (issue is null || !ContainsIgnoreCase(featureTypes, issue.Type))
            return Direct(requestedId, issue);

        var candidates = issue
            .Subtasks.Where(s => ContainsIgnoreCase(taskTypes, s.Type))
            .Select(s => new SubtaskCandidate(s.Id, s.Summary, s.Resolved))
            .ToList();

        if (candidates.Count == 0)
            return new BookingTarget(
                requestedId,
                BookingTargetKind.NoTask,
                requestedId,
                issue.Summary,
                TargetResolved: false,
                Candidates: []
            );

        // Unambiguous target: a single task, or several of which exactly one is still open.
        var target =
            candidates.Count == 1 ? candidates[0]
            : candidates.Count(c => !c.Resolved) == 1 ? candidates.Single(c => !c.Resolved)
            : null;

        return target is not null
            ? new BookingTarget(
                requestedId,
                BookingTargetKind.Redirected,
                target.IssueId,
                target.Summary,
                target.Resolved,
                Candidates: []
            )
            : new BookingTarget(
                requestedId,
                BookingTargetKind.Ambiguous,
                requestedId,
                issue.Summary,
                TargetResolved: false,
                candidates
            );
    }

    /// <summary>Fetch + resolve convenience used by the query handler and the command-side safety net.</summary>
    public static async Task<BookingTarget> ResolveAsync(
        IIssueReader issues,
        AppConfig config,
        string issueId,
        CancellationToken ct = default
    )
    {
        var issue = await issues.GetIssueWithChildrenAsync(issueId, ct).ConfigureAwait(false);
        return Resolve(issueId, issue, config.EffectiveFeatureTypes, config.EffectiveTaskTypes);
    }

    private static BookingTarget Direct(string requestedId, IssueWithChildren? issue) =>
        new(
            requestedId,
            BookingTargetKind.Direct,
            requestedId,
            issue?.Summary,
            TargetResolved: false,
            Candidates: []
        );

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string? value) =>
        value is not null && values.Contains(value, StringComparer.OrdinalIgnoreCase);
}
