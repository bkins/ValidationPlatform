using System.Diagnostics;

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
        _executable = Process.MainModule?.FileName;
    }

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
            if (_executable is null || process.StartTime.ToUniversalTime() != _startedAt || !string.Equals(process.MainModule?.FileName, _executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Application process identity changed; refusing termination.");
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { return; }
            if (!process.WaitForExit(5000)) throw new TimeoutException("Owned application did not stop within five seconds.");
        }
    }
}
