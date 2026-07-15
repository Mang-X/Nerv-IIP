# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs Playwright live specs against the real local stack, INCLUDING real business writes:
#       quality-execute.spec.ts consumes one shared-seed pending inspection task (POST creates an
#       inspection record and flips the task pending -> completed)
#     - Starts the business-pda vite dev server on PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT (default 5177)
#       via the Playwright webServer; if the port is already occupied the script fails by default,
#       and only reuses the existing server when -AllowServerReuse is passed (sets
#       PLAYWRIGHT_PDA_LIVE_REUSE=1; worktree ownership of that server is NOT verified)
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/apps/business-pda/test-results-live/** (Playwright traces/screenshots)
#     - frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd-HHmmss>-<shortSHA>-pda-live/** (per-run unique
#       evidence dir, never overwrites earlier runs; path overridable via -EvidenceDir)
#     - Business data in the local stack databases (inspection records / completed inspection tasks)
#   Cleanup:
#     - Playwright stops the vite webServer it started; a reused pre-existing server is left running
#     - Does NOT clean up business data written by the specs: consumed pending inspection tasks stay
#       completed, so re-running requires re-seeding pending tasks (QualitySeedService); runId data
#       namespacing + cleanup are deferred to M2
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
    # 证据归集目录；默认 frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd-HHmmss>-<shortSHA>-pda-live/
    # （每次运行唯一，避免同日多次运行互相混入/覆盖证据）
    [string] $EvidenceDir,

    # live 端口已被占用时，允许复用既有 dev server（设 PLAYWRIGHT_PDA_LIVE_REUSE=1）。
    # 风险：无法验证该 server 属于哪个 worktree——可能测到别人的代码却把本 worktree 的
    # commit SHA 写进证据。启用后会在证据 metadata 里如实记录。
    [switch] $AllowServerReuse
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

# --- 4. live 端口占用检查：5177 上若已有 dev server，无法验证其属于哪个 worktree——
#        可能测到别人的代码却把本 worktree 的 SHA 写进证据。默认拒绝，-AllowServerReuse 显式放行。
$livePort = if ([string]::IsNullOrWhiteSpace($env:PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT)) { 5177 } else { [int]$env:PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT }
$livePortOccupied = $false
$portProbe = [System.Net.Sockets.TcpClient]::new()
try {
    $livePortOccupied = $portProbe.ConnectAsync('127.0.0.1', $livePort).Wait(1000) -and $portProbe.Connected
}
catch {
    $livePortOccupied = $false
}
finally {
    $portProbe.Dispose()
}
$serverReuse = $false
if ($livePortOccupied) {
    if ($AllowServerReuse) {
        $serverReuse = $true
        $env:PLAYWRIGHT_PDA_LIVE_REUSE = '1'
        Write-Diagnostic -Level 'WARN' -Message "live 端口 $livePort 已被占用，按 -AllowServerReuse 复用既有 dev server——该 server 的 worktree 归属未验证，证据 metadata 将如实记录。"
    }
    else {
        Write-Diagnostic -Level 'ERROR' -Message "live 端口 $livePort 已被占用：可能是另一 worktree/会话的 vite dev server，复用会测错代码并产生失真证据。"
        Write-Diagnostic -Level 'ERROR' -Message "请先停掉占用该端口的进程（或改 PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT 换口），确认就是本 worktree 的 server 时可加 -AllowServerReuse 显式复用。"
        exit 1
    }
}
else {
    # 确保 Playwright webServer 不因外部残留 env 意外进入复用模式。
    $env:PLAYWRIGHT_PDA_LIVE_REUSE = '0'
}

# --- 5. 跑 live spec + 证据归集（无论成败都归集 trace/截图，失败时证据更重要）。
if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    # 每次运行唯一（时间戳 + shortSHA），不与同日其他运行混目录、不覆盖既有证据。
    $EvidenceDir = Join-Path $repoRoot 'frontend' 'DESIGN' 'roadmaps' 'assets' ("{0}-{1}-pda-live" -f (Get-Date -Format 'yyyy-MM-dd-HHmmss'), $head)
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
        # 不加 -Force：默认目录每次运行唯一，若显式 -EvidenceDir 撞既有文件则如实报错而非静默覆盖。
        Copy-Item -Path (Join-Path $testResultsDir '*') -Destination $EvidenceDir -Recurse
        $reuseNote = $serverReuse ? 'true (reused pre-existing dev server; worktree ownership NOT verified — SHA below may not match the code under test)' : 'false'
        "branch=$branch`nhead=$head`nworktree=$scriptToplevel`ndate=$(Get-Date -Format 'o')`nserverReuse=$reuseNote" |
            Set-Content -Path (Join-Path $EvidenceDir 'run-fingerprint.txt') -Encoding utf8
        Write-Diagnostic "证据已归集：$EvidenceDir（trace/截图 + commit 指纹；请按方案 §4.5 补关键请求与后端回读记录）"
    }
    else {
        Write-Diagnostic -Level 'WARN' -Message "未发现 Playwright 输出目录（$testResultsDir），无证据可归集。"
    }
}

exit $exitCode
