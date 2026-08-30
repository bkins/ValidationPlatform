using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public class ChatPage : PageObjectBase, IChatPageState
{
    public ChatPage(IUiValidationProvider ui) : base(ui)
    {
    }

    public Task<bool> IsInputVisibleAsync() => IsIdVisibleAsync("ChatEditor");

    public Task<bool> IsInputEnabledAsync() => IsIdEnabledAsync("ChatEditor");

    public Task<bool> IsSendButtonEnabledAsync() => IsIdEnabledAsync("SendButton");

    public Task<string> GetCurrentInputTextAsync() => GetTextIdAsync("ChatEditor");

    public Task<string> GetLastAssistantMessageAsync() => GetTextNameAsync("AssistantMessage");

    public async Task TypeMessageAsync(string text)
    {
        for (int i = 0; i < 20; i++)
        {
            if (await IsInputEnabledAsync()) break;
            await Task.Delay(250);
        }
        await TypeIdAsync("ChatEditor", text);
    }

    public async Task SendMessageAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            if (await IsSendButtonEnabledAsync()) break;
            await Task.Delay(250);
        }
        await ClickIdAsync("SendButton");
    }

    public async Task ClearInputAsync()
    {
        await ClickIdAsync("ClearButton");
        await Task.Delay(600);
        await ClickNameAsync("Clear");
        await Task.Delay(600);
    }

    public Task ClearInputDirectAsync() => TypeIdAsync("ChatEditor", "");

    public async Task<bool> IsConfirmationModalVisibleAsync(string title)
    {
        for (int i = 0; i < 15; i++)
        {
            if (await Ui.IsElementVisibleAsync(ElementQuery.ByName(title, TimeSpan.FromSeconds(1)))) return true;
            if (await Ui.IsElementVisibleAsync(ElementQuery.ByName("Yes", TimeSpan.FromSeconds(1)))) return true;
            if (await Ui.IsElementVisibleAsync(ElementQuery.ByName("No", TimeSpan.FromSeconds(1)))) return true;
            await Task.Delay(500);
        }
        return false;
    }

    public Task DismissConfirmationModalAsync(string modalTitle, bool confirm)
    {
        var buttonName = confirm ? "Yes" : "No";
        return ClickNameAsync(buttonName);
    }
}
