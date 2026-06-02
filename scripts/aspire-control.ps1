# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Inspects, waits for, streams logs from, or stops the Aspire AppHost
#     - Stops orphaned Aspire usvc-dev containers for this platform when Action=stop
#   Writes:
#     - artifacts/script-logs/** for bounded Aspire helper commands
#   Cleanup:
#     - Uses Aspire CLI lifecycle commands
#     - Stops matching orphaned Aspire usvc-dev containers after stop
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

function Stop-OrphanedAspireDevContainers {
    $resourceNamePattern = '^(postgres|redis|otel-collector|minio|rabbitmq)-'

    try {
        $containers = Invoke-NativeCommandOutput `
            -Command 'docker' `
            -Arguments @(
                'ps',
                '--filter',
                'label=com.microsoft.developer.usvc-dev.group-version=usvc-dev.developer.microsoft.com/v1',
                '--format',
                '{{.ID}}|{{.Names}}'
            ) `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name 'aspire-orphan-container-list'
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not inspect Docker for orphaned Aspire containers: $($_.Exception.Message)"
        return
    }

    $containerIds = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($containers.Stdout -split '\r?\n')) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = "$line" -split '\|', 2
        if ($parts.Count -ne 2) {
            continue
        }

        $id = $parts[0]
        $name = $parts[1]
        if ($name -match $resourceNamePattern) {
            $containerIds.Add($id)
        }
    }

    if ($containerIds.Count -eq 0) {
        return
    }

    Invoke-NativeCommandWithTimeout `
        -Command 'docker' `
        -Arguments (@('stop') + @($containerIds)) `
        -WorkingDirectory $root `
        -TimeoutSeconds 120 `
        -Name 'aspire-orphan-container-stop' | Out-Null

    Write-Diagnostic "Stopped orphaned Aspire usvc-dev containers: $($containerIds -join ', ')"
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

        $result = Invoke-AspireInteractive -Arguments $arguments -WorkingDirectory $root -Name 'aspire-stop'
        Stop-OrphanedAspireDevContainers
        exit $result.ExitCode
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
