<#
.SYNOPSIS
    Release gate script for the Cognitive Platform Universe.
    Runs all test suites sequentially, logs results, and enforces gates.
.PARAMETER SkipLaa
    Skips the LAA MAUI UI Smoke tests (useful for rapid dev iteration).
.PARAMETER LlmProvider
    LLM provider used by live API suites. Defaults to Mock for local readiness safety.
.PARAMETER SuiteTimeoutMinutes
    Maximum time allowed for each suite before the release gate fails it. Defaults to 30 minutes
    because the Controller Integration suite contains broad CRUD coverage.
#>
param(
    [switch]$SkipLaa,
    [ValidateSet("Mock", "Groq")]
    [string]$LlmProvider = "Mock",
    [int]$SuiteTimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"

# Setup directories
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactsDir = Join-Path $scriptDir ".agents\artifacts\release-gate-results"
if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $artifactsDir "release-gate-$timestamp.log"
$suiteOutputDir = Join-Path $artifactsDir "release-gate-$timestamp-suite-output"
New-Item -ItemType Directory -Path $suiteOutputDir -Force | Out-Null

# Define test suites
$suites = @(
    @{
        Name = "Controller Integration CRUD Cycles (IntegrationTests)"
        Path = "C:\Users\benho\source\repos\CognitivePlatform\src\CognitivePlatform.IntegrationTests\CognitivePlatform.IntegrationTests.csproj"
        Skip = $false
        Filter = "Category=Integration"
        RequiresExternalApi = $false
    },
    @{
        Name = "API Health & Probes (CognitiveApi)"
        Path = "$scriptDir\src\ValidationPlatform.Tests.CognitiveApi\ValidationPlatform.Tests.CognitiveApi.csproj"
        Skip = $false
        Filter = ""
        RequiresExternalApi = $true
    },
    @{
        Name = "NL E2E Flow Tests (NlFlows)"
        Path = "$scriptDir\src\ValidationPlatform.Tests.NlFlows\ValidationPlatform.Tests.NlFlows.csproj"
        Skip = $false
        Filter = ""
        RequiresExternalApi = $true
    },
    @{
        Name = "LAA UI Smoke Tests (LaaSmoke)"
        Path = "$scriptDir\src\ValidationPlatform.Tests.LaaSmoke\ValidationPlatform.Tests.LaaSmoke.csproj"
        Skip = $SkipLaa
        Filter = ""
        RequiresExternalApi = $true
    }
)

if ($LlmProvider -eq "Mock") {
    $suites | Where-Object { $_.Name -eq "NL E2E Flow Tests (NlFlows)" } | ForEach-Object {
        $_.Filter = "Category!=RealLlm"
    }
}

function Write-Log($msg, $color = "White") {
    Write-Host $msg -ForegroundColor $color
    Add-Content -Path $logFile -Value $msg
}

Write-Log "==========================================================" -ForegroundColor Cyan
Write-Log "               COGNITIVE PLATFORM RELEASE GATE            " -ForegroundColor Cyan
Write-Log "==========================================================" -ForegroundColor Cyan
Write-Log "Timestamp: $(Get-Date)"
Write-Log "Log file:  $logFile"
Write-Log "Suite output directory: $suiteOutputDir"
if ($SkipLaa) {
    Write-Log "Configuration: SkipLaa=True (LAA Smoke tests will be skipped)" -ForegroundColor Yellow
}
Write-Log "Configuration: LlmProvider=$LlmProvider"
Write-Log "Configuration: SuiteTimeoutMinutes=$SuiteTimeoutMinutes"
Write-Log "----------------------------------------------------------"

$anyFailed = $false
$results = @()
$apiProc = $null
$ownedApiProcessIds = @()
$apiProcessIdsBeforeGate = @(Get-Process -Name CognitivePlatform.Api -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

function Start-TestApi {
    if ($apiProc -and -not $apiProc.HasExited) {
        return
    }

    # 1. Kill any existing test process on 5276
    Write-Log "Cleaning up test port 5276..."
    Get-NetTCPConnection -LocalPort 5276 -ErrorAction Ignore | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction Ignore }

    $testingDataDir = [System.IO.Path]::GetFullPath("C:\CP\Data\Testing")
    if ((Test-Path -LiteralPath $testingDataDir) -and $testingDataDir.Equals("C:\CP\Data\Testing", [StringComparison]::OrdinalIgnoreCase)) {
        Write-Log "Resetting Testing data directory..."
        Remove-Item -LiteralPath $testingDataDir -Recurse -Force -ErrorAction Stop
    }

    # 2. Start background API on test port 5276 in Testing environment
    $apiProjectPath = "C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\CognitivePlatform.Api.csproj"
    $apiDllPath = "C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\bin\Debug\net10.0\CognitivePlatform.Api.dll"
    $apiStdoutPath = Join-Path $suiteOutputDir "api-stdout.log"
    $apiStderrPath = Join-Path $suiteOutputDir "api-stderr.log"
    Write-Log "Building API before background launch..." -ForegroundColor Yellow
    dotnet build "$apiProjectPath" --no-restore -p:GeneratePackageOnBuild=false | Add-Content -Path $apiStdoutPath
    Write-Log "Starting background API on port 5276 (Testing environment) using $LlmProvider provider..." -ForegroundColor Yellow
    $apiProcessStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $apiProcessStartInfo.FileName = "dotnet"
    $apiProcessStartInfo.Arguments = "`"$apiDllPath`""
    $apiProcessStartInfo.WorkingDirectory = Split-Path -Parent $apiProjectPath
    $apiProcessStartInfo.UseShellExecute = $false
    $apiProcessStartInfo.RedirectStandardOutput = $true
    $apiProcessStartInfo.RedirectStandardError = $true
    $apiProcessStartInfo.CreateNoWindow = $true
    $apiProcessStartInfo.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:5276"
    $apiProcessStartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Testing"
    $apiProcessStartInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Testing"
    $apiProcessStartInfo.EnvironmentVariables["LlmClient__Provider"] = $LlmProvider

    $apiProc = New-Object System.Diagnostics.Process
    $apiProc.StartInfo = $apiProcessStartInfo
    $apiProc.Start() | Out-Null
    $script:ownedApiProcessIds += $apiProc.Id
    $apiProc.BeginOutputReadLine()
    $apiProc.BeginErrorReadLine()
    Register-ObjectEvent -InputObject $apiProc -EventName OutputDataReceived -Action {
        if ($EventArgs.Data) {
            Add-Content -Path $Event.MessageData.StdoutPath -Value $EventArgs.Data
        }
    } -MessageData @{ StdoutPath = $apiStdoutPath } | Out-Null
    Register-ObjectEvent -InputObject $apiProc -EventName ErrorDataReceived -Action {
        if ($EventArgs.Data) {
            Add-Content -Path $Event.MessageData.StderrPath -Value $EventArgs.Data
        }
    } -MessageData @{ StderrPath = $apiStderrPath } | Out-Null

    # Wait for API to be listening (TCP, up to 45 seconds)
    $online = $false
    $swOnline = [System.Diagnostics.Stopwatch]::StartNew()
    while ($swOnline.Elapsed.TotalSeconds -lt 45) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient("localhost", 5276)
            $tcp.Close()
            $online = $true
            break;
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $online) {
        Write-Log "ERROR: Background API failed to listen on port 5276 within 45 seconds." -ForegroundColor Red
        exit 1
    }

    Get-NetTCPConnection -LocalPort 5276 -State Listen -ErrorAction Ignore | ForEach-Object {
        $script:ownedApiProcessIds += $_.OwningProcess
    }

    Write-Log "API is listening on port 5276. Waiting for /health/ready..." -ForegroundColor Yellow

    # Wait for /health/ready to return 200 (heavy startup: LLM probe, etc.) using .NET HttpClient (no memory leaks)
    $ready = $false
    $swReady = [System.Diagnostics.Stopwatch]::StartNew()
    [System.Reflection.Assembly]::LoadWithPartialName("System.Net.Http") | Out-Null
    $httpClient = New-Object System.Net.Http.HttpClient
    $httpClient.Timeout = [System.TimeSpan]::FromSeconds(5)
    while ($swReady.Elapsed.TotalSeconds -lt 120) {
        try {
            $respTask = $httpClient.GetAsync("http://localhost:5276/health/ready")
            $resp = $respTask.Result
            if ($resp.StatusCode -eq [System.Net.HttpStatusCode]::OK) {
                $ready = $true
                break
            }
        } catch {
            # 503 or connection refused — keep waiting
        }
        Start-Sleep -Milliseconds 1000
    }
    $httpClient.Dispose()

    if (-not $ready) {
        Write-Log "WARNING: /health/ready did not return 200 within 120s. Continuing anyway (some tests may fail)." -ForegroundColor Yellow
    } else {
        Write-Log "Background API is fully ready on port 5276 ($([Math]::Round($swReady.Elapsed.TotalSeconds, 1))s)." -ForegroundColor Green
    }
    Write-Log "----------------------------------------------------------"
}

function Stop-ProcessTree {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId
    )

    try {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-ProcessTree -ProcessId $_.ProcessId
        }
    } catch {
        # Best-effort descendant discovery.
    }

    try {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    } catch {
        # Best-effort cleanup.
    }
}

try {
    # 3. Run each suite
    foreach ($suite in $suites) {
        if ($suite.Skip) {
            Write-Log "Skipping: $($suite.Name) [Skipped by configuration]" -ForegroundColor Yellow
            $results += [PSCustomObject]@{
                SuiteName = $suite.Name
                Status    = "SKIPPED"
                Duration  = "0s"
            }
            continue
        }

        if ($suite.RequiresExternalApi) {
            Start-TestApi
        }

        Write-Log "Running: $($suite.Name)..." -ForegroundColor White
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        $cmdArgs = "test `"$($suite.Path)`" --no-restore -p:GeneratePackageOnBuild=false --logger `"console;verbosity=minimal`""
        if ($suite.Filter) {
            $cmdArgs += " --filter `"$($suite.Filter)`""
        }

        $envVars = @{
            "LlmClient__Provider"           = $LlmProvider
            "ValidationPlatformWindowsOnly" = "true"
        }
        if ($suite.RequiresExternalApi) {
            $envVars["API_BASE_URL"] = "http://localhost:5276"
        }
        $envVars["ASPNETCORE_ENVIRONMENT"] = "Testing"
        $envVars["DOTNET_ENVIRONMENT"] = "Testing"

        # Save current env values
        $savedEnv = @{}
        foreach ($key in $envVars.Keys) {
            $savedEnv[$key] = [System.Environment]::GetEnvironmentVariable($key, "Process")
            [System.Environment]::SetEnvironmentVariable($key, $envVars[$key], "Process")
        }

        $safeSuiteName = ($suite.Name -replace '[^A-Za-z0-9]+', '-').Trim('-')
        $tempFile = Join-Path $suiteOutputDir "$safeSuiteName.log"
        try {
            # We redirect to files via cmd to completely avoid pipe buffering deadlock issues in .NET
            $cmdArgsEscaped = $cmdArgs -replace '"', '\"'
            $redirectArgs = "/c dotnet $cmdArgsEscaped > `"$tempFile`" 2>&1"
            
            $shellStartInfo = New-Object System.Diagnostics.ProcessStartInfo
            $shellStartInfo.FileName = "cmd.exe"
            $shellStartInfo.Arguments = $redirectArgs
            $shellStartInfo.UseShellExecute = $false
            $shellStartInfo.CreateNoWindow = $true
            
            $shellProc = New-Object System.Diagnostics.Process
            $shellProc.StartInfo = $shellStartInfo
            $shellProc.Start() | Out-Null

            $timeoutMs = [int][TimeSpan]::FromMinutes($SuiteTimeoutMinutes).TotalMilliseconds
            if (-not $shellProc.WaitForExit($timeoutMs)) {
                Stop-ProcessTree -ProcessId $shellProc.Id

                $exitCode = 124
                Add-Content -Path $tempFile -Value ""
                Add-Content -Path $tempFile -Value "ERROR: Suite timed out after $SuiteTimeoutMinutes minute(s)."
            } else {
                $exitCode = $shellProc.ExitCode
            }
        } finally {
            # Restore env variables
            foreach ($key in $savedEnv.Keys) {
                [System.Environment]::SetEnvironmentVariable($key, $savedEnv[$key], "Process")
            }
        }

        $sw.Stop()
        $duration = "$([Math]::Round($sw.Elapsed.TotalSeconds, 1))s"

        $outputContent = ""
        if (Test-Path $tempFile) {
            $outputContent = Get-Content -Path $tempFile -Raw
        }
        Add-Content -Path $logFile -Value ""
        Add-Content -Path $logFile -Value "----- BEGIN SUITE OUTPUT: $($suite.Name) -----"
        Add-Content -Path $logFile -Value $outputContent
        Add-Content -Path $logFile -Value "----- END SUITE OUTPUT: $($suite.Name) -----"

        if ($exitCode -eq 0) {
            Write-Log "PASSED: $($suite.Name) in $duration" -ForegroundColor Green
            $results += [PSCustomObject]@{
                SuiteName = $suite.Name
                Status    = "PASSED"
                Duration  = $duration
            }
        } else {
            Write-Log "FAILED: $($suite.Name) in $duration (Exit Code: $exitCode)" -ForegroundColor Red
            $anyFailed = $true
            $results += [PSCustomObject]@{
                SuiteName = $suite.Name
                Status    = "FAILED"
                Duration  = $duration
            }
        }
        Write-Log "----------------------------------------------------------"
    }
}
finally {
    if ($apiProc) {
        Write-Log "Stopping background API process on port 5276..." -ForegroundColor Yellow
        foreach ($processId in ($ownedApiProcessIds | Select-Object -Unique)) {
            Stop-ProcessTree -ProcessId $processId
        }

        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            $gateOwnedApiProcesses = @(Get-Process -Name CognitivePlatform.Api -ErrorAction SilentlyContinue | Where-Object {
                $apiProcessIdsBeforeGate -notcontains $_.Id
            })

            if ($gateOwnedApiProcesses.Count -eq 0) {
                break
            }

            $gateOwnedApiProcesses | ForEach-Object {
                Stop-ProcessTree -ProcessId $_.Id
            }

            Start-Sleep -Milliseconds 500
        }

        $apiProc.Dispose()
    }
}

# Print final report
Write-Log ""
Write-Log "==========================================================" -ForegroundColor Cyan
Write-Log "                      SUMMARY REPORT                      " -ForegroundColor Cyan
Write-Log "==========================================================" -ForegroundColor Cyan

foreach ($res in $results) {
    $col = "White"
    if ($res.Status -eq "PASSED") { $col = "Green" }
    elseif ($res.Status -eq "FAILED") { $col = "Red" }
    elseif ($res.Status -eq "SKIPPED") { $col = "Yellow" }
    
    Write-Log "$($res.SuiteName.PadRight(50)) : $($res.Status.PadRight(10)) ($($res.Duration))" -ForegroundColor $col
}

Write-Log "==========================================================" -ForegroundColor Cyan

if ($anyFailed) {
    Write-Log "RELEASE GATE CHECK: FAILED (One or more suites failed)" -ForegroundColor Red
    exit 1
} else {
    Write-Log "RELEASE GATE CHECK: SUCCESS (All run suites passed)" -ForegroundColor Green
    exit 0
}
