namespace ValidationPlatform.Tests.LaaSmoke;

public class MarkdownSpacingContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void NativeRenderer_OnlyResultHeaders_GetGap()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NativeMarkdownView.cs.txt"));

        Assert.Matches(@"public NativeMarkdownView\(\)\s*\{[^}]*Spacing\s*=\s*0\s*;", source);
        Assert.Contains("SearchResultSpacing.TopGap(markdown.Substring(paragraph.Span.Start, paragraph.Span.Length))", source);
        Assert.Contains("Children.Add(CreateSearchResultDivider());", source);
        Assert.DoesNotContain("SearchResultSpacing.TopGap(rawText)", source);
        var resultBlocks = new[] { "Results for 'joe':", "[journal] entry #1 (score: 0.92)", "# Title", "Body", "[journal] entry #2 (score: 0.81)", "# Another title", "Another body" };
        Assert.Equal(new double[] { 0, 12, 0, 0, 12, 0, 0 }, resultBlocks.Select(LocalAIAssistant.Views.Controls.SearchResultSpacing.TopGap));
    }
}
