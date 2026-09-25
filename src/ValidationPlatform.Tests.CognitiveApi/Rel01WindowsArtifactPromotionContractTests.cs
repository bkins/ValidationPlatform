namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class Rel01WindowsArtifactPromotionContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void WindowsArtifactPromotion_UsesStoredManifestAndDeployOnlyScript()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var page             = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseConsole.razor.txt"));

        Assert.Contains("IsLaaWindowsDirectory", page);
        Assert.Contains("payload-manifest.json", page);
        Assert.Contains("DeployLaaWindowsArtifactAsync", page);
        Assert.Contains("LAA-Windows-Deploy.ps1", page);
        Assert.Contains("-ArtifactPath \"\"{artifact.Directory}\"\"", page);
        Assert.Contains("-Version \"\"{artifact.Version}\"\"", page);
    }
}
