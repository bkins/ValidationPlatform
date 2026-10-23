using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ValidationPlatform.Providers.Windows;

/// <summary>Borrowed handles are never terminated; owned cleanup verifies process identity.</summary>
public sealed class WindowsProcessSession : IDisposable
{
    public Process? Process { get; private set; }
    private bool _owned;
    private DateTime _startedAt;
    private string? _executable;

    public void Launch(ProcessStartInfo start)
    {
        EnsureEmpty();
        Process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Application failed to start.");
        _owned = true;
        _startedAt = Process.StartTime.ToUniversalTime();
        // Query the process image: MainModule can report ntdll.dll during loader startup.
        var deadline = Stopwatch.StartNew();
        do
        {
            Process.Refresh();
            _executable = GetExecutablePath(Process);
            if (_executable is not null || Process.HasExited) break;
            Thread.Sleep(10);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(2));
    }

    private static string? GetExecutablePath(Process process)
    {
        if (!OperatingSystem.IsWindows()) return process.MainModule?.FileName;
        var buffer = new StringBuilder(32768);
        var length = buffer.Capacity;
        return QueryFullProcessImageName(process.Handle, 0, buffer, ref length) ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
    public void Attach(int processId)
    {
        EnsureEmpty();
        Process = System.Diagnostics.Process.GetProcessById(processId);
        _owned = false;
    }

    private void EnsureEmpty()
    {
        if (Process is not null) throw new InvalidOperationException("Release the current process session before launching or attaching another.");
    }

    public void Dispose()
    {
        var process = Process;
        Process = null;
        if (process is null) return;
        using (process)
        {
            if (!_owned || process.HasExited) return;
            process.Refresh();
            if (_executable is null || process.StartTime.ToUniversalTime() != _startedAt || !string.Equals(GetExecutablePath(process), _executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Application process identity changed; refusing termination.");
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { return; }
            if (!process.WaitForExit(5000)) throw new TimeoutException("Owned application did not stop within five seconds.");
        }
    }
}
