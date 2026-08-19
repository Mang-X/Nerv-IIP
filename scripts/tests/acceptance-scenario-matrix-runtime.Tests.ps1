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

function Get-FixtureFileDigest {
    param([Parameter(Mandatory)] [string] $Path)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([Convert]::ToHexString($sha256.ComputeHash([IO.File]::ReadAllBytes($Path)))).ToLowerInvariant()
    }
    finally { $sha256.Dispose() }
}

function Get-FixtureBytesDigest {
    param([Parameter(Mandatory)] [byte[]] $Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return ([Convert]::ToHexString($sha256.ComputeHash($Bytes))).ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function ConvertTo-JsonFixtureBytes {
    param([Parameter(Mandatory)] [object] $Value)

    return [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 50) + "`n")
}

function Write-JsonFixture {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
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
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [string] $Event = 'workflow_dispatch',
        [scriptblock] $ReadFileBytesAction
    )

    $arguments = @{
        ArtifactPath = $ArtifactPath
        ExpectedArtifactDigest = $ExpectedArtifactDigest
        ManifestFilePath = $ManifestFilePath
        ExpectedManifestDigest = $ExpectedManifestDigest
        V1ManifestPath = $v1ManifestPath
        RepositoryRoot = $repoRoot
        Repository = 'Mang-X/Nerv-IIP'
        TestedSha = '0123456789abcdef0123456789abcdef01234567'
        RunId = '123456789'
        RunAttempt = 2
        ManifestPath = 'scripts/acceptance-scenario-matrix.json'
        Event = $Event
        WorkflowPath = $WorkflowPath
        WorkflowJobName = 'acceptance-scenario-matrix-runtime'
        WorkflowStepName = 'Run acceptance scenario matrix'
        SummaryPath = $SummaryPath
        RuntimeAction = $Action
    }
    if ($null -ne $ReadFileBytesAction) { $arguments.ReadFileBytesAction = $ReadFileBytesAction }
    return $arguments
}

function Assert-PreflightRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [switch] $PreserveArtifactManifestDigest,
        [string] $ExpectedArtifactDigest,
        [string] $ExpectedManifestDigest,
        [string] $ArtifactPathOverride,
        [string] $ManifestFilePathOverride,
        [string] $Event = 'workflow_dispatch',
        [switch] $MutateArtifactBytesAfterDigest,
        [switch] $MutateManifestBytesAfterDigest
    )

    $script:preflightActionCount = 0
    $summaryPath = Join-Path $fixtureRoot "$Name-summary.json"
    $action = { $script:preflightActionCount++ }
    $runtimeManifestPath = Join-Path $fixtureRoot "$Name-manifest.json"
    Write-JsonFixture -Path $runtimeManifestPath -Value $Manifest
    $runtimeManifestDigest = Get-FixtureFileDigest -Path $runtimeManifestPath
    $runtimeArtifact = Copy-JsonObject $Artifact
    if (-not $PreserveArtifactManifestDigest) { $runtimeArtifact.manifestDigest = $runtimeManifestDigest }
    $runtimeArtifactPath = Join-Path $fixtureRoot "$Name-artifact.json"
    Write-JsonFixture -Path $runtimeArtifactPath -Value $runtimeArtifact
    $runtimeArtifactDigest = Get-FixtureFileDigest -Path $runtimeArtifactPath
    if ([string]::IsNullOrEmpty($ExpectedArtifactDigest)) { $ExpectedArtifactDigest = $runtimeArtifactDigest }
    if ([string]::IsNullOrEmpty($ExpectedManifestDigest)) { $ExpectedManifestDigest = $runtimeManifestDigest }
    if ($MutateArtifactBytesAfterDigest) { [IO.File]::AppendAllText($runtimeArtifactPath, " `n", [Text.UTF8Encoding]::new($false)) }
    if ($MutateManifestBytesAfterDigest) { [IO.File]::AppendAllText($runtimeManifestPath, " `n", [Text.UTF8Encoding]::new($false)) }
    if (-not [string]::IsNullOrEmpty($ArtifactPathOverride)) { $runtimeArtifactPath = $ArtifactPathOverride }
    if (-not [string]::IsNullOrEmpty($ManifestFilePathOverride)) { $runtimeManifestPath = $ManifestFilePathOverride }
    $arguments = Get-RuntimeArguments -ArtifactPath $runtimeArtifactPath -ExpectedArtifactDigest $ExpectedArtifactDigest -ManifestFilePath $runtimeManifestPath -ExpectedManifestDigest $ExpectedManifestDigest -WorkflowPath $WorkflowPath -SummaryPath $summaryPath -Action $action -Event $Event
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
            errorCodes = @()
        }
        volatile = [pscustomobject][ordered]@{
            databaseName = $DatabaseName
            processIds = @($ProcessIds)
            capSuffix = $CapSuffix
            startedAtUtc = $StartedAtUtc
            completedAtUtc = $CompletedAtUtc
            cleanupErrors = @()
        }
    }
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $manifest = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $manifestPath -V1ManifestPath $v1ManifestPath -RepositoryRoot $repoRoot
    $manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $manifestPath
    $artifact = New-SalesPlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest
    $artifactPath = Join-Path $fixtureRoot 'planning-artifact.json'
    Write-JsonFixture -Path $artifactPath -Value $artifact
    $artifactDigest = Get-FixtureFileDigest -Path $artifactPath
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
    $runtimeArguments = Get-RuntimeArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $summaryPath -Action $runtimeAction
    $runtimeResult = Invoke-NervAcceptanceScenarioRuntime @runtimeArguments
    Assert-Contract ($script:actionCount -eq 1) 'A valid runtime contract must invoke the injected action exactly once.'
    Assert-Contract ([string]::Equals([string]$runtimeResult.summary.status, 'completed', [StringComparison]::Ordinal)) 'Successful injected action must complete the runtime summary.'
    Assert-Contract ([string]::Equals((@($runtimeResult.summary.transitions.state) -join '|'), 'preflight-passed|action-started|action-completed', [StringComparison]::Ordinal)) 'Runtime state transitions must be stable and complete.'
    $persistedSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals(($persistedSummary.transitions.state -join '|'), 'preflight-passed|action-started|action-completed', [StringComparison]::Ordinal)) 'Every successful runtime transition must be persisted.'
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $summaryPath) -Filter '*.tmp' -File).Count -eq 0) 'Atomic summary persistence must not leave temporary files.'

    $switchedArtifact = Copy-JsonObject $artifact
    $switchedArtifact | Add-Member -NotePropertyName ungovernedSnapshotMarker -NotePropertyValue 'digest-bytes'
    $switchedArtifactBytes = ConvertTo-JsonFixtureBytes -Value $switchedArtifact
    $switchedArtifactDigest = Get-FixtureBytesDigest -Bytes $switchedArtifactBytes
    $script:artifactSnapshotReads = 0
    $artifactSwitchReader = {
        param([string] $Path)
        if ([string]::Equals([IO.Path]::GetFullPath($Path), [IO.Path]::GetFullPath($artifactPath), [StringComparison]::Ordinal)) {
            $script:artifactSnapshotReads++
            if ($script:artifactSnapshotReads -eq 1) { return $switchedArtifactBytes }
        }
        return [IO.File]::ReadAllBytes($Path)
    }
    $script:artifactSwitchActionCount = 0
    $artifactSwitchAction = { $script:artifactSwitchActionCount++ }
    $artifactSwitchSummaryPath = Join-Path $fixtureRoot 'artifact-switch-summary.json'
    $artifactSwitchArguments = Get-RuntimeArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $switchedArtifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $artifactSwitchSummaryPath -Action $artifactSwitchAction -ReadFileBytesAction $artifactSwitchReader
    $artifactSwitchMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @artifactSwitchArguments | Out-Null }
    catch { $artifactSwitchMessage = $_.Exception.Message }
    Assert-Contract ($artifactSwitchMessage.Contains('unknown field', [StringComparison]::Ordinal)) "Artifact snapshot switch must reject the first byte snapshot; observed '$artifactSwitchMessage'."
    Assert-Contract ($script:artifactSnapshotReads -eq 1) 'Runtime must read planning artifact bytes exactly once.'
    Assert-Contract ($script:artifactSwitchActionCount -eq 0) 'Artifact snapshot switch must execute zero runtime actions.'

    $switchedManifest = Copy-JsonObject $manifest
    $switchedManifest.scenarios[0].testProjects = $switchedManifest.scenarios[0].testProjects[0]
    $switchedManifestBytes = ConvertTo-JsonFixtureBytes -Value $switchedManifest
    $switchedManifestDigest = Get-FixtureBytesDigest -Bytes $switchedManifestBytes
    $manifestSwitchArtifact = Copy-JsonObject $artifact
    $manifestSwitchArtifact.manifestDigest = $switchedManifestDigest
    $manifestSwitchArtifactPath = Join-Path $fixtureRoot 'manifest-switch-artifact.json'
    Write-JsonFixture -Path $manifestSwitchArtifactPath -Value $manifestSwitchArtifact
    $manifestSwitchArtifactDigest = Get-FixtureFileDigest -Path $manifestSwitchArtifactPath
    $script:manifestSnapshotReads = 0
    $manifestSwitchReader = {
        param([string] $Path)
        if ([string]::Equals([IO.Path]::GetFullPath($Path), [IO.Path]::GetFullPath($manifestPath), [StringComparison]::Ordinal)) {
            $script:manifestSnapshotReads++
            if ($script:manifestSnapshotReads -eq 1) { return $switchedManifestBytes }
        }
        return [IO.File]::ReadAllBytes($Path)
    }
    $script:manifestSwitchActionCount = 0
    $manifestSwitchAction = { $script:manifestSwitchActionCount++ }
    $manifestSwitchSummaryPath = Join-Path $fixtureRoot 'manifest-switch-summary.json'
    $manifestSwitchArguments = Get-RuntimeArguments -ArtifactPath $manifestSwitchArtifactPath -ExpectedArtifactDigest $manifestSwitchArtifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $switchedManifestDigest -WorkflowPath $workflowPath -SummaryPath $manifestSwitchSummaryPath -Action $manifestSwitchAction -ReadFileBytesAction $manifestSwitchReader
    $manifestSwitchMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @manifestSwitchArguments | Out-Null }
    catch { $manifestSwitchMessage = $_.Exception.Message }
    Assert-Contract ($manifestSwitchMessage.Contains('testProjects must be a non-empty array', [StringComparison]::Ordinal)) "Manifest snapshot switch must validate the first byte snapshot; observed '$manifestSwitchMessage'."
    Assert-Contract ($script:manifestSnapshotReads -eq 1) 'Runtime must read acceptance manifest bytes exactly once.'
    Assert-Contract ($script:manifestSwitchActionCount -eq 0) 'Manifest snapshot switch must execute zero runtime actions.'

    $failedSummaryPath = Join-Path $fixtureRoot 'failure/runtime-summary.json'
    $script:failedActionCount = 0
    $failedAction = {
        $script:failedActionCount++
        throw [InvalidOperationException]::new('fixture-runtime-action-failed')
    }
    $failedArguments = Get-RuntimeArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $failedSummaryPath -Action $failedAction
    $observedFailure = $null
    try { Invoke-NervAcceptanceScenarioRuntime @failedArguments | Out-Null }
    catch { $observedFailure = $_.Exception }
    Assert-Contract ($script:failedActionCount -eq 1) 'A throwing runtime action must be invoked exactly once.'
    Assert-Contract ($observedFailure -is [InvalidOperationException]) 'Runtime action failure must preserve the original exception type.'
    Assert-Contract ([string]::Equals([string]$observedFailure.Message, 'fixture-runtime-action-failed', [StringComparison]::Ordinal)) 'Runtime action failure must preserve the original exception message.'
    $persistedFailureSummary = Get-Content -LiteralPath $failedSummaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$persistedFailureSummary.status, 'failed', [StringComparison]::Ordinal)) 'A throwing runtime action must persist failed status.'
    Assert-Contract ([string]::Equals(($persistedFailureSummary.transitions.state -join '|'), 'preflight-passed|action-started|action-failed', [StringComparison]::Ordinal)) 'A throwing runtime action must atomically persist action-failed as its final transition.'
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $failedSummaryPath) -Filter '*.tmp' -File).Count -eq 0) 'Failed summary persistence must not leave temporary files.'

    foreach ($mutation in @(
        @{ Name = 'repository'; Artifact = { param($value) $value.repository = 'mang-x/Nerv-IIP' }; Message = 'repository does not match' },
        @{ Name = 'tested-sha'; Artifact = { param($value) $value.testedSha = '1123456789abcdef0123456789abcdef01234567' }; Message = 'testedSha does not match' },
        @{ Name = 'run-id'; Artifact = { param($value) $value.runId = '987654321' }; Message = 'runId does not match' },
        @{ Name = 'run-attempt'; Artifact = { param($value) $value.runAttempt = 3 }; Message = 'runAttempt does not match' },
        @{ Name = 'event-wrong-case'; Artifact = { param($value) $value.event = 'WORKFLOW_DISPATCH' }; Message = 'Planning event' },
        @{ Name = 'selection-mode-self-derived'; Artifact = { param($value) $value.selectionMode = 'workflow-dispatch-all-active' }; Message = 'selectionMode does not match expected provenance' },
        @{ Name = 'selection-reasons-self-derived'; Artifact = { param($value) $value.selectionReasons = @('tampered-but-self-derived') }; Message = 'selectionReasons do not exactly equal' },
        @{ Name = 'manifest-path'; Artifact = { param($value) $value.manifestPath = 'scripts/Acceptance-scenario-matrix.json' }; Message = 'manifestPath does not match' },
        @{ Name = 'manifest-digest'; Artifact = { param($value) $value.manifestDigest = ('f' * 64) }; Message = 'manifestDigest does not match'; PreserveManifestDigest = $true },
        @{ Name = 'scenario-missing'; Artifact = { param($value) $value.scenarios = @() }; Message = 'exactly one selected scenario' },
        @{ Name = 'artifact-scenarios-scalar'; Artifact = { param($value) $value.scenarios = $value.scenarios[0] }; Message = 'scenarios must be an array' },
        @{ Name = 'scenario-extra'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], [pscustomobject]@{ id = 'wms-delivery-erp'; status = 'active'; tier = 'core' }) }; Message = 'exactly one selected scenario' },
        @{ Name = 'scenario-duplicate'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], (Copy-JsonObject $value.scenarios[0])) }; Message = 'exactly one selected scenario' },
        @{ Name = 'scenario-wrong-case'; Artifact = { param($value) $value.scenarios[0].id = 'Sales-order-demand' }; Message = "must select only 'sales-order-demand'" },
        @{ Name = 'scenario-blocked'; Artifact = { param($value) $value.scenarios[0].id = 'equipment-unavailable-scheduling-mes'; $value.scenarios[0].status = 'blocked'; $value.scenarios[0].tier = 'extended' }; Message = "must select only 'sales-order-demand'" },
        @{ Name = 'selected-status-blocked'; Artifact = { param($value) $value.scenarios[0].status = 'blocked' }; Message = 'must record only active scenarios' },
        @{ Name = 'selected-status-deferred'; Artifact = { param($value) $value.scenarios[0].status = 'deferred' }; Message = 'must record only active scenarios' },
        @{ Name = 'scenario-deferred'; Manifest = { param($value) $value.scenarios[0].status = 'deferred' }; Message = 'deferredReason' },
        @{ Name = 'manifest-test-projects-scalar'; Manifest = { param($value) $value.scenarios[0].testProjects = $value.scenarios[0].testProjects[0] }; Message = 'testProjects must be a non-empty array' },
        @{ Name = 'manifest-identities-scalar'; Manifest = { param($value) $value.scenarios[0].testProjects[0].frozenTestIdentities = $value.scenarios[0].testProjects[0].frozenTestIdentities[0] }; Message = 'frozenTestIdentities must be an array' },
        @{ Name = 'alias-drift'; Manifest = { param($value) $value.scenarios[0].v1Alias = 'sales-order-demand-planning-drifted' }; Message = 'v1 alias set must exactly match' },
        @{ Name = 'project-drift'; Artifact = { param($value) $value.projects[0].path = 'backend/tests/Drifted/Drifted.csproj' }; Message = 'project set does not exactly equal' },
        @{ Name = 'artifact-projects-scalar'; Artifact = { param($value) $value.projects = $value.projects[0] }; Message = 'projects must be an array' },
        @{ Name = 'artifact-identities-scalar'; Artifact = { param($value) $value.projects[0].expectedTestIdentities = $value.projects[0].expectedTestIdentities[0] }; Message = 'expectedTestIdentities must be an array' },
        @{ Name = 'entrypoint-drift'; Manifest = { param($value) $value.scenarios[0].entrypoint.path = 'scripts/verify-drifted.ps1' }; Message = 'entrypoint must equal v1 entrypoint' },
        @{ Name = 'identity-drift'; Artifact = { param($value) $value.projects[0].discoveredTestIdentities[0] = 'Nerv.IIP.Drifted.Tests.Drifted' }; Message = 'discovered identities do not exactly equal' }
    )) {
        $mutatedArtifact = Copy-JsonObject $artifact
        $mutatedManifest = Copy-JsonObject $manifest
        if ($null -ne $mutation['Artifact']) { & $mutation['Artifact'] $mutatedArtifact }
        if ($null -ne $mutation['Manifest']) { & $mutation['Manifest'] $mutatedManifest }
        Assert-PreflightRejected -Name $mutation.Name -Artifact $mutatedArtifact -Manifest $mutatedManifest -WorkflowPath $workflowPath -ExpectedMessage $mutation.Message -PreserveArtifactManifestDigest:([bool]$mutation['PreserveManifestDigest'])
    }

    Assert-PreflightRejected -Name 'artifact-bytes-after-digest' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'artifact bytes do not match' -MutateArtifactBytesAfterDigest
    Assert-PreflightRejected -Name 'manifest-bytes-after-digest' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'manifest bytes do not match' -MutateManifestBytesAfterDigest
    Assert-PreflightRejected -Name 'artifact-digest-wrong-case' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'expected digest must be a lowercase' -ExpectedArtifactDigest $artifactDigest.ToUpperInvariant()
    Assert-PreflightRejected -Name 'manifest-digest-wrong-case' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'expected digest must be a lowercase' -ExpectedManifestDigest $manifestDigest.ToUpperInvariant()
    $wrongCaseEventArtifact = Copy-JsonObject $artifact
    $wrongCaseEventArtifact.event = 'WORKFLOW_DISPATCH'
    Assert-PreflightRejected -Name 'trusted-event-wrong-case' -Artifact $wrongCaseEventArtifact -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage "Planning event 'WORKFLOW_DISPATCH' is invalid" -Event 'WORKFLOW_DISPATCH'

    $shortWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-short' -StepTimeoutMinutes 37
    Assert-PreflightRejected -Name 'execution-budget-shortened' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $shortWorkflowPath -ExpectedMessage 'must be strictly less than'

    $wrongStepWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-step' -StepName 'Run drifted acceptance scenario'
    Assert-PreflightRejected -Name 'workflow-step-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $wrongStepWorkflowPath -ExpectedMessage 'exactly one timed'

    $wrongCommandWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-command' -Run 'pwsh scripts/run-full-chain-test-lane.ps1'
    Assert-PreflightRejected -Name 'workflow-command-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $wrongCommandWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    $hereStringRun = "|`n          @'`n          pwsh scripts/run-acceptance-scenario-matrix.ps1`n          '@ | Out-Null"
    $hereStringWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-here-string' -Run $hereStringRun
    Assert-PreflightRejected -Name 'workflow-here-string-data' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $hereStringWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    foreach ($nonExecutingCommand in @(
        @{ Name = 'comment'; Run = '# pwsh scripts/run-acceptance-scenario-matrix.ps1' },
        @{ Name = 'assignment'; Run = "`$commandText = 'pwsh scripts/run-acceptance-scenario-matrix.ps1'" },
        @{ Name = 'echo'; Run = "Write-Output 'pwsh scripts/run-acceptance-scenario-matrix.ps1'" },
        @{ Name = 'string-data'; Run = "'pwsh scripts/run-acceptance-scenario-matrix.ps1'" }
    )) {
        $nonExecutingWorkflowPath = Write-RuntimeWorkflowFixture -Name "runtime-workflow-$($nonExecutingCommand.Name)" -Run $nonExecutingCommand.Run
        Assert-PreflightRejected -Name "workflow-$($nonExecutingCommand.Name)-data" -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $nonExecutingWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'
    }

    foreach ($trailingCommand in @(
        @{ Name = 'literal'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 unexpected' },
        @{ Name = 'expression'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 $env:GITHUB_RUN_ID' },
        @{ Name = 'parenthesis'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 (Get-Date)' },
        @{ Name = 'splatting'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 @runtimeArguments' }
    )) {
        $trailingWorkflowPath = Write-RuntimeWorkflowFixture -Name "runtime-workflow-trailing-$($trailingCommand.Name)" -Run $trailingCommand.Run
        Assert-PreflightRejected -Name "workflow-trailing-$($trailingCommand.Name)" -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $trailingWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'
    }

    $firstEquivalenceInput = New-EquivalenceFixture -DatabaseName 'nerv_shadow_run_1' -ProcessIds @(101, 102) -CapSuffix 'attempt-1-aabbcc' -StartedAtUtc '2026-08-19T01:00:00Z' -CompletedAtUtc '2026-08-19T01:01:00Z'
    $secondEquivalenceInput = New-EquivalenceFixture -DatabaseName 'nerv_shadow_run_2' -ProcessIds @(991, 992) -CapSuffix 'attempt-2-ddeeff' -StartedAtUtc '2026-08-19T02:00:00Z' -CompletedAtUtc '2026-08-19T02:01:00Z'
    $firstEquivalenceInput.volatile.cleanupErrors = @('cleanup failed for database nerv_shadow_run_1 pid 101 cap attempt-1-aabbcc at 2026-08-19T01:01:00Z')
    $secondEquivalenceInput.volatile.cleanupErrors = @('cleanup failed for database nerv_shadow_run_2 pid 991 cap attempt-2-ddeeff at 2026-08-19T02:01:00Z')
    $firstVector = New-NervAcceptanceScenarioEquivalenceVector -Result $firstEquivalenceInput -ValidatedScenario $runtimeResult.contract.scenario
    $secondVector = New-NervAcceptanceScenarioEquivalenceVector -Result $secondEquivalenceInput -ValidatedScenario $runtimeResult.contract.scenario
    $firstVectorJson = $firstVector | ConvertTo-Json -Depth 50 -Compress
    $secondVectorJson = $secondVector | ConvertTo-Json -Depth 50 -Compress
    Assert-Contract ([string]::Equals($firstVectorJson, $secondVectorJson, [StringComparison]::Ordinal)) 'Database names, PIDs, CAP suffixes, and timestamps must not participate in equivalence.'
    foreach ($volatileName in @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors')) {
        Assert-Contract (-not $firstVectorJson.Contains($volatileName, [StringComparison]::Ordinal)) "Equivalence vector must exclude volatile field '$volatileName'."
    }
    foreach ($volatileValue in @('nerv_shadow_run_1', '101', 'attempt-1-aabbcc', '2026-08-19T01:01:00Z', 'cleanup failed for database')) {
        Assert-Contract (-not $firstVectorJson.Contains($volatileValue, [StringComparison]::Ordinal)) "Equivalence vector must exclude volatile value '$volatileValue'."
    }

    foreach ($stableStringMutation in @(
        @{ Name = 'conclusion'; Apply = { param($value) $value.conclusion = 'passed-nerv-shadow-run-1-pid-101-attempt-1-aabbcc-20260819' }; Message = 'conclusion must be one of' },
        @{ Name = 'identity'; Apply = { param($value) $value.test.identity = 'Nerv.IIP.Dynamic.nerv_shadow_run_1.pid_101.attempt_1_aabbcc.20260819' }; Message = 'identity must equal' },
        @{ Name = 'diagnostic-schema'; Apply = { param($value) $value.diagnostics.schemas = @('demand_planning', 'erp', 'master_data', 'nerv_shadow_run_1_pid_101_attempt_1_aabbcc_20260819') }; Message = 'schemas must exactly equal' },
        @{ Name = 'cleanup-error-code'; Apply = { param($value) $value.cleanup.errorCodes = @('database-nerv-shadow-run-1-pid-101-attempt-1-aabbcc-at-20260819') }; Message = 'is not allowed by schemaVersion 1' }
    )) {
        $mutatedStableResult = Copy-JsonObject $firstEquivalenceInput
        & $stableStringMutation.Apply $mutatedStableResult
        $stableStringMessage = '<no exception>'
        try { New-NervAcceptanceScenarioEquivalenceVector -Result $mutatedStableResult -ValidatedScenario $runtimeResult.contract.scenario | Out-Null }
        catch { $stableStringMessage = $_.Exception.Message }
        Assert-Contract ($stableStringMessage.Contains($stableStringMutation.Message, [StringComparison]::Ordinal)) "Stable string mutation '$($stableStringMutation.Name)' must fail with '$($stableStringMutation.Message)'; observed '$stableStringMessage'."
    }
    $stableDrift = Copy-JsonObject $secondEquivalenceInput
    $stableDrift.checkpoints.duplicateConverged = $false
    $stableDriftJson = (New-NervAcceptanceScenarioEquivalenceVector -Result $stableDrift -ValidatedScenario $runtimeResult.contract.scenario | ConvertTo-Json -Depth 50 -Compress)
    Assert-Contract (-not [string]::Equals($firstVectorJson, $stableDriftJson, [StringComparison]::Ordinal)) 'A stable business checkpoint drift must change the equivalence vector.'

    $stableCleanupCodeDrift = Copy-JsonObject $secondEquivalenceInput
    $stableCleanupCodeDrift.cleanup.errorCodes = @('owned-resource-cleanup-failed')
    $stableCleanupCodeJson = (New-NervAcceptanceScenarioEquivalenceVector -Result $stableCleanupCodeDrift -ValidatedScenario $runtimeResult.contract.scenario | ConvertTo-Json -Depth 50 -Compress)
    Assert-Contract (-not [string]::Equals($firstVectorJson, $stableCleanupCodeJson, [StringComparison]::Ordinal)) 'A stable cleanup error code drift must change the equivalence vector.'
    Assert-Contract ($stableCleanupCodeJson.Contains('owned-resource-cleanup-failed', [StringComparison]::Ordinal)) 'The equivalence vector must retain canonical cleanup error codes.'

    $invalidCleanupCode = Copy-JsonObject $firstEquivalenceInput
    $invalidCleanupCode.cleanup.errorCodes = @('cleanup failed for database nerv_shadow_run_1')
    $invalidCleanupCodeRejected = $false
    try { New-NervAcceptanceScenarioEquivalenceVector -Result $invalidCleanupCode -ValidatedScenario $runtimeResult.contract.scenario | Out-Null }
    catch { $invalidCleanupCodeRejected = $_.Exception.Message.Contains('must be canonical', [StringComparison]::Ordinal) }
    Assert-Contract $invalidCleanupCodeRejected 'A free-text cleanup error must not enter the stable error code set.'

    $extraEquivalenceField = Copy-JsonObject $firstEquivalenceInput
    $extraEquivalenceField | Add-Member -NotePropertyName ungoverned -NotePropertyValue $true
    $extraRejected = $false
    try { New-NervAcceptanceScenarioEquivalenceVector -Result $extraEquivalenceField -ValidatedScenario $runtimeResult.contract.scenario | Out-Null }
    catch { $extraRejected = $_.Exception.Message.Contains('unknown field', [StringComparison]::Ordinal) }
    Assert-Contract $extraRejected 'An extra equivalence result field must fail closed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
