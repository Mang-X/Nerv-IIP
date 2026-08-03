# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores and runs one classified backend fast test shard
#   Writes:
#     - bin/ and obj/ build outputs under the classified test projects
#     - the supplied TRX results directory
#     - timeout stdout/stderr diagnostics in the supplied results directory
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

    [string] $TestCommand,

    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

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
    $result = if ([string]::IsNullOrWhiteSpace($TestCommand)) {
        Invoke-DotNetOutput -Name "backend-test-shard-$ShardId" -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Arguments $testArguments
    }
    else {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-Command', $TestCommand) -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Name "backend-test-shard-$ShardId-timeout-contract"
    }
}
catch {
    $timeoutStdout = $_.Exception.Data['Stdout']
    $timeoutStderr = $_.Exception.Data['Stderr']
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    if ($null -eq $timeoutStdout) { $timeoutStdout = $_.Exception.Message }
    if ($null -eq $timeoutStderr) { $timeoutStderr = '' }
    Set-Content -LiteralPath (Join-Path $ResultsDirectory "$TrxFilePrefix.timeout.stdout.log") -Value ([string] $timeoutStdout) -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $ResultsDirectory "$TrxFilePrefix.timeout.stderr.log") -Value ([string] $timeoutStderr) -Encoding utf8NoBOM
    throw
}
Write-Output $result.Stdout
if (-not [string]::IsNullOrWhiteSpace($result.Stderr)) {
    Write-Warning $result.Stderr
}
if ($result.Stdout -match 'No test matches the given testcase filter') {
    throw "Fast shard '$ShardId' contains a classified project with zero matched tests; classify its excluded tests more narrowly or move the project to an explicit heavy lane."
}
