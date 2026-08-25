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

This fail-fast compatibility tombstone remains as the explicit path-preserving boundary for the first cleanup batch. The governance test checks its declaration; a later cleanup batch may remove this path together with the fifth-slice harness and its dedicated fixture.
'@
