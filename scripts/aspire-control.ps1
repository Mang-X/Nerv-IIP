# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Inspects, waits for, streams logs from, or stops the Aspire AppHost
#     - Warns about Aspire usvc-dev containers whose exact session ownership is unavailable
#   Writes:
#     - artifacts/script-logs/** for bounded Aspire helper commands
#   Cleanup:
#     - Uses Aspire CLI lifecycle commands
#     - Stops matching AppHost processes after stop; session-owned Docker cleanup is coordinator-managed
#   Requires:
#     - PowerShell 7
#     - Aspire CLI 13.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('stop', 'status', 'describe', 'logs', 'wait')]
    [string] $Action,

    [Parameter(Position = 0)]
    [string] $Resource,

    [ValidateSet('healthy', 'up', 'down')]
    [string] $Status = 'healthy',

    [int] $TimeoutSeconds = 120,

    [int] $Tail = 120,

    [switch] $Follow,

    [switch] $IncludeHidden,

    [switch] $All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

Set-Location $root

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
Get-AspireCliCommand | Out-Null

function Show-UnownedAspireDevContainersWarning {
    try {
        $allContainers = Invoke-NativeCommandOutput `
            -Command 'docker' `
            -Arguments @(
                'ps',
                '-a',
                '--filter',
                'label=com.microsoft.developer.usvc-dev.group-version=usvc-dev.developer.microsoft.com/v1',
                '--format',
                '{{.ID}}|{{.Names}}'
            ) `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name 'aspire-unowned-container-list'
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not inspect Docker for remaining Aspire containers: $($_.Exception.Message)"
        return
    }

    $remaining = @($allContainers.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($remaining.Count -eq 0) {
        return
    }

    Write-Diagnostic -Level 'WARN' -Message "Aspire usvc-dev containers remain, but generic stop cannot prove exact session ownership and will not remove them: $($remaining -join ', ')"
}

function Stop-ProjectProcessesForCurrentRepo {
    if (-not $IsWindows) {
        return
    }

    $repoPath = ((Resolve-Path $root).Path).ToLowerInvariant()
    $projectPath = ((Resolve-Path $appHostProject).Path).ToLowerInvariant()
    $currentPid = [int] $PID
    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        if ($null -eq $_.CommandLine) {
            return $false
        }

        $commandLine = $_.CommandLine.ToLowerInvariant()
        $processId = [int] $_.ProcessId
        if ($processId -eq $currentPid) {
            return $false
        }

        $processName = "$($_.Name)".ToLowerInvariant()
        return (
            $commandLine.Contains($projectPath) -or
            ($commandLine.Contains('nerv.iip.apphost') -and $commandLine.Contains($repoPath)) -or
            ($commandLine.Contains('aspire') -and $commandLine.Contains('start') -and $commandLine.Contains($repoPath)) -or
            ($commandLine.Contains($repoPath) -and @('dotnet.exe', 'dotnet', 'node.exe', 'node', 'pnpm.exe', 'pnpm').Contains($processName))
        )
    }

    foreach ($process in $processes) {
        $processId = [int] $process.ProcessId
        Write-Diagnostic -Level 'WARN' -Message "Stopping current-repo process $processId after Aspire stop cleanup."
        Stop-ProcessTree -ProcessId $processId -Reason 'Aspire stop fallback for current repository' | Out-Null
    }
}

switch ($Action) {
    'stop' {
        $arguments = @('stop', '--non-interactive', '--nologo')
        if ($All) {
            $arguments += '--all'
        }
        else {
            $arguments += @('--apphost', $appHostProject)
        }

        $stopFailed = $false
        try {
            Invoke-Aspire -Arguments $arguments -WorkingDirectory $root -TimeoutSeconds 90 -Name 'aspire-stop' | Out-Null
            Write-Diagnostic 'Aspire stop command completed.'
        }
        catch {
            $stopFailed = $true
            Write-Diagnostic -Level 'WARN' -Message "Aspire stop did not complete cleanly within the bounded path: $($_.Exception.Message)"
        }
        finally {
            Stop-ProjectProcessesForCurrentRepo
            Show-UnownedAspireDevContainersWarning
        }

        if ($stopFailed) {
            Write-Diagnostic -Level 'WARN' -Message 'Fallback stop cleanup completed. If other unrelated Aspire AppHosts are running, inspect them with `aspire ps`.'
        }

        exit 0
    }
    'status' {
        $result = Invoke-AspireInteractive -Arguments @('ps', '--non-interactive', '--nologo') -WorkingDirectory $root -Name 'aspire-status'
        exit $result.ExitCode
    }
    'describe' {
        $arguments = @('describe')
        if (-not [string]::IsNullOrWhiteSpace($Resource)) {
            $arguments += $Resource
        }
        $arguments += @('--apphost', $appHostProject, '--non-interactive', '--nologo')
        if ($IncludeHidden) {
            $arguments += '--include-hidden'
        }

        $result = Invoke-AspireInteractive -Arguments $arguments -WorkingDirectory $root -Name 'aspire-describe'
        exit $result.ExitCode
    }
    'logs' {
        $arguments = @('logs')
        if (-not [string]::IsNullOrWhiteSpace($Resource)) {
            $arguments += $Resource
        }
        $arguments += @('--tail', "$Tail", '--apphost', $appHostProject, '--non-interactive', '--nologo')
        if ($Follow) {
            $arguments += '--follow'
        }
        if ($IncludeHidden) {
            $arguments += '--include-hidden'
        }

        $result = Invoke-AspireInteractive -Arguments $arguments -WorkingDirectory $root -Name 'aspire-logs'
        exit $result.ExitCode
    }
    'wait' {
        if ([string]::IsNullOrWhiteSpace($Resource)) {
            throw 'Resource is required for wait. Example: .\nerv.ps1 wait gateway -Status up'
        }

        Invoke-Aspire -Arguments @('wait', $Resource, '--status', $Status, '--timeout', "$TimeoutSeconds", '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds ($TimeoutSeconds + 20) -Name "aspire-wait-$Resource" | Out-Null
        exit 0
    }
}
