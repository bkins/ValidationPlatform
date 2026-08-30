using ValidationPlatform.Core.Models;
using ValidationPlatform.Providers.Windows;
using Xunit;

namespace ValidationPlatform.Tests.InsAndOuts;

[Trait("Category", "WindowsUi")]
public class CounterTests : WindowsTestBase

{
    private static readonly string[] ExeCandidates = new[]
    {
        @"C:\Users\benho\source\repos\InsAndOutsAndOohs2\InsAndOutsAndOohs2\bin\Debug\net9.0-windows10.0.19041.0\win10-x64\InsAndOutsAndOohs2.exe"
    };

    public CounterTests() : base(ExeCandidates)
    {
    }

    [Fact]
    public async Task CounterButton_Increments_OnClicks()
    {
        try
        {
            await EnsureAppLaunchedAsync();

            // 2. Assert Initial Text using Name query
            var buttonQuery = ElementQuery.ByName("Click me");
            var initialText = await UiProvider.GetTextAsync(buttonQuery);
            Assert.Equal("Click me", initialText);

            // 3. First Click
            await UiProvider.ClickAsync(buttonQuery);
            await Task.Delay(500);

            // Button name changes to "Clicked 1 time", so we query by that new name
            buttonQuery = ElementQuery.ByName("Clicked 1 time");
            var firstClickText = await UiProvider.GetTextAsync(buttonQuery);
            Assert.Equal("Clicked 1 time", firstClickText);

            // 4. Second Click
            await UiProvider.ClickAsync(buttonQuery);
            await Task.Delay(500);

            // Button name changes to "Clicked 2 times", so we query by that new name
            buttonQuery = ElementQuery.ByName("Clicked 2 times");
            var secondClickText = await UiProvider.GetTextAsync(buttonQuery);
            Assert.Equal("Clicked 2 times", secondClickText);
        }
        catch (Exception ex)
        {
            var tree = await UiProvider.GetAccessibilityTreeAsync();
            throw new Exception($"Test failed. ex: {ex.Message}. UIA Tree:\n{tree}");
        }
    }
}
