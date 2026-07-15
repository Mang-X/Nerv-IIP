# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs Playwright live specs against the real local stack, INCLUDING real business writes:
#       quality-execute.spec.ts consumes one shared-seed pending inspection task (POST creates an
#       inspection record and flips the task pending -> completed)
#     - Starts the business-pda vite dev server on PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT (default 5177)
#       via the Playwright webServer; if the port is already occupied the script fails by default,
#       and only reuses the existing server when -AllowServerReuse is passed (sets
#       PLAYWRIGHT_PDA_LIVE_REUSE=1) AND the listener process command line proves it belongs to
#       this worktree; otherwise the script errors out instead of reusing
#     - When starting its own server (default path) and VITE_NERV_IIP_REQUEST_TIMEOUT_MS is not
#       already set, sets it to 2000 in this process so the spawned vite webServer bakes the
#       DEV-only short request-timeout override required by network-resilience.spec.ts; the
#       effective value is logged and recorded in run-fingerprint.txt. In -AllowServerReuse mode
#       the env cannot be proven baked into the reused server, so the script WARNS instead
#       (timeout-injection scenarios may honestly fail as environment blockage)
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/apps/business-pda/test-results-live/** (Playwright traces/screenshots)
#     - frontend/DESIGN/roadmaps/assets/<yyyy-MM-dd-HHmmss-fff>-<shortSHA>-pda-live-<rand4hex>/**
#       (per-run unique evidence dir; created upfront as an atomic placeholder via New-Item without
#       -Force so a concurrent run claiming the same name fails instead of mixing evidence; the
#       script errors out if the target dir already exists; path overridable via -EvidenceDir)
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
    # 证据归集目录；默认 frontend/DESIGN/roadmaps/assets/
    # <yyyy-MM-dd-HHmmss-fff>-<shortSHA>-pda-live-<rand4hex>/（毫秒级时间戳 + shortSHA +
    # 4 位随机十六进制后缀，每次运行唯一；无论默认还是显式指定，唯一性检查通过后立即
    # 原子占位创建目录——已存在（含空目录）一律报错退出，避免混入/覆盖既有证据）
    [string] $EvidenceDir,

    # live 端口已被占用时，允许复用既有 dev server（设 PLAYWRIGHT_PDA_LIVE_REUSE=1）。
    # 复用前会验证监听进程命令行属于本 worktree：不属于（或取不到命令行）一律报错退出——
    # 否则可能测到并行会话的代码却把本 worktree 的 commit SHA 写进证据。
    # 验证通过时在证据 metadata 里如实记录「复用已验证归属的既有 server」。
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
        # 复用前验证归属：监听进程的命令行必须包含本 worktree 根路径，否则判定为并行会话的
        # server（测到别人的代码却把本 worktree 的 SHA 写进证据 = 证据失真），一律报错退出。
        $ownerCommandLine = $null
        try {
            $listener = Get-NetTCPConnection -LocalPort $livePort -State Listen -ErrorAction Stop | Select-Object -First 1
            $ownerProcess = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" -ErrorAction Stop
            $ownerCommandLine = $ownerProcess.CommandLine
        }
        catch {
            Write-Diagnostic -Level 'WARN' -Message "读取 live 端口 $livePort 监听进程信息失败：$($_.Exception.Message)"
        }
        if ([string]::IsNullOrWhiteSpace($ownerCommandLine)) {
            Write-Diagnostic -Level 'ERROR' -Message "无法读取 live 端口 $livePort 监听进程的 CommandLine（权限不足或进程已退出），无法验证该 dev server 的 worktree 归属。"
            Write-Diagnostic -Level 'ERROR' -Message '归属不可验证时不允许复用：请停掉占用进程（或改 PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT 换口）后不带 -AllowServerReuse 重跑。'
            exit 1
        }
        $normalizedCommandLine = ($ownerCommandLine -replace '\\', '/').ToLowerInvariant()
        $normalizedToplevel = ($scriptToplevel -replace '\\', '/').ToLowerInvariant()
        # 带路径边界匹配：worktree 根路径之后必须紧跟 /、引号、空白或字符串结尾，
        # 防止 'Nerv-IIP' 被 'Nerv-IIP-old' 之类的前缀碰撞误判为本 worktree。
        $toplevelBoundaryPattern = [regex]::Escape($normalizedToplevel) + '([/"''\s]|$)'
        if ($normalizedCommandLine -notmatch $toplevelBoundaryPattern) {
            Write-Diagnostic -Level 'ERROR' -Message "live 端口 $livePort 上的 dev server 不属于本 worktree（$scriptToplevel）：监听进程命令行为 '$ownerCommandLine'。判定为并行会话的 server，复用会产生失真证据。"
            Write-Diagnostic -Level 'ERROR' -Message '请停掉该进程（或改 PLAYWRIGHT_BUSINESS_PDA_LIVE_PORT 换口）后重跑。'
            exit 1
        }
        $serverReuse = $true
        $env:PLAYWRIGHT_PDA_LIVE_REUSE = '1'
        Write-Diagnostic -Level 'WARN' -Message "live 端口 $livePort 已被占用，按 -AllowServerReuse 复用已验证归属的既有 dev server（监听进程命令行包含本 worktree 根路径）。"
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

# --- 5. 短超时注入（M2 network-resilience.spec.ts 的「挂起 + 短超时」场景依赖，方案 §4.2）：
#        VITE_NERV_IIP_REQUEST_TIMEOUT_MS 是 DEV-only 覆盖（request-timeout.ts 生产门禁，
#        钳制区间 [100, 30000]），由 vite dev server 启动时烘焙进 import.meta.env。
$timeoutEnvNote = $null
if (-not $serverReuse) {
    # 默认路径（本脚本经 Playwright webServer 自起 server）：未显式设置时注入推荐值 2000，
    # 保证标准入口跑全量 live spec 时超时场景不因缺 env 而环境阻塞。
    if ([string]::IsNullOrWhiteSpace($env:VITE_NERV_IIP_REQUEST_TIMEOUT_MS)) {
        $env:VITE_NERV_IIP_REQUEST_TIMEOUT_MS = '2000'
        $timeoutEnvNote = "$($env:VITE_NERV_IIP_REQUEST_TIMEOUT_MS) (script-injected default)"
    }
    else {
        $timeoutEnvNote = "$($env:VITE_NERV_IIP_REQUEST_TIMEOUT_MS) (pre-set by caller)"
    }
    Write-Diagnostic "短超时注入：VITE_NERV_IIP_REQUEST_TIMEOUT_MS=$timeoutEnvNote——将随本脚本进程 env 烘焙进自起的 vite webServer（DEV-only 覆盖，生产构建无效）。"
}
else {
    # 复用模式：vite define 在 server **启动时**烘焙，复用的既有 server 无法证明启动时带了
    # 该值——如实 WARN，不静默跳过、不伪造：依赖超时注入的场景可能如实失败（环境阻塞）。
    $timeoutEnvNote = 'unverifiable (reused pre-existing dev server; env is baked at server start)'
    Write-Diagnostic -Level 'WARN' -Message '复用既有 dev server：无法证明其启动时带 VITE_NERV_IIP_REQUEST_TIMEOUT_MS（vite env 在 server 启动时烘焙）——超时注入影响的场景（network-resilience 挂起+短超时）可能如实失败（环境阻塞），不静默跳过、不伪造。需要该场景请停掉既有 server 后不带 -AllowServerReuse 重跑。'
}

# --- 6. 跑 live spec + 证据归集（无论成败都归集 trace/截图，失败时证据更重要）。
if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    # 每次运行唯一（毫秒级时间戳 + shortSHA + 4 位随机十六进制后缀），不与其他运行
    # 混目录、不覆盖既有证据；随机后缀防两次并行运行落在同一毫秒 + 同一 HEAD 撞名。
    $EvidenceDir = Join-Path $repoRoot 'frontend' 'DESIGN' 'roadmaps' 'assets' ("{0}-{1}-pda-live-{2:x4}" -f (Get-Date -Format 'yyyy-MM-dd-HHmmss-fff'), $head, (Get-Random -Maximum 65536))
}
# 归集前守卫（默认目录与显式 -EvidenceDir 一视同仁）：目标目录已存在且非空 → 报错退出，
# 绝不把本次证据静默混入/覆盖到既有证据里。
if ((Test-Path $EvidenceDir) -and $null -ne (Get-ChildItem -LiteralPath $EvidenceDir -Force | Select-Object -First 1)) {
    Write-Diagnostic -Level 'ERROR' -Message "证据目录已存在且非空：$EvidenceDir。拒绝混入/覆盖既有证据——请换一个 -EvidenceDir，或先清理该目录后重跑。"
    exit 1
}
# 原子占位：唯一性检查通过后立即创建目录（不带 -Force，已存在即失败），把
# 「检查 → 归集时创建」窗口内并行运行抢占同名目录的竞态收敛为此处的创建失败。
try {
    New-Item -ItemType Directory -Path $EvidenceDir -ErrorAction Stop | Out-Null
}
catch {
    Write-Diagnostic -Level 'ERROR' -Message "证据目录创建失败（目录已存在——可能被并行运行抢占，或显式 -EvidenceDir 指向既有空目录）：$EvidenceDir——$($_.Exception.Message)。请换一个 -EvidenceDir 后重跑。"
    exit 1
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
        # 证据目录已在第 6 步前置原子占位创建，这里只写入不创建。
        # 不加 -Force：目录本次运行独占（占位创建成功即无既有文件），撞文件如实报错而非静默覆盖。
        Copy-Item -Path (Join-Path $testResultsDir '*') -Destination $EvidenceDir -Recurse
        $reuseNote = $serverReuse ? 'true (reused pre-existing dev server; worktree ownership VERIFIED — listener process command line contains this worktree root)' : 'false'
        "branch=$branch`nhead=$head`nworktree=$scriptToplevel`ndate=$(Get-Date -Format 'o')`nserverReuse=$reuseNote`nrequestTimeoutMs=$timeoutEnvNote" |
            Set-Content -Path (Join-Path $EvidenceDir 'run-fingerprint.txt') -Encoding utf8
        Write-Diagnostic "证据已归集：$EvidenceDir（trace/截图 + commit 指纹；请按方案 §4.5 补关键请求与后端回读记录）"
    }
    else {
        Write-Diagnostic -Level 'WARN' -Message "未发现 Playwright 输出目录（$testResultsDir），无证据可归集。"
    }
}

exit $exitCode
