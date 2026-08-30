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
The second vertical-slice operations verifier was a May 2026 milestone harness and is retired under #2157.

It no longer starts services, restarts Docker connectors, or runs the historical operations chain. Use .\nerv.ps1 help for current local-development and isolated full-stack entry points, and use the dedicated CI lanes for repository acceptance.
'@
