namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class Story292ReleaseVersionContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void ReleaseManagement_ReservesAndDisplaysOneImmutableMonotonicVersion()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var service = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseVersionService.cs.txt"));
        var contract = File.ReadAllText(Path.Combine(fixtureDirectory, "IReleaseVersionService.cs.txt"));
        var page = File.ReadAllText(Path.Combine(fixtureDirectory, "ReleaseConsole.razor.txt"));

        Assert.Contains("FindHighestIssuedBuild", service);
        Assert.Contains("new Mutex", service);
        Assert.Contains("File.Move(temporaryPath, _versionJsonPath, overwrite: true)", service);
        Assert.Contains("ReleaseVersionReservation Reserve", contract);
        Assert.Contains("ReleaseVersionReservation StartNew", contract);
        Assert.Contains("Reserved automatically when a build or release starts.", page);
        Assert.Contains("ReadOnly=\"true\"", page);
        Assert.Contains("EnsureVersionAsync(\"API,CocoAPI,Laa,LaaWindows\")", page);
        Assert.DoesNotContain("ReleaseVersionService.IncrementBuild()", page);
    }
}
