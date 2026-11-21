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

# 已启动的旧 coordinator 仍按此文件名启动 guardian，必须在同一 PID 中接管监护。
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'watch-fullstack-session.ps1') @PSBoundParameters
