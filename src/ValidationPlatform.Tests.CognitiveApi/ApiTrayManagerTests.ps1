param([string]$Scenario)
$ErrorActionPreference = 'Stop'
Import-Module 'C:\Users\benho\source\Application Documentation\CpApiTray\CpApiControl.psm1' -Force
$module = Get-Module CpApiControl
& $module {
    param($Scenario)
    function Assert-Equal($actual, $expected) {
        if ($actual -ne $expected) { throw "Expected '$expected', got '$actual'" }
    }
    $target = Get-CpApiTarget QA
    $script:fakeProcesses = @()
    $script:fakePorts = @()
    $script:fakeResponding = $false
    $script:started = 0
    $script:stopped = @()
    function Get-CpApiProcesses { param($Target) return $script:fakeProcesses }
    function Get-CpApiListeners { param($Target) return $script:fakePorts }
    function Test-CpApiResponse { param($Target) return $script:fakeResponding }
    function Start-CpApiExecutable { param($Target) $script:started++; throw 'simulated launch failure' }
    function Stop-CpApiOwnedProcess { param($Process, $Target) $script:stopped += $Process.Id }
    function Test-Path { param($LiteralPath) return $true }
    $owned = [pscustomobject]@{ Id = 123; Path = $target.Executable; StartedUtc = '2026-10-08T00:00:00Z' }
    switch ($Scenario) {
        EnvironmentMapping {
            Assert-Equal (Get-CpApiTarget Dev).Port 5273
            Assert-Equal (Get-CpApiTarget QA).AspNetEnvironment 'QA'
            Assert-Equal (Get-CpApiTarget Prod).Executable 'C:\CP\Deploy\API\Prod\CognitivePlatform.Api.exe'
            try { Get-CpApiTarget Unknown; throw 'unknown accepted' } catch { if ($_.Exception.Message -eq 'unknown accepted') { throw } }
        }
        Stopped { Assert-Equal (Get-CpApiStatus $target).State 'Stopped' }
        Responding {
            $script:fakeProcesses = @($owned)
            $script:fakePorts = @(123)
            $script:fakeResponding = $true
            Assert-Equal (Get-CpApiStatus $target).State 'Running'
        }
        Unresponsive {
            $script:fakeProcesses = @($owned)
            $script:fakePorts = @(123)
            Assert-Equal (Get-CpApiStatus $target).State 'Unresponsive'
        }
        PortConflict {
            $script:fakePorts = @(456)
            Assert-Equal (Get-CpApiStatus $target).State 'Conflict'
            try { Start-CpApi $target; throw 'conflict accepted' } catch { if ($_.Exception.Message -eq 'conflict accepted') { throw } }
            Assert-Equal $script:started 0
        }
        DuplicateStart {
            $script:fakeProcesses = @($owned)
            Start-CpApi $target | Out-Null
            Assert-Equal $script:started 0
        }
        StopOnlyOwnedProcess {
            $script:fakeProcesses = @($owned)
            $script:fakePorts = @(456)
            try { Stop-CpApi $target; throw 'conflict stopped' } catch { if ($_.Exception.Message -eq 'conflict stopped') { throw } }
            Assert-Equal $script:stopped.Count 0
            $script:fakePorts = @(123)
            Stop-CpApi $target | Out-Null
            Assert-Equal $script:stopped.Count 1
            Assert-Equal $script:stopped[0] 123
        }
        FailedStartup {
            try { Start-CpApi $target; throw 'failure swallowed' } catch { if ($_.Exception.Message -eq 'failure swallowed') { throw } }
            Assert-Equal $script:started 1
        }
        NativeProcessIdentity {
            $temporaryProcess = [Diagnostics.Process]::Start((New-Object Diagnostics.ProcessStartInfo -Property @{
                FileName = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
                Arguments = '-NoProfile -Command "Start-Sleep -Seconds 60"'
                UseShellExecute = $false
                CreateNoWindow = $true
            }))
            try {
                $identity = [CpApiNative]::Identity($temporaryProcess.Id)
                try { [CpApiNative]::Stop($temporaryProcess.Id, 'C:\wrong\CognitivePlatform.Api.exe', $identity[1]); throw 'wrong path stopped' }
                catch { if ($_.Exception.Message -eq 'wrong path stopped') { throw } }
                Assert-Equal $temporaryProcess.HasExited $false
                try { [CpApiNative]::Stop($temporaryProcess.Id, $identity[0], 'wrong creation time'); throw 'wrong identity stopped' }
                catch { if ($_.Exception.Message -eq 'wrong identity stopped') { throw } }
                Assert-Equal $temporaryProcess.HasExited $false
                [CpApiNative]::Stop($temporaryProcess.Id, $identity[0], $identity[1])
                Assert-Equal $temporaryProcess.WaitForExit(5000) $true
            } finally {
                if (-not $temporaryProcess.HasExited) { $temporaryProcess.Kill() }
                $temporaryProcess.Dispose()
            }
        }
    }
    Write-Output "PASS $Scenario"
} $Scenario
