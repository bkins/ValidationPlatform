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
    public async Task Selected_Log_Deletion_Is_Durable_And_Does_Not_Remove_Duplicate_Record()
    {
        var logPath = Path.GetTempFileName();
        var deletionPath = $"{logPath}.deleted";
        try
        {
            File.WriteAllText(logPath, "same\nsame\n");
            var records = NewestLogLineReader.ReadRecords(logPath, 2);
            var store = new DeletedLogEntryStore(deletionPath);

            var deleted = await store.AddAsync(logPath, records[0].StorageId, records[0].Offset, records[0].Text);
            var persisted = await store.GetAllAsync();

            Assert.True(deleted);
            Assert.Contains(records[0].StorageId, persisted);
            Assert.DoesNotContain(records[1].StorageId, persisted);
        }
        finally
        {
            File.Delete(logPath);
            if (File.Exists(deletionPath)) File.Delete(deletionPath);
        }
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void Logs_Page_Provides_Explicit_Detail_Navigation_And_Date_Filter_Label()
    {
        var markup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogsPage.xaml.txt"));
        var codeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogsPage.xaml.cs.txt"));
        var detailMarkup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogDetailPage.xaml.txt"));
        var detailCodeBehind = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LogDetailPage.xaml.cs.txt"));

        Assert.Contains("Text=\"Filter by date range\"", markup);
        Assert.Contains("WidthRequest=\"52\"", markup);
        Assert.Contains("HandlerChanged=\"OnDateFilterSwitchHandlerChanged\"", markup);
        Assert.Contains("toggleSwitch.MinWidth = 0", codeBehind);
        Assert.Contains("AutomationId=\"ViewLogDetailsButton\"", markup);
        Assert.Contains("<Button Grid.Column=\"4\"", markup);
        Assert.Contains("Clicked=\"OnViewDetailsClicked\"", markup);
        Assert.Contains("MinimumHeightRequest=\"0\"", markup);
        Assert.Contains("Text=\"Details ›\"", markup);
        Assert.DoesNotContain("<Button Grid.Row=\"3\"", markup);
        Assert.DoesNotContain("StaticResource Gray800", detailMarkup);
        Assert.Contains("GoToAsync(nameof(LogDetailPage)", codeBehind);
        Assert.Contains("AutomationId=\"DeleteLogEntryButton\"", detailMarkup);
        Assert.Contains("<Editor Text=\"{Binding Message}\"", detailMarkup);
        Assert.Contains("<Editor Text=\"{Binding Exception}\"", detailMarkup);
        Assert.Contains("IsReadOnly=\"True\"", detailMarkup);
        Assert.Contains("DisplayAlert(\"Delete log entry?\"", detailCodeBehind);
        Assert.Contains("DeleteLogEntryAsync", detailCodeBehind);
        Assert.Contains("DeleteEntryButton.Text = \"Deleting...\"", detailCodeBehind);
    }
}
