# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts or stops one exact isolated full-stack session
#     - Enables opt-in leader-demo prerequisite seeds for that session
#   Writes:
#     - Machine-local leader-demo current-session pointer
#     - Session-owned full-stack manifests and artifacts
#   Cleanup:
#     - Stop and reset clean only the validated recorded session ID
#   Requires:
#     - PowerShell 7
#     - Aspire CLI 13.4.x
#     - Docker

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'reset', 'seed', 'health-check', 'stop', 'help')]
    [string] $Action = 'help',

    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Write-NervLeaderDemoHelp {
    Write-Host @'
Nerv-IIP leader-demo environment

Usage:
  .\nerv.ps1 demo start
  .\nerv.ps1 demo reset
  .\nerv.ps1 demo seed
  .\nerv.ps1 demo health-check
  .\nerv.ps1 demo stop

Set NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD in the current process before start,
reset, seed, or health-check. The password is never accepted as a command-line argument.
'@
}

if ($MyInvocation.InvocationName -eq '.') { return }

try {
    if ($Action -eq 'help') {
        Write-NervLeaderDemoHelp
    }
    else {
        Invoke-NervLeaderDemoCommand -Action $Action -NoBuild:$NoBuild | Write-Output
    }
    exit 0
}
catch {
    Write-Error (Protect-ScriptAutomationText -Text "$($_.Exception.Message)")
    exit 1
}
