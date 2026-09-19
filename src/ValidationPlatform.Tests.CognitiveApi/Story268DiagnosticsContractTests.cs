namespace ValidationPlatform.Tests.CognitiveApi;

public class Story268DiagnosticsContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void ProviderFailures_PreserveFallbackAndDiagnosticContracts()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var geminiSource = File.ReadAllText(Path.Combine(fixtureDirectory, "GeminiLlmClient.cs.txt"));
        var routerSource = File.ReadAllText(Path.Combine(fixtureDirectory, "LlmRouter.cs.txt"));
        var orchestratorSource = File.ReadAllText(Path.Combine(fixtureDirectory, "ConversationOrchestrator.cs.txt"));
        var loggerSource = File.ReadAllText(Path.Combine(fixtureDirectory, "InMemoryLogger.cs.txt"));
        var settings = File.ReadAllText(Path.Combine(fixtureDirectory, "appsettings.json"));

        Assert.Contains("throw new HttpRequestException(errorMessage, null, response.StatusCode);", geminiSource);
        Assert.Contains("context.Metadata[\"resolved_provider\"] = resolvedProvider;", routerSource);
        Assert.Contains("Diagnostic ID: {diagnosticId}", routerSource);
        Assert.DoesNotContain("check if Ollama is running locally", routerSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Metadata.TryRemove(\"resolved_provider\"", orchestratorSource);
        Assert.Contains("System.Net.Http.HttpClient.OllamaEmbedding", loggerSource);
        Assert.Contains("\"Model\": \"qwen2.5:7b\"", settings);
    }
}
