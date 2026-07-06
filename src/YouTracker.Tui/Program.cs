using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Application;
using YouTracker.Core.Config;
using YouTracker.Core.DependencyInjection;
using YouTracker.Core.Domain;
using YouTracker.Infrastructure.Anthropic.DependencyInjection;
using YouTracker.Infrastructure.Storage.DependencyInjection;
using YouTracker.Infrastructure.YouTrack.DependencyInjection;

namespace YouTracker.Tui;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Config bootstrap: only the Storage module knows where/how config lives.
        var bootstrap = new ServiceCollection().AddYouTrackerStorage();
        using var bootstrapProvider = bootstrap.BuildServiceProvider();
        var configStore = bootstrapProvider.GetService<IConfigStore>();
        if (configStore is null)
        {
            Console.Error.WriteLine("IConfigStore is not registered by the Storage module.");
            return 2;
        }

        if (!configStore.Exists)
        {
            Console.WriteLine($"Config missing. Create {configStore.ConfigPath} with:");
            Console.WriteLine(configStore.Template);
            return 1;
        }

        var config = configStore.Load();

        var services = new ServiceCollection();
        services.AddYouTrackerCore();
        services.AddYouTrackerYouTrack();
        services.AddYouTrackerStorage();
        services.AddYouTrackerAnthropic();
        services.AddSingleton(config);
        using var provider = services.BuildServiceProvider();

        if (args.Contains("--check"))
            return RunCheck(provider, config);

        var app = new App(
            provider.GetRequiredService<IDispatcher>(),
            provider.GetRequiredService<IEventBus>(),
            config
        );
        app.Run();
        return 0;
    }

    /// <summary>Non-interactive smoke test: query issues + current week overview, print, exit.</summary>
    private static int RunCheck(IServiceProvider provider, AppConfig config)
    {
        try
        {
            RunCheckAsync(provider, config).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static async Task RunCheckAsync(IServiceProvider provider, AppConfig config)
    {
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var today = config.Today(TimeProvider.System);
        var monday = Dates.WeekStart(today);
        var sunday = monday.AddDays(6);

        var issues = await dispatcher.QueryAsync(new GetMyOpenIssuesQuery());
        var overview = await dispatcher.QueryAsync(new GetTimeOverviewQuery(monday, sunday));

        Console.WriteLine($"Open issues: {issues.Count}");
        foreach (var issue in issues.Take(10))
            Console.WriteLine($"{issue.IssueId, -12} {issue.State ?? "-", -18} {issue.Summary}");

        Console.WriteLine();
        Console.WriteLine($"Week {monday:yyyy-MM-dd} .. {sunday:yyyy-MM-dd}");
        foreach (var day in overview.Days)
        {
            var gap =
                day.GapMinutes > 0 ? $"  gap {DurationFormat.ToPresentation(day.GapMinutes)}" : "";
            var fokus = day.FokusScore is int score ? $"  fokus {score}" : "";
            Console.WriteLine(
                $"{day.Date.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}  "
                    + $"booked {DurationFormat.ToPresentation(day.BookedMinutes), -8} / "
                    + $"target {DurationFormat.ToPresentation(day.TargetMinutes), -8}{gap}{fokus}"
            );
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Total: {DurationFormat.ToPresentation(overview.TotalBookedMinutes)} / "
                + $"{DurationFormat.ToPresentation(overview.TotalTargetMinutes)}  "
                + $"Fokus-Score: {overview.AverageFokusScore?.ToString() ?? "-"}"
        );
    }
}
