using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Interpreter.FastPath;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Options;
using Moq;

namespace ValidationPlatform.Tests.ConversationRouting;

public sealed partial class TaskRequestRoutingTests
{
    private readonly Mock<ICapabilityRegistry> _registry = new();
    private readonly Mock<ILlmClient> _client = new();
    private readonly Mock<IExecutionEngine> _execution = new();
    private readonly ConversationContextStore _contexts = new();
    private readonly ActionMetadata _listTasks = new() { Name = "ListTasks", Parameters = [] };

    [Theory]
    [InlineData("Show my active tasks.", false)]
    [InlineData("Show my active tasks", false)]
    [InlineData("Show my active tasks?!", true)]
    [InlineData("Show my active tasks.", true)]
    [InlineData("List my active tasks", false)]
    [InlineData("What are my active tasks?", true)]
    [InlineData("Show my open tasks", false)]
    [Trait("Category", "ClientContract")]
    public async Task Converse_ActiveTaskRequest_ExecutesWithoutProviders(string input, bool afterConversation)
    {
        var orchestrator = Build();
        _client.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("provider unavailable"));
        if (afterConversation)
        {
            _contexts.GetOrCreate("routing-test").LastUserMessage = "Write a long guide to planning a productive week";
            _contexts.GetOrCreate("routing-test").LastActionName = "ChitChat";
        }

        var result = await orchestrator.ConverseAsync(new ConverseRequest { SessionId = "routing-test", Input = input });

        Assert.True(result.Success);
        Assert.True(result.WasFastPath);
        Assert.Equal("ListTasks", result.SelectedAction);
        Assert.Equal("Active task fixture", result.Message);
        _execution.Verify(engine => engine.ExecuteAsync(_listTasks,
            It.Is<IDictionary<string, string>>(parameters => parameters["includeCompleted"] == "false"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Do not show my active tasks")]
    [InlineData("Show my active tasks tagged work")]
    [InlineData("Explain how to show my active tasks")]
    [InlineData("Show my active tasks and delete task 1")]
    [Trait("Category", "ClientContract")]
    public void Resolve_AmbiguousOrQualifiedTaskRequest_DoesNotDropMeaning(string input)
    {
        var actions = new Mock<IActionRegistry>();
        actions.SetupGet(registry => registry.Actions).Returns([_listTasks]);
        actions.SetupGet(registry => registry.FastPathActions).Returns([]);
        var resolver = new FastPathResolver(actions.Object, Mock.Of<IDailyRecordCommandParser>());

        var resolved = resolver.TryResolve(input, out var action, out _);

        Assert.False(resolved && action?.Name == "ListTasks");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "ClientContract")]
    public async Task Converse_InterpreterProvidersExhausted_DoesNotMakeSecondChatCallOrClaimSuccess(bool explicitChain)
    {
        var orchestrator = Build(explicitChain);
        var sequence = _client.SetupSequence(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                              .ThrowsAsync(new HttpRequestException("provider unavailable"));
        if (!explicitChain) sequence.ThrowsAsync(new HttpRequestException("capacity fallback unavailable"));
        sequence.ReturnsAsync(new LlmResponse { Content = "As an AI, I don't have access to your tasks." });

        var result = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "routing-test", Input = "Retrieve the tasks I haven't finished"
        });

        Assert.False(result.Success);
        Assert.Contains("Diagnostic ID:", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("As an AI", result.Message, StringComparison.Ordinal);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(explicitChain ? 1 : 2));
        _execution.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public async Task Converse_GeneralWritingRequest_PreservesConversationalFallback()
    {
        var orchestrator = Build();
        _client.SetupSequence(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new LlmResponse { Content = """{"actionName":null,"parameters":{},"failureType":"NoMatchingAction"}""" })
               .ReturnsAsync(new LlmResponse { Content = "Here is a guide to planning your week." });

        var result = await orchestrator.ConverseAsync(new ConverseRequest { SessionId = "routing-test", Input = "Write a guide to planning a week" });

        Assert.True(result.Success);
        Assert.Equal("Here is a guide to planning your week.", result.Message);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public async Task Converse_ProviderRecoversNextTurn_AllowsTaskExecutionInSameSession()
    {
        var orchestrator = Build();
        _client.SetupSequence(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("provider unavailable"))
               .ReturnsAsync(new LlmResponse { Content = """{"actionName":"ListTasks","parameters":{},"failureType":"None"}""" });
        var request = new ConverseRequest { SessionId = "routing-test", Input = "Retrieve the tasks I haven't finished" };

        var failed = await orchestrator.ConverseAsync(request);
        var recovered = await orchestrator.ConverseAsync(new ConverseRequest { SessionId = request.SessionId, Input = request.Input });

        Assert.False(failed.Success);
        Assert.True(recovered.Success);
        Assert.Equal("ListTasks", recovered.SelectedAction);
        Assert.Equal("Active task fixture", recovered.Message);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("What can you help me do?")]
    [InlineData("How can you help me?")]
    [InlineData("What can I use this for?")]
    public async Task Converse_DiscoveryWithUnavailableProviders_OnlyExecutesReadOnlyHelp(string input)
    {
        var orchestrator = Build(includeHelp: true);
        _client.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("provider unavailable"));
        var result = await orchestrator.ConverseAsync(new ConverseRequest { SessionId = "help-test", Input = input });
        Assert.True(result.Success);
        Assert.True(result.WasFastPath);
        Assert.Equal("ListActions", result.SelectedAction);
        _client.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _execution.Verify(engine => engine.ExecuteAsync(It.Is<ActionMetadata>(action => action.Name != "ListActions"),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ConversationOrchestrator Build(bool explicitChain = true, bool includeHelp = false, ActionMetadata[]? additional = null, Mock<IIdempotencyStore>? store = null)
    {
        ActionMetadata[] registered = includeHelp ? [_listTasks, new ActionMetadata { Name = "ListActions", Parameters = [] }] : [_listTasks];
        registered = registered.Concat(additional ?? []).ToArray();
        _registry.Setup(registry => registry.GetAll()).Returns(registered);
        var actions = new Mock<IActionRegistry>();
        actions.SetupGet(registry => registry.Actions).Returns(registered);
        actions.SetupGet(registry => registry.FastPathActions).Returns([]);
        var fastPath = new FastPathResolver(actions.Object, Mock.Of<IDailyRecordCommandParser>());
        var settings = new LlmClientSettings { Provider = LlmProvider.Groq, Groq = new GroqSettings { Model = "fixture-model" } };
        var defaults = new LlmProviderDefaults(Options.Create(settings));
        var factory = new Mock<ILlmClientFactory>();
        factory.SetupGet(provider => provider.DefaultProvider).Returns(LlmProvider.Groq);
        factory.Setup(provider => provider.Create(It.IsAny<LlmProvider>())).Returns(_client.Object);
        var chain = new Mock<ILlmFallbackChain>();
        chain.SetupGet(fallback => fallback.Enabled).Returns(explicitChain);
        chain.Setup(fallback => fallback.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<(LlmProvider, string)>());
        var rateLimiter = new Mock<ILlmRateLimiter>();
        rateLimiter.Setup(limiter => limiter.GetCurrentSnapshot(It.IsAny<string>())).Returns(LlmRateLimitSnapshot.Empty);
        var capacity = new Mock<ILlmCapacityRouter>();
        capacity.Setup(router => router.SelectModel(It.IsAny<TaskComplexity>()))
                .Returns(new LlmModelCapacity { ModelId = new LlmModelId("Groq", "fallback-model") });
        var router = new LlmRouter(factory.Object, defaults, Mock.Of<IPromptLogger>(), Mock.Of<ILlmUsageAggregator>(),
                                   rateLimiter.Object, capacity.Object, chain.Object);
        var telemetry = Mock.Of<ITelemetrySink>();
        var catalog = new LlmModelCatalog();
        var interpreter = new LlmInterpreter(_registry.Object, telemetry, router, catalog, settings, Mock.Of<IPromptLogger>());
        var workspace = new Mock<IWorkspaceContext>();
        workspace.SetupGet(context => context.ActiveWorkspace).Returns("personal");
        var idempotency = store ?? new Mock<IIdempotencyStore>();
        idempotency.Setup(store => store.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ConverseResponse?)null);
        var insights = new Mock<IInsightEngine>();
        insights.Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<Insight>)Array.Empty<Insight>());
        _execution.Setup(engine => engine.ExecuteAsync(It.IsAny<ActionMetadata>(), It.IsAny<IDictionary<string, string>>(),
                                                      It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("Active task fixture");
        return new ConversationOrchestrator(_registry.Object, interpreter, _execution.Object, _contexts, telemetry, fastPath,
            workspace.Object, router, idempotency.Object, new TelemetryContext { SessionId = "routing-test" }, insights.Object,
            new InMemoryInsightHistoryStore(), Mock.Of<IActivityLog>(), catalog, defaults, rateLimiter.Object,
            Mock.Of<IConversationTurnStore>(), Mock.Of<IConversationMetadataStore>(), new TaskComplexityClassifier());
    }
}
