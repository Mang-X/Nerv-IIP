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
The fourth vertical-slice real-infrastructure verifier was a May 2026 milestone harness and is retired under #2157.

It deliberately no longer starts the shared development infrastructure, recreates historical verification databases, or nests the retired console verifier. Use `.\nerv.ps1 help` to select a current local-development or isolated full-stack entry point, and use the dedicated real-provider CI lanes for repository acceptance.

The file remains temporarily as a fail-fast tombstone so historical documentation and governance scans resolve without keeping the obsolete executable path alive.
'@
