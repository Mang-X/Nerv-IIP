# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Builds the business-pda web bundle (pnpm run build -> dist/) with the dev-APK
#       build-time env injected (VITE_NERV_IIP_API_BASE_URL + NERV_PDA_DEV_APK=1 by default;
#       -ReleaseProfile explicitly CLEARS NERV_PDA_DEV_APK from the process env so
#       androidScheme stays https even when the flag leaked in from a previous shell/CI step,
#       and requires an explicit https:// -ApiBaseUrl)
#     - After the build, verifies the produced APK fail-closed via aapt2 (manifest
#       usesCleartextTraffic must match the profile) + the packaged
#       assets/capacitor.config.json (androidScheme/cleartext must match the profile);
#       any mismatch exits 1 and the verification verdict is written into
#       build-fingerprint.txt
#     - Regenerates the gitignored native shell frontend/apps/business-pda/android/ via
#       `cap add android` when missing, then runs `cap sync android`
#     - Runs Gradle assembleDebug inside android/ (first run downloads the Gradle
#       distribution + Maven dependencies into the user-level Gradle cache ~/.gradle)
#   Writes:
#     - frontend/apps/business-pda/dist/** (web bundle)
#     - frontend/apps/business-pda/android/** (gitignored regenerable native project,
#       including android/app/build/outputs/apk/debug/app-debug.apk and a
#       build-fingerprint.txt next to the APK)
#     - artifacts/script-logs/** (via ScriptAutomation command logs)
#     - ~/.gradle/** (user-level Gradle wrapper/dependency cache)
#   Cleanup:
#     - None (android/ and dist/ are gitignored build outputs; re-runs are idempotent
#       and overwrite in place; user-level Gradle cache is intentionally kept)
#   Requires:
#     - PowerShell 7
#     - Node.js + pnpm (frontend workspace installed: pnpm -C frontend install)
#     - JDK 21+ (JAVA_HOME, checked against $JAVA_HOME/release; Capacitor 8's android
#       library compiles with sourceCompatibility 21 -- JDK 17 fails with
#       "invalid source release: 21")
#     - Android SDK (ANDROID_HOME or ANDROID_SDK_ROOT; platform-tools + build-tools
#       (incl. aapt2, preferred pin 35.0.0, used for the fail-closed APK verification)
#       + platforms;android-3x installed)
#     - Network access to Gradle/Maven repositories on the first run

# PDA dev 调试 APK 可复现构建入口（方案 frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §5 / §8 M3a；
# 部署口径 docs/architecture/mobile-pda-deployment.md）。
# 默认产出 L3 dev 冒烟用 debug APK：基址指向宿主 vite dev 统一双代理入口
# http://10.0.2.2:5126（模拟器内 10.0.2.2 = 宿主回环），并注入 NERV_PDA_DEV_APK=1
# 使 capacitor.config.ts 切到 androidScheme http + cleartext true（仅本次构建生效）。
# 生产/真实网关 APK 请传 -ApiBaseUrl https://<gateway> -ReleaseProfile（显式清除 dev 分叉
# env 防残留，且基址必须显式传 https:// 绝对地址；androidScheme 保持 https；打的仍是
# assembleDebug 调试签名包，正式发布签名归发版流程）。
# 两条 profile 构建后都做 fail-closed 校验：aapt2 断言 manifest usesCleartextTraffic 与
# profile 一致 + 解包 assets/capacitor.config.json 断言 androidScheme/cleartext 一致，
# 不一致 exit 1，结论写入 build-fingerprint.txt。

[CmdletBinding()]
param(
    # 构建期注入的网关基址（VITE_NERV_IIP_API_BASE_URL）。默认 dev 冒烟统一入口。
    [string] $ApiBaseUrl = 'http://10.0.2.2:5126',

    # 不注入 NERV_PDA_DEV_APK=1：capacitor.config.ts 保持 androidScheme https、无 cleartext。
    # 用于按真实 HTTPS 网关基址打包验证 release 网络口径（产物仍是 debug 签名 APK）。
    [switch] $ReleaseProfile,

    # 删除既有 android/ 后从 capacitor.config.ts + 锁定的 @capacitor/* 全量再生（确定性重建）。
    [switch] $Recreate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $repoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

$appDir = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$androidDir = Join-Path $appDir 'android'

# --- 1. 工具链前置检查（显式 env 优先；未设时自动探测约定安装位置——工具链装在
#     用户目录但不设全局 env 时（本仓开发机现状），新终端/新会话零知识也能直接跑；
#     探测不到才失败，不静默降级）。
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

$androidHome = Resolve-PdaAndroidHome
if ([string]::IsNullOrWhiteSpace($androidHome)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 Android SDK：ANDROID_HOME/ANDROID_SDK_ROOT 未设，且约定位置（%USERPROFILE%\android-sdk、%LOCALAPPDATA%\Android\Sdk）均无 platform-tools\adb.exe。安装口径见 docs/architecture/mobile-pda-deployment.md（sdkmanager 装 platform-tools/build-tools/platforms/emulator/系统镜像）。'
    exit 1
}
$env:ANDROID_HOME = $androidHome
Write-Diagnostic "ANDROID_HOME=$androidHome"

# JDK 解析：显式 JAVA_HOME 若满足 21+ 直接用；不满足（未设/缺 java.exe/主版本<21）则
# 探测约定位置（%USERPROFILE%\.jdks、Eclipse Adoptium 安装目录）里主版本最高的 21+ JDK。
# Capacitor 8 的 android 库 sourceCompatibility=21，JDK 17 会报「无效的源发行版：21」。
function Get-PdaJdkMajor([string] $jdkHome) {
    $releaseFile = Join-Path $jdkHome 'release'
    if (-not (Test-Path (Join-Path $jdkHome 'bin\java.exe')) -or -not (Test-Path $releaseFile)) { return 0 }
    $m = (Select-String -LiteralPath $releaseFile -Pattern '^JAVA_VERSION="([^"]+)"').Matches
    if ($m.Count -eq 0) { return 0 }
    return [int] (($m[0].Groups[1].Value) -split '\.')[0]
}

function Resolve-PdaJavaHome21 {
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $explicitMajor = Get-PdaJdkMajor $env:JAVA_HOME
        if ($explicitMajor -ge 21 -and $explicitMajor -le 24) { return $env:JAVA_HOME }
        Write-Diagnostic -Level 'WARN' -Message "显式 JAVA_HOME 不在兼容区间 JDK 21–24（$($env:JAVA_HOME)，主版本 $explicitMajor；Gradle 8.14 最高支持 Java 24），尝试探测约定位置的兼容 JDK。"
    }
    $best = $null
    $bestMajor = 0
    foreach ($root in @((Join-Path $env:USERPROFILE '.jdks'), 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files\Java')) {
        if (-not (Test-Path $root)) { continue }
        foreach ($dir in (Get-ChildItem -LiteralPath $root -Directory)) {
            $major = Get-PdaJdkMajor $dir.FullName
            # 兼容区间 21–24：下限是 Capacitor 8 的 sourceCompatibility=21，
            # 上限是 Gradle 8.14 支持的最高 daemon JVM（Java 24），更高版本会构建失败。
            if ($major -ge 21 -and $major -le 24 -and $major -gt $bestMajor) { $best = $dir.FullName; $bestMajor = $major }
        }
    }
    return $best
}

$resolvedJavaHome = Resolve-PdaJavaHome21
if ([string]::IsNullOrWhiteSpace($resolvedJavaHome)) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 JDK 21+：JAVA_HOME 未设或版本不足，且约定位置（%USERPROFILE%\.jdks、Program Files\Eclipse Adoptium/Java）未探测到 21+ JDK。Capacitor 8 的 android 库 sourceCompatibility=21（JDK 17 会报「无效的源发行版：21」）。安装口径见 docs/architecture/mobile-pda-deployment.md。'
    exit 1
}
$env:JAVA_HOME = $resolvedJavaHome
# 版本前置断言：读 $JAVA_HOME/release 的 JAVA_VERSION，主版本 < 21 直接失败（免得 gradle
# 跑一分多钟才在 :capacitor-android:compileDebugJavaWithJavac 上报源发行版错误）。
$javaReleaseFile = Join-Path $env:JAVA_HOME 'release'
if (Test-Path $javaReleaseFile) {
    $javaVersionLine = (Select-String -LiteralPath $javaReleaseFile -Pattern '^JAVA_VERSION="([^"]+)"').Matches
    if ($javaVersionLine.Count -gt 0) {
        $javaVersion = $javaVersionLine[0].Groups[1].Value
        $javaMajor = [int] ($javaVersion -split '\.')[0]
        if ($javaMajor -lt 21) {
            Write-Diagnostic -Level 'ERROR' -Message "JAVA_HOME 指向 JDK $javaVersion，低于 Capacitor 8 要求的 21（android 库 sourceCompatibility=21）。请换 JDK 21+。"
            exit 1
        }
        Write-Diagnostic "JAVA_HOME=$($env:JAVA_HOME)（JDK $javaVersion）"
    }
    else {
        Write-Diagnostic -Level 'WARN' -Message "无法从 $javaReleaseFile 解析 JAVA_VERSION，跳过版本断言（要求 JDK 21+）。"
    }
}
else {
    Write-Diagnostic -Level 'WARN' -Message "JAVA_HOME 下无 release 文件，跳过版本断言（要求 JDK 21+）：$($env:JAVA_HOME)"
}

if (-not (Test-Path (Join-Path $appDir 'node_modules'))) {
    Write-Diagnostic -Level 'ERROR' -Message "缺少依赖：$appDir\node_modules 不存在。请先在仓库根运行 pnpm -C frontend install。"
    exit 1
}

# --- 2. 构建期 env 注入（vp build 阶段把 VITE_ 变量内联进 bundle；NERV_PDA_DEV_APK 由
#        capacitor.config.ts 在 cap sync 求值时读取，控制 androidScheme/cleartext 分叉）。
if ($ReleaseProfile) {
    # fail-closed（其一）：显式清除可能由手工调试 / CI 步骤残留的 dev 分叉开关。
    # capacitor.config.ts 只认这一个 env——残留会把 release 构建悄悄切到 http + cleartext。
    Remove-Item Env:NERV_PDA_DEV_APK -ErrorAction SilentlyContinue
    # release profile 基址语义：必须显式传入且为 https:// 绝对地址。默认值 10.0.2.2:5126 是
    # dev 冒烟统一入口，烧进 release 包必然连不上；http:// 明文基址在 androidScheme https +
    # 无 cleartext 下会被系统网络栈直接拒绝——两种情况 WARN 后继续只会产出必然无法联网的包，
    # 因此直接失败。
    if (-not $PSBoundParameters.ContainsKey('ApiBaseUrl')) {
        Write-Diagnostic -Level 'ERROR' -Message '-ReleaseProfile 必须显式传 -ApiBaseUrl https://<gateway>（默认值 http://10.0.2.2:5126 是 dev 冒烟统一入口，release 包不可用）。'
        exit 1
    }
    # 按绝对 URI 解析校验，而非前缀匹配：`https://`（空 Host）/`https:///path` 这类
    # 畸形值前缀能过但必然连不上，同样 fail-closed。
    $parsedBaseUri = $null
    if (
        -not [System.Uri]::TryCreate($ApiBaseUrl, [System.UriKind]::Absolute, [ref] $parsedBaseUri) -or
        $parsedBaseUri.Scheme -ne 'https' -or
        [string]::IsNullOrWhiteSpace($parsedBaseUri.Host)
    ) {
        Write-Diagnostic -Level 'ERROR' -Message "-ReleaseProfile 下 -ApiBaseUrl 必须是含非空 Host 的 https:// 绝对地址（收到：$ApiBaseUrl）。androidScheme https + 无 cleartext 的包访问非 https 基址必然失败；如要走明文 dev 口径请去掉 -ReleaseProfile。"
        exit 1
    }
    Write-Diagnostic "构建配置：ReleaseProfile（androidScheme https，无 cleartext）；VITE_NERV_IIP_API_BASE_URL=$ApiBaseUrl"
}
else {
    $env:NERV_PDA_DEV_APK = '1'
    Write-Diagnostic "构建配置：dev 调试 APK（androidScheme http + cleartext true，仅本次构建）；VITE_NERV_IIP_API_BASE_URL=$ApiBaseUrl"
    Write-Diagnostic 'dev 口径提醒：该 APK 依赖宿主机上同时运行 vite dev（5126，统一双代理入口）与后端栈（nerv.ps1 dev）。'
}
$env:VITE_NERV_IIP_API_BASE_URL = $ApiBaseUrl

# --- 3. web 构建（vue-tsc + vp build → dist/）。
Invoke-Pnpm -Arguments @('run', 'build') -WorkingDirectory $appDir -TimeoutSeconds 1800 -Name 'pda-apk-web-build' | Out-Null

# --- 4. 原生壳工程：缺失（或 -Recreate）时按部署文档口径确定性再生，随后 cap sync。
if ($Recreate -and (Test-Path $androidDir)) {
    Write-Diagnostic "按 -Recreate 删除既有 android/ 以全量再生：$androidDir"
    Remove-Item -LiteralPath $androidDir -Recurse -Force
}
if (-not (Test-Path $androidDir)) {
    Write-Diagnostic 'android/ 不存在，执行 cap add android（依据 capacitor.config.ts + 锁定的 @capacitor/* 确定性再生）……'
    Invoke-Pnpm -Arguments @('exec', 'cap', 'add', 'android') -WorkingDirectory $appDir -TimeoutSeconds 900 -Name 'pda-apk-cap-add' | Out-Null
}
Invoke-Pnpm -Arguments @('exec', 'cap', 'sync', 'android') -WorkingDirectory $appDir -TimeoutSeconds 900 -Name 'pda-apk-cap-sync' | Out-Null

# 本仓路径含非 ASCII（中文）目录，AGP 的路径检查会直接 fail：
# 「Your project path contains non-ASCII characters ... can be disabled by
# android.overridePathCheck=true」。现代 AGP/aapt2 下该检查是保守告警（实测本仓可正常出包），
# 幂等地在再生的 gradle.properties（gitignored）里补上开关。
$gradleProps = Join-Path $androidDir 'gradle.properties'
if ((Get-Content -LiteralPath $gradleProps -Raw) -notmatch 'android\.overridePathCheck') {
    Add-Content -LiteralPath $gradleProps -Value "`n# Repo lives under a non-ASCII (Chinese) path; the AGP path check is advisory on modern AGP/aapt2.`nandroid.overridePathCheck=true" -Encoding utf8
    Write-Diagnostic '已在 android/gradle.properties 追加 android.overridePathCheck=true（仓库路径含中文目录，AGP 路径检查须显式关闭）。'
}

# --- 5. Gradle assembleDebug（平台相关 wrapper；首跑会下载 Gradle 发行版与依赖，可能较慢）。
$gradleWrapper = if ($IsWindows) { Join-Path $androidDir 'gradlew.bat' } else { Join-Path $androidDir 'gradlew' }
if (-not (Test-Path $gradleWrapper)) {
    Write-Diagnostic -Level 'ERROR' -Message "未找到 Gradle wrapper：$gradleWrapper（cap add android 产物异常，请用 -Recreate 重跑）。"
    exit 1
}
# Windows 上 .bat 须经 cmd 启动（与 ScriptAutomation 的 Invoke-Pnpm 同法，避免
# ProcessStartInfo 直启批处理的兼容性问题）；工作目录即 android/，用 .\ 相对前缀显式
# 指向当前目录（环境可能设 NoDefaultCurrentDirectoryInExePath，裸文件名不搜当前目录）。
if ($IsWindows) {
    Invoke-NativeCommandWithTimeout -Command 'cmd' -Arguments @('/d', '/s', '/c', '.\gradlew.bat', 'assembleDebug', '--no-daemon') -WorkingDirectory $androidDir -TimeoutSeconds 3600 -Name 'pda-apk-gradle' | Out-Null
}
else {
    Invoke-NativeCommandWithTimeout -Command $gradleWrapper -Arguments @('assembleDebug', '--no-daemon') -WorkingDirectory $androidDir -TimeoutSeconds 3600 -Name 'pda-apk-gradle' | Out-Null
}

# --- 6. 产物存在性校验。
$apkPath = Join-Path $androidDir 'app' 'build' 'outputs' 'apk' 'debug' 'app-debug.apk'
if (-not (Test-Path $apkPath)) {
    Write-Diagnostic -Level 'ERROR' -Message "Gradle 成功退出但未找到 APK：$apkPath。"
    exit 1
}
$apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
$apkSize = (Get-Item -LiteralPath $apkPath).Length

# --- 7. fail-closed 校验（其二，核心）：断言产物 APK 的 manifest cleartext 与打进包内的
#        capacitor.config.json scheme/cleartext 与 profile 一致。env 残留、配置漂移或
#        cap sync 未按预期求值时，这里直接失败而不是产出口径不符的包。
$aapt2Name = $IsWindows ? 'aapt2.exe' : 'aapt2'
$buildToolsRoot = Join-Path $androidHome 'build-tools'
$aapt2 = Join-Path $buildToolsRoot '35.0.0' $aapt2Name
if (-not (Test-Path -LiteralPath $aapt2 -PathType Leaf)) {
    # 锁定优先 35.0.0；不在时退而取已安装的最高版本。校验是 fail-closed 门禁，找不到
    # aapt2 一律失败，不允许「没工具就跳过校验」。
    $aapt2 = $null
    $buildToolsCandidates = @()
    if (Test-Path -LiteralPath $buildToolsRoot) {
        $buildToolsCandidates = @(Get-ChildItem -LiteralPath $buildToolsRoot -Directory | Sort-Object Name -Descending)
    }
    foreach ($buildToolsCandidate in $buildToolsCandidates) {
        $aapt2Probe = Join-Path $buildToolsCandidate.FullName $aapt2Name
        if (Test-Path -LiteralPath $aapt2Probe -PathType Leaf) {
            $aapt2 = $aapt2Probe
            break
        }
    }
    if (-not $aapt2) {
        Write-Diagnostic -Level 'ERROR' -Message "未找到 aapt2（$buildToolsRoot\<version>\$aapt2Name）。APK manifest 校验是 fail-closed 门禁不可跳过：请 sdkmanager 安装 build-tools;35.0.0。"
        exit 1
    }
    Write-Diagnostic -Level 'WARN' -Message "首选 build-tools;35.0.0 未安装，改用：$aapt2"
}

try {
    $manifestDump = Invoke-NativeCommandOutput -Command $aapt2 -Arguments @('dump', 'xmltree', '--file', 'AndroidManifest.xml', $apkPath) -TimeoutSeconds 120 -Name 'pda-apk-aapt2-manifest'
}
catch {
    Write-Diagnostic -Level 'ERROR' -Message "aapt2 dump xmltree 失败（无法校验 manifest，fail-closed）：$($_.Exception.Message)"
    exit 1
}
# aapt2 xmltree 里 cleartext 属性形如：
#   A: http://schemas.android.com/apk/res/android:usesCleartextTraffic(0x0101064e)=true
$manifestCleartext = if ($manifestDump.Stdout -match 'usesCleartextTraffic[^\r\n]*=true') { 'true' }
elseif ($manifestDump.Stdout -match 'usesCleartextTraffic') { 'present-not-true' }
else { 'absent' }

# APK 即 zip：直接读包内 assets/capacitor.config.json（cap sync 求值 capacitor.config.ts 的落地产物）。
Add-Type -AssemblyName System.IO.Compression.FileSystem
$capacitorConfigJson = $null
$apkZip = [System.IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    $configEntry = @($apkZip.Entries | Where-Object { $_.FullName -eq 'assets/capacitor.config.json' })
    if ($configEntry.Count -gt 0) {
        $configReader = [System.IO.StreamReader]::new($configEntry[0].Open())
        try {
            $capacitorConfigJson = $configReader.ReadToEnd()
        }
        finally {
            $configReader.Dispose()
        }
    }
}
finally {
    $apkZip.Dispose()
}
if ([string]::IsNullOrWhiteSpace($capacitorConfigJson)) {
    Write-Diagnostic -Level 'ERROR' -Message 'APK 内未找到 assets/capacitor.config.json（无法校验 androidScheme，fail-closed）。'
    exit 1
}
$capacitorConfig = $capacitorConfigJson | ConvertFrom-Json
$packagedScheme = 'absent'
$packagedCleartext = 'absent'
if ($capacitorConfig.PSObject.Properties['server']) {
    if ($capacitorConfig.server.PSObject.Properties['androidScheme']) {
        $packagedScheme = [string] $capacitorConfig.server.androidScheme
    }
    if ($capacitorConfig.server.PSObject.Properties['cleartext']) {
        $packagedCleartext = ([bool] $capacitorConfig.server.cleartext) ? 'true' : 'false'
    }
}

$expectedScheme = $ReleaseProfile ? 'https' : 'http'
$expectedManifestCleartext = $ReleaseProfile ? 'absent' : 'true'
$expectedPackagedCleartext = $ReleaseProfile ? 'absent' : 'true'
$verifyFailures = @()
if ($manifestCleartext -ne $expectedManifestCleartext) {
    $verifyFailures += "manifest usesCleartextTraffic=$manifestCleartext（期望 $expectedManifestCleartext）"
}
if ($packagedScheme -ne $expectedScheme) {
    $verifyFailures += "capacitor.config.json androidScheme=$packagedScheme（期望 $expectedScheme）"
}
if ($packagedCleartext -ne $expectedPackagedCleartext) {
    $verifyFailures += "capacitor.config.json cleartext=$packagedCleartext（期望 $expectedPackagedCleartext）"
}
if ($verifyFailures.Count -gt 0) {
    Write-Diagnostic -Level 'ERROR' -Message "APK 产物校验失败（profile=$($ReleaseProfile ? 'release' : 'dev')）：$($verifyFailures -join '；')。产物口径与 profile 不符，拒绝放行——检查 NERV_PDA_DEV_APK 残留 / capacitor.config.ts 分叉逻辑后重跑。"
    exit 1
}
Write-Diagnostic "APK 产物校验通过：manifest usesCleartextTraffic=$manifestCleartext，androidScheme=$packagedScheme，cleartext=$packagedCleartext（与 $($ReleaseProfile ? 'release' : 'dev') profile 一致）。"

# --- 8. 构建指纹（commit/时间/基址/分叉开关 + SHA256 + 校验结论），落在 APK 旁（gitignored）。
$branch = (git -C $appDir rev-parse --abbrev-ref HEAD)
$head = (git -C $appDir rev-parse HEAD)
$profileNote = $ReleaseProfile ? 'release-profile (androidScheme https, no cleartext; still a debug-signed APK)' : 'dev-apk (NERV_PDA_DEV_APK=1: androidScheme http + cleartext true)'
$fingerprintPath = Join-Path (Split-Path -Parent $apkPath) 'build-fingerprint.txt'
@(
    "apk=$apkPath"
    "sha256=$apkHash"
    "sizeBytes=$apkSize"
    "branch=$branch"
    "commit=$head"
    "builtAt=$(Get-Date -Format 'o')"
    "apiBaseUrl=$ApiBaseUrl"
    "profile=$profileNote"
    "verifiedProfile=$($ReleaseProfile ? 'release' : 'dev')"
    "verifiedAndroidScheme=$packagedScheme"
    "verifiedPackagedCleartext=$packagedCleartext"
    "verifiedManifestCleartext=$manifestCleartext"
) -join "`n" | Set-Content -Path $fingerprintPath -Encoding utf8

Write-Diagnostic "APK 构建完成：$apkPath"
Write-Diagnostic "SHA256=$apkHash sizeBytes=$apkSize"
Write-Diagnostic "构建指纹已写入：$fingerprintPath（commit=$head, baseUrl=$ApiBaseUrl, profile=$profileNote）"

exit 0
