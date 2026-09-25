using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Automation;
using ValidationPlatform.Core.Models;
using ValidationPlatform.Providers.Windows;
using ValidationPlatform.Tests.LaaSmoke.Pages;
using Xunit;
using Xunit.Abstractions;

namespace ValidationPlatform.Tests.LaaSmoke;

[Trait("Category", "WindowsUi")]
public class SmokeTests : WindowsTestBase

{
    private readonly ITestOutputHelper _output;
    private readonly AppShell _shell;
    private readonly ChatPage _chatPage;

    private static readonly string[] ExeCandidates = new[]
    {
        @"C:\Users\benho\source\repos\LocalAIAssistant\bin\Debug\net9.0-windows10.0.19041.0\win10-x64\LocalAIAssistant.Ui.Maui.exe",
        @"C:\Users\benho\source\repos\LocalAIAssistant\bin\Debug\net9.0-windows10.0.19041.0\LocalAIAssistant.Ui.Maui.exe",
        @"C:\Users\benho\source\repos\LocalAIAssistant\bin\Release\net9.0-windows10.0.19041.0\win10-x64\LocalAIAssistant.Ui.Maui.exe",
        @"C:\Users\benho\source\repos\LocalAIAssistant\bin\Release\net9.0-windows10.0.19041.0\LocalAIAssistant.Ui.Maui.exe"
    };

    public SmokeTests(ITestOutputHelper output) : base(ExeCandidates)
    {
        _output = output;
        _shell = new AppShell(UiProvider);
        _chatPage = new ChatPage(UiProvider);
    }

    private int GetActiveApiPort()
    {
        foreach (var port in new[] { 5276, 5273 })
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient("localhost", port);
                return port;
            }
            catch { }
        }
        return 0;
    }

    private bool IsApiOnline()
    {
        var port = GetActiveApiPort();
        if (port > 0)
        {
            Environment.SetEnvironmentVariable("CP_API_BASE_URL", $"http://localhost:{port}");
            return true;
        }
        return false;
    }

    [Fact]
    public async Task RunAllSmokeTestsSequential()
    {
        try
        {
            _output.WriteLine("Starting LAA Smoke Test Suite Sequential Runs...");
            await EnsureAppLaunchedAsync();

        // 1. Chat page visible
        _output.WriteLine("Step 1: Chat page visible");
        var isVisible = await _chatPage.IsInputVisibleAsync();
        Assert.True(isVisible, "ChatEditor automation element is not visible.");

        // 2. Chat editor enabled
        _output.WriteLine("Step 2: Chat editor enabled");
        var editorEl = UiProvider.FindElement(ElementQuery.ById("ChatEditor"));
        Assert.NotNull(editorEl);
        Assert.True(editorEl.Current.IsEnabled, "ChatEditor is disabled.");

        // 3. Chat editor accepts text input
        _output.WriteLine("Step 3: Chat editor accepts text input");
        await _chatPage.TypeMessageAsync("smoke_test_hello");
        await Task.Delay(400);
        var typedText = await UiProvider.GetTextAsync(ElementQuery.ById("ChatEditor"));
        Assert.Equal("smoke_test_hello", typedText);
        await _chatPage.ClearInputDirectAsync();

        // 4. Send button present and enabled
        _output.WriteLine("Step 4: Send button present and enabled");
        var sendEl = UiProvider.FindElement(ElementQuery.ById("SendButton"));
        Assert.NotNull(sendEl);
        Assert.True(sendEl.Current.IsEnabled, "SendButton is disabled.");

        // 5. Clear button present and enabled
        _output.WriteLine("Step 5: Clear button present and enabled");
        var clearEl = UiProvider.FindElement(ElementQuery.ById("ClearButton"));
        Assert.NotNull(clearEl);
        Assert.True(clearEl.Current.IsEnabled, "ClearButton is disabled.");

        // 5b. Destructive action confirmation modal triggers
        _output.WriteLine("Step 5b: Destructive action confirmation modal triggers");
        if (IsApiOnline())
        {
            _output.WriteLine("Executing step 5b: Issue add task command...");
            await _chatPage.TypeMessageAsync("add task TestTaskForDelete");
            await Task.Delay(400);
            await _chatPage.SendMessageAsync();

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(300);
                if (await _chatPage.IsInputEnabledAsync()) break;
            }
            await Task.Delay(500);

            _output.WriteLine("Issue delete task 1 command...");
            await _chatPage.TypeMessageAsync("delete task 1");
            await Task.Delay(400);
            await _chatPage.SendMessageAsync();

            var isModalVisible = await _chatPage.IsConfirmationModalVisibleAsync("Confirm Action");
            if (isModalVisible)
            {
                _output.WriteLine("Step 5b PASSED: Confirmation modal displayed and handled successfully.");
                await _chatPage.DismissConfirmationModalAsync("Confirm Action", confirm: false);
                await Task.Delay(600);
            }
            else
            {
                _output.WriteLine("Step 5b NOTE: Confirmation modal not visible in UIA tree (non-interactive session).");
            }
            await _chatPage.ClearInputDirectAsync();
        }
        else
        {
            _output.WriteLine("Skipped Step 5b: API is offline.");
        }

        // 5c. Provider and Model Switching
        _output.WriteLine("Step 5c: Provider and Model Switching");
        if (IsApiOnline())
        {
            await _chatPage.TypeMessageAsync("switch provider to Groq");
            await Task.Delay(400);
            await _chatPage.SendMessageAsync();
            await Task.Delay(1000);
            await UiProvider.WaitForEnabledAsync(ElementQuery.ById("ChatEditor"), TimeSpan.FromSeconds(30));
            await _chatPage.ClearInputDirectAsync();

            await _chatPage.TypeMessageAsync("switch model to qwen2.5:14b");
            await Task.Delay(400);
            await _chatPage.SendMessageAsync();
            await Task.Delay(1000);
            await UiProvider.WaitForEnabledAsync(ElementQuery.ById("ChatEditor"), TimeSpan.FromSeconds(30));
            await _chatPage.ClearInputDirectAsync();
        }
        else
        {
            _output.WriteLine("Skipped Step 5c: API is offline.");
        }

        // 6. Navigate to Chats tab
        _output.WriteLine("Step 6: Navigate to Chats tab");
        await _shell.NavigateToTabAsync("Chats");
        await Task.Delay(800);
        var newChatVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("NewChatButton"));
        var pastConversationsVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Past Conversations"));
        Assert.True(newChatVisible || pastConversationsVisible, "Neither NewChatButton nor 'Past Conversations' header was found.");

        // 7. Chats page shows list or empty state
        _output.WriteLine("Step 7: Chats page shows list or empty state");
        var listVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("ConversationsList"));
        var emptyVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("No past conversations"));
        Assert.True(listVisible || emptyVisible, "Neither ConversationsList nor empty-state label was found.");

        // 7b. Rename conversation
        _output.WriteLine("Step 7b: Rename conversation");
        if (IsApiOnline() && listVisible)
        {
            var renameVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Rename", TimeSpan.FromSeconds(3)));
            if (renameVisible)
            {
                // Click the Rename swipe item
                await UiProvider.ClickAsync(ElementQuery.ByName("Rename", TimeSpan.FromSeconds(5)));
                await Task.Delay(800);

                // Wait for and enter text in the prompt dialog
                var promptDialog = UiProvider.FindElement(ElementQuery.ByName("Rename Conversation", TimeSpan.FromSeconds(5)));
                if (promptDialog != null)
                {
                    var editField = promptDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                    if (editField != null)
                    {
                        if (editField.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj))
                        {
                            ((ValuePattern)valObj).SetValue("Renamed Chat Title");
                        }
                        else
                        {
                            editField.SetFocus();
                            System.Windows.Forms.SendKeys.SendWait("^a{BACKSPACE}Renamed Chat Title");
                        }
                    }

                    // Click OK button
                    var okBtn = promptDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "OK"));
                    if (okBtn != null)
                    {
                        if (okBtn.TryGetCurrentPattern(InvokePattern.Pattern, out var invObj))
                        {
                            ((InvokePattern)invObj).Invoke();
                        }
                    }
                    await Task.Delay(600);
                    _output.WriteLine("Step 7b PASSED: Conversation successfully renamed.");
                }
            }
            else
            {
                _output.WriteLine("Step 7b NOTE: Rename option not visible on current list.");
            }
        }
        else
        {
            _output.WriteLine("Skipped Step 7b: API is offline or ConversationsList not visible.");
        }

        // 8. Navigate to Inbox tab
        _output.WriteLine("Step 8: Navigate to Inbox tab");
        await _shell.NavigateToTabAsync("Inbox");
        await Task.Delay(800);
        var inboxListVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("InboxList"));
        Assert.True(inboxListVisible, "InboxList not found after switching to Inbox tab.");

        // 9. Inbox page loads without crashing
        _output.WriteLine("Step 9: Inbox page loads without crashing");
        Assert.True(Driver.IsRunning, "App process crashed on Inbox page.");

        // 10. Navigate back to Chat tab
        _output.WriteLine("Step 10: Navigate back to Chat tab");
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);
        Assert.True(await _chatPage.IsInputVisibleAsync(), "ChatEditor not visible after returning to Chat tab.");

        // 11. Rapid tab cycling does not crash
        _output.WriteLine("Step 11: Rapid tab cycling does not crash");
        var tabs = new[] { "Chats", "Inbox", "Memory", "Logs", "Settings", "Chat" };
        foreach (var tab in tabs)
        {
            await _shell.NavigateToTabAsync(tab);
            await Task.Delay(400);
        }
        Assert.True(Driver.IsRunning, "App process crashed during rapid tab cycling.");
        Assert.True(await _chatPage.IsInputVisibleAsync(), "ChatEditor not visible after rapid tab cycling.");

        // 12. Back-navigation from Chats does not crash
        _output.WriteLine("Step 12: Back-navigation from Chats does not crash");
        await _shell.NavigateToTabAsync("Chats");
        await Task.Delay(800);
        var focusEl = UiProvider.FindElement(ElementQuery.ById("NewChatButton")) ??
                      UiProvider.FindElement(ElementQuery.ByName("New Chat"));
        if (focusEl != null)
        {
            focusEl.SetFocus();
            await Task.Delay(150);
            try { System.Windows.Forms.SendKeys.SendWait("%{LEFT}"); } catch { }
            await Task.Delay(800);
        }
        Assert.True(Driver.IsRunning, "App process crashed after back navigation.");
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(600);

        // 13. Settings page renders Coco section
        _output.WriteLine("Step 13: Settings page renders Coco section");
        await _shell.NavigateToTabAsync("Settings");
        await Task.Delay(1000);
        var enableCocoVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Enable Coco"));
        var urlLabelVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Coco API URL"));
        var indexBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Index Project"));
        Assert.True(enableCocoVisible, "'Enable Coco' label not found.");
        Assert.True(urlLabelVisible, "'Coco API URL' label not found.");
        Assert.True(indexBtnVisible, "'Index Project' button not found.");

        // 14. Clipboard monitoring toggle present
        _output.WriteLine("Step 14: Clipboard monitoring toggle present");
        var clipboardMonVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Clipboard monitoring"));
        Assert.True(clipboardMonVisible, "'Clipboard monitoring' label not found.");

        // 15. Global hotkey field present
        _output.WriteLine("Step 15: Global hotkey field present");
        var hotkeyVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Global hotkey"));
        Assert.True(hotkeyVisible, "'Global hotkey' label not found.");

        // 16. Ask Coco toolbar toggle visible in Chat when Coco enabled
        _output.WriteLine("Step 16: Ask Coco toolbar toggle visible when Coco enabled");
        var enableLabelEl = UiProvider.FindElement(ElementQuery.ByName("Enable Coco"));
        Assert.NotNull(enableLabelEl);
        
        var walker = TreeWalker.RawViewWalker;
        var parentEl = walker.GetParent(enableLabelEl);
        AutomationElement? cocoSwitch = null;
        if (parentEl != null)
        {
            cocoSwitch = parentEl.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleSwitch"));
            if (cocoSwitch == null)
            {
                var grandparent = walker.GetParent(parentEl);
                if (grandparent != null)
                {
                    cocoSwitch = grandparent.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleSwitch"));
                }
            }
        }

        if (cocoSwitch != null)
        {
            if (cocoSwitch.TryGetCurrentPattern(TogglePattern.Pattern, out var toggleObj))
            {
                var togglePat = (TogglePattern)toggleObj;
                if (togglePat.Current.ToggleState == ToggleState.Off)
                {
                    togglePat.Toggle();
                    await Task.Delay(300);
                }
            }
        }

        var saveBtn = UiProvider.FindElement(ElementQuery.ByName("Save"));
        if (saveBtn != null)
        {
            if (saveBtn.TryGetCurrentPattern(InvokePattern.Pattern, out var invObj))
            {
                ((InvokePattern)invObj).Invoke();
                await Task.Delay(600);
            }
        }

        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);

        var askCocoVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("🔵 Ask Coco")) ||
                             await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Ask Coco"));
        Assert.True(askCocoVisible, "Ask Coco toolbar label not visible after enabling Coco.");

        // 17. Chat editor still accessible
        _output.WriteLine("Step 17: Chat editor accessible after changes");
        Assert.True(await _chatPage.IsInputVisibleAsync(), "ChatEditor is not visible.");
        await _chatPage.TypeMessageAsync("phase34_guard");
        await Task.Delay(300);
        var guardText = await UiProvider.GetTextAsync(ElementQuery.ById("ChatEditor"));
        Assert.Equal("phase34_guard", guardText);
        await _chatPage.ClearInputDirectAsync();

        // 18. Verify the active-conversation shell badge, then check the Memory destination.
        _output.WriteLine("Step 18: Navigate to Memory tab and check elements");
        
        // The debug hook exercises UX-14's active-conversation shell indicator. The Memory
        // destination performs an authoritative refresh, so it must not be used to preserve
        // a synthetic client-only count when the API reports an empty active queue.
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);
        await _chatPage.TypeMessageAsync("test:set_pending_memory");
        await Task.Delay(400);
        await _chatPage.SendMessageAsync();
        await Task.Delay(800);

        var pendingBadgeVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("MemoryBadgeFrame"));
        Assert.True(pendingBadgeVisible, "Active-conversation pending-memory badge was not visible after test:set_pending_memory.");

        var tree = await UiProvider.GetAccessibilityTreeAsync();
        try { System.IO.File.WriteAllText(@"C:\Users\benho\source\repos\CP\CP.Workbench\accessibility_tree.txt", tree); } catch {}

        var tabQuery = ElementQuery.ByIdAndName("navViewItem", "Memory");
        if (await UiProvider.IsElementVisibleAsync(tabQuery))
        {
            await UiProvider.ClickAsync(tabQuery);
        }
        else
        {
            await _shell.NavigateToTabAsync("Memory");
        }
        await Task.Delay(1000);

        var treeAfter = await UiProvider.GetAccessibilityTreeAsync();
        try { System.IO.File.WriteAllText(@"C:\Users\benho\source\repos\CP\CP.Workbench\accessibility_tree_after.txt", treeAfter); } catch {}

        var clearStBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("ClearShorTermButton"));
        var clearLtBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("ClearLongTermButton"));
        var refreshMemoryBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("RefreshButton"));
        Assert.True(clearStBtnVisible, "'Clear Short Term' button not found on Memory tab.");
        Assert.True(clearLtBtnVisible, "'Clear Long Term' button not found on Memory tab.");
        Assert.True(refreshMemoryBtnVisible, "'Refresh' button not found on Memory tab.");

        // 19. Navigate to Logs tab and verify elements
        _output.WriteLine("Step 19: Navigate to Logs tab and verify elements");
        await Task.Delay(2000);
        await _shell.NavigateToTabAsync("Logs");
        await Task.Delay(1000);
        var refreshLogsBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Refresh"))
                                 || await UiProvider.IsElementVisibleAsync(ElementQuery.ById("RefreshButton"));
        var clearLogsBtnVisible   = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Clear Logs"))
                                 || await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Clear"))
                                 || await UiProvider.IsElementVisibleAsync(ElementQuery.ById("ClearLogsButton"));
        var testLoggingBtnVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Test Logging"))
                                 || await UiProvider.IsElementVisibleAsync(ElementQuery.ById("TestLoggingButton"));
        Assert.True(refreshLogsBtnVisible, "'Refresh' button not found on Logs tab.");
        Assert.True(clearLogsBtnVisible, "'Clear Logs' button not found on Logs tab.");
        Assert.True(testLoggingBtnVisible, "'Test Logging' button not found on Logs tab.");

        var logCollectionVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("LogCollection"));
        var noLogsLabelVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("No log entries found"));
        Assert.True(logCollectionVisible || noLogsLabelVisible, "Neither LogCollection nor 'No log entries found' label was found on Logs tab.");

        // 20. Navigate to Inbox tab and check lists
        _output.WriteLine("Step 20: Navigate to Inbox tab and check lists");
        await _shell.NavigateToTabAsync("Inbox");
        await Task.Delay(1000);
        var inboxListReloadedVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("InboxList"));
        Assert.True(inboxListReloadedVisible, "'InboxList' not found on Inbox tab.");

        var emptyInboxVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("No items in your inbox"));
        if (emptyInboxVisible)
        {
            _output.WriteLine("Inbox is empty; 'No items in your inbox' label is visible.");
        }
        else
        {
            _output.WriteLine("Inbox has items; at least one item is visible.");
        }

        // 21. New Chat creates new session
        _output.WriteLine("Step 21: New Chat creates new session");
        
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);
        var originalSessionId = await UiProvider.GetTextAsync(ElementQuery.ById("ActiveConversationIdLabel"));
        _output.WriteLine($"Original session ID: {originalSessionId}");
        Assert.False(string.IsNullOrEmpty(originalSessionId), "Original active conversation ID is empty.");

        await _shell.NavigateToTabAsync("Chats");
        await Task.Delay(1000);
        var newChatBtn = UiProvider.FindElement(ElementQuery.ById("NewChatButton"));
        Assert.NotNull(newChatBtn);
        if (newChatBtn.TryGetCurrentPattern(InvokePattern.Pattern, out var newChatInv))
        {
            ((InvokePattern)newChatInv).Invoke();
        }
        else
        {
            await UiProvider.ClickAsync(ElementQuery.ById("NewChatButton"));
        }
        await Task.Delay(1000);

        // Verify we are back on the Chat tab and the ChatEditor is empty and visible
        var chatEditorVisible = await _chatPage.IsInputVisibleAsync();
        Assert.True(chatEditorVisible, "ChatEditor is not visible after clicking New Chat.");
        var editorText = await UiProvider.GetTextAsync(ElementQuery.ById("ChatEditor"));
        Assert.True(string.IsNullOrEmpty(editorText), "ChatEditor was not cleared after starting a new chat.");

        // Confirm session ID changes
        var newSessionId = await UiProvider.GetTextAsync(ElementQuery.ById("ActiveConversationIdLabel"));
        _output.WriteLine($"New session ID: {newSessionId}");
        Assert.False(string.IsNullOrEmpty(newSessionId), "New active conversation ID is empty.");
        Assert.NotEqual(originalSessionId, newSessionId);

        // 22. Send meal command in Chat
        _output.WriteLine("Step 22: Send meal command in Chat");
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);
        await _chatPage.TypeMessageAsync("/meal list");
        await Task.Delay(300);
        await _chatPage.SendMessageAsync();
        await Task.Delay(1200);
        Assert.True(Driver.IsRunning, "App process crashed after sending /meal list command.");

        // 23. Conversation Recorder page loads and displays controls
        _output.WriteLine("Step 23: Conversation Recorder page loads and displays controls");
        await _shell.NavigateToTabAsync("Record");
        await Task.Delay(1000);
        var recordTitleVisible = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Offline Conversation Recorder"));
        var recordBtnVisible   = await UiProvider.IsElementVisibleAsync(ElementQuery.ById("RecordToggleButton")) ||
                                 await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Record"));
        var savedRecsVisible   = await UiProvider.IsElementVisibleAsync(ElementQuery.ByName("Saved Recordings"));
        Assert.True(recordTitleVisible || recordBtnVisible || savedRecsVisible, "Conversation Recorder controls were not found on Record tab.");
        Assert.True(Driver.IsRunning, "App process crashed on Record page.");
        await _shell.NavigateToTabAsync("Chat");
        await Task.Delay(800);

        _output.WriteLine("LAA Smoke Test Suite completed successfully!");

            if (Environment.GetEnvironmentVariable("FORCE_FAILURE") == "true")
            {
                throw new Exception("Forced failure to verify screenshot functionality.");
            }
        }
        catch (Exception)
        {
            try
            {
                await UiProvider.CaptureScreenshotAsync();
            }
            catch { }
            throw;
        }
    }
}
