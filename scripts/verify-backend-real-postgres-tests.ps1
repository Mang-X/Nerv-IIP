# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores and runs the real PostgreSQL test classes excluded from fast backend shards
#   Writes:
#     - bin/ and obj/ build outputs under the classified test projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL reachable from NERV_IIP_TEST_POSTGRES
#     - Redis reachable from NERV_IIP_TEST_REDIS for CAP-backed cases

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

function Get-OptionalObjectArrayProperty {
    param([Parameter(Mandatory)] [object] $Object, [Parameter(Mandatory)] [string] $PropertyName)

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return @()
    }

    return @($property.Value)
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
$lane = @($manifest.heavyLanes | Where-Object { $_.id -eq 'real-postgres' })
if ($lane.Count -ne 1) {
    throw 'The backend test shard manifest must define exactly one real-postgres heavy lane.'
}

foreach ($variable in @('NERV_IIP_TEST_POSTGRES', 'NERV_IIP_TEST_REDIS')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Set $variable before running the real-postgres heavy lane."
    }
}

$classes = @(
    $manifest.fastShards |
        Where-Object { $_.excludedTestLane -eq 'real-postgres' } |
        ForEach-Object {
            (Get-OptionalObjectArrayProperty -Object $_ -PropertyName 'excludedTestClasses') +
                (Get-OptionalObjectArrayProperty -Object $_ -PropertyName 'excludedTests')
        } |
        Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string] $_) } |
        ForEach-Object { [string] $_ } |
        Sort-Object -Unique
)
if ($classes.Count -eq 0) {
    throw 'The real-postgres heavy lane has no excluded test classes to execute.'
}

foreach ($shard in @($manifest.fastShards | Where-Object { $_.excludedTestLane -eq 'real-postgres' })) {
    $shardTests = @(
        (Get-OptionalObjectArrayProperty -Object $shard -PropertyName 'excludedTestClasses') +
            (Get-OptionalObjectArrayProperty -Object $shard -PropertyName 'excludedTests') |
            Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string] $_) } |
            ForEach-Object { [string] $_ } |
            Sort-Object -Unique
    )
    if ($shardTests.Count -eq 0) {
        continue
    }

    $filter = ($shardTests | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    Invoke-DotNet -Name "backend-real-postgres-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds 1800 -Arguments @(
        'test',
        [string] $shard.solutionFilter,
        '--configuration', 'Release',
        '--filter', $filter
    )
}

Write-Host "Verified $($classes.Count) real PostgreSQL test classes through the explicit heavy lane."
