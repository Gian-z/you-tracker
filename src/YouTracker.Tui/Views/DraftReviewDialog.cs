using System.Data;
using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Domain;
using YouTracker.Core.ReadModels;

namespace YouTracker.Tui.Views;

/// <summary>
/// Modal confirm-before-write gate for AI drafts: only checked rows are committed
/// via CommitWorkLogDraftsCommand. Space toggles, e edits minutes/comment.
/// </summary>
public static class DraftReviewDialog
{
    public static void Show(IDispatcher dispatcher, WorkLogDraftResult result)
    {
        if (result.Drafts.Count == 0)
        {
            var info =
                result.Unmatched.Count == 0
                    ? "No drafts proposed."
                    : "No drafts proposed.\n\nUnmatched:\n" + RenderUnmatched(result.Unmatched);
            MessageBox.Query("Work log drafts", info, "Ok");
            return;
        }

        var drafts = result.Drafts.ToList();
        var isChecked = Enumerable.Repeat(true, drafts.Count).ToList();

        var dialog = new Dialog("Review work log drafts (Space: toggle, e: edit)", 110, 32);

        var table = new TableView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(55),
            FullRowSelect = true,
        };
        table.Table = BuildTable(drafts, isChecked);
        table.KeyPress += e =>
        {
            var row = table.SelectedRow;
            if (row < 0 || row >= drafts.Count)
                return;
            if (e.KeyEvent.Key == Key.Space)
            {
                isChecked[row] = !isChecked[row];
                table.Table.Rows[row][0] = isChecked[row] ? "[x]" : "[ ]";
                table.Update();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key)'e')
            {
                EditDraft(drafts, row);
                table.Table = BuildTable(drafts, isChecked);
                table.SelectedRow = row;
                e.Handled = true;
            }
        };

        var unmatchedFrame = new FrameView("Unmatched")
        {
            X = 0,
            Y = Pos.Bottom(table),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        unmatchedFrame.Add(
            new TextView
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = true,
                Text = result.Unmatched.Count == 0 ? "(none)" : RenderUnmatched(result.Unmatched),
            }
        );

        var commit = new Button("Commit", is_default: true);
        var cancel = new Button("Cancel");
        commit.Clicked += () =>
        {
            var selected = drafts.Where((_, i) => isChecked[i]).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Query("Commit", "No drafts checked.", "Ok");
                return;
            }
            // A second Enter during the commit would book every checked draft twice.
            commit.Enabled = false;
            cancel.Enabled = false;
            UiRunner.Run(async () =>
            {
                try
                {
                    var commitResult = await dispatcher.SendAsync(
                        new CommitWorkLogDraftsCommand(selected, null)
                    );
                    Application.MainLoop?.Invoke(() =>
                    {
                        var message = $"Created {commitResult.Created} work item(s).";
                        if (commitResult.Errors.Count > 0)
                            message += "\nErrors:\n" + string.Join("\n", commitResult.Errors);
                        MessageBox.Query("Commit result", message, "Ok");
                        Application.RequestStop();
                    });
                }
                catch (Exception ex)
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        MessageBox.ErrorQuery("Commit failed", ex.Message, "Ok");
                        commit.Enabled = true;
                        cancel.Enabled = true;
                    });
                }
            });
        };
        cancel.Clicked += () => Application.RequestStop();

        dialog.Add(table, unmatchedFrame);
        dialog.AddButton(commit);
        dialog.AddButton(cancel);
        Application.Run(dialog);
    }

    private static string RenderUnmatched(IReadOnlyList<UnmatchedActivity> unmatched) =>
        string.Join("\n", unmatched.Select(u => $"- {u.Text} ({u.Reason})"));

    private static DataTable BuildTable(List<WorkLogDraft> drafts, List<bool> isChecked)
    {
        var table = new DataTable();
        table.Columns.Add("[x]");
        table.Columns.Add("Issue");
        table.Columns.Add("Date");
        table.Columns.Add("Min");
        table.Columns.Add("Type");
        table.Columns.Add("Comment");
        table.Columns.Add("Conf");
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            table.Rows.Add(
                isChecked[i] ? "[x]" : "[ ]",
                $"{draft.IssueId} {TextUtil.Truncate(draft.IssueSummary, 25)}".TrimEnd(),
                draft.Date.ToString("yyyy-MM-dd"),
                DurationFormat.ToPresentation(draft.Minutes),
                draft.WorkTypeName ?? "",
                TextUtil.Truncate(draft.Comment, 40),
                draft.Confidence
            );
        }
        return table;
    }

    private static void EditDraft(List<WorkLogDraft> drafts, int row)
    {
        var draft = drafts[row];
        var dialog = new Dialog($"Edit {draft.IssueId}", 60, 12);
        var durationLabel = new Label("Duration:") { X = 1, Y = 1 };
        var durationField = new TextField(DurationFormat.ToPresentation(draft.Minutes))
        {
            X = 11,
            Y = 1,
            Width = 20,
        };
        var commentLabel = new Label("Comment:") { X = 1, Y = 3 };
        var commentField = new TextField(draft.Comment ?? "")
        {
            X = 11,
            Y = 3,
            Width = Dim.Fill(2),
        };
        var error = new Label("")
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill(2),
        };

        var ok = new Button("Ok", is_default: true);
        ok.Clicked += () =>
        {
            if (!DurationFormat.TryParseMinutes(durationField.Text.ToString(), out var minutes))
            {
                error.Text = "Invalid duration (e.g. 1h 30m, 90m, 1.5h).";
                return;
            }
            var comment = commentField.Text.ToString();
            drafts[row] = draft with
            {
                Minutes = minutes,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
            };
            Application.RequestStop();
        };
        var cancel = new Button("Cancel");
        cancel.Clicked += () => Application.RequestStop();

        dialog.Add(durationLabel, durationField, commentLabel, commentField, error);
        dialog.AddButton(ok);
        dialog.AddButton(cancel);
        Application.Run(dialog);
    }
}
