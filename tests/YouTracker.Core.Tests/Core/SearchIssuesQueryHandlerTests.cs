using YouTracker.Core.Application;
using YouTracker.Core.Application.Handlers;

namespace YouTracker.Core.Tests.Core;

public class SearchIssuesQueryHandlerTests
{
    [Fact]
    public async Task Maps_issues_to_task_list_items_with_web_url()
    {
        var reader = new FakeIssueReader(TestData.Issue("ALPHA-1", estimate: 90));
        var handler = new SearchIssuesQueryHandler(reader, TestData.Config());

        var items = await handler.HandleAsync(new SearchIssuesQuery("alpha"));

        var item = Assert.Single(items);
        Assert.Equal("ALPHA-1", item.IssueId);
        Assert.Equal("1h 30m", item.Estimate);
        Assert.Equal("https://yt.example.com/issue/ALPHA-1", item.WebUrl);
        Assert.Equal("alpha", Assert.Single(reader.SearchTexts));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    public async Task Short_or_blank_text_returns_empty_without_calling_the_reader(string text)
    {
        var reader = new FakeIssueReader(TestData.Issue("ALPHA-1"));
        var handler = new SearchIssuesQueryHandler(reader, TestData.Config());

        Assert.Empty(await handler.HandleAsync(new SearchIssuesQuery(text)));
        Assert.Empty(reader.SearchTexts);
    }

    [Theory]
    [InlineData(500, 50)]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    public async Task Top_is_clamped(int requested, int expected)
    {
        var reader = new FakeIssueReader();
        var handler = new SearchIssuesQueryHandler(reader, TestData.Config());

        await handler.HandleAsync(new SearchIssuesQuery("alpha", requested));

        Assert.Equal(expected, Assert.Single(reader.SearchTops));
    }
}
