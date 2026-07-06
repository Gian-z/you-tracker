using System.Text.Json;
using YouTracker.Core.Ai;
using YouTracker.Core.Domain;

namespace YouTracker.Core.Tests.Core;

public class AiResponseParserTests
{
    private static readonly DateOnly Fallback = new(2026, 7, 6);
    private static readonly Issue[] KnownIssues =
    [
        TestData.Issue("ABC-1"),
        TestData.Issue("ABC-2"),
    ];

    [Fact]
    public void ParseDrafts_maps_valid_json_to_drafts()
    {
        const string json = """
            {
              "drafts": [
                {
                  "issueId": "ABC-1",
                  "confidence": "high",
                  "date": "2026-07-03",
                  "minutes": 90,
                  "workTypeName": "Entwicklung",
                  "comment": "Implemented feature",
                  "reasoning": "matches ticket"
                }
              ],
              "unmatched": []
            }
            """;

        var result = AiResponseParser.ParseDrafts(json, KnownIssues, Fallback);

        var draft = Assert.Single(result.Drafts);
        Assert.Empty(result.Unmatched);
        Assert.Equal("ABC-1", draft.IssueId);
        Assert.Equal("Summary of ABC-1", draft.IssueSummary);
        Assert.Equal("high", draft.Confidence);
        Assert.Equal(new DateOnly(2026, 7, 3), draft.Date);
        Assert.Equal(90, draft.Minutes);
        Assert.Equal("Entwicklung", draft.WorkTypeName);
        Assert.Equal("Implemented feature", draft.Comment);
    }

    [Fact]
    public void ParseDrafts_moves_unknown_issue_id_to_unmatched()
    {
        const string json = """
            { "drafts": [ { "issueId": "NOPE-99", "minutes": 30, "comment": "mystery work" } ] }
            """;

        var result = AiResponseParser.ParseDrafts(json, KnownIssues, Fallback);

        Assert.Empty(result.Drafts);
        var unmatched = Assert.Single(result.Unmatched);
        Assert.Contains("NOPE-99", unmatched.Reason);
    }

    [Fact]
    public void ParseDrafts_moves_non_positive_minutes_to_unmatched()
    {
        const string json = """
            { "drafts": [ { "issueId": "ABC-1", "minutes": 0, "comment": "zero" } ] }
            """;

        var result = AiResponseParser.ParseDrafts(json, KnownIssues, Fallback);

        Assert.Empty(result.Drafts);
        var unmatched = Assert.Single(result.Unmatched);
        Assert.Contains("non-positive", unmatched.Reason);
    }

    [Fact]
    public void ParseDrafts_uses_fallback_date_for_bad_date_string()
    {
        const string json = """
            { "drafts": [ { "issueId": "ABC-1", "minutes": 30, "date": "yesterday-ish" } ] }
            """;

        var result = AiResponseParser.ParseDrafts(json, KnownIssues, Fallback);

        Assert.Equal(Fallback, Assert.Single(result.Drafts).Date);
    }

    [Fact]
    public void ParseDrafts_normalizes_invalid_confidence_to_low()
    {
        const string json = """
            { "drafts": [ { "issueId": "ABC-1", "minutes": 30, "confidence": "certainly!" } ] }
            """;

        var result = AiResponseParser.ParseDrafts(json, KnownIssues, Fallback);

        Assert.Equal("low", Assert.Single(result.Drafts).Confidence);
    }

    [Fact]
    public void ParseDrafts_throws_on_malformed_json()
    {
        Assert.Throws<JsonException>(() =>
            AiResponseParser.ParseDrafts("{ this is not json", KnownIssues, Fallback)
        );
        Assert.Throws<FormatException>(() =>
            AiResponseParser.ParseDrafts("null", KnownIssues, Fallback)
        );
    }

    [Fact]
    public void ParseTriage_filters_unknown_ids_and_reranks_sequentially()
    {
        const string json = """
            {
              "ranked": [
                { "issueId": "GHOST-1", "rank": 1, "score": 99, "reasons": ["hallucinated"] },
                { "issueId": "ABC-2", "rank": 2, "score": 80, "reasons": ["urgent"] },
                { "issueId": "ABC-1", "rank": 5, "score": 150, "reasons": [] }
              ],
              "focusSuggestion": "Do ABC-2 first."
            }
            """;

        var result = AiResponseParser.ParseTriage(json, KnownIssues);

        Assert.Equal(2, result.Ranked.Count);
        Assert.Equal("ABC-2", result.Ranked[0].IssueId);
        Assert.Equal(1, result.Ranked[0].Rank);
        Assert.Equal("ABC-1", result.Ranked[1].IssueId);
        Assert.Equal(2, result.Ranked[1].Rank);
        Assert.Equal(100, result.Ranked[1].Score); // clamped
        Assert.Equal("Do ABC-2 first.", result.FocusSuggestion);
    }
}
