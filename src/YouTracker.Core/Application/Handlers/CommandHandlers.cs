using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Core.Application.Handlers;

public sealed class CreateWorkItemCommandHandler(IWorkItemWriter writer, IEventBus events)
    : ICommandHandler<CreateWorkItemCommand, WorkItem>
{
    public async Task<WorkItem> HandleAsync(
        CreateWorkItemCommand command,
        CancellationToken ct = default
    )
    {
        if (command.Minutes <= 0)
            throw new ArgumentException("Duration must be positive.", nameof(command));
        var created = await writer
            .CreateWorkItemAsync(
                new NewWorkItem(
                    command.IssueId,
                    command.Date,
                    command.Minutes,
                    command.TypeId,
                    command.Text
                ),
                ct
            )
            .ConfigureAwait(false);
        events.Publish(new WorkItemCreated(created.IssueId, created.Date, created.Minutes));
        return created;
    }
}

public sealed class CommitWorkLogDraftsCommandHandler(
    IWorkItemWriter writer,
    IWorkItemReader reader,
    IEventBus events
) : ICommandHandler<CommitWorkLogDraftsCommand, CommitResult>
{
    public async Task<CommitResult> HandleAsync(
        CommitWorkLogDraftsCommand command,
        CancellationToken ct = default
    )
    {
        var types = await reader.GetWorkItemTypesAsync(ct).ConfigureAwait(false);
        var created = 0;
        var errors = new List<string>();

        foreach (var draft in command.ConfirmedDrafts)
        {
            if (string.IsNullOrWhiteSpace(draft.IssueId) || draft.Minutes <= 0)
            {
                errors.Add(
                    $"{draft.IssueId}: invalid draft (missing issue or non-positive duration)."
                );
                continue;
            }

            var typeId =
                types
                    .FirstOrDefault(t =>
                        string.Equals(
                            t.Name,
                            draft.WorkTypeName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    ?.Id ?? command.DefaultTypeId;

            try
            {
                var item = await writer
                    .CreateWorkItemAsync(
                        new NewWorkItem(
                            draft.IssueId,
                            draft.Date,
                            draft.Minutes,
                            typeId,
                            draft.Comment
                        ),
                        ct
                    )
                    .ConfigureAwait(false);
                events.Publish(new WorkItemCreated(item.IssueId, item.Date, item.Minutes));
                created++;
            }
            catch (Exception ex)
            {
                errors.Add($"{draft.IssueId}: {ex.Message}");
            }
        }

        return new CommitResult(created, errors);
    }
}

public sealed class StartTimerCommandHandler(ITimerStore store, TimeProvider time, IEventBus events)
    : ICommandHandler<StartTimerCommand, TimerState>
{
    public Task<TimerState> HandleAsync(StartTimerCommand command, CancellationToken ct = default)
    {
        if (store.Load() is { } running)
            throw new InvalidOperationException(
                $"A timer is already running on {running.IssueId}. Stop it first."
            );
        var state = new TimerState(command.IssueId, command.IssueSummary, time.GetUtcNow());
        store.Save(state);
        events.Publish(new TimerStarted(state.IssueId, state.StartedUtc));
        return Task.FromResult(state);
    }
}

public sealed class StopTimerCommandHandler(
    ITimerStore store,
    TimeProvider time,
    AppConfig config,
    IEventBus events
) : ICommandHandler<StopTimerCommand, TimerStopResult?>
{
    public Task<TimerStopResult?> HandleAsync(
        StopTimerCommand command,
        CancellationToken ct = default
    )
    {
        if (store.Load() is not { } running)
            return Task.FromResult<TimerStopResult?>(null);
        store.Clear();
        var elapsed = Math.Max(
            1,
            (int)Math.Round((time.GetUtcNow() - running.StartedUtc).TotalMinutes)
        );
        events.Publish(new TimerStopped(running.IssueId, running.IssueSummary, elapsed));
        return Task.FromResult<TimerStopResult?>(
            new TimerStopResult(running.IssueId, running.IssueSummary, elapsed, config.Today(time))
        );
    }
}
