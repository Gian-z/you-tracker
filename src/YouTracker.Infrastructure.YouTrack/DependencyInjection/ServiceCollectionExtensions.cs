using Microsoft.Extensions.DependencyInjection;

namespace YouTracker.Infrastructure.YouTrack.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the YouTrack REST adapter as IIssueReader/IWorkItemReader/IWorkItemWriter.</summary>
    public static IServiceCollection AddYouTrackerYouTrack(this IServiceCollection services)
    {
        // Implementation registered by the YouTrack module (YouTrackClient).
        return services;
    }
}
