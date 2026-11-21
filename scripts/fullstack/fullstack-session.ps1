# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Forwards an existing guardian's exact-session cleanup request
#   Writes:
#     - Session cleanup state and governed script logs
#   Cleanup:
#     - Delegates ownership checks and cleanup to the session manager
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)] [ValidateSet('stop')] [string] $Action,
    [Parameter(Mandatory)] [string] $SessionId
)

# 已运行的旧 guardian 在租约到期时仍会从此路径调用 stop -SessionId。
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'manage-fullstack-session.ps1') @PSBoundParameters
