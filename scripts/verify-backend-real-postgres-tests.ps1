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
#     - Docker CLI 与可访问的 Docker daemon（InventoryDirectoryPostgresTests 的自管容器路径必需）

[CmdletBinding()]
param(
    # Budget for each shard's `dotnet test` discovery/execution invocation. Exceeding it fails as a
    # timeout, not as a test failure; raise it for a local run whose CPU is shared with other
    # worktrees (#2870 / #3295). Bounds are owned by Invoke-NativeCommandOutput; 1800 is a default,
    # not a ceiling, so no ValidateRange is repeated here.
    [int] $TimeoutSeconds = 1800,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/BackendTestShardSelectors.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = Join-Path $repositoryRoot 'artifacts/real-postgres-tests'
$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
$evidencePolicy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-evidence-policy.json') -Raw | ConvertFrom-Json
$lane = @($manifest.heavyLanes | Where-Object { [string]::Equals([string]($_.id), [string]('real-postgres'), [StringComparison]::OrdinalIgnoreCase) })
if ($lane.Count -ne 1) {
    throw 'The backend test shard manifest must define exactly one real-postgres heavy lane.'
}

foreach ($variable in @('NERV_IIP_TEST_POSTGRES')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Set $variable before running the real-postgres heavy lane."
    }
}

$ownedShards = @($manifest.fastShards | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@(@($_.excludedTestLanes)), [StringComparer]::OrdinalIgnoreCase).Contains([string]('real-postgres')) })
$postgresRules = @($evidencePolicy.rules | Where-Object { [string]::Equals([string]($_.requiredLane), [string]($lane[0].policyLane), [StringComparison]::Ordinal) })
$ownedSelectorCount = @(
    Get-NervStringsSorted -Values @(
        $ownedShards |
            ForEach-Object { Get-BackendTestShardExcludedSelectors -Shard $_ } |
            Where-Object { @(Get-BackendTestShardPolicyIdentityMatches -Selector ([string] $_) -Rules $postgresRules).Count -gt 0 }
    ) -Comparer ([StringComparer]::Ordinal) -Unique
).Count
if ($ownedSelectorCount -eq 0) {
    throw 'The real-postgres heavy lane has no excluded test selectors to execute.'
}

$verifiedSelectorCount = 0
foreach ($shard in $ownedShards) {
    $methodSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $shard -Kind 'method')
    $shardSelectors = @(
        Get-BackendTestShardExcludedSelectors -Shard $shard |
            Where-Object { @(Get-BackendTestShardPolicyIdentityMatches -Selector ([string] $_) -Rules $postgresRules).Count -gt 0 }
    )
    if ($shardSelectors.Count -eq 0) {
        continue
    }

    foreach ($selector in $shardSelectors) {
        $verifiedSelectorCount++
        $discovery = Invoke-DotNetOutput -Name "backend-real-postgres-discovery-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Arguments @(
            'test', [string] $shard.solutionFilter, '--configuration', 'Release', '--list-tests', '--filter', "FullyQualifiedName~$selector"
        )
        $isMethodSelector = $methodSelectors -contains $selector
        # #3279：把原始 stdout 整体交给断言函数切行。这里刻意不在调用方保留一个「行」中间物——
        # 那正是 dotnet test 结尾换行产生的空元素撞上 Mandatory [string[]] 元素非空校验的入口。
        $discovered = @(Assert-BackendTestShardSelectorDiscovery -Selector $selector -MethodSelector $isMethodSelector -DiscoveryOutput ([string] $discovery.Stdout))

        $selectorSlug = ($selector -replace '[^A-Za-z0-9._-]', '_')
        $selectorDirectory = Join-Path $resultsRoot $selectorSlug
        New-Item -ItemType Directory -Force -Path $selectorDirectory | Out-Null
        Invoke-DotNetOutput -Name "backend-real-postgres-execution-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Arguments @(
            'test', [string] $shard.solutionFilter, '--configuration', 'Release', '--filter', "FullyQualifiedName~$selector",
            '--logger', "trx;LogFilePrefix=$selectorSlug", '--results-directory', $selectorDirectory
        ) | Out-Null
        # #3283：证据是**目录下全部 TRX 的合并结果**，不是其中按 mtime 最新的那一份。
        # `dotnet test <slnf>` 给 slnf 里每个项目各写一份 TRX，绝大多数是 0 结果空壳；挑哪一份取决于
        # 文件系统写入先后，两次运行会停在不同的 selector 上。归因与 fail-closed 的三个分支写在
        # Get-BackendTestShardSelectorTrxResults 的函数注释里，这里不复述。调用方在类型上不再持有
        # 「某一份 TRX」这个中间物，按 mtime 挑选的形状在这一层已无从表达。
        $results = @(Get-BackendTestShardSelectorTrxResults -Selector $selector -ResultsDirectory $selectorDirectory)
        Assert-BackendTestShardSelectorExecution -Selector $selector -DiscoveredTests $discovered -TrxResults $results
    }
}

Write-Host "Verified $verifiedSelectorCount of $ownedSelectorCount real PostgreSQL test selectors through discovery and TRX evidence."
