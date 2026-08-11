# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the backend test determinism verifier against a disposable stubbed .NET harness
#   Writes:
#     - Temporary harness and evidence files under the operating-system temp directory
#     - artifacts/script-logs/backend-test-determinism-verifier-test-*/**
#   Cleanup:
#     - Removes the disposable harness, evidence, and governed command logs created by this test
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifier = Join-Path $repoRoot 'scripts/verify-backend-test-determinism.ps1'
$caseId = [Guid]::NewGuid().ToString('N')
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-backend-test-determinism-verifier-$caseId"
$harnessRoot = Join-Path $tempRoot 'harness'
$artifactRoot = Join-Path $tempRoot 'evidence'
$capturePath = Join-Path $tempRoot 'dotnet-invocations.jsonl'
$scriptLogName = "backend-test-determinism-verifier-test-$caseId"
$scriptLogRoot = Join-Path $repoRoot "artifacts/script-logs/$scriptLogName"
$raceScriptLogRoot = Join-Path $repoRoot "artifacts/script-logs/$scriptLogName-race"

$projects = @(
    'backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj',
    'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj',
    'backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj',
    'backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj'
)

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [AllowNull()]
        [object] $Expected,

        [AllowNull()]
        [object] $Actual,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if ((-not [string]::Equals([string]($Expected), [string]($Actual), [StringComparison]::Ordinal))) {
        throw "$Message Expected='$Expected' Actual='$Actual'."
    }
}

$earlySettingsIndex = [Array]::IndexOf([object[]] @('--no-build', '--settings', 'early.runsettings'), '--settings')
Assert-Equal -Expected 1 -Actual $earlySettingsIndex -Message 'Array.IndexOf must find --settings before index 4.'

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function New-StubbedHarness {
    if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
        throw "Backend test determinism verifier must exist: $verifier"
    }

    $harnessScripts = Join-Path $harnessRoot 'scripts'
    $harnessLibrary = Join-Path $harnessScripts 'lib'
    [System.IO.Directory]::CreateDirectory($harnessLibrary) | Out-Null
    $harnessVerifier = Join-Path $harnessScripts 'verify-backend-test-determinism.ps1'
    Copy-Item -LiteralPath $verifier -Destination $harnessVerifier

    # The stub keeps the real governed helpers (including the atomic invocation claim) and only
    # replaces the process-launching surface, so nothing here can drift from production behaviour.
    $stubHelper = @"
Set-StrictMode -Version Latest

. '$((Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'))'

function Write-Diagnostic {
    param([string] `$Message, [string] `$Level = 'INFO')
}

function Invoke-WithScopedEnvironment {
    param([hashtable] `$Variables, [scriptblock] `$ScriptBlock)

    `$originals = @{}
    foreach (`$key in `$Variables.Keys) {
        `$originals[`$key] = [pscustomobject]@{
            HadValue = Test-Path "Env:`$key"
            Value = [Environment]::GetEnvironmentVariable(`$key, 'Process')
        }
    }

    try {
        foreach (`$key in `$Variables.Keys) {
            Set-Item "Env:`$key" `$Variables[`$key]
        }
        & `$ScriptBlock
    }
    finally {
        foreach (`$key in `$originals.Keys) {
            if (`$originals[`$key].HadValue) {
                Set-Item "Env:`$key" `$originals[`$key].Value
            }
            else {
                Remove-Item "Env:`$key" -ErrorAction SilentlyContinue
            }
        }
    }
}

function Invoke-DotNet {
    param(
        [string[]] `$Arguments,
        [string] `$WorkingDirectory,
        [int] `$TimeoutSeconds = 600,
        [string] `$Name = 'dotnet',
        [int[]] `$SensitiveArgumentIndexes = @()
    )

    `$record = [ordered]@{
        arguments = @(`$Arguments)
        seed = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_ORDER_SEED', 'Process')
        name = `$Name
        timeoutSeconds = `$TimeoutSeconds
    }
    `$line = `$record | ConvertTo-Json -Depth 5 -Compress
    [System.IO.File]::AppendAllText(
        `$env:NERV_MAN662_CAPTURE_PATH,
        "`$line`$([Environment]::NewLine)",
        [System.Text.UTF8Encoding]::new(`$false))

    if (`$Arguments.Count -ge 2 -and
        `$Arguments[0] -ceq 'test' -and
        `$Arguments[1] -ceq `$env:NERV_MAN662_FAIL_PROJECT -and
        `$record.seed -ceq `$env:NERV_MAN662_FAIL_SEED) {
        throw "Command 'dotnet' exited with 23 after 00:00:00.001. Logs: stub"
    }

    `$stdoutRoot = Join-Path ([System.IO.Path]::GetDirectoryName(`$env:NERV_MAN662_CAPTURE_PATH)) ('stdout-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory(`$stdoutRoot) | Out-Null
    `$stdoutPath = Join-Path `$stdoutRoot 'stdout.log'
    `$summary = if (`$Arguments.Count -ge 2 -and
        `$Arguments[1] -ceq `$env:NERV_MAN662_DRIFT_PROJECT -and
        `$record.seed -ceq `$env:NERV_MAN662_DRIFT_SEED) {
        'Passed!  - Failed:     0, Passed:     7, Skipped:     3, Total:    10'
    }
    else {
        'Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10'
    }
    [System.IO.File]::WriteAllText(`$stdoutPath, `$summary, [System.Text.UTF8Encoding]::new(`$false))

    return [pscustomobject]@{
        ExitCode = 0
        Duration = [TimeSpan]::FromMilliseconds(1)
        StdoutPath = `$stdoutPath
    }
}
"@
    Write-Utf8NoBom -Path (Join-Path $harnessLibrary 'ScriptAutomation.ps1') -Content $stubHelper
}

function Invoke-VerifierCase {
    param(
        [Parameter(Mandatory)]
        [string] $InvocationId,

        [string] $FailProject,

        [string] $FailSeed,

        [string] $DriftProject,

        [string] $DriftSeed
    )

    if (Test-Path -LiteralPath $capturePath) {
        Remove-Item -LiteralPath $capturePath -Force
    }

    $variables = @{
        NERV_MAN662_CAPTURE_PATH = $capturePath
        NERV_MAN662_FAIL_PROJECT = $FailProject
        NERV_MAN662_FAIL_SEED = $FailSeed
        NERV_MAN662_DRIFT_PROJECT = $DriftProject
        NERV_MAN662_DRIFT_SEED = $DriftSeed
    }
    $exitCode = 0
    try {
        Invoke-WithScopedEnvironment -Variables $variables -ScriptBlock {
            Invoke-PwshScript `
                -ScriptPath (Join-Path $harnessRoot 'scripts/verify-backend-test-determinism.ps1') `
                -Arguments @('-ArtifactRoot', $artifactRoot, '-InvocationId', $InvocationId) `
                -WorkingDirectory $harnessRoot `
                -TimeoutSeconds 60 `
                -Name $scriptLogName | Out-Null
        }
    }
    catch {
        if ($_.Exception.Message -notmatch 'exited with (?<exitCode>\d+)') {
            throw
        }
        $exitCode = [int] $Matches['exitCode']
    }

    $records = @(
        Get-Content -LiteralPath $capturePath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json }
    )
    $testRecords = @($records | Where-Object { [string]::Equals([string]($_.arguments[0]), [string]('test'), [StringComparison]::Ordinal) })
    $summaryPath = Join-Path (Join-Path $artifactRoot $InvocationId) 'summary.json'
    Assert-True -Condition (Test-Path -LiteralPath $summaryPath -PathType Leaf) -Message "Summary was not written for '$InvocationId'."

    return [pscustomobject]@{
        ExitCode = $exitCode
        Records = $testRecords
        SummaryPath = $summaryPath
        Summary = @(Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json)
    }
}

function Assert-SixRoundContract {
    param(
        [Parameter(Mandatory)]
        [object] $Case
    )

    Assert-Equal -Expected 24 -Actual $Case.Records.Count -Message 'Verifier must invoke four target projects in each of six rounds.'
    Assert-Equal -Expected 6 -Actual $Case.Summary.Count -Message 'Summary must contain exactly six rows.'

    $expectedFields = @('elapsedMs', 'exitCode', 'profile', 'projectOrder', 'projectResults', 'run', 'seed')
    for ($index = 0; $index -lt 6; $index++) {
        $roundNumber = $index + 1
        $seed = 'man662-{0:d2}' -f $roundNumber
        $profile = if ($roundNumber % 2 -eq 1) { 'serial' } else { 'parallel' }
        $threads = if ([string]::Equals([string]($profile), [string]('serial'), [StringComparison]::Ordinal)) { '1' } else { '4' }
        $expectedOrder = @(
            for ($offset = 0; $offset -lt $projects.Count; $offset++) {
                $projects[($index + $offset) % $projects.Count]
            }
        )

        $row = $Case.Summary[$index]
        $actualFields = @(Get-NervStringsSorted -Values @($row.PSObject.Properties.Name) -Comparer ([StringComparer]::Ordinal))
        Assert-Equal -Expected ($expectedFields -join '|') -Actual ($actualFields -join '|') -Message "Round $roundNumber summary fields must stay local-reproduction-only."
        Assert-Equal -Expected $roundNumber -Actual ([int] $row.run) -Message "Round $roundNumber run number is wrong."
        Assert-Equal -Expected $seed -Actual ([string] $row.seed) -Message "Round $roundNumber seed is wrong."
        Assert-Equal -Expected $profile -Actual ([string] $row.profile) -Message "Round $roundNumber profile is wrong."
        Assert-Equal -Expected ($expectedOrder -join '|') -Actual (@($row.projectOrder) -join '|') -Message "Round $roundNumber project order is not the required rotation."
        Assert-True -Condition (((([long] $row.elapsedMs) -ge (0)))) -Message "Round $roundNumber elapsedMs must be nonnegative."

        $roundRecords = @($Case.Records | Where-Object { [string]::Equals([string]($_.seed), [string]($seed), [StringComparison]::Ordinal) })
        Assert-Equal -Expected 4 -Actual $roundRecords.Count -Message "Round $roundNumber must invoke four seeded test projects."
        Assert-Equal -Expected ($expectedOrder -join '|') -Actual (($roundRecords | ForEach-Object { $_.arguments[1] }) -join '|') -Message "Round $roundNumber invocation order is wrong."

        $settingsPaths = @(
            Get-NervStringsSorted -Values @($roundRecords |
                ForEach-Object {
                    $settingsIndex = [Array]::IndexOf([object[]] $_.arguments, '--settings')
                    Assert-True -Condition ($settingsIndex -ge 0) -Message "Round $roundNumber invocation omitted --settings."
                    [string] $_.arguments[$settingsIndex + 1]
                }) -Comparer ([StringComparer]::Ordinal) -Unique
        )
        Assert-Equal -Expected 1 -Actual $settingsPaths.Count -Message "Round $roundNumber must use one generated runsettings file."
        Assert-True -Condition (Test-Path -LiteralPath $settingsPaths[0] -PathType Leaf) -Message "Round $roundNumber runsettings file is missing."
        [xml] $settings = Get-Content -LiteralPath $settingsPaths[0] -Raw
        Assert-Equal -Expected 'true' -Actual ([string] $settings.RunSettings.xUnit.ParallelizeTestCollections) -Message "Round $roundNumber must enable collection parallelization through VSTest settings."
        Assert-Equal -Expected $threads -Actual ([string] $settings.RunSettings.xUnit.MaxParallelThreads) -Message "Round $roundNumber max thread profile is wrong."
    }
}

function Assert-VerifierPreservesCallerLocation {
    $callerRoot = Join-Path $tempRoot 'caller-location'
    $existingInvocation = Join-Path $artifactRoot 'cwd-existing'
    [System.IO.Directory]::CreateDirectory($callerRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($existingInvocation) | Out-Null

    Push-Location $callerRoot
    try {
        $before = (Get-Location).Path
        try {
            & (Join-Path $harnessRoot 'scripts/verify-backend-test-determinism.ps1') `
                -ArtifactRoot $artifactRoot `
                -InvocationId 'cwd-existing'
        }
        catch {
            Assert-True -Condition $_.Exception.Message.Contains('already exists', [StringComparison]::Ordinal) -Message "Expected existing-evidence rejection, got: $($_.Exception.Message)"
        }

        Assert-Equal -Expected $before -Actual (Get-Location).Path -Message 'Verifier must preserve the caller runspace location when it fails.'
    }
    finally {
        Pop-Location
    }
}

function Assert-ClaimIsAtomicUnderConcurrency {
    # Races the governed claim primitive itself in two real processes. The barrier lives in this
    # test-owned script, so the verifier source is never rewritten to make the race deterministic.
    $claimPath = Join-Path $tempRoot 'atomic.claim'
    $barrierRoot = Join-Path $tempRoot 'claim-barrier'
    [System.IO.Directory]::CreateDirectory($barrierRoot) | Out-Null
    $racerPath = Join-Path $tempRoot 'claim-racer.ps1'
    $racerBody = @'
param([string] $LibraryPath, [string] $ClaimPath, [string] $BarrierRoot, [string] $Participant)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $LibraryPath

[System.IO.Directory]::CreateDirectory((Join-Path $BarrierRoot $Participant)) | Out-Null
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
while (@(Get-ChildItem -LiteralPath $BarrierRoot -Directory).Count -lt 2) {
    if ($stopwatch.Elapsed -gt [TimeSpan]::FromSeconds(10)) {
        throw 'Timed out waiting for the claim race barrier.'
    }
    Start-Sleep -Milliseconds 10
}

New-ExclusiveInvocationClaim -ClaimPath $ClaimPath -InvocationId 'atomic' | Out-Null
'@
    Write-Utf8NoBom -Path $racerPath -Content $racerBody

    $library = Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'
    $processes = [System.Collections.Generic.List[object]]::new()
    try {
        foreach ($participant in @('participant-0', 'participant-1')) {
            $processes.Add((Start-ManagedBackgroundProcess `
                -Command 'pwsh' `
                -Arguments @(
                    '-NoProfile',
                    '-ExecutionPolicy',
                    'Bypass',
                    '-File',
                    $racerPath,
                    $library,
                    $claimPath,
                    $barrierRoot,
                    $participant) `
                -WorkingDirectory $repoRoot `
                -Name "$scriptLogName-race"))
        }

        foreach ($process in $processes) {
            Assert-True -Condition $process.Process.WaitForExit(60000) -Message "Claim race process $($process.ProcessId) timed out."
        }

        $exitCodes = @($processes | ForEach-Object { $_.Process.ExitCode })
        Assert-Equal -Expected 1 -Actual @($exitCodes | Where-Object { $_ -eq 0 }).Count -Message 'Exactly one process may win an invocation claim.'
        Assert-Equal -Expected 1 -Actual @($exitCodes | Where-Object { $_ -ne 0 }).Count -Message 'The losing process must fail to claim the same invocation ID.'
        Assert-True -Condition (Test-Path -LiteralPath $claimPath -PathType Leaf) -Message 'The winning process must leave the claim file behind.'
    }
    finally {
        foreach ($process in $processes) {
            & $process.Stop 'Claim race test cleanup'
        }
    }
}

function Assert-ClaimedInvocationRunsNoProjects {
    # The loser of a same-ID race sees exactly this state: the claim already exists. The verifier must
    # refuse before it runs a single project, so prior evidence can never be overwritten by a rerun.
    $invocationId = 'already-claimed'
    $capture = Join-Path $tempRoot 'already-claimed.jsonl'
    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    Write-Utf8NoBom -Path (Join-Path $artifactRoot ".$invocationId.claim") -Content "$invocationId`n"

    $exitCode = 0
    $variables = @{
        NERV_MAN662_CAPTURE_PATH = $capture
        NERV_MAN662_FAIL_PROJECT = ''
        NERV_MAN662_FAIL_SEED = ''
    }
    try {
        Invoke-WithScopedEnvironment -Variables $variables -ScriptBlock {
            Invoke-PwshScript `
                -ScriptPath (Join-Path $harnessRoot 'scripts/verify-backend-test-determinism.ps1') `
                -Arguments @('-ArtifactRoot', $artifactRoot, '-InvocationId', $invocationId) `
                -WorkingDirectory $harnessRoot `
                -TimeoutSeconds 60 `
                -Name $scriptLogName | Out-Null
        }
    }
    catch {
        if ($_.Exception.Message -notmatch 'exited with (?<exitCode>\d+)') {
            throw
        }
        $exitCode = [int] $Matches['exitCode']
    }

    Assert-True -Condition ($exitCode -ne 0) -Message 'A verifier run against an already-claimed invocation must fail.'
    $records = @(
        if (Test-Path -LiteralPath $capture -PathType Leaf) {
            Get-Content -LiteralPath $capture | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }
    )
    Assert-Equal -Expected 0 -Actual $records.Count -Message 'A verifier that lost the claim must not run any project.'
    Assert-True -Condition (-not (Test-Path -LiteralPath (Join-Path $artifactRoot $invocationId))) -Message 'A verifier that lost the claim must not create an evidence directory.'
}

[System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    New-StubbedHarness

    Assert-ClaimIsAtomicUnderConcurrency
    Assert-ClaimedInvocationRunsNoProjects
    Assert-VerifierPreservesCallerLocation

    $successful = Invoke-VerifierCase -InvocationId 'success'
    Assert-Equal -Expected 0 -Actual $successful.ExitCode -Message 'Successful six-round verifier run must exit zero.'
    Assert-SixRoundContract -Case $successful
    Assert-True -Condition (@($successful.Summary | Where-Object { (-not (([int] $_.exitCode) -eq (0))) }).Count -eq 0) -Message 'Successful summary must record exitCode 0 for every round.'

    $failed = Invoke-VerifierCase `
        -InvocationId 'failure' `
        -FailProject $projects[2] `
        -FailSeed 'man662-03'
    Assert-True -Condition ($failed.ExitCode -ne 0) -Message 'A nonzero target project run must make the verifier process nonzero.'
    Assert-SixRoundContract -Case $failed
    Assert-Equal -Expected 23 -Actual ([int] $failed.Summary[2].exitCode) -Message 'The failing project exit code must fail its round.'
    Assert-True -Condition (Test-Path -LiteralPath $successful.SummaryPath -PathType Leaf) -Message 'A later verifier execution must not replace prior evidence.'
    Assert-True -Condition ((-not [string]::Equals([string]($successful.SummaryPath), [string]($failed.SummaryPath), [StringComparison]::Ordinal))) -Message 'Reruns must be recorded under a new invocation path.'

    # Equal exit codes are not equal results: a round that silently skipped tests must still fail.
    $drifted = Invoke-VerifierCase `
        -InvocationId 'result-drift' `
        -DriftProject $projects[1] `
        -DriftSeed 'man662-04'
    Assert-True -Condition ($drifted.ExitCode -ne 0) -Message 'A round whose test results drift must fail even when every exit code is zero.'
    Assert-True -Condition (@($drifted.Summary | Where-Object { (-not (([int] $_.exitCode) -eq (0))) }).Count -eq 0) -Message 'The drift case must be detected through results, not exit codes.'
    Assert-Equal -Expected 24 -Actual $drifted.Records.Count -Message 'The drift case must still run all 24 project invocations.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    foreach ($logRoot in @($scriptLogRoot, $raceScriptLogRoot)) {
        if (Test-Path -LiteralPath $logRoot) {
            Remove-Item -LiteralPath $logRoot -Recurse -Force
        }
    }
}

Write-Host 'Backend test determinism verifier script tests passed.'
