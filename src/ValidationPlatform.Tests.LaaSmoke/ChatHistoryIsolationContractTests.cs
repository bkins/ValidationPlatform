using LocalAIAssistant.Core.ConversationHistory;

namespace ValidationPlatform.Tests.LaaSmoke;

public class ChatHistoryIsolationContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void DevAndQa_ConversationAndMemoryOwners_AreDistinct()
    {
        Assert.NotEqual(ChatHistoryScope.ActiveConversationKey("Dev"), ChatHistoryScope.ActiveConversationKey("QA"));
        Assert.NotEqual(ChatHistoryScope.MemoryDirectory("app", "Dev"), ChatHistoryScope.MemoryDirectory("app", "QA"));
        Assert.NotEqual("ActiveConversationId", ChatHistoryScope.ActiveConversationKey("QA"));
        Assert.NotEqual("app", ChatHistoryScope.MemoryDirectory("app", "QA"));
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public async Task QaEmptyServerHistory_DoesNotReadDevFallback()
    {
        var history = await ConversationHistoryLoader.LoadAsync<string>(
            () => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()),
            () => throw new Exception("Local memory must not be read"),
            _ => throw new Exception("Unexpected server failure"));
        Assert.Empty(history);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public async Task QaOffline_ServerFailure_UsesQaFallback()
    {
        var history = await ConversationHistoryLoader.LoadAsync<string>(
            () => Task.FromException<IReadOnlyList<string>>(new HttpRequestException("offline")),
            () => Task.FromResult<IReadOnlyList<string>>(new[] { "QA history" }),
            _ => { });
        Assert.Equal(new[] { "QA history" }, history);
    }
}
