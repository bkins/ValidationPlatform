using System.Diagnostics;

namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class ApiTrayManagerTests
{
    [Theory]
    [Trait("Category", "ClientContract")]
    [InlineData("EnvironmentMapping")]
    [InlineData("Stopped")]
    [InlineData("Responding")]
    [InlineData("Unresponsive")]
    [InlineData("PortConflict")]
    [InlineData("DuplicateStart")]
    [InlineData("StopOnlyOwnedProcess")]
    [InlineData("FailedStartup")]
    [InlineData("NativeProcessIdentity")]
    public async Task ApiTray_Scenario_PreservesEnvironmentAndProcessBoundaries(string scenario)
    {
        var repositoryRoot = @"C:\Users\benho\source\repos\CP\ValidationPlatform";
        var script = Path.Combine(repositoryRoot, "src", "ValidationPlatform.Tests.CognitiveApi", "ApiTrayManagerTests.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
                        {
                            UseShellExecute        = false
                          , CreateNoWindow         = true
                          , RedirectStandardOutput = true
                          , RedirectStandardError  = true
                        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Scenario", scenario })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var completed = process.WaitForExit(30000);
        if (!completed) process.Kill(entireProcessTree: true);

        Assert.True(completed, "Tray regression scenario timed out.");
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
