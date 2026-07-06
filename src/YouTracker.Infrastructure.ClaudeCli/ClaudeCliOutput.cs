using System.Text.Json;

namespace YouTracker.Infrastructure.ClaudeCli;

/// <summary>Parsing helpers for the Claude CLI JSON envelope and model output.</summary>
public static class ClaudeCliOutput
{
    /// <summary>
    /// Extracts the assistant text from the CLI's <c>--output-format json</c> envelope:
    /// <c>{"type":"result","subtype":"success","is_error":false,"result":"...",...}</c>.
    /// </summary>
    public static string ExtractResult(string stdout)
    {
        var envelope = ParseEnvelope(stdout);

        if (
            envelope.TryGetProperty("is_error", out var isError)
            && isError.ValueKind == JsonValueKind.True
        )
            throw new ClaudeCliException(
                $"Claude CLI reported an error: {envelope.GetRawText()[..Math.Min(400, envelope.GetRawText().Length)]}"
            );

        if (
            !envelope.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(result.GetString())
        )
            throw new ClaudeCliException("Claude CLI returned an empty result.");

        return result.GetString()!;
    }

    /// <summary>
    /// Unwraps a JSON object from model text: strips markdown fences and any prose
    /// before/after the outermost <c>{...}</c>.
    /// </summary>
    public static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            return trimmed;

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        throw new ClaudeCliException(
            $"Claude CLI response did not contain a JSON object: {trimmed[..Math.Min(200, trimmed.Length)]}"
        );
    }

    private static JsonElement ParseEnvelope(string stdout)
    {
        var text = stdout.Trim();
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            // Some shells prepend noise lines; fall back to the last line that parses.
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                try
                {
                    return JsonDocument.Parse(line.Trim()).RootElement.Clone();
                }
                catch (JsonException)
                {
                    // keep scanning
                }
            }

            throw new ClaudeCliException(
                $"Claude CLI produced unparseable output: {text[..Math.Min(200, text.Length)]}"
            );
        }
    }
}
