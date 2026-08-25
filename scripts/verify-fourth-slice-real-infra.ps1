# Script-Governance:
#   Category: verify
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

throw @'
The fourth vertical-slice real-infrastructure verifier was a May 2026 milestone harness and is retired under #2157.

It no longer starts shared development infrastructure, recreates historical verification databases, or nests the retired console verifier. Use `.\nerv.ps1 help` for current local-development and isolated full-stack entry points, and use the dedicated real-provider CI lanes for repository acceptance.

This fail-fast compatibility tombstone remains only because the current script-governance fixture names this path explicitly. It must be deleted together with that stale fixture and the fifth-slice harness in the next cleanup batch.
'@
