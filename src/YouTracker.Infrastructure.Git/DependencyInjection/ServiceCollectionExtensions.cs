using Microsoft.Extensions.DependencyInjection;
using YouTracker.Core.Abstractions;

namespace YouTracker.Infrastructure.Git.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the local-git ICommitActivityReader (no-op when no scan roots configured).</summary>
    public static IServiceCollection AddYouTrackerGit(this IServiceCollection services)
    {
        // Transient so a settings-dialog config change (scan roots) takes effect live.
        services.AddTransient<ICommitActivityReader, GitCommitActivityReader>();
        return services;
    }
}
