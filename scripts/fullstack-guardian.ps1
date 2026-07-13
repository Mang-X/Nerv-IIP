# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Renews observation of one session and requests exact cleanup when abandoned
#   Writes:
#     - Session cleanup state and governed script logs
#   Cleanup:
#     - Exits when the exact session reaches Stopped
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SessionId,
    [Parameter(Mandatory)] [ValidateSet('Automated', 'Interactive')] [string] $Mode,
    [int] $CoordinatorPid,
    [string] $CoordinatorStartTimeUtc,
    [ValidateRange(1, 3600)] [int] $IntervalSeconds = 60,
    [string] $StateRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')
if (-not [string]::IsNullOrWhiteSpace($StateRoot)) { $env:NERV_IIP_FULLSTACK_STATE_ROOT = $StateRoot }

while ($true) {
    $manifest = Read-NervFullStackManifest -SessionId $SessionId
    if ("$($manifest.state)" -eq 'Stopped') { exit 0 }

    $leaseExpired = [DateTimeOffset] $manifest.leaseExpiresAtUtc -le [DateTimeOffset]::UtcNow
    $coordinatorMissing = $false
    if ($Mode -eq 'Automated') {
        $coordinatorMissing = $CoordinatorPid -le 0 -or
            [string]::IsNullOrWhiteSpace($CoordinatorStartTimeUtc) -or
            -not (Test-NervProcessIdentity -ProcessId $CoordinatorPid -ProcessStartTimeUtc $CoordinatorStartTimeUtc)
    }
    if ($leaseExpired -or $coordinatorMissing) {
        $env:NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID = "$PID"
        try {
            Invoke-PwshScript `
                -ScriptPath (Join-Path $repoRoot 'scripts/fullstack-session.ps1') `
                -Arguments @('stop', '-SessionId', $SessionId) `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 300 `
                -Name "fullstack-$SessionId-guardian-stop" | Out-Null
        }
        finally {
            Remove-Item Env:NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID -ErrorAction SilentlyContinue
        }
        exit 0
    }
    Start-Sleep -Seconds $IntervalSeconds
}
