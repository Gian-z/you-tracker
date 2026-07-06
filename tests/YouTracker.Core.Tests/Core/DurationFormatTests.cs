using YouTracker.Core.Domain;

namespace YouTracker.Core.Tests.Core;

public class DurationFormatTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(45, "45m")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h 30m")]
    [InlineData(480, "8h")]
    public void ToPresentation_formats_minutes(int minutes, string expected) =>
        Assert.Equal(expected, DurationFormat.ToPresentation(minutes));

    [Theory]
    [InlineData("1h 30m", 90)]
    [InlineData("90m", 90)]
    [InlineData("1.5h", 90)]
    [InlineData("90", 90)]
    [InlineData("2h", 120)]
    public void TryParseMinutes_parses_valid_input(string input, int expected)
    {
        Assert.True(DurationFormat.TryParseMinutes(input, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void TryParseMinutes_rejects_invalid_input(string input) =>
        Assert.False(DurationFormat.TryParseMinutes(input, out _));
}
