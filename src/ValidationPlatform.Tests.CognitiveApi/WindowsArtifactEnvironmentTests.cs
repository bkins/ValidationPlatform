using System.Diagnostics;
using System.Text.Json;

namespace ValidationPlatform.Tests.CognitiveApi;

public sealed class WindowsArtifactEnvironmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cp-windows-environment-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Dev", "5273")]
    [InlineData("QA", "5274")]
    [InlineData("Prod", "5275")]
    [Trait("Category", "ClientContract")]
    public void Build_SuccessfulPublish_RecordsPublishInputsInsideManifest(string environment, string port)
    {
        Prepare(environment);
        var buildScript = Path.Combine(FindScripts(), "LAA-Windows-Build.ps1");
        var output = Path.Combine(_root, "compiled");
        var command = """
            function dotnet {
                $outputIndex = [Array]::IndexOf($args, '-o')
                Set-Content -LiteralPath (Join-Path $args[$outputIndex + 1] 'LocalAIAssistant.Ui.Maui.exe') -Value 'fake compiler output'
                $args | ConvertTo-Json | Set-Content -LiteralPath '__ARGS__'
                $global:LASTEXITCODE = 0
            }
            & '__SCRIPT__' -Environment '__ENV__' -Version '1.0.0.1' -OutputPath '__OUTPUT__'
            """.Replace("__ARGS__", Literal(Path.Combine(_root, "arguments.json")), StringComparison.Ordinal)
               .Replace("__SCRIPT__", Literal(buildScript), StringComparison.Ordinal)
               .Replace("__ENV__", environment, StringComparison.Ordinal)
               .Replace("__OUTPUT__", Literal(output), StringComparison.Ordinal);

        var result = Run(command);

        Assert.True(result.ExitCode == 0, result.Output);
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "build-environment.json")));
        Assert.Equal(environment, provenance.RootElement.GetProperty("BuiltFor").GetString());
        Assert.Equal("1.0.0.1", provenance.RootElement.GetProperty("Version").GetString());
        Assert.Equal($"http://localhost:{port}", provenance.RootElement.GetProperty("ApiBaseUrl").GetString());
        Assert.Contains("build-environment.json", File.ReadAllText(Path.Combine(output, "payload-manifest.json")), StringComparison.Ordinal);
        var arguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(_root, "arguments.json")))!;
        Assert.Contains($"/p:ApiEnvironmentName={environment}", arguments);
        Assert.Contains($"/p:ApiBaseUrl=http://localhost:{port}", arguments);
    }

    [Theory]
    [InlineData("Dev")]
    [InlineData("QA")]
    [InlineData("Prod")]
    [Trait("Category", "ClientContract")]
    public void Deploy_MatchingEnvironment_PreservesManifestAndBacksUpPreviousTarget(string environment)
    {
        Prepare(environment);
        var result = Deploy(environment);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("new launcher", File.ReadAllText(Path.Combine(_root, environment, "LocalAIAssistant.Ui.Maui.exe")));
        Assert.Single(Directory.GetDirectories(_root, $"{environment}.backup.*"));
        Assert.Equal(File.ReadAllText(Path.Combine(_root, "artifact", "payload-manifest.json")),
                     File.ReadAllText(Path.Combine(_root, environment, "payload-manifest.json")));
    }

    [Theory]
    [InlineData("mismatch", "built for")]
    [InlineData("missing", "build-environment.json")]
    [InlineData("malformed", "build-environment.json")]
    [InlineData("version", "version")]
    [InlineData("endpoint", "endpoint")]
    [InlineData("unknown", "environment")]
    [InlineData("schema", "schema")]
    [InlineData("fields", "BuiltFor")]
    [InlineData("tamper", "Payload manifest mismatch")]
    [Trait("Category", "ClientContract")]
    public void Deploy_InvalidProvenance_LeavesTargetAndDirectoriesUntouched(string problem, string expectedError)
    {
        Prepare("Dev");
        var provenancePath = Path.Combine(_root, "artifact", "build-environment.json");
        if (problem == "missing") File.Delete(provenancePath);
        else if (problem == "malformed") File.WriteAllText(provenancePath, "{invalid");
        else if (problem == "schema") File.WriteAllText(provenancePath, "{\"SchemaVersion\":2}");
        else if (problem == "fields") File.WriteAllText(provenancePath, "{\"SchemaVersion\":1}");
        else WriteProvenance(problem == "mismatch" ? "QA" : problem == "unknown" ? "Other" : "Dev",
                             problem == "version" ? "1.0.0.2" : "1.0.0.1",
                             problem is "endpoint" or "tamper" ? "http://localhost:5275" : null);
        if (problem != "tamper") CreateManifest();

        var result = Deploy("Dev");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedError, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old launcher", File.ReadAllText(Path.Combine(_root, "Dev", "LocalAIAssistant.Ui.Maui.exe")));
        Assert.DoesNotContain(Directory.GetDirectories(_root), path => Path.GetFileName(path).StartsWith("Dev.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Dev", "QA")]
    [InlineData("Dev", "Prod")]
    [InlineData("QA", "Dev")]
    [InlineData("QA", "Prod")]
    [InlineData("Prod", "Dev")]
    [InlineData("Prod", "QA")]
    [Trait("Category", "ClientContract")]
    public void Deploy_DifferentTargetEnvironment_RejectsEveryCrossEnvironmentPair(string builtFor, string target)
    {
        Prepare(target);
        WriteProvenance(builtFor, "1.0.0.1");
        CreateManifest();

        var result = Deploy(target);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("built for", result.Output, StringComparison.Ordinal);
        Assert.Equal("old launcher", File.ReadAllText(Path.Combine(_root, target, "LocalAIAssistant.Ui.Maui.exe")));
        Assert.DoesNotContain(Directory.GetDirectories(_root), path => Path.GetFileName(path).StartsWith(target + ".", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "ClientContract")]
    public void Deploy_ActivationVerificationFails_RestoresPreviousTarget()
    {
        Prepare("Dev");
        var manifestPath = Path.Combine(_root, "PayloadManifest.ps1");
        File.AppendAllText(manifestPath, """

            $originalManifestCheck = ${function:Test-PayloadManifest}
            function Test-PayloadManifest {
                param([string]$RootPath)
                if ($RootPath -eq '__TARGET__') { throw 'Injected activation failure' }
                & $originalManifestCheck -RootPath $RootPath
            }
            """.Replace("__TARGET__", Literal(Path.Combine(_root, "Dev")), StringComparison.Ordinal));

        var result = Deploy("Dev");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Injected activation failure", result.Output, StringComparison.Ordinal);
        Assert.Equal("old launcher", File.ReadAllText(Path.Combine(_root, "Dev", "LocalAIAssistant.Ui.Maui.exe")));
        Assert.Single(Directory.GetDirectories(_root, "Dev.failed.*"));
    }

    private void Prepare(string environment)
    {
        Directory.CreateDirectory(_root);
        var scripts = FindScripts();
        foreach (var name in new[] { "PayloadManifest.ps1", "LAA-Windows-Deploy.ps1" })
            File.Copy(Path.Combine(scripts, name), Path.Combine(_root, name));
        var helper = Path.Combine(scripts, "WindowsArtifactEnvironment.ps1");
        if (File.Exists(helper)) File.Copy(helper, Path.Combine(_root, "WindowsArtifactEnvironment.ps1"));
        var deployPath = Path.Combine(_root, "LAA-Windows-Deploy.ps1");
        var script = File.ReadAllText(deployPath);
        foreach (var target in new[] { "Dev", "QA", "Prod" })
            script = script.Replace($@"C:\CP\Deploy\LaaWindows\{target}", Path.Combine(_root, target), StringComparison.Ordinal);
        File.WriteAllText(deployPath, script);
        Directory.CreateDirectory(Path.Combine(_root, "artifact"));
        Directory.CreateDirectory(Path.Combine(_root, environment));
        File.WriteAllText(Path.Combine(_root, "artifact", "LocalAIAssistant.Ui.Maui.exe"), "new launcher");
        File.WriteAllText(Path.Combine(_root, environment, "LocalAIAssistant.Ui.Maui.exe"), "old launcher");
        WriteProvenance(environment, "1.0.0.1");
        CreateManifest();
    }

    private void WriteProvenance(string environment, string version, string? endpoint = null)
    {
        var port = environment switch { "QA" => 5274, "Prod" => 5275, _ => 5273 };
        File.WriteAllText(Path.Combine(_root, "artifact", "build-environment.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1
          , BuiltFor = environment
          , Version = version
          , ApiBaseUrl = endpoint ?? $"http://localhost:{port}"
        }));
    }

    private void CreateManifest()
    {
        var result = Run($". '{Literal(Path.Combine(_root, "PayloadManifest.ps1"))}'; New-PayloadManifest -RootPath '{Literal(Path.Combine(_root, "artifact"))}' | Out-Null");
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private (int ExitCode, string Output) Deploy(string environment) =>
        Run($"& '{Literal(Path.Combine(_root, "LAA-Windows-Deploy.ps1"))}' -Environment {environment} -Version '1.0.0.1' -ArtifactPath '{Literal(Path.Combine(_root, "artifact"))}'");

    private static (int ExitCode, string Output) Run(string command)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false
          , CreateNoWindow = true
          , RedirectStandardOutput = true
          , RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Fixture deployment timed out."); }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FindScripts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var relative in new[] { "CognitivePlatform.Admin/Scripts", "CognitivePlatform/CognitivePlatform.Admin/Scripts" })
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(Path.Combine(candidate, "LAA-Windows-Deploy.ps1"))) return candidate;
            }
        }
        throw new DirectoryNotFoundException("Active Admin scripts not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
