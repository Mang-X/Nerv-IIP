# Script-Governance:
#   Category: verify
#   SideEffects:
#     - create: creates an Android Virtual Device (default name nerv-pda, pixel_5 profile,
#       system-images;android-35;google_apis;x86_64) under the user AVD home
#       (%USERPROFILE%\.android\avd); idempotent — when an AVD with the same name exists its
#       config.ini image.sysdir.1 is verified against the pinned system image (mismatch or
#       unreadable config fails with exit 1 and a -Recreate hint); -Recreate deletes and
#       recreates the AVD (destroys its disk images)
#     - start: probes emulator acceleration (emulator -accel-check), then launches the Android
#       emulator as a detached background process (windowed by default; -Headless adds
#       -no-window/-no-audio/-no-boot-anim + swiftshader GPU) and polls adb until
#       sys.boot_completed=1; the emulator KEEPS RUNNING after the script exits
#     - start: starts the adb server if it is not already running
#     - stop: kills the target emulator instance(s) via adb emu kill — by default only
#       instances whose AVD name (adb emu avd name) matches -AvdName; -Serial targets one
#       explicit instance; -All targets every online emulator; waits until the targets go
#       offline and exits non-zero on kill failure or shutdown timeout
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
    # 白名单 ^[A-Za-z0-9._-]{1,64}$（脚本体内显式校验，越界 exit 1）：该值会拼进
    # cmd /c 的 avdmanager 命令行与 AVD 目录路径，白名单是防命令注入的第一道门。
    [string] $AvdName = 'nerv-pda',

    # create：同名 AVD 存在时先 avdmanager delete 再重建（会销毁该 AVD 的磁盘镜像）。
    [switch] $Recreate,

    # start：无窗口模式（-no-window -no-audio -no-boot-anim + swiftshader GPU），
    # 适合无人值守跑冒烟 + screencap 存证；默认带窗口便于人工走查。
    [switch] $Headless,

    # start：加速探测不可用时的显式降级开关——加 -no-accel 纯软件模拟（极慢但能跑）。
    # 探测可用时传入本开关同样生效（强制关加速），便于对照排查。
    [switch] $NoAccel,

    # start：从 emulator 进程拉起到 sys.boot_completed=1 的总超时（秒）。
    [int] $BootTimeoutSeconds = 600,

    # stop：目标模拟器 serial（如 emulator-5554），显式指定时只关该实例。
    # 缺省时默认只关 AVD 名（adb emu avd name 反查）匹配 -AvdName 的在线实例。
    [string] $Serial,

    # stop：关停所有在线 emulator-* 实例（不按 -AvdName 过滤）。
    [switch] $All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $repoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

# --- 注入面白名单校验：AvdName 会拼进 cmd /c 命令行（avdmanager 是 .bat，见
#     Invoke-AvdManagerCommandLine 注释）与 AVD 目录路径，越界直接拒绝，不做任何转义补救。
if ($AvdName -notmatch '^[A-Za-z0-9._-]{1,64}$') {
    Write-Diagnostic -Level 'ERROR' -Message "非法 -AvdName '$AvdName'：仅接受 ^[A-Za-z0-9._-]{1,64}$（该值会进入 cmd 命令行与文件路径，白名单校验防命令注入）。"
    exit 1
}
# -Serial 同样会进 adb 参数（经 ArgumentList 传递，本身安全），仍按已知形态校验防误用。
if (-not [string]::IsNullOrWhiteSpace($Serial) -and $Serial -notmatch '^emulator-\d{1,5}$') {
    Write-Diagnostic -Level 'ERROR' -Message "非法 -Serial '$Serial'：模拟器 serial 形如 emulator-5554。"
    exit 1
}

# --- Android SDK 定位：显式 ANDROID_HOME/ANDROID_SDK_ROOT 优先；未设时自动探测约定
#     安装位置（%USERPROFILE%\android-sdk、%LOCALAPPDATA%\Android\Sdk），新终端/新会话
#     零知识可跑；探测不到才报错。
function Resolve-PdaAndroidHome {
    $candidates = @(
        $env:ANDROID_HOME
        $env:ANDROID_SDK_ROOT
        (Join-Path $env:USERPROFILE 'android-sdk')
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
    )
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-Path (Join-Path $candidate 'platform-tools\adb.exe')) { return $candidate }
    }
    return $null
}

$sdkRoot = Resolve-PdaAndroidHome
if ([string]::IsNullOrWhiteSpace($sdkRoot)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 Android SDK：ANDROID_HOME/ANDROID_SDK_ROOT 未设，且约定位置（%USERPROFILE%\android-sdk、%LOCALAPPDATA%\Android\Sdk）均无 platform-tools\adb.exe。安装口径见 docs/architecture/mobile-pda-deployment.md。'
    exit 1
}
$env:ANDROID_HOME = $sdkRoot
Write-Diagnostic "ANDROID_HOME=$sdkRoot"
# SDK 路径也会拼进 cmd /c 命令行（引号包裹）：含 cmd 元字符/引号的路径直接拒绝，
# 而不是尝试转义（cmd 的引号语义无法与 .NET ArgumentList 转义可靠互通）。
if ($sdkRoot -match '["&|<>^%]') {
    Write-Diagnostic -Level 'ERROR' -Message "ANDROID_HOME 含 cmd 元字符（引号或 & | < > ^ %）：$sdkRoot。请把 SDK 放在不含这些字符的路径下。"
    exit 1
}
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
    #
    # 为什么不走 ScriptAutomation 的 Invoke-NativeCommandOutput（本函数是显式包装层）：
    # 1) .bat 不能由 ProcessStartInfo 可靠直启，须经 cmd；而 cmd /s 的引号语义与 .NET
    #    ArgumentList 的 C 式转义（\"）互不兼容，整条命令行经 ArgumentList 传给 cmd /c
    #    会被二次转义破坏（路径反斜杠 + 引号组合），无法安全 round-trip；
    # 2) create 需要向 stdin 灌 'no' 回答交互提示，helper 无 stdin 通道。
    # 注入面控制：拼进 $CommandLine 的变量只有白名单校验过的 $AvdName、元字符校验过的
    # SDK 路径与脚本内写死的常量（$systemImage/$deviceProfile），调用点不得引入其他变量。
    # avdmanager list/create/delete 均为秒级短命令，无 helper 超时通道的风险敞口有限；
    # 这里补齐与 helper 同口径的调用日志。
    param(
        [Parameter(Mandatory)]
        [string] $CommandLine,

        [switch] $AnswerNoToPrompts
    )

    if ($CommandLine -match '[\r\n]') {
        Write-Diagnostic -Level 'ERROR' -Message 'avdmanager 命令行含换行符，拒绝执行（防注入）。'
        exit 1
    }
    Write-Diagnostic "Invoking avdmanager via cmd /d /s /c: $CommandLine"

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

function Get-AvdConfiguredImage {
    # 读 AVD config.ini 的 image.sysdir.1（实测格式：
    #   image.sysdir.1 = system-images\android-35\google_apis\x86_64\
    # 分隔符/尾斜杠随平台浮动），归一化为正斜杠、无尾斜杠形态供与锁定镜像比对。
    # AVD 定义定位：优先 ANDROID_AVD_HOME，再读 <name>.ini 指针文件的 path=，兜底默认目录。
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $avdHome = if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_AVD_HOME)) { $env:ANDROID_AVD_HOME } else { Join-Path $HOME '.android' 'avd' }
    $avdDirectory = Join-Path $avdHome "$Name.avd"
    $pointerIni = Join-Path $avdHome "$Name.ini"
    if (Test-Path -LiteralPath $pointerIni -PathType Leaf) {
        $pathMatch = (Select-String -LiteralPath $pointerIni -Pattern '^\s*path\s*=\s*(.+?)\s*$').Matches
        if ($pathMatch.Count -gt 0) {
            $avdDirectory = $pathMatch[0].Groups[1].Value
        }
    }

    $configPath = Join-Path $avdDirectory 'config.ini'
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        return [pscustomobject]@{ ConfigPath = $configPath; SysDir = $null; Normalized = $null }
    }
    $imageMatch = (Select-String -LiteralPath $configPath -Pattern '^\s*image\.sysdir\.1\s*=\s*(.+?)\s*$').Matches
    if ($imageMatch.Count -eq 0) {
        return [pscustomobject]@{ ConfigPath = $configPath; SysDir = $null; Normalized = $null }
    }
    $rawSysDir = $imageMatch[0].Groups[1].Value
    return [pscustomobject]@{
        ConfigPath = $configPath
        SysDir = $rawSysDir
        Normalized = (($rawSysDir -replace '\\', '/').TrimEnd('/'))
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
            if ($Recreate) {
                Write-Diagnostic "按 -Recreate 删除既有 AVD '$AvdName'（含磁盘镜像）后重建……"
                $deleteResult = Invoke-AvdManagerCommandLine -CommandLine "`"$avdManagerBat`" delete avd -n `"$AvdName`""
                if ($deleteResult.ExitCode -ne 0) {
                    Write-Diagnostic -Level 'ERROR' -Message "avdmanager delete avd 失败（exit=$($deleteResult.ExitCode)）：$($deleteResult.Output -join ' | ')"
                    exit 1
                }
            }
            else {
                # 幂等不只认名字：核验既有 AVD 的 config.ini 系统镜像与锁定镜像一致，
                # 防「同名但镜像漂移」的 AVD 冒充可复现环境静默通过。config.ini 不可读
                # 视为无法核验，同样 fail-closed。
                $configuredImage = Get-AvdConfiguredImage -Name $AvdName
                $expectedImageDir = ($systemImage -replace ';', '/')
                if ($null -eq $configuredImage.SysDir) {
                    Write-Diagnostic -Level 'ERROR' -Message "AVD '$AvdName' 已存在但无法从 $($configuredImage.ConfigPath) 读到 image.sysdir.1，无法核验镜像一致性。请传 -Recreate 重建，或手工核对该 AVD。"
                    exit 1
                }
                if ($configuredImage.Normalized -ne $expectedImageDir) {
                    Write-Diagnostic -Level 'ERROR' -Message "AVD '$AvdName' 已存在但系统镜像不一致：config.ini image.sysdir.1=$($configuredImage.SysDir)，期望 $expectedImageDir（即 $systemImage）。证据指纹不可比，请传 -Recreate 按锁定镜像重建。"
                    exit 1
                }
                Write-Diagnostic "AVD '$AvdName' 已存在且镜像一致（image.sysdir.1=$($configuredImage.Normalized)），跳过创建（幂等）。如需重建请传 -Recreate。"
                exit 0
            }
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

        # 镜像指纹从 config.ini 实测读取（不回显脚本内锁定常量——AVD 若由旧版本/他处创建，
        # 硬编码值会谎报实际镜像）。
        $configuredImage = Get-AvdConfiguredImage -Name $AvdName
        $imageNote = ($null -ne $configuredImage.SysDir) ? $configuredImage.Normalized : "unknown（$($configuredImage.ConfigPath) 无 image.sysdir.1）"

        Write-Diagnostic "模拟器就绪：serial=$serialValue，boot 耗时 $($stopwatch.Elapsed)。"
        Write-Diagnostic "指纹：avd=$AvdName image=$imageNote（config.ini 实测）build=$buildFingerprint webview=$webviewVersion accel=$($NoAccel ? 'off (-NoAccel)' : ($accel.Usable ? 'on' : 'unknown'))"
        Write-Host "SERIAL=$serialValue"
        exit 0
    }

    'stop' {
        # 目标选择：-Serial 显式实例 > -All 全量 > 默认只关 AVD 名匹配 -AvdName 的实例
        # （adb emu avd name 反查——避免误杀并行会话/其他用途的模拟器）。
        # @() 必须包在 if 表达式整体外层：if 赋值同样走 pipeline 解包，单元素会退化成标量，
        # StrictMode 下 .Count 会炸。
        $onlineSerials = @(Get-EmulatorSerialList)
        $targetSerials = @(
            if (-not [string]::IsNullOrWhiteSpace($Serial)) {
                $Serial
            }
            elseif ($All) {
                $onlineSerials
            }
            else {
                foreach ($candidateSerial in $onlineSerials) {
                    try {
                        $nameProbe = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $candidateSerial, 'emu', 'avd', 'name') -Name 'adb-emu-avd-name'
                        # 输出为「<avd 名>\nOK」，取首个非空行。
                        $candidateName = @($nameProbe.Stdout -split "`r?`n" | Where-Object { $_ -match '\S' })[0].Trim()
                        if ($candidateName -eq $AvdName) {
                            $candidateSerial
                        }
                        else {
                            Write-Diagnostic "跳过 $candidateSerial（avd=$candidateName ≠ $AvdName；-All 可全量关停）。"
                        }
                    }
                    catch {
                        Write-Diagnostic -Level 'WARN' -Message "读取 $candidateSerial 的 AVD 名失败（$($_.Exception.Message)），按不匹配跳过。"
                    }
                }
            }
        )
        if ($targetSerials.Count -eq 0) {
            Write-Diagnostic "没有匹配的在线模拟器实例（AvdName=$AvdName$([string]::IsNullOrWhiteSpace($Serial) ? '' : "，Serial=$Serial")），无需关停。全量关停请传 -All。"
            exit 0
        }

        $killFailed = $false
        foreach ($targetSerial in $targetSerials) {
            try {
                $killResult = Invoke-NativeCommandOutput -Command $adbExe -Arguments @('-s', $targetSerial, 'emu', 'kill') -Name 'adb-emu-kill'
                Write-Diagnostic "已请求关停 $targetSerial：$($killResult.Stdout.Trim())"
            }
            catch {
                Write-Diagnostic -Level 'WARN' -Message "关停 $targetSerial 请求失败：$($_.Exception.Message)"
                $killFailed = $true
            }
        }

        # 关停确认：轮询到目标 serial 全部离线才算成功；超时/请求失败 exit 非零（不虚报已关停）。
        $shutdownDeadline = [datetime]::UtcNow.AddSeconds(60)
        while ($true) {
            $stillOnline = @(Get-EmulatorSerialList | Where-Object { $targetSerials -contains $_ })
            if ($stillOnline.Count -eq 0) {
                break
            }
            if ([datetime]::UtcNow -gt $shutdownDeadline) {
                Write-Diagnostic -Level 'ERROR' -Message "关停超时：$($stillOnline -join ', ') 在 60s 后仍在线。"
                exit 1
            }
            Start-Sleep -Seconds 2
        }
        if ($killFailed) {
            Write-Diagnostic -Level 'ERROR' -Message '部分实例的关停请求失败（见上方 WARN）。'
            exit 1
        }
        Write-Diagnostic "已关停并确认离线：$($targetSerials -join ', ')。"
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
