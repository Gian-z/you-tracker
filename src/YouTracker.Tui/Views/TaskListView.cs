using System.Data;
using System.Diagnostics;
using Terminal.Gui;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.ReadModels;

namespace YouTracker.Tui.Views;

/// <summary>F1: my open issues. r refresh, o open in browser, t start timer, Enter log time, / filter.</summary>
public sealed class TaskListView : View
{
    private readonly IDispatcher _dispatcher;
    private readonly TextField _filter;
    private readonly TableView _table;
    private IReadOnlyList<TaskListItem> _all = Array.Empty<TaskListItem>();
    private List<TaskListItem> _visible = new();

    public event Action<TaskListItem>? StartTimerRequested;
    public event Action<TaskListItem>? LogTimeRequested;

    public TaskListView(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var filterLabel = new Label("Filter (/):") { X = 0, Y = 0 };
        _filter = new TextField("")
        {
            X = Pos.Right(filterLabel) + 1,
            Y = 0,
            Width = Dim.Fill(),
        };
        _filter.TextChanged += _ => ApplyFilter();

        _table = new TableView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
        };
        _table.CellActivated += _ =>
        {
            if (SelectedItem is { } item)
                LogTimeRequested?.Invoke(item);
        };
        _table.KeyPress += OnTableKeyPress;

        Add(filterLabel, _filter, _table);
    }

    public TaskListItem? SelectedItem =>
        _table.SelectedRow >= 0 && _table.SelectedRow < _visible.Count
            ? _visible[_table.SelectedRow]
            : null;

    public void Load(bool bypassCache = false) =>
        UiRunner.Run(
            async () =>
            {
                var items = await _dispatcher.QueryAsync(new GetMyOpenIssuesQuery(bypassCache));
                Application.MainLoop?.Invoke(() =>
                {
                    _all = items;
                    ApplyFilter();
                });
            },
            "Loading tasks failed"
        );

    private void OnTableKeyPress(View.KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case (Key)'r':
                Load(bypassCache: true);
                e.Handled = true;
                break;
            case (Key)'o':
                OpenSelectedInBrowser();
                e.Handled = true;
                break;
            case (Key)'t':
                if (SelectedItem is { } item)
                    StartTimerRequested?.Invoke(item);
                e.Handled = true;
                break;
            case (Key)'/':
                _filter.SetFocus();
                e.Handled = true;
                break;
        }
    }

    private void OpenSelectedInBrowser()
    {
        if (SelectedItem is not { } item)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(item.WebUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Open issue", ex.Message, "Ok");
        }
    }

    private void ApplyFilter()
    {
        var filter = _filter.Text?.ToString() ?? "";
        _visible = _all.Where(i =>
                filter.Length == 0
                || i.IssueId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || i.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (i.State?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            )
            .ToList();

        var table = new DataTable();
        table.Columns.Add("ID");
        table.Columns.Add("Summary");
        table.Columns.Add("Type");
        table.Columns.Add("State");
        table.Columns.Add("Prio");
        table.Columns.Add("Est");
        table.Columns.Add("Spent");
        foreach (var i in _visible)
        {
            table.Rows.Add(
                i.IssueId,
                TextUtil.Truncate(i.Summary, 60),
                i.Type ?? "",
                i.State ?? "",
                i.Priority ?? "",
                i.Estimate ?? "",
                i.Spent ?? ""
            );
        }

        var selected = _table.SelectedRow;
        _table.Table = table;
        if (table.Rows.Count > 0)
            _table.SelectedRow = Math.Clamp(selected, 0, table.Rows.Count - 1);
    }
}
