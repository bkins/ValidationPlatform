namespace ValidationPlatform.Core.Environment;

public interface IEnvironmentController
{
    Task ResetStateAsync();
    Task ClearLocalStorageAsync();
    Task SeedTestDataAsync<T>(string key, T data);
}
