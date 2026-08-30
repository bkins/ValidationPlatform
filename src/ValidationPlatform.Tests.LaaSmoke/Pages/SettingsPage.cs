using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public class SettingsPage : PageObjectBase, ISettingsPageState
{
    public SettingsPage(IUiValidationProvider ui) : base(ui)
    {
    }

    public Task<bool> IsPageLoadedAsync() => IsNameVisibleAsync("Settings");

    public Task<string> GetSelectedModelAsync() => GetTextIdAsync("SelectedModelPicker");

    public Task<string> GetApiBaseUrlAsync() => GetTextIdAsync("ApiBaseUrlEntry");

    public Task ScrollSettingsAsync() => ScrollIdIntoViewAsync("SaveSettingsButton");
}
