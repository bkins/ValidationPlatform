using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using ValidationPlatform.Core.Interfaces;

namespace ValidationPlatform.Providers.Windows;

public class WindowsApplicationDriver : IApplicationDriver
{
    private Process? _process;
    private readonly WindowsUiValidationProvider _uiProvider;

    public bool IsRunning => _process != null && !_process.HasExited;
    public int ProcessId => _process?.Id ?? 0;

    public WindowsApplicationDriver(WindowsUiValidationProvider uiProvider)
    {
        _uiProvider = uiProvider;
    }

    public Task LaunchAsync(string path, string[]? arguments = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Application is already running.");
        }

        var processName = Path.GetFileNameWithoutExtension(path);
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                p.Kill(true);
                p.WaitForExit(1000);
            }
            catch
            {
                // Safe ignore
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments != null ? string.Join(" ", arguments) : string.Empty,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        _process = Process.Start(startInfo);
        if (_process == null)
        {
            throw new Exception($"Failed to start process: {path}");
        }

        return Task.CompletedTask;
    }

    public Task TerminateAsync()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to kill process: {ex.Message}");
            }
        }

        _process = null;
        _uiProvider.RootElement = null;
        return Task.CompletedTask;
    }

    public Task AttachAsync(int processId)
    {
        _process = Process.GetProcessById(processId);
        AttachRootElement();
        return Task.CompletedTask;
    }

    public Task WaitForReadyAsync(TimeSpan timeout)
    {
        if (_process == null)
        {
            throw new InvalidOperationException("No process is currently running.");
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (_process.HasExited)
            {
                throw new Exception("Application process exited prematurely.");
            }

            // Refresh window handle
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    AttachRootElement();
                    if (_uiProvider.RootElement != null)
                    {
                        return Task.CompletedTask;
                    }
                }
                catch
                {
                    // MainWindowHandle might exist but UIA structure not fully populated yet
                }
            }

            Thread.Sleep(300);
        }

        throw new TimeoutException("Timed out waiting for application window to be ready.");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_RESTORE = 9;

    private void ForceFocus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SwitchToThisWindow(hwnd, true);
        SetForegroundWindow(hwnd);
    }

    private void AttachRootElement()
    {
        if (_process == null) return;

        // Focus using WScript.Shell AppActivate (matching legacy PowerShell behavior)
        try
        {
            var wscriptType = Type.GetTypeFromProgID("WScript.Shell");
            if (wscriptType != null)
            {
                dynamic? wsh = Activator.CreateInstance(wscriptType);
                wsh?.AppActivate(_process.Id);
            }
        }
        catch { }

        // Try to obtain AutomationElement from window handle
        if (_process.MainWindowHandle != IntPtr.Zero)
        {
            var hwnd = _process.MainWindowHandle;
            ForceFocus(hwnd);
            _uiProvider.RootElement = AutomationElement.FromHandle(hwnd);
            return;
        }

        // Fallback: search by Process ID
        var cond = new PropertyCondition(AutomationElement.ProcessIdProperty, _process.Id);
        var found = AutomationElement.RootElement.FindFirst(TreeScope.Children, cond);
        if (found != null)
        {
            var hwnd = (IntPtr)found.Current.NativeWindowHandle;
            ForceFocus(hwnd);
            _uiProvider.RootElement = found;
        }
    }
}
