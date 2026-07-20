# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs script-governance fixture checks
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$checker = Join-Path $repoRoot 'scripts/check-script-governance.ps1'
$fixtures = Join-Path $repoRoot 'scripts/tests/fixtures/script-governance'
$helper = Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'

. $helper

function Invoke-GovernanceCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [int] $ExpectedExitCode
    )

    $target = Join-Path $fixtures $Name
    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $target 2>&1
    $actualExitCode = $LASTEXITCODE

    if ($actualExitCode -ne $ExpectedExitCode) {
        $output | ForEach-Object { Write-Host $_ }
        throw "Expected $Name to exit $ExpectedExitCode, got $actualExitCode."
    }
}

function Invoke-GovernanceScriptCase {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $emptyBaseline = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-empty-script-governance-baseline-$([System.Guid]::NewGuid().ToString('N')).json"
    [System.IO.File]::WriteAllText($emptyBaseline, '{"schema":1,"exemptions":[]}', [System.Text.UTF8Encoding]::new($false))

    try {
        $target = Join-Path $repoRoot $RelativePath
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $target -BaselinePath $emptyBaseline 2>&1
        $actualExitCode = $LASTEXITCODE

        if ($actualExitCode -ne 0) {
            $output | ForEach-Object { Write-Host $_ }
            throw "Expected $RelativePath to pass without baseline exemptions, got $actualExitCode."
        }
    }
    finally {
        Remove-Item -LiteralPath $emptyBaseline -Force -ErrorAction SilentlyContinue
    }
}

function Test-ExactTestProcessIdentity {
    param([Parameter(Mandatory)] [string] $IdentityPath)

    if (-not (Test-Path -LiteralPath $IdentityPath -PathType Leaf)) { return $false }
    $identity = Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json
    $process = Get-Process -Id ([int] $identity.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    try {
        $expected = [DateTimeOffset]::Parse("$($identity.processStartTimeUtc)").UtcDateTime
        $actual = $process.StartTime.ToUniversalTime()
        return [Math]::Abs(($actual - $expected).TotalMilliseconds) -lt 1
    }
    finally {
        $process.Dispose()
    }
}

function Stop-ExactTestProcessIdentity {
    param([Parameter(Mandatory)] [string] $IdentityPath)

    if (-not (Test-Path -LiteralPath $IdentityPath -PathType Leaf)) { return }
    $identity = Get-Content -LiteralPath $IdentityPath -Raw | ConvertFrom-Json
    $process = Get-Process -Id ([int] $identity.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    try {
        $expected = [DateTimeOffset]::Parse("$($identity.processStartTimeUtc)").UtcDateTime
        $actual = $process.StartTime.ToUniversalTime()
        if ([Math]::Abs(($actual - $expected).TotalMilliseconds) -ge 1) { return }
        $process.Kill()
        [void] $process.WaitForExit(10000)
    }
    finally {
        $process.Dispose()
    }
}

Invoke-GovernanceCase -Name 'allowed-check.ps1' -ExpectedExitCode 0
Invoke-GovernanceCase -Name 'allowed-multi-category.ps1' -ExpectedExitCode 0
Invoke-GovernanceCase -Name 'missing-helper.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-dotnet.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'direct-start-job.ps1' -ExpectedExitCode 1
Invoke-GovernanceCase -Name 'dynamic-invocation.ps1' -ExpectedExitCode 1

Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fifth-slice-persistence-foundation.ps1'
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fourth-slice-real-infra.ps1'

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-script-governance-$([System.Guid]::NewGuid().ToString('N'))"
try {
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'Write-Output helper-smoke') -TimeoutSeconds 15 -Name 'helper-smoke' -LogDirectory (Join-Path $smokeRoot 'helper-smoke') | Out-Null
}
finally {
    $resolvedSmokeRoot = Resolve-Path $smokeRoot -ErrorAction SilentlyContinue
    if ($resolvedSmokeRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedSmokeRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove helper smoke directory outside temp: $($resolvedSmokeRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedSmokeRoot.Path -Recurse -Force
    }
}

$failurePrecedenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-failure-precedence-$([System.Guid]::NewGuid().ToString('N'))"
$faultedStreamTaskAction = {
    param($Reader, $StreamName)
    if ($StreamName -ceq 'stdout') {
        return [System.Threading.Tasks.Task]::FromException[string](
            [InvalidOperationException]::new('token=super-secret-token simulated stdout drain failure')
        )
    }
    return [System.Threading.Tasks.Task]::FromResult[string]('')
}
try {
    [System.IO.Directory]::CreateDirectory($failurePrecedenceRoot) | Out-Null

    $withTimeoutExitFailure = $null
    try {
        Invoke-NativeCommandWithTimeout `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'exit 31') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 10 `
            -Name 'drain-precedence-with-timeout-exit' `
            -LogDirectory (Join-Path $failurePrecedenceRoot 'with-timeout-exit') `
            -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
    }
    catch { $withTimeoutExitFailure = $_ }
    if ($null -eq $withTimeoutExitFailure -or -not $withTimeoutExitFailure.Exception.Message.Contains('exited with 31')) {
        throw 'Invoke-NativeCommandWithTimeout must prioritize the native nonzero exit over a completed drain fault.'
    }
    if ($withTimeoutExitFailure.Exception.Message.Contains('super-secret-token')) {
        throw 'Invoke-NativeCommandWithTimeout nonzero diagnostics must redact drain secrets.'
    }

    $outputExitFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'exit 32') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 10 `
            -Name 'drain-precedence-output-exit' `
            -LogDirectory (Join-Path $failurePrecedenceRoot 'output-exit') `
            -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
    }
    catch { $outputExitFailure = $_ }
    if ($null -eq $outputExitFailure -or [int] $outputExitFailure.Exception.Data['ExitCode'] -ne 32) {
        throw 'Invoke-NativeCommandOutput must preserve structured native exit data over a completed drain fault.'
    }
    if ($outputExitFailure.Exception.Message.Contains('super-secret-token')) {
        throw 'Invoke-NativeCommandOutput nonzero diagnostics must redact drain secrets.'
    }

    $withTimeoutTimeoutFailure = $null
    try {
        Invoke-NativeCommandWithTimeout `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 5') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 1 `
            -Name 'drain-precedence-with-timeout-timeout' `
            -LogDirectory (Join-Path $failurePrecedenceRoot 'with-timeout-timeout') `
            -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
    }
    catch { $withTimeoutTimeoutFailure = $_ }
    if ($null -eq $withTimeoutTimeoutFailure -or -not $withTimeoutTimeoutFailure.Exception.Message.Contains('timed out after 1 seconds')) {
        throw 'Invoke-NativeCommandWithTimeout must prioritize its timeout over a completed drain fault.'
    }
    if ($withTimeoutTimeoutFailure.Exception.Message.Contains('super-secret-token')) {
        throw 'Invoke-NativeCommandWithTimeout timeout diagnostics must redact drain secrets.'
    }

    $outputTimeoutFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 5') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 1 `
            -Name 'drain-precedence-output-timeout' `
            -LogDirectory (Join-Path $failurePrecedenceRoot 'output-timeout') `
            -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
    }
    catch { $outputTimeoutFailure = $_ }
    if ($null -eq $outputTimeoutFailure -or -not $outputTimeoutFailure.Exception.Message.Contains('timed out after 1 seconds')) {
        throw 'Invoke-NativeCommandOutput must prioritize its timeout over a completed drain fault.'
    }
    if ($outputTimeoutFailure.Exception.Message.Contains('super-secret-token')) {
        throw 'Invoke-NativeCommandOutput timeout diagnostics must redact drain secrets.'
    }

    foreach ($zeroExitCase in @(
        [pscustomobject]@{
            Name = 'Invoke-NativeCommandWithTimeout'
            Action = {
                Invoke-NativeCommandWithTimeout `
                    -Command 'pwsh' `
                    -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'exit 0') `
                    -WorkingDirectory $repoRoot `
                    -TimeoutSeconds 10 `
                    -Name 'drain-precedence-with-timeout-zero' `
                    -LogDirectory (Join-Path $failurePrecedenceRoot 'with-timeout-zero') `
                    -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
            }
        },
        [pscustomobject]@{
            Name = 'Invoke-NativeCommandOutput'
            Action = {
                Invoke-NativeCommandOutput `
                    -Command 'pwsh' `
                    -Arguments @('-NoProfile', '-NonInteractive', '-Command', 'exit 0') `
                    -WorkingDirectory $repoRoot `
                    -TimeoutSeconds 10 `
                    -Name 'drain-precedence-output-zero' `
                    -LogDirectory (Join-Path $failurePrecedenceRoot 'output-zero') `
                    -StreamReadTaskAction $faultedStreamTaskAction | Out-Null
            }
        }
    )) {
        $drainOnlyFailure = $null
        try { & $zeroExitCase.Action }
        catch { $drainOnlyFailure = $_ }
        if ($null -eq $drainOnlyFailure -or -not $drainOnlyFailure.Exception.Message.Contains('redirected stream drain failed')) {
            throw "$($zeroExitCase.Name) must surface a drain failure when the root exits zero."
        }
        if ($drainOnlyFailure.Exception.Message.Contains('super-secret-token')) {
            throw "$($zeroExitCase.Name) drain-only diagnostics must redact secrets."
        }
        if (-not $drainOnlyFailure.Exception.Message.Contains('token=<redacted>')) {
            throw "$($zeroExitCase.Name) drain-only diagnostics must retain a useful redacted marker."
        }
    }
}
finally {
    $resolvedFailurePrecedenceRoot = Resolve-Path $failurePrecedenceRoot -ErrorAction SilentlyContinue
    if ($resolvedFailurePrecedenceRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedFailurePrecedenceRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove failure-precedence fixture outside temp: $($resolvedFailurePrecedenceRoot.Path)"
        }
        Remove-Item -LiteralPath $resolvedFailurePrecedenceRoot.Path -Recurse -Force
    }
}

$partialEofResult = Invoke-NativeCommandOutput `
    -Command 'pwsh' `
    -Arguments @(
        '-NoProfile',
        '-NonInteractive',
        '-Command',
        "[Console]::Out.Write('stdout-final-partial'); [Console]::Error.Write('stderr-final-partial')"
    ) `
    -WorkingDirectory $repoRoot `
    -TimeoutSeconds 10 `
    -Name 'partial-eof-output'
if ($partialEofResult.Stdout -cne 'stdout-final-partial') {
    throw "Invoke-NativeCommandOutput changed final partial stdout at normal EOF: '$($partialEofResult.Stdout)'."
}
if ($partialEofResult.Stderr -cne 'stderr-final-partial') {
    throw "Invoke-NativeCommandOutput changed final partial stderr at normal EOF: '$($partialEofResult.Stderr)'."
}
if ($partialEofResult.PartialOutput -or @($partialEofResult.UnfinishedStreams).Count -ne 0) {
    throw 'Invoke-NativeCommandOutput must identify normal EOF output as complete.'
}
$aspireOutputDefinition = (Get-Command Invoke-AspireOutput -ErrorAction Stop).Definition
if (-not $aspireOutputDefinition.Contains('[switch] $AllowPartialOutput') -or -not $aspireOutputDefinition.Contains('-AllowPartialOutput:$AllowPartialOutput')) {
    throw 'Invoke-AspireOutput must explicitly forward the narrow partial-output opt-in.'
}

$streamDrainRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-stream-drain-$([System.Guid]::NewGuid().ToString('N'))"
$streamDrainOutputIdentity = Join-Path $streamDrainRoot 'output-child.json'
$streamDrainOptInIdentity = Join-Path $streamDrainRoot 'output-opt-in-child.json'
$streamDrainNonzeroIdentity = Join-Path $streamDrainRoot 'output-nonzero-child.json'
$streamDrainTimeoutIdentity = Join-Path $streamDrainRoot 'timeout-child.json'
try {
    [System.IO.Directory]::CreateDirectory($streamDrainRoot) | Out-Null
    $streamDrainChild = Join-Path $streamDrainRoot 'inherited-handle-child.ps1'
    $streamDrainLauncher = Join-Path $streamDrainRoot 'inherited-handle-launcher.ps1'
    $streamDrainParent = Join-Path $streamDrainRoot 'inherited-handle-parent.ps1'
    [System.IO.File]::WriteAllText(
        $streamDrainChild,
        @'
param($IdentityPath, $SleepSeconds)
$process = Get-Process -Id $PID -ErrorAction Stop
$identity = [ordered]@{ pid = $PID; processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O') }
[System.IO.File]::WriteAllText($IdentityPath, ($identity | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
[Console]::Out.WriteLine('inherited child stdout')
[Console]::Error.WriteLine('inherited child stderr')
Start-Sleep -Seconds $SleepSeconds
'@,
        [System.Text.UTF8Encoding]::new($false)
    )
    [System.IO.File]::WriteAllText(
        $streamDrainLauncher,
        @'
param($ChildScript, $IdentityPath, $SleepSeconds)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = (Get-Process -Id $PID -ErrorAction Stop).Path
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $false
$startInfo.RedirectStandardError = $false
foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $ChildScript, $IdentityPath, "$SleepSeconds")) {
    [void] $startInfo.ArgumentList.Add($argument)
}
$child = [System.Diagnostics.Process]::Start($startInfo)
$child.Dispose()
'@,
        [System.Text.UTF8Encoding]::new($false)
    )
    [System.IO.File]::WriteAllText(
        $streamDrainParent,
        @'
param($LauncherScript, $ChildScript, $IdentityPath, $ChildSleepSeconds, $ParentSleepSeconds, $RootExitCode = 0)
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = (Get-Process -Id $PID -ErrorAction Stop).Path
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $false
$startInfo.RedirectStandardError = $false
foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $LauncherScript, $ChildScript, $IdentityPath, "$ChildSleepSeconds")) {
    [void] $startInfo.ArgumentList.Add($argument)
}
$launcher = [System.Diagnostics.Process]::Start($startInfo)
[void] $launcher.WaitForExit(5000)
$launcher.Dispose()
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
while (-not [System.IO.File]::Exists($IdentityPath) -and [DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 25
}
if (-not [System.IO.File]::Exists($IdentityPath)) { throw 'Inherited-handle child did not publish identity.' }
[Console]::Out.WriteLine('inherited parent stdout')
[Console]::Error.WriteLine('inherited parent stderr')
[Console]::Out.Write('inherited parent stdout partial')
[Console]::Error.Write('inherited parent stderr partial token=partial-output-secret')
if ([int] $ParentSleepSeconds -gt 0) { Start-Sleep -Seconds $ParentSleepSeconds }
if ([int] $RootExitCode -ne 0) { exit ([int] $RootExitCode) }
'@,
        [System.Text.UTF8Encoding]::new($false)
    )

    $script:ScriptAutomationStreamDrainTimeoutMilliseconds = 500
    $defaultPartialStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $defaultPartialFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-File', $streamDrainParent, $streamDrainLauncher, $streamDrainChild, $streamDrainOutputIdentity, '30', '0') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 10 `
            -Name 'stream-drain-output-default' `
            -LogDirectory (Join-Path $streamDrainRoot 'output-default-logs') | Out-Null
    }
    catch { $defaultPartialFailure = $_ }
    $defaultPartialStopwatch.Stop()
    if ($null -eq $defaultPartialFailure -or -not [bool] $defaultPartialFailure.Exception.Data['PartialOutput']) {
        throw 'Invoke-NativeCommandOutput must reject partial redirected output by default with structured failure data.'
    }
    if (@($defaultPartialFailure.Exception.Data['UnfinishedStreams']).Count -eq 0) {
        throw 'Partial-output failure must identify its unfinished redirected streams.'
    }
    if ($defaultPartialStopwatch.Elapsed.TotalSeconds -gt 15) {
        throw "Invoke-NativeCommandOutput default rejection waited for an inherited handle: $($defaultPartialStopwatch.Elapsed)."
    }
    foreach ($logName in @('stdout.log', 'stderr.log')) {
        $logPath = Join-Path $streamDrainRoot "output-default-logs/$logName"
        $logText = [System.IO.File]::ReadAllText($logPath)
        if (-not $logText.Contains('[NERV-IIP PARTIAL OUTPUT:')) {
            throw "Partial output diagnostic '$logName' must contain an explicit truncation marker."
        }
        if (-not $logText.Contains('inherited parent')) {
            throw "Partial output diagnostic '$logName' discarded the captured root prefix."
        }
        if ($logText.Contains('partial-output-secret') -or ($logName -ceq 'stderr.log' -and -not $logText.Contains('token=<redacted>'))) {
            throw "Partial output diagnostic '$logName' did not redact captured output before publishing the truncation marker."
        }
    }
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainOutputIdentity
    if (Test-ExactTestProcessIdentity -IdentityPath $streamDrainOutputIdentity) {
        throw 'Default partial-output fixture exact child cleanup did not complete.'
    }

    $outputStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $outputResult = Invoke-NativeCommandOutput `
        -Command 'pwsh' `
        -Arguments @('-NoProfile', '-NonInteractive', '-File', $streamDrainParent, $streamDrainLauncher, $streamDrainChild, $streamDrainOptInIdentity, '30', '0') `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 10 `
        -Name 'stream-drain-output' `
        -LogDirectory (Join-Path $streamDrainRoot 'output-logs') `
        -AllowPartialOutput
    $outputStopwatch.Stop()
    if ($outputStopwatch.Elapsed.TotalSeconds -gt 15) {
        throw "Invoke-NativeCommandOutput waited for an inherited handle after root exit: $($outputStopwatch.Elapsed)."
    }
    foreach ($expected in @('inherited parent stdout', 'inherited parent stdout partial')) {
        if (-not $outputResult.Stdout.Contains($expected)) {
            throw "Invoke-NativeCommandOutput discarded root stdout received before the inherited-handle cutoff: '$expected'."
        }
    }
    foreach ($expected in @('inherited parent stderr', 'inherited parent stderr partial')) {
        if (-not $outputResult.Stderr.Contains($expected)) {
            throw "Invoke-NativeCommandOutput discarded root stderr received before the inherited-handle cutoff: '$expected'."
        }
    }
    if (-not $outputResult.PartialOutput -or @($outputResult.UnfinishedStreams).Count -eq 0) {
        throw 'Invoke-NativeCommandOutput opt-in result must expose partial-output state and unfinished streams.'
    }
    foreach ($logName in @('stdout.log', 'stderr.log')) {
        $logPath = Join-Path $streamDrainRoot "output-logs/$logName"
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            throw "Bounded output drain must publish its '$logName' diagnostic path."
        }
        $expected = if ($logName -ceq 'stdout.log') { 'inherited parent stdout partial' } else { 'inherited parent stderr partial' }
        if (-not [System.IO.File]::ReadAllText($logPath).Contains($expected)) {
            throw "Bounded output drain '$logName' discarded root output received before cutoff: '$expected'."
        }
        if (-not [System.IO.File]::ReadAllText($logPath).Contains('[NERV-IIP PARTIAL OUTPUT:')) {
            throw "Bounded output drain '$logName' must retain the truncation marker when partial output is explicitly allowed."
        }
    }
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainOptInIdentity
    if (Test-ExactTestProcessIdentity -IdentityPath $streamDrainOptInIdentity) {
        throw 'Output drain fixture exact child cleanup did not complete.'
    }

    $partialNonzeroFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-File', $streamDrainParent, $streamDrainLauncher, $streamDrainChild, $streamDrainNonzeroIdentity, '30', '0', '33') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 10 `
            -Name 'stream-drain-output-nonzero' `
            -LogDirectory (Join-Path $streamDrainRoot 'output-nonzero-logs') | Out-Null
    }
    catch { $partialNonzeroFailure = $_ }
    if ($null -eq $partialNonzeroFailure -or [int] $partialNonzeroFailure.Exception.Data['ExitCode'] -ne 33) {
        throw 'Invoke-NativeCommandOutput must prioritize a native nonzero exit over partial-output rejection.'
    }
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainNonzeroIdentity
    if (Test-ExactTestProcessIdentity -IdentityPath $streamDrainNonzeroIdentity) {
        throw 'Partial nonzero fixture exact child cleanup did not complete.'
    }

    $timeoutStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timeoutFailure = $null
    try {
        Invoke-NativeCommandWithTimeout `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-File', $streamDrainParent, $streamDrainLauncher, $streamDrainChild, $streamDrainTimeoutIdentity, '30', '30') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 10 `
            -Name 'stream-drain-timeout' `
            -LogDirectory (Join-Path $streamDrainRoot 'timeout-logs') | Out-Null
    }
    catch { $timeoutFailure = $_ }
    $timeoutStopwatch.Stop()
    if ($null -eq $timeoutFailure -or -not $timeoutFailure.Exception.Message.Contains('timed out after 10 seconds')) {
        throw 'Invoke-NativeCommandWithTimeout must preserve its timeout failure.'
    }
    if ($timeoutStopwatch.Elapsed.TotalSeconds -gt 25) {
        throw "Invoke-NativeCommandWithTimeout waited for an inherited handle after timeout: $($timeoutStopwatch.Elapsed)."
    }
    foreach ($logName in @('stdout.log', 'stderr.log')) {
        $logPath = Join-Path $streamDrainRoot "timeout-logs/$logName"
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
            throw "Bounded timeout drain must publish its '$logName' diagnostic path."
        }
        $expected = if ($logName -ceq 'stdout.log') { 'inherited parent stdout partial' } else { 'inherited parent stderr partial' }
        if (-not [System.IO.File]::ReadAllText($logPath).Contains($expected)) {
            throw "Bounded timeout drain '$logName' discarded root output received before cutoff: '$expected'."
        }
        if (-not [System.IO.File]::ReadAllText($logPath).Contains('[NERV-IIP PARTIAL OUTPUT:')) {
            throw "Bounded timeout drain '$logName' must contain an explicit truncation marker."
        }
    }
    if (-not (Test-Path -LiteralPath $streamDrainTimeoutIdentity -PathType Leaf)) {
        throw 'Timeout drain fixture did not publish the inherited child identity before root timeout.'
    }
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainTimeoutIdentity
    if (Test-ExactTestProcessIdentity -IdentityPath $streamDrainTimeoutIdentity) {
        throw 'Timeout drain fixture exact child cleanup did not complete.'
    }
}
finally {
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainOutputIdentity
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainOptInIdentity
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainNonzeroIdentity
    Stop-ExactTestProcessIdentity -IdentityPath $streamDrainTimeoutIdentity
    $resolvedStreamDrainRoot = Resolve-Path $streamDrainRoot -ErrorAction SilentlyContinue
    if ($resolvedStreamDrainRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedStreamDrainRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove stream-drain fixture outside temp: $($resolvedStreamDrainRoot.Path)"
        }
        Remove-Item -LiteralPath $resolvedStreamDrainRoot.Path -Recurse -Force
    }
}

$interactiveResult = Invoke-NativeCommandInteractive -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -Name 'interactive-exit-code-smoke'
if ($interactiveResult.ExitCode -ne 7) {
    throw "Expected interactive helper to return ExitCode 7, got $($interactiveResult.ExitCode)."
}

$redactionRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-log-redaction-$([System.Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Force -Path $redactionRoot | Out-Null
    $redactionLog = Join-Path $redactionRoot 'redaction.log'
    [System.IO.File]::WriteAllLines(
        $redactionLog,
        @(
            'normal line before secret',
            'token=super-secret-token',
            'normal line after secret'
        ),
        [System.Text.UTF8Encoding]::new($false)
    )

    Protect-ScriptAutomationLogFile -Path $redactionLog
    $redactedLines = [System.IO.File]::ReadAllLines($redactionLog)
    $redactedText = [string]::Join("`n", $redactedLines)

    foreach ($expected in @('normal line before secret', 'token=<redacted>', 'normal line after secret')) {
        if (-not $redactedText.Contains($expected)) {
            throw "Expected redacted log to contain '$expected'. Output: $redactedText"
        }
    }

    if ($redactedText.Contains('super-secret-token')) {
        throw "Expected redacted log to remove secret token. Output: $redactedText"
    }

    $helperContent = Get-Content -Path $helper -Raw
    $parseErrors = $null
    $helperAst = [System.Management.Automation.Language.Parser]::ParseInput($helperContent, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        throw "Failed to parse ScriptAutomation helper: $($parseErrors[0].Message)"
    }

    $protectLogFileAst = $helperAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Protect-ScriptAutomationLogFile'
    }, $true)
    if (-not $protectLogFileAst) {
        throw 'Could not find Protect-ScriptAutomationLogFile implementation.'
    }

    if ($protectLogFileAst.Extent.Text -match 'Get-Content\s+\$Path\s+-Raw') {
        throw 'Protect-ScriptAutomationLogFile must stream log redaction instead of using Get-Content -Raw.'
    }
}
finally {
    $resolvedRedactionRoot = Resolve-Path $redactionRoot -ErrorAction SilentlyContinue
    if ($resolvedRedactionRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedRedactionRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove log redaction directory outside temp: $($resolvedRedactionRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedRedactionRoot.Path -Recurse -Force
    }
}

$idempotentRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-background-idempotent-$([System.Guid]::NewGuid().ToString('N'))"
$idempotentBackground = $null
try {
    $idempotentBackground = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') -Name 'background-idempotent-stop-smoke' -LogDirectory (Join-Path $idempotentRoot 'background-idempotent-stop-smoke')

    $firstStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $idempotentBackground.Stop 'first'
    $firstStopwatch.Stop()

    $secondStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $idempotentBackground.Stop 'second'
    $secondStopwatch.Stop()

    foreach ($measurement in @(
        [pscustomobject]@{ Name = 'first'; Duration = $firstStopwatch.Elapsed },
        [pscustomobject]@{ Name = 'second'; Duration = $secondStopwatch.Elapsed }
    )) {
        if ($measurement.Duration.TotalSeconds -gt 5) {
            throw "Expected $($measurement.Name) background Stop call to return quickly, took $($measurement.Duration)."
        }
    }

    $idempotentBackground = $null
}
finally {
    if ($idempotentBackground) {
        & $idempotentBackground.Stop 'idempotent stop final cleanup'
    }

    $resolvedIdempotentRoot = Resolve-Path $idempotentRoot -ErrorAction SilentlyContinue
    if ($resolvedIdempotentRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedIdempotentRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove idempotent smoke directory outside temp: $($resolvedIdempotentRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedIdempotentRoot.Path -Recurse -Force
    }
}

$backgroundRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-background-arguments-$([System.Guid]::NewGuid().ToString('N'))"
$backgroundScript = Join-Path $backgroundRoot 'print-arguments.ps1'
$background = $null
try {
    New-Item -ItemType Directory -Force -Path $backgroundRoot | Out-Null
    [System.IO.File]::WriteAllText($backgroundScript, 'Write-Output "count=$($args.Count)"; Write-Output "arg0=$($args[0])"; Write-Output "arg1=$($args[1])"', [System.Text.UTF8Encoding]::new($false))
    $background = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-File', $backgroundScript, 'a b', 'a "quoted" b') -Name 'background-argument-smoke' -LogDirectory (Join-Path $backgroundRoot 'background-argument-smoke')

    if (-not $background.Process.WaitForExit(15000)) {
        throw 'Background argument smoke process did not exit in time.'
    }

    & $background.Stop 'Background argument smoke cleanup'
    $stdout = Get-Content -Path $background.StdoutPath -Raw
    $background = $null

    foreach ($expected in @('count=2', 'arg0=a b', 'arg1=a "quoted" b')) {
        if (-not $stdout.Contains($expected)) {
            throw "Expected background stdout to contain '$expected'. Output: $stdout"
        }
    }
}
finally {
    if ($background) {
        & $background.Stop 'Background argument smoke final cleanup'
    }

    $resolvedBackgroundRoot = Resolve-Path $backgroundRoot -ErrorAction SilentlyContinue
    if ($resolvedBackgroundRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedBackgroundRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove background smoke directory outside temp: $($resolvedBackgroundRoot.Path)"
        }

        Remove-Item -LiteralPath $resolvedBackgroundRoot.Path -Recurse -Force
    }
}

$detachedRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-detached-$([System.Guid]::NewGuid().ToString('N'))"
try {
    [System.IO.Directory]::CreateDirectory($detachedRoot) | Out-Null
    $detachedChild = Join-Path $detachedRoot 'detached child.ps1'
    $detachedLauncher = Join-Path $detachedRoot 'launcher.ps1'
    $detachedMarker = Join-Path $detachedRoot 'completion marker.txt'
    $detachedIdentity = Join-Path $detachedRoot 'identity.json'
    $detachedStdout = Join-Path $detachedRoot 'stdout.log'
    $detachedStderr = Join-Path $detachedRoot 'stderr.log'
    [System.IO.File]::WriteAllText(
        $detachedChild,
        'param($Marker,$First,$Second); Start-Sleep -Seconds 2; [IO.File]::WriteAllText($Marker,"$First|$Second"); Write-Output completed',
        [System.Text.UTF8Encoding]::new($false)
    )
    $quote = { param($Value) "'$($Value.Replace("'", "''"))'" }
    $launcherText = @"
. $(& $quote $helper)
`$identity = Start-DetachedManagedProcess -Command 'pwsh' -Arguments @('-NoProfile','-File',$(& $quote $detachedChild),$(& $quote $detachedMarker),'a b','a "quoted" b') -WorkingDirectory $(& $quote $detachedRoot) -StdoutPath $(& $quote $detachedStdout) -StderrPath $(& $quote $detachedStderr)
if (`$identity.Pid -le 0 -or [string]::IsNullOrWhiteSpace("`$(`$identity.ProcessStartTimeUtc)")) { throw 'Detached identity missing.' }
[IO.File]::WriteAllText($(& $quote $detachedIdentity), (`$identity | ConvertTo-Json -Compress))
"@
    [System.IO.File]::WriteAllText($detachedLauncher, $launcherText, [System.Text.UTF8Encoding]::new($false))
    & pwsh -NoProfile -File $detachedLauncher
    if ($LASTEXITCODE -ne 0) { throw "Detached launcher failed with exit $LASTEXITCODE." }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $detachedMarker) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $detachedMarker)) { throw 'Detached child did not survive its launcher process.' }
    $markerText = [System.IO.File]::ReadAllText($detachedMarker)
    if ($markerText -ne 'a b|a "quoted" b') { throw "Detached arguments were corrupted: $markerText" }
    $identity = Get-Content -LiteralPath $detachedIdentity -Raw | ConvertFrom-Json
    Wait-Process -Id ([int] $identity.Pid) -Timeout 10 -ErrorAction SilentlyContinue
}
finally {
    if (Test-Path -LiteralPath $detachedIdentity) {
        $identity = Get-Content -LiteralPath $detachedIdentity -Raw | ConvertFrom-Json
        $process = Get-Process -Id ([int] $identity.Pid) -ErrorAction SilentlyContinue
        if ($process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    $resolvedDetachedRoot = Resolve-Path $detachedRoot -ErrorAction SilentlyContinue
    if ($resolvedDetachedRoot) {
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedDetachedRoot.Path.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove detached smoke directory outside temp: $($resolvedDetachedRoot.Path)"
        }
        Remove-Item -LiteralPath $resolvedDetachedRoot.Path -Recurse -Force
    }
}

Write-Host 'Script governance fixture tests passed.'
