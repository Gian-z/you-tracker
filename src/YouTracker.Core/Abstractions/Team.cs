namespace YouTracker.Core.Abstractions;

// Scrum-team configuration for the sprint dashboard (seeded from the
// youtrack-sprint-zeitbuchungen skill's team-config files).

public sealed record TeamMember(
    string Login,
    string Name,
    int ThresholdMinutes,
    IReadOnlyList<DayOfWeek> Weekdays
);

public sealed record TeamAbsence(string Login, DateOnly From, DateOnly To);

public sealed record TeamSprint(
    string Name,
    IReadOnlyList<DateOnly> Workdays,
    IReadOnlyList<TeamAbsence> Absences
);

public sealed record TeamConfig(
    string Name,
    IReadOnlyList<string> Projects,
    string TaskQuery,
    string FeatureSprintQuery,
    IReadOnlyList<string> CeremonyPatterns,
    IReadOnlyList<TeamMember> Members,
    IReadOnlyList<TeamSprint> Sprints
);

/// <summary>Persistence port for the team configuration (UI can edit sprint absences).</summary>
public interface ITeamConfigStore
{
    string ConfigPath { get; }
    TeamConfig? Load();
    void Save(TeamConfig config);
}

/// <summary>A sprint task with its parent feature's Roadmapvorhaben category (null = unknown).</summary>
public sealed record SprintTaskCategory(string IssueId, string? Roadmapvorhaben);

public sealed record SprintFeature(
    string Id,
    string Summary,
    string? Roadmapvorhaben,
    string? AssigneeLogin,
    int? EstimateMinutes,
    int? SpentMinutes
);

/// <summary>Read port for sprint-scoped issue data (queries arrive fully resolved).</summary>
public interface ISprintReader
{
    /// <summary>Tasks matched by <paramref name="taskQuery"/> with parent-feature RMV via Subtask INWARD links.</summary>
    Task<IReadOnlyList<SprintTaskCategory>> GetSprintTaskCategoriesAsync(
        string taskQuery,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<SprintFeature>> GetSprintFeaturesAsync(
        string featureQuery,
        CancellationToken ct = default
    );
}
