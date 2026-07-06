using System.Text;
using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.ReadModels;

namespace YouTracker.Tui.Views;

/// <summary>F3: free-text AI assistant. Drafts always go through the DraftReviewDialog confirm gate.</summary>
public sealed class AiAssistantView : View
{
    private readonly IDispatcher _dispatcher;
    private readonly Func<DateOnly> _today;
    private readonly TextView _input;
    private readonly TextView _output;

    public AiAssistantView(IDispatcher dispatcher, Func<DateOnly> today)
    {
        _dispatcher = dispatcher;
        _today = today;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var inputFrame = new FrameView("Free text (what did you work on?)")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 8,
        };
        _input = new TextView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true,
        };
        inputFrame.Add(_input);

        var draftButton = new Button("Draft work log") { X = 0, Y = 8 };
        draftButton.Clicked += DraftWorkLog;
        var gapsButton = new Button("Fill gaps") { X = Pos.Right(draftButton) + 1, Y = 8 };
        gapsButton.Clicked += FillGaps;
        var dayButton = new Button("Summarize day") { X = Pos.Right(gapsButton) + 1, Y = 8 };
        dayButton.Clicked += () => Summarize(_today(), _today());
        var weekButton = new Button("Summarize week") { X = Pos.Right(dayButton) + 1, Y = 8 };
        weekButton.Clicked += () =>
        {
            var start = Dates.WeekStart(_today());
            Summarize(start, start.AddDays(6));
        };
        var triageButton = new Button("Triage") { X = Pos.Right(weekButton) + 1, Y = 8 };
        triageButton.Clicked += Triage;

        var outputFrame = new FrameView("Result")
        {
            X = 0,
            Y = 10,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _output = new TextView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
        };
        outputFrame.Add(_output);

        Add(inputFrame, draftButton, gapsButton, dayButton, weekButton, triageButton, outputFrame);
    }

    private void SetOutput(string text)
    {
        _output.Text = text;
        _output.SetNeedsDisplay();
    }

    private void DraftWorkLog()
    {
        var text = _input.Text.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Query(
                "Draft work log",
                "Enter some free text describing your work first.",
                "Ok"
            );
            return;
        }

        var date = _today();
        SetOutput("Drafting work log…");
        UiRunner.Run(
            async () =>
            {
                var result = await _dispatcher.QueryAsync(new DraftWorkLogQuery(text, date));
                Application.MainLoop?.Invoke(() =>
                {
                    SetOutput(
                        $"{result.Drafts.Count} draft(s), {result.Unmatched.Count} unmatched."
                    );
                    DraftReviewDialog.Show(_dispatcher, result);
                });
            },
            "Draft work log failed"
        );
    }

    private void FillGaps()
    {
        var from = Dates.WeekStart(_today());
        var to = from.AddDays(6);
        SetOutput($"Suggesting gap fills for {from:yyyy-MM-dd} .. {to:yyyy-MM-dd}…");
        UiRunner.Run(
            async () =>
            {
                var result = await _dispatcher.QueryAsync(new SuggestGapFillsQuery(from, to));
                Application.MainLoop?.Invoke(() =>
                {
                    SetOutput(
                        $"{result.Drafts.Count} draft(s), {result.Unmatched.Count} unmatched."
                    );
                    DraftReviewDialog.Show(_dispatcher, result);
                });
            },
            "Gap fill suggestion failed"
        );
    }

    private void Summarize(DateOnly from, DateOnly to)
    {
        SetOutput($"Summarizing {from:yyyy-MM-dd} .. {to:yyyy-MM-dd}…");
        UiRunner.Run(
            async () =>
            {
                var summary = await _dispatcher.QueryAsync(new SummarizePeriodQuery(from, to));
                Application.MainLoop?.Invoke(() => SetOutput(summary));
            },
            "Summarize failed"
        );
    }

    private void Triage()
    {
        SetOutput("Triaging open issues…");
        UiRunner.Run(
            async () =>
            {
                var result = await _dispatcher.QueryAsync(new TriageIssuesQuery());
                Application.MainLoop?.Invoke(() => SetOutput(RenderTriage(result)));
            },
            "Triage failed"
        );
    }

    private static string RenderTriage(TriageResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Focus: {result.FocusSuggestion}");
        sb.AppendLine();
        foreach (var issue in result.Ranked.OrderBy(t => t.Rank))
        {
            sb.AppendLine($"{issue.Rank, 2}. [{issue.Score, 3}] {issue.IssueId}  {issue.Summary}");
            if (issue.Reasons.Count > 0)
                sb.AppendLine($"      {string.Join("; ", issue.Reasons)}");
        }
        return sb.ToString();
    }
}
