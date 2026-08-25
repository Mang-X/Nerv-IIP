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

throw @'
The first vertical-slice verifier was a May 2026 milestone harness and is retired under #2157.

It deliberately no longer restores whole solutions, starts fixed-port services, or replays the historical connector flow. Use `.\nerv.ps1 help` to select a current local-development or isolated full-stack entry point, and use the current CI lanes for repository acceptance.

The file remains temporarily as a fail-fast tombstone so historical documentation resolves without keeping the obsolete executable path alive.
'@
