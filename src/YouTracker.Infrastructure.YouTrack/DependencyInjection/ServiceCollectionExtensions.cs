using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.YouTrack.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the YouTrack REST adapter as IIssueReader/IWorkItemReader/IWorkItemWriter.</summary>
    public static IServiceCollection AddYouTrackerYouTrack(this IServiceCollection services)
    {
        services.AddSingleton<YouTrackClient>(sp => new YouTrackClient(
            new HttpClient(),
            sp.GetRequiredService<AppConfig>()
        ));
        services.AddSingleton<IIssueReader>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddSingleton<IWorkItemReader>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddSingleton<IWorkItemWriter>(sp => sp.GetRequiredService<YouTrackClient>());
        services.AddSingleton<IUserDirectory>(sp => sp.GetRequiredService<YouTrackClient>());
        return services;
    }
}
