namespace ValidationPlatform.Tests.CognitiveApi;

public class Story286TaskKnowledgeWorkspaceContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void TaskKnowledgeSource_UsesActiveWorkspaceForFullItemsAndHeaders()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var source = File.ReadAllText(Path.Combine(fixtureDirectory, "TaskKnowledgeSource.cs.txt"));

        Assert.Contains("IWorkspaceContext workspaceContext", source);
        Assert.Equal(2, CountOccurrences(source, "partitionKey: _workspaceContext.ActivePartitionKey"));
        Assert.DoesNotContain("List<TaskItem>(partitionKey: null", source);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
