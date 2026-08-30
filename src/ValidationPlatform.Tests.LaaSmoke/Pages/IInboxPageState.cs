namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public interface IInboxPageState
{
    Task<bool> IsPageLoadedAsync();
    Task<bool> IsFilterVisibleAsync(string filterName);
}
