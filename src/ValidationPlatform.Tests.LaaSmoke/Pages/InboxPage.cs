using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public class InboxPage : PageObjectBase, IInboxPageState
{
    public InboxPage(IUiValidationProvider ui) : base(ui)
    {
    }

    public Task<bool> IsPageLoadedAsync() => IsNameVisibleAsync("Inbox");

    public Task<bool> IsFilterVisibleAsync(string filterName) => IsNameVisibleAsync(filterName);
}
