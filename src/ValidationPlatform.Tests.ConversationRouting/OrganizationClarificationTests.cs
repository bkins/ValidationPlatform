using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using Moq;

namespace ValidationPlatform.Tests.ConversationRouting;

public sealed partial class TaskRequestRoutingTests
{
    private CognitivePlatform.Api.Orchestrator.ConversationOrchestrator OrganizationEngine()
    {
        var engine = Build(additional:
        [
            new ActionMetadata { Name = "GetDomainGuidance", Parameters = [] },
            new ActionMetadata { Name = "OpenDay", Parameters = [] },
            new ActionMetadata { Name = "StartBrainDump", Parameters = [] }
        ]);
        _client.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("provider unavailable"));
        return engine;
    }

    [Theory]
    [InlineData("Help me get organized")]
    [InlineData("I need help getting organized.")]
    [InlineData("Help me organize my day")]
    public async Task Organization_VagueRequest_AsksWithoutExecutionOrProviders(string input)
    {
        var engine = OrganizationEngine();
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = input });
        Assert.True(result.Success);
        Assert.Null(result.SelectedAction);
        Assert.Contains("tasks", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("daily", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brain dump", result.Message!, StringComparison.OrdinalIgnoreCase);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_TasksThenExplicitRead_UsesNormalEngineAndRetainsOriginalTurns()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        var question = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        Assert.Null(question.SelectedAction);
        Assert.Contains("active tasks", question.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Help me get organized", _contexts.GetOrCreate("organize").PendingOrganizationIntent!.OriginalInput);
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "review active tasks" });
        Assert.Equal("ListTasks", result.SelectedAction);
        Assert.True(result.Success);
        Assert.Contains(_contexts.GetOrCreate("organize").Turns, turn => turn.UserMessage == "Tasks");
        _client.VerifyNoOtherCalls();
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "ListTasks"),
            It.Is<IDictionary<string, string>>(parameters => parameters["includeCompleted"] == "false"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("never mind")]
    public async Task Organization_Cancel_EndsFlowWithoutExecution(string cancel)
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = cancel });
        Assert.Contains("cancel", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        Assert.Null(result.SelectedAction);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_Correction_ChangesAreaWithoutStartingDay()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Actually, daily planning" });
        Assert.Contains("daily", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.SelectedAction);
        var guidance = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "explain it" });
        Assert.Equal("GetDomainGuidance", guidance.SelectedAction);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "OpenDay"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Organization_RepeatedAmbiguity_ExitsAndResetSeparatesSessions()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "hmm" });
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "maybe" });
        Assert.Contains("stop", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        Assert.Null(result.SelectedAction);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_MissingCapabilities_OnlyOffersRealChoices()
    {
        var engine = Build();
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        Assert.Contains("tasks", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("daily", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("brain dump", result.Message!, StringComparison.OrdinalIgnoreCase);
        _execution.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Organization_ResetAndSeparateSession_DoNotConsumeOldAnswer()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "first", Input = "Help me get organized" });
        _contexts.GetOrCreate("first").Reset();
        Assert.Null(_contexts.GetOrCreate("first").PendingOrganizationIntent);
        Assert.Null(_contexts.GetOrCreate("second").PendingOrganizationIntent);
    }

    [Fact]
    public async Task Organization_NewExplicitCommand_ClearsIntentAndExecutesNormally()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Show my active tasks" });
        Assert.Equal("ListTasks", result.SelectedAction);
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_NewWritingRequest_UsesNormalProviderRoute()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Write a poem about the ocean" });
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Organization_ExistingDestructiveConfirmation_RemainsAuthority()
    {
        var engine = OrganizationEngine();
        var context = _contexts.GetOrCreate("organize");
        context.PendingAction = new PendingAction { ActionName = "DeleteTask", ConfirmationRequired = true };
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        Assert.Contains("confirm", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(context.PendingAction);
        Assert.Null(context.PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_ChangedCatalog_DoesNotDispatchMissingAction()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        _registry.Setup(registry => registry.GetAll()).Returns([]);
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "review active tasks" });
        Assert.Null(result.SelectedAction);
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_RetriedAnswer_DoesNotAdvanceAmbiguityOrDuplicateHistory()
    {
        var store = new Mock<IIdempotencyStore>();
        var engine = Build(store: store);
        var cache = new Dictionary<Guid, ConverseResponse>();
        store.Setup(source => source.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .Returns((Guid key, CancellationToken token) => Task.FromResult(cache.GetValueOrDefault(key)));
        store.Setup(source => source.StoreAsync(It.IsAny<Guid>(), It.IsAny<ConverseResponse>(), It.IsAny<CancellationToken>()))
             .Returns((Guid key, ConverseResponse response, CancellationToken token) => { cache[key] = response; return Task.CompletedTask; });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        var request = new ConverseRequest { SessionId = "organize", Input = "hmm", ClientRequestId = Guid.NewGuid() };
        var first = await engine.ConverseAsync(request);
        var retry = await engine.ConverseAsync(request);
        Assert.Same(first, retry);
        Assert.Equal(1, _contexts.GetOrCreate("organize").PendingOrganizationIntent!.UnclearReplies);
        Assert.Equal(2, _contexts.GetOrCreate("organize").Turns.Count);
        _execution.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Help me get organized")]
    [InlineData("I need to plan tomorrow")]
    [InlineData("I'm overwhelmed")]
    public async Task Organization_ExplicitDeletion_StillRequiresConfirmation(string starter)
    {
        var engine = Build(additional: [new ActionMetadata { Name = "DeleteTask", IsDestructive = true, Parameters = [] }]);
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = starter });
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Delete task 1" });
        Assert.True(result.IsConfirmationRequired);
        Assert.NotNull(_contexts.GetOrCreate("organize").PendingAction);
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Do not help me get organized")]
    [InlineData("Write about the phrase help me get organized")]
    [InlineData("Help me delete all tasks")]
    public async Task Organization_UnrelatedOrNegatedRequest_DoesNotStartFlow(string input)
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = input });
        Assert.Null(_contexts.GetOrCreate("organize").PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("daily planning")]
    [InlineData("brain dump")]
    public async Task Organization_YesToSingleExplanationChoice_RoutesOnlyGuidance(string area)
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" });
        var question = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = area });
        Assert.Null(question.SelectedAction);
        _execution.VerifyNoOtherCalls();
        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "yes please" });
        Assert.Equal("GetDomainGuidance", result.SelectedAction);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name != "GetDomainGuidance"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Organization_Streaming_UsesSameQuestionsAndSafeTaskHandoff()
    {
        var engine = OrganizationEngine();
        var replies = new List<string>();
        foreach (var input in new[] { "Help me get organized", "Tasks", "review active tasks" })
        {
            var parts = new List<string>();
            await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "stream-organize", Input = input }))
                parts.Add(part);
            replies.Add(string.Concat(parts));
        }
        Assert.Contains("What would help", replies[0]);
        Assert.Contains("active tasks", replies[1]);
        Assert.Equal("Active task fixture", replies[2]);
        _client.VerifyNoOtherCalls();
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "ListTasks"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(3, _contexts.GetOrCreate("stream-organize").Turns.Count);
    }

    [Fact]
    public async Task Organization_StreamingBrainDumpAndCancel_DoesNotStartAnything()
    {
        var engine = OrganizationEngine();
        var replies = new List<string>();
        foreach (var input in new[] { "Help me get organized", "brain dump", "cancel" })
        {
            var parts = new List<string>();
            await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "stream-organize", Input = input }))
                parts.Add(part);
            replies.Add(string.Concat(parts));
        }
        Assert.Contains("What would help", replies[0]);
        Assert.Contains("explain", replies[1]);
        Assert.Contains("Cancelled", replies[2]);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Help me get organized")]
    [InlineData("I need to plan tomorrow")]
    [InlineData("I'm overwhelmed")]
    public async Task Organization_StreamRetryAfterFinalHandoff_DoesNotExecuteAgain(string starter)
    {
        var store = new Mock<IIdempotencyStore>();
        var engine = Build(store: store);
        var cache = new Dictionary<Guid, ConverseResponse>();
        store.Setup(source => source.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .Returns((Guid key, CancellationToken token) => Task.FromResult(cache.GetValueOrDefault(key)));
        store.Setup(source => source.StoreAsync(It.IsAny<Guid>(), It.IsAny<ConverseResponse>(), It.IsAny<CancellationToken>()))
             .Returns((Guid key, ConverseResponse response, CancellationToken token) => { cache[key] = response; return Task.CompletedTask; });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = starter });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        var request = new ConverseRequest { SessionId = "organize", Input = "review active tasks", ClientRequestId = Guid.NewGuid() };
        foreach (var attempt in Enumerable.Range(0, 2))
            await foreach (var part in engine.StreamAsync(request)) Assert.Equal("Active task fixture", part);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "ListTasks"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(3, _contexts.GetOrCreate("organize").Turns.Count);
    }

    [Fact]
    public async Task Organization_StreamThenRegularCorrection_RetainsSharedState()
    {
        var engine = OrganizationEngine();
        await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "organize", Input = "Help me get organized" }))
            Assert.Contains("What would help", part);
        await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Tasks" });
        var correction = await engine.ConverseAsync(new ConverseRequest { SessionId = "organize", Input = "Actually, daily planning" });
        Assert.Contains("daily", correction.Message!);
        await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "organize", Input = "yes please" }))
            Assert.Equal("Active task fixture", part);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "GetDomainGuidance"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("I'm overwhelmed")]
    [InlineData("I’m feeling overwhelmed!")]
    [InlineData("I have too much to do")]
    [InlineData("I've got too much to do")]
    [InlineData("I don't know where to start")]
    [InlineData("I DON’T KNOW WHERE TO START.")]
    [InlineData("I need to plan tomorrow")]
    [InlineData("Can you help me plan tomorrow?")]
    [InlineData("Help me prepare for tomorrow")]
    public async Task Organization_NaturalStarter_PreservesConcern_WithoutProviders(string input)
    {
        var engine = OrganizationEngine();

        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "natural", Input = input });

        Assert.Null(result.SelectedAction);
        Assert.Contains("tasks", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(input, _contexts.GetOrCreate("natural").PendingOrganizationIntent?.OriginalInput);
        if (input.Contains("tomorrow")) Assert.Contains("tomorrow", result.Message!, StringComparison.OrdinalIgnoreCase);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, "I'm overwhelmed")]
    [InlineData(true, "I'm overwhelmed")]
    [InlineData(false, "I need to plan tomorrow")]
    [InlineData(true, "I need to plan tomorrow")]
    public async Task Organization_NaturalFlow_ContextualRead_HasTransportParity(bool streaming, string starter)
    {
        var engine = OrganizationEngine();
        foreach (var input in new[] { starter, "tasks", "review them" })
        {
            if (streaming)
                await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "natural", Input = input })) { }
            else
                await engine.ConverseAsync(new ConverseRequest { SessionId = "natural", Input = input });
        }

        Assert.Null(_contexts.GetOrCreate("natural").PendingOrganizationIntent);
        Assert.Equal(3, _contexts.GetOrCreate("natural").Turns.Count);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name == "ListTasks"),
            It.Is<IDictionary<string, string>>(parameters => parameters["includeCompleted"] == "false"
                && !parameters.ContainsKey("dueBefore") && !parameters.ContainsKey("dueAfter")),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Organization_Tomorrow_RefusesTodaySubstitution_AndRetainsCorrection(bool streaming)
    {
        var engine = OrganizationEngine();
        var replies = new List<string>();
        foreach (var input in new[] { "Help me plan tomorrow", "Actually, daily planning", "open tomorrow's daily plan" })
        {
            if (streaming)
            {
                var parts = new List<string>();
                await foreach (var part in engine.StreamAsync(new ConverseRequest { SessionId = "tomorrow", Input = input })) parts.Add(part);
                replies.Add(string.Concat(parts));
            }
            else replies.Add((await engine.ConverseAsync(new ConverseRequest { SessionId = "tomorrow", Input = input })).Message!);
        }

        Assert.All(replies, reply => Assert.Contains("tomorrow", reply, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("today", replies[^1], StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(_contexts.GetOrCreate("tomorrow").PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_Tomorrow_DirectReview_IsAllActiveTasks_AndBareYesDoesNotChoose()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "tomorrow", Input = "I need to plan tomorrow" });
        var ambiguous = await engine.ConverseAsync(new ConverseRequest { SessionId = "tomorrow", Input = "yes" });
        Assert.Null(ambiguous.SelectedAction);
        _execution.VerifyNoOtherCalls();

        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "tomorrow", Input = "review active tasks" });

        Assert.Equal("ListTasks", result.SelectedAction);
        Assert.Null(_contexts.GetOrCreate("tomorrow").PendingOrganizationIntent);
        _client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("I'm not overwhelmed")]
    [InlineData("Write a poem about being overwhelmed")]
    [InlineData("\"I'm overwhelmed\"")]
    [InlineData("Create a task called I'm overwhelmed")]
    [InlineData("I need to plan tomorrow's team presentation slides")]
    [InlineData("I don't know where to start debugging my printer")]
    public void Organization_NaturalMatching_DoesNotHijackOtherRequests(string input)
    {
        Assert.False(OrganizationClarification.IsStarter(input));
    }
    [Theory]
    [InlineData("I'm overwhelmed")]
    [InlineData("I need to plan tomorrow")]
    public async Task Organization_NaturalFlow_CancelAndBoundedExit_ClearState(string starter)
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "cancel-natural", Input = starter });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "cancel-natural", Input = "tasks" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "cancel-natural", Input = "cancel" });
        Assert.Null(_contexts.GetOrCreate("cancel-natural").PendingOrganizationIntent);
        await engine.ConverseAsync(new ConverseRequest { SessionId = "bounded-natural", Input = starter });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "bounded-natural", Input = "hmm" });
        var stopped = await engine.ConverseAsync(new ConverseRequest { SessionId = "bounded-natural", Input = "maybe" });
        Assert.Contains("stop", stopped.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_contexts.GetOrCreate("bounded-natural").PendingOrganizationIntent);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_Tomorrow_MissingGuidance_DoesNotOfferIt()
    {
        var engine = Build();

        var result = await engine.ConverseAsync(new ConverseRequest { SessionId = "limited", Input = "Help me plan tomorrow" });

        Assert.Contains("all active tasks", result.Message!);
        Assert.DoesNotContain("explain planning features", result.Message!);
        Assert.DoesNotContain("brain dump", result.Message!);
        _execution.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Organization_Tomorrow_Correction_RetainsTypedDayAndConcern()
    {
        var engine = OrganizationEngine();
        await engine.ConverseAsync(new ConverseRequest { SessionId = "typed", Input = "Help me plan tomorrow" });
        await engine.ConverseAsync(new ConverseRequest { SessionId = "typed", Input = "tasks" });

        await engine.ConverseAsync(new ConverseRequest { SessionId = "typed", Input = "Actually, daily planning" });

        var pending = _contexts.GetOrCreate("typed").PendingOrganizationIntent!;
        Assert.Equal("tomorrow", pending.TargetDay);
        Assert.Equal("future-day", pending.IntentFamily);
        Assert.Equal("Help me plan tomorrow", pending.OriginalInput);
        Assert.Equal("daily planning", pending.SelectedArea);
        _execution.VerifyNoOtherCalls();
    }
}
