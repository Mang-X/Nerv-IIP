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
        # #3283：结果目录必须是 **run-scoped** 的。`artifacts/real-postgres-tests/**` 没有任何一处会清理
        # （全仓只有 .gitignore 忽略它），而下面读证据的口径是「目录下全部 TRX 的合并」——两件事撞在一起，
        # 上一轮留下的 Passed TRX 就会被这一轮当成自己的执行证据，于是「本轮一个用例都没跑」也能放行。
        #
        # ⚠️ 这一条不是聚合带来的新风险的全部，而是**聚合拿掉了一个偶然的保护**：改之前按 mtime 取最新
        # 那一份，虽然选哪一份不确定，但「最新」天然属于本轮（本轮必写至少一份 TRX）。换成聚合后本轮性
        # 不再是副产品，必须显式声明。失败方向也因此被摆正：清空之后「本轮没有执行证据」会红，而不是
        # 悄悄复用上一轮的绿。scripts/tests/backend-test-shards.Tests.ps1 用一份预置的旧 TRX 端到端钉住。
        #
        # ⚠️ 这条删除动作是本次新增的 blast radius，所以先把它钉在 $resultsRoot 之内再删。
        # $selectorSlug 的字符类 `[^A-Za-z0-9._-]` **保留 `.`**，于是 `.` 与 `..` 会原样通过，
        # `Join-Path $resultsRoot '..'` 解析出来就是 artifacts/ 本身。今天不可达——这样的 selector
        # 还得匹配上 test-evidence-policy.json 某条 rule 的 identity，没有真实 FQN 长那样——
        # 所以这条守卫**没有配套用例**（不可达的分支喂不进去），它声明的只是「删除范围有上界」，
        # 不声明「有人会那么写」。改之前这里没有删除动作、blast radius 为零，这是补回那个零。
        $resolvedSelectorDirectory = [IO.Path]::GetFullPath($selectorDirectory)
        $resolvedResultsRoot = [IO.Path]::GetFullPath($resultsRoot + [IO.Path]::DirectorySeparatorChar)
        if (-not $resolvedSelectorDirectory.StartsWith($resolvedResultsRoot, [StringComparison]::Ordinal)) {
            throw "Real PostgreSQL selector '$selector' resolves to '$resolvedSelectorDirectory', which escapes the results root '$resolvedResultsRoot'; refusing to clear it."
        }
        if (Test-Path -LiteralPath $resolvedSelectorDirectory) {
            Remove-Item -LiteralPath $resolvedSelectorDirectory -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $selectorDirectory | Out-Null
        Invoke-DotNetOutput -Name "backend-real-postgres-execution-$($shard.id)" -WorkingDirectory $repositoryRoot -TimeoutSeconds $TimeoutSeconds -Arguments @(
            'test', [string] $shard.solutionFilter, '--configuration', 'Release', '--filter', "FullyQualifiedName~$selector",
            '--logger', "trx;LogFilePrefix=$selectorSlug", '--results-directory', $selectorDirectory
        ) | Out-Null
        # #3283：证据是**上面这个 run-scoped 目录下全部 TRX 的合并结果**，不是其中按 mtime 最新的那一份。
        # `dotnet test <slnf>` 给 slnf 里每个项目各写一份 TRX，绝大多数是 0 结果空壳；挑哪一份取决于
        # 文件系统写入先后，两次运行会停在不同的 selector 上。归因与四支 fail-closed 写在
        # scripts/lib/BackendTestShardSelectors.ps1 里聚合读取函数的注释里，这里不复述。
        #
        # ⚠️ 这一行的正确性**不靠本注释、也不靠任何源码文本断言**守住：把它改回 pre-PR 的
        # 「mtime 取单份 + [xml] + .TestRun.Results.UnitTestResult」会让 backend-test-shards.Tests.ps1
        # 里那两条用 dotnet shim 端到端跑本脚本的用例转红（#3283 复审 B1：只比对源码子串的断言会被
        # 同文件的一句注释缴械，因此已删除）。
        $results = @(Get-BackendTestShardSelectorTrxResults -Selector $selector -ResultsDirectory $selectorDirectory)
        Assert-BackendTestShardSelectorExecution -Selector $selector -DiscoveredTests $discovered -TrxResults $results
    }
}

Write-Host "Verified $verifiedSelectorCount of $ownedSelectorCount real PostgreSQL test selectors through discovery and TRX evidence."
