# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores and runs one classified backend fast test shard
#   Writes:
#     - bin/ and obj/ build outputs under the classified test projects
#     - the supplied job-local raw TRX results directory, which is never uploaded
#     - redacted buffered stdout/stderr diagnostics to the caller's log stream only
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('business-gateway', 'platform', 'business-core-a', 'business-core-b')]
    [string] $ShardId,

    [Parameter(Mandatory)]
    [string] $ResultsDirectory,

    [Parameter(Mandatory)]
    [string] $TrxFilePrefix,

    [ValidateRange(1, 1800)]
    [int] $TimeoutSeconds = 1800,

    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/BackendTestShardDiagnostics.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
$shard = @($manifest.fastShards | Where-Object { $_.id -eq $ShardId })
if ($shard.Count -ne 1) {
    throw "Backend test shard '$ShardId' must be defined exactly once in $ManifestPath."
}

$excludedClassesProperty = $shard[0].PSObject.Properties['excludedTestClasses']
$excludedTestsProperty = $shard[0].PSObject.Properties['excludedTests']
$excludedClasses = if ($null -eq $excludedClassesProperty) { @() } else { @($excludedClassesProperty.Value) }
$excludedMethods = if ($null -eq $excludedTestsProperty) { @() } else { @($excludedTestsProperty.Value) }
$excludedTests = @(
    $excludedClasses + $excludedMethods |
        Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string] $_) } |
        ForEach-Object { [string] $_ } |
        Sort-Object -Unique
)
$filterClauses = @($excludedTests | ForEach-Object { "FullyQualifiedName!~$_" })
$testArguments = @(
    'test',
    [string] $shard[0].solutionFilter,
    '--configuration', 'Release',
    '--logger', "trx;LogFilePrefix=$TrxFilePrefix",
    '--results-directory', $ResultsDirectory
)

if ($filterClauses.Count -gt 0) {
    $testArguments += @('--filter', ($filterClauses -join '&'))
}

try {
    $result = Invoke-DotNetOutput -Name "backend-test-shard-$ShardId" -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Arguments $testArguments
}
catch {
    Write-Host (Get-BackendTestShardFailureDiagnostics -ErrorRecord $_ -TrxFilePrefix $TrxFilePrefix)
    throw
}
Write-Output $result.Stdout
if (-not [string]::IsNullOrWhiteSpace($result.Stderr)) {
    Write-Warning $result.Stderr
}
if ($result.Stdout -match 'No test matches the given testcase filter') {
    throw "Fast shard '$ShardId' contains a classified project with zero matched tests; classify its excluded tests more narrowly or move the project to an explicit heavy lane."
}
