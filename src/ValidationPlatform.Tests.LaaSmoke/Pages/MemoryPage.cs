using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public class MemoryPage : PageObjectBase, IMemoryPageState
{
    public MemoryPage(IUiValidationProvider ui) : base(ui)
    {
    }

    public Task<bool> IsPageLoadedAsync() => IsNameVisibleAsync("Memory");

    public Task<string> GetPendingMemoriesCountTextAsync() => GetTextIdAsync("PendingMemoriesBadge");
}
