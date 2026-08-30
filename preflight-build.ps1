<#
.SYNOPSIS
    Preflight checks for ValidationPlatform builds.
.DESCRIPTION
    Detects running processes that commonly lock build output used by
    ValidationPlatform.slnx, especially WatchLists.exe and lingering testhost
    processes. This script is intentionally no-kill: it reports the blocking
    process and exits non-zero so the operator can close the app deliberately.
#>
param(
    [switch]$WarnOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$watchListRepo = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "..\..\WatchList\WatchLists"))
$watchListOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $watchListRepo "bin"))
$validationOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "src\ValidationPlatform.Tests.WatchList\bin"))

$blockingProcesses = @()
$candidateNames = @("WatchLists", "testhost")

foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    if ($candidateNames -notcontains $process.ProcessName) {
        continue
    }

    $path = $null
    try {
        $path = $process.Path
    } catch {
        $path = $null
    }

    if ($process.ProcessName -eq "WatchLists" -or
        ($path -and ($path.StartsWith($watchListOutputRoot, [StringComparison]::OrdinalIgnoreCase) -or
                     $path.StartsWith($validationOutputRoot, [StringComparison]::OrdinalIgnoreCase)))) {
        $displayPath = $path
        if (-not $displayPath) {
            $displayPath = "(path unavailable)"
        }

        $blockingProcesses += [PSCustomObject]@{
            Id          = $process.Id
            ProcessName = $process.ProcessName
            Path        = $displayPath
        }
    }
}

if ($blockingProcesses.Count -eq 0) {
    Write-Host "ValidationPlatform build preflight passed: no WatchList/testhost build-output locks detected." -ForegroundColor Green
    exit 0
}

Write-Host "ValidationPlatform build preflight found likely build-output locks:" -ForegroundColor Yellow
$blockingProcesses | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Close the running WatchLists app or wait for the listed testhost processes to exit, then rerun the build." -ForegroundColor Yellow
Write-Host "This script does not stop processes automatically." -ForegroundColor Yellow

if ($WarnOnly) {
    exit 0
}

exit 2
