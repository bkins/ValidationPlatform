using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Core.Interfaces;

public interface IUiValidationProvider : IValidationProvider
{
    Task ClickAsync(ElementQuery query);
    Task TypeTextAsync(ElementQuery query, string text);
    Task<string> GetTextAsync(ElementQuery query);
    Task<bool> IsElementVisibleAsync(ElementQuery query);
    Task<bool> IsElementEnabledAsync(ElementQuery query);
    Task WaitForElementAsync(ElementQuery query, TimeSpan? timeout = null);
    Task ScrollIntoViewAsync(ElementQuery query);
    Task<byte[]> CaptureScreenshotAsync();
    Task<string> GetAccessibilityTreeAsync();
}

