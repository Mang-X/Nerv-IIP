# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Applies repository EF Core migrations to explicitly allowlisted business PostgreSQL databases
#     - Delegates execution to the shared release migration executor in migrate-platform-databases.ps1
#   Writes:
#     - Selected business service migration history and schema objects
#     - bin/ and obj/ build outputs for selected Infrastructure projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - Does not delete, recreate, seed, or roll back databases
#     - Leaves successfully applied migrations intact when a later service fails
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10 and repository dotnet tools
#     - Process-scoped connection variables declared by business-release-database-migrations.json

[CmdletBinding()]
param(
    [string] $ReleaseId = "release-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))",

    [string] $CorrelationId = [Guid]::NewGuid().ToString('D'),

    [string[]] $Service = @(),

    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')
$executor = Join-Path $PSScriptRoot 'migrate-platform-databases.ps1'
$arguments = @('-Profile', 'business', '-ReleaseId', $ReleaseId, '-CorrelationId', $CorrelationId)
if ($Service.Count -gt 0) {
    $arguments += '-Service'
    $arguments += $Service
}
if ($ValidateOnly) {
    $arguments += '-ValidateOnly'
}
Invoke-PwshScript `
    -ScriptPath $executor `
    -Arguments $arguments `
    -WorkingDirectory $root `
    -TimeoutSeconds 14400 `
    -Name "business-release-migration-$ReleaseId" | Out-Null
