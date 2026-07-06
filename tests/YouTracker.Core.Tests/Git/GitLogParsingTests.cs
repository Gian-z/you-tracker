using YouTracker.Infrastructure.Git;

namespace YouTracker.Core.Tests.Git;

public sealed class GitLogParsingTests
{
    private const char Unit = '';
    private const char Record = '';

    [Fact]
    public void ParseGitLog_maps_records_and_skips_malformed()
    {
        var stdout =
            $"abc1234{Unit}2026-07-03T09:15:00+02:00{Unit}feat(api): Add import handler [XBOX-549]{Record}"
            + $"\ndef5678{Unit}2026-07-03T16:40:00+02:00{Unit}fix: retry [ST6-191]{Record}"
            + $"\nbroken-record-without-separators{Record}"
            + $"\nghi9012{Unit}not-a-date{Unit}whatever{Record}";

        var commits = GitCommitActivityReader.ParseGitLog(stdout, "cmi-api");

        Assert.Equal(2, commits.Count);
        Assert.Equal("abc1234", commits[0].Sha);
        Assert.Equal("cmi-api", commits[0].Repo);
        Assert.Equal("feat(api): Add import handler [XBOX-549]", commits[0].Message);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 3, 9, 15, 0, TimeSpan.FromHours(2)),
            commits[0].Timestamp
        );
    }

    [Fact]
    public void ParseGitLog_empty_output_yields_no_commits()
    {
        Assert.Empty(GitCommitActivityReader.ParseGitLog("", "repo"));
    }

    [Fact]
    public void DiscoverRepositories_finds_nested_git_dirs_and_ignores_missing_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "repo-a", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "group", "repo-b", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "not-a-repo"));

            var repos = GitCommitActivityReader.DiscoverRepositories(
                [root, Path.Combine(root, "does-not-exist")]
            );

            Assert.Equal(2, repos.Count);
            Assert.Contains(repos, r => r.EndsWith("repo-a"));
            Assert.Contains(repos, r => r.EndsWith("repo-b"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
