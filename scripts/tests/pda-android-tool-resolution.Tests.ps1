# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes business-pda Android tool discovery against disposable fake SDK/JDK commands
#   Writes:
#     - Temporary test harness files outside the repository
#     - artifacts/script-logs/** through the governed command wrapper
#   Cleanup:
#     - Removes the disposable test harness directory in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$pdaScripts = Join-Path $repoRoot 'frontend/apps/business-pda/scripts'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-pda-android-tools-$([Guid]::NewGuid().ToString('N'))"
$sdkRoot = Join-Path $fixtureRoot 'android-sdk'
$jdkRoot = Join-Path $fixtureRoot 'jdk-21'
$previousAndroidHome = $env:ANDROID_HOME
$previousAndroidSdkRoot = $env:ANDROID_SDK_ROOT
$previousJavaHome = $env:JAVA_HOME

function Write-FakeTool {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $PosixBody,
        [Parameter(Mandatory)] [string] $WindowsBody
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, ($IsWindows ? $WindowsBody : $PosixBody), [Text.UTF8Encoding]::new($false))
    if (-not $IsWindows) {
        [IO.File]::SetUnixFileMode(
            $Path,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    }
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $adbName = $IsWindows ? 'adb.exe' : 'adb'
    $emulatorName = $IsWindows ? 'emulator.exe' : 'emulator'
    $avdManagerName = $IsWindows ? 'avdmanager.bat' : 'avdmanager'
    $javaName = $IsWindows ? 'java.exe' : 'java'

    $adbPosix = @'
#!/bin/sh
case "$*" in *devices*) printf 'List of devices attached\n' ;; esac
exit 0
'@
    $emulatorPosix = @'
#!/bin/sh
printf 'accel is installed and usable\n'
'@
    $avdManagerPosix = @'
#!/bin/sh
printf 'nerv-test-avd\n'
'@
    $javaPosix = @'
#!/bin/sh
exit 0
'@
    $adbWindows = "@echo off`r`nif `"%1`"==`"devices`" echo List of devices attached`r`nexit /b 0`r`n"
    $emulatorWindows = "@echo accel is installed and usable`r`n"
    $avdManagerWindows = "@echo nerv-test-avd`r`n"
    $javaWindows = "@exit /b 0`r`n"

    Write-FakeTool -Path (Join-Path $sdkRoot 'platform-tools' $adbName) -PosixBody $adbPosix -WindowsBody $adbWindows
    Write-FakeTool -Path (Join-Path $sdkRoot 'emulator' $emulatorName) -PosixBody $emulatorPosix -WindowsBody $emulatorWindows
    Write-FakeTool -Path (Join-Path $sdkRoot 'cmdline-tools' 'latest' 'bin' $avdManagerName) -PosixBody $avdManagerPosix -WindowsBody $avdManagerWindows
    Write-FakeTool -Path (Join-Path $jdkRoot 'bin' $javaName) -PosixBody $javaPosix -WindowsBody $javaWindows
    [IO.File]::WriteAllText((Join-Path $jdkRoot 'release'), 'JAVA_VERSION="21.0.8"', [Text.UTF8Encoding]::new($false))

    $env:ANDROID_HOME = $sdkRoot
    $env:ANDROID_SDK_ROOT = $null
    $env:JAVA_HOME = $jdkRoot

    $buildText = Get-Content -LiteralPath (Join-Path $pdaScripts 'pda-apk-build.ps1') -Raw
    $tokens = $null
    $parseErrors = $null
    $buildAst = [Management.Automation.Language.Parser]::ParseInput($buildText, [ref] $tokens, [ref] $parseErrors)
    $requiredFunctions = @('Resolve-PdaAndroidHome', 'Get-PdaJdkMajor', 'Resolve-PdaJavaHome21')
    $functionText = @($requiredFunctions | ForEach-Object {
        $functionName = $_
        $functionAst = $buildAst.Find({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                [string]::Equals($node.Name, $functionName, [StringComparison]::Ordinal)
        }, $true)
        if ($null -eq $functionAst) { throw "Missing tool resolver function: $functionName" }
        $functionAst.Extent.Text
    }) -join [Environment]::NewLine
    $buildHarnessPath = Join-Path $fixtureRoot 'pda-apk-build-tools.ps1'
    $buildHarness = @"
function Write-Diagnostic { param([string] `$Level, [string] `$Message) }
$functionText
Write-Host "ANDROID_HOME=`$(Resolve-PdaAndroidHome)"
Write-Host "JAVA_HOME=`$(Resolve-PdaJavaHome21)"
"@
    [IO.File]::WriteAllText($buildHarnessPath, $buildHarness, [Text.UTF8Encoding]::new($false))
    $buildOutput = & pwsh -NoProfile -File $buildHarnessPath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or
        -not $buildOutput.Contains("ANDROID_HOME=$sdkRoot", [StringComparison]::Ordinal) -or
        -not $buildOutput.Contains("JAVA_HOME=$jdkRoot", [StringComparison]::Ordinal)) {
        throw "pda-apk-build must accept the platform-native adb/java names. Output: $buildOutput"
    }

    $avdOutput = & pwsh -NoProfile -File (Join-Path $pdaScripts 'pda-avd.ps1') -Action status 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or -not $avdOutput.Contains('nerv-test-avd', [StringComparison]::Ordinal)) {
        throw "pda-avd status must invoke the platform-native adb/emulator/avdmanager names. Output: $avdOutput"
    }

    $scanOutput = & pwsh -NoProfile -File (Join-Path $pdaScripts 'pda-adb-scan.ps1') -Code 'NERV-1973' -Serial 'emulator-5554' 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or -not $scanOutput.Contains("已向 emulator-5554 注入码值 'NERV-1973'", [StringComparison]::Ordinal)) {
        throw "pda-adb-scan must invoke the platform-native adb name. Output: $scanOutput"
    }
}
finally {
    $env:ANDROID_HOME = $previousAndroidHome
    $env:ANDROID_SDK_ROOT = $previousAndroidSdkRoot
    $env:JAVA_HOME = $previousJavaHome
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host 'PDA Android tool resolution contracts passed.'
