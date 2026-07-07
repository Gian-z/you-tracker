using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

[assembly: InternalsVisibleTo("YouTracker.Core.Tests")]

namespace YouTracker.Infrastructure.Git;

/// <summary>
/// Reads the user's commits from local clones: every git repository found under the configured
/// scan roots is queried with <c>git log --all</c> for the period. Repos that fail (no git,
/// corrupt, empty) are skipped - activity is best-effort context, never load-bearing.
/// </summary>
public sealed class GitCommitActivityReader(AppConfig config) : ICommitActivityReader
{
    private const int MaxCommits = 300;
    private static readonly TimeSpan PerRepoTimeout = TimeSpan.FromSeconds(20);

    private string? _author;

    public async Task<IReadOnlyList<CommitActivity>> GetCommitsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    )
    {
        var roots = config.Git?.ScanRoots ?? [];
        if (roots.Count == 0)
            return Array.Empty<CommitActivity>();

        var author = await GetAuthorAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(author))
            return Array.Empty<CommitActivity>();

        var commits = new List<CommitActivity>();
        foreach (var repo in DiscoverRepositories(roots))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var stdout = await RunGitAsync(
                        repo,
                        "log --all --no-merges "
                            + $"--since={from:yyyy-MM-dd}T00:00:00 --until={to.AddDays(1):yyyy-MM-dd}T00:00:00 "
                            + $"--author=\"{author}\" --pretty=format:%H%x1f%aI%x1f%s%x1e",
                        ct
                    )
                    .ConfigureAwait(false);
                commits.AddRange(ParseGitLog(stdout, Path.GetFileName(repo)));
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // best effort - skip repos git can't read
            }
        }

        return [.. commits.DistinctBy(c => c.Sha).OrderBy(c => c.Timestamp).Take(MaxCommits)];
    }

    /// <summary>Directories containing a `.git` entry, up to two levels below each root.</summary>
    internal static IReadOnlyList<string> DiscoverRepositories(IReadOnlyList<string> roots)
    {
        var repos = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            if (Directory.Exists(Path.Combine(root, ".git")))
            {
                repos.Add(root);
                continue;
            }
            foreach (var level1 in SafeSubdirectories(root))
            {
                if (Directory.Exists(Path.Combine(level1, ".git")))
                {
                    repos.Add(level1);
                    continue;
                }
                repos.AddRange(
                    SafeSubdirectories(level1)
                        .Where(level2 => Directory.Exists(Path.Combine(level2, ".git")))
                );
            }
        }
        return repos;
    }

    /// <summary>Parses `%H U+001F %aI U+001F %s U+001E`-formatted git log output.</summary>
    internal static IReadOnlyList<CommitActivity> ParseGitLog(string stdout, string repoName)
    {
        var commits = new List<CommitActivity>();
        foreach (var record in stdout.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Trim('\n', '\r', ' ').Split('\u001f');
            if (fields.Length < 3 || fields[0].Length < 7)
                continue;
            if (!DateTimeOffset.TryParse(fields[1], out var timestamp))
                continue;
            commits.Add(new CommitActivity(fields[0], timestamp, repoName, fields[2].Trim()));
        }
        return commits;
    }

    private async Task<string?> GetAuthorAsync(CancellationToken ct)
    {
        if (_author is not null)
            return _author;
        if (!string.IsNullOrWhiteSpace(config.Git?.Author))
            return _author = config.Git.Author;
        try
        {
            _author = (
                await RunGitAsync(null, "config --global user.email", ct).ConfigureAwait(false)
            ).Trim();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _author = "";
        }
        return _author;
    }

    private static async Task<string> RunGitAsync(
        string? workingDirectory,
        string arguments,
        CancellationToken ct
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = workingDirectory is null
                ? arguments
                : $"-C \"{workingDirectory}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(PerRepoTimeout);
        try
        {
            // stderr must be drained concurrently: a chatty git (e.g. corrupt repo warnings)
            // fills the pipe buffer and blocks before ever closing stdout.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdout = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git exited with {process.ExitCode}");
            return stdout;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
            throw;
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception)
        {
            return [];
        }
    }
}
