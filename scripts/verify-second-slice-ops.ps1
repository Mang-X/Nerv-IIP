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
    [switch] $UsePostgres,

    [ValidateSet('InMemory', 'RabbitMQ')]
    [string] $MessagingProvider = 'InMemory'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

throw @'
The second vertical-slice Ops verifier was a May 2026 milestone harness and is retired under #2157.

It deliberately no longer builds whole solutions, injects historical local credentials, enables AutoMigrate, starts fixed-port platform services, or mutates a Docker demo container. Use `.\nerv.ps1 help` to select a current local-development or isolated full-stack entry point, and use the current CI lanes for repository acceptance.

The legacy parameters are accepted only so old invocations reach this explicit retirement diagnostic; they no longer select an execution path.
'@
