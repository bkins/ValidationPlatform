using LocalAIAssistant.Knowledge.Inbox;

namespace ValidationPlatform.Tests.LaaSmoke;

[Trait("Category", "ClientContract")]
public class InboxReturnContractTests
{
    [Fact]
    public async Task RepeatedReadOnlyReturns_DoNotInvokeRefresh()
    {
        var policy = new InboxReturnRefreshPolicy();
        policy.MarkLoaded(0);
        var refreshes = 0;

        for (var visit = 0; visit < 20; visit++)
        {
            policy.BeginDetailNavigation();
            if (policy.ShouldLoadOnAppearance(0))
            {
                refreshes++;
                await Task.Yield();
            }
        }

        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void SuccessfulMutation_ForcesRefreshOnDetailReturn()
    {
        var state = new KnowledgeInboxRefreshState();
        var policy = new InboxReturnRefreshPolicy();
        policy.MarkLoaded(state.Revision);
        policy.BeginDetailNavigation();
        state.MarkChanged();

        Assert.True(policy.ShouldLoadOnAppearance(state.Revision));
        policy.MarkLoaded(state.Revision);
        policy.BeginDetailNavigation();
        Assert.False(policy.ShouldLoadOnAppearance(state.Revision));
    }

    [Fact]
    public async Task SuccessfulTaskMutation_Refreshes_The_Registered_Inbox_Exactly_Once()
    {
        var state = new KnowledgeInboxRefreshState();
        var refreshes = 0;
        state.RegisterRefresh(() =>
        {
            refreshes++;
            return Task.CompletedTask;
        });

        await state.MarkChangedAndRefreshAsync();

        Assert.Equal(1, refreshes);
        Assert.Equal(1, state.Revision);
    }

    [Fact]
    public void NormalEntry_RefreshesAfterReadOnlyReturnWasConsumed()
    {
        var policy = new InboxReturnRefreshPolicy();
        policy.MarkLoaded(0);
        policy.BeginDetailNavigation();

        Assert.False(policy.ShouldLoadOnAppearance(0));
        Assert.True(policy.ShouldLoadOnAppearance(0));
    }
}
