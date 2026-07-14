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
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')
if (-not [string]::IsNullOrWhiteSpace($StateRoot)) { $env:NERV_IIP_FULLSTACK_STATE_ROOT = $StateRoot }

Invoke-NervFullStackGuardian `
    -SessionId $SessionId `
    -Mode $Mode `
    -CoordinatorPid $CoordinatorPid `
    -CoordinatorStartTimeUtc $CoordinatorStartTimeUtc `
    -IntervalSeconds $IntervalSeconds | Out-Null
