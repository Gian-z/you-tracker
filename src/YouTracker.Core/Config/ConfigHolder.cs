namespace YouTracker.Core.Config;

/// <summary>
/// Mutable holder for the current <see cref="AppConfig"/> so the settings UI can apply
/// config changes without a restart. Hosts register the holder as a singleton and
/// <see cref="AppConfig"/> as a transient resolving <see cref="Current"/>; services that
/// must see live changes are registered transient as well. The AI provider choice
/// (CLI vs. SDK) stays a startup decision — switching it requires a restart.
/// </summary>
public sealed class ConfigHolder(AppConfig initial)
{
    private volatile AppConfig _current = initial;

    public AppConfig Current => _current;

    public void Swap(AppConfig next) => _current = next;
}
