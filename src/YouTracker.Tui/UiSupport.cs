using Terminal.Gui;

namespace YouTracker.Tui;

/// <summary>Runs dispatcher work off the UI thread and marshals errors to a MessageBox.</summary>
internal static class UiRunner
{
    public static void Run(Func<Task> action, string errorTitle = "Error")
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Application.MainLoop?.Invoke(() =>
                    MessageBox.ErrorQuery(errorTitle, ex.Message, "Ok")
                );
            }
        });
    }
}

internal static class Dates
{
    /// <summary>Monday of the week containing <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}

internal static class TextUtil
{
    public static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? ""
        : text.Length <= max ? text
        : text[..(max - 1)] + "…";
}
