namespace ValidationPlatform.Tests.CognitiveApi;

public class Story289MediaIdContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void MediaLookups_NormalizeDtoGuid_And_Delete_The_Resolved_Stored_Key()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var source = File.ReadAllText(Path.Combine(fixtureDirectory, "MediaAttachmentService.cs.txt"));

        Assert.Contains("GetCompatibleIds(id)", source);
        Assert.Contains("Guid.TryParse(id, out var guid)", source);
        Assert.Contains("guid.ToString(\"N\")", source);
        Assert.Contains("guid.ToString(\"D\")", source);
        Assert.Contains("var attachment = GetByCompatibleId(id)", source);
        Assert.Contains("SoftDelete<MediaAttachment>(attachment.Id", source);
        Assert.DoesNotContain("SoftDelete<MediaAttachment>(candidate", source);
    }
}
