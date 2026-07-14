using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.Calendar.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ICS-feed IMeetingReader (empty result when no icsUrl configured).
    /// Transient (shared HttpClient) so a settings-dialog config change takes effect live.
    /// </summary>
    public static IServiceCollection AddYouTrackerCalendar(this IServiceCollection services)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        services.AddTransient<IMeetingReader>(sp => new IcsMeetingReader(
            http,
            sp.GetRequiredService<AppConfig>()
        ));
        return services;
    }
}
