namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public interface ISettingsPageState
{
    Task<bool>   IsPageLoadedAsync();
    Task<string> GetSelectedModelAsync();
    Task<string> GetApiBaseUrlAsync();
    Task         ScrollSettingsAsync();
}
