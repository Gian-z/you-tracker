using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.ClaudeCli.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Claude Code CLI-backed IAiProvider (no API key required).</summary>
    public static IServiceCollection AddYouTrackerClaudeCli(this IServiceCollection services)
    {
        // Transient so a settings-dialog change (cliCommand) takes effect live.
        services.AddTransient<IAiProvider, ClaudeCliAiProvider>();
        return services;
    }
}
