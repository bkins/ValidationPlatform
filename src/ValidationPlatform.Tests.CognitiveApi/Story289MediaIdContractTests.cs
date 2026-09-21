namespace ValidationPlatform.Tests.CognitiveApi;

public class Story289MediaIdContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void MediaLookups_NormalizeDtoGuidBeforeObjectStoreAccess()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var source = File.ReadAllText(Path.Combine(fixtureDirectory, "MediaAttachmentService.cs.txt"));

        Assert.Contains("GetCompatibleIds(id)", source);
        Assert.Contains("Guid.TryParse(id, out var guid)", source);
        Assert.Contains("guid.ToString(\"N\")", source);
        Assert.Contains("guid.ToString(\"D\")", source);
        Assert.Contains("SoftDelete<MediaAttachment>(candidate", source);
    }
}
