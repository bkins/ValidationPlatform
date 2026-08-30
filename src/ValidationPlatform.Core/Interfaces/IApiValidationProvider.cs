namespace ValidationPlatform.Core.Interfaces;

public interface IApiValidationProvider : IValidationProvider
{
    Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request);
    Task<T?> SendRequestAsync<T>(HttpRequestMessage request);
}
