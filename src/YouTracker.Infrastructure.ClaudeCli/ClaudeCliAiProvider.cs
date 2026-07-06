using System.Diagnostics;
using System.Text;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.ClaudeCli;

/// <summary>
/// IAiProvider backed by the local Claude Code CLI in headless mode (<c>claude -p --output-format json</c>).
/// Uses the user's existing Claude Code authentication — no Anthropic API key required.
/// The prompt is piped via stdin to avoid command-line length limits.
/// </summary>
public sealed class ClaudeCliAiProvider(AppConfig config) : IAiProvider
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromMinutes(5);

    public Task<string> CompleteTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default
    ) => RunAsync($"{systemPrompt}\n\n{userPrompt}", ct);

    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string jsonSchema,
        CancellationToken ct = default
    )
    {
        // The CLI has no structured-output parameter, so the schema is enforced by prompt
        // and the response is defensively unwrapped; Core validates the JSON afterwards.
        var prompt =
            $"{systemPrompt}\n\n{userPrompt}\n\n"
            + "## Output format\n"
            + "Respond with ONLY a single JSON object conforming to this JSON Schema. "
            + "No markdown fences, no commentary, no text before or after the JSON:\n"
            + jsonSchema;
        var raw = await RunAsync(prompt, ct).ConfigureAwait(false);
        return ClaudeCliOutput.ExtractJsonObject(raw);
    }

    private async Task<string> RunAsync(string prompt, CancellationToken ct)
    {
        var command = string.IsNullOrWhiteSpace(config.Anthropic.CliCommand)
            ? "claude"
            : config.Anthropic.CliCommand;
        const string cliArgs = "-p --output-format json";

        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        if (OperatingSystem.IsWindows())
        {
            // npm-installed CLIs are .cmd shims — resolve them through the shell.
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c {command} {cliArgs}";
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"{command} {cliArgs}");
        }

        using var process =
            Process.Start(psi)
            ?? throw new ClaudeCliException($"Failed to start the Claude CLI ('{command}').");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CliTimeout);

        try
        {
            await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new ClaudeCliException(
                    $"Claude CLI exited with code {process.ExitCode}: "
                        + $"{Truncate(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)} "
                        + $"(Is Claude Code installed and logged in, and '{command}' on PATH?)"
                );

            return ClaudeCliOutput.ExtractResult(stdout);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            KillQuietly(process);
            throw new ClaudeCliException(
                $"Claude CLI timed out after {CliTimeout.TotalMinutes:0} minutes."
            );
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort — the process may already have exited
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 400 ? text.Trim() : text[..400].Trim() + "…";
}
