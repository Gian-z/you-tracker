namespace YouTracker.Infrastructure.ClaudeCli;

public sealed class ClaudeCliException : Exception
{
    public ClaudeCliException(string message)
        : base(message) { }

    public ClaudeCliException(string message, Exception inner)
        : base(message, inner) { }
}
