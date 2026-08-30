using ValidationPlatform.Core.Interfaces;

namespace ValidationPlatform.Core.Models;

public abstract class PageObjectBase
{
    protected readonly IUiValidationProvider Ui;

    protected PageObjectBase(IUiValidationProvider ui)
    {
        Ui = ui;
    }

    protected Task ClickIdAsync(string automationId) => 
        Ui.ClickAsync(ElementQuery.ById(automationId));

    protected Task ClickNameAsync(string name) => 
        Ui.ClickAsync(ElementQuery.ByName(name));

    protected Task TypeIdAsync(string automationId, string text) => 
        Ui.TypeTextAsync(ElementQuery.ById(automationId), text);

    protected Task TypeNameAsync(string name, string text) => 
        Ui.TypeTextAsync(ElementQuery.ByName(name), text);

    protected Task<string> GetTextIdAsync(string automationId) => 
        Ui.GetTextAsync(ElementQuery.ById(automationId));

    protected Task<string> GetTextNameAsync(string name) => 
        Ui.GetTextAsync(ElementQuery.ByName(name));

    protected Task<bool> IsIdVisibleAsync(string automationId) => 
        Ui.IsElementVisibleAsync(ElementQuery.ById(automationId));

    protected Task<bool> IsNameVisibleAsync(string name) => 
        Ui.IsElementVisibleAsync(ElementQuery.ByName(name));

    protected Task<bool> IsIdEnabledAsync(string automationId) => 
        Ui.IsElementEnabledAsync(ElementQuery.ById(automationId));

    protected Task<bool> IsNameEnabledAsync(string name) => 
        Ui.IsElementEnabledAsync(ElementQuery.ByName(name));

    protected Task WaitForIdAsync(string automationId, TimeSpan? timeout = null) => 
        Ui.WaitForElementAsync(ElementQuery.ById(automationId), timeout);

    protected Task WaitForNameAsync(string name, TimeSpan? timeout = null) => 
        Ui.WaitForElementAsync(ElementQuery.ByName(name), timeout);

    protected Task ScrollIdIntoViewAsync(string automationId) => 
        Ui.ScrollIntoViewAsync(ElementQuery.ById(automationId));
}

