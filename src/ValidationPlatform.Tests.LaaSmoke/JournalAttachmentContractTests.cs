using System.Net;
using LocalAIAssistant.Core.Media;

namespace ValidationPlatform.Tests.LaaSmoke;

public class JournalAttachmentContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void Journal_Edit_Exposes_Accessible_Unclipped_Remove_Action()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EditJournalEntryPage.xaml.txt"));

        Assert.Contains("Text=\"Remove\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinimumHeightRequest=\"44\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SemanticProperties.Description=\"{Binding RemoveAccessibilityText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"✕\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HeightRequest=\"132\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public async Task Journal_List_Uses_Owner_Endpoint_And_Accepts_Empty_Storage_Path()
    {
        var ownerId = Guid.NewGuid();
        var handler = new MediaHandler();
        var client = new MediaAttachmentApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5274/") });

        var attachments = await client.ListAsync(ownerId);

        Assert.Equal($"/api/media/JournalEntry/{ownerId}", handler.Path);
        Assert.Single(attachments!);
        Assert.True(attachments![0].IsImage);
        Assert.Empty(attachments[0].StoragePath);
    }

    private sealed class MediaHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":"9ab7ff12-0a86-584f-a42c-4d15bfff3e00","fileName":"Entry1.jpg","contentType":"image/jpeg","storagePath":""}]""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
