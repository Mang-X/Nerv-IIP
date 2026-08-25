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
param(
    [switch] $UsePostgres
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

throw @'
The third vertical-slice console verifier was a May 2026 milestone harness and is retired under #2157.

It deliberately no longer nests the retired Ops verifier or repeats OpenAPI generation, frontend dependency installation, type checking, tests, and builds as one monolithic command. Use the dedicated current commands exposed by `.codex/environments/environment.toml`, or `.\nerv.ps1 help` for current local-development and isolated full-stack entry points.

The legacy parameter is accepted only so old invocations reach this explicit retirement diagnostic; it no longer selects an execution path.
'@
