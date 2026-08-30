namespace ValidationPlatform.Core.Interfaces;

public interface IApplicationDriver
{
    Task LaunchAsync(string path, string[]? arguments = null);
    Task TerminateAsync();
    Task AttachAsync(int processId);
    Task WaitForReadyAsync(TimeSpan timeout);
    bool IsRunning { get; }
}
