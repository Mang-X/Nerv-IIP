# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts short-lived native command probes and exact mutation probes
#   Writes:
#     - Temporary probe scripts, mutated library copies and command logs under the operating-system temp directory
#   Cleanup:
#     - Stops only probe processes whose exact PIDs were created by this test
#     - Removes the owned temporary root in finally
#   Requires:
#     - PowerShell 7

# Issue #2956 的 Regression 合同：一次性 native command 的正毫秒预算必须与既有 exit、stream、
# timeout、signal 和 cleanup 生命周期组合成立。每条关键断言都由下面的等价错误变异反证。

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'
$pwshPath = (Get-Process -Id $PID -ErrorAction Stop).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-native-output-budget-$([Guid]::NewGuid().ToString('N'))"

function Assert-Contract {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) { throw $Message }
}

function Invoke-ContractProbe {
    param(
        [Parameter(Mandatory)] [string] $ProbePath,
        [Parameter(Mandatory)] [string] $ProbeLibraryPath,
        [Parameter(Mandatory)] [string] $Scenario,
        [Parameter(Mandatory)] [string] $ScenarioRoot,
        [ValidateSet('root-exit', 'snapshot')]
        [string] $DrainMode = 'root-exit'
    )

    $output = @(
        & $pwshPath `
            -NoProfile `
            -NonInteractive `
            -File $ProbePath `
            -LibraryPath $ProbeLibraryPath `
            -Scenario $Scenario `
            -ScenarioRoot $ScenarioRoot `
            -DrainMode $DrainMode 2>&1 |
            ForEach-Object { "$_" }
    )
    $exitCode = $LASTEXITCODE

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output -join [Environment]::NewLine
    }
}

function Write-MutatedLibrary {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Anchor,
        [Parameter(Mandatory)] [string] $Replacement,
        [Parameter(Mandatory)] [int] $ExpectedOccurrences
    )

    $source = [IO.File]::ReadAllText($libraryPath)
    $occurrences = ([regex]::Matches($source, [regex]::Escape($Anchor))).Count
    Assert-Contract ($occurrences -eq $ExpectedOccurrences) "Mutation '$Name' expected $ExpectedOccurrences exact anchor occurrence(s), observed $occurrences."

    $mutationDirectory = Join-Path $temporaryRoot "mutation-$Name"
    [IO.Directory]::CreateDirectory($mutationDirectory) | Out-Null
    $mutationPath = Join-Path $mutationDirectory 'ScriptAutomation.ps1'
    [IO.File]::WriteAllText($mutationPath, $source.Replace($Anchor, $Replacement), [Text.UTF8Encoding]::new($false))
    return $mutationPath
}

$probeScript = @'
param(
    [Parameter(Mandatory)] [string] $LibraryPath,
    [Parameter(Mandatory)] [string] $Scenario,
    [Parameter(Mandatory)] [string] $ScenarioRoot,
    [ValidateSet('root-exit', 'snapshot')]
    [string] $DrainMode = 'root-exit'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $LibraryPath

function Assert-Probe {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) { throw $Message }
}

function Get-ProbeFailure {
    param([Parameter(Mandatory)] [scriptblock] $Action)

    try {
        & $Action | Out-Null
    }
    catch {
        return $_
    }

    throw 'The command was expected to fail but succeeded.'
}

function Wait-ProbeProcessExit {
    param([Parameter(Mandatory)] [int] $ProcessId)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(3)
    while ($null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 25
    }
    return $null -eq (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Invoke-NormalScenario {
    $quickArguments = if ([OperatingSystem]::IsWindows()) {
        @('-NoProfile', '-NonInteractive', '-Command', '[Console]::Out.Write("quick")')
    }
    else {
        @('-c', 'printf quick')
    }
    $quickCommand = if ([OperatingSystem]::IsWindows()) { (Get-Process -Id $PID).Path } else { '/bin/sh' }
    $quick = Invoke-NativeCommandOutput `
        -Command $quickCommand `
        -Arguments $quickArguments `
        -WorkingDirectory $ScenarioRoot `
        -TimeoutMilliseconds 900 `
        -Name 'native-output-positive-millisecond-budget'
    Assert-Probe ([string]::Equals([string]$quick.Stdout, 'quick', [StringComparison]::Ordinal)) 'A positive sub-second budget must start and complete a short command.'

    $zeroBudget = Get-ProbeFailure {
        Invoke-NativeCommandOutput `
            -Command $quickCommand `
            -Arguments $quickArguments `
            -WorkingDirectory $ScenarioRoot `
            -TimeoutMilliseconds 0 `
            -Name 'native-output-zero-budget'
    }
    Assert-Probe ([string]::Equals($zeroBudget.Exception.GetType().Name, 'ParameterBindingValidationException', [StringComparison]::Ordinal)) 'A zero millisecond budget must be rejected at the public boundary.'

    $largeOutputCommand = @"
`$stdout = 'o' * 131072 + 'tail'
`$stderr = 'e' * 131072 + 'errtail'
[Console]::Out.Write(`$stdout)
[Console]::Error.Write(`$stderr)
"@
    $result = Invoke-NativeCommandOutput `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-NonInteractive', '-Command', $largeOutputCommand) `
        -WorkingDirectory $ScenarioRoot `
        -TimeoutMilliseconds 5000 `
        -Name 'native-output-complete-streams'
    Assert-Probe ($result.ExitCode -eq 0) 'A successful command must preserve exit code 0.'
    Assert-Probe ($result.Stdout.Length -eq 131076 -and $result.Stdout.EndsWith('tail', [StringComparison]::Ordinal)) 'Stdout must be drained completely, including a final fragment without a newline.'
    Assert-Probe ($result.Stderr.Length -eq 131079 -and $result.Stderr.EndsWith('errtail', [StringComparison]::Ordinal)) 'Stderr must be drained completely, including a final fragment without a newline.'
    Assert-Probe (-not $result.PartialOutput) 'Complete redirected streams must not be marked partial.'

    $secondsResult = Invoke-NativeCommandOutput `
        -Command $quickCommand `
        -Arguments $quickArguments `
        -WorkingDirectory $ScenarioRoot `
        -TimeoutSeconds 2 `
        -Name 'native-output-seconds-compatibility'
    Assert-Probe ([string]::Equals([string]$secondsResult.Stdout, 'quick', [StringComparison]::Ordinal)) 'Existing explicit seconds callers must retain their behavior.'

    $positionalResult = Invoke-NativeCommandOutput $quickCommand $quickArguments $ScenarioRoot 2 'native-output-positional-compatibility'
    Assert-Probe ([string]::Equals([string]$positionalResult.Stdout, 'quick', [StringComparison]::Ordinal)) 'Existing positional seconds callers must retain their behavior.'
}

function Invoke-TimeoutScenario {
    $pidPath = Join-Path $ScenarioRoot 'timeout-pids.txt'
    $logDirectory = Join-Path $ScenarioRoot 'timeout-logs'
    $secret = 'native-timeout-secret'
    $sentinel = Start-Process `
        -FilePath (Get-Process -Id $PID).Path `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 20') `
        -PassThru

    try {
        if ([OperatingSystem]::IsWindows()) {
            $command = (Get-Process -Id $PID).Path
            $arguments = @(
                '-NoProfile',
                '-NonInteractive',
                '-Command',
                '[IO.File]::WriteAllText($env:NERV_PID_PATH, "$PID"); [Console]::Out.Write($env:NERV_TIMEOUT_SECRET); [Console]::Error.Write($env:NERV_TIMEOUT_SECRET); Start-Sleep -Milliseconds 450'
            )
        }
        else {
            $command = '/bin/sh'
            $arguments = @('-c', 'sleep 0.45 & child=$!; printf "%s,%s" "$$" "$child" > "$NERV_PID_PATH"; printf "%s" "$NERV_TIMEOUT_SECRET"; printf "%s" "$NERV_TIMEOUT_SECRET" >&2; wait "$child"')
        }

        $failure = Get-ProbeFailure {
            Invoke-NativeCommandOutput `
                -Command $command `
                -Arguments $arguments `
                -WorkingDirectory $ScenarioRoot `
                -TimeoutMilliseconds 100 `
                -Name 'native-output-timeout' `
                -LogDirectory $logDirectory `
                -Environment @{ NERV_PID_PATH = $pidPath; NERV_TIMEOUT_SECRET = $secret } `
                -SensitiveValues @($secret)
        }

        Assert-Probe ($failure.Exception -is [TimeoutException]) 'A command that exceeds a positive millisecond budget must fail with TimeoutException.'
        Assert-Probe ($failure.Exception.Message.Contains('100 milliseconds', [StringComparison]::Ordinal)) 'The timeout diagnosis must retain the millisecond budget without rounding it to seconds.'
        Assert-Probe ($failure.Exception.Data.Contains('Stdout') -and $failure.Exception.Data.Contains('Stderr')) 'Timeout diagnostics must retain captured stdout and stderr fields.'
        Assert-Probe (-not ([string]$failure.Exception.Data['Stdout']).Contains($secret, [StringComparison]::Ordinal)) 'Timeout stdout diagnostics must redact sensitive output.'
        Assert-Probe (-not ([string]$failure.Exception.Data['Stderr']).Contains($secret, [StringComparison]::Ordinal)) 'Timeout stderr diagnostics must redact sensitive output.'
        Assert-Probe ($failure.Exception.Data.Contains('PartialOutput')) 'Timeout diagnostics must retain the partial-output status.'
        Assert-Probe ([string]::Equals([string]$failure.Exception.Data['LogDirectory'], $logDirectory, [StringComparison]::Ordinal)) 'Timeout diagnostics must retain the requested log location.'
        Assert-Probe (Test-Path -LiteralPath (Join-Path $logDirectory 'stdout.log') -PathType Leaf) 'Timeout must persist captured stdout diagnostics.'
        Assert-Probe (Test-Path -LiteralPath (Join-Path $logDirectory 'stderr.log') -PathType Leaf) 'Timeout must persist captured stderr diagnostics.'
        $stdoutLog = [IO.File]::ReadAllText((Join-Path $logDirectory 'stdout.log'))
        $stderrLog = [IO.File]::ReadAllText((Join-Path $logDirectory 'stderr.log'))
        Assert-Probe ($stdoutLog.Contains('<redacted>', [StringComparison]::Ordinal) -and -not $stdoutLog.Contains($secret, [StringComparison]::Ordinal)) 'Persisted timeout stdout must contain only the redacted sensitive value.'
        Assert-Probe ($stderrLog.Contains('<redacted>', [StringComparison]::Ordinal) -and -not $stderrLog.Contains($secret, [StringComparison]::Ordinal)) 'Persisted timeout stderr must contain only the redacted sensitive value.'

        Assert-Probe (Test-Path -LiteralPath $pidPath -PathType Leaf) 'The timed command must publish the exact PIDs owned by this invocation.'
        $ownedProcessIds = @(([IO.File]::ReadAllText($pidPath)).Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [int]$_ })
        foreach ($ownedProcessId in $ownedProcessIds) {
            Assert-Probe (Wait-ProbeProcessExit -ProcessId $ownedProcessId) "Timeout cleanup left owned PID $ownedProcessId alive."
        }
        Assert-Probe (-not $sentinel.HasExited) 'Timeout cleanup must not stop an unrelated process outside this invocation.'
    }
    finally {
        if (-not $sentinel.HasExited) {
            $sentinel.Kill()
            [void] $sentinel.WaitForExit(5000)
        }
        $sentinel.Dispose()
    }
}

function Invoke-TimeoutTreeScenario {
    if (-not [OperatingSystem]::IsLinux()) {
        Write-Host 'Owned descendant cleanup probe is skipped outside Linux because the current process-tree enumerator is platform-specific.'
        return
    }

    $pidPath = Join-Path $ScenarioRoot 'timeout-tree-pids.txt'
    $ownedProcessIds = @()
    try {
        $failure = Get-ProbeFailure {
            Invoke-NativeCommandOutput `
                -Command '/bin/sh' `
                -Arguments @('-c', 'sleep 20 & child=$!; printf "%s,%s" "$$" "$child" > "$NERV_PID_PATH"; wait "$child"') `
                -WorkingDirectory $ScenarioRoot `
                -TimeoutMilliseconds 100 `
                -Name 'native-output-timeout-tree' `
                -LogDirectory (Join-Path $ScenarioRoot 'timeout-tree-logs') `
                -Environment @{ NERV_PID_PATH = $pidPath }
        }
        Assert-Probe ($failure.Exception -is [TimeoutException]) 'A long-running owned process tree must fail with TimeoutException at the millisecond budget.'
        Assert-Probe (Test-Path -LiteralPath $pidPath -PathType Leaf) 'The timeout tree probe must publish its exact root and descendant PIDs.'
        $ownedProcessIds = @(([IO.File]::ReadAllText($pidPath)).Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [int]$_ })
        Assert-Probe ($ownedProcessIds.Count -eq 2) 'The timeout tree probe must publish one root and one owned descendant PID.'
        foreach ($ownedProcessId in $ownedProcessIds) {
            Assert-Probe (Wait-ProbeProcessExit -ProcessId $ownedProcessId) "Timeout process-tree cleanup left owned PID $ownedProcessId alive."
        }
    }
    finally {
        foreach ($ownedProcessId in $ownedProcessIds) {
            $remaining = Get-Process -Id $ownedProcessId -ErrorAction SilentlyContinue
            if ($null -ne $remaining) {
                $remaining.Kill()
                [void] $remaining.WaitForExit(5000)
                $remaining.Dispose()
            }
        }
    }
}

function Invoke-DelayedDrainScenario {
    $rootIdentityPath = Join-Path $ScenarioRoot 'delayed-drain-root.json'
    $childIdentityPath = Join-Path $ScenarioRoot 'delayed-drain-child.json'
    $coordinatorIdentityPath = Join-Path $ScenarioRoot 'delayed-drain-coordinator.json'
    $childReadyPath = Join-Path $ScenarioRoot 'delayed-drain-child-ready'
    $rootExitedPath = Join-Path $ScenarioRoot 'delayed-drain-root-exited'
    $snapshotTakenPath = Join-Path $ScenarioRoot 'delayed-drain-snapshot-taken'
    $releasePath = Join-Path $ScenarioRoot 'delayed-drain-release'
    $childFinishedPath = Join-Path $ScenarioRoot 'delayed-drain-child-finished'
    $coordinatorFailurePath = Join-Path $ScenarioRoot 'delayed-drain-coordinator-failure.txt'
    $childScriptPath = Join-Path $ScenarioRoot 'delayed-drain-child.ps1'
    $rootScriptPath = Join-Path $ScenarioRoot 'delayed-drain-root.ps1'
    $coordinatorScriptPath = Join-Path $ScenarioRoot 'delayed-drain-coordinator.ps1'
    $coordinator = $null

    [IO.File]::WriteAllText(
        $childScriptPath,
        @"
param(
    [string] `$IdentityPath,
    [string] `$ReadyPath,
    [string] `$ReleasePath,
    [string] `$FinishedPath
)
`$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
`$process = Get-Process -Id `$PID -ErrorAction Stop
`$identity = [ordered]@{ pid = `$PID; processStartTimeUtc = `$process.StartTime.ToUniversalTime().ToString('O') }
`$identityTempPath = "`$IdentityPath.`$PID.tmp"
[IO.File]::WriteAllText(`$identityTempPath, (`$identity | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
[IO.File]::Move(`$identityTempPath, `$IdentityPath)
`$readyTempPath = "`$ReadyPath.`$PID.tmp"
[IO.File]::WriteAllText(`$readyTempPath, 'ready', [Text.UTF8Encoding]::new(`$false))
[IO.File]::Move(`$readyTempPath, `$ReadyPath)
`$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
while (-not [IO.File]::Exists(`$ReleasePath) -and [DateTimeOffset]::UtcNow -lt `$deadline) {
    Start-Sleep -Milliseconds 25
}
if (-not [IO.File]::Exists(`$ReleasePath)) { throw 'The delayed drain release edge was not observed.' }
[Console]::Out.Write('late-stdout')
[Console]::Error.Write('late-stderr')
`$finishedTempPath = "`$FinishedPath.`$PID.tmp"
[IO.File]::WriteAllText(`$finishedTempPath, 'finished', [Text.UTF8Encoding]::new(`$false))
[IO.File]::Move(`$finishedTempPath, `$FinishedPath)
"@,
        [Text.UTF8Encoding]::new($false)
    )
    [IO.File]::WriteAllText(
        $rootScriptPath,
        @"
param(
    [string] `$ChildScriptPath,
    [string] `$RootIdentityPath,
    [string] `$ChildIdentityPath,
    [string] `$ChildReadyPath,
    [string] `$ReleasePath,
    [string] `$ChildFinishedPath
)
`$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
`$process = Get-Process -Id `$PID -ErrorAction Stop
`$identity = [ordered]@{ pid = `$PID; processStartTimeUtc = `$process.StartTime.ToUniversalTime().ToString('O') }
`$identityTempPath = "`$RootIdentityPath.`$PID.tmp"
[IO.File]::WriteAllText(`$identityTempPath, (`$identity | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
[IO.File]::Move(`$identityTempPath, `$RootIdentityPath)
`$startInfo = [Diagnostics.ProcessStartInfo]::new()
`$startInfo.FileName = (Get-Process -Id `$PID -ErrorAction Stop).Path
`$startInfo.UseShellExecute = `$false
`$startInfo.RedirectStandardOutput = `$false
`$startInfo.RedirectStandardError = `$false
foreach (`$argument in @(
    '-NoProfile', '-NonInteractive', '-File', `$ChildScriptPath,
    `$ChildIdentityPath, `$ChildReadyPath, `$ReleasePath, `$ChildFinishedPath
)) {
    [void] `$startInfo.ArgumentList.Add(`$argument)
}
`$child = [Diagnostics.Process]::Start(`$startInfo)
`$child.Dispose()
`$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
while (-not [IO.File]::Exists(`$ChildReadyPath) -and [DateTimeOffset]::UtcNow -lt `$deadline) {
    Start-Sleep -Milliseconds 25
}
if (-not [IO.File]::Exists(`$ChildReadyPath)) { throw 'The delayed drain child did not publish its ready edge.' }
"@,
        [Text.UTF8Encoding]::new($false)
    )
    [IO.File]::WriteAllText(
        $coordinatorScriptPath,
        @"
param(
    [string] `$RootIdentityPath,
    [string] `$CoordinatorIdentityPath,
    [string] `$RootExitedPath,
    [string] `$SnapshotTakenPath,
    [string] `$ReleasePath,
    [string] `$FailurePath,
    [ValidateSet('root-exit', 'snapshot')]
    [string] `$Mode
)
`$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    `$process = Get-Process -Id `$PID -ErrorAction Stop
    `$identity = [ordered]@{ pid = `$PID; processStartTimeUtc = `$process.StartTime.ToUniversalTime().ToString('O') }
    `$identityTempPath = "`$CoordinatorIdentityPath.`$PID.tmp"
    [IO.File]::WriteAllText(`$identityTempPath, (`$identity | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new(`$false))
    [IO.File]::Move(`$identityTempPath, `$CoordinatorIdentityPath)

    `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while (-not [IO.File]::Exists(`$RootIdentityPath) -and [DateTimeOffset]::UtcNow -lt `$deadline) {
        Start-Sleep -Milliseconds 25
    }
    if (-not [IO.File]::Exists(`$RootIdentityPath)) { throw 'The delayed drain root did not publish its identity.' }
    `$rootIdentity = Get-Content -LiteralPath `$RootIdentityPath -Raw | ConvertFrom-Json
    `$expectedRootStartTimeUtc = [DateTimeOffset]::Parse(
        [string] `$rootIdentity.processStartTimeUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime

    while ([DateTimeOffset]::UtcNow -lt `$deadline) {
        `$root = Get-Process -Id ([int] `$rootIdentity.pid) -ErrorAction SilentlyContinue
        if (`$null -eq `$root) { break }
        `$sameIdentity = `$root.StartTime.ToUniversalTime() -eq `$expectedRootStartTimeUtc
        `$root.Dispose()
        if (-not `$sameIdentity) { break }
        Start-Sleep -Milliseconds 25
    }
    `$remainingRoot = Get-Process -Id ([int] `$rootIdentity.pid) -ErrorAction SilentlyContinue
    if (`$null -ne `$remainingRoot) {
        `$sameIdentity = `$remainingRoot.StartTime.ToUniversalTime() -eq `$expectedRootStartTimeUtc
        `$remainingRoot.Dispose()
        if (`$sameIdentity) { throw 'The delayed drain root exit edge was not observed.' }
    }

    `$rootExitedTempPath = "`$RootExitedPath.`$PID.tmp"
    [IO.File]::WriteAllText(`$rootExitedTempPath, 'root-exited', [Text.UTF8Encoding]::new(`$false))
    [IO.File]::Move(`$rootExitedTempPath, `$RootExitedPath)
    if ([string]::Equals(`$Mode, 'snapshot', [StringComparison]::Ordinal)) {
        `$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while (-not [IO.File]::Exists(`$SnapshotTakenPath) -and [DateTimeOffset]::UtcNow -lt `$deadline) {
            Start-Sleep -Milliseconds 25
        }
        if (-not [IO.File]::Exists(`$SnapshotTakenPath)) { throw 'The wrong-path snapshot edge was not observed.' }
    }

    `$releaseTempPath = "`$ReleasePath.`$PID.tmp"
    [IO.File]::WriteAllText(`$releaseTempPath, 'release', [Text.UTF8Encoding]::new(`$false))
    [IO.File]::Move(`$releaseTempPath, `$ReleasePath)
    exit 0
}
catch {
    [IO.File]::WriteAllText(`$FailurePath, `$_.Exception.Message, [Text.UTF8Encoding]::new(`$false))
    exit 1
}
"@,
        [Text.UTF8Encoding]::new($false)
    )

    try {
        $coordinatorStartInfo = [Diagnostics.ProcessStartInfo]::new()
        $coordinatorStartInfo.FileName = (Get-Process -Id $PID -ErrorAction Stop).Path
        $coordinatorStartInfo.UseShellExecute = $false
        foreach ($argument in @(
            '-NoProfile', '-NonInteractive', '-File', $coordinatorScriptPath,
            $rootIdentityPath, $coordinatorIdentityPath, $rootExitedPath,
            $snapshotTakenPath, $releasePath, $coordinatorFailurePath, $DrainMode
        )) {
            [void] $coordinatorStartInfo.ArgumentList.Add($argument)
        }
        $coordinator = [Diagnostics.Process]::Start($coordinatorStartInfo)

        $result = Invoke-NativeCommandOutput `
            -Command (Get-Process -Id $PID).Path `
            -Arguments @(
                '-NoProfile', '-NonInteractive', '-File', $rootScriptPath, $childScriptPath,
                $rootIdentityPath, $childIdentityPath, $childReadyPath, $releasePath, $childFinishedPath
            ) `
            -WorkingDirectory $ScenarioRoot `
            -TimeoutMilliseconds 20000 `
            -Name 'native-output-delayed-drain' `
            -LogDirectory (Join-Path $ScenarioRoot 'delayed-drain-logs') `
            -Environment @{ NERV_SNAPSHOT_TAKEN_PATH = $snapshotTakenPath }

        Assert-Probe ([string]::Equals([string]$result.Stdout, 'late-stdout', [StringComparison]::Ordinal)) 'The command must wait for stdout completion after the root process exits.'
        Assert-Probe ([string]::Equals([string]$result.Stderr, 'late-stderr', [StringComparison]::Ordinal)) 'The command must wait for stderr completion after the root process exits.'
        Assert-Probe (-not $result.PartialOutput) 'Streams that complete inside the drain bound must not be marked partial.'
        Assert-Probe ([IO.File]::Exists($childReadyPath)) 'The child-ready edge must precede the root exit.'
        Assert-Probe ([IO.File]::Exists($rootExitedPath)) 'The coordinator must observe the exact root identity exit before release.'
        Assert-Probe ([IO.File]::Exists($releasePath)) 'The child output must be released only after the root exit edge.'
        Assert-Probe ([IO.File]::Exists($childFinishedPath)) 'The child must publish both late stream fragments before it exits.'
        Assert-Probe ($coordinator.WaitForExit(10000)) 'The delayed drain coordinator did not exit after publishing release.'
        $coordinatorFailure = if ([IO.File]::Exists($coordinatorFailurePath)) { [IO.File]::ReadAllText($coordinatorFailurePath) } else { '' }
        Assert-Probe ($coordinator.ExitCode -eq 0) "The delayed drain coordinator failed: $coordinatorFailure"
    }
    finally {
        foreach ($identityPath in @($rootIdentityPath, $childIdentityPath, $coordinatorIdentityPath)) {
            if (-not (Test-Path -LiteralPath $identityPath -PathType Leaf)) { continue }
            $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
            $remaining = Get-Process -Id ([int]$identity.pid) -ErrorAction SilentlyContinue
            if ($null -ne $remaining) {
                $expectedStartTimeUtc = [DateTimeOffset]::Parse(
                    [string]$identity.processStartTimeUtc,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime
                if ($remaining.StartTime.ToUniversalTime() -eq $expectedStartTimeUtc) {
                    $remaining.Kill()
                    [void] $remaining.WaitForExit(5000)
                }
                $remaining.Dispose()
            }
        }
        if ($null -ne $coordinator) {
            if (-not $coordinator.HasExited) {
                $coordinator.Kill()
                [void] $coordinator.WaitForExit(5000)
            }
            $coordinator.Dispose()
        }
    }
}

function Invoke-NonzeroScenario {
    $ordinary = Get-ProbeFailure {
        Invoke-NativeCommandOutput `
            -Command (Get-Process -Id $PID).Path `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', '[Console]::Out.Write("ordinary"); exit 7') `
            -WorkingDirectory $ScenarioRoot `
            -TimeoutMilliseconds 5000 `
            -Name 'native-output-nonzero'
    }
    Assert-Probe ($ordinary.Exception -is [InvalidOperationException]) 'A nonzero command must fail closed.'
    Assert-Probe ([int]$ordinary.Exception.Data['ExitCode'] -eq 7) 'A nonzero command must retain its original exit code in structured diagnostics.'
    Assert-Probe ($ordinary.Exception.Message.Contains('exited with 7', [StringComparison]::Ordinal)) 'A nonzero command must retain its original exit code in the failure message.'
}

function Invoke-SignalScenario {
    if ([OperatingSystem]::IsWindows()) { return }

    $signal = Get-ProbeFailure {
        Invoke-NativeCommandOutput `
            -Command '/bin/sh' `
            -Arguments @('-c', 'kill -15 $$') `
            -WorkingDirectory $ScenarioRoot `
            -TimeoutMilliseconds 5000 `
            -Name 'native-output-signal'
    }
    Assert-Probe ([int]$signal.Exception.Data['ExitCode'] -eq 143) 'A signal exit must retain its original 128 + signal exit code.'
    Assert-Probe ($signal.Exception.Message.Contains('Terminated by signal', [StringComparison]::Ordinal)) 'A signal exit must retain its signal termination diagnosis.'
    Assert-Probe ($signal.Exception.Message.Contains('SIGTERM', [StringComparison]::Ordinal)) 'A signal exit must retain its portable signal classification.'
}

function Invoke-DrainFailureScenario {
    $faultedRead = {
        param([IO.StreamReader] $Reader, [string] $StreamName)
        return [Threading.Tasks.Task]::FromException([IO.IOException]::new("$StreamName injected drain failure"))
    }
    $failure = Get-ProbeFailure {
        Invoke-NativeCommandOutput `
            -Command (Get-Process -Id $PID).Path `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'exit 0') `
            -WorkingDirectory $ScenarioRoot `
            -TimeoutMilliseconds 5000 `
            -Name 'native-output-drain-failure' `
            -StreamReadTaskAction $faultedRead
    }
    Assert-Probe ($failure.Exception.Message.Contains('redirected stream drain failed', [StringComparison]::Ordinal)) 'A redirected stream drain failure must fail closed.'
    Assert-Probe ($failure.Exception.Message.Contains('stdout injected drain failure', [StringComparison]::Ordinal)) 'A redirected stream drain failure must retain the failing stream diagnosis.'
}

function Invoke-FinallyCleanupScenario {
    $pidPath = Join-Path $ScenarioRoot 'finally-cleanup-pid.txt'
    $ownedProcessId = $null
    $readSetupFailure = {
        param([IO.StreamReader] $Reader, [string] $StreamName)
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
        while (-not [IO.File]::Exists($pidPath) -and [DateTimeOffset]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 25
        }
        if (-not [IO.File]::Exists($pidPath)) { throw 'The cleanup probe did not publish its PID.' }
        throw "$StreamName injected stream setup failure"
    }

    try {
        $failure = Get-ProbeFailure {
            Invoke-NativeCommandOutput `
                -Command (Get-Process -Id $PID).Path `
                -Arguments @('-NoProfile', '-NonInteractive', '-Command', '[IO.File]::WriteAllText($env:NERV_PID_PATH, "$PID"); Start-Sleep -Seconds 20') `
                -WorkingDirectory $ScenarioRoot `
                -TimeoutMilliseconds 5000 `
                -Name 'native-output-finally-cleanup' `
                -Environment @{ NERV_PID_PATH = $pidPath } `
                -StreamReadTaskAction $readSetupFailure
        }
        Assert-Probe ($failure.Exception.Message.Contains('injected stream setup failure', [StringComparison]::Ordinal)) "The original stream setup failure must remain visible; observed '$($failure.Exception.Message)'."
        $ownedProcessId = [int][IO.File]::ReadAllText($pidPath)
        Assert-Probe (Wait-ProbeProcessExit -ProcessId $ownedProcessId) "Finally cleanup left owned PID $ownedProcessId alive."
    }
    finally {
        if ($null -ne $ownedProcessId) {
            $remaining = Get-Process -Id $ownedProcessId -ErrorAction SilentlyContinue
            if ($null -ne $remaining) {
                $remaining.Kill()
                [void] $remaining.WaitForExit(5000)
                $remaining.Dispose()
            }
        }
    }
}

try {
    [IO.Directory]::CreateDirectory($ScenarioRoot) | Out-Null
    switch ($Scenario) {
        'normal' { Invoke-NormalScenario }
        'timeout' { Invoke-TimeoutScenario }
        'timeout-tree' { Invoke-TimeoutTreeScenario }
        'delayed-drain' { Invoke-DelayedDrainScenario }
        'nonzero' { Invoke-NonzeroScenario }
        'signal' { Invoke-SignalScenario }
        'drain-failure' { Invoke-DrainFailureScenario }
        'finally-cleanup' { Invoke-FinallyCleanupScenario }
        default { throw "Unknown contract scenario '$Scenario'." }
    }
    Write-Host "CONTRACT-PASS: $Scenario"
    exit 0
}
catch {
    Write-Error "CONTRACT-FAILURE: $Scenario`: $($_.Exception.Message)"
    exit 1
}
'@

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $probePath = Join-Path $temporaryRoot 'native-output-contract-probe.ps1'
    [IO.File]::WriteAllText($probePath, $probeScript, [Text.UTF8Encoding]::new($false))

    foreach ($scenario in @('normal', 'timeout', 'timeout-tree', 'delayed-drain', 'nonzero', 'signal', 'drain-failure', 'finally-cleanup')) {
        $result = Invoke-ContractProbe `
            -ProbePath $probePath `
            -ProbeLibraryPath $libraryPath `
            -Scenario $scenario `
            -ScenarioRoot (Join-Path $temporaryRoot "production-$scenario")
        Assert-Contract ($result.ExitCode -eq 0) "Production contract scenario '$scenario' failed: $($result.Output)"
        Assert-Contract ($result.Output.Contains("CONTRACT-PASS: $scenario", [StringComparison]::Ordinal)) "Production contract scenario '$scenario' did not emit its pass marker."
        if (-not [OperatingSystem]::IsWindows() -and [string]::Equals($scenario, 'signal', [StringComparison]::Ordinal)) {
            Assert-Contract ($result.Output.Contains('NERV-SIGNAL-EXIT', [StringComparison]::Ordinal)) 'The signal scenario must emit the inheritable NERV-SIGNAL-EXIT marker.'
        }
    }

    $mutations = @(
        [pscustomobject]@{
            Name = 'round-milliseconds-to-seconds'
            Scenario = 'timeout'
            Anchor = @'
    $effectiveTimeoutMilliseconds = if ($usesMillisecondBudget) {
        $TimeoutMilliseconds
'@
            Replacement = @'
    $effectiveTimeoutMilliseconds = if ($usesMillisecondBudget) {
        [int] ([Math]::Ceiling($TimeoutMilliseconds / 1000.0) * 1000)
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'delete-timeout-failure-close'
            Scenario = 'timeout'
            Anchor = '        if (-not $process.WaitForExit($effectiveTimeoutMilliseconds)) {'
            Replacement = "        [void] `$process.WaitForExit()`n        if (`$false) {"
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'delete-nonzero-failure-close'
            Scenario = 'nonzero'
            Anchor = @'
        if ($exitCode -ne 0) {
            $safeOutput = Protect-ScriptAutomationText
'@
            Replacement = @'
        if ($false) {
            $safeOutput = Protect-ScriptAutomationText
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'delete-signal-classification'
            Scenario = 'signal'
            Anchor = @'
    if ([OperatingSystem]::IsWindows()) { return $null }
    if ($ExitCode -le 128 -or $ExitCode -gt 192) { return $null }
'@
            Replacement = @'
    if ([OperatingSystem]::IsWindows()) { return $null }
    if ($ExitCode -le 128 -or $ExitCode -gt 192) { return $null }
    return $null
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'ignore-redirected-stream-drain'
            Scenario = 'normal'
            Anchor = @'
        $drain = Complete-ScriptAutomationRedirectedStreamDrain `
            -Process $process `
            -StdoutTask $stdoutTask `
            -StderrTask $stderrTask `
            -Name $Name `
            -LogDirectory $LogDirectory `
            -StdoutCapture $stdoutCapture `
            -StderrCapture $stderrCapture `
            -SensitiveValues $SensitiveValues
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        $stdout = $drain.Stdout
'@
            Replacement = @'
        $drain = [pscustomobject]@{
            Stdout = ''
            Stderr = ''
            TimedOut = $false
            UnfinishedStreams = @()
            DrainErrors = @()
            LogDirectory = $LogDirectory
        }
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        $stdout = $drain.Stdout
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'ignore-redirected-stream-completion'
            Scenario = 'delayed-drain'
            DrainMode = 'snapshot'
            Anchor = @'
        $drain = Complete-ScriptAutomationRedirectedStreamDrain `
            -Process $process `
            -StdoutTask $stdoutTask `
            -StderrTask $stderrTask `
            -Name $Name `
            -LogDirectory $LogDirectory `
            -StdoutCapture $stdoutCapture `
            -StderrCapture $stderrCapture `
            -SensitiveValues $SensitiveValues
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        $stdout = $drain.Stdout
'@
            Replacement = @'
        $wrongStdout = [string] $stdoutCapture.Snapshot()
        $wrongStderr = [string] $stderrCapture.Snapshot()
        $snapshotTakenPath = [string] $Environment['NERV_SNAPSHOT_TAKEN_PATH']
        $snapshotTakenTempPath = "$snapshotTakenPath.$PID.tmp"
        [IO.File]::WriteAllText($snapshotTakenTempPath, 'snapshot-taken', [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($snapshotTakenTempPath, $snapshotTakenPath)
        $drain = [pscustomobject]@{
            Stdout = $wrongStdout
            Stderr = $wrongStderr
            TimedOut = $false
            UnfinishedStreams = @()
            DrainErrors = @()
            LogDirectory = $LogDirectory
        }
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        $stdout = $drain.Stdout
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'stop-timeout-root-only'
            Scenario = 'timeout-tree'
            Anchor = '            Stop-ProcessTree -ProcessId $process.Id -Reason "Timeout while reading output for $Command" | Out-Null'
            Replacement = '            Stop-Process -Id $process.Id -Force -ErrorAction Stop'
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'delete-timeout-log-redaction'
            Scenario = 'timeout'
            Anchor = @'
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stdout.log') -Content $drain.Stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stderr.log') -Content $drain.Stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
'@
            Replacement = @'
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stdout.log') -Content $drain.Stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stderr.log') -Content $drain.Stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams
'@
            ExpectedOccurrences = 1
        },
        [pscustomobject]@{
            Name = 'delete-finally-process-cleanup'
            Scenario = 'finally-cleanup'
            Anchor = '            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for output command $Command" | Out-Null'
            Replacement = '            [void] $process.Id'
            ExpectedOccurrences = 1
        }
    )

    foreach ($mutation in $mutations) {
        if ([OperatingSystem]::IsWindows() -and [string]::Equals($mutation.Scenario, 'signal', [StringComparison]::Ordinal)) {
            Write-Host "Mutation '$($mutation.Name)' is skipped on Windows because POSIX signal exit codes do not exist there."
            continue
        }
        if (-not [OperatingSystem]::IsLinux() -and [string]::Equals($mutation.Scenario, 'timeout-tree', [StringComparison]::Ordinal)) {
            Write-Host "Mutation '$($mutation.Name)' is skipped outside Linux because the current process-tree enumerator is platform-specific."
            continue
        }

        $mutatedLibraryPath = Write-MutatedLibrary `
            -Name $mutation.Name `
            -Anchor $mutation.Anchor `
            -Replacement $mutation.Replacement `
            -ExpectedOccurrences $mutation.ExpectedOccurrences
        $mutatedResult = Invoke-ContractProbe `
            -ProbePath $probePath `
            -ProbeLibraryPath $mutatedLibraryPath `
            -Scenario $mutation.Scenario `
            -ScenarioRoot (Join-Path $temporaryRoot "rejected-$($mutation.Name)") `
            -DrainMode $(if ($null -ne $mutation.PSObject.Properties['DrainMode']) { $mutation.DrainMode } else { 'root-exit' })
        Assert-Contract ($mutatedResult.ExitCode -ne 0) "Equivalent wrong mutation '$($mutation.Name)' survived scenario '$($mutation.Scenario)'."
        Assert-Contract ($mutatedResult.Output.Contains("CONTRACT-FAILURE: $($mutation.Scenario)", [StringComparison]::Ordinal)) "Equivalent wrong mutation '$($mutation.Name)' failed without the target contract marker: $($mutatedResult.Output)"
    }

    Write-Host 'ScriptAutomation native command output millisecond-budget contracts passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
