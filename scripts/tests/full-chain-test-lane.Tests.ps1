# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates FullChain lane manifest, TRX and summary contracts with temporary fixtures
#   Writes:
#     - Temporary TRX fixtures under the operating-system temp directory
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
    foreach ($deniedCase in @(
        [pscustomobject]@{ Kind = 'fullstack'; Elapsed = 401; Required = 1800 },
        [pscustomobject]@{ Kind = 'script'; Elapsed = 701; Required = 1500 },
        [pscustomobject]@{ Kind = 'dotnet'; Elapsed = 1001; Required = 1200 }
    )) {
        $deniedSummary = New-FullChainMemberAdmissionSummary
        $laneCleanupReached = $false
        try {
            $deniedAdmission = Invoke-NervFullChainMemberAdmission -MemberId "denied-$($deniedCase.Kind)" -EntrypointKind $deniedCase.Kind -GlobalDeadlineSeconds 2200 -ElapsedSeconds $deniedCase.Elapsed -FullstackEntrypointTimeoutSeconds 1200 -ScriptEntrypointTimeoutSeconds 900 -DotnetEntrypointTimeoutSeconds 600 -CleanupReserveSeconds 300 -GuardReserveSeconds 300 -MemberSummary $deniedSummary -Action { param($memberId) $invokedMembers.Add($memberId) | Out-Null }
        }
        finally {
            $laneCleanupReached = $true
        }
        Assert-Contract (-not $deniedAdmission.Allowed -and [string]::Equals([string]$deniedAdmission.Reason, 'InsufficientRemainingBudget', [StringComparison]::Ordinal)) "Insufficient remaining time must deny the $($deniedCase.Kind) member with a stable reason."
        Assert-Contract ($deniedAdmission.RemainingSeconds -eq ($deniedCase.Required - 1) -and $deniedAdmission.RequiredSeconds -eq $deniedCase.Required) "The $($deniedCase.Kind) denial must use its governed entrypoint budget."
        Assert-Contract ([string]::Equals(($invokedMembers -join '|'), 'first|second', [StringComparison]::Ordinal)) "A denied $($deniedCase.Kind) member must invoke its target entrypoint zero times."
        Assert-Contract ([string]::Equals([string]$deniedSummary.outcome, 'failed', [StringComparison]::Ordinal) -and [string]::Equals([string]$deniedSummary.cleanup, 'passed', [StringComparison]::Ordinal) -and [string]::Equals([string]$deniedSummary.diagnosticEvidence, 'deadline-admission-denied', [StringComparison]::Ordinal)) "A denied $($deniedCase.Kind) member must emit failure summary evidence without requiring member cleanup."
        Assert-Contract ($laneCleanupReached) "A denied $($deniedCase.Kind) member must leave the lane cleanup path reachable."
    }

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
