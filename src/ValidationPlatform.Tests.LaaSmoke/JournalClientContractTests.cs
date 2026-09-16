using LocalAIAssistant.CognitivePlatform.CpClients.Journal;
using LocalAIAssistant.Knowledge.Inbox;
using LocalAIAssistant.Knowledge.Journals.Models;

namespace ValidationPlatform.Tests.LaaSmoke;

[Trait("Category", "ClientContract")]
public class JournalClientContractTests
{
    [Theory]
    [InlineData("\"Committed\"", JournalEntryState.Committed)]
    [InlineData("\"Edited\"", JournalEntryState.Edited)]
    [InlineData("3", JournalEntryState.Committed)]
    public async Task JournalDetail_WireState_PreservesImportedPresentation(string state, JournalEntryState expected)
    {
        var id = Guid.NewGuid();
        var wire = $$"""
                     {"id":"{{id}}","text":"# Legacy title\n\nRemembered body","state":{{state}},"tags":["source:ttr","journal:personal"],"createdAt":"2021-12-24T06:49:36Z","mood":"Happy 😊","moodScore":null}
                     """;
        using var http = new HttpClient(new JournalResponseHandler(wire)) { BaseAddress = new Uri("http://localhost:5276") };
        var client = new JournalApiClient(http);

        var entry = await client.GetByIdAsync(id);

        Assert.NotNull(entry);
        Assert.Equal(id, entry.Id);
        Assert.Equal("# Legacy title\n\nRemembered body", entry.Text);
        Assert.Equal(expected, entry.State);
        Assert.Equal(new[] { "source:ttr", "journal:personal" }, entry.Tags);
        Assert.Equal("Happy 😊", entry.Mood);
        Assert.Null(entry.MoodScore);
        Assert.Equal(DateTimeOffset.Parse("2021-12-24T06:49:36Z"), entry.CreatedAt.ToUniversalTime());
    }

    [Theory]
    [InlineData(KnowledgeKind.Meal)]
    [InlineData(KnowledgeKind.Conversation)]
    [InlineData((KnowledgeKind)999)]
    public async Task Inbox_UnsupportedKind_IsNonfatalAndDoesNotNavigate(KnowledgeKind kind)
    {
        var invoked = false;

        var error = await KnowledgeItemDetailNavigation.OpenAsync(new KnowledgeItem { Kind = kind }, _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        Assert.False(invoked);
        Assert.NotNull(error);
        Assert.Contains("not supported", error);
    }

    [Fact]
    public async Task Inbox_JournalNavigationFailure_IsContained()
    {
        var item = new KnowledgeItem { Id = Guid.NewGuid(), Kind = KnowledgeKind.Journal };

        var error = await KnowledgeItemDetailNavigation.OpenAsync(item, _ => Task.FromException(new InvalidOperationException("Shell failure")));

        Assert.NotNull(error);
        Assert.Contains("Unable to open", error);
    }
}
