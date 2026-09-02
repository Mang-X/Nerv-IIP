# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates FullChain lane manifest, TRX and summary contracts with temporary fixtures
#     - Executes the production FullChain runner against temporary leaf command shims
#   Writes:
#     - Temporary TRX, runner, workflow, command-shim and summary fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullChainTestLane.ps1')
. (Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrix.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$scenarioMatrixPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-full-chain-lane-$([Guid]::NewGuid().ToString('N'))"

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-FullChainDeadlineAdmissionContract {
    param(
        [Parameter(Mandatory)] [scriptblock] $Admission,
        [Parameter(Mandatory)] [string] $Context
    )

    $cases = @(
        [pscustomobject]@{ Name = 'ample-budget'; Deadline = 2000; Elapsed = 200; Entrypoint = 1000; Cleanup = 500; Guard = 100; Remaining = 1800; Required = 1600; Allowed = $true; Reason = 'Allowed' },
        [pscustomobject]@{ Name = 'exact-boundary'; Deadline = 1800; Elapsed = 200; Entrypoint = 1000; Cleanup = 500; Guard = 100; Remaining = 1600; Required = 1600; Allowed = $true; Reason = 'Allowed' },
        [pscustomobject]@{ Name = 'insufficient-elapsed-budget'; Deadline = 1800; Elapsed = 201; Entrypoint = 1000; Cleanup = 500; Guard = 100; Remaining = 1599; Required = 1600; Allowed = $false; Reason = 'InsufficientRemainingBudget' },
        [pscustomobject]@{ Name = 'entrypoint-budget-contribution'; Deadline = 1800; Elapsed = 200; Entrypoint = 1001; Cleanup = 500; Guard = 100; Remaining = 1600; Required = 1601; Allowed = $false; Reason = 'InsufficientRemainingBudget' },
        [pscustomobject]@{ Name = 'cleanup-reserve-contribution'; Deadline = 1800; Elapsed = 200; Entrypoint = 1000; Cleanup = 501; Guard = 100; Remaining = 1600; Required = 1601; Allowed = $false; Reason = 'InsufficientRemainingBudget' },
        [pscustomobject]@{ Name = 'guard-reserve-contribution'; Deadline = 1800; Elapsed = 200; Entrypoint = 1000; Cleanup = 500; Guard = 101; Remaining = 1600; Required = 1601; Allowed = $false; Reason = 'InsufficientRemainingBudget' }
    )

    foreach ($case in $cases) {
        $result = & $Admission $case.Deadline $case.Elapsed $case.Entrypoint $case.Cleanup $case.Guard
        Assert-Contract ($result.RemainingSeconds -eq $case.Remaining) "$Context case '$($case.Name)' failed field 'RemainingSeconds'."
        Assert-Contract ($result.RequiredSeconds -eq $case.Required) "$Context case '$($case.Name)' failed field 'RequiredSeconds'."
        Assert-Contract ([bool]$result.Allowed -eq [bool]$case.Allowed) "$Context case '$($case.Name)' failed field 'Allowed'."
        Assert-Contract ([string]::Equals([string]$result.Reason, [string]$case.Reason, [StringComparison]::Ordinal)) "$Context case '$($case.Name)' failed field 'Reason'."
    }
}

function New-FullChainDeadlineAdmissionTestResult {
    param(
        [Parameter(Mandatory)] $RemainingSeconds,
        [Parameter(Mandatory)] $RequiredSeconds,
        [Parameter(Mandatory)] [bool] $Allowed,
        [Parameter(Mandatory)] [string] $Reason
    )

    return [pscustomobject]@{
        Allowed = $Allowed
        Reason = $Reason
        RemainingSeconds = $RemainingSeconds
        RequiredSeconds = $RequiredSeconds
    }
}

function New-FullChainMemberAdmissionSummary {
    return [pscustomobject][ordered]@{
        outcome = 'not-run'
        cleanup = 'not-run'
        diagnosticEvidence = 'not-run'
        deadlineAdmission = [pscustomobject][ordered]@{
            reason = 'not-evaluated'
            elapsedSeconds = 0
            remainingSeconds = 0
            requiredSeconds = 0
        }
    }
}

function Write-FullChainRunnerWorkflowFixture {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $RunStepTimeoutMinutes
    )

    $workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
    $pattern = '(?m)(^\s+- name: Run governed FullChain scenarios\r?\n\s+timeout-minutes: )120$'
    $updated = [regex]::Replace($workflow, $pattern, "`${1}$RunStepTimeoutMinutes")
    if ([string]::Equals($workflow, $updated, [StringComparison]::Ordinal)) { throw 'FullChain workflow fixture did not replace the governed runner timeout.' }
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
    return $Path
}

function New-FullChainRunnerFakeCommands {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string[]] $DiscoveredIdentities
    )

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    if ($IsWindows) {
        $docker = @'
@echo off
>>"%NERV_FULLCHAIN_FAKE_COMMAND_LOG%" echo docker %*
echo %* | findstr /C:" exec -T postgres psql " >nul
if not errorlevel 1 (
  if "%NERV_FULLCHAIN_FAKE_FAILURE%"=="postgres-version" (
    >&2 echo NERV_FULLCHAIN_ORIGINAL_FAILURE
    exit /b 42
  )
  echo 18.6
)
echo %* | findstr /C:" redis-cli --raw PING" >nul
if not errorlevel 1 echo PONG
echo %* | findstr /C:" redis-cli --raw INFO server" >nul
if not errorlevel 1 echo redis_version:8.10.1
exit /b 0
'@
        $identityOutput = (@('  echo The following Tests are available:') + @($DiscoveredIdentities | ForEach-Object { "  echo     $_" })) -join "`r`n"
        $dotnet = @"
@echo off
>>"%NERV_FULLCHAIN_FAKE_COMMAND_LOG%" echo dotnet %*
echo %* | findstr /C:" --filter " >nul
if not errorlevel 1 >>"%NERV_FULLCHAIN_FAKE_COMMAND_LOG%" echo ENTRYPOINT dotnet %*
echo %* | findstr /C:" --list-tests " >nul
if not errorlevel 1 (
$identityOutput
)
exit /b 0
"@
        $pwsh = @'
@echo off
>>"%NERV_FULLCHAIN_FAKE_COMMAND_LOG%" echo ENTRYPOINT pwsh %*
exit /b 43
'@
        [IO.File]::WriteAllText((Join-Path $Directory 'docker.cmd'), $docker, [Text.ASCIIEncoding]::new())
        [IO.File]::WriteAllText((Join-Path $Directory 'dotnet.cmd'), $dotnet, [Text.ASCIIEncoding]::new())
        [IO.File]::WriteAllText((Join-Path $Directory 'pwsh.cmd'), $pwsh, [Text.ASCIIEncoding]::new())
        return
    }

    $docker = @'
#!/bin/sh
printf 'docker %s\n' "$*" >> "$NERV_FULLCHAIN_FAKE_COMMAND_LOG"
case " $* " in
  *" exec -T postgres psql "*)
    if [ "$NERV_FULLCHAIN_FAKE_FAILURE" = "postgres-version" ]; then
      printf '%s\n' 'NERV_FULLCHAIN_ORIGINAL_FAILURE' >&2
      exit 42
    fi
    printf '%s\n' '18.6'
    ;;
  *" redis-cli --raw PING "*) printf '%s\n' 'PONG' ;;
  *" redis-cli --raw INFO server "*) printf '%s\n' 'redis_version:8.10.1' ;;
esac
exit 0
'@
    $identityOutput = ((@("    printf '%s\n' 'The following Tests are available:'") + @($DiscoveredIdentities | ForEach-Object { "    printf '%s\n' '    $_'" })) -join "`n")
    $dotnet = @"
#!/bin/sh
printf 'dotnet %s\n' "`$*" >> "`$NERV_FULLCHAIN_FAKE_COMMAND_LOG"
case " `$* " in
  *" --filter "*) printf 'ENTRYPOINT dotnet %s\n' "`$*" >> "`$NERV_FULLCHAIN_FAKE_COMMAND_LOG" ;;
  *" --list-tests "*)
$identityOutput
    ;;
esac
exit 0
"@
    $pwsh = @'
#!/bin/sh
printf 'ENTRYPOINT pwsh %s\n' "$*" >> "$NERV_FULLCHAIN_FAKE_COMMAND_LOG"
exit 43
'@
    foreach ($command in @(
        @{ Name = 'docker'; Content = $docker },
        @{ Name = 'dotnet'; Content = $dotnet },
        @{ Name = 'pwsh'; Content = $pwsh }
    )) {
        $path = Join-Path $Directory $command.Name
        [IO.File]::WriteAllText($path, $command.Content, [Text.UTF8Encoding]::new($false))
        [IO.File]::SetUnixFileMode(
            $path,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute -bor
                [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupExecute -bor
                [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherExecute)
    }
}

function Assert-FullChainV1WorkflowContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $v1JobProperties = @($parsedWorkflow.jobs.PSObject.Properties | Where-Object {
            [string]::Equals([string]$_.Name, 'business-full-chain-acceptance-v1', [StringComparison]::Ordinal)
        })
    Assert-Contract ($v1JobProperties.Count -eq 1) 'CI must define exactly one business-full-chain-acceptance-v1 physical worker.'
    $v1Job = $v1JobProperties[0].Value
    Assert-Contract ([string]::Equals([string]$v1Job.name, 'Business FullChain Acceptance / v1 Authority', [StringComparison]::Ordinal)) 'The physical FullChain worker must retain the v1 Authority Actions name.'
    Assert-Contract ([int]$v1Job.'timeout-minutes' -eq 225) 'The physical v1 worker must retain the governed 225-minute job budget.'
    Assert-Contract ([string]::Equals([string]$v1Job.'runs-on', 'ubuntu-latest', [StringComparison]::Ordinal)) 'The physical v1 worker must run on ubuntu-latest.'
    $v1Needs = @($v1Job.needs | ForEach-Object { [string]$_ })
    Assert-Contract ($v1Needs.Count -eq 1 -and [string]::Equals($v1Needs[0], 'acceptance-scenario-matrix-planning', [StringComparison]::Ordinal)) 'The physical v1 worker must need only acceptance-scenario-matrix-planning.'
    $allowedV1Conditions = [Collections.Generic.HashSet[string]]::new([string[]]@(
            "`${{ needs.acceptance-scenario-matrix-planning.result == 'success' }}",
            "`${{ !cancelled() && needs.acceptance-scenario-matrix-planning.result == 'success' }}"
        ), [StringComparer]::Ordinal)
    Assert-Contract ($allowedV1Conditions.Contains([string]$v1Job.if)) 'The physical v1 worker must start only after planning succeeds.'
    $v1OutputsProperty = $v1Job.PSObject.Properties['outputs']
    Assert-Contract ($null -ne $v1OutputsProperty -and
        [string]::Equals([string]$v1OutputsProperty.Value.'artifact-name', '${{ steps.v1-artifact-identity.outputs.artifact-name }}', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$v1OutputsProperty.Value.'producer-run-attempt', '${{ steps.v1-artifact-identity.outputs.producer-run-attempt }}', [StringComparison]::Ordinal)) 'The physical v1 worker must publish its canonical artifact identity and physical producer attempt.'

    $v1Steps = @($v1Job.steps)
    $expectedStepNames = @(
        'Checkout',
        'Setup .NET',
        'Cache NuGet packages',
        'Setup Aspire CLI',
        'Setup pnpm',
        'Setup Node.js',
        'Install frontend dependencies',
        'Install Playwright Chromium',
        'Prepare FullChain dependency images',
        'Resolve FullChain evidence environment',
        'Run governed FullChain scenarios',
        'Resolve v1 canonical artifact identity',
        'Upload v1 sales-order-demand canonical result',
        'Collect FullChain evidence',
        'Upload FullChain normalized evidence',
        'Upload FullChain dependency summary',
        'Upload FullChain failure diagnostics'
    )
    Assert-Contract ([string]::Equals((@($v1Steps.name) -join '|'), ($expectedStepNames -join '|'), [StringComparison]::Ordinal)) 'The physical v1 worker must carry the complete governed workflow step sequence.'
    Assert-Contract (@($v1Steps | Where-Object { $null -eq $_.PSObject.Properties['timeout-minutes'] -or [int]$_.'timeout-minutes' -le 0 }).Count -eq 0) 'Every physical v1 workflow step must retain a positive timeout.'
    $v1StepBudget = (@($v1Steps | ForEach-Object { [int]$_.'timeout-minutes' }) | Measure-Object -Sum).Sum
    Assert-Contract ($v1StepBudget -lt [int]$v1Job.'timeout-minutes') 'The physical v1 worker explicit step budget must remain strictly below its job budget.'
    $runSteps = @($v1Steps | Where-Object { [string]::Equals([string]$_.name, 'Run governed FullChain scenarios', [StringComparison]::Ordinal) })
    Assert-Contract ($runSteps.Count -eq 1 -and [int]$runSteps[0].'timeout-minutes' -eq 120) 'The physical v1 worker must retain exactly one 120-minute governed FullChain runner step.'
    $v1Run = [string]$runSteps[0].run
    Assert-Contract ($v1Run.Contains("`$v1CanonicalResultPath = [IO.Path]::GetFullPath('artifacts/acceptance-scenario-matrix/v1/sales-order-demand-result.json')", [StringComparison]::Ordinal) -and
        $v1Run.Contains('-CanonicalResultPath $v1CanonicalResultPath', [StringComparison]::Ordinal)) 'The physical v1 worker must pass a canonical absolute repository path to the governed FullChain runner.'
    foreach ($canonicalArgument in @("-TrackIdentifier 'v1'", "-Repository '`${{ github.repository }}'", "-RunId '`${{ github.run_id }}'", "-RunAttempt '`${{ github.run_attempt }}'", "-TestedSha '`${{ needs.acceptance-scenario-matrix-planning.outputs.tested-sha }}'", "-ManifestDigest '`${{ needs.acceptance-scenario-matrix-planning.outputs.manifest-digest }}'", "-ScenarioId 'sales-order-demand'")) {
        Assert-Contract ($v1Run.Contains($canonicalArgument, [StringComparison]::Ordinal)) "The v1 sales member canonical invocation is missing '$canonicalArgument'."
    }
    $canonicalUploads = @($v1Steps | Where-Object {
            [string]::Equals([string]$_.name, 'Upload v1 sales-order-demand canonical result', [StringComparison]::Ordinal) -and
            [string]::Equals([string]$_.uses, 'actions/upload-artifact@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($canonicalUploads.Count -eq 1) 'The v1 worker must upload exactly one sales-order-demand canonical artifact.'
    $v1IdentitySteps = @($v1Steps | Where-Object {
            $idProperty = $_.PSObject.Properties['id']
            $null -ne $idProperty -and [string]::Equals([string]$idProperty.Value, 'v1-artifact-identity', [StringComparison]::Ordinal)
        })
    Assert-Contract ($v1IdentitySteps.Count -eq 1 -and
        ([string]$v1IdentitySteps[0].run).Contains('artifact-name=acceptance-scenario-matrix-result-v1-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal) -and
        ([string]$v1IdentitySteps[0].run).Contains('producer-run-attempt=${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'The v1 worker must single-source its canonical artifact name and physical producer attempt.'
    Assert-Contract ([string]::Equals([string]$canonicalUploads[0].with.name, '${{ steps.v1-artifact-identity.outputs.artifact-name }}', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$canonicalUploads[0].with.path, 'artifacts/acceptance-scenario-matrix/v1/sales-order-demand-result.json', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$canonicalUploads[0].with.'if-no-files-found', 'error', [StringComparison]::Ordinal) -and
        [int]$canonicalUploads[0].with.'retention-days' -eq 14 -and
        $null -eq $canonicalUploads[0].with.PSObject.Properties['overwrite']) 'The v1 canonical artifact must be one exact immutable attempt file, fail closed when absent, and retain 14 days.'

    $collectorSteps = @($v1Steps | Where-Object {
            $runProperty = $_.PSObject.Properties['run']
            $null -ne $runProperty -and
            ([string]$runProperty.Value).Contains('./scripts/collect-test-evidence.ps1', [StringComparison]::Ordinal) -and
            ([string]$runProperty.Value).Contains('-Lane full-chain', [StringComparison]::Ordinal)
        })
    Assert-Contract ($collectorSteps.Count -eq 1) 'The physical v1 worker must remain the sole FullChain MAN-661 collector owner.'
    Assert-Contract (([string]$collectorSteps[0].run).Contains('-JobName "Business FullChain Acceptance / v1 Authority"', [StringComparison]::Ordinal)) 'The FullChain collector must bind rerun authority to the physical v1 Actions job.'

    $allFullChainCollectors = @(
        foreach ($jobProperty in $parsedWorkflow.jobs.PSObject.Properties) {
            foreach ($step in @($jobProperty.Value.steps)) {
                $runProperty = $step.PSObject.Properties['run']
                $run = if ($null -eq $runProperty) { '' } else { [string]$runProperty.Value }
                if ($run.Contains('./scripts/collect-test-evidence.ps1', [StringComparison]::Ordinal) -and $run.Contains('-Lane full-chain', [StringComparison]::Ordinal)) {
                    [pscustomobject]@{ Job = [string]$jobProperty.Name; Step = [string]$step.name }
                }
            }
        }
    )
    Assert-Contract ($allFullChainCollectors.Count -eq 1 -and [string]::Equals([string]$allFullChainCollectors[0].Job, 'business-full-chain-acceptance-v1', [StringComparison]::Ordinal)) 'v1 must remain the sole formal full-chain evidence owner; shadow must not attach a MAN-661 collector.'

    return $parsedWorkflow
}

function New-FullChainTrx {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Identities,
        [string] $Outcome = 'Passed'
    )

    $definitions = [Collections.Generic.List[string]]::new()
    $results = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Identities.Count; $index++) {
        $identity = $Identities[$index]
        $separatorIndex = $identity.LastIndexOf('.', [StringComparison]::Ordinal)
        $class = $identity.Substring(0, $separatorIndex)
        $method = $identity.Substring($separatorIndex + 1)
        $id = "test-$index"
        $definitions.Add("<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest>")
        $results.Add("<UnitTestResult testId=`"$id`" testName=`"$method`" outcome=`"$Outcome`" />")
    }
    $trx = "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$($results -join '')</Results><TestDefinitions>$($definitions -join '')</TestDefinitions></TestRun>"
    [IO.File]::WriteAllText($Path, $trx, [Text.UTF8Encoding]::new($false))
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    $canonicalDeadlineAdmission = {
        param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
        Test-NervFullChainDeadlineAdmission `
            -GlobalDeadlineSeconds $Deadline `
            -ElapsedSeconds $Elapsed `
            -EntrypointTimeoutSeconds $Entrypoint `
            -CleanupReserveSeconds $Cleanup `
            -GuardReserveSeconds $Guard
    }
    Assert-FullChainDeadlineAdmissionContract -Admission $canonicalDeadlineAdmission -Context 'Canonical implementation'

    $deadlineAdmissionMutations = @(
        [pscustomobject]@{
            Name = 'cleanup-reserve-deleted'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'guard-reserve-deleted'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'cleanup-reserve-replaced-by-guard'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Guard + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'guard-reserve-replaced-by-cleanup'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Cleanup
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'strict-boundary'
            ExpectedField = 'Allowed'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $remaining -gt $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'elapsed-ignored-by-decision'
            ExpectedField = 'Allowed'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $Deadline -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'wrong-entrypoint-budget'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = 900 + $Cleanup + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'reason-misreported'
            ExpectedField = 'Reason'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'InsufficientRemainingBudget' } else { 'Allowed' })
            }
        },
        [pscustomobject]@{
            Name = 'required-seconds-misreported'
            ExpectedField = 'RequiredSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $remaining -RequiredSeconds ($Entrypoint + $Cleanup + $Cleanup) -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'remaining-seconds-clamped-to-required'
            ExpectedField = 'RemainingSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $remaining -ge $required
                $reportedRemaining = if ($remaining -lt $required) { $remaining } else { $required }
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $reportedRemaining -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        },
        [pscustomobject]@{
            Name = 'remaining-seconds-ignores-elapsed'
            ExpectedField = 'RemainingSeconds'
            Admission = {
                param($Deadline, $Elapsed, $Entrypoint, $Cleanup, $Guard)
                $remaining = $Deadline - $Elapsed
                $required = $Entrypoint + $Cleanup + $Guard
                $allowed = $remaining -ge $required
                New-FullChainDeadlineAdmissionTestResult -RemainingSeconds $Deadline -RequiredSeconds $required -Allowed $allowed -Reason $(if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' })
            }
        }
    )
    foreach ($mutation in $deadlineAdmissionMutations) {
        $mutationFailure = $null
        try { Assert-FullChainDeadlineAdmissionContract -Admission $mutation.Admission -Context "Mutation '$($mutation.Name)'" }
        catch { $mutationFailure = $_ }
        Assert-Contract ($null -ne $mutationFailure) "Deadline admission mutation '$($mutation.Name)' must be rejected by the behavioral contract."
        Assert-Contract (
            ([string]$mutationFailure.Exception.Message).Contains("field '$($mutation.ExpectedField)'", [StringComparison]::Ordinal)
        ) "Deadline admission mutation '$($mutation.Name)' must fail the '$($mutation.ExpectedField)' semantic assertion, but failed with '$($mutationFailure.Exception.Message)'."
    }

    $invokedMembers = [Collections.Generic.List[string]]::new()
    $firstSummary = New-FullChainMemberAdmissionSummary
    $secondSummary = New-FullChainMemberAdmissionSummary
    $firstAdmission = Invoke-NervFullChainMemberAdmission -MemberId 'first' -EntrypointKind 'fullstack' -GlobalDeadlineSeconds 2200 -ElapsedSeconds 0 -FullstackEntrypointTimeoutSeconds 1200 -ScriptEntrypointTimeoutSeconds 900 -DotnetEntrypointTimeoutSeconds 600 -CleanupReserveSeconds 300 -GuardReserveSeconds 300 -MemberSummary $firstSummary -Action { param($memberId) $invokedMembers.Add($memberId) | Out-Null }
    $secondAdmission = Invoke-NervFullChainMemberAdmission -MemberId 'second' -EntrypointKind 'script' -GlobalDeadlineSeconds 2200 -ElapsedSeconds 600 -FullstackEntrypointTimeoutSeconds 1200 -ScriptEntrypointTimeoutSeconds 900 -DotnetEntrypointTimeoutSeconds 600 -CleanupReserveSeconds 300 -GuardReserveSeconds 300 -MemberSummary $secondSummary -Action { param($memberId) $invokedMembers.Add($memberId) | Out-Null }
    Assert-Contract ($firstAdmission.Allowed -and $secondAdmission.Allowed -and [string]::Equals(($invokedMembers -join '|'), 'first|second', [StringComparison]::Ordinal)) 'An early first-member completion must release its unused time to a later member admission.'

    $manifest = Import-NervFullChainTestLaneManifest -ManifestPath $manifestPath -RepositoryRoot $repoRoot
    $scenarioMatrix = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $scenarioMatrixPath -V1ManifestPath $manifestPath -RepositoryRoot $repoRoot
    [string[]]$manifestIds = @($manifest.members.id | ForEach-Object { [string]$_ })
    [string[]]$matrixAliases = @($scenarioMatrix.scenarios | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal) } | ForEach-Object { [string]$_.v1Alias })
    [Array]::Sort($manifestIds, [StringComparer]::Ordinal)
    [Array]::Sort($matrixAliases, [StringComparer]::Ordinal)
    Assert-Contract ([string]::Equals(($manifestIds -join '|'), ($matrixAliases -join '|'), [StringComparison]::Ordinal)) 'FullChain manifest member identities must equal the acceptance matrix active/core alias set.'
    $expectedIds = @($manifest.members.id | ForEach-Object { [string]$_ })
    Assert-Contract (@($manifest.members | Where-Object {
        -not [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal)
    }).Count -eq 0) 'All governed FullChain scenarios must be active/core.'
    Assert-Contract (@($manifest.members | Where-Object { @($_.expectedTestIdentities).Count -ne 1 }).Count -eq 0) 'Each FullChain scenario must freeze exactly one identity.'
    Assert-Contract (@($manifest.members | Where-Object { -not [string]::Equals([string]$_.project, 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj', [StringComparison]::Ordinal) }).Count -eq 0) 'All scenarios must target the FullChain test project.'
    Assert-Contract (@($manifest.members | Where-Object { @($_.diagnosticSchemas).Count -eq 0 }).Count -eq 0) 'Every scenario must declare restricted PostgreSQL diagnostic schemas.'
    Assert-Contract (@($manifest.members | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.entrypoint.kind) }).Count -eq 0) 'Every scenario must declare an entrypoint kind.'

    $runnerPath = Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'
    Assert-Contract (Test-Path -LiteralPath $runnerPath -PathType Leaf) 'The governed FullChain runner must exist.'
    $runnerWorkflowPath = Write-FullChainRunnerWorkflowFixture -Path (Join-Path $fixtureRoot 'runner-denied-workflow.yml') -RunStepTimeoutMinutes 1
    $fakeCommandDirectory = Join-Path $fixtureRoot 'runner-fake-bin'
    $fakeCommandLog = Join-Path $fixtureRoot 'runner-fake-commands.log'
    $allIdentities = @($manifest.members.expectedTestIdentities | ForEach-Object { [string]$_ })
    New-FullChainRunnerFakeCommands -Directory $fakeCommandDirectory -DiscoveredIdentities $allIdentities
    $savedPath = [Environment]::GetEnvironmentVariable('PATH')
    $savedPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
    $savedRedis = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
    $savedFakeCommandLog = [Environment]::GetEnvironmentVariable('NERV_FULLCHAIN_FAKE_COMMAND_LOG')
    $savedFakeFailure = [Environment]::GetEnvironmentVariable('NERV_FULLCHAIN_FAKE_FAILURE')
    $savedComposeProject = [Environment]::GetEnvironmentVariable('COMPOSE_PROJECT_NAME')
    $realPwsh = [string](@(Get-Command pwsh -CommandType Application)[0].Source)
    try {
        [Environment]::SetEnvironmentVariable('PATH', "$fakeCommandDirectory$([IO.Path]::PathSeparator)$savedPath")
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', 'Host=fake-postgres;Database=postgres')
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', 'fake-redis:6379')
        [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_COMMAND_LOG', $fakeCommandLog)
        [Environment]::SetEnvironmentVariable('COMPOSE_PROJECT_NAME', $null)

        foreach ($deniedCase in @(
            [pscustomobject]@{ Kind = 'fullstack'; MemberId = 'maintenance-runtime-hours' },
            [pscustomobject]@{ Kind = 'script'; MemberId = 'erp-wms-delivery-completion' },
            [pscustomobject]@{ Kind = 'dotnet'; MemberId = 'erp-return-closure' }
        )) {
            [IO.File]::WriteAllText($fakeCommandLog, '', [Text.UTF8Encoding]::new($false))
            [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_FAILURE', $null)
            $caseRoot = Join-Path $fixtureRoot "runner-denied-$($deniedCase.Kind)"
            $caseSummaryPath = Join-Path $caseRoot 'summary.json'
            $writeState = [pscustomobject]@{ terminal = 0; resumable = 0 }
            $summaryWriter = {
                param([string] $Path, [string] $Payload, [string] $Phase)

                $writeState.$Phase++
                [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
                [IO.File]::WriteAllText($Path, $Payload, [Text.UTF8Encoding]::new($false))
            }.GetNewClosure()
            $runnerFailure = $null
            try {
                & $runnerPath `
                    -MemberId $deniedCase.MemberId `
                    -WorkflowPath $runnerWorkflowPath `
                    -ResultsDirectory (Join-Path $caseRoot 'results') `
                    -SummaryPath $caseSummaryPath `
                    -SummaryFileWriter $summaryWriter 6>$null | Out-Null
            }
            catch { $runnerFailure = $_ }

            Assert-Contract ($null -ne $runnerFailure -and ([string]$runnerFailure.Exception.Message).Contains('deadline admission denied', [StringComparison]::Ordinal)) "Production $($deniedCase.Kind) denied fixture must preserve the admission failure."
            $commands = @([IO.File]::ReadAllLines($fakeCommandLog))
            Assert-Contract (@($commands | Where-Object { $_ -match '^docker .* up -d ' }).Count -eq 1) "Production $($deniedCase.Kind) denied fixture must enter runner-owned infrastructure state."
            Assert-Contract (@($commands | Where-Object { $_ -match '^docker .* (?:stop|down) ' }).Count -eq 1) "Production $($deniedCase.Kind) denied fixture must clean runner-owned infrastructure exactly once."
            Assert-Contract (@($commands | Where-Object { $_.StartsWith('ENTRYPOINT ', [StringComparison]::Ordinal) }).Count -eq 0) "Production $($deniedCase.Kind) denied fixture must invoke its member entrypoint zero times."
            Assert-Contract ($writeState.terminal -eq 1) "Production $($deniedCase.Kind) denied fixture must persist exactly one terminal summary."
            $caseSummary = Get-Content -LiteralPath $caseSummaryPath -Raw | ConvertFrom-Json -Depth 20
            Assert-Contract ([string]::Equals([string]$caseSummary.members[0].outcome, 'failed', [StringComparison]::Ordinal) -and [string]::Equals([string]$caseSummary.members[0].diagnosticEvidence, 'deadline-admission-denied', [StringComparison]::Ordinal) -and [string]::Equals([string]$caseSummary.cleanup, 'passed', [StringComparison]::Ordinal)) "Production $($deniedCase.Kind) denied fixture must persist its final denied outcome and cleanup."
        }

        [IO.File]::WriteAllText($fakeCommandLog, '', [Text.UTF8Encoding]::new($false))
        [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_FAILURE', 'postgres-version')
        $failureRoot = Join-Path $fixtureRoot 'runner-original-failure'
        $failureSummaryPath = Join-Path $failureRoot 'summary.json'
        $failureWriteState = [pscustomobject]@{ terminal = 0; resumable = 0 }
        $failureSummaryWriter = {
            param([string] $Path, [string] $Payload, [string] $Phase)

            $failureWriteState.$Phase++
            [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
            [IO.File]::WriteAllText($Path, $Payload, [Text.UTF8Encoding]::new($false))
        }.GetNewClosure()
        $originalFailure = $null
        try {
            & $runnerPath `
                -MemberId 'maintenance-runtime-hours' `
                -WorkflowPath $runnerWorkflowPath `
                -ResultsDirectory (Join-Path $failureRoot 'results') `
                -SummaryPath $failureSummaryPath `
                -SummaryFileWriter $failureSummaryWriter 6>$null | Out-Null
        }
        catch { $originalFailure = $_ }
        Assert-Contract ($null -ne $originalFailure -and ([string]$originalFailure.Exception.Message).Contains('NERV_FULLCHAIN_ORIGINAL_FAILURE', [StringComparison]::Ordinal)) 'Production infrastructure failure fixture must preserve the original failure marker.'
        $failureCommands = @([IO.File]::ReadAllLines($fakeCommandLog))
        Assert-Contract (@($failureCommands | Where-Object { $_ -match '^docker .* (?:stop|down) ' }).Count -eq 1) 'Production infrastructure failure fixture must clean runner-owned infrastructure exactly once.'
        Assert-Contract ($failureWriteState.terminal -eq 1) 'Production infrastructure failure fixture must persist exactly one terminal summary.'
        $failureSummary = Get-Content -LiteralPath $failureSummaryPath -Raw | ConvertFrom-Json -Depth 20
        Assert-Contract ([string]::Equals([string]$failureSummary.cleanup, 'failed', [StringComparison]::Ordinal)) 'Production infrastructure failure fixture must persist its final cleanup state.'

        [IO.File]::WriteAllText($fakeCommandLog, '', [Text.UTF8Encoding]::new($false))
        [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_FAILURE', $null)
        $childRoot = Join-Path $fixtureRoot 'runner-child-process'
        $childSummaryPath = Join-Path $childRoot 'summary.json'
        $childFailure = $null
        try {
            Invoke-NativeCommandOutput `
                -Command $realPwsh `
                -Arguments @(
                    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath,
                    '-MemberId', 'maintenance-runtime-hours',
                    '-WorkflowPath', $runnerWorkflowPath,
                    '-ResultsDirectory', (Join-Path $childRoot 'results'),
                    '-SummaryPath', $childSummaryPath
                ) `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 60 `
                -Name 'full-chain-production-composition-child' | Out-Null
        }
        catch { $childFailure = $_ }
        Assert-Contract ($null -ne $childFailure -and ([string]$childFailure.Exception.Message).Contains('deadline admission denied', [StringComparison]::Ordinal)) "The real FullChain child-process entrypoint must preserve the denied failure; observed '$($childFailure.Exception.Message)'."
        $childCommands = @([IO.File]::ReadAllLines($fakeCommandLog))
        Assert-Contract (@($childCommands | Where-Object { $_ -match '^docker .* (?:stop|down) ' }).Count -eq 1) 'The real FullChain child-process entrypoint must execute production cleanup exactly once.'
        $childSummary = Get-Content -LiteralPath $childSummaryPath -Raw | ConvertFrom-Json -Depth 20
        Assert-Contract ([string]::Equals([string]$childSummary.members[0].diagnosticEvidence, 'deadline-admission-denied', [StringComparison]::Ordinal) -and [string]::Equals([string]$childSummary.cleanup, 'passed', [StringComparison]::Ordinal)) 'The real FullChain child-process entrypoint must persist its terminal denied summary.'
    }
    finally {
        [Environment]::SetEnvironmentVariable('PATH', $savedPath)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedPostgres)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', $savedRedis)
        [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_COMMAND_LOG', $savedFakeCommandLog)
        [Environment]::SetEnvironmentVariable('NERV_FULLCHAIN_FAKE_FAILURE', $savedFakeFailure)
        [Environment]::SetEnvironmentVariable('COMPOSE_PROJECT_NAME', $savedComposeProject)
    }
    $runnerContent = [IO.File]::ReadAllText($runnerPath)
    foreach ($requiredFragment in @(
        'Import-NervFullChainTestLaneManifest',
        'Get-NervFullChainTrxResult',
        'Assert-NervFullChainTestLaneSummary',
        'Wait-NervFullChainComposeProbe',
        'MaximumAttempts = 30',
        "'fullstack'",
        "'script'",
        "'dotnet'",
        'NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY',
        'NERV_IIP_FULL_CHAIN_RESULT_FILE',
        'NERV_IIP_FULLSTACK_STATE_ROOT',
        'NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH',
        "SetEnvironmentVariable('Messaging__Provider', 'Redis')",
        "SetEnvironmentVariable('Persistence__Provider', 'PostgreSQL')",
        "dependencyEvidence = 'passed'",
        'Assert-NervFullChainMemberEvidence'
        'CanonicalResultPath'
        "'-TrackIdentifier', `$TrackIdentifier"
    )) {
        Assert-Contract ($runnerContent.Contains($requiredFragment, [StringComparison]::Ordinal)) "FullChain runner is missing required contract fragment '$requiredFragment'."
    }
    Assert-Contract (-not $runnerContent.Contains('continue-on-error', [StringComparison]::OrdinalIgnoreCase)) 'FullChain runner must preserve natural failures.'
    Assert-Contract (-not $runnerContent.Contains('FLUSHALL', [StringComparison]::OrdinalIgnoreCase)) 'FullChain runner must never use broad Redis cleanup.'
    Assert-Contract (([regex]::Matches($runnerContent, "'--list-tests'", [Text.RegularExpressions.RegexOptions]::CultureInvariant)).Count -eq 1) 'FullChain discovery must execute exactly once for the shared test project.'
    $discoveryIndex = $runnerContent.IndexOf("'--list-tests'", [StringComparison]::Ordinal)
    $memberLoopIndex = $runnerContent.IndexOf('$memberResultsDirectory =', [StringComparison]::Ordinal)
    Assert-Contract ($discoveryIndex -ge 0 -and $memberLoopIndex -ge 0 -and $discoveryIndex -lt $memberLoopIndex) 'FullChain discovery must finish before any side-effecting member entrypoint runs.'
    Assert-Contract ($runnerContent.IndexOf('discovery expected 1 frozen test', [StringComparison]::Ordinal) -lt $memberLoopIndex) 'Every frozen identity must be validated from the shared discovery result before member entrypoints run.'
    Assert-Contract ($runnerContent.Contains("'restore', `$fullChainProject", [StringComparison]::Ordinal)) 'FullChain runner must restore the shared project exactly once before discovery.'
    Assert-Contract ($runnerContent.Contains("'--no-restore', '--list-tests'", [StringComparison]::Ordinal)) 'FullChain discovery must not restore again after the explicit restore phase.'
    Assert-Contract ($runnerContent.Contains("SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', '1')", [StringComparison]::Ordinal)) 'FullChain runner must disable MSBuild node reuse on hosted runners.'
    Assert-Contract ($runnerContent.Contains("SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', '0')", [StringComparison]::Ordinal)) 'FullChain runner must disable the persistent dotnet build server.'
    Assert-Contract ($runnerContent.Contains('[Diagnostics.Stopwatch]::StartNew()', [StringComparison]::Ordinal)) 'FullChain runner must measure its global deadline with a monotonic stopwatch.'
    Assert-Contract ($runnerContent.Contains('Invoke-NervFullChainMemberAdmission', [StringComparison]::Ordinal)) 'Every FullChain member must pass deadline admission before its action.'
    Assert-Contract (-not $runnerContent.Contains('$maximumGovernedRuntimeSeconds', [StringComparison]::Ordinal)) 'FullChain runner must not reject the lane from a sum-of-maximums precheck.'
    Assert-Contract ($runnerContent.Contains('Write-NervFullChainSummarySnapshot', [StringComparison]::Ordinal)) 'FullChain runner must write resumable summary snapshots before final completion.'
    Assert-Contract ($runnerContent.IndexOf('Write-NervFullChainSummarySnapshot', [StringComparison]::Ordinal) -lt $runnerContent.IndexOf('try {', [StringComparison]::Ordinal)) 'FullChain runner must create the dependency summary before starting governed work.'

    $fullstackSessionContent = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/fullstack-session.ps1'))
    Assert-Contract ($fullstackSessionContent.Contains('[string]::Equals($env:NERV_IIP_FULL_CHAIN_CONFIGURATION, ''Release'', [StringComparison]::Ordinal)', [StringComparison]::Ordinal)) 'FullChain lane probes must opt into Release without changing the standalone fullstack recipe.'
    Assert-Contract (-not $fullstackSessionContent.Contains("'--configuration', 'Release',`n                    '--no-restore'", [StringComparison]::Ordinal)) 'Standalone fullstack probes must retain their existing default configuration.'

    $workflowContent = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
    [void](Assert-FullChainV1WorkflowContract -Path (Join-Path $repoRoot '.github/workflows/ci.yml'))
    foreach ($requiredWorkflowFragment in @(
        'business-full-chain-acceptance-v1:',
        'name: Business FullChain Acceptance / v1 Authority',
        "needs.acceptance-scenario-matrix-planning.result == 'success'",
        'bash "${RUNNER_TEMP}/aspire-install.sh" --version 13.4.6',
        'pnpm -C frontend install --frozen-lockfile',
        'pnpm -C frontend --filter @nerv-iip/business-console exec playwright install chromium',
        '-Lane full-chain',
        '-SelectedLanes full-chain',
        'full-chain-dependency-summary-${{ github.run_id }}-${{ github.run_attempt }}',
        'full-chain-failure-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}',
        'retention-days: 14'
    )) {
        Assert-Contract ($workflowContent.Contains($requiredWorkflowFragment, [StringComparison]::Ordinal)) "FullChain workflow is missing required contract fragment '$requiredWorkflowFragment'."
    }
    Assert-Contract ($workflowContent.Contains('if-no-files-found: error', [StringComparison]::Ordinal)) 'FullChain evidence uploads must fail when required artifacts are missing.'

    foreach ($v1Mutation in @(
            @{
                Name = 'v1-job-id-drift'
                Original = "  business-full-chain-acceptance-v1:`n"
                Replacement = "  business-full-chain-acceptance-v1-drift:`n"
            },
            @{
                Name = 'v1-evidence-owner-drift'
                Original = '-JobName "Business FullChain Acceptance / v1 Authority"'
                Replacement = '-JobName "Business FullChain Acceptance"'
            },
            @{
                Name = 'v1-relative-canonical-result-path'
                Original = '-CanonicalResultPath $v1CanonicalResultPath'
                Replacement = '-CanonicalResultPath artifacts/acceptance-scenario-matrix/v1/sales-order-demand-result.json'
            },
            @{
                Name = 'v1-zero-step-timeout'
                Original = "      - name: Resolve v1 canonical artifact identity`n        timeout-minutes: 1"
                Replacement = "      - name: Resolve v1 canonical artifact identity`n        timeout-minutes: 0"
            },
            @{
                Name = 'v1-step-budget-exhausts-job'
                Original = "      - name: Checkout`n        timeout-minutes: 3"
                Replacement = "      - name: Checkout`n        timeout-minutes: 8"
            }
        )) {
        $mutatedV1Workflow = $workflowContent.Replace([string]$v1Mutation.Original, [string]$v1Mutation.Replacement)
        Assert-Contract (-not [string]::Equals($mutatedV1Workflow, $workflowContent, [StringComparison]::Ordinal)) "FullChain v1 mutation '$($v1Mutation.Name)' must match the canonical workflow."
        $v1MutationPath = Join-Path $fixtureRoot "$($v1Mutation.Name).yml"
        [IO.File]::WriteAllText($v1MutationPath, $mutatedV1Workflow, [Text.UTF8Encoding]::new($false))
        $v1MutationFailure = $null
        try { Assert-FullChainV1WorkflowContract -Path $v1MutationPath | Out-Null } catch { $v1MutationFailure = $_ }
        Assert-Contract ($null -ne $v1MutationFailure) "FullChain v1 mutation '$($v1Mutation.Name)' must be rejected."
    }

    $manifestObject = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $manifestObject.members = @($manifestObject.members | Select-Object -First 4)
    $missingMemberPath = Join-Path $fixtureRoot 'missing-member.json'
    [IO.File]::WriteAllText($missingMemberPath, (($manifestObject | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $missingRejected = $false
    try { Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $scenarioMatrixPath -V1ManifestPath $missingMemberPath -RepositoryRoot $repoRoot | Out-Null }
    catch { $missingRejected = $_.Exception.Message.Contains('must exactly match', [StringComparison]::Ordinal) }
    Assert-Contract $missingRejected 'Removing a FullChain member while retaining its active/core matrix identity must fail closure.'

    $duplicateManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $duplicateManifest.members += $duplicateManifest.members[0]
    $duplicateMemberPath = Join-Path $fixtureRoot 'duplicate-member.json'
    [IO.File]::WriteAllText($duplicateMemberPath, (($duplicateManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $duplicateRejected = $false
    try { Import-NervFullChainTestLaneManifest -ManifestPath $duplicateMemberPath -RepositoryRoot $repoRoot | Out-Null }
    catch { $duplicateRejected = $_.Exception.Message.Contains('must be unique and canonical', [StringComparison]::Ordinal) }
    Assert-Contract $duplicateRejected 'A duplicate FullChain member identity must fail closed.'

    $extraManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $extraMember = ($extraManifest.members[-1] | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20)
    $extraMember.id = 'unexpected-extra-member'
    $extraMember.filter = 'FullyQualifiedName=Nerv.IIP.Business.FullChain.Tests.UnexpectedTests.Unexpected'
    $extraMember.expectedTestIdentities = @('Nerv.IIP.Business.FullChain.Tests.UnexpectedTests.Unexpected')
    $extraManifest.members += $extraMember
    $extraMemberPath = Join-Path $fixtureRoot 'extra-member.json'
    [IO.File]::WriteAllText($extraMemberPath, (($extraManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $extraRejected = $false
    try { Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $scenarioMatrixPath -V1ManifestPath $extraMemberPath -RepositoryRoot $repoRoot | Out-Null }
    catch { $extraRejected = $_.Exception.Message.Contains('must exactly match', [StringComparison]::Ordinal) }
    Assert-Contract $extraRejected 'An extra FullChain member without an active/core matrix identity must fail closure.'

    $mismatchedMatrix = Get-Content -LiteralPath $scenarioMatrixPath -Raw | ConvertFrom-Json -Depth 50
    $firstAlias = [string]$mismatchedMatrix.scenarios[0].v1Alias
    $mismatchedMatrix.scenarios[0].v1Alias = [string]$mismatchedMatrix.scenarios[1].v1Alias
    $mismatchedMatrix.scenarios[1].v1Alias = $firstAlias
    $mismatchedMatrixPath = Join-Path $fixtureRoot 'mismatched-matrix.json'
    [IO.File]::WriteAllText($mismatchedMatrixPath, (($mismatchedMatrix | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
    $mismatchRejected = $false
    try { Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $mismatchedMatrixPath -V1ManifestPath $manifestPath -RepositoryRoot $repoRoot | Out-Null }
    catch { $mismatchRejected = $_.Exception.Message.Contains('must equal v1', [StringComparison]::Ordinal) }
    Assert-Contract $mismatchRejected 'A matrix alias mapped to the wrong FullChain member contract must fail closure.'

    $identity = [string]$manifest.members[0].expectedTestIdentities[0]
    $trxPath = Join-Path $fixtureRoot 'full-chain.trx'
    New-FullChainTrx -Path $trxPath -Identities @($identity)
    $trxResult = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity)
    Assert-Contract ($trxResult.passed -eq 1 -and $trxResult.failed -eq 0 -and $trxResult.skipped -eq 0) 'One passed frozen identity must satisfy the FullChain TRX contract.'

    New-FullChainTrx -Path $trxPath -Identities @($identity) -Outcome 'NotExecuted'
    $skip = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) -AllowInvalid
    Assert-Contract (-not $skip.valid -and $skip.skipped -eq 1) 'A skipped FullChain identity must remain visible and invalid.'

    New-FullChainTrx -Path $trxPath -Identities @($identity, 'Nerv.IIP.Business.FullChain.Tests.UnexpectedTests.Unexpected')
    $extra = Get-NervFullChainTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) -AllowInvalid
    Assert-Contract (-not $extra.identitiesMatch) 'An extra FullChain identity must fail the frozen set contract.'

    $memberEvidenceRoot = Join-Path $fixtureRoot 'member-evidence'
    [IO.Directory]::CreateDirectory($memberEvidenceRoot) | Out-Null
    $memberEvidencePath = Join-Path $memberEvidenceRoot 'entrypoint-evidence.json'
    $cleanupFixture = [ordered]@{
        managedProcesses = [ordered]@{ remaining = 0 }
        disposableDatabase = [ordered]@{ remaining = 0 }
        composeServices = [ordered]@{ remaining = 0 }
        cleanupFailures = @()
    }
    [IO.File]::WriteAllText($memberEvidencePath, (($cleanupFixture | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $verifiedMemberEvidence = Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot
    Assert-Contract (
        [string]::Equals([string]$verifiedMemberEvidence.cleanup, 'passed', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$verifiedMemberEvidence.diagnosticEvidence, 'entrypoint-evidence-verified', [StringComparison]::Ordinal)
    ) 'A complete entrypoint-owned cleanup artifact must satisfy the member evidence contract.'
    Remove-Item -LiteralPath $memberEvidencePath -Force
    $missingEvidenceRejected = $false
    try { Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
    catch { $missingEvidenceRejected = $_.Exception.Message.Contains('cleanup evidence is missing', [StringComparison]::Ordinal) }
    Assert-Contract $missingEvidenceRejected 'Removing entrypoint cleanup evidence must fail the FullChain member contract.'
    $emptyShellEvidence = [ordered]@{ cleanupFailures = @() }
    [IO.File]::WriteAllText($memberEvidencePath, (($emptyShellEvidence | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $emptyShellRejected = $false
    try { Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
    catch { $emptyShellRejected = $_.Exception.Message.Contains("missing required 'managedProcesses'", [StringComparison]::Ordinal) }
    Assert-Contract $emptyShellRejected 'An empty-shell cleanup artifact must not coerce missing readbacks to zero.'
    $missingReadbackFixture = [ordered]@{
        managedProcesses = [ordered]@{}
        disposableDatabase = [ordered]@{ remaining = 0 }
        composeServices = [ordered]@{ remaining = 0 }
        cleanupFailures = @()
    }
    [IO.File]::WriteAllText($memberEvidencePath, (($missingReadbackFixture | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $missingReadbackRejected = $false
    try { Assert-NervFullChainMemberEvidence -Member $manifest.members[3] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
    catch { $missingReadbackRejected = $_.Exception.Message.Contains("missing required 'remaining'", [StringComparison]::Ordinal) }
    Assert-Contract $missingReadbackRejected 'Deleting a required cleanup readback field must fail the member evidence contract.'

    # === #3135 residual 覆盖闭合 =================================================================
    # 见 scripts/run-full-chain-test-lane.ps1 里的同名段落：lane 的选取口径从「白名单精确 filter」
    # 翻转为「默认全跑 + 无排除注册表」。以下三组断言分别钉住：解析不依赖 locale、`[Theory]` 参数
    # 截断、以及「residual 取全部 members 的差集而非 -MemberId 选中子集」。

    $fullChainRootNamespace = 'Nerv.IIP.Business.FullChain.Tests'
    $listTestsBodyLines = @(
        "    $fullChainRootNamespace.AlphaTests.First_case",
        "    $fullChainRootNamespace.AlphaTests.Second_case",
        "    $fullChainRootNamespace.BetaTests.Nested_case"
    )
    # VSTest 的这行表头随 CLI UI 语言变化：CI 是英文，装了中文语言包的开发机是中文。
    # 拿它当解析锚点就是「本机绿 CI 红」的经典形状（本仓 timestamptz 那条同族），因此这里用
    # 两种 locale 的真实表头各喂一遍，断言解析结果**逐字相同**。
    $englishDiscovery = @(
        '  Determining projects to restore...',
        "  $fullChainRootNamespace -> /repo/bin/Release/net10.0/$fullChainRootNamespace.dll",
        'Test run for /repo/bin/Release/net10.0/Nerv.IIP.Business.FullChain.Tests.dll (.NETCoreApp,Version=v10.0)',
        'The following Tests are available:'
    ) + $listTestsBodyLines
    $chineseDiscovery = @(
        '  正在确定要还原的项目...',
        "  $fullChainRootNamespace -> /repo/bin/Release/net10.0/$fullChainRootNamespace.dll",
        '/repo/bin/Release/net10.0/Nerv.IIP.Business.FullChain.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行',
        '以下测试可用:'
    ) + $listTestsBodyLines
    $englishIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines $englishDiscovery -RootNamespace $fullChainRootNamespace)
    $chineseIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines $chineseDiscovery -RootNamespace $fullChainRootNamespace)
    $expectedIdentities = @(
        "$fullChainRootNamespace.AlphaTests.First_case",
        "$fullChainRootNamespace.AlphaTests.Second_case",
        "$fullChainRootNamespace.BetaTests.Nested_case"
    )
    Assert-Contract ([string]::Equals(($englishIdentities -join "`n"), ($expectedIdentities -join "`n"), [StringComparison]::Ordinal)) 'FullChain discovery must parse the English --list-tests output into the exact identity set.'
    Assert-Contract ([string]::Equals(($englishIdentities -join "`n"), ($chineseIdentities -join "`n"), [StringComparison]::Ordinal)) 'FullChain discovery parsing must not depend on the localized --list-tests header.'

    # 表头整行缺失也必须得到同一结果：证明解析确实没有把表头当锚点，而不是「碰巧两种表头都不匹配」。
    $headerlessIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines $listTestsBodyLines -RootNamespace $fullChainRootNamespace)
    Assert-Contract ([string]::Equals(($headerlessIdentities -join "`n"), ($expectedIdentities -join "`n"), [StringComparison]::Ordinal)) 'FullChain discovery must yield the same identities when no header line is present at all.'

    # MSBuild 的构建输出行以被测程序集名开头；只要解析退化成「前缀匹配」就会把它当成一条用例，
    # 拼进 filter 后整个 residual 跑法作废。这两条断言的鉴别力由「把整行完全匹配放松成前缀匹配」
    # 这个变异实测过。
    Assert-Contract (@($englishIdentities | Where-Object { $_.Contains(' -> ', [StringComparison]::Ordinal) }).Count -eq 0) 'FullChain discovery must reject MSBuild build output lines.'
    $buildOnlyIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines @("  $fullChainRootNamespace -> /repo/x.dll") -RootNamespace $fullChainRootNamespace)
    Assert-Contract ($buildOnlyIdentities.Count -eq 0) 'A build output line alone must produce no FullChain identity.'

    # `[Theory]` 按参数逐行列出；不截断就会得到跑不起来的 filter，且同一方法被重复计数。
    $theoryDiscovery = @(
        "    $fullChainRootNamespace.ThetaTests.Theory_case(value: 1)",
        "    $fullChainRootNamespace.ThetaTests.Theory_case(value: 2)",
        "    $fullChainRootNamespace.ThetaTests.Theory_case(value: `"a -> b`")"
    )
    $theoryIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines $theoryDiscovery -RootNamespace $fullChainRootNamespace)
    Assert-Contract ($theoryIdentities.Count -eq 1 -and [string]::Equals($theoryIdentities[0], "$fullChainRootNamespace.ThetaTests.Theory_case", [StringComparison]::Ordinal)) 'FullChain discovery must truncate [Theory] arguments to one method-level identity.'

    # 只有一段（没有类型名）不是用例身份；别的程序集的用例也不属于本项目。
    $foreignIdentities = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines @(
        "    $fullChainRootNamespace.OnlyOneSegment",
        '    Nerv.IIP.Business.Acceptance.Tests.OtherTests.Other_case'
    ) -RootNamespace $fullChainRootNamespace)
    Assert-Contract ($foreignIdentities.Count -eq 0) 'FullChain discovery must ignore non-identity and foreign-assembly lines.'

    # residual = 发现全集 − 冻结成员集。
    $residualFixture = @(Get-NervFullChainResidualTestIdentities -DiscoveredIdentities $expectedIdentities -ClaimedIdentities @("$fullChainRootNamespace.AlphaTests.First_case"))
    Assert-Contract ([string]::Equals(($residualFixture -join "`n"), (@("$fullChainRootNamespace.AlphaTests.Second_case", "$fullChainRootNamespace.BetaTests.Nested_case") -join "`n"), [StringComparison]::Ordinal)) 'FullChain residual must be the ordinal set difference between discovery and frozen members.'
    Assert-Contract (@(Get-NervFullChainResidualTestIdentities -DiscoveredIdentities $expectedIdentities -ClaimedIdentities $expectedIdentities).Count -eq 0) 'Claiming every discovered identity must leave an empty FullChain residual.'

    # 发现集 = 成员集 ∪ residual 集，两个方向都要红。
    $staleClaimRejected = $false
    try { Assert-NervFullChainDiscoveryClosure -DiscoveredIdentities $expectedIdentities -ClaimedIdentities @("$fullChainRootNamespace.DeletedTests.Gone") -ResidualIdentities $expectedIdentities }
    catch { $staleClaimRejected = $_.Exception.Message.Contains('discovery did not report', [StringComparison]::Ordinal) }
    Assert-Contract $staleClaimRejected 'A frozen FullChain identity that discovery no longer reports must fail closed.'
    $unaccountedRejected = $false
    try { Assert-NervFullChainDiscoveryClosure -DiscoveredIdentities $expectedIdentities -ClaimedIdentities @("$fullChainRootNamespace.AlphaTests.First_case") -ResidualIdentities @("$fullChainRootNamespace.AlphaTests.Second_case") }
    catch { $unaccountedRejected = $_.Exception.Message.Contains('no member and no residual run accounts for', [StringComparison]::Ordinal) }
    Assert-Contract $unaccountedRejected 'A discovered FullChain test that neither a member nor the residual run accounts for must fail closed.'

    # 本项目当前的真实身份必须与 manifest 冻结的 5 条相容：冻结身份是发现集的子集。
    $realDiscoveryFixture = @(@($manifest.members | ForEach-Object { "    $([string]$_.expectedTestIdentities[0])" }) + $listTestsBodyLines)
    $realDiscovered = @(Get-NervFullChainDiscoveredTestIdentities -DiscoveryLines $realDiscoveryFixture -RootNamespace $fullChainRootNamespace)
    $realClaimed = @($manifest.members | ForEach-Object { [string]$_.expectedTestIdentities[0] })
    $realResidual = @(Get-NervFullChainResidualTestIdentities -DiscoveredIdentities $realDiscovered -ClaimedIdentities $realClaimed)
    Assert-NervFullChainDiscoveryClosure -DiscoveredIdentities $realDiscovered -ClaimedIdentities $realClaimed -ResidualIdentities $realResidual
    Assert-Contract ($realResidual.Count -eq $listTestsBodyLines.Count) 'The real frozen FullChain identities must all be claimed, leaving only the injected fixture tests as residual.'

    # --- residual TRX 判定 ------------------------------------------------------------------------
    # 本机变异实证（#3135）：直接复用成员那套逐字身份比较，会在项目里新增一个 `[Theory]` 时误红
    # （TRX 是逐参数用例的、residual 是方法级集合）。因此 residual 走独立判定：身份归一到方法级比
    # 集合，但**每一条参数化用例**都必须通过。
    function New-FullChainResidualTrx {
        param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [object[]] $Cases)

        $definitions = [Collections.Generic.List[string]]::new()
        $results = [Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $Cases.Count; $index++) {
            $case = $Cases[$index]
            $raw = [string]$case.Identity
            $separatorIndex = $raw.LastIndexOf('.', [StringComparison]::Ordinal)
            $class = $raw.Substring(0, $separatorIndex)
            $method = $raw.Substring($separatorIndex + 1)
            $id = "residual-$index"
            $definitions.Add("<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest>")
            $results.Add("<UnitTestResult testId=`"$id`" testName=`"$method`" outcome=`"$([string]$case.Outcome)`" />")
        }
        $trx = "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$($results -join '')</Results><TestDefinitions>$($definitions -join '')</TestDefinitions></TestRun>"
        [IO.File]::WriteAllText($Path, $trx, [Text.UTF8Encoding]::new($false))
    }

    $residualTrxRoot = Join-Path $fixtureRoot 'residual-trx'
    [IO.Directory]::CreateDirectory($residualTrxRoot) | Out-Null
    $residualTrxPath = Join-Path $residualTrxRoot 'residual.trx'
    $residualExpected = @("$fullChainRootNamespace.AlphaTests.First_case", "$fullChainRootNamespace.ThetaTests.Theory_case")
    New-FullChainResidualTrx -Path $residualTrxPath -Cases @(
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.AlphaTests.First_case"; Outcome = 'Passed' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 1)"; Outcome = 'Passed' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 2)"; Outcome = 'Passed' }
    )
    $residualTrxResult = Get-NervFullChainResidualTrxResult -ResultsDirectory $residualTrxRoot -ExpectedTestIdentities $residualExpected
    Assert-Contract ($residualTrxResult.methods -eq 2 -and $residualTrxResult.total -eq 3 -and $residualTrxResult.passed -eq 3 -and $residualTrxResult.failed -eq 0 -and $residualTrxResult.skipped -eq 0) 'A [Theory] must collapse to one residual method identity while every case still counts as executed.'

    New-FullChainResidualTrx -Path $residualTrxPath -Cases @(
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.AlphaTests.First_case"; Outcome = 'Passed' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 1)"; Outcome = 'Passed' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 2)"; Outcome = 'Failed' }
    )
    $residualCaseFailureRejected = $false
    try { Get-NervFullChainResidualTrxResult -ResultsDirectory $residualTrxRoot -ExpectedTestIdentities $residualExpected | Out-Null }
    catch { $residualCaseFailureRejected = $_.Exception.Message.Contains('Theory_case(value: 2)', [StringComparison]::Ordinal) }
    Assert-Contract $residualCaseFailureRejected 'One failing [Theory] case must fail residual coverage and name the failing case.'

    New-FullChainResidualTrx -Path $residualTrxPath -Cases @(
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.AlphaTests.First_case"; Outcome = 'Passed' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 1)"; Outcome = 'NotExecuted' },
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.ThetaTests.Theory_case(value: 2)"; Outcome = 'Passed' }
    )
    $residualSkipRejected = $false
    try { Get-NervFullChainResidualTrxResult -ResultsDirectory $residualTrxRoot -ExpectedTestIdentities $residualExpected | Out-Null }
    catch { $residualSkipRejected = $_.Exception.Message.Contains('0 failed and 0 skipped', [StringComparison]::Ordinal) }
    Assert-Contract $residualSkipRejected 'A silently skipped residual case must fail closed rather than count as coverage.'

    # 「跑了但跑的不是发现到的那一组」必须红：否则 filter 拼错会退化成静默少跑。
    New-FullChainResidualTrx -Path $residualTrxPath -Cases @(
        [pscustomobject]@{ Identity = "$fullChainRootNamespace.AlphaTests.First_case"; Outcome = 'Passed' }
    )
    $residualDriftRejected = $false
    try { Get-NervFullChainResidualTrxResult -ResultsDirectory $residualTrxRoot -ExpectedTestIdentities $residualExpected | Out-Null }
    catch { $residualDriftRejected = $_.Exception.Message.Contains('executed a different identity set', [StringComparison]::Ordinal) }
    Assert-Contract $residualDriftRejected 'Residual coverage must fail when it executes fewer identities than discovery reported.'

    # --- runner 接线：residual 的 claimed 必须取全部 members ---------------------------------------
    # 这是反直觉的一条：本地 `-MemberId one-member` 只跑一个成员时，另外 4 个重依赖成员**不该**落进
    # residual 被无依赖重跑。后人很容易「顺手修正」成 $selectedMembers，那个错不会红、只会让人困惑，
    # 所以在这里钉死，并配一条把它改回 $selectedMembers 的变异对照。
    $runnerSourcePath = Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'
    $runnerSourceText = [IO.File]::ReadAllText($runnerSourcePath)
    function Assert-FullChainResidualClaimSource {
        param([Parameter(Mandatory)] [string] $SourceText, [Parameter(Mandatory)] [string] $Context)

        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($SourceText, [ref]$null, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw "$Context runner source does not parse." }
        $assignments = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text.Contains('$claimedIdentities', [StringComparison]::Ordinal)
        }, $true))
        if ($assignments.Count -ne 1) { throw "$Context must assign `$claimedIdentities exactly once; observed $($assignments.Count)." }
        $rightText = $assignments[0].Right.Extent.Text
        if (-not $rightText.Contains('$manifest.members', [StringComparison]::Ordinal)) {
            throw "$Context must derive FullChain residual claims from every manifest member."
        }
        if ($rightText.Contains('$selectedMembers', [StringComparison]::Ordinal)) {
            throw "$Context must not derive FullChain residual claims from the -MemberId selection."
        }
    }
    Assert-FullChainResidualClaimSource -SourceText $runnerSourceText -Context 'FullChain runner'
    $claimMutations = @(
        [pscustomobject]@{ Name = 'selected-members'; From = '$claimedIdentities = @($manifest.members'; To = '$claimedIdentities = @($selectedMembers' }
    )
    foreach ($claimMutation in $claimMutations) {
        Assert-Contract ($runnerSourceText.IndexOf($claimMutation.From, [StringComparison]::Ordinal) -ge 0) "FullChain residual claim mutation '$($claimMutation.Name)' anchor must exist."
        $mutatedRunner = $runnerSourceText.Replace($claimMutation.From, $claimMutation.To)
        Assert-Contract (-not [string]::Equals($mutatedRunner, $runnerSourceText, [StringComparison]::Ordinal)) "FullChain residual claim mutation '$($claimMutation.Name)' must change the runner."
        $claimMutationRejected = $false
        try { Assert-FullChainResidualClaimSource -SourceText $mutatedRunner -Context 'FullChain runner mutation' }
        catch { $claimMutationRejected = $true }
        Assert-Contract $claimMutationRejected "FullChain residual claim mutation '$($claimMutation.Name)' must be rejected."
    }

    # residual 的执行、记账与失败语义必须真的写在 runner 里；删掉任何一条都不会让成员断言变红。
    #
    # 这里刻意走 AST 而不是文本 IndexOf：本票实测过，`# ` 注释掉整行时文本锚点仍然命中（那一行
    # 还在文件里，只是不再执行），两个变异 R1/R2 因此存活。AST 只看真正会被求值的命令与赋值，
    # 注释掉即消失。
    $runnerAst = [System.Management.Automation.Language.Parser]::ParseInput($runnerSourceText, [ref]$null, [ref]$null)
    function Get-RunnerCommandNames {
        param([Parameter(Mandatory)] $Ast)
        return @($Ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) |
            ForEach-Object { [string]$_.GetCommandName() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    $runnerCommandNames = @(Get-RunnerCommandNames -Ast $runnerAst)
    foreach ($requiredCommand in @('Get-NervFullChainDiscoveredTestIdentities', 'Get-NervFullChainResidualTestIdentities', 'Assert-NervFullChainDiscoveryClosure', 'Get-NervFullChainResidualTrxResult')) {
        Assert-Contract (@($runnerCommandNames | Where-Object { [string]::Equals($_, $requiredCommand, [StringComparison]::Ordinal) }).Count -ge 1) "The FullChain runner must actually invoke '$requiredCommand', not merely mention it."
    }
    $residualDotnetCommands = @($runnerAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        [string]::Equals([string]$node.GetCommandName(), 'Invoke-DotNetOutput', [StringComparison]::Ordinal) -and
        $node.Extent.Text.Contains('full-chain-residual-coverage', [StringComparison]::Ordinal)
    }, $true))
    Assert-Contract ($residualDotnetCommands.Count -eq 1) 'The FullChain runner must execute residual coverage through exactly one governed dotnet invocation.'
    $residualTotalAssignments = @($runnerAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text.Contains('$summary.discovered', [StringComparison]::Ordinal) -and
        $node.Right.Extent.Text.Contains('$summary.residual.discovered', [StringComparison]::Ordinal)
    }, $true))
    Assert-Contract ($residualTotalAssignments.Count -eq 1) 'The FullChain runner must fold residual coverage into the lane-level discovered count so CI logs report tests actually run, not names registered.'
    # 断言必须落在 **条件子树** 上：本票实测过，只看整个 if 的 extent 时，把条件改成 `$false`
    # 仍然绿——因为 body 里的失败消息本身就含那两个字符串。这是「相邻同型守卫兜住变异」的同族。
    $residualOutcomeGuards = @($runnerAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.IfStatementAst] -and
        @($node.Clauses | Where-Object {
            $_.Item1.Extent.Text.Contains('$summary.residual.outcome', [StringComparison]::Ordinal) -and
            $_.Item1.Extent.Text.Contains('$firstFailure', [StringComparison]::Ordinal)
        }).Count -ge 1 -and
        $node.Extent.Text.Contains('$firstFailure =', [StringComparison]::Ordinal)
    }, $true))
    Assert-Contract ($residualOutcomeGuards.Count -eq 1) 'The FullChain runner must turn a non-passed residual outcome into a lane failure, and the guard must be evaluated rather than short-circuited.'
    # 排除注册表是本票刻意不造的逃生口：空注册表拿不出鉴别力证据，而有逃生口就会被用来重新造暗测试。
    foreach ($forbidden in @('excludedTests', 'excludedTestClasses', 'residualExclusions')) {
        Assert-Contract ($runnerSourceText.IndexOf($forbidden, [StringComparison]::Ordinal) -lt 0) "The FullChain runner must not grow an exclusion registry ('$forbidden')."
    }
    $ncrMember = @($manifest.members | Where-Object { [string]::Equals([string]$_.id, 'ncr-rework-cost-closure', [StringComparison]::Ordinal) })
    Assert-Contract ($ncrMember.Count -eq 1) 'The NCR rework cost closure member must exist exactly once.'
    $ncrCleanupFixture = [ordered]@{
        cleanup = [ordered]@{
            managedProcessRemaining = 0
            exactDatabaseRemaining = 0
            ownedComposeServiceRemaining = 0
            errors = @()
        }
    }
    [IO.File]::WriteAllText($memberEvidencePath, (($ncrCleanupFixture | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $verifiedNcrEvidence = Assert-NervFullChainMemberEvidence -Member $ncrMember[0] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot
    Assert-Contract (
        [string]::Equals([string]$verifiedNcrEvidence.cleanup, 'passed', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$verifiedNcrEvidence.diagnosticEvidence, 'entrypoint-evidence-verified', [StringComparison]::Ordinal)
    ) 'Zero NCR process, database, compose-service, and error readbacks must satisfy cleanup evidence.'
    foreach ($readbackName in @('managedProcessRemaining', 'exactDatabaseRemaining', 'ownedComposeServiceRemaining')) {
        $ncrCleanupMutation = ($ncrCleanupFixture | ConvertTo-Json -Depth 10 | ConvertFrom-Json -Depth 10)
        $ncrCleanupMutation.cleanup.$readbackName = 1
        [IO.File]::WriteAllText($memberEvidencePath, (($ncrCleanupMutation | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
        $nonZeroRejected = $false
        try { Assert-NervFullChainMemberEvidence -Member $ncrMember[0] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
        catch { $nonZeroRejected = $_.Exception.Message.Contains("'$readbackName' readback must be zero", [StringComparison]::Ordinal) }
        Assert-Contract $nonZeroRejected "NCR cleanup mutation '$readbackName=1' must fail closed."
    }
    $ncrErrorMutation = ($ncrCleanupFixture | ConvertTo-Json -Depth 10 | ConvertFrom-Json -Depth 10)
    $ncrErrorMutation.cleanup.errors = @('cleanup failed')
    [IO.File]::WriteAllText($memberEvidencePath, (($ncrErrorMutation | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
    $cleanupErrorRejected = $false
    try { Assert-NervFullChainMemberEvidence -Member $ncrMember[0] -MemberResultsDirectory $memberEvidenceRoot -RepositoryRoot $repoRoot | Out-Null }
    catch { $cleanupErrorRejected = $_.Exception.Message.Contains('must contain an empty errors array', [StringComparison]::Ordinal) }
    Assert-Contract $cleanupErrorRejected 'A non-empty NCR cleanup errors array must fail closed.'

    $summaries = @($manifest.members | ForEach-Object {
        [pscustomobject]@{ memberId = $_.id; outcome = 'passed'; cleanup = 'passed'; expected = 1; discovered = 1; passed = 1; failed = 0; skipped = 0; dependencyEvidence = 'passed'; diagnosticEvidence = 'fixture-verified' }
    })
    Assert-NervFullChainTestLaneSummary -SelectedMemberIds $expectedIds -MemberSummaries $summaries
    $summaries[2].cleanup = 'failed'
    $cleanupRejected = $false
    try { Assert-NervFullChainTestLaneSummary -SelectedMemberIds $expectedIds -MemberSummaries $summaries }
    catch { $cleanupRejected = $_.Exception.Message.Contains("cleanup 'failed'", [StringComparison]::Ordinal) }
    Assert-Contract $cleanupRejected 'A FullChain cleanup failure must fail the lane summary.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'FullChain test lane contract tests passed.'
