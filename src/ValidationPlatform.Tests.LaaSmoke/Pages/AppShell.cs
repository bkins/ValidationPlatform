using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public class AppShell : PageObjectBase
{
    public AppShell(IUiValidationProvider ui) : base(ui)
    {
    }

    public async Task NavigateToTabAsync(string tabName)
    {
        var autoIdQuery = ElementQuery.ById($"{tabName}TabItem", TimeSpan.FromSeconds(3));
        var nameQuery   = ElementQuery.ByName(tabName, TimeSpan.FromSeconds(3));

        if (await Ui.IsElementVisibleAsync(autoIdQuery))
        {
            await Ui.ClickAsync(autoIdQuery);
            return;
        }

        if (await Ui.IsElementVisibleAsync(nameQuery))
        {
            await Ui.ClickAsync(nameQuery);
            return;
        }

        var moreQuery = ElementQuery.ByName("More", TimeSpan.FromSeconds(2));
        if (await Ui.IsElementVisibleAsync(moreQuery))
        {
            await Ui.ClickAsync(moreQuery);
            await Task.Delay(500);

            if (await Ui.IsElementVisibleAsync(autoIdQuery))
            {
                await Ui.ClickAsync(autoIdQuery);
                return;
            }

            if (await Ui.IsElementVisibleAsync(nameQuery))
            {
                await Ui.ClickAsync(nameQuery);
                return;
            }
        }

        try
        {
            await Ui.ClickAsync(autoIdQuery);
        }
        catch
        {
            await Ui.ClickAsync(nameQuery);
        }
    }
}
