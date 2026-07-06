using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Anthropic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Claude-backed IAiProvider. AppConfig is provided by the host.</summary>
    public static IServiceCollection AddYouTrackerAnthropic(this IServiceCollection services)
    {
        services.AddSingleton<IAiProvider, AnthropicAiProvider>();
        return services;
    }
}
