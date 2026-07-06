using YouTracker.Infrastructure.Storage;

namespace YouTracker.Core.Tests.Storage;

public sealed class JsonConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public JsonConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private JsonConfigStore StoreWith(string json)
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"), json);
        return new JsonConfigStore(_dir);
    }

    /// <summary>Runs the action with the env var temporarily set (null = unset), restoring it afterwards.</summary>
    private static void WithEnvVar(string name, string? value, Action action)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    [Fact]
    public void Load_reads_a_full_config_file()
    {
        var store = StoreWith(
            """
            {
              "youTrack": { "baseUrl": "https://yt.example.com/youtrack", "webBaseUrl": "https://yt.example.com", "token": "perm:file-token" },
              "anthropic": { "apiKey": "sk-ant-file", "model": "claude-haiku-4-5" },
              "workday": { "targetHours": 6.5, "timezone": "Europe/Berlin", "inProgressStates": ["Doing"] }
            }
            """
        );

        WithEnvVar(
            "YOUTRACK_TOKEN",
            null,
            () =>
                WithEnvVar(
                    "ANTHROPIC_API_KEY",
                    null,
                    () =>
                    {
                        Assert.True(store.Exists);
                        var config = store.Load();
                        Assert.Equal("https://yt.example.com/youtrack", config.YouTrack.BaseUrl);
                        Assert.Equal("https://yt.example.com", config.YouTrack.WebBaseUrl);
                        Assert.Equal("perm:file-token", config.YouTrack.Token);
                        Assert.Equal("sk-ant-file", config.Anthropic.ApiKey);
                        Assert.Equal("claude-haiku-4-5", config.Anthropic.Model);
                        Assert.Equal(6.5, config.Workday.TargetHours);
                        Assert.Equal("Europe/Berlin", config.Workday.Timezone);
                        Assert.Equal(["Doing"], config.Workday.InProgressStates);
                    }
                )
        );
    }

    [Fact]
    public void Environment_variable_overrides_token_from_file()
    {
        var store = StoreWith(
            """
            {
              "youTrack": { "baseUrl": "https://yt.example.com/youtrack", "webBaseUrl": "https://yt.example.com", "token": "perm:file-token" },
              "anthropic": { "apiKey": "sk-ant-file" }
            }
            """
        );

        WithEnvVar(
            "YOUTRACK_TOKEN",
            "perm:env-token",
            () =>
            {
                var config = store.Load();
                Assert.Equal("perm:env-token", config.YouTrack.Token);
            }
        );
    }

    [Fact]
    public void Missing_token_without_env_var_throws_naming_field_and_path()
    {
        var store = StoreWith(
            """
            {
              "youTrack": { "baseUrl": "https://yt.example.com/youtrack", "webBaseUrl": "https://yt.example.com" },
              "anthropic": { "apiKey": "sk-ant-file" }
            }
            """
        );

        WithEnvVar(
            "YOUTRACK_TOKEN",
            null,
            () =>
            {
                var ex = Assert.Throws<InvalidOperationException>(() => store.Load());
                Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(store.ConfigPath, ex.Message);
            }
        );
    }

    [Fact]
    public void Defaults_apply_when_workday_and_model_are_missing()
    {
        var store = StoreWith(
            """
            {
              "youTrack": { "baseUrl": "https://yt.example.com/youtrack", "webBaseUrl": "https://yt.example.com", "token": "perm:file-token" },
              "anthropic": { "apiKey": "sk-ant-file" }
            }
            """
        );

        WithEnvVar(
            "ANTHROPIC_API_KEY",
            null,
            () =>
            {
                var config = store.Load();
                Assert.Equal("claude-opus-4-8", config.Anthropic.Model);
                Assert.Equal(8.0, config.Workday.TargetHours);
                Assert.Equal("Europe/Zurich", config.Workday.Timezone);
                Assert.Equal(["In Bearbeitung", "In Arbeit"], config.Workday.InProgressStates);
            }
        );
    }
}
