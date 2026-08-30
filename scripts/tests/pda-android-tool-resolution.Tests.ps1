# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes business-pda Android tool discovery against one disposable fake SDK/JDK fixture
#   Writes:
#     - Temporary test files outside the repository
#     - artifacts/script-logs/** through ScriptAutomation
#   Cleanup:
#     - Removes the disposable fixture in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$pdaScripts = Join-Path $repoRoot 'frontend/apps/business-pda/scripts'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $pdaScripts 'PdaAndroidTools.ps1')

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-pda-android-tools-$([Guid]::NewGuid().ToString('N'))"
$sdkRoot = Join-Path $fixtureRoot 'android-sdk'
$jdkRoot = Join-Path $fixtureRoot 'jdk-21'
$avdManagerCapture = Join-Path $fixtureRoot 'avdmanager-arguments.log'
$adbCapture = Join-Path $fixtureRoot 'adb-invocations.log'

function Write-FakeTool {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Body
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Body, [Text.UTF8Encoding]::new($false))
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

    if ($IsWindows) {
        Write-FakeTool -Path (Join-Path $sdkRoot 'platform-tools' $adbName) -Body ''
        Write-FakeTool -Path (Join-Path $jdkRoot 'bin' $javaName) -Body ''
    }
    else {
        $adbBody = @'
#!/bin/sh
printf 'PDA_ADB_MARKER\n'
printf 'PDA_ADB_MARKER %s\n' "$*" >> "$NERV_PDA_FAKE_ADB_CAPTURE"
case "$*" in *devices*) printf 'List of devices attached\n' ;; esac
'@
        $emulatorBody = @'
#!/bin/sh
printf 'PDA_EMULATOR_MARKER installed and usable\n'
'@
        $avdManagerBody = @'
#!/bin/sh
printf 'PDA_AVDMANAGER_MARKER\n'
printf '%s\n' "$*" >> "$NERV_PDA_FAKE_AVDMANAGER_CAPTURE"
'@
        Write-FakeTool -Path (Join-Path $sdkRoot 'platform-tools' $adbName) -Body $adbBody
        Write-FakeTool -Path (Join-Path $sdkRoot 'emulator' $emulatorName) -Body $emulatorBody
        Write-FakeTool -Path (Join-Path $sdkRoot 'cmdline-tools' 'latest' 'bin' $avdManagerName) -Body $avdManagerBody
        Write-FakeTool -Path (Join-Path $jdkRoot 'bin' $javaName) -Body "#!/bin/sh`nexit 0`n"
    }
    [IO.File]::WriteAllText((Join-Path $jdkRoot 'release'), 'JAVA_VERSION="21.0.8"', [Text.UTF8Encoding]::new($false))

    Invoke-WithScopedEnvironment -Variables @{
        ANDROID_HOME = $sdkRoot
        ANDROID_SDK_ROOT = $null
        JAVA_HOME = $jdkRoot
        NERV_PDA_FAKE_AVDMANAGER_CAPTURE = $avdManagerCapture
        NERV_PDA_FAKE_ADB_CAPTURE = $adbCapture
    } -ScriptBlock {
        $buildAst = $null
        foreach ($parsePath in @(
            (Join-Path $pdaScripts 'PdaAndroidTools.ps1')
            (Join-Path $pdaScripts 'pda-apk-build.ps1')
            (Join-Path $pdaScripts 'pda-avd.ps1')
            (Join-Path $pdaScripts 'pda-adb-scan.ps1')
            $PSCommandPath
        )) {
            $tokens = $null
            $parseErrors = $null
            $parsedAst = [Management.Automation.Language.Parser]::ParseInput(
                [IO.File]::ReadAllText($parsePath),
                [ref] $tokens,
                [ref] $parseErrors)
            if ($parseErrors.Count -gt 0) {
                throw "PowerShell parse failed for $parsePath`: $($parseErrors -join '; ')"
            }
            if ([string]::Equals([IO.Path]::GetFileName($parsePath), 'pda-apk-build.ps1', [StringComparison]::Ordinal)) {
                $buildAst = $parsedAst
            }
            Write-Host "PDA_PARSE_OK=$([IO.Path]::GetRelativePath($repoRoot, $parsePath))"
        }

        foreach ($buildResolverContract in @(
            [pscustomobject]@{ Variable = 'androidHome'; Command = 'Resolve-PdaAndroidHome' }
            [pscustomobject]@{ Variable = 'resolvedJavaHome'; Command = 'Resolve-PdaJavaHome21' }
        )) {
            $assignment = @($buildAst.EndBlock.Statements | Where-Object {
                $_ -is [Management.Automation.Language.AssignmentStatementAst] -and
                $_.Left -is [Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals($_.Left.VariablePath.UserPath, $buildResolverContract.Variable, [StringComparison]::Ordinal)
            })
            $command = $assignment.Count -eq 1 -and
                $assignment[0].Right -is [Management.Automation.Language.PipelineAst] -and
                $assignment[0].Right.PipelineElements.Count -eq 1 -and
                $assignment[0].Right.PipelineElements[0] -is [Management.Automation.Language.CommandAst] ?
                $assignment[0].Right.PipelineElements[0].GetCommandName() : $null
            if (-not [string]::Equals($command, $buildResolverContract.Command, [StringComparison]::Ordinal)) {
                throw "pda-apk-build must assign `$$($buildResolverContract.Variable) from $($buildResolverContract.Command); found '$command'."
            }
        }

        if (-not [string]::Equals((Resolve-PdaAndroidHome), $sdkRoot, [StringComparison]::Ordinal)) {
            throw 'The PDA Android SDK resolver did not select the explicit fixture SDK.'
        }
        if (-not [string]::Equals((Resolve-PdaJavaHome21), $jdkRoot, [StringComparison]::Ordinal)) {
            throw 'The PDA JDK resolver did not select the explicit fixture JDK.'
        }
        foreach ($toolContract in @(
            [pscustomobject]@{ Name = 'adb'; WindowsSuffix = '.exe'; Expected = $adbName }
            [pscustomobject]@{ Name = 'emulator'; WindowsSuffix = '.exe'; Expected = $emulatorName }
            [pscustomobject]@{ Name = 'avdmanager'; WindowsSuffix = '.bat'; Expected = $avdManagerName }
            [pscustomobject]@{ Name = 'java'; WindowsSuffix = '.exe'; Expected = $javaName }
        )) {
            $resolvedName = Get-PdaPlatformToolName -Name $toolContract.Name -WindowsSuffix $toolContract.WindowsSuffix
            if (-not [string]::Equals($resolvedName, $toolContract.Expected, [StringComparison]::Ordinal)) {
                throw "PDA tool name resolution returned '$resolvedName'; expected '$($toolContract.Expected)'."
            }
        }

        if ($IsWindows) {
            Write-Host 'PDA_WINDOWS_CODE_PATH_ONLY=SDK/JDK and .exe/.bat names resolved; fake tools not executed'
        }
        else {
            $avdResult = Invoke-PwshScript -ScriptPath (Join-Path $pdaScripts 'pda-avd.ps1') -Arguments @('-Action', 'status') -Name 'pda-avd-tool-resolution' -WorkingDirectory $repoRoot -TimeoutSeconds 30
            $avdOutput = [IO.File]::ReadAllText($avdResult.StdoutPath)
            foreach ($marker in @('PDA_ADB_MARKER', 'PDA_EMULATOR_MARKER', 'PDA_AVDMANAGER_MARKER')) {
                if (-not $avdOutput.Contains($marker, [StringComparison]::Ordinal)) {
                    throw "pda-avd status did not invoke $marker. Output: $avdOutput"
                }
            }

            $createResult = Invoke-PwshScript -ScriptPath (Join-Path $pdaScripts 'pda-avd.ps1') -Arguments @('-Action', 'create', '-AvdName', 'nerv-pda-fixture') -Name 'pda-avd-image-resolution' -WorkingDirectory $repoRoot -TimeoutSeconds 30
            $createOutput = [IO.File]::ReadAllText($createResult.StdoutPath)
            $capturedArguments = [IO.File]::ReadAllText($avdManagerCapture)
            $expectedAbi = ($IsMacOS -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) ? 'arm64-v8a' : 'x86_64'
            $expectedPackage = "system-images;android-35;google_apis;$expectedAbi"
            if (-not $capturedArguments.Contains("create avd -n nerv-pda-fixture -k $expectedPackage -d pixel_5", [StringComparison]::Ordinal)) {
                throw "pda-avd create did not select $expectedPackage. Output: $createOutput; captured: $capturedArguments"
            }

            [IO.File]::WriteAllText($adbCapture, '', [Text.UTF8Encoding]::new($false))
            $scanResult = Invoke-PwshScript -ScriptPath (Join-Path $pdaScripts 'pda-adb-scan.ps1') -Arguments @('-Code', 'NERV-1973', '-Serial', 'emulator-5554') -Name 'pda-adb-scan-tool-resolution' -WorkingDirectory $repoRoot -TimeoutSeconds 30
            $scanOutput = [IO.File]::ReadAllText($scanResult.StdoutPath)
            $scanAdbInvocations = @([IO.File]::ReadAllLines($adbCapture))
            $expectedScanAdbInvocations = @(
                'PDA_ADB_MARKER -s emulator-5554 shell input text NERV-1973'
                'PDA_ADB_MARKER -s emulator-5554 shell input keyevent 66'
            )
            if ($scanAdbInvocations.Count -ne $expectedScanAdbInvocations.Count -or
                -not [string]::Equals([string]::Join("`n", $scanAdbInvocations), [string]::Join("`n", $expectedScanAdbInvocations), [StringComparison]::Ordinal) -or
                -not $scanOutput.Contains("已向 emulator-5554 注入码值 'NERV-1973'", [StringComparison]::Ordinal)) {
                throw "pda-adb-scan must invoke exactly the text and Enter adb commands and report success. Output: $scanOutput; adb: $($scanAdbInvocations -join '; ')"
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Host 'PDA Android tool resolution contracts passed.'
