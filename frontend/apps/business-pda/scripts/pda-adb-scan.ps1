# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Injects key input into a connected Android emulator/device via
#       adb shell input text + keyevent 66 (Enter) — the code lands in whichever view
#       currently holds focus on the device
#     - With -Screencap: captures the device framebuffer to a temp file on the device
#       (/data/local/tmp/) and pulls it to the given host path, then deletes the device temp file
#   Writes:
#     - artifacts/script-logs/**
#     - The -Screencap host path (PNG), when requested
#   Cleanup:
#     - Deletes the on-device screencap temp file after pulling it
#   Requires:
#     - PowerShell 7
#     - Android SDK platform-tools (adb) at ANDROID_HOME
#     - A booted emulator/device visible in adb devices (pda-avd.ps1 -Action start)

# PDA L3（Android 模拟器 + APK）设施：adb 扫码注入。
# 方案：frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §5 / §2 保真等级；
# 文档：docs/architecture/mobile-pda-testing-and-smoke.md「L3 Android 模拟器 + APK」节。
#
# 保真定位（方案 §2）：adb shell input 是 **Android 输入栈注入**——字符流经 IME/焦点系统
# 进入当前焦点视图，比浏览器 page.keyboard（DOM 近似）更接近真机，但**不是 HID 硬件等价**：
# 没有 USB scan code / device id / KeyCharacterMap，仿真不了键重复/丢键与厂商扫码服务
# （如 Zebra DataWedge——未来接入改 adb shell am broadcast intent 仿真，本脚本留扩展位）。
# 末尾 keyevent 66（Enter）对应扫码枪 Enter 后缀（ScanBar 现契约仅处理 Enter 后缀）。

[CmdletBinding()]
param(
    # 要注入的条码内容。受 adb shell input text 的转义限制（见下方校验注释），
    # 仅接受 [A-Za-z0-9._:+,/=-]；含空格/控制字符/shell 元字符的码值请改用 L4 实体枪验证。
    [Parameter(Mandatory)]
    [string] $Code,

    # 目标设备 serial（如 emulator-5554）；缺省时要求恰好一个在线模拟器，否则报错。
    [string] $Serial,

    # 注入完成后截屏并拉取到该宿主机路径（PNG）。
    [string] $Screencap
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $repoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

if ([string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少环境变量 ANDROID_HOME。请指向本机 Android SDK 根目录（本仓库开发机为 C:\Users\hp\android-sdk）。'
    exit 1
}
$adbExe = Join-Path $env:ANDROID_HOME 'platform-tools' 'adb.exe'
if (-not (Test-Path -LiteralPath $adbExe -PathType Leaf)) {
    Write-Diagnostic -Level 'ERROR' -Message "adb 不存在：$adbExe。请用 sdkmanager 安装 platform-tools。"
    exit 1
}

# --- 码值校验：adb shell input text 对特殊字符没有可靠的跨版本转义通道——
#     空格须编码为 %s；& | ( ) < > ; * ~ ' " \ $ 会被设备端 shell 解析（破坏码值甚至注入命令）；
#     GS1 FNC1/GS 等控制字符（\x1d）根本无法经 input text 注入（归 L4 实体枪）。
#     这里选择白名单直接拒绝，而不是静默转义后注入一个「看起来像但不是」的码值。
if ($Code -notmatch '^[A-Za-z0-9._:+,/=-]+$') {
    Write-Diagnostic -Level 'ERROR' -Message "码值含 adb shell input text 无法可靠注入的字符：'$Code'。仅支持 [A-Za-z0-9._:+,/=-]（空格、引号、shell 元字符、GS1 控制字符均不支持——这是 input text 的通道限制，不是本脚本的裁剪；此类码值请走 L4 实体扫码枪验证）。"
    exit 1
}

# --- 目标设备解析。
function Get-EmulatorSerialList {
    $devices = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('devices') -Name 'adb-devices'
    return @(
        $devices.Stdout -split "`r?`n" |
            Where-Object { $_ -match '^(emulator-\d+)\s+device$' } |
            ForEach-Object { ($_ -split '\s+')[0] }
    )
}

if ([string]::IsNullOrWhiteSpace($Serial)) {
    # @() 包裹：pipeline 单元素输出会被解包成标量，StrictMode 下 .Count 会炸。
    $onlineSerials = @(Get-EmulatorSerialList)
    if ($onlineSerials.Count -eq 0) {
        Write-Diagnostic -Level 'ERROR' -Message '没有在线的模拟器。先运行 pda-avd.ps1 -Action start，或用 -Serial 指定真机/其他设备。'
        exit 1
    }
    if ($onlineSerials.Count -gt 1) {
        Write-Diagnostic -Level 'ERROR' -Message "在线模拟器多于一个（$($onlineSerials -join ', ')），请用 -Serial 显式指定目标。"
        exit 1
    }
    $Serial = $onlineSerials[0]
}

# --- 注入：input text（字符流经 Android 输入栈/焦点系统进入当前焦点视图）+ keyevent 66（Enter 后缀）。
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $Serial, 'shell', 'input', 'text', $Code) -Name 'adb-input-text' | Out-Null
Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $Serial, 'shell', 'input', 'keyevent', '66') -Name 'adb-input-enter' | Out-Null
$stopwatch.Stop()
Write-Diagnostic "已向 $Serial 注入码值 '$Code' + Enter（keyevent 66），耗时 $($stopwatch.Elapsed)。注意：input text 是整段提交，无法仿真实体枪 10–30ms 字符间隔时序（该时序覆盖归 L2 simulateScanGun / L4 实体枪）。"

# --- 截屏存证（可选）：设备端 screencap → adb pull（二进制安全）→ 清理设备端临时文件。
if (-not [string]::IsNullOrWhiteSpace($Screencap)) {
    $screencapDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($Screencap))
    if (-not (Test-Path -LiteralPath $screencapDirectory)) {
        New-Item -ItemType Directory -Path $screencapDirectory -Force | Out-Null
    }
    $devicePath = "/data/local/tmp/pda-adb-scan-$([System.Guid]::NewGuid().ToString('N')).png"
    try {
        Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $Serial, 'shell', 'screencap', '-p', $devicePath) -Name 'adb-screencap' | Out-Null
        Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $Serial, 'pull', $devicePath, $Screencap) -Name 'adb-pull-screencap' | Out-Null
        Write-Diagnostic "截屏已保存：$Screencap"
    }
    finally {
        try {
            Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $Serial, 'shell', 'rm', '-f', $devicePath) -Name 'adb-rm-screencap' | Out-Null
        }
        catch {
            Write-Diagnostic -Level 'WARN' -Message "清理设备端临时截屏失败（$devicePath）：$($_.Exception.Message)"
        }
    }
}

exit 0
