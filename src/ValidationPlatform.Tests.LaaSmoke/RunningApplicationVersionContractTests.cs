using LocalAIAssistant.Core.Versioning;

namespace ValidationPlatform.Tests.LaaSmoke;

public sealed class RunningApplicationVersionContractTests
{
    [Theory]
    [InlineData("1.3.0", "111", "v1.3.0.111")]
    [InlineData("1.4.0", "7", "v1.4.0.7")]
    [InlineData("1.4.0.7", "7", "v1.4.0.7")]
    [Trait("Category", "ClientContract")]
    public void ShellVersion_UsesPackageDisplayAndBuildVersions( string displayVersion
                                                               , string buildVersion
                                                               , string expected)
    {
        var result = RunningApplicationVersion.Format(displayVersion, buildVersion);

        Assert.Equal(expected, result);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void ShellHeader_ExposesVersionAsAccessibleAutomationTarget()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppShell.xaml.txt"));
        var project = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocalAIAssistant.Ui.Maui.csproj.txt"));

        Assert.Contains("AutomationId=\"ApplicationVersionLabel\"", markup);
        Assert.Contains("Text=\"{Binding ApplicationVersionText}\"", markup);
        Assert.Contains("SemanticProperties.Description=\"Running application version\"", markup);
        Assert.Contains("public const string Version = &quot;$(ApplicationDisplayVersion).$(ApplicationVersion)&quot;%3B", project);
    }
}
