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
The third vertical-slice console verifier was a May 2026 milestone harness and is retired under #2157.

It no longer exports OpenAPI, generates the API client, or runs the historical console quality chain. Use scripts/export-gateway-openapi.ps1, pnpm -C frontend generate:api, and the focused frontend commands as needed; use the dedicated CI lanes for repository acceptance.
'@
