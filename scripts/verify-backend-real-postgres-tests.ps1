# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores and runs the real PostgreSQL test classes excluded from fast backend shards
#   Writes:
#     - bin/ and obj/ build outputs under the classified test projects
#     - artifacts/script-logs/**
#     - artifacts/real-postgres-tests/** TRX evidence
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
. (Join-Path $PSScriptRoot 'lib/BackendTestShardSelectors.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = Join-Path $repositoryRoot 'artifacts/real-postgres-tests'
$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
$lane = @($manifest.heavyLanes | Where-Object { [string]::Equals([string]($_.id), [string]('real-postgres'), [StringComparison]::OrdinalIgnoreCase) })
if ($lane.Count -ne 1) {
    throw 'The backend test shard manifest must define exactly one real-postgres heavy lane.'
}

foreach ($variable in @('NERV_IIP_TEST_POSTGRES', 'NERV_IIP_TEST_REDIS')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Set $variable before running the real-postgres heavy lane."
    }
}

$ownedShards = @($manifest.fastShards | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@(@($_.excludedTestLanes)), [StringComparer]::OrdinalIgnoreCase).Contains([string]('real-postgres')) })
$ownedSelectorCount = @(Get-NervStringsSorted -Values @($ownedShards | ForEach-Object { Get-BackendTestShardExcludedSelectors -Shard $_ }) -Comparer ([StringComparer]::Ordinal) -Unique).Count
if ($ownedSelectorCount -eq 0) {
    throw 'The real-postgres heavy lane has no excluded test selectors to execute.'
}

$verifiedSelectorCount = 0
foreach ($shard in $ownedShards) {
    $methodSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $shard -Kind 'method')
    $shardSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $shard)
    if ($shardSelectors.Count -eq 0) {
        continue
    }

    foreach ($selector in $shardSelectors) {
        $verifiedSelectorCount++
        $discovery = Invoke-DotNetOutput -Name "backend-real-postgres-discovery-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds 1800 -Arguments @(
            'test', [string] $shard.solutionFilter, '--configuration', 'Release', '--list-tests', '--filter', "FullyQualifiedName~$selector"
        )
        $discovered = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() })
        $isMethodSelector = $methodSelectors -contains $selector
        $discovered = Assert-BackendTestShardSelectorDiscovery -Selector $selector -MethodSelector $isMethodSelector -DiscoveredTests $discovered

        $selectorSlug = ($selector -replace '[^A-Za-z0-9._-]', '_')
        $selectorDirectory = Join-Path $resultsRoot $selectorSlug
        New-Item -ItemType Directory -Force -Path $selectorDirectory | Out-Null
        Invoke-DotNetOutput -Name "backend-real-postgres-execution-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds 1800 -Arguments @(
            'test', [string] $shard.solutionFilter, '--configuration', 'Release', '--filter', "FullyQualifiedName~$selector",
            '--logger', "trx;LogFilePrefix=$selectorSlug", '--results-directory', $selectorDirectory
        ) | Out-Null
        $trx = Get-NervItemsSorted -Items @(Get-ChildItem -LiteralPath $selectorDirectory -Filter '*.trx' -File) -Comparison { param($left, $right) if ($right.LastWriteTimeUtc -gt $left.LastWriteTimeUtc) { 1 } elseif ($right.LastWriteTimeUtc -lt $left.LastWriteTimeUtc) { -1 } else { 0 } } | Select-Object -First 1
        if ($null -eq $trx) {
            throw "Real PostgreSQL selector '$selector' executed without TRX evidence."
        }
        [xml] $trxXml = Get-Content -LiteralPath $trx.FullName -Raw
        $results = @($trxXml.TestRun.Results.UnitTestResult)
        Assert-BackendTestShardSelectorExecution -Selector $selector -DiscoveredTests $discovered -TrxResults $results
    }
}

Write-Host "Verified $verifiedSelectorCount of $ownedSelectorCount real PostgreSQL test selectors through discovery and TRX evidence."
