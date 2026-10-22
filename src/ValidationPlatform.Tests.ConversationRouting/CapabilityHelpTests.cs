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

    [Fact]
    public void Overview_UsesDifferentOperationsAndOmitsDuplicateGeneratedCrud()
    {
        var result = Help(
            new ActionMetadata { Name = "AddJournalEntry", Category = "Journal", Description = "Write a journal entry", Examples = ["Write in my journal", "Add a journal entry"] },
            new ActionMetadata { Name = "AddJournalEntry_Generated", Category = "Journal", Description = "Adds a new JournalEntry.", Examples = ["Generated add"] },
            new ActionMetadata { Name = "SearchJournalEntries", Category = "Journal", Description = "Search journal entries", Examples = ["Search my journal"] });
        Assert.DoesNotContain("JournalEntry", result);
        Assert.Contains("Search journal entries", result);
        Assert.Contains("Search my journal", result);
        Assert.DoesNotContain("Try: “Add a journal entry”", result);
        Assert.DoesNotContain("Generated add", result);
    }

    [Fact]
    public void Overview_LongDescriptionEndsAtWordBoundary()
    {
        var result = Help(new ActionMetadata { Name = "Example", Category = "Tasks", Description = new string('x', 130) + " unfinishedword following text" });
        Assert.Contains(new string('x', 130) + "...", result);
        Assert.DoesNotContain(" unfin", result);
    }

    [Fact]
    public void Overview_PrefersActualDailyCategoryWithinSixGroups()
    {
        var actions = Enumerable.Range(0, 8).Select(index => new ActionMetadata
        { Name = $"Other{index}", Category = $"Area{index}", Description = "Other area" }).ToList();
        actions.Add(new ActionMetadata { Name = "OpenDay", Category = "Daily", Description = "Plan today", Examples = ["Open my day"] });
        var result = Help(actions.ToArray());
        Assert.Contains("**Daily**", result);
        Assert.Contains("Open my day", result);
        Assert.DoesNotContain("**Area7**", result);
    }

    [Fact]
    public void DomainGuidance_DirectsFullDirectoryToExplicitCommand()
    {
        Assert.Contains("List all commands", CognitivePlatform.Api.Actions.GuidanceActions.GetDomainGuidance("Tasks"));
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
