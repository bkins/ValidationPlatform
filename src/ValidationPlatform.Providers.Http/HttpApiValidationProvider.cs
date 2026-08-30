using System.Net.Http.Json;
using ValidationPlatform.Core.Interfaces;

namespace ValidationPlatform.Providers.Http;

public class HttpApiValidationProvider : IApiValidationProvider
{
    private readonly HttpClient _client;

    public string Name => "HTTP API Validation Provider";

    public HttpApiValidationProvider(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request)
    {
        return await _client.SendAsync(request);
    }

    public async Task<T?> SendRequestAsync<T>(HttpRequestMessage request)
    {
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }
}
