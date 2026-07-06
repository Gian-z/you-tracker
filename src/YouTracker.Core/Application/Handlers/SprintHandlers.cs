using System.Text;
using System.Text.Json;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Ai;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

public sealed class GetTeamConfigQueryHandler(ITeamConfigStore store)
    : IQueryHandler<GetTeamConfigQuery, TeamConfig?>
{
    public Task<TeamConfig?> HandleAsync(
        GetTeamConfigQuery query,
        CancellationToken ct = default
    ) => Task.FromResult(store.Load());
}

public sealed class SaveSprintAbsencesCommandHandler(ITeamConfigStore store)
    : ICommandHandler<SaveSprintAbsencesCommand, TeamSprint>
{
    public Task<TeamSprint> HandleAsync(
        SaveSprintAbsencesCommand command,
        CancellationToken ct = default
    )
    {
        var config =
            store.Load()
            ?? throw new InvalidOperationException(
                $"No team config found at '{store.ConfigPath}'."
            );
        var sprint =
            config.Sprints.FirstOrDefault(s =>
                string.Equals(s.Name, command.SprintName, StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidOperationException($"Unknown sprint '{command.SprintName}'.");

        var updated = sprint with { Absences = command.Absences };
        store.Save(
            config with
            {
                Sprints = [.. config.Sprints.Select(s => s.Name == sprint.Name ? updated : s)],
            }
        );
        return Task.FromResult(updated);
    }
}

public sealed class GetSprintDashboardQueryHandler(
    ITeamConfigStore teamStore,
    ISprintReader sprintReader,
    IWorkItemReader workItems,
    AppConfig config,
    TimeProvider time
) : IQueryHandler<GetSprintDashboardQuery, SprintDashboard>
{
    public async Task<SprintDashboard> HandleAsync(
        GetSprintDashboardQuery query,
        CancellationToken ct = default
    )
    {
        var (team, sprint) = ResolveSprint(teamStore, query.SprintName);
        var from = sprint.Workdays.Min();
        var to = sprint.Workdays.Max();

        // Fan out per member; sequential keeps YouTrack load predictable and cache warm.
        var byLogin = new Dictionary<string, IReadOnlyList<WorkItem>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var member in team.Members)
            byLogin[member.Login] = await workItems
                .GetWorkItemsAsync(member.Login, from, to, ct)
                .ConfigureAwait(false);

        var tasks = await sprintReader
            .GetSprintTaskCategoriesAsync(Substitute(team.TaskQuery, sprint.Name), ct)
            .ConfigureAwait(false);
        var features = await sprintReader
            .GetSprintFeaturesAsync(Substitute(team.FeatureSprintQuery, sprint.Name), ct)
            .ConfigureAwait(false);

        return SprintMetricsCalculator.Build(
            sprint,
            team.Members,
            byLogin,
            tasks,
            features,
            team.CeremonyPatterns,
            config.Today(time)
        );
    }

    internal static string Substitute(string queryTemplate, string sprintName) =>
        queryTemplate.Replace("{SPRINT}", "{" + sprintName + "}", StringComparison.Ordinal);

    internal static (TeamConfig Team, TeamSprint Sprint) ResolveSprint(
        ITeamConfigStore store,
        string sprintName
    )
    {
        var team =
            store.Load()
            ?? throw new InvalidOperationException(
                "No team config found. Create team.json next to config.json."
            );
        var sprint =
            team.Sprints.FirstOrDefault(s =>
                string.Equals(s.Name, sprintName, StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidOperationException($"Unknown sprint '{sprintName}'.");
        if (sprint.Workdays.Count == 0)
            throw new InvalidOperationException($"Sprint '{sprintName}' has no workdays.");
        return (team, sprint);
    }
}

/// <summary>German Fazit prose per dev. Reuses the dashboard query (cached) for the facts.</summary>
public sealed class GenerateSprintVerdictsQueryHandler(IDispatcher dispatcher, IAiProvider ai)
    : IQueryHandler<GenerateSprintVerdictsQuery, IReadOnlyList<SprintVerdict>>
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "verdicts": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "login": { "type": "string" },
                  "text": { "type": "string", "description": "Zwei Absätze, durch Leerzeile getrennt" }
                },
                "required": ["login", "text"],
                "additionalProperties": false
              }
            }
          },
          "required": ["verdicts"],
          "additionalProperties": false
        }
        """;

    public async Task<IReadOnlyList<SprintVerdict>> HandleAsync(
        GenerateSprintVerdictsQuery query,
        CancellationToken ct = default
    )
    {
        var dashboard = await dispatcher
            .QueryAsync(new GetSprintDashboardQuery(query.SprintName), ct)
            .ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine(
            $"## Sprint {dashboard.SprintName} — Fakten pro Entwickler (fix, nicht neu bewerten)"
        );
        foreach (var v in dashboard.Verdicts)
        {
            sb.AppendLine(
                $"- {v.Login} ({v.Name}) | Ampel: {v.Ampel} | Buchungstage: {v.DaysWithBookings}/{v.AvailableDays} | "
                    + $"Roadmap: {DurationFormat.ToPresentation(v.RoadmapMinutes)} von {DurationFormat.ToPresentation(v.TargetMinutes)} ({v.AttainmentPercent}%) | "
                    + $"Non-Roadmap: {DurationFormat.ToPresentation(v.NonRoadmapMinutes)} | Unbekannt: {DurationFormat.ToPresentation(v.UnknownMinutes)}"
            );
            foreach (var signal in v.Signals)
                sb.AppendLine($"  - Signal: {signal}");
            foreach (var f in v.OwnFeatures)
                sb.AppendLine(
                    $"  - Feature {f.IssueId}: Schätzung {(f.EstimateMinutes is { } e ? DurationFormat.ToPresentation(e) : "-")}, "
                        + $"gebucht {DurationFormat.ToPresentation(f.SpentMinutes)}"
                        + (f.GapPercent is { } gp ? $" ({(gp >= 0 ? "+" : "")}{gp}%)" : "")
                );
        }
        sb.AppendLine();
        sb.AppendLine(
            "## Aufgabe\nSchreibe pro Entwickler ein deutsches Fazit mit GENAU zwei Absätzen "
                + "(getrennt durch eine Leerzeile): Absatz 1 = Buchungspräsenz & Roadmap-Anteil "
                + "(Tage, Erreichung vs. Soll, Pensum berücksichtigen). Absatz 2 = Estimation-Einhaltung "
                + "& konkrete Handlungsempfehlung an den Scrum Master (Feature-IDs nennen). "
                + "Die Ampel und alle Zahlen sind fix — nicht neu bewerten, nur verbalisieren. "
                + "Merksatz: Viel buchen ≠ viel liefern."
        );

        var json = await ai.CompleteJsonAsync(PromptBuilder.SystemBase, sb.ToString(), Schema, ct)
            .ConfigureAwait(false);
        return ParseVerdicts(json, dashboard.Verdicts);
    }

    internal static IReadOnlyList<SprintVerdict> ParseVerdicts(
        string json,
        IReadOnlyList<DevVerdictFacts> facts
    )
    {
        var known = facts.Select(f => f.Login).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        var result = new List<SprintVerdict>();
        if (doc.RootElement.TryGetProperty("verdicts", out var verdicts))
        {
            foreach (var v in verdicts.EnumerateArray())
            {
                var login = v.TryGetProperty("login", out var l) ? l.GetString() : null;
                var text = v.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (login is not null && text is not null && known.Contains(login))
                    result.Add(new SprintVerdict(login, text));
            }
        }
        return result;
    }
}
