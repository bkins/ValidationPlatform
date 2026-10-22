using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter.FastPath;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using Moq;

namespace ValidationPlatform.Tests.ConversationRouting;

[Collection("LlmSharedState")]
public sealed class CapabilityHelpTests
{
    [Theory]
    [InlineData("What can you help me do?")]
    [InlineData("How can you help me?")]
    [InlineData("What can I use this for?")]
    [InlineData("What can you do?")]
    public void Discovery_IsDeterministicAndUsesOverview(string input)
    {
        var resolver = Resolver();
        Assert.True(resolver.TryResolve(input, out var action, out var parameters));
        Assert.Equal("ListActions", action!.Name);
        Assert.False(parameters!.ContainsKey("full"));
    }

    [Theory]
    [InlineData("List all commands")]
    [InlineData("List commands")]
    [InlineData("Show all actions.")]
    public void FullDirectory_IsExplicit(string input)
    {
        Assert.True(Resolver().TryResolve(input, out var action, out var parameters));
        Assert.Equal("ListActions", action!.Name);
        Assert.Equal("true", parameters!["full"]);
    }

    [Theory]
    [InlineData("Do not show capabilities")]
    [InlineData("Write a story about how can you help me")]
    [InlineData("Help me delete all tasks")]
    public void OtherRequests_AreNotMistakenForDiscovery(string input)
    {
        var resolved = Resolver().TryResolve(input, out var action, out _);
        Assert.False(resolved && action?.Name == "ListActions");
    }

    [Fact]
    public void Overview_IsBoundedGroundedAndStable()
    {
        var actions = Enumerable.Range(0, 12).Select(index => new ActionMetadata
        {
            Name = $"Action{index:00}", Category = $"Area{index / 3}", Description = $"Supported operation {index}",
            Examples = [$"Real example {index}", $"Another example {index}", $"Extra example {index}"]
        }).ToArray();
        var first = Help(actions);
        Assert.Equal(first, Help(actions.Reverse().ToArray()));
        Assert.True(first.Length < 2500);
        Assert.Contains("Which area", first);
        Assert.Contains("Real example 0", first);
        Assert.DoesNotContain("Extra example", first);
        Assert.DoesNotContain("Action00", first);
    }

    [Fact]
    public void Overview_RemovingAnActionRemovesItsExample()
    {
        var task = new ActionMetadata { Name = "ListTasks", Category = "Tasks", Description = "View tasks", Examples = ["Show my tasks"] };
        Assert.Contains("Show my tasks", Help(task));
        Assert.DoesNotContain("Show my tasks", Help());
        Assert.Contains("unavailable", Help(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overview_LabelsConfirmationAndDoesNotClaimConnections()
    {
        var result = Help(new ActionMetadata { Name = "DeleteTask", Category = "Tasks", Description = "Remove tasks", IsDestructive = true, Examples = ["Delete a task"] });
        Assert.Contains("confirmation", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign-in", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delete a task", result);
        Assert.DoesNotContain("connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overview_DoesNotInventExamplesOrExposeHandlers()
    {
        var result = Help(new ActionMetadata { Name = "InternalHandler", Category = "Tasks", Description = "View tasks" });
        Assert.DoesNotContain("InternalHandler", result);
        Assert.DoesNotContain("Try:", result);
    }

    private static string Help(params ActionMetadata[] actions)
    {
        var registry = new Mock<ICapabilityRegistry>();
        registry.Setup(source => source.GetAll()).Returns(actions);
        MetaActions.SetRegistry(registry.Object);
        return MetaActions.ListActions();
    }

    private static FastPathResolver Resolver()
    {
        var registry = new Mock<IActionRegistry>();
        registry.SetupGet(source => source.Actions).Returns([new ActionMetadata { Name = "ListActions" }]);
        registry.SetupGet(source => source.FastPathActions).Returns([]);
        return new FastPathResolver(registry.Object, Mock.Of<IDailyRecordCommandParser>());
    }
}
