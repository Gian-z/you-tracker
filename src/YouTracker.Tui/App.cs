using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Config;
using YouTracker.Tui.Views;

namespace YouTracker.Tui;

/// <summary>Terminal.Gui shell: window, status bar, view switching, global timer handling.</summary>
public sealed class App
{
    private readonly IDispatcher _dispatcher;
    private readonly IEventBus _eventBus;
    private readonly AppConfig _config;

    private TimerState? _timer;
    private Window? _content;
    private StatusBar? _statusBar;
    private StatusItem? _timerItem;
    private TaskListView? _tasks;
    private TimeOverviewView? _time;
    private AiAssistantView? _ai;

    public App(IDispatcher dispatcher, IEventBus eventBus, AppConfig config)
    {
        _dispatcher = dispatcher;
        _eventBus = eventBus;
        _config = config;
    }

    private DateOnly Today() => _config.Today(TimeProvider.System);

    public void Run()
    {
        Application.Init();
        try
        {
            var top = Application.Top;

            _content = new Window("you-tracker")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
            };

            _tasks = new TaskListView(_dispatcher);
            _tasks.StartTimerRequested += OnStartTimerFromTaskList;
            _tasks.LogTimeRequested += item =>
                LogTimeDialog.Show(_dispatcher, item.IssueId, item.Summary, Today(), null);
            _time = new TimeOverviewView(_dispatcher, Today);
            _ai = new AiAssistantView(_dispatcher, Today);

            _timerItem = new StatusItem(Key.Null, "■ timer off", () => { });
            _statusBar = new StatusBar(
                new[]
                {
                    new StatusItem(Key.F1, "~F1~ Tasks", () => ShowView(_tasks!)),
                    new StatusItem(Key.F2, "~F2~ Time", () => ShowView(_time!)),
                    new StatusItem(Key.F3, "~F3~ AI", () => ShowView(_ai!)),
                    new StatusItem(Key.CtrlMask | Key.T, "~^T~ Timer", ToggleTimer),
                    new StatusItem(
                        Key.CtrlMask | Key.Q,
                        "~^Q~ Quit",
                        () => Application.RequestStop()
                    ),
                    _timerItem,
                }
            );

            top.Add(_content, _statusBar);
            ShowView(_tasks);
            _tasks.Load();
            _time.Load();

            using var workItemSub = _eventBus.Subscribe<WorkItemCreated>(_ =>
                Application.MainLoop?.Invoke(() =>
                {
                    _tasks?.Load();
                    _time?.Load();
                })
            );
            using var timerStartedSub = _eventBus.Subscribe<TimerStarted>(_ => RefreshTimerState());
            using var timerStoppedSub = _eventBus.Subscribe<TimerStopped>(_ => RefreshTimerState());

            RefreshTimerState(); // resumes a persisted timer
            Application.MainLoop.AddTimeout(
                TimeSpan.FromSeconds(1),
                _ =>
                {
                    UpdateTimerText();
                    return true;
                }
            );

            Application.Run(top);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private void ShowView(View view)
    {
        if (_content is null)
            return;
        _content.RemoveAll();
        _content.Add(view);
        view.SetFocus();
        _content.SetNeedsDisplay();
    }

    /// <summary>Ctrl+T: stop-and-log when running, otherwise start on the task list selection.</summary>
    private void ToggleTimer()
    {
        if (_timer is not null)
        {
            StopTimerAndLog();
            return;
        }

        var item = _tasks?.SelectedItem;
        if (item is null)
        {
            MessageBox.Query("Timer", "Select a task in the Tasks view (F1) first.", "Ok");
            return;
        }
        StartTimer(item.IssueId, item.Summary);
    }

    private void OnStartTimerFromTaskList(Core.ReadModels.TaskListItem item)
    {
        if (_timer is not null)
        {
            var choice = MessageBox.Query(
                "Timer running",
                $"A timer is already running on {_timer.IssueId}. Stop it and log the time first?",
                "Stop & log",
                "Cancel"
            );
            if (choice == 0)
                StopTimerAndLog();
            return;
        }
        StartTimer(item.IssueId, item.Summary);
    }

    private void StartTimer(string issueId, string issueSummary) =>
        UiRunner.Run(
            async () =>
            {
                await _dispatcher.SendAsync(new StartTimerCommand(issueId, issueSummary));
            },
            "Start timer failed"
        );

    private void StopTimerAndLog() =>
        UiRunner.Run(
            async () =>
            {
                var result = await _dispatcher.SendAsync(new StopTimerCommand());
                Application.MainLoop?.Invoke(() =>
                {
                    if (result is null)
                        return;
                    // The timer store is only cleared after the booking succeeded — cancelling
                    // the dialog keeps the timer (and its elapsed time) running.
                    LogTimeDialog.Show(
                        _dispatcher,
                        result.IssueId,
                        result.IssueSummary,
                        result.Date,
                        Math.Max(1, result.ElapsedMinutes),
                        afterSave: async () =>
                            await _dispatcher.SendAsync(new DiscardTimerCommand())
                    );
                });
            },
            "Stop timer failed"
        );

    private void RefreshTimerState() =>
        UiRunner.Run(
            async () =>
            {
                var state = await _dispatcher.QueryAsync(new GetTimerStateQuery());
                Application.MainLoop?.Invoke(() =>
                {
                    _timer = state;
                    UpdateTimerText();
                });
            },
            "Timer state failed"
        );

    private void UpdateTimerText()
    {
        if (_timerItem is null || _statusBar is null)
            return;
        var text = _timer is null ? "■ timer off" : $"▶ {_timer.IssueId} {Elapsed(_timer)}";
        if (_timerItem.Title.ToString() == text)
            return;
        _timerItem.Title = text;
        _statusBar.SetNeedsDisplay();
    }

    private static string Elapsed(TimerState timer)
    {
        var elapsed = DateTimeOffset.UtcNow - timer.StartedUtc;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
