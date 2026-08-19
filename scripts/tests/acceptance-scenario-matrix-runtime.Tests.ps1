# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the shadow acceptance runtime contract with injected in-process fixture actions
#   Writes:
#     - Temporary workflow and runtime summary fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runtimeLibraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrixRuntime.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$v1ManifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-acceptance-runtime-$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $runtimeLibraryPath -PathType Leaf)) {
    throw "Acceptance scenario matrix runtime library is missing at '$runtimeLibraryPath'."
}
. $runtimeLibraryPath

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Copy-JsonObject {
    param([Parameter(Mandatory)] [object] $Value)

    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50)
}

function Write-RuntimeWorkflowFixture {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [int] $StepTimeoutMinutes = 45,
        [string] $JobName = 'acceptance-scenario-matrix-runtime',
        [string] $StepName = 'Run acceptance scenario matrix',
        [string] $Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1'
    )

    $path = Join-Path $fixtureRoot "$Name.yml"
    $content = @"
name: Acceptance runtime fixture
on:
  workflow_dispatch:
jobs:
  $JobName`:
    runs-on: ubuntu-latest
    timeout-minutes: $($StepTimeoutMinutes + 5)
    steps:
      - name: $StepName
        timeout-minutes: $StepTimeoutMinutes
        run: $Run
"@
    [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
    return $path
}

function New-SalesPlanningArtifact {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ManifestDigest
    )

    $scenario = @($Manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.id, 'sales-order-demand', [StringComparison]::Ordinal)
    })[0]
    $project = $scenario.testProjects[0]
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        repository = 'Mang-X/Nerv-IIP'
        testedSha = '0123456789abcdef0123456789abcdef01234567'
        runId = '123456789'
        runAttempt = 2
        manifestPath = 'scripts/acceptance-scenario-matrix.json'
        manifestDigest = $ManifestDigest
        event = 'workflow_dispatch'
        selectionMode = 'workflow-dispatch-scenario'
        selectionReasons = @('dispatch:sales-order-demand')
        scenarios = @(
            [pscustomobject][ordered]@{
                id = 'sales-order-demand'
                status = 'active'
                tier = 'core'
            }
        )
        projects = @(
            [pscustomobject][ordered]@{
                path = [string]$project.path
                scenarioIds = @('sales-order-demand')
                expectedTestIdentities = @([string]$project.frozenTestIdentities[0])
                discoveredTestIdentities = @([string]$project.frozenTestIdentities[0])
            }
        )
    }
}

function Get-RuntimeArguments {
    param(
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    return @{
        Artifact = $Artifact
        Manifest = $Manifest
        Repository = 'Mang-X/Nerv-IIP'
        TestedSha = '0123456789abcdef0123456789abcdef01234567'
        RunId = '123456789'
        RunAttempt = 2
        ManifestPath = 'scripts/acceptance-scenario-matrix.json'
        ManifestDigest = $ManifestDigest
        Event = 'workflow_dispatch'
        WorkflowPath = $WorkflowPath
        WorkflowJobName = 'acceptance-scenario-matrix-runtime'
        WorkflowStepName = 'Run acceptance scenario matrix'
        SummaryPath = $SummaryPath
        RuntimeAction = $Action
    }
}

function Assert-PreflightRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $script:preflightActionCount = 0
    $summaryPath = Join-Path $fixtureRoot "$Name-summary.json"
    $action = { $script:preflightActionCount++ }
    $arguments = Get-RuntimeArguments -Artifact $Artifact -Manifest $Manifest -ManifestDigest $ManifestDigest -WorkflowPath $WorkflowPath -SummaryPath $summaryPath -Action $action
    $observedMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @arguments | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Preflight mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
    Assert-Contract ($script:preflightActionCount -eq 0) "Preflight mutation '$Name' must execute zero injected actions."
    Assert-Contract (-not (Test-Path -LiteralPath $summaryPath)) "Preflight mutation '$Name' must not create a runtime summary."
}

function New-EquivalenceFixture {
    param([string] $DatabaseName, [int[]] $ProcessIds, [string] $CapSuffix, [string] $StartedAtUtc, [string] $CompletedAtUtc)

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        scenarioId = 'sales-order-demand'
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{
            identity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events'
            expected = 1
            discovered = 1
            passed = 1
            failed = 0
            skipped = 0
        }
        checkpoints = [pscustomobject][ordered]@{
            sourceStateCommittedBeforeMutation = $true
            http200BusinessErrorRejected = $true
            duplicateConverged = $true
            outOfOrderConverged = $true
            firstConsumeFailureRecovered = $true
        }
        diagnostics = [pscustomobject][ordered]@{
            schemas = @('demand_planning', 'erp', 'master_data')
            capturedBeforeCleanup = $true
            secretsRedacted = $true
        }
        cleanup = [pscustomobject][ordered]@{
            managedProcessesRemaining = 0
            disposableDatabasesRemaining = 0
            ownedResourcesRemaining = 0
            errors = @()
        }
        volatile = [pscustomobject][ordered]@{
            databaseName = $DatabaseName
            processIds = @($ProcessIds)
            capSuffix = $CapSuffix
            startedAtUtc = $StartedAtUtc
            completedAtUtc = $CompletedAtUtc
        }
    }
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $manifest = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $manifestPath -V1ManifestPath $v1ManifestPath -RepositoryRoot $repoRoot
    $manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $manifestPath
    $artifact = New-SalesPlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest
    $workflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow'
    $summaryPath = Join-Path $fixtureRoot 'success/runtime-summary.json'

    $script:actionCount = 0
    $runtimeAction = {
        param([object] $Contract)
        $script:actionCount++
        Assert-Contract (Test-Path -LiteralPath $summaryPath -PathType Leaf) 'Runtime summary must exist before the injected action runs.'
        $summaryBeforeAction = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
        Assert-Contract ([string]::Equals([string]$summaryBeforeAction.status, 'running', [StringComparison]::Ordinal)) 'Runtime summary must be running before the injected action runs.'
        Assert-Contract ([string]::Equals([string]$summaryBeforeAction.transitions[-1].state, 'action-started', [StringComparison]::Ordinal)) 'The action-started transition must be atomically persisted before invocation.'
        Assert-Contract ([string]::Equals([string]$Contract.scenario.id, 'sales-order-demand', [StringComparison]::Ordinal)) 'The injected action must receive the exact validated scenario contract.'
        return [pscustomobject]@{ fixtureResult = 'completed' }
    }
    $runtimeArguments = Get-RuntimeArguments -Artifact $artifact -Manifest $manifest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $summaryPath -Action $runtimeAction
    $runtimeResult = Invoke-NervAcceptanceScenarioRuntime @runtimeArguments
    Assert-Contract ($script:actionCount -eq 1) 'A valid runtime contract must invoke the injected action exactly once.'
    Assert-Contract ([string]::Equals([string]$runtimeResult.summary.status, 'completed', [StringComparison]::Ordinal)) 'Successful injected action must complete the runtime summary.'
    Assert-Contract ([string]::Equals((@($runtimeResult.summary.transitions.state) -join '|'), 'preflight-passed|action-started|action-completed', [StringComparison]::Ordinal)) 'Runtime state transitions must be stable and complete.'
    $persistedSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals(($persistedSummary.transitions.state -join '|'), 'preflight-passed|action-started|action-completed', [StringComparison]::Ordinal)) 'Every successful runtime transition must be persisted.'
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $summaryPath) -Filter '*.tmp' -File).Count -eq 0) 'Atomic summary persistence must not leave temporary files.'

    foreach ($mutation in @(
        @{ Name = 'repository'; Artifact = { param($value) $value.repository = 'mang-x/Nerv-IIP' }; Message = 'repository does not match' },
        @{ Name = 'tested-sha'; Artifact = { param($value) $value.testedSha = '1123456789abcdef0123456789abcdef01234567' }; Message = 'testedSha does not match' },
        @{ Name = 'run-id'; Artifact = { param($value) $value.runId = '987654321' }; Message = 'runId does not match' },
        @{ Name = 'run-attempt'; Artifact = { param($value) $value.runAttempt = 3 }; Message = 'runAttempt does not match' },
        @{ Name = 'event-wrong-case'; Artifact = { param($value) $value.event = 'WORKFLOW_DISPATCH' }; Message = 'Planning event' },
        @{ Name = 'manifest-path'; Artifact = { param($value) $value.manifestPath = 'scripts/Acceptance-scenario-matrix.json' }; Message = 'manifestPath does not match' },
        @{ Name = 'manifest-digest'; Artifact = { param($value) $value.manifestDigest = ('f' * 64) }; Message = 'manifestDigest does not match' },
        @{ Name = 'scenario-missing'; Artifact = { param($value) $value.scenarios = @() }; Message = 'exactly one selected scenario' },
        @{ Name = 'scenario-extra'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], [pscustomobject]@{ id = 'wms-delivery-erp'; status = 'active'; tier = 'core' }) }; Message = 'exactly one selected scenario' },
        @{ Name = 'scenario-duplicate'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], (Copy-JsonObject $value.scenarios[0])) }; Message = 'exactly one selected scenario' },
        @{ Name = 'scenario-wrong-case'; Artifact = { param($value) $value.scenarios[0].id = 'Sales-order-demand' }; Message = "must select only 'sales-order-demand'" },
        @{ Name = 'scenario-blocked'; Artifact = { param($value) $value.scenarios[0].id = 'equipment-unavailable-scheduling-mes'; $value.scenarios[0].status = 'blocked'; $value.scenarios[0].tier = 'extended' }; Message = "must select only 'sales-order-demand'" },
        @{ Name = 'selected-status-blocked'; Artifact = { param($value) $value.scenarios[0].status = 'blocked' }; Message = 'must record only active scenarios' },
        @{ Name = 'selected-status-deferred'; Artifact = { param($value) $value.scenarios[0].status = 'deferred' }; Message = 'must record only active scenarios' },
        @{ Name = 'scenario-deferred'; Manifest = { param($value) $value.scenarios[0].status = 'deferred' }; Message = 'must be active/core' },
        @{ Name = 'alias-drift'; Manifest = { param($value) $value.scenarios[0].v1Alias = 'sales-order-demand-planning-drifted' }; Message = 'v1Alias drifted' },
        @{ Name = 'project-drift'; Artifact = { param($value) $value.projects[0].path = 'backend/tests/Drifted/Drifted.csproj' }; Message = 'project set does not exactly equal' },
        @{ Name = 'entrypoint-drift'; Manifest = { param($value) $value.scenarios[0].entrypoint.path = 'scripts/verify-drifted.ps1' }; Message = 'entrypoint drifted' },
        @{ Name = 'identity-drift'; Artifact = { param($value) $value.projects[0].discoveredTestIdentities[0] = 'Nerv.IIP.Drifted.Tests.Drifted' }; Message = 'discovered identities do not exactly equal' }
    )) {
        $mutatedArtifact = Copy-JsonObject $artifact
        $mutatedManifest = Copy-JsonObject $manifest
        if ($null -ne $mutation['Artifact']) { & $mutation['Artifact'] $mutatedArtifact }
        if ($null -ne $mutation['Manifest']) { & $mutation['Manifest'] $mutatedManifest }
        Assert-PreflightRejected -Name $mutation.Name -Artifact $mutatedArtifact -Manifest $mutatedManifest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath -ExpectedMessage $mutation.Message
    }

    $shortWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-short' -StepTimeoutMinutes 37
    Assert-PreflightRejected -Name 'execution-budget-shortened' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -ManifestDigest $manifestDigest -WorkflowPath $shortWorkflowPath -ExpectedMessage 'must be strictly less than'

    $wrongStepWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-step' -StepName 'Run drifted acceptance scenario'
    Assert-PreflightRejected -Name 'workflow-step-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -ManifestDigest $manifestDigest -WorkflowPath $wrongStepWorkflowPath -ExpectedMessage 'exactly one timed'

    $wrongCommandWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-command' -Run 'pwsh scripts/run-full-chain-test-lane.ps1'
    Assert-PreflightRejected -Name 'workflow-command-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -ManifestDigest $manifestDigest -WorkflowPath $wrongCommandWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    $firstEquivalenceInput = New-EquivalenceFixture -DatabaseName 'nerv_shadow_run_1' -ProcessIds @(101, 102) -CapSuffix 'attempt-1-aabbcc' -StartedAtUtc '2026-08-19T01:00:00Z' -CompletedAtUtc '2026-08-19T01:01:00Z'
    $secondEquivalenceInput = New-EquivalenceFixture -DatabaseName 'nerv_shadow_run_2' -ProcessIds @(991, 992) -CapSuffix 'attempt-2-ddeeff' -StartedAtUtc '2026-08-19T02:00:00Z' -CompletedAtUtc '2026-08-19T02:01:00Z'
    $firstVector = New-NervAcceptanceScenarioEquivalenceVector -Result $firstEquivalenceInput
    $secondVector = New-NervAcceptanceScenarioEquivalenceVector -Result $secondEquivalenceInput
    $firstVectorJson = $firstVector | ConvertTo-Json -Depth 50 -Compress
    $secondVectorJson = $secondVector | ConvertTo-Json -Depth 50 -Compress
    Assert-Contract ([string]::Equals($firstVectorJson, $secondVectorJson, [StringComparison]::Ordinal)) 'Database names, PIDs, CAP suffixes, and timestamps must not participate in equivalence.'
    foreach ($volatileName in @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc')) {
        Assert-Contract (-not $firstVectorJson.Contains($volatileName, [StringComparison]::Ordinal)) "Equivalence vector must exclude volatile field '$volatileName'."
    }
    $stableDrift = Copy-JsonObject $secondEquivalenceInput
    $stableDrift.checkpoints.duplicateConverged = $false
    $stableDriftJson = (New-NervAcceptanceScenarioEquivalenceVector -Result $stableDrift | ConvertTo-Json -Depth 50 -Compress)
    Assert-Contract (-not [string]::Equals($firstVectorJson, $stableDriftJson, [StringComparison]::Ordinal)) 'A stable business checkpoint drift must change the equivalence vector.'

    $extraEquivalenceField = Copy-JsonObject $firstEquivalenceInput
    $extraEquivalenceField | Add-Member -NotePropertyName ungoverned -NotePropertyValue $true
    $extraRejected = $false
    try { New-NervAcceptanceScenarioEquivalenceVector -Result $extraEquivalenceField | Out-Null }
    catch { $extraRejected = $_.Exception.Message.Contains('unknown field', [StringComparison]::Ordinal) }
    Assert-Contract $extraRejected 'An extra equivalence result field must fail closed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
