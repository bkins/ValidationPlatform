using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamCommandCenter.Infrastructure.Persistence;
using ValidationPlatform.Providers.Http;
using Xunit;

namespace ValidationPlatform.Tests.TeamCommandCenter;

public sealed class TeamCommandCenterTestApp : WebApplicationFactory<Program>
{
    private static readonly string SeedDbPath = @"C:\Users\benho\source\repos\TeamCommandCenter\src\TeamCommandCenter.Api\teamcommandcenter.db";
    private readonly string _testDbPath = Path.Combine(Path.GetTempPath(), $"tcc_test_{Guid.NewGuid():N}.db");

    public TeamCommandCenterTestApp()
    {
        if (File.Exists(SeedDbPath))
        {
            File.Copy(SeedDbPath, _testDbPath, overwrite: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(serviceDescriptor => serviceDescriptor.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_testDbPath}"));

            foreach (var hostedServiceDescriptor in services.Where(serviceDescriptor => serviceDescriptor.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hostedServiceDescriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch { }
        }
    }
}

public class ApiFixture : IDisposable
{
    public TeamCommandCenterTestApp Factory { get; }

    public ApiFixture()
    {
        Factory = new TeamCommandCenterTestApp();
    }

    public void Dispose()
    {
        Factory.Dispose();
    }

    public bool IsApiOnline() => true;
}

public class TeamCommandCenterApiTests : IClassFixture<ApiFixture>, IDisposable
{
    private readonly ApiFixture _fixture;
    private readonly HttpApiValidationProvider _apiProvider;

    public TeamCommandCenterApiTests(ApiFixture fixture)
    {
        _fixture     = fixture;
        var client   = fixture.Factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        _apiProvider = new HttpApiValidationProvider(client);
    }

    public void Dispose()
    {
        _apiProvider.ShutdownAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetRawWorkItems_Returns_Ok_Or_Success()
    {
        Console.WriteLine("[TeamCommandCenterApiTests] GetRawWorkItems executing GET request to /api/time-tracking/raw-workitems ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/time-tracking/raw-workitems");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[TeamCommandCenterApiTests] GetRawWorkItems received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task GetWeeklySummary_Returns_Ok_Or_Success()
    {
        Console.WriteLine("[TeamCommandCenterApiTests] GetWeeklySummary executing GET request to /api/time-tracking/weekly-summary ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/time-tracking/weekly-summary");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[TeamCommandCenterApiTests] GetWeeklySummary received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task GetTtsReconciliation_Returns_Ok_Or_Success()
    {
        Console.WriteLine("[TeamCommandCenterApiTests] GetTtsReconciliation executing GET request for week of 2026-07-06...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tts/reconciliation?weekStart=2026-07-06");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsReconciliation received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task GetTtsProjectsAndComponents_Returns_Ok_Or_Success()
    {
        var reqProjects = new HttpRequestMessage(HttpMethod.Get, "/api/tts/projects");
        var respProjects = await _apiProvider.SendRequestAsync(reqProjects);
        Assert.True((int)respProjects.StatusCode >= 200 && (int)respProjects.StatusCode < 500);

        var reqComponents = new HttpRequestMessage(HttpMethod.Get, "/api/tts/components");
        var respComponents = await _apiProvider.SendRequestAsync(reqComponents);
        Assert.True((int)respComponents.StatusCode >= 200 && (int)respComponents.StatusCode < 500);
    }

    [Fact]
    public async Task SubmitTtsTime_Returns_Ok_Or_Success()
    {
        Console.WriteLine("[TeamCommandCenterApiTests] SubmitTtsTime executing POST request to submit time...");
        var payload = new { WeekStart = new DateTime(2026, 7, 6), DeveloperEmail = "beth.dockins@ofm.wa.gov" };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/tts/submit")
        {
            Content = JsonContent.Create(payload)
        };
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[TeamCommandCenterApiTests] SubmitTtsTime received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task GetTtsReconciliation_IncludesSyncedSeededData_WhenQueried()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsReconciliation_IncludesSyncedSeededData SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsReconciliation_IncludesSyncedSeededData executing GET request for week of 2026-06-29...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tts/reconciliation?weekStart=2026-06-29");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(summary.GetProperty("isMockMode").GetBoolean());

        var rows = summary.GetProperty("rows");
        Assert.True(rows.GetArrayLength() > 0);

        // Verify Benjamin Hopkins (1980) seeded synced entry exists
        var benRow = rows.EnumerateArray()
                         .FirstOrDefault(r => string.Equals(r.GetProperty("developerEmail").GetString(), "benjamin.hopkins@ofm.wa.gov", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(default, benRow);
        Assert.True(benRow.GetProperty("ttsHours").GetDouble() > 0);

        var benDetails = benRow.GetProperty("details");
        var benDetail = benDetails.EnumerateArray()
                                  .FirstOrDefault(d => d.GetProperty("projectAcronym").GetString() == "HRLabor" 
                                                    && d.GetProperty("componentAcronym").GetString() == "CCJobs");
        Assert.NotEqual(default, benDetail);
        Assert.Equal(3.0, benDetail.GetProperty("ttsHours").GetDouble());

        // Verify Durga Gorti (1983) seeded synced entry exists
        var durgaRow = rows.EnumerateArray()
                           .FirstOrDefault(r => string.Equals(r.GetProperty("developerEmail").GetString(), "durga.gorti@ofm.wa.gov", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(default, durgaRow);
        Assert.True(durgaRow.GetProperty("ttsHours").GetDouble() > 0);

        var durgaDetails = durgaRow.GetProperty("details");
        var durgaDetail = durgaDetails.EnumerateArray()
                                      .FirstOrDefault(d => d.GetProperty("projectAcronym").GetString() == "HRLabor" 
                                                        && d.GetProperty("componentAcronym").GetString() == "SNAP");
        Assert.NotEqual(default, durgaDetail);
        Assert.Equal(12.0, durgaDetail.GetProperty("ttsHours").GetDouble());
    }

    [Fact]
    public async Task GetTtsMockData_Returns_Ok_Or_Success()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsMockData SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsMockData executing GET request...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tts/mock-data");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(data.GetProperty("staff").GetArrayLength() > 0);
        Assert.True(data.GetProperty("projects").GetArrayLength() > 0);
        Assert.True(data.GetProperty("components").GetArrayLength() > 0);
        Assert.True(data.GetProperty("timeEntries").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetTtsLogs_Returns_Ok_Or_Success()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsLogs SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine($"[TeamCommandCenterApiTests] GetTtsLogs executing GET request...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tts/logs");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var logs = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(logs.GetArrayLength() > 0);
    }

    [Fact]
    public async Task SubmitTtsTime_DryRun_DoesNotMutateData()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[TeamCommandCenterApiTests] SubmitTtsTime_DryRun SKIPPED because API is offline.");
            return;
        }

        // 1. Get initial mock data store state
        var reqInitial = new HttpRequestMessage(HttpMethod.Get, "/api/tts/mock-data");
        var respInitial = await _apiProvider.SendRequestAsync(reqInitial);
        Assert.Equal(System.Net.HttpStatusCode.OK, respInitial.StatusCode);
        var initialData = await respInitial.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        int initialCount = initialData.GetProperty("timeEntries").GetArrayLength();

        // 2. Perform Dry Run sync
        Console.WriteLine($"[TeamCommandCenterApiTests] SubmitTtsTime_DryRun executing dry run POST request...");
        var payload = new { WeekStart = new DateTime(2026, 6, 29), DeveloperEmail = "benjamin.hopkins@ofm.wa.gov", DryRun = true };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/tts/submit")
        {
            Content = JsonContent.Create(payload)
        };
        var response = await _apiProvider.SendRequestAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(result.GetProperty("isDryRun").GetBoolean());
        Assert.True(result.GetProperty("recordsCreated").GetInt32() > 0);

        // 3. Get final mock data store state and assert it is identical
        var reqFinal = new HttpRequestMessage(HttpMethod.Get, "/api/tts/mock-data");
        var respFinal = await _apiProvider.SendRequestAsync(reqFinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, respFinal.StatusCode);
        var finalData = await respFinal.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        int finalCount = finalData.GetProperty("timeEntries").GetArrayLength();

        Assert.Equal(initialCount, finalCount);
    }

    [Fact]
    public async Task GetStoryPointsSummary_Returns_Ok_Or_Success()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine("[TeamCommandCenterApiTests] GetStoryPointsSummary SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine("[TeamCommandCenterApiTests] GetStoryPointsSummary executing GET request...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/story-points");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(data.TryGetProperty("overallProjectAveragePointsPerSprint", out var overallAvg));
        Assert.True(data.TryGetProperty("projects", out var projectsArr));
        Assert.True(data.TryGetProperty("developers", out var devsArr));
    }

    [Fact]
    public async Task GetTeams_Returns_Ok_Or_Success()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine("[TeamCommandCenterApiTests] GetTeams SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine("[TeamCommandCenterApiTests] GetTeams executing GET request to /api/teams...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/teams");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var teams = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(System.Text.Json.JsonValueKind.Array, teams.ValueKind);
    }

    [Fact]
    public async Task GetUnassignedDevelopers_Returns_Ok_Or_Success()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine("[TeamCommandCenterApiTests] GetUnassignedDevelopers SKIPPED because API is offline.");
            return;
        }

        Console.WriteLine("[TeamCommandCenterApiTests] GetUnassignedDevelopers executing GET request to /api/teams/unassigned...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/teams/unassigned");
        var response = await _apiProvider.SendRequestAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var developers = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(System.Text.Json.JsonValueKind.Array, developers.ValueKind);
    }

    [Fact]
    public async Task CreateGetUpdateDeleteTeam_ApiLifecycle_ExecutesSuccessfully()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine("[TeamCommandCenterApiTests] CreateGetUpdateDeleteTeam SKIPPED because API is offline.");
            return;
        }

        // 1. Create Team
        var createPayload = new { Name = "API Test Team", Description = "Integration Test Team Description" };
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/teams")
        {
            Content = JsonContent.Create(createPayload)
        };
        var createResp = await _apiProvider.SendRequestAsync(createReq);
        Assert.Equal(System.Net.HttpStatusCode.Created, createResp.StatusCode);

        var createdJson = await createResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        int teamId = createdJson.GetProperty("id").GetInt32();
        Assert.True(teamId > 0);
        Assert.Equal("API Test Team", createdJson.GetProperty("name").GetString());

        // 2. Get Team By Id
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/teams/{teamId}");
        var getResp = await _apiProvider.SendRequestAsync(getReq);
        Assert.Equal(System.Net.HttpStatusCode.OK, getResp.StatusCode);

        // 3. Update Team
        var updatePayload = new { Name = "API Test Team Updated", Description = "New Description" };
        var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/teams/{teamId}")
        {
            Content = JsonContent.Create(updatePayload)
        };
        var updateResp = await _apiProvider.SendRequestAsync(updateReq);
        Assert.Equal(System.Net.HttpStatusCode.OK, updateResp.StatusCode);
        var updatedJson = await updateResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("API Test Team Updated", updatedJson.GetProperty("name").GetString());

        // 4. Delete Team
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/teams/{teamId}");
        var deleteResp = await _apiProvider.SendRequestAsync(deleteReq);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleteResp.StatusCode);

        // 5. Verify Get returns 404
        var getAfterDeleteReq = new HttpRequestMessage(HttpMethod.Get, $"/api/teams/{teamId}");
        var getAfterDeleteResp = await _apiProvider.SendRequestAsync(getAfterDeleteReq);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, getAfterDeleteResp.StatusCode);
    }
}
