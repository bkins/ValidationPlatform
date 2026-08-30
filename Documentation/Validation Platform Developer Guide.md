# Validation Platform — Developer Guide

This developer guide describes how to author new tests and create new validation test projects using the extensible **Validation Platform** framework.

---

# Architecture Overview

The framework is decoupled into three main layers:
1. **Core Abstractions (`ValidationPlatform.Core`)**: Defines contracts and base models (interfaces for drivers, providers, and shared query structures).
2. **Providers (`ValidationPlatform.Providers.*`)**: Implements platform-specific validation methods (e.g., Windows UI Automation UIA3, HTTP client API validation).
3. **Validation Suites (`ValidationPlatform.Tests.*`)**: Contains declarative, semantic tests representing target application specifications.

```
       [Validation Tests (xUnit)]
                   │
                   ▼ (Interacts with semantic Pages)
       [Page Objects (PageObjectBase)]
                   │
                   ▼ (Translates to high-level actions)
     [Abstractions (IUiValidationProvider)]
                   │
                   ▼ (Binds to OS-level mechanics)
    [Windows UIA Provider (WindowsTestBase)]
                   │
                   ▼
         [Target Desktop App]
```

---

# Writing Page Objects

Page Objects encapsulate application-specific control mappings and expose clean, semantic operations to the validation tests. By inheriting from `PageObjectBase`, you gain shorthand access to standard interactions, removing manual `ElementQuery` instantiation.

### Example: Chat Page Object & Semantic State Interface
Create your semantic state interface and page object in the `Pages` directory of your test project:

```csharp
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
    public Task TypeMessageAsync(string text) => TypeIdAsync("ChatEditor", text);
    public Task SendMessageAsync() => ClickIdAsync("SendButton");
    public Task ClearInputDirectAsync() => TypeIdAsync("ChatEditor", "");
}
```

---

# Declarative Test Data Fixtures

Use `TestDataProviders` for centralized test fixtures instead of hardcoding strings inline:

```csharp
var testUser = TestDataProviders.Users.Administrator;
var greeting = TestDataProviders.Prompts.Greeting;
var defaultTask = TestDataProviders.Tasks.DefaultTask;
```


---

# Authoring UI Tests

UI tests should inherit from `WindowsTestBase`. This base class automatically handles process launching, window synchronization, and process cleanup on test completion or failure.

### Example: Counter Tests
Create a test class specifying the search paths to the target application's executable:

```csharp
using ValidationPlatform.Core.Models;
using ValidationPlatform.Providers.Windows;
using Xunit;

namespace ValidationPlatform.Tests.InsAndOuts;

public class CounterTests : WindowsTestBase
{
    // Define relative or absolute search paths for the executable
    private static readonly string[] ExeCandidates = new[]
    {
        @"..\InsAndOutsAndOohs2\bin\Debug\net9.0-windows10.0.19041.0\win10-x64\InsAndOutsAndOohs2.exe"
    };

    // Constructor automatically launches the app and awaits main window initialization
    public CounterTests() : base(ExeCandidates)
    {
    }

    [Fact]
    public async Task CounterButton_Increments_OnClicks()
    {
        try
        {
            // Get initial button text using a Name query
            var buttonQuery = ElementQuery.ByName("Click me");
            var initialText = await UiProvider.GetTextAsync(buttonQuery);
            Assert.Equal("Click me", initialText);

            // Perform click action
            await UiProvider.ClickAsync(buttonQuery);
            await Task.Delay(500);

            // Re-query using updated text name
            buttonQuery = ElementQuery.ByName("Clicked 1 time");
            var updatedText = await UiProvider.GetTextAsync(buttonQuery);
            Assert.Equal("Clicked 1 time", updatedText);
        }
        catch (Exception ex)
        {
            // Dump the structural accessibility tree on failure for diagnostic analysis
            var tree = await UiProvider.GetAccessibilityTreeAsync();
            throw new Exception($"Test failed: {ex.Message}. Accessibility Tree:\n{tree}");
        }
    }
}
```

---

# Adding a New Test Project

Follow these steps to create and configure a brand-new test project in the solution:

### Step 1: Create the Project File
Create a new directory inside `src` (e.g., `src/ValidationPlatform.Tests.NewApp`) and write `ValidationPlatform.Tests.NewApp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ValidationPlatform.Core\ValidationPlatform.Core.csproj" />
    <ProjectReference Include="..\ValidationPlatform.Providers.Windows\ValidationPlatform.Providers.Windows.csproj" />
  </ItemGroup>

</Project>
```

### Step 2: Add the Project to the Solution
Navigate to the root directory `ValidationPlatform` and register the project:
```bash
dotnet sln add src\ValidationPlatform.Tests.NewApp\ValidationPlatform.Tests.NewApp.csproj
```

### Step 3: Run the Tests
Execute the tests directly or build the solution:
```bash
dotnet test
```
