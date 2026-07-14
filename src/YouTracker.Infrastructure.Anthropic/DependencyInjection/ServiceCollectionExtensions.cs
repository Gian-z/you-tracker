using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Anthropic.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Claude-backed IAiProvider. AppConfig is provided by the host.</summary>
    public static IServiceCollection AddYouTrackerAnthropic(this IServiceCollection services)
    {
        // Transient so a settings-dialog change (model/key) takes effect live.
        services.AddTransient<IAiProvider, AnthropicAiProvider>();
        return services;
    }
}
