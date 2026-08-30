using System.Diagnostics;
using System.Windows.Automation;
using ValidationPlatform.Core.Interfaces;
using ValidationPlatform.Core.Models;

namespace ValidationPlatform.Providers.Windows;

public class WindowsUiValidationProvider : IUiValidationProvider
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    private const int MOUSEEVENTF_LEFTDOWN = 0x02;
    private const int MOUSEEVENTF_LEFTUP = 0x04;
    private static readonly string ClickLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ValidationPlatform-clicks.log");

    public string Name => "Windows UI Automation (UIA3) Provider";

    public AutomationElement? RootElement { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;

    public Task ClickAsync(ElementQuery query)
    {
        var element = FindEnabledElement(query);
        if (element == null)
        {
            throw new Exception($"UI Element not found for query: {query}");
        }

        WriteClickLog($"ClickAsync: Found element ID={element.Current.AutomationId} Name=\"{element.Current.Name}\" Type={element.Current.ControlType.ProgrammaticName} Enabled={element.Current.IsEnabled}");

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invPattern))
        {
            WriteClickLog("ClickAsync: Calling InvokePattern.Invoke()");
            try
            {
                ((InvokePattern)invPattern).Invoke();
                return Task.CompletedTask;
            }
            catch (ElementNotEnabledException ex)
            {
                WriteClickLog($"InvokePattern.Invoke failed because the element was not enabled: {ex.Message}. Attempting simulated physical click...");
                if (TryPhysicalClick(element, out var clickFailure))
                {
                    return Task.CompletedTask;
                }

                throw new InvalidOperationException($"UI element was visible but not enabled for query: {query}. Physical click fallback also failed: {clickFailure}", ex);
            }
        }

        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selPattern))
        {
            WriteClickLog("ClickAsync: Calling SelectionItemPattern.Select()");
            try
            {
                ((SelectionItemPattern)selPattern).Select();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                WriteClickLog($"SelectionItemPattern.Select failed: {ex.Message}. Attempting simulated physical click...");
                if (TryPhysicalClick(element, out _))
                {
                    return Task.CompletedTask;
                }

                throw;
            }
        }

        WriteClickLog("ClickAsync: No Invoke or Selection pattern found. Attempting simulated physical click...");
        if (TryPhysicalClick(element, out var failureMessage))
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Element does not support InvokePattern or SelectionItemPattern, and physical click fallback failed: {query}. {failureMessage}");
    }

    private static void WriteClickLog(string message)
    {
        try
        {
            System.IO.File.AppendAllText(ClickLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never fail a smoke test.
        }
    }

    private static bool TryPhysicalClick(AutomationElement element, out string failureMessage)
    {
        failureMessage = string.Empty;

        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect == System.Windows.Rect.Empty)
            {
                failureMessage = "Element bounding rectangle was empty.";
                WriteClickLog(failureMessage);
                return false;
            }

            var x = (int)(rect.Left + rect.Width / 2);
            var y = (int)(rect.Top + rect.Height / 2);
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
            Thread.Sleep(200);
            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, x, y, 0, 0);
            WriteClickLog($"Simulated click succeeded at ({x}, {y})");
            return true;
        }
        catch (Exception ex)
        {
            failureMessage = ex.Message;
            WriteClickLog($"Simulated click failed: {ex.Message}");
            return false;
        }
    }

    private AutomationElement? FindEnabledElement(ElementQuery query)
    {
        var timeout = query.Timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();
        AutomationElement? lastElement = null;

        while (stopwatch.Elapsed < timeout)
        {
            lastElement = FindElement(query with { Timeout = TimeSpan.FromMilliseconds(250) });
            if (lastElement == null)
            {
                continue;
            }

            try
            {
                if (lastElement.Current.IsEnabled)
                {
                    return lastElement;
                }
            }
            catch (ElementNotAvailableException)
            {
                lastElement = null;
            }

            Thread.Sleep(200);
        }

        return lastElement;
    }

    public Task TypeTextAsync(ElementQuery query, string text)
    {
        var element = FindElement(query);
        if (element == null)
        {
            throw new Exception($"UI Element not found for query: {query}");
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            ((ValuePattern)pattern).SetValue(text);
            try
            {
                element.SetFocus();
                Thread.Sleep(100);
                System.Windows.Forms.SendKeys.SendWait(" {BACKSPACE}");
            }
            catch { }
            return Task.CompletedTask;
        }

        // Fallback: set focus and try typing or throw
        element.SetFocus();
        System.Windows.Forms.SendKeys.SendWait(text);
        return Task.CompletedTask;
    }

    public Task<string> GetTextAsync(ElementQuery query)
    {
        var element = FindElement(query);
        if (element == null)
        {
            throw new Exception($"UI Element not found for query: {query}");
        }

        // Check ValuePattern first, then Name
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            return Task.FromResult(((ValuePattern)pattern).Current.Value);
        }

        return Task.FromResult(element.Current.Name);
    }

    public Task<bool> IsElementVisibleAsync(ElementQuery query)
    {
        try
        {
            var element = FindElement(query);
            return Task.FromResult(element != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsElementEnabledAsync(ElementQuery query)
    {
        try
        {
            var element = FindElement(query);
            return Task.FromResult(element != null && element.Current.IsEnabled);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task WaitForElementAsync(ElementQuery query, TimeSpan? timeout = null)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            var element = FindElement(query);
            if (element != null)
            {
                return;
            }
            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for UI Element: {query} after {maxWait.TotalSeconds}s");
    }

    public Task ScrollIntoViewAsync(ElementQuery query)
    {
        var element = FindElement(query);
        if (element == null)
        {
            throw new Exception($"UI Element not found for query: {query}");
        }

        if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scrollPattern))
        {
            ((ScrollItemPattern)scrollPattern).ScrollIntoView();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }


    public Task<byte[]> CaptureScreenshotAsync()
    {
        try
        {
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
            if (primaryScreen == null)
            {
                throw new Exception("Primary screen was null.");
            }
            var bounds = primaryScreen.Bounds;
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }

            var tempDir = System.IO.Path.GetTempPath();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = System.IO.Path.Combine(tempDir, $"LAA_Win_Failure_{timestamp}.png");
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            
            Console.WriteLine($"Captured failure screenshot: {filePath}");

            using var ms = new System.IO.MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Task.FromResult(ms.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture screenshot: {ex.Message}");
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    public Task<string> GetAccessibilityTreeAsync()
    {
        if (RootElement == null) return Task.FromResult(string.Empty);
        var writer = new System.IO.StringWriter();
        DumpTree(RootElement, writer, "");
        return Task.FromResult(writer.ToString());
    }

    private void DumpTree(AutomationElement element, System.IO.TextWriter writer, string indent)
    {
        if (element == null) return;
        string type = "Unknown";
        string id = "";
        string name = "";
        try
        {
            type = element.Current.ControlType?.ProgrammaticName ?? "Unknown";
            id = element.Current.AutomationId ?? "";
            name = element.Current.Name ?? "";
        }
        catch { }

        writer.WriteLine($"{indent}[{type}] ID={id} Name=\"{name}\"");

        try
        {
            var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            if (children != null)
            {
                foreach (AutomationElement child in children)
                {
                    DumpTree(child, writer, indent + "  ");
                }
            }
        }
        catch { }
    }

    public AutomationElement? FindElement(ElementQuery query)
    {
        if (RootElement == null)
        {
            throw new InvalidOperationException("RootElement has not been set on the Windows UI Provider.");
        }

        Condition condition;
        var conditions = new List<Condition>();
        if (!string.IsNullOrEmpty(query.AutomationId))
        {
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, query.AutomationId));
        }
        if (!string.IsNullOrEmpty(query.Name))
        {
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, query.Name));
        }
        if (!string.IsNullOrEmpty(query.ClassName))
        {
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, query.ClassName));
        }

        if (conditions.Count == 0)
        {
            throw new ArgumentException("ElementQuery must specify at least AutomationId, Name, or ClassName.");
        }
        else if (conditions.Count == 1)
        {
            condition = conditions[0];
        }
        else
        {
            condition = new AndCondition(conditions.ToArray());
        }

        var timeout = query.Timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var root = RootElement;
            if (root == null) break;

            var found = root.FindFirst(TreeScope.Descendants, condition);
            if (found != null)
            {
                return found;
            }

            try
            {
                var appProcessId = RootElement != null ? RootElement.Current.ProcessId : 0;
                if (appProcessId > 0)
                {
                    var procCond = new PropertyCondition(AutomationElement.ProcessIdProperty, appProcessId);
                    var appWindows = AutomationElement.RootElement.FindAll(TreeScope.Children, procCond);
                    foreach (AutomationElement win in appWindows)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(query.Name) && string.Equals(win.Current.Name, query.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                return win;
                            }
                        }
                        catch { }

                        var winFound = win.FindFirst(TreeScope.Descendants, condition);
                        if (winFound != null)
                        {
                            return winFound;
                        }
                    }
                }

                var globalFound = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition);
                if (globalFound != null)
                {
                    return globalFound;
                }
            }
            catch
            {
                // Ignore desktop search errors
            }

            Thread.Sleep(200);
        }

        return null;
    }

    public async Task WaitForEnabledAsync(ElementQuery query, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var element = FindElement(query);
            if (element != null && element.Current.IsEnabled)
            {
                return;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Timed out waiting for element {query} to become enabled.");
    }
}
