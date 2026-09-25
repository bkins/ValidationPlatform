namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class Story284PayloadIntegrityContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void ReleaseManagement_RecordsSourceIdentitiesAndVerifiesTheWindowsLaaPayloadManifest()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var page = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseConsole.razor.txt"));
        var sourceIdentityService = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseSourceIdentityService.cs.txt"));
        var buildScript = File.ReadAllText(Path.Combine(fixtureDirectory, "LAA-Windows-Build.ps1.txt"));
        var deployScript = File.ReadAllText(Path.Combine(fixtureDirectory, "LAA-Windows-Deploy.ps1.txt"));
        var manifestTools = File.ReadAllText(Path.Combine(fixtureDirectory, "PayloadManifest.ps1.txt"));

        Assert.Contains("CreateSourceIdentityAsync(componentScope)", page);
        Assert.Contains("CognitivePlatformCommitHash", page);
        Assert.Contains("LocalAIAssistantCommitHash", page);
        Assert.Contains("SystemPaths:CognitivePlatformRepo", sourceIdentityService);
        Assert.Contains("SystemPaths:LocalAIAssistantRepo", sourceIdentityService);
        Assert.Contains("New-PayloadManifest -RootPath $OutputPath", buildScript);
        Assert.Contains("Test-PayloadManifest -RootPath $artifactPathResolved", deployScript);
        Assert.Contains("Test-PayloadManifest -RootPath $stagingPath", deployScript);
        Assert.Contains("Test-PayloadManifest -RootPath $deployPath", deployScript);
        Assert.Contains("payload-manifest.json", manifestTools);
        Assert.Contains("[System.StringComparer]::Ordinal", manifestTools);
    }
}
