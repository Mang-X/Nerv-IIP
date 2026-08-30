# Script-Governance:
#   Category: check
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

$pdaToolsRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '..')).Path
. (Join-Path $pdaToolsRepoRoot 'scripts' 'lib' 'ScriptAutomation.ps1')

function Get-PdaPlatformToolName {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [string] $WindowsSuffix = '.exe'
    )

    return $IsWindows ? "$Name$WindowsSuffix" : $Name
}

function Resolve-PdaAndroidHome {
    $adbName = Get-PdaPlatformToolName -Name 'adb'
    $candidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
    if ($IsWindows) {
        if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) { $candidates += Join-Path $env:USERPROFILE 'android-sdk' }
        if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $candidates += Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    }
    else {
        $candidates += Join-Path $HOME 'Library/Android/sdk'
        $candidates += Join-Path $HOME 'Android/Sdk'
    }
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-Path -LiteralPath (Join-Path $candidate 'platform-tools' $adbName) -PathType Leaf) { return $candidate }
    }
    return $null
}

function Get-PdaJdkMajor {
    param(
        [Parameter(Mandatory)]
        [string] $JdkHome
    )

    $javaName = Get-PdaPlatformToolName -Name 'java'
    $releaseFile = Join-Path $JdkHome 'release'
    if (-not (Test-Path -LiteralPath (Join-Path $JdkHome 'bin' $javaName) -PathType Leaf) -or
        -not (Test-Path -LiteralPath $releaseFile -PathType Leaf)) {
        return 0
    }
    $match = (Select-String -LiteralPath $releaseFile -Pattern '^JAVA_VERSION="([^"]+)"').Matches
    if ($match.Count -eq 0) { return 0 }
    return [int] (($match[0].Groups[1].Value) -split '\.')[0]
}

function Resolve-PdaJavaHome21 {
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $explicitMajor = Get-PdaJdkMajor -JdkHome $env:JAVA_HOME
        if ($explicitMajor -ge 21 -and $explicitMajor -le 24) { return $env:JAVA_HOME }
        Write-Diagnostic -Level 'WARN' -Message "显式 JAVA_HOME 不在兼容区间 JDK 21–24（$($env:JAVA_HOME)，主版本 $explicitMajor；Gradle 8.14 最高支持 Java 24），尝试探测约定位置的兼容 JDK。"
    }

    $best = $null
    $bestMajor = 0
    $roots = @()
    if (-not [string]::IsNullOrWhiteSpace($HOME)) { $roots += Join-Path $HOME '.jdks' }
    if ($IsWindows) {
        $roots += 'C:\Program Files\Eclipse Adoptium'
        $roots += 'C:\Program Files\Java'
    }
    else {
        $javaCommand = Get-Command 'java' -ErrorAction SilentlyContinue
        if ($javaCommand -and (Test-Path -LiteralPath $javaCommand.Source -PathType Leaf)) {
            $javaFile = Get-Item -LiteralPath $javaCommand.Source
            $resolvedJavaFile = $javaFile.ResolveLinkTarget($true)
            $javaPath = $null -eq $resolvedJavaFile ? $javaFile.FullName : $resolvedJavaFile.FullName
            $commandJdkHome = Split-Path -Parent (Split-Path -Parent $javaPath)
            $commandMajor = Get-PdaJdkMajor -JdkHome $commandJdkHome
            if ($commandMajor -ge 21 -and $commandMajor -le 24) { return $commandJdkHome }
        }
    }

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($directory in (Get-ChildItem -LiteralPath $root -Directory)) {
            $major = Get-PdaJdkMajor -JdkHome $directory.FullName
            if ($major -ge 21 -and $major -le 24 -and $major -gt $bestMajor) {
                $best = $directory.FullName
                $bestMajor = $major
            }
        }
    }
    return $best
}
