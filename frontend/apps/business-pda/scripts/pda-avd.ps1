# Script-Governance:
#   Category: verify
#   SideEffects:
#     - create: creates an Android Virtual Device (default name nerv-pda, pixel_5 profile,
#       system-images;android-35;google_apis;x86_64) under the user AVD home
#       (%USERPROFILE%\.android\avd); idempotent — skipped when an AVD with the same name exists
#     - start: probes emulator acceleration (emulator -accel-check), then launches the Android
#       emulator as a detached background process (windowed by default; -Headless adds
#       -no-window/-no-audio/-no-boot-anim + swiftshader GPU) and polls adb until
#       sys.boot_completed=1; the emulator KEEPS RUNNING after the script exits
#     - start: starts the adb server if it is not already running
#     - stop: kills the target emulator instance(s) via adb emu kill
#     - status: read-only (adb devices / avdmanager list avd / accel probe)
#   Writes:
#     - artifacts/script-logs/** (command logs + detached emulator stdout/stderr)
#     - %USERPROFILE%\.android\avd\<AvdName>.avd/** (AVD disk images, created by create /
#       mutated by the running emulator)
#   Cleanup:
#     - start does NOT stop the emulator on exit (that is the point — subsequent adb steps need
#       it); run -Action stop to shut it down
#     - stop leaves the AVD definition and disk images in place (delete manually via
#       avdmanager delete avd -n <name> if needed)
#   Requires:
#     - PowerShell 7
#     - Android SDK at ANDROID_HOME with: emulator, platform-tools (adb),
#       cmdline-tools\latest (avdmanager), system-images;android-35;google_apis;x86_64
#     - Hardware acceleration (WHPX/AEHD) for a usable boot; without it pass -NoAccel to
#       run degraded (very slow) after the accel probe reports unavailable

# PDA L3（Android 模拟器 + APK）设施：AVD 生命周期脚本。
# 方案：frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §5 / §8 M3b；
# 文档：docs/architecture/mobile-pda-testing-and-smoke.md「L3 Android 模拟器 + APK」节。
# 本脚本只管 AVD 创建/启动/关停/状态；APK 构建安装归 pda-apk-build.ps1，扫码注入归 pda-adb-scan.ps1。

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('create', 'start', 'stop', 'status')]
    [string] $Action,

    # AVD 名称；create/start/stop 共用。固定默认值保证证据指纹可比。
    [string] $AvdName = 'nerv-pda',

    # start：无窗口模式（-no-window -no-audio -no-boot-anim + swiftshader GPU），
    # 适合无人值守跑冒烟 + screencap 存证；默认带窗口便于人工走查。
    [switch] $Headless,

    # start：加速探测不可用时的显式降级开关——加 -no-accel 纯软件模拟（极慢但能跑）。
    # 探测可用时传入本开关同样生效（强制关加速），便于对照排查。
    [switch] $NoAccel,

    # start：从 emulator 进程拉起到 sys.boot_completed=1 的总超时（秒）。
    [int] $BootTimeoutSeconds = 600,

    # stop：目标模拟器 serial（如 emulator-5554）；缺省时对所有在线 emulator-* 实例逐一关停。
    [string] $Serial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $repoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

# --- Android SDK 定位：不依赖全局安装状态，显式从 ANDROID_HOME 读，缺失即报错并给出本机既定路径提示。
if ([string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少环境变量 ANDROID_HOME。请指向本机 Android SDK 根目录（本仓库开发机为 C:\Users\hp\android-sdk），例如：$env:ANDROID_HOME = ''C:\Users\hp\android-sdk''。'
    exit 1
}
$sdkRoot = $env:ANDROID_HOME
$emulatorExe = Join-Path $sdkRoot 'emulator' 'emulator.exe'
$adbExe = Join-Path $sdkRoot 'platform-tools' 'adb.exe'
$avdManagerBat = Join-Path $sdkRoot 'cmdline-tools' 'latest' 'bin' 'avdmanager.bat'
foreach ($tool in @($emulatorExe, $adbExe, $avdManagerBat)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        Write-Diagnostic -Level 'ERROR' -Message "Android SDK 组件缺失：$tool。请用 sdkmanager 安装 emulator / platform-tools / cmdline-tools;latest（以及 system-images;android-35;google_apis;x86_64）。"
        exit 1
    }
}

$systemImage = 'system-images;android-35;google_apis;x86_64'
$deviceProfile = 'pixel_5'

function Invoke-AvdManagerCommandLine {
    # avdmanager 是 .bat，经 cmd /d /s /c "<整条命令行>" 调起（/s 剥外层引号、保留内层引号，
    # 保证含分号的 -k 包 id 与含空格的 SDK 路径都完整传入 .bat）。
    # create 会交互式问「Do you wish to create a custom hardware profile?」——经 stdin 灌 'no' 回答默认值。
    param(
        [Parameter(Mandatory)]
        [string] $CommandLine,

        [switch] $AnswerNoToPrompts
    )

    $output = if ($AnswerNoToPrompts) {
        'no' | cmd /d /s /c $CommandLine 2>&1
    }
    else {
        cmd /d /s /c $CommandLine 2>&1
    }

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = @($output | ForEach-Object { "$_" })
    }
}

function Get-EmulatorSerialList {
    $devices = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('devices') -Name 'adb-devices'
    return @(
        $devices.Stdout -split "`r?`n" |
            Where-Object { $_ -match '^(emulator-\d+)\s+device$' } |
            ForEach-Object { ($_ -split '\s+')[0] }
    )
}

function Test-EmulatorAcceleration {
    # emulator -accel-check：可用时 exit 0 且输出 'is installed and usable'；不可用时非零退出。
    try {
        $probe = Invoke-NativeCommandOutput -Command $emulatorExe -Arguments @('-accel-check') -Name 'emulator-accel-check'
        return [pscustomobject]@{ Usable = ($probe.Stdout -match '(?i)installed and usable'); Detail = $probe.Stdout.Trim() }
    }
    catch {
        return [pscustomobject]@{ Usable = $false; Detail = "$($_.Exception.Message)" }
    }
}

switch ($Action) {
    'create' {
        $listResult = Invoke-AvdManagerCommandLine -CommandLine "`"$avdManagerBat`" list avd -c"
        if ($listResult.ExitCode -ne 0) {
            Write-Diagnostic -Level 'ERROR' -Message "avdmanager list avd 失败（exit=$($listResult.ExitCode)）：$($listResult.Output -join ' | ')"
            exit 1
        }
        if (@($listResult.Output | Where-Object { $_.Trim() -eq $AvdName }).Count -gt 0) {
            Write-Diagnostic "AVD '$AvdName' 已存在，跳过创建（幂等）。如需重建请先 avdmanager delete avd -n $AvdName。"
            exit 0
        }

        Write-Diagnostic "创建 AVD '$AvdName'（$systemImage / $deviceProfile）……"
        $createResult = Invoke-AvdManagerCommandLine -AnswerNoToPrompts -CommandLine "`"$avdManagerBat`" create avd -n `"$AvdName`" -k `"$systemImage`" -d `"$deviceProfile`""
        if ($createResult.ExitCode -ne 0) {
            Write-Diagnostic -Level 'ERROR' -Message "avdmanager create avd 失败（exit=$($createResult.ExitCode)）：$($createResult.Output -join ' | ')"
            exit 1
        }
        Write-Diagnostic "AVD '$AvdName' 创建完成。"
        exit 0
    }

    'start' {
        # --- 1. 加速探测：不可加速且未显式 -NoAccel 时报错退出（不静默慢跑）。
        $accel = Test-EmulatorAcceleration
        if ($accel.Usable) {
            Write-Diagnostic "加速探测：可用——$($accel.Detail)"
        }
        elseif ($NoAccel) {
            Write-Diagnostic -Level 'WARN' -Message "加速探测：不可用（$($accel.Detail)）。按 -NoAccel 以纯软件模拟降级运行——启动会非常慢，请相应调大 -BootTimeoutSeconds。"
        }
        else {
            Write-Diagnostic -Level 'ERROR' -Message "加速探测：不可用——$($accel.Detail)"
            Write-Diagnostic -Level 'ERROR' -Message '本机无可用模拟器加速（WHPX/AEHD）。确认无法启用加速时，可加 -NoAccel 显式降级为纯软件模拟（极慢但能跑）。'
            exit 1
        }

        # --- 2. 启动前快照既有模拟器 serial，用于识别本次拉起的新实例。
        Invoke-NativeCommandOutput -Command $adbExe -Arguments @('start-server') -Name 'adb-start-server' | Out-Null
        $preexistingSerials = @(Get-EmulatorSerialList)

        $emulatorArguments = @('-avd', $AvdName, '-no-snapshot')
        if ($Headless) {
            # 无人值守：swiftshader 软件 GPU 渲染稳定出帧（screencap 可用），并关窗口/音频/开机动画。
            $emulatorArguments += @('-no-window', '-no-audio', '-no-boot-anim', '-gpu', 'swiftshader_indirect')
        }
        else {
            $emulatorArguments += @('-gpu', 'auto')
        }
        if ($NoAccel) {
            $emulatorArguments += @('-no-accel')
        }

        $logDirectory = New-ScriptAutomationLogDirectory -Name 'pda-avd-emulator'
        Write-Diagnostic "启动模拟器：emulator $($emulatorArguments -join ' ')（detached，日志：$logDirectory）"
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $detached = Start-DetachedManagedProcess -Command $emulatorExe -Arguments $emulatorArguments -StdoutPath (Join-Path $logDirectory 'emulator-stdout.log') -StderrPath (Join-Path $logDirectory 'emulator-stderr.log')
        Write-Diagnostic "模拟器进程已拉起（pid=$($detached.Pid)），等待 adb 设备上线……"

        # --- 3. 等新 serial 上线（adb devices 出现 device 态），再轮询 sys.boot_completed=1。
        $serialValue = $null
        while ($stopwatch.Elapsed.TotalSeconds -lt $BootTimeoutSeconds -and -not $serialValue) {
            $emulatorProcess = Get-Process -Id $detached.Pid -ErrorAction SilentlyContinue
            if ($null -eq $emulatorProcess) {
                Write-Diagnostic -Level 'ERROR' -Message "模拟器进程（pid=$($detached.Pid)）已退出。检查日志：$logDirectory"
                exit 1
            }
            $newSerials = @(Get-EmulatorSerialList | Where-Object { $preexistingSerials -notcontains $_ })
            if ($newSerials.Count -gt 0) {
                $serialValue = $newSerials[0]
                break
            }
            Start-Sleep -Seconds 3
        }
        if (-not $serialValue) {
            Write-Diagnostic -Level 'ERROR' -Message "等待新模拟器设备上线超时（${BootTimeoutSeconds}s）。检查日志：$logDirectory"
            exit 1
        }
        Write-Diagnostic "设备上线：$serialValue（$($stopwatch.Elapsed)），等待 sys.boot_completed……"

        $booted = $false
        while ($stopwatch.Elapsed.TotalSeconds -lt $BootTimeoutSeconds) {
            try {
                $bootProp = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $serialValue, 'shell', 'getprop', 'sys.boot_completed') -Name 'adb-getprop-boot'
                if ($bootProp.Stdout.Trim() -eq '1') {
                    $booted = $true
                    break
                }
            }
            catch {
                # 设备短暂 offline/重枚举期间 getprop 会非零退出——按未就绪继续轮询。
            }
            Start-Sleep -Seconds 5
        }
        if (-not $booted) {
            Write-Diagnostic -Level 'ERROR' -Message "等待 sys.boot_completed=1 超时（${BootTimeoutSeconds}s，serial=$serialValue）。模拟器仍在运行，可手动排查或 -Action stop 关停。日志：$logDirectory"
            exit 1
        }
        $stopwatch.Stop()

        # --- 4. 就绪指纹（L3 证据口径的一部分：AVD/系统镜像/WebView 版本）。
        $buildFingerprint = (Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $serialValue, 'shell', 'getprop', 'ro.build.fingerprint') -Name 'adb-getprop-fingerprint').Stdout.Trim()
        $webviewVersion = 'unknown'
        try {
            $webviewDump = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $serialValue, 'shell', 'dumpsys', 'package', 'com.google.android.webview') -Name 'adb-webview-version'
            $webviewMatch = [regex]::Match($webviewDump.Stdout, 'versionName=(\S+)')
            if ($webviewMatch.Success) {
                $webviewVersion = $webviewMatch.Groups[1].Value
            }
        }
        catch {
            Write-Diagnostic -Level 'WARN' -Message "读取 WebView 版本失败：$($_.Exception.Message)"
        }

        Write-Diagnostic "模拟器就绪：serial=$serialValue，boot 耗时 $($stopwatch.Elapsed)。"
        Write-Diagnostic "指纹：avd=$AvdName image=$systemImage build=$buildFingerprint webview=$webviewVersion accel=$($NoAccel ? 'off (-NoAccel)' : ($accel.Usable ? 'on' : 'unknown'))"
        Write-Host "SERIAL=$serialValue"
        exit 0
    }

    'stop' {
        # @() 必须包在 if 表达式整体外层：if 赋值同样走 pipeline 解包，单元素会退化成标量，
        # StrictMode 下 .Count 会炸。
        $targetSerials = @(if ([string]::IsNullOrWhiteSpace($Serial)) { Get-EmulatorSerialList } else { $Serial })
        if ($targetSerials.Count -eq 0) {
            Write-Diagnostic '没有在线的模拟器实例，无需关停。'
            exit 0
        }
        foreach ($targetSerial in $targetSerials) {
            try {
                $killResult = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $targetSerial, 'emu', 'kill') -Name 'adb-emu-kill'
                Write-Diagnostic "已请求关停 $targetSerial：$($killResult.Stdout.Trim())"
            }
            catch {
                Write-Diagnostic -Level 'WARN' -Message "关停 $targetSerial 失败：$($_.Exception.Message)"
            }
        }
        exit 0
    }

    'status' {
        $accel = Test-EmulatorAcceleration
        Write-Diagnostic "加速探测：$($accel.Usable ? '可用' : '不可用')——$($accel.Detail)"
        $avdList = Invoke-AvdManagerCommandLine -CommandLine "`"$avdManagerBat`" list avd -c"
        Write-Diagnostic "已定义 AVD：$(@($avdList.Output | Where-Object { $_ -match '\S' }) -join ', ')"
        $devices = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('devices', '-l') -Name 'adb-devices-l'
        Write-Host $devices.Stdout.Trim()
        exit 0
    }
}
