using System.Diagnostics;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("========================================");
Console.WriteLine("   Validation Platform Test Orchestrator");
Console.WriteLine("========================================");
Console.ResetColor();

var targetProject = "";
var suiteName = args.Length > 0 ? args[0].ToLowerInvariant() : "";

if (suiteName.Contains("laa") || suiteName.Contains("smoke"))
{
    targetProject = "src/ValidationPlatform.Tests.LaaSmoke/ValidationPlatform.Tests.LaaSmoke.csproj";
    Console.WriteLine("Targeting Suite: LAA Smoke Tests");
}
else if (suiteName.Contains("watchlist") || suiteName.Contains("watch"))
{
    targetProject = "src/ValidationPlatform.Tests.WatchList/ValidationPlatform.Tests.WatchList.csproj";
    Console.WriteLine("Targeting Suite: WatchList UI & Integration Tests");
}
else if (suiteName.Contains("api") || suiteName.Contains("cognitive"))
{
    targetProject = "src/ValidationPlatform.Tests.CognitiveApi/ValidationPlatform.Tests.CognitiveApi.csproj";
    Console.WriteLine("Targeting Suite: Cognitive Platform API Tests");
}
else if (suiteName.Contains("insandouts") || suiteName.Contains("counter"))
{
    targetProject = "src/ValidationPlatform.Tests.InsAndOuts/ValidationPlatform.Tests.InsAndOuts.csproj";
    Console.WriteLine("Targeting Suite: InsAndOuts Tests");
}
else if (suiteName.Contains("nl") || suiteName.Contains("flow"))
{
    targetProject = "src/ValidationPlatform.Tests.NlFlows/ValidationPlatform.Tests.NlFlows.csproj";
    Console.WriteLine("Targeting Suite: NL E2E Flow Tests");
}
else if (suiteName.Contains("team") || suiteName.Contains("commandcenter"))
{
    targetProject = "src/ValidationPlatform.Tests.TeamCommandCenter/ValidationPlatform.Tests.TeamCommandCenter.csproj";
    Console.WriteLine("Targeting Suite: Team Command Center Tests");
}
else
{
    Console.WriteLine("Targeting Suite: All Solution Tests");
}

var forceFailure = args.Any(arg => arg.ToLowerInvariant().Contains("force-failure") || arg.ToLowerInvariant().Contains("forcefailure"));
if (forceFailure)
{
    Console.WriteLine("Forced Failure Flag Detected");
}

var filterArg = "";
var filterIndex = Array.FindIndex(args, arg => arg.Equals("--filter", StringComparison.OrdinalIgnoreCase));
if (filterIndex >= 0 && filterIndex + 1 < args.Length)
{
    filterArg = $" --filter \"{args[filterIndex + 1]}\"";
    Console.WriteLine($"Applying Filter: {args[filterIndex + 1]}");
}

var commandArgs = string.IsNullOrEmpty(targetProject)
    ? "test"
    : $"test \"{targetProject}\"";

var extraArgs = (forceFailure ? " --environment FORCE_FAILURE=true" : "") + filterArg;

var startInfo = new ProcessStartInfo
{
    FileName               = "dotnet"
  , Arguments              = $"{commandArgs} --logger:console;verbosity=detailed{extraArgs}"
  , WorkingDirectory       = @"C:\Users\benho\source\repos\CP\ValidationPlatform"
  , UseShellExecute        = false
  , RedirectStandardOutput = true
  , RedirectStandardError  = true
  , CreateNoWindow         = true
};

var stopwatch = Stopwatch.StartNew();
using var process = new Process { StartInfo = startInfo };

process.OutputDataReceived += (sender, eventArgs) => { if (eventArgs.Data != null) Console.WriteLine(eventArgs.Data); };
process.ErrorDataReceived  += (sender, eventArgs) => { if (eventArgs.Data != null) Console.Error.WriteLine(eventArgs.Data); };

var timeout = TimeSpan.FromMinutes(10);
using var cts = new CancellationTokenSource(timeout);

try
{
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    await process.WaitForExitAsync(cts.Token);
    stopwatch.Stop();

    Console.WriteLine();
    Console.ForegroundColor = process.ExitCode == 0 ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine($"Validation completed with exit code: {process.ExitCode} (Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s)");
    Console.ResetColor();

    Environment.Exit(process.ExitCode);
}
catch (OperationCanceledException)
{
    stopwatch.Stop();
    try { process.Kill(entireProcessTree: true); } catch { }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Runner Timeout] Test suite execution exceeded {timeout.TotalMinutes:F1} minutes and was forcefully terminated.");
    Console.ResetColor();

    Environment.Exit(1);
}
