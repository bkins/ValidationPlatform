using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class TtrMigrationTests : IClassFixture<ApiFixture>, IDisposable
{
    private readonly ApiFixture _fixture;
    private readonly string     _root;
    private readonly string     _databasePath;

    public TtrMigrationTests(ApiFixture fixture)
    {
        _fixture      = fixture;
        _root         = Path.Combine(Path.GetTempPath(), $"cp-ttr-validation-{Guid.NewGuid():N}");
        _databasePath = Path.Combine(_root, "synthetic.db3");
        Directory.CreateDirectory(_root);
        CreateSyntheticFixture();
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("personal")]
    public async Task Plan_SyntheticImmutableFixture_ReturnsDeterministicZeroWritePlan(string targetWorkspace)
    {
        if (!_fixture.IsApiOnline()) return;
        using var client = new HttpClient { BaseAddress = new Uri($"http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}") };
        client.DefaultRequestHeaders.Add("X-Admin-Secret", ApiFixture.AdminSecret);
        var batchesBefore = await ReadBatchIdsAsync(client);
        var sourceHash    = await ComputeSha256Async(_databasePath);
        var request       = new
                            {
                                databasePath           = _databasePath
                              , recoveryBundlePath     = _root
                              , expectedDatabaseSha256 = sourceHash
                              , logicalSourceInstance  = "validation-synthetic"
                              , sourceTimeZoneId       = "America/Los_Angeles"
                              , targetPartition        = targetWorkspace
                            };

        var firstResponse  = await client.PostAsJsonAsync("/api/admin/journal/import/ttr/plan", request);
        var secondResponse = await client.PostAsJsonAsync("/api/admin/journal/import/ttr/plan", request);

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        using var first  = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(sourceHash, first.RootElement.GetProperty("sourceSha256").GetString());
        Assert.Equal(first.RootElement.GetProperty("planSha256").GetString(), second.RootElement.GetProperty("planSha256").GetString());
        Assert.Equal(1, first.RootElement.GetProperty("readyEntryCount").GetInt32());
        Assert.Equal(targetWorkspace, first.RootElement.GetProperty("targetPartition").GetString());
        var entry = Assert.Single(first.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Matches("^[0-9a-f]{32}$", entry.GetProperty("entryId").GetString()!);
        Assert.Matches("^[0-9a-f]{32}$", entry.GetProperty("revisionId").GetString()!);
        Assert.Equal(batchesBefore, await ReadBatchIdsAsync(client));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void CreateSyntheticFixture()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Mood (Id INTEGER PRIMARY KEY, Title TEXT, Emoji TEXT);
            CREATE TABLE Journal (Id INTEGER PRIMARY KEY, Title TEXT, JournalTypeId INTEGER);
            CREATE TABLE Entry (Id INTEGER PRIMARY KEY, Title TEXT, Text TEXT, CreateDateTime INTEGER, JournalId INTEGER, MoodId INTEGER, Image BLOB, ImageFileName TEXT, Video BLOB, VideoFileName TEXT, OriginalJournalId INTEGER);
            CREATE TABLE JournalType (Id INTEGER PRIMARY KEY, Title TEXT);
            CREATE TABLE Media (Id INTEGER PRIMARY KEY, MediaBytes BLOB, MediaFileName TEXT, Type INTEGER, EntryId INTEGER);
            CREATE TABLE Notification (Id INTEGER PRIMARY KEY);
            INSERT INTO Mood VALUES (1, 'Good', '🙂');
            INSERT INTO JournalType VALUES (1, 'Personal');
            INSERT INTO Journal VALUES (1, 'Validation', 1);
            INSERT INTO Entry VALUES (1, 'Synthetic title', '<p>Synthetic body</p>', 637451534450000000, 1, 1, NULL, NULL, NULL, NULL, 0);
            """;
        command.ExecuteNonQuery();
    }

    private static async Task<string[]> ReadBatchIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/journal/import/ttr/batches");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray()
                       .Select(batch => batch.GetProperty("id").GetString() ?? string.Empty)
                       .OrderBy(id => id, StringComparer.Ordinal)
                       .ToArray();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
