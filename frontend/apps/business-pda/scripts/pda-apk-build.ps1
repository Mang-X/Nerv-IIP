# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Builds the business-pda web bundle (pnpm run build -> dist/) with the dev-APK
#       build-time env injected (VITE_NERV_IIP_API_BASE_URL + NERV_PDA_DEV_APK=1 by default;
#       -ReleaseProfile keeps NERV_PDA_DEV_APK unset so androidScheme stays https)
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
#       + platforms;android-3x installed)
#     - Network access to Gradle/Maven repositories on the first run

# PDA dev 调试 APK 可复现构建入口（方案 frontend/DESIGN/roadmaps/2026-07-15-pda-device-sim-detection-plan.md §5 / §8 M3a；
# 部署口径 docs/architecture/mobile-pda-deployment.md）。
# 默认产出 L3 dev 冒烟用 debug APK：基址指向宿主 vite dev 统一双代理入口
# http://10.0.2.2:5126（模拟器内 10.0.2.2 = 宿主回环），并注入 NERV_PDA_DEV_APK=1
# 使 capacitor.config.ts 切到 androidScheme http + cleartext true（仅本次构建生效）。
# 生产/真实网关 APK 请传 -ApiBaseUrl https://<gateway> -ReleaseProfile（不注入 dev 分叉，
# androidScheme 保持 https；打的仍是 assembleDebug 调试签名包，正式发布签名归发版流程）。

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

# --- 1. 工具链前置检查（缺失即失败并给出本仓约定，不静默降级）。
$androidHome = if (-not [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
if ([string]::IsNullOrWhiteSpace($androidHome) -or -not (Test-Path (Join-Path $androidHome 'platform-tools'))) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 Android SDK：请设置 ANDROID_HOME（或 ANDROID_SDK_ROOT）指向已装 platform-tools/build-tools/platforms 的 SDK 根目录。本机约定路径示例：C:\Users\hp\android-sdk（本仓不设全局 env，须显式传给本脚本进程）。'
    exit 1
}
$env:ANDROID_HOME = $androidHome
Write-Diagnostic "ANDROID_HOME=$androidHome"

if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME) -or -not (Test-Path (Join-Path $env:JAVA_HOME 'bin'))) {
    Write-Diagnostic -Level 'ERROR' -Message '缺少 JDK：请设置 JAVA_HOME 指向 JDK 21+（Capacitor 8 的 android 库 sourceCompatibility=21，JDK 17 会报「无效的源发行版：21」）。本机约定路径示例：C:\Users\hp\.jdks\jdk-21.0.11+10。'
    exit 1
}
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
$env:VITE_NERV_IIP_API_BASE_URL = $ApiBaseUrl
if ($ReleaseProfile) {
    Remove-Item Env:NERV_PDA_DEV_APK -ErrorAction SilentlyContinue
    Write-Diagnostic "构建配置：ReleaseProfile（androidScheme https，无 cleartext）；VITE_NERV_IIP_API_BASE_URL=$ApiBaseUrl"
    if ($ApiBaseUrl -match '^http://') {
        Write-Diagnostic -Level 'WARN' -Message 'ReleaseProfile 下基址是 http:// 明文地址——APK 内 WebView 将因 cleartext 限制无法访问该基址。请改用 https 网关，或去掉 -ReleaseProfile 走 dev 分叉。'
    }
}
else {
    $env:NERV_PDA_DEV_APK = '1'
    Write-Diagnostic "构建配置：dev 调试 APK（androidScheme http + cleartext true，仅本次构建）；VITE_NERV_IIP_API_BASE_URL=$ApiBaseUrl"
    Write-Diagnostic 'dev 口径提醒：该 APK 依赖宿主机上同时运行 vite dev（5126，统一双代理入口）与后端栈（nerv.ps1 dev）。'
}

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

# --- 6. 产物校验 + 构建指纹（commit/时间/基址/分叉开关 + SHA256），落在 APK 旁（gitignored）。
$apkPath = Join-Path $androidDir 'app' 'build' 'outputs' 'apk' 'debug' 'app-debug.apk'
if (-not (Test-Path $apkPath)) {
    Write-Diagnostic -Level 'ERROR' -Message "Gradle 成功退出但未找到 APK：$apkPath。"
    exit 1
}
$apkHash = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
$apkSize = (Get-Item -LiteralPath $apkPath).Length
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
) -join "`n" | Set-Content -Path $fingerprintPath -Encoding utf8

Write-Diagnostic "APK 构建完成：$apkPath"
Write-Diagnostic "SHA256=$apkHash sizeBytes=$apkSize"
Write-Diagnostic "构建指纹已写入：$fingerprintPath（commit=$head, baseUrl=$ApiBaseUrl, profile=$profileNote）"

exit 0
