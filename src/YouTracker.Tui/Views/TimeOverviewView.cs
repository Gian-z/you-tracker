using System.Data;
using System.Globalization;
using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Domain;
using YouTracker.Core.Metrics;

namespace YouTracker.Tui.Views;

/// <summary>F2: current week per-day booked/target/gap/fokus. ←/→ week, d today, r refresh, Enter gap fill.</summary>
public sealed class TimeOverviewView : View
{
    private readonly IDispatcher _dispatcher;
    private readonly Func<DateOnly> _today;
    private readonly Label _header;
    private readonly TableView _table;
    private readonly Label _footer;
    private DateOnly _weekStart;
    private TimeOverview? _overview;

    public TimeOverviewView(IDispatcher dispatcher, Func<DateOnly> today)
    {
        _dispatcher = dispatcher;
        _today = today;
        _weekStart = Dates.WeekStart(today());
        Width = Dim.Fill();
        Height = Dim.Fill();

        _header = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };
        _table = new TableView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            FullRowSelect = true,
        };
        _footer = new Label("")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
        };
        _table.KeyPress += OnTableKeyPress;
        _table.CellActivated += OnCellActivated;
        Add(_header, _table, _footer);
    }

    public void Load(bool bypassCache = false)
    {
        var from = _weekStart;
        var to = _weekStart.AddDays(6);
        UiRunner.Run(
            async () =>
            {
                var overview = await _dispatcher.QueryAsync(
                    new GetTimeOverviewQuery(from, to, bypassCache)
                );
                Application.MainLoop?.Invoke(() => SetData(overview));
            },
            "Loading time overview failed"
        );
    }

    private void SetData(TimeOverview overview)
    {
        _overview = overview;
        _header.Text =
            $"Week {overview.From:yyyy-MM-dd} .. {overview.To:yyyy-MM-dd}   "
            + "(←/→ week, d today, r refresh, Enter on gap day: AI gap fill)";

        var table = new DataTable();
        table.Columns.Add("Date");
        table.Columns.Add("Booked");
        table.Columns.Add("Target");
        table.Columns.Add("Gap");
        table.Columns.Add("Fokus");
        foreach (var day in overview.Days)
        {
            table.Rows.Add(
                day.Date.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture),
                DurationFormat.ToPresentation(day.BookedMinutes),
                DurationFormat.ToPresentation(day.TargetMinutes),
                day.GapMinutes > 0 ? $"⚠ {DurationFormat.ToPresentation(day.GapMinutes)}" : "-",
                day.FokusScore?.ToString() ?? "-"
            );
        }

        var selected = _table.SelectedRow;
        _table.Table = table;
        if (table.Rows.Count > 0)
            _table.SelectedRow = Math.Clamp(selected, 0, table.Rows.Count - 1);

        _footer.Text =
            $"Week total: {DurationFormat.ToPresentation(overview.TotalBookedMinutes)} / "
            + $"{DurationFormat.ToPresentation(overview.TotalTargetMinutes)}   "
            + $"Ø Fokus-Score: {overview.AverageFokusScore?.ToString() ?? "-"}";
    }

    private void OnTableKeyPress(View.KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case Key.CursorLeft:
                _weekStart = _weekStart.AddDays(-7);
                Load();
                e.Handled = true;
                break;
            case Key.CursorRight:
                _weekStart = _weekStart.AddDays(7);
                Load();
                e.Handled = true;
                break;
            case (Key)'d':
                _weekStart = Dates.WeekStart(_today());
                Load();
                e.Handled = true;
                break;
            case (Key)'r':
                Load(bypassCache: true);
                e.Handled = true;
                break;
        }
    }

    private void OnCellActivated(TableView.CellActivatedEventArgs args)
    {
        if (_overview is null || args.Row < 0 || args.Row >= _overview.Days.Count)
            return;
        var day = _overview.Days[args.Row];
        if (day.GapMinutes <= 0)
            return;

        var from = _overview.From;
        var to = _overview.To;
        UiRunner.Run(
            async () =>
            {
                var result = await _dispatcher.QueryAsync(new SuggestGapFillsQuery(from, to));
                Application.MainLoop?.Invoke(() => DraftReviewDialog.Show(_dispatcher, result));
            },
            "Gap fill suggestion failed"
        );
    }
}
