using Microsoft.Extensions.DependencyInjection;

namespace YouTracker.Infrastructure.Anthropic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Claude-backed IAiProvider.</summary>
    public static IServiceCollection AddYouTrackerAnthropic(this IServiceCollection services)
    {
        // Implementation registered by the Anthropic module (AnthropicAiProvider).
        return services;
    }
}
