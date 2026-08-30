namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public interface IMemoryPageState
{
    Task<bool>   IsPageLoadedAsync();
    Task<string> GetPendingMemoriesCountTextAsync();
}
