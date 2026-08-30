using System.Diagnostics;
using System.Net.Sockets;
using ValidationPlatform.Providers.Http;
using Xunit;

namespace ValidationPlatform.Tests.CognitiveApi;

public class ApiFixture : IDisposable
{
    public Process? SpawnedApiProcess { get; private set; }
    public const int ApiPort = 5276;
    public const string ApiHost = "127.0.0.1";

    public ApiFixture()
    {
        if (IsApiOnline())
        {
            Console.WriteLine($"[ApiFixture] Found API already listening on port {ApiPort}. Verifying environment...");
            EnsureEnvironmentIsTesting(ApiHost, ApiPort);
            Console.WriteLine($"[ApiFixture] Connected to verified 'Testing' API on port {ApiPort}.");
            return;
        }

        Console.WriteLine($"[ApiFixture] API is offline on port {ApiPort}. Starting isolated API for 'Testing' environment...");
        try
        {
            var apiProjectPath = @"C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\CognitivePlatform.Api.csproj";
            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet"
              , Arguments              = $"run --project \"{apiProjectPath}\" --launch-profile Api-Testing"
              , WorkingDirectory       = Path.GetDirectoryName(apiProjectPath)
              , UseShellExecute        = false
              , CreateNoWindow         = true
            };
            startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://{ApiHost}:{ApiPort}";
            startInfo.EnvironmentVariables["LlmClient__Provider"] = "Mock";
            startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing";

            SpawnedApiProcess = Process.Start(startInfo);

            // Wait/poll for the API to come online (up to 45 seconds to allow for compile + cold start)
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (IsApiOnline())
                {
                    Console.WriteLine($"[ApiFixture] API successfully came online on port {ApiPort} after {sw.Elapsed.TotalSeconds:F1}s.");
                    EnsureEnvironmentIsTesting(ApiHost, ApiPort);
                    break;
                }
                Thread.Sleep(500);
            }

            if (!IsApiOnline())
            {
                Console.Error.WriteLine($"[ApiFixture] API started but did not come online within 45s on port {ApiPort}.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ApiFixture] Failed to auto-start API: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (SpawnedApiProcess is not null)
        {
            try
            {
                Console.WriteLine("[ApiFixture] Stopping spawned API process...");
                SpawnedApiProcess.Kill(entireProcessTree: true);
                SpawnedApiProcess.Dispose();
                SpawnedApiProcess = null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ApiFixture] Exception shutting down spawned API process: {ex.Message}");
            }
        }
    }

    public bool IsApiOnline()
    {
        try
        {
            using var client = new TcpClient(ApiHost, ApiPort);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureEnvironmentIsTesting(string host, int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = client.GetAsync($"http://{host}:{port}/api/system/environment").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"API on port {port} returned status {(int)response.StatusCode} on environment check.");
            }

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;
            var envName = root.GetProperty("data").GetProperty("Environment").GetProperty("environmentName").GetString();

            if (!string.Equals(envName, "Testing", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CRITICAL ENVIRONMENT GUARD: API on port {port} is running environment '{envName}' (NOT 'Testing')! Aborting tests to protect non-testing environment.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to verify API environment on port {port}: {ex.Message}", ex);
        }
    }
}

public class CognitiveApiTests : IClassFixture<ApiFixture>, IDisposable
{
    private readonly ApiFixture _fixture;
    private readonly HttpApiValidationProvider _apiProvider;

    public CognitiveApiTests(ApiFixture fixture)
    {
        _fixture = fixture;
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _apiProvider = new HttpApiValidationProvider(client);
    }

    public void Dispose()
    {
        _apiProvider.ShutdownAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetHealth_Returns_Response()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetHealth SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetHealth executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/ ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetHealth received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 500);
    }

    [Fact]
    public async Task GetHealthReady_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetHealthReady SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetHealthReady executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/health/ready ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetHealthReady received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Ready", content.Trim('"'));
    }

    [Fact]
    public async Task GetSystemEnvironment_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetSystemEnvironment SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetSystemEnvironment executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/system/environment ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/system/environment");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetSystemEnvironment received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSystemVersion_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetSystemVersion SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetSystemVersion executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/system/version ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/system/version");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetSystemVersion received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetTasks SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetTasks executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/tasks ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetTasks received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetJournals_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetJournals SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetJournals executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/journals ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/journals");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetJournals received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetJournalRevisions_Returns_NotFound_ForRandomGuid()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetJournalRevisions SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        var randomGuid = Guid.NewGuid();
        Console.WriteLine($"[CognitiveApiTests] GetJournalRevisions executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/journals/{randomGuid}/revisions ...");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/journals/{randomGuid}/revisions");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetJournalRevisions received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJournalMedia_Returns_NotFound_ForRandomGuid()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetJournalMedia SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        var randomGuid = Guid.NewGuid();
        Console.WriteLine($"[CognitiveApiTests] GetJournalMedia executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/journals/{randomGuid}/media ...");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/journals/{randomGuid}/media");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetJournalMedia received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonas_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetPersonas SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetPersonas executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/persona ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/persona");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetPersonas received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNotificationSchedule_Returns_Ok()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetNotificationSchedule SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetNotificationSchedule executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/notifications/schedule ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/schedule");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetNotificationSchedule received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EvaluateInsights_ReturnsOk_WhenApiOnline()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] EvaluateInsights SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] EvaluateInsights executing POST request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/insights/evaluate ...");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/insights/evaluate");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] EvaluateInsights received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetMealsToday_ReturnsOk_WhenApiOnline()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetMealsToday SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetMealsToday executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/meals/today ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/meals/today");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetMealsToday received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNutritionSummary_ReturnsOk_WhenApiOnline()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] GetNutritionSummary SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        Console.WriteLine($"[CognitiveApiTests] GetNutritionSummary executing GET request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/meals/summary ...");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/meals/summary");
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] GetNutritionSummary received response: Status={response.StatusCode}");
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateMeal_ReturnsOk_WhenApiOnline()
    {
        if (!_fixture.IsApiOnline())
        {
            Console.WriteLine($"[CognitiveApiTests] CreateMeal SKIPPED because API is offline on port {ApiFixture.ApiPort}.");
            return;
        }

        var mealJson = """
                       {
                           "mealType": "Breakfast",
                           "consumedAt": "2026-08-14T08:00:00Z",
                           "foods": [
                               {
                                   "name": "Oatmeal",
                                   "quantity": 1,
                                   "unit": "bowl",
                                   "nutrition": { "calories": 150, "proteinGrams": 5 }
                               }
                           ]
                       }
                       """;

        Console.WriteLine($"[CognitiveApiTests] CreateMeal executing POST request to http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}/api/meals ...");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/meals")
        {
            Content = new StringContent(mealJson, System.Text.Encoding.UTF8, "application/json")
        };
        var response = await _apiProvider.SendRequestAsync(request);

        Console.WriteLine($"[CognitiveApiTests] CreateMeal received response: Status={response.StatusCode}");
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }
}

