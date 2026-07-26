# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs the world-history seed engine's own consistency validators against real PostgreSQL
#     - Creates and drops throwaway databases on the configured PostgreSQL instance
#   Writes:
#     - JSON and Markdown evidence under artifacts/world-history
#   Cleanup:
#     - Runs in the foreground and creates no background process
#     - Throwaway databases are dropped by the test harness on success or handled failure
#   Requires:
#     - PowerShell 7
#     - .NET 10 SDK
#     - A reachable PostgreSQL instance (default: the infra/docker-compose.dev.yml dev instance)

<#
.SYNOPSIS
《工厂世界观设定集》L1 背景历史引擎的一致性校验证据脚本。

.DESCRIPTION
引擎自带的一致性校验器（ERP 侧 `WorldHistoryConsistencyValidator` 与 MES 侧同名类型）是
**fail-closed** 的：seed 结束前必跑，任何一条对账不成立就抛异常让 seed 失败。本脚本通过
NERV_IIP_TEST_POSTGRES 门控的真机测试跑一遍全量生成 + 校验，并把耗时、单据量与 20 单抽样
全链引用落成可归档证据。

覆盖的对账（设定集 §7 末尾）：
- 订单 → 发货 → 应收 → 凭证 → 收款 的数量与金额链；
- 工单 → 工序任务 → 报工 → 完工入库 的数量链；
- 已收款订单必有凭证且金额平；
- 全链时间戳落在 [2026-01-05, 今天] 且单调；
- 状态分布落在设定集比例的抽样容差内。

.PARAMETER Scale
生成缩放比例。1.0 = 全量（约 3200 单 / 3600 工单），0.1 = 约十分之一的快速验证。

.PARAMETER PostgresConnectionString
PostgreSQL 连接串。缺省读环境变量 NERV_IIP_TEST_POSTGRES，再缺省用本地 dev compose 实例。

.EXAMPLE
scripts/verify-world-history.ps1

.EXAMPLE
scripts/verify-world-history.ps1 -Scale 0.1
#>

[CmdletBinding()]
param(
    [ValidateRange(0.01, 1.0)]
    [double] $Scale = 1.0,

    [string] $PostgresConnectionString,

    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')]
    [string] $RunId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

if (-not $RunId) {
    $RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
}

if (-not $PostgresConnectionString) {
    $PostgresConnectionString = $env:NERV_IIP_TEST_POSTGRES
}

if (-not $PostgresConnectionString) {
    $PostgresConnectionString = 'Host=localhost;Port=15432;Username=nerv;Password=nerv;Database=nerv_iip'
    Write-Host "No connection string supplied; falling back to the local dev PostgreSQL instance." -ForegroundColor Yellow
}

$artifactRoot = Join-Path $repoRoot "artifacts/world-history/$RunId"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$env:NERV_IIP_TEST_POSTGRES = $PostgresConnectionString

$targets = @(
    [ordered]@{
        Name    = 'erp'
        Project = 'backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistorySeedPostgresTests'
        Prefix  = 'erp-world-history'
    },
    [ordered]@{
        Name    = 'mes'
        Project = 'backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistorySeedPostgresTests'
        Prefix  = 'mes-world-history'
    }
)

$summary = [ordered]@{
    runId                 = $RunId
    startedAtUtc          = (Get-Date).ToUniversalTime().ToString('o')
    scale                 = $Scale
    goLiveDate            = '2026-01-05'
    services              = [ordered]@{}
    consistencyValidator  = 'fail-closed: seed throws WorldHistoryConsistencyException on any unbalanced chain'
}

$failed = $false

foreach ($target in $targets) {
    Write-Host "Running $($target.Name) world-history consistency proof..." -ForegroundColor Cyan
    $logPath = Join-Path $artifactRoot "$($target.Name)-consistency.log"

    $arguments = @(
        'test'
        (Join-Path $repoRoot $target.Project)
        '--filter'
        $target.Filter
        '--logger'
        'console;verbosity=detailed'
        '--nologo'
    )

    # Invoke-DotNetOutput 在非零退出码时抛异常，异常消息里带完整输出；这里按证据脚本的
    # 「记录失败而不中断另一侧」语义接住它，最后再统一决定退出码。
    $succeeded = $true
    $exitCode = 0
    $stdout = ''
    try {
        $result = Invoke-DotNetOutput -Arguments $arguments -WorkingDirectory $repoRoot -TimeoutSeconds 1800
        $stdout = "$($result.Stdout)`n$($result.Stderr)"
        $exitCode = $result.ExitCode
    }
    catch {
        $succeeded = $false
        $stdout = "$($_.Exception.Message)"
        $exitCode = if ($_.Exception.Data['ExitCode']) { [int] $_.Exception.Data['ExitCode'] } else { 1 }
    }

    $stdout | Set-Content -Path $logPath -Encoding utf8

    $metrics = [ordered]@{}
    $samples = @()
    foreach ($line in ($stdout -split "`r?`n")) {
        $trimmed = "$line".Trim()
        if ($trimmed -match "^$($target.Prefix)-sample:\s*(.+)$") {
            $samples += $Matches[1]
            continue
        }
        if ($trimmed -match "^$($target.Prefix)-([a-z0-9-]+)=(.+)$") {
            $metrics[$Matches[1]] = $Matches[2]
        }
    }

    if (-not $succeeded) { $failed = $true }

    $summary.services[$target.Name] = [ordered]@{
        succeeded = $succeeded
        exitCode  = $exitCode
        metrics   = $metrics
        sample    = $samples
        logFile   = "$($target.Name)-consistency.log"
    }

    if ($succeeded) {
        Write-Host "  $($target.Name): consistency validator passed ($($samples.Count) sampled chains)." -ForegroundColor Green
    }
    else {
        Write-Host "  $($target.Name): FAILED (exit $exitCode). See $logPath" -ForegroundColor Red
    }
}

$summary.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
$summary.succeeded = -not $failed

$jsonPath = Join-Path $artifactRoot 'world-history-consistency.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine('# L1 背景历史一致性校验证据')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Run: ``$RunId``")
[void]$markdown.AppendLine("- Scale: ``$Scale``")
[void]$markdown.AppendLine("- 结论: " + $(if ($failed) { '**失败**' } else { '**通过**' }))
[void]$markdown.AppendLine()

foreach ($name in $summary.services.Keys) {
    $service = $summary.services[$name]
    [void]$markdown.AppendLine("## $($name.ToUpperInvariant())")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| 指标 | 数值 |')
    [void]$markdown.AppendLine('| --- | --- |')
    foreach ($key in $service.metrics.Keys) {
        [void]$markdown.AppendLine("| $key | $($service.metrics[$key]) |")
    }
    [void]$markdown.AppendLine()
    if ($service.sample.Count -gt 0) {
        [void]$markdown.AppendLine('### 抽样全链引用（人工可追）')
        [void]$markdown.AppendLine()
        [void]$markdown.AppendLine('```text')
        foreach ($line in $service.sample) {
            [void]$markdown.AppendLine($line)
        }
        [void]$markdown.AppendLine('```')
        [void]$markdown.AppendLine()
    }
}

$markdownPath = Join-Path $artifactRoot 'world-history-consistency.md'
$markdown.ToString() | Set-Content -Path $markdownPath -Encoding utf8

Write-Host ''
Write-Host "Evidence written to $artifactRoot" -ForegroundColor Cyan

if ($failed) {
    throw 'World-history consistency verification failed. Inspect the per-service logs under the artifact directory.'
}

Write-Host 'World-history consistency verification passed.' -ForegroundColor Green
