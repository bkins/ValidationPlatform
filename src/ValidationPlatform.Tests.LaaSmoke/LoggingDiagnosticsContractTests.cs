using LocalAIAssistant.Services.Logging;

namespace ValidationPlatform.Tests.LaaSmoke;

public class LoggingDiagnosticsContractTests
{
    [Fact]
    [Trait("Category", "ClientContract")]
    public void Export_Redaction_Removes_Credentials_But_Preserves_Diagnostic_Id()
    {
        var values = LogRedaction.Properties(new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer private-token",
            ["DiagnosticId"] = "diag-123"
        }).ToDictionary();

        Assert.Equal("[REDACTED]", values["Authorization"]);
        Assert.Equal("diag-123", values["DiagnosticId"]);
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void Newest_First_Reader_Is_Bounded_And_Preserves_Unicode()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "old\nnewer 😀\nnewest\n");
            Assert.Equal(new[] { "newest", "newer 😀" }, NewestLogLineReader.Read(path, 2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void Logs_Page_Provides_Explicit_Detail_Navigation_And_Date_Filter_Label()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogsPage.xaml.txt"));
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogsPage.xaml.cs.txt"));
        var detailMarkup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogDetailPage.xaml.txt"));

        Assert.Contains("Text=\"Filter by date range\"", markup);
        Assert.Contains("WidthRequest=\"52\"", markup);
        Assert.Contains("AutomationId=\"ViewLogDetailsButton\"", markup);
        Assert.Contains("Tapped=\"OnViewDetailsTapped\"", markup);
        Assert.Contains("Text=\"Details ›\"", markup);
        Assert.DoesNotContain("<Button Grid.Row=\"3\"", markup);
        Assert.DoesNotContain("StaticResource Gray800", detailMarkup);
        Assert.Contains("GoToAsync(nameof(LogDetailPage)", codeBehind);
    }
}
