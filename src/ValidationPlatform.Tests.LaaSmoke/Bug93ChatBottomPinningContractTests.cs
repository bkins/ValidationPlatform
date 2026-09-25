using LocalAIAssistant.Views;

namespace ValidationPlatform.Tests.LaaSmoke;

public sealed class Bug93ChatBottomPinningContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void ChatBottomPinning_PreservesIntentAndRenderedContentSignals()
    {
        var state = new ChatScrollState();
        state.ObserveViewport(lastVisibleItemIndex: 2, messageCount: 6);

        Assert.False(state.ShouldPinAfterContentChange);

        state.MarkPromptSent();

        Assert.True(state.ShouldPinAfterContentChange);

        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml.txt"));
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml.cs.txt"));

        Assert.Contains("Scrolled=\"OnMessagesViewScrolled\"", markup);
        Assert.Contains("SizeChanged=\"OnMessageContentSizeChanged\"", markup);
        Assert.Contains("new[] { 0, 50, 150, 300 }", source);
        Assert.Contains("CancelBottomPin", source);
    }
}
