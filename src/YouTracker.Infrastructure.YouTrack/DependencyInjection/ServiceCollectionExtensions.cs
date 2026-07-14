using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.YouTrack.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the YouTrack REST adapter as IIssueReader/IWorkItemReader/IWorkItemWriter.
    /// Transient (with one shared HttpClient) so hosts that register AppConfig per-resolve
    /// (settings dialog live-reload) always hit the current base URL/token.
    /// </summary>
    public static IServiceCollection AddYouTrackerYouTrack(this IServiceCollection services)
    {
        var http = new HttpClient();
        services.AddTransient<YouTrackClient>(sp => new YouTrackClient(
            http,
            sp.GetRequiredService<AppConfig>()
        ));
        services.AddTransient<IIssueReader>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddTransient<IWorkItemReader>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddTransient<IWorkItemWriter>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddTransient<IUserDirectory>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddTransient<ISprintReader>(sp => sp.GetRequiredService<YouTrackClient>());
        return services;
    }
}
