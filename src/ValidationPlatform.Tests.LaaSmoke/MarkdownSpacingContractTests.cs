namespace ValidationPlatform.Tests.LaaSmoke;

public class MarkdownSpacingContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void NativeRenderer_DefaultBlockGap_IsConfigured()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NativeMarkdownView.cs.txt"));

        Assert.Matches(@"public NativeMarkdownView\(\)\s*\{[^}]*Spacing\s*=\s*8\s*;", source);
    }
}
