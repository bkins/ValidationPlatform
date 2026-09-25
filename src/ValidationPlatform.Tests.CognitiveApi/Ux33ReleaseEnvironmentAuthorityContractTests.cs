namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class Ux33ReleaseEnvironmentAuthorityContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void ReleaseManagement_UsesOneCapturedHeaderEnvironmentAuthority()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var page = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseConsole.razor.txt"));

        Assert.Contains("@inject EnvironmentService", page);
        Assert.Contains("Header target: @SelectedEnvironment", page);
        Assert.Contains("SelectedEnvironment => _operationEnvironment ?? ValidateEnvironment(EnvService.Current)", page);
        Assert.Contains("CaptureOperationEnvironment()", page);
        Assert.DoesNotContain("_selectedEnvironment", page);
        Assert.DoesNotContain("DeployArtifactAsync(capturedArtifact, capturedEnv)", page);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void ReleaseManagement_ProductionOperationsAreConfirmedAndRevalidated()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var page = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseConsole.razor.txt"));

        Assert.Contains("ConfirmProductionOperationAsync", page);
        Assert.Contains("Run {operation} for {component} against PROD", page);
        Assert.Contains("RevalidateProductionTarget", page);
        Assert.Contains("Production target changed before execution. Operation blocked.", page);
    }
}
