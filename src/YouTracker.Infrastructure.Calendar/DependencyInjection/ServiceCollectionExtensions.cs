using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.Calendar.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the ICS-feed IMeetingReader (empty result when no icsUrl configured).</summary>
    public static IServiceCollection AddYouTrackerCalendar(this IServiceCollection services)
    {
        services.AddSingleton<IMeetingReader>(sp => new IcsMeetingReader(
            new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
            sp.GetRequiredService<AppConfig>()
        ));
        return services;
    }
}
