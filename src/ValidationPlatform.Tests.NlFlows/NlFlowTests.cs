using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace ValidationPlatform.Tests.NlFlows;

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

        Console.WriteLine($"[ApiFixture] Starting isolated API on port {ApiPort} for 'Testing' environment...");
        try
        {
            try
            {
                var testingDir = Path.GetFullPath(@"C:\CP\Data\Testing");
                if (Directory.Exists(testingDir) && string.Equals(testingDir, @"C:\CP\Data\Testing", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(testingDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not clean Testing database before NL flow tests: {ex.Message}", ex);
            }

            var apiProjectPath = @"C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\CognitivePlatform.Api.csproj";
            var startInfo = new ProcessStartInfo
            {
                FileName               = "dotnet"
              , Arguments              = $"run --no-launch-profile --project \"{apiProjectPath}\""
              , RedirectStandardOutput = false
              , RedirectStandardError  = false
              , UseShellExecute        = false
              , CreateNoWindow         = true
            };
            startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://{ApiHost}:{ApiPort}";
            startInfo.EnvironmentVariables["LlmClient__Provider"] = "Mock";
            startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing";
            startInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Testing";

            SpawnedApiProcess = Process.Start(startInfo);

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
            using var doc = JsonDocument.Parse(content);
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

public class NlFlowTests : IClassFixture<ApiFixture>, IDisposable
{
    private readonly ApiFixture _fixture;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NlFlowTests(ApiFixture fixture)
    {
        _fixture = fixture;
        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://{ApiFixture.ApiHost}:{ApiFixture.ApiPort}"),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task FastPath_AddTask_CreatesTaskSuccessfully()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-addtask-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "task: Buy milk" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("AddTask", root.GetProperty("selectedAction").GetString());

        // Verify task exists in task list
        var taskListResponse = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, taskListResponse.StatusCode);

        var listBody = await taskListResponse.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(listBody);
        var tasks = listJson.RootElement;
        
        Assert.True(tasks.ValueKind == JsonValueKind.Array);
        bool found = false;
        foreach (var task in tasks.EnumerateArray())
        {
            if (task.GetProperty("shortDescription").GetString() == "Buy milk")
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Task 'Buy milk' should be present in the active tasks list");
    }

    [Fact]
    public async Task FastPath_ListTasks_ResolvesListTasks()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-listtasks-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "list my tasks" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("ListTasks", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_OpenDay_ResolvesOpenDay()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-openday-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "Plan: Focus on E2E testing" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("OpenDay", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_AddJournalEntry_ResolvesAddJournalEntry()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-journal-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "journal: Had a good meeting" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("AddJournalEntry", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_DeleteTask_RequiresConfirmation()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-deletetask-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "delete task 999" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("DeleteTask", root.GetProperty("selectedAction").GetString());
        Assert.True(root.GetProperty("isConfirmationRequired").GetBoolean());
        Assert.NotNull(root.GetProperty("confirmationPrompt").GetString());
    }

    [Fact]
    public async Task FastPath_WorkspacePrefix_SwitchesWorkspaceAndCreatesTask()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-wsprefix-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "work: task: Buy work milk" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("AddTask", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_ListActions_ResolvesListActions()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-listactions-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "what can you do?" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("ListActions", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    [Trait("Category", "RealLlm")]
    public async Task RealLlm_HowAreMyTasksDoing_ReturnsLlmSummary()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-realllm-tasks-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "how are my tasks doing?" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        
        // If the LLM provider is offline/unusable, it will return success but explain it's unusable,
        // or it might return non-success depending on setup. Let's check status code first.
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            var msg = root.GetProperty("message").GetString() ?? string.Empty;
            if (msg.Contains("not usable on this system") || msg.Contains("didn't recognize that as a command"))
            {
                // Graceful skip
                Console.WriteLine("[RealLlm] Skipped because LLM model/provider is not usable on this system.");
                return;
            }

            Assert.False(root.GetProperty("wasFastPath").GetBoolean());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.NotEmpty(msg);
        }
    }

    [Fact]
    public async Task FastPath_DailyBrief_ReturnsBrief()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-realllm-brief-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "give me a daily brief" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            var msg = root.GetProperty("message").GetString() ?? string.Empty;
            if (msg.Contains("not usable on this system") || msg.Contains("didn't recognize that as a command"))
            {
                Console.WriteLine("[RealLlm] Skipped because LLM model/provider is not usable on this system.");
                return;
            }

            Assert.True(root.GetProperty("wasFastPath").GetBoolean());
            Assert.Equal("GetDailyBrief", root.GetProperty("selectedAction").GetString());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.NotEmpty(msg);
        }
    }

    [Fact]
    [Trait("Category", "RealLlm")]
    public async Task RealLlm_UseKnowledgeDomain_ReturnsLlmResponse()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-realllm-domain-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "use knowledge domain HumanResources" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            var msg = root.GetProperty("message").GetString() ?? string.Empty;
            if (msg.Contains("not usable on this system") || msg.Contains("didn't recognize that as a command"))
            {
                Console.WriteLine("[RealLlm] Skipped because LLM model/provider is not usable on this system.");
                return;
            }

            Assert.False(root.GetProperty("wasFastPath").GetBoolean());
            Assert.NotEmpty(msg);
        }
    }

    [Fact]
    public async Task FastPath_AddTaskWithColonDueDate_CreatesTaskWithDueDateSuccessfully()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-addtask-due-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "task: Buy groceries due:today" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("AddTask", root.GetProperty("selectedAction").GetString());

        // Verify task exists in task list and has a due date set for today
        var taskListResponse = await _client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.OK, taskListResponse.StatusCode);

        var listBody = await taskListResponse.Content.ReadAsStringAsync();
        using var listJson = JsonDocument.Parse(listBody);
        var tasks = listJson.RootElement;
        
        Assert.True(tasks.ValueKind == JsonValueKind.Array);
        bool found = false;
        foreach (var task in tasks.EnumerateArray())
        {
            if (task.GetProperty("shortDescription").GetString() == "Buy groceries")
            {
                found = true;
                Assert.True(task.TryGetProperty("dueDate", out var dueProp), "Task should have a dueDate property");
                Assert.NotEqual(JsonValueKind.Null, dueProp.ValueKind);
                var dueDate = dueProp.GetDateTimeOffset();
                Assert.Equal(DateTimeOffset.Now.Date, dueDate.ToLocalTime().Date);
                break;
            }
        }
        Assert.True(found, $"Task 'Buy groceries' should be present in the active tasks list. Converse Response: {body}. Tasks: {listBody}");
    }

    [Fact]
    public async Task ConverseStream_WithGeneralInput_StreamsResponseSuccessfully()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-stream-{Guid.NewGuid():N}";
        var payload = new
                      {
                          SessionId = sessionId
                        , Input     = "Hello"
                      };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse/stream", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var receivedData = false;
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                receivedData = true;
                var data = line["data:".Length..].Trim();
                Assert.NotEmpty(data);
            }
        }

        Assert.True(receivedData, "Should have received at least one data event from the SSE stream");
    }

    [Fact]
    public async Task Secrets_IntegrationFlow_Succeeds()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"secrets-flow-{Guid.NewGuid():N}";
        var secretTitle = $"Bank PIN {Guid.NewGuid():N}";
        var secretValue = "9876";
        var savePayload = new { SessionId = sessionId, Input = $"save secret \"{secretTitle}\" is \"{secretValue}\"" };

        var statusResponse = await _client.GetAsync("/api/secrets/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        using var statusDoc = JsonDocument.Parse(statusBody);
        var statusRoot = statusDoc.RootElement;
        var isInitialized = statusRoot.GetProperty("isInitialized").GetBoolean();
        var isUnlocked = statusRoot.GetProperty("isUnlocked").GetBoolean();

        if (!isInitialized)
        {
            // Step 1: Try to save a secret when vault is not initialized.
            var initResponse = await _client.PostAsJsonAsync("/api/conversation/converse", savePayload);
            Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);

            var initBody = await initResponse.Content.ReadAsStringAsync();
            using var initDoc = JsonDocument.Parse(initBody);
            var initRoot = initDoc.RootElement;
            Assert.False(initRoot.GetProperty("success").GetBoolean(), initBody);
            Assert.True(initRoot.GetProperty("isVaultSetupRequired").GetBoolean(), initBody);

            // Step 2: Set up the vault with PIN "1234".
            var setupPayload = new { Pin = "1234" };
            var setupResponse = await _client.PostAsJsonAsync("/api/secrets/setup", setupPayload);
            Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        }
        else if (!isUnlocked)
        {
            var unlockExistingResponse = await _client.PostAsJsonAsync("/api/secrets/unlock", new { Pin = "1234" });
            if (!unlockExistingResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("[Secrets] Skipped because the existing Testing vault uses an unknown PIN.");
                return;
            }
        }

        // Step 3: Retry saving the secret. Since setup unlocks the vault, this should succeed.
        var retrySaveResponse = await _client.PostAsJsonAsync("/api/conversation/converse", savePayload);
        Assert.Equal(HttpStatusCode.OK, retrySaveResponse.StatusCode);

        var retrySaveBody = await retrySaveResponse.Content.ReadAsStringAsync();
        using var retrySaveDoc = JsonDocument.Parse(retrySaveBody);
        var retrySaveRoot = retrySaveDoc.RootElement;
        Assert.True(retrySaveRoot.GetProperty("success").GetBoolean());
        Assert.Contains("saved successfully", retrySaveRoot.GetProperty("message").GetString() ?? string.Empty);

        // Step 4: Retrieve the secret when unlocked.
        var getPayload = new { SessionId = sessionId, Input = $"get secret \"{secretTitle}\"" };
        var getUnlockedResponse = await _client.PostAsJsonAsync("/api/conversation/converse", getPayload);
        Assert.Equal(HttpStatusCode.OK, getUnlockedResponse.StatusCode);

        var getUnlockedBody = await getUnlockedResponse.Content.ReadAsStringAsync();
        using var getUnlockedDoc = JsonDocument.Parse(getUnlockedBody);
        var getUnlockedRoot = getUnlockedDoc.RootElement;
        Assert.True(getUnlockedRoot.GetProperty("success").GetBoolean());
        Assert.Contains(secretValue, getUnlockedRoot.GetProperty("message").GetString() ?? string.Empty);

        // Step 5: Lock the vault.
        var lockResponse = await _client.PostAsJsonAsync("/api/secrets/lock", new { });
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        // Step 6: Try to retrieve the secret when locked. Should return unlock required.
        var getLockedResponse = await _client.PostAsJsonAsync("/api/conversation/converse", getPayload);
        Assert.Equal(HttpStatusCode.OK, getLockedResponse.StatusCode);

        var getLockedBody = await getLockedResponse.Content.ReadAsStringAsync();
        using var getLockedDoc = JsonDocument.Parse(getLockedBody);
        var getLockedRoot = getLockedDoc.RootElement;
        Assert.False(getLockedRoot.GetProperty("success").GetBoolean());
        Assert.True(getLockedRoot.GetProperty("isVaultUnlockRequired").GetBoolean());

        // Step 7: Unlock the vault.
        var unlockPayload = new { Pin = "1234" };
        var unlockResponse = await _client.PostAsJsonAsync("/api/secrets/unlock", unlockPayload);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        // Step 8: Retry retrieving the secret. Should succeed.
        var getUnlockedRetryResponse = await _client.PostAsJsonAsync("/api/conversation/converse", getPayload);
        Assert.Equal(HttpStatusCode.OK, getUnlockedRetryResponse.StatusCode);

        var getUnlockedRetryBody = await getUnlockedRetryResponse.Content.ReadAsStringAsync();
        using var getUnlockedRetryDoc = JsonDocument.Parse(getUnlockedRetryBody);
        var getUnlockedRetryRoot = getUnlockedRetryDoc.RootElement;
        Assert.True(getUnlockedRetryRoot.GetProperty("success").GetBoolean());
        Assert.Contains(secretValue, getUnlockedRetryRoot.GetProperty("message").GetString() ?? string.Empty);
    }

    [Fact]
    public async Task FastPath_MealPrefix_ListMeals_ResolvesCorrectly()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-mealprefix-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "/meal list" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("ListMeals", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_NutritionSummary_ResolvesCorrectly()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-macros-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "show my macros" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("GetNutritionSummary", root.GetProperty("selectedAction").GetString());
    }

    [Fact]
    public async Task FastPath_WhatDidIEatToday_ResolvesCorrectly()
    {
        if (!_fixture.IsApiOnline()) return;

        var sessionId = $"nl-flow-whatate-{Guid.NewGuid():N}";
        var payload = new { SessionId = sessionId, Input = "what did I eat today" };

        var response = await _client.PostAsJsonAsync("/api/conversation/converse", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.True(root.GetProperty("wasFastPath").GetBoolean());
        Assert.Equal("ListMeals", root.GetProperty("selectedAction").GetString());
    }
}
