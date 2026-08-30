namespace ValidationPlatform.Tests.LaaSmoke.Pages;

public interface IChatPageState
{
    Task<bool>   IsInputVisibleAsync();
    Task<bool>   IsInputEnabledAsync();
    Task<bool>   IsSendButtonEnabledAsync();
    Task<string> GetCurrentInputTextAsync();
    Task<string> GetLastAssistantMessageAsync();
    Task         TypeMessageAsync(string text);
    Task         SendMessageAsync();
    Task         ClearInputDirectAsync();
}
