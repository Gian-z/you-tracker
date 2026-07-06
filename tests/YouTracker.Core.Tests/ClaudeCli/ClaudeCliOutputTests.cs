using YouTracker.Core.Config;
using YouTracker.Infrastructure.ClaudeCli;

namespace YouTracker.Core.Tests.ClaudeCli;

public sealed class ClaudeCliOutputTests
{
    [Fact]
    public void ExtractResult_returns_result_field_from_envelope()
    {
        const string stdout = """
            {"type":"result","subtype":"success","is_error":false,"result":"Hello there","session_id":"abc"}
            """;
        Assert.Equal("Hello there", ClaudeCliOutput.ExtractResult(stdout));
    }

    [Fact]
    public void ExtractResult_skips_noise_lines_before_envelope()
    {
        const string stdout = """
            some npm warning
            {"type":"result","subtype":"success","is_error":false,"result":"OK"}
            """;
        Assert.Equal("OK", ClaudeCliOutput.ExtractResult(stdout));
    }

    [Fact]
    public void ExtractResult_throws_on_error_envelope()
    {
        const string stdout = """
            {"type":"result","subtype":"error_during_execution","is_error":true,"result":"boom"}
            """;
        Assert.Throws<ClaudeCliException>(() => ClaudeCliOutput.ExtractResult(stdout));
    }

    [Fact]
    public void ExtractResult_throws_on_empty_result()
    {
        const string stdout = """{"type":"result","is_error":false,"result":""}""";
        Assert.Throws<ClaudeCliException>(() => ClaudeCliOutput.ExtractResult(stdout));
    }

    [Fact]
    public void ExtractResult_throws_on_unparseable_output()
    {
        Assert.Throws<ClaudeCliException>(() => ClaudeCliOutput.ExtractResult("not json at all"));
    }

    [Theory]
    [InlineData("""{"drafts":[]}""", """{"drafts":[]}""")]
    [InlineData("```json\n{\"drafts\":[]}\n```", """{"drafts":[]}""")]
    [InlineData("```\n{\"drafts\":[]}\n```", """{"drafts":[]}""")]
    [InlineData("Here is the JSON:\n{\"drafts\":[]}\nHope that helps!", """{"drafts":[]}""")]
    public void ExtractJsonObject_unwraps_fences_and_prose(string input, string expected)
    {
        Assert.Equal(expected, ClaudeCliOutput.ExtractJsonObject(input));
    }

    [Fact]
    public void ExtractJsonObject_throws_when_no_json_present()
    {
        Assert.Throws<ClaudeCliException>(() =>
            ClaudeCliOutput.ExtractJsonObject("I cannot produce that.")
        );
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("sk-ant-PASTE-YOUR-API-KEY-HERE", false)]
    [InlineData("sk-ant-...", false)]
    [InlineData("sk-ant-api03-real-key", true)]
    public void AnthropicConfig_HasApiKey_detects_placeholders(string apiKey, bool expected)
    {
        var config = new AnthropicConfig(apiKey, "claude-opus-4-8");
        Assert.Equal(expected, config.HasApiKey);
    }
}
