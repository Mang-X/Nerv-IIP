# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs Playwright live specs against the real local stack (read-only business flows)
#     - Starts (or reuses) the business-pda vite dev server on PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT (default 5177) via the Playwright webServer
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/apps/business-pda/test-results-live/** (Playwright traces/screenshots)
#     - frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd>-pda-live/** (collected evidence, path overridable via -EvidenceDir)
#   Cleanup:
#     - Playwright stops the vite webServer it started; a pre-existing dev server on the live port is reused and left running
#     - Does not start or stop the backend stack (expects an already-running nerv.ps1 dev stack)
#   Requires:
#     - PowerShell 7
#     - Node.js + pnpm (frontend workspace installed)
#     - A running full local stack (nerv.ps1 dev): BusinessGateway 5119 + PlatformGateway 5100
#     - NERV_IIP_LIVE_USER / NERV_IIP_LIVE_PASSWORD environment variables

# PDA L2 真实栈仿真走查入口（方案文档 frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §4.5 / §8 M1b）：
# worktree 归属检查 → 栈可达性检查 → pnpm e2e:live → 证据归集。
# 本脚本不起后端栈（PR#917 并行隔离方案落地后迁移到隔离入口）；不进 CI。

[CmdletBinding()]
param(
    # 证据归集目录；默认 frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd>-pda-live/
    [string] $EvidenceDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $repoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

$appDir = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# --- 1. worktree 归属检查：脚本所在 worktree 必须与当前 git worktree 一致，避免误测并行会话的栈/前端。
$scriptToplevel = (git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
$currentToplevel = (git -C (Get-Location).Path rev-parse --show-toplevel 2>$null)
if (-not $scriptToplevel) {
    Write-Diagnostic -Level 'ERROR' -Message "无法解析脚本所在 git worktree（$PSScriptRoot）。"
    exit 1
}
if ($currentToplevel -and (($scriptToplevel -replace '\\', '/') -ne ($currentToplevel -replace '\\', '/'))) {
    Write-Diagnostic -Level 'ERROR' -Message "worktree 归属不一致：脚本属 '$scriptToplevel'，当前目录属 '$currentToplevel'。请在脚本所在 worktree 内运行，避免误测并行会话的栈。"
    exit 1
}
$branch = (git -C $PSScriptRoot rev-parse --abbrev-ref HEAD)
$head = (git -C $PSScriptRoot rev-parse --short HEAD)
Write-Diagnostic "worktree 归属检查通过：$scriptToplevel（$branch @ $head）"

# --- 2. 栈可达性检查：两个网关的匿名 GET /health（代码事实：BusinessGateway/PlatformGateway HealthEndpoint.cs）。
$gatewayTargets = @(
    @{ Name = 'BusinessGateway'; Url = "$([string]::IsNullOrWhiteSpace($env:NERV_IIP_BUSINESS_GATEWAY_URL) ? 'http://127.0.0.1:5119' : $env:NERV_IIP_BUSINESS_GATEWAY_URL)/health" },
    @{ Name = 'PlatformGateway'; Url = "$([string]::IsNullOrWhiteSpace($env:NERV_IIP_PLATFORM_GATEWAY_URL) ? 'http://127.0.0.1:5100' : $env:NERV_IIP_PLATFORM_GATEWAY_URL)/health" }
)
foreach ($target in $gatewayTargets) {
    try {
        $response = Invoke-WebRequest -Uri $target.Url -TimeoutSec 5 -UseBasicParsing
        Write-Diagnostic "$($target.Name) 可达：$($target.Url) → $($response.StatusCode) $($response.Content)"
    }
    catch {
        Write-Diagnostic -Level 'ERROR' -Message "环境阻塞：真实栈不可用——$($target.Name) 健康探测失败（$($target.Url)）：$($_.Exception.Message)"
        Write-Diagnostic -Level 'ERROR' -Message '请先在仓库根目录运行 .\nerv.ps1 dev 拉起完整栈（Docker 先开），再重试本脚本。live 走查不降级、不假绿。'
        exit 1
    }
}

# --- 3. 凭据前置检查（spec 内也会校验；这里提前失败给出指引）。
if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_LIVE_USER) -or [string]::IsNullOrWhiteSpace($env:NERV_IIP_LIVE_PASSWORD)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 live 登录凭据：请设置环境变量 NERV_IIP_LIVE_USER 与 NERV_IIP_LIVE_PASSWORD（本地栈 IAM admin 凭据，勿硬编码入仓）。'
    exit 1
}

# --- 4. 跑 live spec + 证据归集（无论成败都归集 trace/截图，失败时证据更重要）。
if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    $EvidenceDir = Join-Path $repoRoot 'frontend' 'DESIGN' 'roadmaps' 'assets' ("{0}-pda-live" -f (Get-Date -Format 'yyyy-MM-dd'))
}
$testResultsDir = Join-Path $appDir 'test-results-live'
$exitCode = 0
try {
    Invoke-Pnpm -Arguments @('run', 'e2e:live') -WorkingDirectory $appDir -TimeoutSeconds 1800 -Name 'pda-e2e-live'
}
catch {
    Write-Diagnostic -Level 'ERROR' -Message "live 走查失败：$($_.Exception.Message)"
    $exitCode = 1
}
finally {
    if (Test-Path $testResultsDir) {
        New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
        Copy-Item -Path (Join-Path $testResultsDir '*') -Destination $EvidenceDir -Recurse -Force
        "branch=$branch`nhead=$head`nworktree=$scriptToplevel`ndate=$(Get-Date -Format 'o')" |
            Set-Content -Path (Join-Path $EvidenceDir 'run-fingerprint.txt') -Encoding utf8
        Write-Diagnostic "证据已归集：$EvidenceDir（trace/截图 + commit 指纹；请按方案 §4.5 补关键请求与后端回读记录）"
    }
    else {
        Write-Diagnostic -Level 'WARN' -Message "未发现 Playwright 输出目录（$testResultsDir），无证据可归集。"
    }
}

exit $exitCode
