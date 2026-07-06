namespace YouTracker.Infrastructure.Anthropic;

/// <summary>
/// Wraps Anthropic SDK failures so Core and the host never depend on SDK exception types.
/// </summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string message)
        : base(message) { }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException) { }
}
