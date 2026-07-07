using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Domain;

namespace YouTracker.Tui.Views;

/// <summary>
/// Modal work-item creation for a fixed issue. Used from the task list (Enter)
/// and after stopping the timer (duration prefilled from the elapsed time).
/// </summary>
public static class LogTimeDialog
{
    public static void Show(
        IDispatcher dispatcher,
        string issueId,
        string issueSummary,
        DateOnly defaultDate,
        int? prefillMinutes,
        Func<Task>? afterSave = null
    )
    {
        UiRunner.Run(
            async () =>
            {
                IReadOnlyList<WorkItemType> types;
                try
                {
                    types = await dispatcher.QueryAsync(new GetWorkItemTypesQuery());
                }
                catch
                {
                    types = Array.Empty<WorkItemType>();
                }
                Application.MainLoop?.Invoke(() =>
                    Open(
                        dispatcher,
                        issueId,
                        issueSummary,
                        defaultDate,
                        prefillMinutes,
                        types,
                        afterSave
                    )
                );
            },
            "Loading work item types failed"
        );
    }

    private static void Open(
        IDispatcher dispatcher,
        string issueId,
        string issueSummary,
        DateOnly defaultDate,
        int? prefillMinutes,
        IReadOnlyList<WorkItemType> types,
        Func<Task>? afterSave
    )
    {
        var dialog = new Dialog("Log time", 70, 18);

        var issueLabel = new Label($"Issue: {issueId}  {TextUtil.Truncate(issueSummary, 45)}")
        {
            X = 1,
            Y = 0,
        };
        var durationLabel = new Label("Duration:") { X = 1, Y = 2 };
        var durationField = new TextField(
            prefillMinutes is int minutes ? DurationFormat.ToPresentation(minutes) : ""
        )
        {
            X = 12,
            Y = 2,
            Width = 20,
        };
        var dateLabel = new Label("Date:") { X = 1, Y = 4 };
        var dateField = new TextField(defaultDate.ToString("yyyy-MM-dd"))
        {
            X = 12,
            Y = 4,
            Width = 20,
        };
        var typeLabel = new Label("Type:") { X = 1, Y = 6 };
        var typeNames = new List<string> { "" };
        typeNames.AddRange(types.Select(t => t.Name));
        var typeCombo = new ComboBox
        {
            X = 12,
            Y = 6,
            Width = 30,
            Height = 5,
        };
        typeCombo.SetSource(typeNames);
        typeCombo.SelectedItem = 0;
        var commentLabel = new Label("Comment:") { X = 1, Y = 8 };
        var commentField = new TextField("")
        {
            X = 12,
            Y = 8,
            Width = Dim.Fill(2),
        };
        var error = new Label("")
        {
            X = 1,
            Y = 10,
            Width = Dim.Fill(2),
        };

        var ok = new Button("Ok", is_default: true);
        var cancel = new Button("Cancel");
        ok.Clicked += () =>
        {
            if (!DurationFormat.TryParseMinutes(durationField.Text.ToString(), out var parsed))
            {
                error.Text = "Invalid duration (e.g. 1h 30m, 90m, 1.5h).";
                return;
            }
            if (!DateOnly.TryParseExact(dateField.Text.ToString(), "yyyy-MM-dd", out var date))
            {
                error.Text = "Invalid date (yyyy-MM-dd).";
                return;
            }
            var typeId =
                typeCombo.SelectedItem > 0 && typeCombo.SelectedItem <= types.Count
                    ? types[typeCombo.SelectedItem - 1].Id
                    : null;
            var comment = commentField.Text.ToString();
            error.Text = "Saving…";
            // A second Enter during the POST would book the work item twice.
            ok.Enabled = false;
            cancel.Enabled = false;
            UiRunner.Run(async () =>
            {
                try
                {
                    await dispatcher.SendAsync(
                        new CreateWorkItemCommand(
                            issueId,
                            date,
                            parsed,
                            typeId,
                            string.IsNullOrWhiteSpace(comment) ? null : comment
                        )
                    );
                    if (afterSave is not null)
                        await afterSave();
                    Application.MainLoop?.Invoke(() =>
                    {
                        Application.RequestStop();
                    });
                }
                catch (Exception ex)
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        error.Text = $"Create work item failed: {ex.Message}";
                        ok.Enabled = true;
                        cancel.Enabled = true;
                    });
                }
            });
        };
        cancel.Clicked += () => Application.RequestStop();

        dialog.Add(
            issueLabel,
            durationLabel,
            durationField,
            dateLabel,
            dateField,
            typeLabel,
            typeCombo,
            commentLabel,
            commentField,
            error
        );
        dialog.AddButton(ok);
        dialog.AddButton(cancel);
        Application.Run(dialog);
    }
}
