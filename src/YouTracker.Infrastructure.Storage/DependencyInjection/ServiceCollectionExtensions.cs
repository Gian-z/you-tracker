using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Storage.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers file-based ITimerStore and IConfigStore implementations.</summary>
    public static IServiceCollection AddYouTrackerStorage(this IServiceCollection services)
    {
        services.AddSingleton<IConfigStore>(_ => new JsonConfigStore());
        services.AddSingleton<ITimerStore>(_ => new FileTimerStore());
        return services;
    }
}
