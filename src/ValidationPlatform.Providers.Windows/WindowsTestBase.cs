using System.IO;
using ValidationPlatform.Core.Interfaces;

namespace ValidationPlatform.Providers.Windows;

public abstract class WindowsTestBase : IDisposable
{
    protected readonly WindowsUiValidationProvider UiProvider;
    protected readonly WindowsApplicationDriver Driver;
    private readonly string[] _exePathCandidates;
    private readonly int _waitSeconds;
    private bool _isInitialized;

    protected WindowsTestBase(string[] exePathCandidates, int waitSeconds = 30)
    {
        _exePathCandidates = exePathCandidates;
        _waitSeconds = waitSeconds;
        UiProvider = new WindowsUiValidationProvider();
        Driver = new WindowsApplicationDriver(UiProvider);
    }

    protected async Task EnsureAppLaunchedAsync()
    {
        if (_isInitialized) return;

        var exePath = ResolveExePath(_exePathCandidates);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Could not find target application executable: {exePath}");
        }

        await Driver.LaunchAsync(exePath);
        await Driver.WaitForReadyAsync(TimeSpan.FromSeconds(Math.Max(_waitSeconds, 15)));
        _isInitialized = true;
    }

    private string ResolveExePath(string[] candidates)
    {
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException(
            $"Could not find target application executable. Evaluated paths:\n{string.Join("\n", candidates)}");
    }

    public virtual void Dispose()
    {
        if (_isInitialized)
        {
            Driver.TerminateAsync().GetAwaiter().GetResult();
        }
    }
}
