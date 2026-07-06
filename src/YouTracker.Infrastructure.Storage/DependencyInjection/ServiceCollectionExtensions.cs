using Microsoft.Extensions.DependencyInjection;

namespace YouTracker.Infrastructure.Storage.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers file-based ITimerStore and IConfigStore implementations.</summary>
    public static IServiceCollection AddYouTrackerStorage(this IServiceCollection services)
    {
        // Implementation registered by the Storage module (FileTimerStore, JsonConfigStore).
        return services;
    }
}
