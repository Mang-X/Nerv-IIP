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
$runnerPath = Join-Path $repoRoot 'scripts/run-acceptance-scenario-matrix.ps1'
$runtimeLibraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrixRuntime.ps1'
$wmsVerifierPath = Join-Path $repoRoot 'scripts/verify-erp-wms-delivery-completion.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$v1ManifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-acceptance-runtime-$([Guid]::NewGuid().ToString('N'))"
$repositoryFixtureRoot = Join-Path $repoRoot ".superpowers/sdd/runtime-fixtures/$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $runtimeLibraryPath -PathType Leaf)) {
    throw "Acceptance scenario matrix runtime library is missing at '$runtimeLibraryPath'."
}
. $runtimeLibraryPath

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-RuntimeSelectionAccepted {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string[]] $ExpectedScenarioIds
    )

    $selection = Get-NervAcceptanceRuntimeArtifactSelection -Artifact $Artifact -Manifest $Manifest -Event $Event
    $observedIds = [string[]]@($selection.scenarios | ForEach-Object { [string]$_.id })
    Assert-Contract (Test-NervAcceptanceOrdinalSequenceEqual -Left $observedIds -Right $ExpectedScenarioIds) "Selection fixture '$Name' must preserve the artifact scenario order after validating set membership."
}

function Assert-RuntimeSelectionRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $observedMessage = '<no exception>'
    try { Get-NervAcceptanceRuntimeArtifactSelection -Artifact $Artifact -Manifest $Manifest -Event $Event | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Selection mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
}

function Copy-JsonObject {
    param([Parameter(Mandatory)] [object] $Value)

    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 -DateKind String)
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

function New-PlanningArtifact {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string[]] $ScenarioIds,
        [Parameter(Mandatory)] [string] $Event,
        [Parameter(Mandatory)] [string] $SelectionMode,
        [Parameter(Mandatory)] [string[]] $SelectionReasons
    )

    $scenarioById = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($scenario in @($Manifest.scenarios)) {
        $scenarioById.Add([string]$scenario.id, $scenario)
    }
    $scenarios = @($ScenarioIds | ForEach-Object { $scenarioById[[string]$_] })
    $projects = @(Get-NervAcceptancePlanningProjects -Scenarios $scenarios | ForEach-Object {
        [pscustomobject][ordered]@{
            path = [string]$_.path
            scenarioIds = @($_.scenarioIds)
            expectedTestIdentities = @($_.expectedTestIdentities)
            discoveredTestIdentities = @($_.expectedTestIdentities)
        }
    })
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        repository = 'Mang-X/Nerv-IIP'
        testedSha = '0123456789abcdef0123456789abcdef01234567'
        runId = '123456789'
        runAttempt = 2
        manifestPath = 'scripts/acceptance-scenario-matrix.json'
        manifestDigest = $ManifestDigest
        event = $Event
        selectionMode = $SelectionMode
        selectionReasons = @($SelectionReasons)
        scenarios = @($scenarios | ForEach-Object { [pscustomobject][ordered]@{ id = [string]$_.id; status = [string]$_.status; tier = [string]$_.tier } })
        projects = @($projects)
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
        [string] $RepositoryRoot = $repoRoot,
        [string] $V1ManifestPath = $v1ManifestPath,
        [string] $RunAttempt = '2',
        [string] $PlanningRunAttempt = $RunAttempt,
        [scriptblock] $ReadFileBytesAction,
        [string] $ScenarioId = 'sales-order-demand'
    )

    $arguments = @{
        ArtifactPath = $ArtifactPath
        ExpectedArtifactDigest = $ExpectedArtifactDigest
        ManifestFilePath = $ManifestFilePath
        ExpectedManifestDigest = $ExpectedManifestDigest
        V1ManifestPath = $V1ManifestPath
        RepositoryRoot = $RepositoryRoot
        Repository = 'Mang-X/Nerv-IIP'
        TestedSha = '0123456789abcdef0123456789abcdef01234567'
        RunId = '123456789'
        RunAttempt = $RunAttempt
        PlanningRunAttempt = $PlanningRunAttempt
        ManifestPath = 'scripts/acceptance-scenario-matrix.json'
        Event = $Event
        WorkflowPath = $WorkflowPath
        WorkflowJobName = 'acceptance-scenario-matrix-runtime'
        WorkflowStepName = 'Run acceptance scenario matrix'
        ScenarioId = $ScenarioId
        SummaryPath = $SummaryPath
        RuntimeAction = $Action
    }
    if ($null -ne $ReadFileBytesAction) { $arguments.ReadFileBytesAction = $ReadFileBytesAction }
    return $arguments
}

function Get-RunnerArguments {
    param(
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $SummaryPath,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [string] $Event = 'workflow_dispatch',
        [string] $RunAttempt = '2',
        [string] $PlanningRunAttempt = $RunAttempt,
        [string] $ScenarioId = 'sales-order-demand'
    )

    return @{
        ArtifactPath = $ArtifactPath
        ExpectedArtifactDigest = $ExpectedArtifactDigest
        ManifestFilePath = $ManifestFilePath
        ExpectedManifestDigest = $ExpectedManifestDigest
        V1ManifestPath = $v1ManifestPath
        RepositoryRoot = $repoRoot
        Repository = 'Mang-X/Nerv-IIP'
        TestedSha = '0123456789abcdef0123456789abcdef01234567'
        RunId = '123456789'
        RunAttempt = $RunAttempt
        PlanningRunAttempt = $PlanningRunAttempt
        ManifestPath = 'scripts/acceptance-scenario-matrix.json'
        Event = $Event
        WorkflowPath = $WorkflowPath
        WorkflowJobName = 'acceptance-scenario-matrix-runtime'
        WorkflowStepName = 'Run acceptance scenario matrix'
        ScenarioId = $ScenarioId
        SummaryPath = $SummaryPath
        RuntimeAction = $Action
    }
}

function Assert-RunnerBoundaryRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [hashtable] $Overrides = @{},
        [string[]] $SecretMarkers = @()
    )

    $summaryPath = Join-Path $fixtureRoot "runner-boundary-$Name-summary.json"
    $actionContracts = [Collections.Generic.List[object]]::new()
    $action = { param([object] $Contract) $actionContracts.Add($Contract); return $firstEquivalenceInput }.GetNewClosure()
    $arguments = Get-RunnerArguments `
        -ArtifactPath $ArtifactPath `
        -ExpectedArtifactDigest $ArtifactDigest `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest $ManifestDigest `
        -WorkflowPath $WorkflowPath `
        -SummaryPath $summaryPath `
        -Action $action
    foreach ($key in $Overrides.Keys) { $arguments[$key] = $Overrides[$key] }
    $observedMessage = '<no exception>'
    try { & $runnerPath @arguments | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
    Assert-Contract ($actionContracts.Count -eq 0) "Runner boundary mutation '$Name' must execute zero actions."
    Assert-Contract (Test-Path -LiteralPath $summaryPath -PathType Leaf) "Runner boundary mutation '$Name' must persist a final summary."
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$summary.status, 'failed', [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' must persist failed status."
    Assert-Contract ([string]::Equals(($summary.transitions.state -join '|'), 'preflight-started|preflight-failed', [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' must persist preflight failure."
    Assert-Contract ([string]::Equals([string]$summary.failureClassification, 'preflight-failed', [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' must classify the preflight failure."
    foreach ($field in @('repository', 'testedSha', 'runId', 'event', 'runAttempt')) {
        Assert-Contract ($null -eq $summary.PSObject.Properties[$field].Value) "Runner boundary mutation '$Name' must keep unvalidated summary field '$field' null."
    }
    $summaryJson = $summary | ConvertTo-Json -Depth 50 -Compress
    foreach ($marker in $SecretMarkers) {
        Assert-Contract (-not $observedMessage.Contains($marker, [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' exception must not echo raw marker '$marker'."
        Assert-Contract (-not $summaryJson.Contains($marker, [StringComparison]::Ordinal)) "Runner boundary mutation '$Name' summary must not persist raw marker '$marker'."
    }
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
        [string] $V1ManifestPathOverride,
        [string] $RepositoryRootOverride,
        [string] $Event = 'workflow_dispatch',
        [switch] $MutateArtifactBytesAfterDigest,
        [switch] $MutateManifestBytesAfterDigest
    )

    $script:preflightActionCount = 0
    $summaryPath = Join-Path $fixtureRoot "$Name-summary.json"
    $action = { $script:preflightActionCount++ }
    $runtimeManifestBytesPath = Join-Path $fixtureRoot "$Name-manifest.json"
    Write-JsonFixture -Path $runtimeManifestBytesPath -Value $Manifest
    $runtimeManifestDigest = Get-FixtureFileDigest -Path $runtimeManifestBytesPath
    $runtimeArtifact = Copy-JsonObject $Artifact
    if (-not $PreserveArtifactManifestDigest) { $runtimeArtifact.manifestDigest = $runtimeManifestDigest }
    $runtimeArtifactPath = Join-Path $fixtureRoot "$Name-artifact.json"
    Write-JsonFixture -Path $runtimeArtifactPath -Value $runtimeArtifact
    $runtimeArtifactDigest = Get-FixtureFileDigest -Path $runtimeArtifactPath
    if ([string]::IsNullOrEmpty($ExpectedArtifactDigest)) { $ExpectedArtifactDigest = $runtimeArtifactDigest }
    if ([string]::IsNullOrEmpty($ExpectedManifestDigest)) { $ExpectedManifestDigest = $runtimeManifestDigest }
    if ($MutateArtifactBytesAfterDigest) { [IO.File]::AppendAllText($runtimeArtifactPath, " `n", [Text.UTF8Encoding]::new($false)) }
    if ($MutateManifestBytesAfterDigest) { [IO.File]::AppendAllText($runtimeManifestBytesPath, " `n", [Text.UTF8Encoding]::new($false)) }
    if (-not [string]::IsNullOrEmpty($ArtifactPathOverride)) { $runtimeArtifactPath = $ArtifactPathOverride }
    $runtimeManifestPath = if ([string]::IsNullOrEmpty($ManifestFilePathOverride)) { $manifestPath } else { $ManifestFilePathOverride }
    $runtimeV1ManifestPath = if ([string]::IsNullOrEmpty($V1ManifestPathOverride)) { $v1ManifestPath } else { $V1ManifestPathOverride }
    $runtimeRepositoryRoot = if ([string]::IsNullOrEmpty($RepositoryRootOverride)) { $repoRoot } else { $RepositoryRootOverride }
    $manifestBytesPath = $runtimeManifestBytesPath
    $canonicalManifestPath = $manifestPath
    $readFileBytesAction = {
        param([string] $Path)
        if ([string]::Equals([IO.Path]::GetFullPath($Path), [IO.Path]::GetFullPath($canonicalManifestPath), [StringComparison]::Ordinal)) {
            return [IO.File]::ReadAllBytes($manifestBytesPath)
        }
        return [IO.File]::ReadAllBytes($Path)
    }.GetNewClosure()
    $arguments = Get-RuntimeArguments -ArtifactPath $runtimeArtifactPath -ExpectedArtifactDigest $ExpectedArtifactDigest -ManifestFilePath $runtimeManifestPath -ExpectedManifestDigest $ExpectedManifestDigest -WorkflowPath $WorkflowPath -SummaryPath $summaryPath -Action $action -Event $Event -V1ManifestPath $runtimeV1ManifestPath -RepositoryRoot $runtimeRepositoryRoot -ReadFileBytesAction $readFileBytesAction
    $observedMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @arguments | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Preflight mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
    Assert-Contract ($script:preflightActionCount -eq 0) "Preflight mutation '$Name' must execute zero injected actions."
    Assert-Contract (Test-Path -LiteralPath $summaryPath -PathType Leaf) "Preflight mutation '$Name' must persist a final summary."
    $failedSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$failedSummary.status, 'failed', [StringComparison]::Ordinal)) "Preflight mutation '$Name' must persist failed status."
    Assert-Contract ([string]::Equals(($failedSummary.transitions.state -join '|'), 'preflight-started|preflight-failed', [StringComparison]::Ordinal)) "Preflight mutation '$Name' must persist preflight failure before rethrowing."
}

function Replace-FirstOrdinal {
    param(
        [Parameter(Mandatory)] [string] $Value,
        [Parameter(Mandatory)] [string] $OldValue,
        [Parameter(Mandatory)] [string] $NewValue
    )

    $index = $Value.IndexOf($OldValue, [StringComparison]::Ordinal)
    if ($index -lt 0) { throw "Raw JSON fixture token '$OldValue' was not found." }
    return $Value.Substring(0, $index) + $NewValue + $Value.Substring($index + $OldValue.Length)
}

function Assert-RawJsonPreflightRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $ArtifactJson,
        [Parameter(Mandatory)] [string] $ManifestJson,
        [Parameter(Mandatory)] [string] $V1ManifestJson,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $artifactBytes = [Text.UTF8Encoding]::new($false).GetBytes($ArtifactJson)
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($ManifestJson)
    $v1ManifestBytes = [Text.UTF8Encoding]::new($false).GetBytes($V1ManifestJson)
    $rawArtifactPath = Join-Path $fixtureRoot "$Name-raw-artifact.json"
    Write-JsonFixture -Path $rawArtifactPath -Value ([pscustomobject]@{ placeholder = $true })
    $rawArtifactFullPath = [IO.Path]::GetFullPath($rawArtifactPath)
    $canonicalManifestFullPath = [IO.Path]::GetFullPath($manifestPath)
    $canonicalV1FullPath = [IO.Path]::GetFullPath($v1ManifestPath)
    $readRawBytesAction = {
        param([string] $Path)
        $fullPath = [IO.Path]::GetFullPath($Path)
        if ([string]::Equals($fullPath, $rawArtifactFullPath, [StringComparison]::Ordinal)) { return $artifactBytes }
        if ([string]::Equals($fullPath, $canonicalManifestFullPath, [StringComparison]::Ordinal)) { return $manifestBytes }
        if ([string]::Equals($fullPath, $canonicalV1FullPath, [StringComparison]::Ordinal)) { return $v1ManifestBytes }
        return [IO.File]::ReadAllBytes($Path)
    }.GetNewClosure()

    $script:rawJsonActionCount = 0
    $summaryPath = Join-Path $fixtureRoot "$Name-raw-summary.json"
    $arguments = Get-RuntimeArguments `
        -ArtifactPath $rawArtifactPath `
        -ExpectedArtifactDigest (Get-FixtureBytesDigest -Bytes $artifactBytes) `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest (Get-FixtureBytesDigest -Bytes $manifestBytes) `
        -WorkflowPath $WorkflowPath `
        -SummaryPath $summaryPath `
        -Action { $script:rawJsonActionCount++ } `
        -ReadFileBytesAction $readRawBytesAction
    $observedMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @arguments | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Raw JSON mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
    Assert-Contract ($script:rawJsonActionCount -eq 0) "Raw JSON mutation '$Name' must execute zero injected actions."
    Assert-Contract (Test-Path -LiteralPath $summaryPath -PathType Leaf) "Raw JSON mutation '$Name' must persist a final summary."
    $failedSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$failedSummary.status, 'failed', [StringComparison]::Ordinal)) "Raw JSON mutation '$Name' must persist failed status."
    Assert-Contract ([string]::Equals(($failedSummary.transitions.state -join '|'), 'preflight-started|preflight-failed', [StringComparison]::Ordinal)) "Raw JSON mutation '$Name' must persist preflight failure before rethrowing."
}

function New-EquivalenceFixture {
    param([string] $Track, [string] $DatabaseName, [int[]] $ProcessIds, [string] $CapSuffix, [string] $StartedAtUtc, [string] $CompletedAtUtc)

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = [pscustomobject][ordered]@{
            repository = 'Mang-X/Nerv-IIP'
            runId = '123456789'
            runAttempt = 2
            testedSha = '0123456789abcdef0123456789abcdef01234567'
            manifestDigest = $manifestDigest
            scenarioId = 'sales-order-demand'
        }
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{
            identity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events'
            expected = 1
            discovered = 1
            passed = 1
            failed = 0
            skipped = 0
        }
        businessFacts = [pscustomobject][ordered]@{
            sourceStateCommittedBeforeMutation = $true
            changeV2Converged = $true
            changeV3Converged = $true
            duplicateConverged = $true
            outOfOrderConverged = $true
            cancellationConverged = $true
        }
        diagnostics = [pscustomobject][ordered]@{
            schemas = @('demand_planning', 'erp', 'master_data')
            failureCaptureSupported = $true
            failureDiagnosticsCaptured = $false
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
            ports = [pscustomobject][ordered]@{ masterData = 5101; erp = 5102; demandPlanning = 5103 }
            paths = [pscustomobject][ordered]@{ businessEvidence = '/tmp/evidence.json'; probeTrx = '/tmp/probe.trx'; cleanupEvidence = '/tmp/cleanup.json'; canonicalResult = '/tmp/result.json' }
        }
    }
}

function New-WmsEquivalenceFixture {
    param([string] $Track, [string] $VolatileMarker)

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = [pscustomobject][ordered]@{
            repository = 'Mang-X/Nerv-IIP'
            runId = '123456789'
            runAttempt = 2
            testedSha = '0123456789abcdef0123456789abcdef01234567'
            manifestDigest = $manifestDigest
            scenarioId = 'wms-delivery-erp'
        }
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{
            identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts'
            expected = 1
            discovered = 1
            passed = 1
            failed = 0
            skipped = 0
        }
        businessFacts = [pscustomobject][ordered]@{
            outboundAssigned = $true
            pickingLifecycleCompleted = $true
            outboundCompleted = $true
            deliveryCompleted = $true
            receivableCreated = $true
            completionReplayConverged = $true
            repeatedEventConverged = $true
        }
        diagnostics = [pscustomobject][ordered]@{
            schemas = @('erp', 'inventory', 'wms')
            failureCaptureSupported = $true
            failureDiagnosticsCaptured = $false
            secretsRedacted = $true
        }
        cleanup = [pscustomobject][ordered]@{
            managedProcessesRemaining = 0
            disposableDatabasesRemaining = 0
            ownedResourcesRemaining = 0
            errorCodes = @()
        }
        volatile = [pscustomobject][ordered]@{
            databaseName = "db-$VolatileMarker"
            processIds = @(701, 702, 703)
            capSuffix = "cap-$VolatileMarker"
            startedAtUtc = '2026-08-24T00:00:00.0000000+00:00'
            completedAtUtc = '2026-08-24T00:01:00.0000000+00:00'
            cleanupErrors = @()
            ports = [pscustomobject][ordered]@{ erp = 42001; wms = 42002; inventory = 42003 }
            paths = [pscustomobject][ordered]@{
                businessEvidence = "/tmp/$VolatileMarker/evidence.json"
                probeTrx = "/tmp/$VolatileMarker/probe.trx"
                cleanupEvidence = "/tmp/$VolatileMarker/evidence.json"
                canonicalResult = "/tmp/$VolatileMarker/result.json"
            }
        }
    }
}

function Assert-ResultRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Results,
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [Parameter(Mandatory)] [string] $ArtifactDigest,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [string] $ScenarioId = 'sales-order-demand'
    )

    $summaryPath = Join-Path $fixtureRoot "result-$Name-summary.json"
    $capturedResults = @($Results)
    $action = { return $capturedResults }.GetNewClosure()
    $arguments = Get-RunnerArguments `
        -ArtifactPath $ArtifactPath `
        -ExpectedArtifactDigest $ArtifactDigest `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest $ManifestDigest `
        -WorkflowPath $WorkflowPath `
        -SummaryPath $summaryPath `
        -Action $action `
        -ScenarioId $ScenarioId
    $observedMessage = '<no exception>'
    try { & $runnerPath @arguments | Out-Null }
    catch { $observedMessage = $_.Exception.Message }
    Assert-Contract ($observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)) "Result mutation '$Name' must fail with '$ExpectedMessage'; observed '$observedMessage'."
    Assert-Contract (Test-Path -LiteralPath $summaryPath -PathType Leaf) "Result mutation '$Name' must persist a final summary."
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$summary.status, 'failed', [StringComparison]::Ordinal)) "Result mutation '$Name' must persist failed status."
    Assert-Contract ([string]::Equals([string]$summary.transitions[-1].state, 'result-validation-failed', [StringComparison]::Ordinal)) "Result mutation '$Name' must persist result-validation-failed as the final transition."
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $summaryPath) -Filter '*.tmp' -File).Count -eq 0) "Result mutation '$Name' must not leave temporary summary files."
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    . (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
    $wmsDiagnosticSecret = 'man527-secret-token'
    $protectedWmsDiagnostic = Protect-NervAcceptanceWmsDiagnosticText `
        -Text "Authorization: Bearer $wmsDiagnosticSecret; Password=database-secret; endpoint-secret" `
        -SensitiveValues @($wmsDiagnosticSecret, 'database-secret', 'endpoint-secret')
    Assert-Contract (-not $protectedWmsDiagnostic.Contains($wmsDiagnosticSecret, [StringComparison]::Ordinal) -and
        -not $protectedWmsDiagnostic.Contains('database-secret', [StringComparison]::Ordinal) -and
        -not $protectedWmsDiagnostic.Contains('endpoint-secret', [StringComparison]::Ordinal)) 'MAN-527 diagnostic redaction must remove every caller-declared sensitive value.'
    Assert-Contract ($protectedWmsDiagnostic.Contains('<redacted>', [StringComparison]::Ordinal)) 'MAN-527 diagnostic redaction must retain an explicit redaction marker.'
    $manifest = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $manifestPath -V1ManifestPath $v1ManifestPath -RepositoryRoot $repoRoot
    $manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $manifestPath
    $artifact = New-SalesPlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest
    $artifactPath = Join-Path $fixtureRoot 'planning-artifact.json'
    Write-JsonFixture -Path $artifactPath -Value $artifact
    $artifactDigest = Get-FixtureFileDigest -Path $artifactPath
    $workflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow'
    $summaryPath = Join-Path $fixtureRoot 'success/runtime-summary.json'

    $firstEquivalenceInput = New-EquivalenceFixture -Track 'shadow' -DatabaseName 'nerv_shadow_run_1' -ProcessIds @(101, 102) -CapSuffix 'attempt-1-aabbcc' -StartedAtUtc '2026-08-19T01:00:00Z' -CompletedAtUtc '2026-08-19T01:01:00Z'
    $secondEquivalenceInput = New-EquivalenceFixture -Track 'v1' -DatabaseName 'nerv_shadow_run_2' -ProcessIds @(991, 992) -CapSuffix 'attempt-2-ddeeff' -StartedAtUtc '2026-08-19T02:00:00Z' -CompletedAtUtc '2026-08-19T02:01:00Z'

    $wmsArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds @('wms-delivery-erp') -Event 'workflow_dispatch' -SelectionMode 'workflow-dispatch-scenario' -SelectionReasons @('dispatch:wms-delivery-erp')
    $wmsArtifactPath = Join-Path $fixtureRoot 'wms/planning-artifact.json'
    Write-JsonFixture -Path $wmsArtifactPath -Value $wmsArtifact
    $wmsSummaryPath = Join-Path $fixtureRoot 'wms/runtime-summary.json'
    $wmsActionContracts = [Collections.Generic.List[object]]::new()
    $wmsCanonicalResult = New-WmsEquivalenceFixture -Track shadow -VolatileMarker wms-shadow
    $wmsBusinessEvidence = [pscustomobject][ordered]@{
        scenarioStatus = 'passed'
        deliveryOrderNo = 'DO-MAN527-1234ABCD'
        wmsOutboundOrder = [pscustomobject][ordered]@{
            firstAssignment = [pscustomobject][ordered]@{ poolCode = 'POOL-MAN527-SHIPPING-1234ABCD'; operatorPrincipalId = 'man527-operator-1234abcd' }
            pickingLifecycle = 'public create/read/assign/start/progress/complete for every outbound line'
            pickingLifecycleCompleted = $true
            completionHttpReplay = 'same idempotency key accepted twice'
            completionHttpReplayConverged = $true
            completionReadback = [pscustomobject][ordered]@{ status = 'Completed'; completedAtUtc = '2026-08-24T00:00:30Z' }
        }
        erpDelivery = [pscustomobject][ordered]@{ status = 'completed'; shippedQuantity = 2; shippedAtUtc = '2026-08-24T00:00:00Z'; completedAtUtc = '2026-08-24T00:01:00Z' }
        accountReceivable = [pscustomobject][ordered]@{ receivableNo = 'AR-001'; sourceDocumentNo = 'DO-MAN527-1234ABCD' }
        repeatedEvent = 'same event id published twice through Redis; one delivery projection, one receivable, one target-consumer durable inbox row, no target-consumer dead letter'
        repeatedEventConverged = $true
    }
    $wmsCounters = [pscustomobject][ordered]@{ total = 1; executed = 1; passed = 1; failed = 0; skipped = 0 }
    $wmsCleanup = [pscustomobject][ordered]@{ managedProcessRemaining = 0; exactDatabaseRemaining = 0; postgres = 'owned-stopped'; redis = 'owned-stopped'; errors = @() }
    $wmsDiagnostics = [pscustomobject][ordered]@{ failureCaptureSupported = $true; failureDiagnosticsCaptured = $false; secretsRedacted = $true; artifactPaths = @(); errors = @() }
    $wmsVolatile = [pscustomobject][ordered]@{
        databaseName = 'man527_1234567890abcdef1234567890abcdef'
        processIds = @(701, 702, 703)
        capSuffix = 'man527-123456789abc'
        startedAtUtc = '2026-08-24T00:00:00Z'
        completedAtUtc = '2026-08-24T00:01:00Z'
        ports = [pscustomobject][ordered]@{ erp = 42001; wms = 42002; inventory = 42003 }
        paths = [pscustomobject][ordered]@{ businessEvidence = '/tmp/evidence.json'; probeTrx = '/tmp/probe.trx'; cleanupEvidence = '/tmp/evidence.json'; canonicalResult = '/tmp/result.json' }
    }
    $wmsBuiltCanonical = New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $wmsCanonicalResult.provenance -Track shadow -BusinessEvidence $wmsBusinessEvidence -TestCounters $wmsCounters -CleanupEvidence $wmsCleanup -DiagnosticEvidence $wmsDiagnostics -Volatile $wmsVolatile
    Assert-Contract ([string]::Equals([string]$wmsBuiltCanonical.provenance.scenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal) -and $wmsBuiltCanonical.businessFacts.outboundCompleted -and $wmsBuiltCanonical.businessFacts.repeatedEventConverged) 'The MAN-527 adapter must construct the WMS canonical result from WMS completion evidence, exact TRX counters, and cleanup readback.'
    foreach ($wmsInputMutation in @(
        @{ Name = 'bad-provenance'; Message = 'runId must be a positive'; Provenance = { param($value) $value.runId = '01' } },
        @{ Name = 'missing-business-evidence'; Message = 'missing required field'; Business = { param($value) $value.PSObject.Properties.Remove('accountReceivable') } },
        @{ Name = 'empty-assignment'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.firstAssignment.poolCode = '' } },
        @{ Name = 'picking-not-completed'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.pickingLifecycleCompleted = $false } },
        @{ Name = 'outbound-pending'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionReadback.status = 'Pending' } },
        @{ Name = 'outbound-completion-time-missing'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionReadback.completedAtUtc = '' } },
        @{ Name = 'delivery-pending'; Message = 'every business checkpoint'; Business = { param($value) $value.erpDelivery.status = 'pending' } },
        @{ Name = 'completion-replay-not-converged'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionHttpReplayConverged = $false } },
        @{ Name = 'repeated-event-not-converged'; Message = 'every business checkpoint'; Business = { param($value) $value.repeatedEventConverged = $false } },
        @{ Name = 'business-checkpoint-string-false'; Message = 'business checkpoint flags must be JSON booleans'; Business = { param($value) $value.wmsOutboundOrder.pickingLifecycleCompleted = 'false' } },
        @{ Name = 'extra-test-identity'; Message = 'exact TRX counts'; Counters = { param($value) $value.total = 2; $value.executed = 2; $value.passed = 2 } },
        @{ Name = 'zero-execution'; Message = 'exact TRX counts'; Counters = { param($value) $value.total = 0; $value.executed = 0; $value.passed = 0 } },
        @{ Name = 'managed-process-residue'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.managedProcessRemaining = 1 } },
        @{ Name = 'database-residue'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.exactDatabaseRemaining = 1 } },
        @{ Name = 'cleanup-error'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.errors = @('stop-erp: still running') } },
        @{ Name = 'postgres-pending'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.postgres = 'owned-pending-cleanup' } },
        @{ Name = 'redis-pending'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.redis = 'owned-pending-cleanup' } },
        @{ Name = 'diagnostic-capture-unsupported'; Message = 'diagnostic failure capture'; Diagnostics = { param($value) $value.failureCaptureSupported = $false } },
        @{ Name = 'success-with-failure-diagnostics'; Message = 'must not claim failure diagnostics'; Diagnostics = { param($value) $value.failureDiagnosticsCaptured = $true } },
        @{ Name = 'diagnostics-not-redacted'; Message = 'diagnostic secrets must be redacted'; Diagnostics = { param($value) $value.secretsRedacted = $false } },
        @{ Name = 'diagnostic-checkpoint-string-false'; Message = 'diagnostic evidence secretsRedacted must be a JSON boolean'; Diagnostics = { param($value) $value.secretsRedacted = 'false' } },
        @{ Name = 'success-with-diagnostic-artifact'; Message = 'must not retain failure diagnostic artifacts'; Diagnostics = { param($value) $value.artifactPaths = @('/tmp/failure-summary.json') } },
        @{ Name = 'diagnostic-capture-error'; Message = 'diagnostic capture errors must be empty'; Diagnostics = { param($value) $value.errors = @('capture-failed') } }
    )) {
        $mutatedProvenance = Copy-JsonObject $wmsCanonicalResult.provenance
        $mutatedBusiness = Copy-JsonObject $wmsBusinessEvidence
        $mutatedCounters = Copy-JsonObject $wmsCounters
        $mutatedCleanup = Copy-JsonObject $wmsCleanup
        $mutatedDiagnostics = Copy-JsonObject $wmsDiagnostics
        if ($null -ne $wmsInputMutation['Provenance']) { & $wmsInputMutation['Provenance'] $mutatedProvenance }
        if ($null -ne $wmsInputMutation['Business']) { & $wmsInputMutation['Business'] $mutatedBusiness }
        if ($null -ne $wmsInputMutation['Counters']) { & $wmsInputMutation['Counters'] $mutatedCounters }
        if ($null -ne $wmsInputMutation['Cleanup']) { & $wmsInputMutation['Cleanup'] $mutatedCleanup }
        if ($null -ne $wmsInputMutation['Diagnostics']) { & $wmsInputMutation['Diagnostics'] $mutatedDiagnostics }
        $mutationMessage = '<no exception>'
        try { New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $mutatedProvenance -Track shadow -BusinessEvidence $mutatedBusiness -TestCounters $mutatedCounters -CleanupEvidence $mutatedCleanup -DiagnosticEvidence $mutatedDiagnostics -Volatile $wmsVolatile | Out-Null }
        catch { $mutationMessage = $_.Exception.Message }
        Assert-Contract ($mutationMessage.Contains([string]$wmsInputMutation.Message, [StringComparison]::Ordinal)) "WMS canonical input mutation '$($wmsInputMutation.Name)' must fail closed; observed '$mutationMessage'."
    }

    $validPickingReadbacks = @(
        [pscustomobject]@{ status = 'Completed'; plannedQuantity = 1; executedQuantity = 1; completedAtUtc = '2026-08-24T00:00:00Z' },
        [pscustomobject]@{ status = 'completed'; plannedQuantity = 2; executedQuantity = 2; completedAtUtc = '2026-08-24T00:00:01Z' }
    )
    Assert-Contract (Test-NervAcceptanceWmsPickingReadbacks -Readbacks $validPickingReadbacks -RequestedQuantities @(1, 2)) 'Completed public picking readbacks must satisfy the WMS picking checkpoint.'
    foreach ($pickingMutation in @(
        @{ Name = 'status'; Apply = { param($value) $value[0].status = 'InProgress' } },
        @{ Name = 'planned-quantity'; Apply = { param($value) $value[0].plannedQuantity = 2 } },
        @{ Name = 'requested-quantity'; Apply = { param($value) $value[0].plannedQuantity = 2; $value[0].executedQuantity = 2 } },
        @{ Name = 'completed-at'; Apply = { param($value) $value[0].completedAtUtc = '' } }
    )) {
        $mutatedPickingReadbacks = Copy-JsonObject $validPickingReadbacks
        & $pickingMutation.Apply $mutatedPickingReadbacks
        Assert-Contract (-not (Test-NervAcceptanceWmsPickingReadbacks -Readbacks $mutatedPickingReadbacks -RequestedQuantities @(1, 2))) "Picking readback mutation '$($pickingMutation.Name)' must fail closed."
    }
    Assert-Contract (-not (Test-NervAcceptanceWmsPickingReadbacks -Readbacks @($validPickingReadbacks[0]) -RequestedQuantities @(1, 2))) 'Missing public picking readbacks must fail closed.'

    $firstCompletionResponse = [pscustomobject]@{ data = [pscustomobject]@{ requestId = 'movement-request-1' } }
    $replayedCompletionResponse = [pscustomobject]@{ data = [pscustomobject]@{ requestId = 'movement-request-1' } }
    Assert-Contract (Test-NervAcceptanceWmsCompletionReplay -FirstCompletion $firstCompletionResponse -ReplayCompletion $replayedCompletionResponse) 'Matching non-empty completion requestIds must satisfy the WMS replay checkpoint.'
    foreach ($completionMutation in @(
        @{ Name = 'empty-first'; First = ''; Replay = 'movement-request-1' },
        @{ Name = 'empty-replay'; First = 'movement-request-1'; Replay = '' },
        @{ Name = 'both-empty'; First = ''; Replay = '' },
        @{ Name = 'ordinal-mismatch'; First = 'movement-request-1'; Replay = 'MOVEMENT-REQUEST-1' }
    )) {
        $mutatedFirstCompletion = [pscustomobject]@{ data = [pscustomobject]@{ requestId = $completionMutation.First } }
        $mutatedReplayCompletion = [pscustomobject]@{ data = [pscustomobject]@{ requestId = $completionMutation.Replay } }
        Assert-Contract (-not (Test-NervAcceptanceWmsCompletionReplay -FirstCompletion $mutatedFirstCompletion -ReplayCompletion $mutatedReplayCompletion)) "Completion replay mutation '$($completionMutation.Name)' must fail closed."
    }

    Assert-Contract (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Completed'; completedAtUtc = '2026-08-24T00:00:30Z' })) 'A public WMS outbound readback must prove both Completed status and a completion timestamp.'
    Assert-Contract (-not (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Pending'; completedAtUtc = '2026-08-24T00:00:30Z' }))) 'A pending public WMS outbound readback must not prove completion.'
    Assert-Contract (-not (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Completed'; completedAtUtc = '' }))) 'A completed public WMS outbound readback without completedAtUtc must fail closed.'

    $diagnosticArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/actual-evidence.json'
    $diagnosticSecret = 'wms-runtime-secret-123'
    $diagnosticWriteProof = Write-NervAcceptanceWmsDiagnosticArtifact `
        -Path $diagnosticArtifactPath `
        -Content "actual evidence Authorization: Bearer $diagnosticSecret" `
        -SensitiveValues @($diagnosticSecret)
    Assert-Contract ($diagnosticWriteProof.evidenceWritten -and $diagnosticWriteProof.secretsRedacted) 'The WMS diagnostic writer must earn capability flags from an actual persisted artifact and post-write scan.'
    $writtenDiagnosticContent = [IO.File]::ReadAllText($diagnosticArtifactPath)
    Assert-Contract (-not $writtenDiagnosticContent.Contains($diagnosticSecret, [StringComparison]::Ordinal)) 'The actual WMS diagnostic artifact must not retain the declared sensitive value.'
    $successfulDiagnosticEvidence = New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $diagnosticWriteProof -FailureCaptureSupported $true
    Assert-Contract ($successfulDiagnosticEvidence.failureCaptureSupported -and $successfulDiagnosticEvidence.secretsRedacted -and -not $successfulDiagnosticEvidence.failureDiagnosticsCaptured) 'WMS canonical diagnostic capability must be derived from the actual artifact write proof.'
    $canonicalResultWithFactoryDiagnostics = New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $wmsCanonicalResult.provenance -Track shadow -BusinessEvidence $wmsBusinessEvidence -TestCounters $wmsCounters -CleanupEvidence $wmsCleanup -DiagnosticEvidence $successfulDiagnosticEvidence -Volatile $wmsVolatile
    Assert-Contract $canonicalResultWithFactoryDiagnostics.diagnostics.failureCaptureSupported 'The successful diagnostic factory output must feed the canonical builder directly.'
    foreach ($invalidProofCase in @(
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = $false; secretsRedacted = $true }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = $true; secretsRedacted = $false }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = 'true'; secretsRedacted = $true }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ evidenceWritten = $true; secretsRedacted = $true }; Message = 'missing required field' }
    )) {
        $invalidProofRejected = $false
        try { New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $invalidProofCase.Proof -FailureCaptureSupported $true | Out-Null }
        catch { $invalidProofRejected = $_.Exception.Message.Contains([string]$invalidProofCase.Message, [StringComparison]::Ordinal) }
        Assert-Contract $invalidProofRejected 'WMS diagnostic capability must reject missing, false, or non-boolean artifact write proof fields.'
    }
    foreach ($invalidFailureCaptureSupport in @($false, 'true')) {
        $invalidFailureCaptureRejected = $false
        try { New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $diagnosticWriteProof -FailureCaptureSupported $invalidFailureCaptureSupport | Out-Null }
        catch { $invalidFailureCaptureRejected = $_.Exception.Message.Contains('failure-capture contract', [StringComparison]::Ordinal) }
        Assert-Contract $invalidFailureCaptureRejected 'WMS successful diagnostics must reject false or non-boolean failure-capture contract proof.'
    }

    $unannouncedSecretArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/unannounced-secret.json'
    [IO.File]::WriteAllText($unannouncedSecretArtifactPath, '{"authorization":"Bearer unannounced-secret-987"}', [Text.UTF8Encoding]::new($false))
    $unannouncedSecretRejected = $false
    try { Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $unannouncedSecretArtifactPath -SensitiveValues @() | Out-Null }
    catch { $unannouncedSecretRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    Assert-Contract ($unannouncedSecretRejected -and -not (Test-Path -LiteralPath $unannouncedSecretArtifactPath)) 'The independent diagnostic scanner must reject and remove an unpublished artifact containing an undeclared bearer secret.'
    $unannouncedPasswordArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/unannounced-password.json'
    [IO.File]::WriteAllText($unannouncedPasswordArtifactPath, 'Password=unannounced-password-321', [Text.UTF8Encoding]::new($false))
    $unannouncedPasswordRejected = $false
    try { Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $unannouncedPasswordArtifactPath -SensitiveValues @() | Out-Null }
    catch { $unannouncedPasswordRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    Assert-Contract ($unannouncedPasswordRejected -and -not (Test-Path -LiteralPath $unannouncedPasswordArtifactPath)) 'The independent diagnostic scanner must reject and remove an unpublished artifact containing an undeclared password field.'
    $atomicDiagnosticPath = Join-Path $fixtureRoot 'wms-diagnostics/atomic-evidence.json'
    [IO.File]::WriteAllText($atomicDiagnosticPath, '{"status":"previous-safe-evidence"}', [Text.UTF8Encoding]::new($false))
    $unannouncedUserInfoRejected = $false
    try { Write-NervAcceptanceWmsDiagnosticArtifact -Path $atomicDiagnosticPath -Content 'postgres://dbuser:unannounced-secret-654@localhost/db' -SensitiveValues @() | Out-Null }
    catch { $unannouncedUserInfoRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    $remainingDiagnosticTemps = @(Get-ChildItem -LiteralPath (Split-Path -Parent $atomicDiagnosticPath) -Filter '*.tmp' -File)
    Assert-Contract ($unannouncedUserInfoRejected -and [IO.File]::ReadAllText($atomicDiagnosticPath).Contains('previous-safe-evidence', [StringComparison]::Ordinal) -and $remainingDiagnosticTemps.Count -eq 0) 'The atomic diagnostic writer must reject an undeclared URI userinfo secret without replacing prior safe evidence or retaining its unpublished temporary artifact.'

    $verifierContract = Test-NervAcceptanceWmsVerifierContract -Path $wmsVerifierPath
    Assert-Contract ($verifierContract.failureCaptureSupported -and $verifierContract.pickingReadbackWired -and $verifierContract.completionReplayWired) 'The MAN-527 verifier must wire reachable failure capture and public WMS checkpoint predicates.'
    $wmsVerifierSource = [IO.File]::ReadAllText($wmsVerifierPath)
    $mutationTokens = $mutationParseErrors = $null
    $mutationAst = [Management.Automation.Language.Parser]::ParseInput($wmsVerifierSource, [ref]$mutationTokens, [ref]$mutationParseErrors)
    Assert-Contract ($mutationParseErrors.Count -eq 0) 'The verifier must parse before generating reachability mutations.'
    foreach ($contractMutation in @(
        @{ Name = 'failure-capture-if-false'; Command = 'Export-Man527FailureDiagnostics'; Variable = '$diagnosticEvidence'; Property = 'failureCaptureSupported' },
        @{ Name = 'picking-if-false'; Command = 'Test-NervAcceptanceWmsPickingReadbacks'; Variable = '$pickingLifecycleCompleted'; Property = 'pickingReadbackWired' },
        @{ Name = 'completion-if-false'; Command = 'Test-NervAcceptanceWmsCompletionReplay'; Variable = '$completionHttpReplayConverged'; Property = 'completionReplayWired' }
    )) {
        $mutationCommand = @($mutationAst.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.CommandAst] -and
                        [string]::Equals($node.GetCommandName(), [string]$contractMutation.Command, [StringComparison]::Ordinal)
                }, $true) | Where-Object {
                    $ancestor = $_.Parent
                    while ($null -ne $ancestor -and $ancestor -isnot [Management.Automation.Language.AssignmentStatementAst]) { $ancestor = $ancestor.Parent }
                    $null -ne $ancestor -and [string]::Equals($ancestor.Left.Extent.Text, [string]$contractMutation.Variable, [StringComparison]::Ordinal)
                })
        Assert-Contract ($mutationCommand.Count -eq 1) "Verifier contract mutation '$($contractMutation.Name)' requires one exact production assignment."
        $mutationAssignment = $mutationCommand[0].Parent
        while ($mutationAssignment -isnot [Management.Automation.Language.AssignmentStatementAst]) { $mutationAssignment = $mutationAssignment.Parent }
        $mutatedVerifierPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name).ps1"
        $mutatedVerifierSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, ' }').Insert($mutationAssignment.Extent.StartOffset, 'if ($false) { ')
        [IO.File]::WriteAllText($mutatedVerifierPath, $mutatedVerifierSource, [Text.UTF8Encoding]::new($false))
        $mutatedContract = Test-NervAcceptanceWmsVerifierContract -Path $mutatedVerifierPath
        Assert-Contract (-not [bool]$mutatedContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier contract mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

        $unusedFunctionMutationPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-unused-function.ps1"
        $unusedFunctionMutationSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, ' }').Insert($mutationAssignment.Extent.StartOffset, "function Invoke-UnusedContractMutation { ")
        [IO.File]::WriteAllText($unusedFunctionMutationPath, $unusedFunctionMutationSource, [Text.UTF8Encoding]::new($false))
        $unusedFunctionContract = Test-NervAcceptanceWmsVerifierContract -Path $unusedFunctionMutationPath
        Assert-Contract (-not [bool]$unusedFunctionContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier unused-function mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

        if (-not [string]::Equals([string]$contractMutation.Property, 'failureCaptureSupported', [StringComparison]::Ordinal)) {
            $overrideMutationPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-top-level-override.ps1"
            $overrideMutationSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($contractMutation.Variable) = `$true")
            [IO.File]::WriteAllText($overrideMutationPath, $overrideMutationSource, [Text.UTF8Encoding]::new($false))
            $overrideContract = Test-NervAcceptanceWmsVerifierContract -Path $overrideMutationPath
            Assert-Contract (-not [bool]$overrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier top-level override mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

            $alternateOverrideMutations = if ([string]::Equals([string]$contractMutation.Property, 'pickingReadbackWired', [StringComparison]::Ordinal)) {
                @(
                    @{ Name = 'script-scoped'; Statement = '$script:pickingLifecycleCompleted = $true' },
                    @{ Name = 'braced'; Statement = '${pickingLifecycleCompleted} = $true' }
                )
            }
            else {
                @(
                    @{ Name = 'set-variable'; Statement = 'Set-Variable -Name completionHttpReplayConverged -Value $true' },
                    @{ Name = 'typed'; Statement = '[bool]$completionHttpReplayConverged = $true' }
                )
            }
            foreach ($alternateOverrideMutation in $alternateOverrideMutations) {
                $alternateOverridePath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-$($alternateOverrideMutation.Name)-override.ps1"
                $alternateOverrideSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($alternateOverrideMutation.Statement)")
                [IO.File]::WriteAllText($alternateOverridePath, $alternateOverrideSource, [Text.UTF8Encoding]::new($false))
                $alternateOverrideContract = Test-NervAcceptanceWmsVerifierContract -Path $alternateOverridePath
                Assert-Contract (-not [bool]$alternateOverrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier alternate top-level override mutation '$($contractMutation.Name)-$($alternateOverrideMutation.Name)' must be killed by '$($contractMutation.Property)'."

                $functionLocalOverridePath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-$($alternateOverrideMutation.Name)-function-local.ps1"
                $functionLocalOverrideSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`nfunction Invoke-UnusedAlternateOverride { $($alternateOverrideMutation.Statement) }")
                [IO.File]::WriteAllText($functionLocalOverridePath, $functionLocalOverrideSource, [Text.UTF8Encoding]::new($false))
                $functionLocalOverrideContract = Test-NervAcceptanceWmsVerifierContract -Path $functionLocalOverridePath
                Assert-Contract ([bool]$functionLocalOverrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier function-local write '$($contractMutation.Name)-$($alternateOverrideMutation.Name)' must not be counted as a top-level override."
            }
        }
    }

    $failureAssignmentCommand = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals($node.GetCommandName(), 'Export-Man527FailureDiagnostics', [StringComparison]::Ordinal)
            }, $true))[0]
    $failureAssignment = $failureAssignmentCommand.Parent
    while ($failureAssignment -isnot [Management.Automation.Language.AssignmentStatementAst]) { $failureAssignment = $failureAssignment.Parent }
    $duplicateFailureCapturePath = Join-Path $fixtureRoot 'wms-diagnostics/failure-capture-duplicate.ps1'
    $duplicateFailureCaptureSource = $wmsVerifierSource.Insert($failureAssignment.Extent.EndOffset, "`n$($failureAssignment.Extent.Text)")
    [IO.File]::WriteAllText($duplicateFailureCapturePath, $duplicateFailureCaptureSource, [Text.UTF8Encoding]::new($false))
    $duplicateFailureCaptureContract = Test-NervAcceptanceWmsVerifierContract -Path $duplicateFailureCapturePath
    Assert-Contract (-not $duplicateFailureCaptureContract.failureCaptureSupported) 'A duplicate top-level failure exporter assignment must invalidate failureCaptureSupported.'
    $wmsAction = { param([object] $Contract) $wmsActionContracts.Add($Contract); return $wmsCanonicalResult }.GetNewClosure()
    $wmsArguments = Get-RuntimeArguments -ArtifactPath $wmsArtifactPath -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $wmsArtifactPath) -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $wmsSummaryPath -Action $wmsAction -ScenarioId 'wms-delivery-erp'
    $wmsRuntimeResult = Invoke-NervAcceptanceScenarioRuntime @wmsArguments
    Assert-Contract ($wmsActionContracts.Count -eq 1 -and [string]::Equals([string]$wmsActionContracts[0].scenario.id, 'wms-delivery-erp', [StringComparison]::Ordinal)) 'The WMS runtime adapter must dispatch exactly one validated wms-delivery-erp contract.'
    Assert-Contract ([string]::Equals([string]$wmsRuntimeResult.summary.scenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal) -and [string]::Equals([string]$wmsRuntimeResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The WMS runtime adapter must persist a passing scenario-specific summary.'
    $wmsBusinessMutation = Copy-JsonObject $wmsCanonicalResult
    $wmsBusinessMutation.businessFacts.repeatedEventConverged = $false
    Assert-ResultRejected -Name 'wms-business-mutation' -Results @($wmsBusinessMutation) -ExpectedMessage "business fact 'repeatedEventConverged' must be true" -ArtifactPath $wmsArtifactPath -ArtifactDigest (Get-FixtureFileDigest -Path $wmsArtifactPath) -ManifestDigest $manifestDigest -WorkflowPath $workflowPath -ScenarioId 'wms-delivery-erp'
    Assert-RunnerBoundaryRejected -Name 'unsupported-scenario-adapter' -ExpectedMessage 'Runtime scenarioId is not supported' -ArtifactPath $wmsArtifactPath -ArtifactDigest (Get-FixtureFileDigest -Path $wmsArtifactPath) -ManifestDigest $manifestDigest -WorkflowPath $workflowPath -Overrides @{ ScenarioId = 'unknown-scenario' }

    $defaultRunnerRoot = Join-Path $fixtureRoot 'default-path-runner'
    $defaultRunnerScriptsRoot = Join-Path $defaultRunnerRoot 'scripts'
    $defaultRunnerLibraryRoot = Join-Path $defaultRunnerScriptsRoot 'lib'
    $defaultRunnerWorkflowRoot = Join-Path $defaultRunnerRoot '.github/workflows'
    [IO.Directory]::CreateDirectory($defaultRunnerLibraryRoot) | Out-Null
    [IO.Directory]::CreateDirectory($defaultRunnerWorkflowRoot) | Out-Null
    Copy-Item -LiteralPath $runnerPath -Destination (Join-Path $defaultRunnerScriptsRoot 'run-acceptance-scenario-matrix.ps1')
    foreach ($libraryName in @('ScriptAutomation.ps1', 'AcceptanceScenarioMatrixRuntime.ps1', 'AcceptanceScenarioMatrix.ps1', 'CiWorkflowBudgets.ps1', 'OrdinalString.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/lib/$libraryName") -Destination (Join-Path $defaultRunnerLibraryRoot $libraryName)
    }
    $scriptAutomationFixturePath = Join-Path $defaultRunnerLibraryRoot 'ScriptAutomation.ps1'
    $invokePwshFixture = @'

function Invoke-PwshScript {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = (Get-Location).Path,
        [int] $TimeoutSeconds = 600,
        [string] $Name = 'pwsh-script'
    )

    $canonicalIndex = [Array]::IndexOf([string[]]$Arguments, '-CanonicalResultPath')
    if ($canonicalIndex -lt 0 -or $canonicalIndex + 1 -ge $Arguments.Count) {
        throw 'Fixture verifier invocation is missing CanonicalResultPath.'
    }
    [IO.File]::Copy($env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE, $Arguments[$canonicalIndex + 1], $true)
    $capture = [pscustomobject][ordered]@{
        scriptPath = $ScriptPath
        arguments = @($Arguments)
        workingDirectory = $WorkingDirectory
        timeoutSeconds = $TimeoutSeconds
        name = $Name
    }
    [IO.File]::AppendAllText(
        $env:NERV_IIP_RUNTIME_ACTION_CAPTURE,
        (($capture | ConvertTo-Json -Depth 10 -Compress) + "`n"),
        [Text.UTF8Encoding]::new($false))
}
'@
    [IO.File]::AppendAllText($scriptAutomationFixturePath, $invokePwshFixture, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $workflowPath -Destination (Join-Path $defaultRunnerWorkflowRoot 'ci.yml')
    $defaultRunnerPath = Join-Path $defaultRunnerScriptsRoot 'run-acceptance-scenario-matrix.ps1'
    $defaultArtifactPath = Join-Path $defaultRunnerRoot 'artifacts/acceptance-scenario-matrix/planning.json'
    Write-JsonFixture -Path $defaultArtifactPath -Value $artifact
    $defaultArtifactDigest = Get-FixtureFileDigest -Path $defaultArtifactPath
    $defaultPathAction = { return $firstEquivalenceInput }.GetNewClosure()

    $defaultArtifactSummaryPath = Join-Path $fixtureRoot 'default-artifact/runtime-summary.json'
    $defaultArtifactArguments = Get-RunnerArguments -ArtifactPath $defaultArtifactPath -ExpectedArtifactDigest $defaultArtifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $defaultArtifactSummaryPath -Action $defaultPathAction
    [void]$defaultArtifactArguments.Remove('ArtifactPath')
    $defaultArtifactResult = & $defaultRunnerPath @defaultArtifactArguments
    Assert-Contract ([string]::Equals([string]$defaultArtifactResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The production runner default ArtifactPath must cross the strict raw runtime boundary as a canonical absolute existing file.'

    $defaultWorkflowSummaryPath = Join-Path $fixtureRoot 'default-workflow/runtime-summary.json'
    $defaultWorkflowArguments = Get-RunnerArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath (Join-Path $defaultRunnerWorkflowRoot 'ci.yml') -SummaryPath $defaultWorkflowSummaryPath -Action $defaultPathAction
    [void]$defaultWorkflowArguments.Remove('WorkflowPath')
    $defaultWorkflowResult = & $defaultRunnerPath @defaultWorkflowArguments
    Assert-Contract ([string]::Equals([string]$defaultWorkflowResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The production runner default WorkflowPath must cross the strict raw runtime boundary as a canonical absolute existing file.'

    $defaultActionResultFixturePath = Join-Path $fixtureRoot 'default-action/canonical-result-fixture.json'
    Write-JsonFixture -Path $defaultActionResultFixturePath -Value $firstEquivalenceInput
    $defaultActionCapturePath = Join-Path $fixtureRoot 'default-action/invoke-pwsh-capture.jsonl'
    $defaultActionSummaryPath = Join-Path $fixtureRoot 'default-action/runtime-summary.json'
    $defaultActionCanonicalPath = [IO.Path]::GetFullPath((Join-Path $fixtureRoot 'default-action/canonical-result.json'))
    $defaultActionArguments = Get-RunnerArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $defaultActionSummaryPath -Action { throw 'The injected RuntimeAction seam must be absent from this fixture.' }
    [void]$defaultActionArguments.Remove('RuntimeAction')
    $defaultActionArguments.CanonicalResultPath = $defaultActionCanonicalPath
    $defaultActionArguments.TrackIdentifier = 'shadow'
    $previousRuntimeActionResultFixture = $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE
    $previousRuntimeActionCapture = $env:NERV_IIP_RUNTIME_ACTION_CAPTURE
    try {
        $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE = $defaultActionResultFixturePath
        $env:NERV_IIP_RUNTIME_ACTION_CAPTURE = $defaultActionCapturePath
        $defaultActionResult = & $defaultRunnerPath @defaultActionArguments
    }
    finally {
        $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE = $previousRuntimeActionResultFixture
        $env:NERV_IIP_RUNTIME_ACTION_CAPTURE = $previousRuntimeActionCapture
    }
    Assert-Contract ([string]::Equals([string]$defaultActionResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The production runner default action must survive deferred invocation by the dot-sourced runtime library.'
    $defaultActionCaptureLines = @(Get-Content -LiteralPath $defaultActionCapturePath)
    Assert-Contract ($defaultActionCaptureLines.Count -eq 1) 'The production runner default action must invoke the governed verifier exactly once.'
    $defaultActionCapture = $defaultActionCaptureLines[0] | ConvertFrom-Json -Depth 10
    Assert-Contract ([string]::Equals([IO.Path]::GetFullPath([string]$defaultActionCapture.scriptPath), [IO.Path]::GetFullPath((Join-Path $defaultRunnerScriptsRoot 'verify-erp-sales-order-demand-planning.ps1')), [StringComparison]::Ordinal)) 'The production runner default action must invoke only the governed ERP sales-order verifier.'
    $defaultActionCapturedArguments = @($defaultActionCapture.arguments)
    $defaultActionCanonicalIndex = [Array]::IndexOf([object[]]$defaultActionCapturedArguments, '-CanonicalResultPath')
    $defaultActionTrackIndex = [Array]::IndexOf([object[]]$defaultActionCapturedArguments, '-TrackIdentifier')
    Assert-Contract ($defaultActionCanonicalIndex -ge 0 -and [string]::Equals([string]$defaultActionCapturedArguments[$defaultActionCanonicalIndex + 1], $defaultActionCanonicalPath, [StringComparison]::Ordinal)) 'The production runner default action must pass the canonical result path to the governed verifier.'
    Assert-Contract ($defaultActionTrackIndex -ge 0 -and [string]::Equals([string]$defaultActionCapturedArguments[$defaultActionTrackIndex + 1], 'shadow', [StringComparison]::Ordinal)) 'The production runner default action must pass the shadow track identifier to the governed verifier.'
    Assert-Contract ([string]::Equals([string]$defaultActionCapture.name, 'acceptance-scenario-matrix-sales-order-demand', [StringComparison]::Ordinal)) 'The production runner default action must retain the governed verifier invocation identity.'

    $defaultWmsActionResultFixturePath = Join-Path $fixtureRoot 'default-wms-action/canonical-result-fixture.json'
    Write-JsonFixture -Path $defaultWmsActionResultFixturePath -Value $wmsCanonicalResult
    $defaultWmsActionCapturePath = Join-Path $fixtureRoot 'default-wms-action/invoke-pwsh-capture.jsonl'
    $defaultWmsSummaryPath = Join-Path $fixtureRoot 'default-wms-action/runtime-summary.json'
    $defaultWmsCanonicalPath = [IO.Path]::GetFullPath((Join-Path $fixtureRoot 'default-wms-action/canonical-result.json'))
    $defaultWmsArguments = Get-RunnerArguments -ArtifactPath $wmsArtifactPath -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $wmsArtifactPath) -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $defaultWmsSummaryPath -Action { throw 'The injected RuntimeAction seam must be absent from this fixture.' } -ScenarioId 'wms-delivery-erp'
    [void]$defaultWmsArguments.Remove('RuntimeAction')
    $defaultWmsArguments.CanonicalResultPath = $defaultWmsCanonicalPath
    $defaultWmsArguments.TrackIdentifier = 'shadow'
    $previousRuntimeActionResultFixture = $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE
    $previousRuntimeActionCapture = $env:NERV_IIP_RUNTIME_ACTION_CAPTURE
    try {
        $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE = $defaultWmsActionResultFixturePath
        $env:NERV_IIP_RUNTIME_ACTION_CAPTURE = $defaultWmsActionCapturePath
        $defaultWmsResult = & $defaultRunnerPath @defaultWmsArguments
    }
    finally {
        $env:NERV_IIP_RUNTIME_ACTION_RESULT_FIXTURE = $previousRuntimeActionResultFixture
        $env:NERV_IIP_RUNTIME_ACTION_CAPTURE = $previousRuntimeActionCapture
    }
    Assert-Contract ([string]::Equals([string]$defaultWmsResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The production runner default WMS action must return a validated canonical result.'
    $defaultWmsCapture = @(Get-Content -LiteralPath $defaultWmsActionCapturePath)[0] | ConvertFrom-Json -Depth 10
    Assert-Contract ([string]::Equals([IO.Path]::GetFullPath([string]$defaultWmsCapture.scriptPath), [IO.Path]::GetFullPath((Join-Path $defaultRunnerScriptsRoot 'verify-erp-wms-delivery-completion.ps1')), [StringComparison]::Ordinal)) 'The explicit WMS adapter must invoke only the governed MAN-527 verifier.'
    Assert-Contract ([string]::Equals([string]$defaultWmsCapture.name, 'acceptance-scenario-matrix-wms-delivery-erp', [StringComparison]::Ordinal)) 'The explicit WMS adapter must retain its governed invocation identity.'

    $productionWorkflowSummaryPath = Join-Path $fixtureRoot 'production-workflow/runtime-summary.json'
    $productionWorkflowActionContracts = [Collections.Generic.List[object]]::new()
    $productionWorkflowAction = { param([object] $Contract) $productionWorkflowActionContracts.Add($Contract); return $firstEquivalenceInput }.GetNewClosure()
    $productionWorkflowArguments = Get-RuntimeArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath (Join-Path $repoRoot '.github/workflows/ci.yml') -SummaryPath $productionWorkflowSummaryPath -Action $productionWorkflowAction
    $productionWorkflowResult = Invoke-NervAcceptanceScenarioRuntime @productionWorkflowArguments
    Assert-Contract ($productionWorkflowActionContracts.Count -eq 1 -and [string]::Equals([string]$productionWorkflowResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'The runtime preflight must accept the one top-level governed runner invocation in the production PowerShell workflow block.'

    $actionContracts = [Collections.Generic.List[object]]::new()
    $assertContract = ${function:Assert-Contract}
    $runtimeAction = {
        param([object] $Contract)
        $actionContracts.Add($Contract)
        & $assertContract (Test-Path -LiteralPath $summaryPath -PathType Leaf) 'Runtime summary must exist before the injected action runs.'
        $summaryBeforeAction = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.status, 'running', [StringComparison]::Ordinal)) 'Runtime summary must be running before the injected action runs.'
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.transitions[-1].state, 'action-started', [StringComparison]::Ordinal)) 'The action-started transition must be atomically persisted before invocation.'
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.repository, 'Mang-X/Nerv-IIP', [StringComparison]::Ordinal)) 'The validated repository must be published before the action runs.'
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.testedSha, '0123456789abcdef0123456789abcdef01234567', [StringComparison]::Ordinal)) 'The validated tested SHA must be published before the action runs.'
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.runId, '123456789', [StringComparison]::Ordinal)) 'The validated run id must be published before the action runs.'
        & $assertContract ($summaryBeforeAction.runAttempt -eq 2) 'The validated run attempt must be published before the action runs.'
        & $assertContract ([string]::Equals([string]$summaryBeforeAction.event, 'workflow_dispatch', [StringComparison]::Ordinal)) 'The validated event must be published before the action runs.'
        & $assertContract ([string]::Equals([string]$Contract.scenario.id, 'sales-order-demand', [StringComparison]::Ordinal)) 'The injected action must receive the exact validated scenario contract.'
        return $firstEquivalenceInput
    }.GetNewClosure()
    $runnerArguments = Get-RunnerArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $summaryPath -Action $runtimeAction
    $runtimeResult = & $runnerPath @runnerArguments
    Assert-Contract ($actionContracts.Count -eq 1) 'A valid runtime contract must invoke the injected action exactly once.'
    Assert-Contract ([string]::Equals([string]$runtimeResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'A valid single result must pass the runtime summary.'
    Assert-Contract ([string]::Equals((@($runtimeResult.summary.transitions.state) -join '|'), 'preflight-started|preflight-passed|action-started|action-completed|result-validation-started|result-validation-passed', [StringComparison]::Ordinal)) 'Runtime state transitions must be stable and complete.'
    $persistedSummary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals(($persistedSummary.transitions.state -join '|'), 'preflight-started|preflight-passed|action-started|action-completed|result-validation-started|result-validation-passed', [StringComparison]::Ordinal)) 'Every successful runtime transition must be persisted.'
    Assert-Contract ([string]::Equals([string]$persistedSummary.result.test.identity, [string]$firstEquivalenceInput.test.identity, [StringComparison]::Ordinal)) 'Final summary must persist the validated frozen test identity.'
    Assert-Contract ($persistedSummary.result.test.expected -eq 1 -and $persistedSummary.result.test.discovered -eq 1 -and $persistedSummary.result.test.passed -eq 1 -and $persistedSummary.result.test.failed -eq 0 -and $persistedSummary.result.test.skipped -eq 0) 'Final summary must persist the exact validated result counts.'
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $summaryPath) -Filter '*.tmp' -File).Count -eq 0) 'Atomic summary persistence must not leave temporary files.'
    $canonicalResultJson = $persistedSummary.result | ConvertTo-Json -Depth 50 -Compress
    foreach ($unsupportedClaim in @('http200BusinessErrorRejected', 'firstConsumeFailureRecovered', 'capturedBeforeCleanup')) {
        Assert-Contract (-not $canonicalResultJson.Contains($unsupportedClaim, [StringComparison]::Ordinal)) "Canonical result must not claim unsupported fact '$unsupportedClaim'."
    }

    $mixedAttemptArtifact = Copy-JsonObject $artifact
    $mixedAttemptArtifact.runAttempt = 1
    $mixedAttemptArtifactPath = Join-Path $fixtureRoot 'mixed-attempt/planning-artifact.json'
    Write-JsonFixture -Path $mixedAttemptArtifactPath -Value $mixedAttemptArtifact
    $mixedAttemptSummaryPath = Join-Path $fixtureRoot 'mixed-attempt/runtime-summary.json'
    $mixedAttemptCalls = [Collections.Generic.List[object]]::new()
    $mixedAttemptAction = { param([object] $Contract) $mixedAttemptCalls.Add($Contract); return $firstEquivalenceInput }.GetNewClosure()
    $mixedAttemptArguments = Get-RuntimeArguments `
        -ArtifactPath $mixedAttemptArtifactPath `
        -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $mixedAttemptArtifactPath) `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest $manifestDigest `
        -WorkflowPath $workflowPath `
        -SummaryPath $mixedAttemptSummaryPath `
        -Action $mixedAttemptAction `
        -PlanningRunAttempt '1' `
        -RunAttempt '2'
    $mixedAttemptResult = Invoke-NervAcceptanceScenarioRuntime @mixedAttemptArguments
    Assert-Contract ($mixedAttemptCalls.Count -eq 1 -and [string]::Equals([string]$mixedAttemptResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'A reused attempt-1 planning artifact must drive an attempt-2 runtime action exactly once.'
    Assert-Contract ($mixedAttemptResult.contract.artifact.runAttempt -eq 1 -and $mixedAttemptResult.contract.provenance.runAttempt -eq 2 -and $mixedAttemptResult.summary.runAttempt -eq 2) 'Planning validation must retain producer attempt 1 while runtime result provenance retains physical attempt 2.'

    $wrongPlanningAttemptSummaryPath = Join-Path $fixtureRoot 'mixed-attempt/wrong-planning-attempt-summary.json'
    $wrongPlanningAttemptArguments = Get-RuntimeArguments `
        -ArtifactPath $mixedAttemptArtifactPath `
        -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $mixedAttemptArtifactPath) `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest $manifestDigest `
        -WorkflowPath $workflowPath `
        -SummaryPath $wrongPlanningAttemptSummaryPath `
        -Action { throw 'A mismatched planning attempt must fail before the action.' } `
        -PlanningRunAttempt '2' `
        -RunAttempt '2'
    $wrongPlanningAttemptMessage = '<no exception>'
    try { Invoke-NervAcceptanceScenarioRuntime @wrongPlanningAttemptArguments | Out-Null }
    catch { $wrongPlanningAttemptMessage = $_.Exception.Message }
    Assert-Contract ($wrongPlanningAttemptMessage.Contains('Planning artifact runAttempt does not match expected provenance.', [StringComparison]::Ordinal)) 'Runtime must reject a planning artifact whose producer attempt does not match PlanningRunAttempt.'

    Assert-RunnerBoundaryRejected -Name 'planning-run-attempt-leading-zero' -ExpectedMessage 'planning run attempt must be a canonical positive integer' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath -Overrides @{ PlanningRunAttempt = '01' }

    $activeCoreIds = @($manifest.scenarios | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal) } | ForEach-Object { [string]$_.id })
    $planningOrderedActiveCoreIds = [string[]]@($activeCoreIds)
    [Array]::Sort($planningOrderedActiveCoreIds, [StringComparer]::Ordinal)
    $mainArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds $planningOrderedActiveCoreIds -Event 'push' -SelectionMode 'main-active-core' -SelectionReasons @('main')
    $mainArtifactPath = Join-Path $fixtureRoot 'main-five-planning-artifact.json'
    Write-JsonFixture -Path $mainArtifactPath -Value $mainArtifact
    $mainSummaryPath = Join-Path $fixtureRoot 'main-five/runtime-summary.json'
    $mainActionCalls = [Collections.Generic.List[object]]::new()
    $mainAction = { param([object] $Contract) $mainActionCalls.Add($Contract); return $firstEquivalenceInput }.GetNewClosure()
    $mainArguments = Get-RunnerArguments -ArtifactPath $mainArtifactPath -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $mainArtifactPath) -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $mainSummaryPath -Action $mainAction -Event 'push'
    $mainResult = & $runnerPath @mainArguments
    Assert-Contract ($mainActionCalls.Count -eq 1 -and $mainResult.contract.selected) 'A valid main five-scenario planning artifact must extract and execute sales exactly once.'

    $reversedActiveCoreIds = [string[]]@($activeCoreIds)
    [Array]::Reverse($reversedActiveCoreIds)
    $blockedScenario = @($manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.status, 'blocked', [StringComparison]::Ordinal)
    })[0]
    foreach ($selectionCase in @(
        @{ Name = 'push'; Event = 'push'; Mode = 'main-active-core'; Reasons = @('main'); Failure = 'Runtime push planning artifact must preserve the main active/core selection provenance.' },
        @{ Name = 'schedule'; Event = 'schedule'; Mode = 'nightly-active'; Reasons = @('nightly'); Failure = 'Runtime scheduled planning artifact must preserve the nightly active selection provenance.' },
        @{ Name = 'workflow-dispatch-all-active'; Event = 'workflow_dispatch'; Mode = 'workflow-dispatch-all-active'; Reasons = @('dispatch:lane'); Failure = 'Runtime workflow_dispatch all-active selection provenance is inconsistent.' },
        @{ Name = 'conservative-pr'; Event = 'pull_request'; Mode = 'conservative-active-core'; Reasons = @('impact-rules-failed'); Failure = 'Runtime conservative PR selection provenance is inconsistent.' }
    )) {
        $originalOrderArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds $activeCoreIds -Event $selectionCase.Event -SelectionMode $selectionCase.Mode -SelectionReasons $selectionCase.Reasons
        Assert-RuntimeSelectionAccepted -Name "$($selectionCase.Name)-manifest-order" -Artifact $originalOrderArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedScenarioIds $activeCoreIds

        $reversedOrderArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds $reversedActiveCoreIds -Event $selectionCase.Event -SelectionMode $selectionCase.Mode -SelectionReasons $selectionCase.Reasons
        Assert-RuntimeSelectionAccepted -Name "$($selectionCase.Name)-reversed-order" -Artifact $reversedOrderArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedScenarioIds $reversedActiveCoreIds

        $subsetArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds ([string[]]@($activeCoreIds | Select-Object -First ($activeCoreIds.Count - 1))) -Event $selectionCase.Event -SelectionMode $selectionCase.Mode -SelectionReasons $selectionCase.Reasons
        Assert-RuntimeSelectionRejected -Name "$($selectionCase.Name)-true-subset" -Artifact $subsetArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedMessage $selectionCase.Failure

        $extraArtifact = Copy-JsonObject $originalOrderArtifact
        $extraArtifact.scenarios = @($extraArtifact.scenarios) + [pscustomobject][ordered]@{ id = 'unknown-active-core'; status = 'active'; tier = 'core' }
        Assert-RuntimeSelectionRejected -Name "$($selectionCase.Name)-extra-member" -Artifact $extraArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedMessage "must identify one selected active/core manifest scenario"

        $duplicateArtifact = Copy-JsonObject $originalOrderArtifact
        $duplicateArtifact.scenarios = @($duplicateArtifact.scenarios) + (Copy-JsonObject $duplicateArtifact.scenarios[0])
        Assert-RuntimeSelectionRejected -Name "$($selectionCase.Name)-duplicate-member" -Artifact $duplicateArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedMessage 'duplicate selected scenario'

        $illegalArtifact = Copy-JsonObject $originalOrderArtifact
        $illegalArtifact.scenarios = @($illegalArtifact.scenarios) + [pscustomobject][ordered]@{ id = [string]$blockedScenario.id; status = 'blocked'; tier = 'extended' }
        Assert-RuntimeSelectionRejected -Name "$($selectionCase.Name)-illegal-member" -Artifact $illegalArtifact -Manifest $manifest -Event $selectionCase.Event -ExpectedMessage "must identify one selected active/core manifest scenario"
    }

    $multiPrArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds @('sales-order-demand', 'wms-delivery-erp') -Event 'pull_request' -SelectionMode 'pull-request-impact' -SelectionReasons @('impact:backend/services/Business/Erp/src/example.cs')
    $multiPrArtifactPath = Join-Path $fixtureRoot 'multi-pr-planning-artifact.json'
    Write-JsonFixture -Path $multiPrArtifactPath -Value $multiPrArtifact
    $multiPrSummaryPath = Join-Path $fixtureRoot 'multi-pr/runtime-summary.json'
    $multiPrActionCalls = [Collections.Generic.List[object]]::new()
    $multiPrAction = { param([object] $Contract) $multiPrActionCalls.Add($Contract); return $firstEquivalenceInput }.GetNewClosure()
    $multiPrArguments = Get-RunnerArguments -ArtifactPath $multiPrArtifactPath -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $multiPrArtifactPath) -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $multiPrSummaryPath -Action $multiPrAction -Event 'pull_request'
    $multiPrResult = & $runnerPath @multiPrArguments
    Assert-Contract ($multiPrActionCalls.Count -eq 1 -and $multiPrResult.contract.selected) 'A valid multi-select PR planning artifact must extract and execute sales exactly once.'

    $noSalesArtifact = New-PlanningArtifact -Manifest $manifest -ManifestDigest $manifestDigest -ScenarioIds @('wms-delivery-erp') -Event 'pull_request' -SelectionMode 'pull-request-impact' -SelectionReasons @('impact:backend/services/Business/Wms/src/example.cs')
    $noSalesArtifactPath = Join-Path $fixtureRoot 'no-sales-planning-artifact.json'
    Write-JsonFixture -Path $noSalesArtifactPath -Value $noSalesArtifact
    $noSalesSummaryPath = Join-Path $fixtureRoot 'no-sales/runtime-summary.json'
    $script:noSalesActionCalls = 0
    $noSalesAction = { $script:noSalesActionCalls++; throw 'sales action must not run' }
    $noSalesArguments = Get-RunnerArguments -ArtifactPath $noSalesArtifactPath -ExpectedArtifactDigest (Get-FixtureFileDigest -Path $noSalesArtifactPath) -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $noSalesSummaryPath -Action $noSalesAction -Event 'pull_request'
    $noSalesResult = & $runnerPath @noSalesArguments
    Assert-Contract ($script:noSalesActionCalls -eq 0) 'A valid planning artifact without sales must invoke zero runtime actions.'
    Assert-Contract (-not $noSalesResult.contract.selected -and -not $noSalesResult.summary.selected -and [string]::Equals([string]$noSalesResult.summary.status, 'passed', [StringComparison]::Ordinal)) 'A planning artifact without sales must return selected=false as a passing no-execution outcome.'
    Assert-Contract ([string]::Equals((@($noSalesResult.summary.transitions.state) -join '|'), 'preflight-started|preflight-passed|not-selected', [StringComparison]::Ordinal)) 'A no-sales planning artifact must persist the stable no-execution transition sequence.'

    Assert-ResultRejected -Name 'missing' -Results @() -ExpectedMessage 'exactly one result' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-ResultRejected -Name 'extra' -Results @($firstEquivalenceInput, $secondEquivalenceInput) -ExpectedMessage 'exactly one result' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath

    foreach ($mutation in @(
        @{ Name = 'unknown-field'; Apply = { param($value) $value | Add-Member -NotePropertyName unknown -NotePropertyValue $true }; Message = 'unknown field' },
        @{ Name = 'wrong-case-field'; Apply = { param($value) $value.test.PSObject.Properties.Remove('expected'); $value.test | Add-Member -NotePropertyName Expected -NotePropertyValue 1 }; Message = 'unknown field' },
        @{ Name = 'provenance-repository'; Apply = { param($value) $value.provenance.repository = 'Mang-X/Drifted' }; Message = 'provenance repository must match' },
        @{ Name = 'provenance-run-id'; Apply = { param($value) $value.provenance.runId = '987654321' }; Message = 'provenance runId must match' },
        @{ Name = 'provenance-run-attempt'; Apply = { param($value) $value.provenance.runAttempt = 3 }; Message = 'provenance runAttempt must match' },
        @{ Name = 'provenance-tested-sha'; Apply = { param($value) $value.provenance.testedSha = '1123456789abcdef0123456789abcdef01234567' }; Message = 'provenance testedSha must match' },
        @{ Name = 'provenance-manifest-digest'; Apply = { param($value) $value.provenance.manifestDigest = ('f' * 64) }; Message = 'provenance manifestDigest must match' },
        @{ Name = 'provenance-scenario'; Apply = { param($value) $value.provenance.scenarioId = 'wms-delivery-erp' }; Message = 'provenance scenarioId must match' },
        @{ Name = 'track-empty'; Apply = { param($value) $value.track = '' }; Message = 'track must be a canonical identifier' },
        @{ Name = 'expected-type'; Apply = { param($value) $value.test.expected = '1' }; Message = 'must be a non-negative JSON integer' },
        @{ Name = 'expected-count'; Apply = { param($value) $value.test.expected = 0 }; Message = 'expected must be 1' },
        @{ Name = 'discovered-count'; Apply = { param($value) $value.test.discovered = 0 }; Message = 'discovered must be 1' },
        @{ Name = 'passed-count'; Apply = { param($value) $value.test.passed = 0 }; Message = 'passed must be 1' },
        @{ Name = 'failed-count'; Apply = { param($value) $value.test.failed = 1 }; Message = 'failed must be 0' },
        @{ Name = 'skipped-count'; Apply = { param($value) $value.test.skipped = 1 }; Message = 'skipped must be 0' },
        @{ Name = 'conclusion-failed'; Apply = { param($value) $value.conclusion = 'failed' }; Message = "conclusion must be 'passed'" },
        @{ Name = 'committed-source'; Apply = { param($value) $value.businessFacts.sourceStateCommittedBeforeMutation = $false }; Message = "business fact 'sourceStateCommittedBeforeMutation' must be true" },
        @{ Name = 'changed-v2'; Apply = { param($value) $value.businessFacts.changeV2Converged = $false }; Message = "business fact 'changeV2Converged' must be true" },
        @{ Name = 'changed-v3'; Apply = { param($value) $value.businessFacts.changeV3Converged = $false }; Message = "business fact 'changeV3Converged' must be true" },
        @{ Name = 'duplicate'; Apply = { param($value) $value.businessFacts.duplicateConverged = $false }; Message = "business fact 'duplicateConverged' must be true" },
        @{ Name = 'out-of-order'; Apply = { param($value) $value.businessFacts.outOfOrderConverged = $false }; Message = "business fact 'outOfOrderConverged' must be true" },
        @{ Name = 'cancellation'; Apply = { param($value) $value.businessFacts.cancellationConverged = $false }; Message = "business fact 'cancellationConverged' must be true" },
        @{ Name = 'business-fact-type'; Apply = { param($value) $value.businessFacts.duplicateConverged = 'true' }; Message = 'must be a boolean' },
        @{ Name = 'diagnostic-schema-missing'; Apply = { param($value) $value.diagnostics.schemas = @('demand_planning', 'erp') }; Message = 'schemas must exactly equal' },
        @{ Name = 'diagnostic-schema-extra'; Apply = { param($value) $value.diagnostics.schemas = @('demand_planning', 'erp', 'master_data', 'public') }; Message = 'schemas must exactly equal' },
        @{ Name = 'diagnostic-capability'; Apply = { param($value) $value.diagnostics.failureCaptureSupported = $false }; Message = "diagnostic 'failureCaptureSupported' must be true" },
        @{ Name = 'diagnostic-success-capture'; Apply = { param($value) $value.diagnostics.failureDiagnosticsCaptured = $true }; Message = "diagnostic 'failureDiagnosticsCaptured' must be false on success" },
        @{ Name = 'diagnostic-redaction'; Apply = { param($value) $value.diagnostics.secretsRedacted = $false }; Message = "diagnostic 'secretsRedacted' must be true" },
        @{ Name = 'process-cleanup'; Apply = { param($value) $value.cleanup.managedProcessesRemaining = 1 }; Message = 'managedProcessesRemaining must be 0' },
        @{ Name = 'database-cleanup'; Apply = { param($value) $value.cleanup.disposableDatabasesRemaining = 1 }; Message = 'disposableDatabasesRemaining must be 0' },
        @{ Name = 'owned-resource-cleanup'; Apply = { param($value) $value.cleanup.ownedResourcesRemaining = 1 }; Message = 'ownedResourcesRemaining must be 0' },
        @{ Name = 'cleanup-error'; Apply = { param($value) $value.cleanup.errorCodes = @('owned-resource-cleanup-failed') }; Message = 'errorCodes must be empty' },
        @{ Name = 'volatile-process-id-type'; Apply = { param($value) $value.volatile.processIds = @('101') }; Message = 'processIds must contain only non-negative JSON integers' },
        @{ Name = 'volatile-process-id-duplicate'; Apply = { param($value) $value.volatile.processIds = @(101, 101) }; Message = 'processIds must contain unique integer values' },
        @{ Name = 'volatile-started-at-type'; Apply = { param($value) $value.volatile.startedAtUtc = 123 }; Message = 'startedAtUtc must be a trimmed non-empty string' }
    )) {
        $mutatedResult = Copy-JsonObject $firstEquivalenceInput
        & $mutation.Apply $mutatedResult
        Assert-ResultRejected -Name $mutation.Name -Results @($mutatedResult) -ExpectedMessage $mutation.Message -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    }

    $compoundCleanupFailure = Copy-JsonObject $firstEquivalenceInput
    $compoundCleanupFailure.conclusion = 'failed'
    $compoundCleanupFailure.test.passed = 0
    $compoundCleanupFailure.test.failed = 1
    $compoundCleanupFailure.businessFacts.cancellationConverged = $false
    $compoundCleanupFailure.cleanup.ownedResourcesRemaining = 1
    $compoundCleanupFailure.cleanup.errorCodes = @('owned-resource-cleanup-failed')
    $compoundSummaryPath = Join-Path $fixtureRoot 'result-compound-cleanup-summary.json'
    $compoundAction = { return $compoundCleanupFailure }.GetNewClosure()
    $compoundArguments = Get-RunnerArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $compoundSummaryPath -Action $compoundAction
    $compoundException = $null
    try { & $runnerPath @compoundArguments | Out-Null }
    catch { $compoundException = $_.Exception }
    Assert-Contract ($null -ne $compoundException) 'A compound scenario and cleanup failure must throw.'
    Assert-Contract ([string]::Equals([string]$compoundException.Data['NervAcceptanceFailureClassification'], 'cleanup-failed', [StringComparison]::Ordinal)) 'A compound failure exception must classify cleanup as the priority failure.'
    Assert-Contract ($compoundException.Message.Contains('cleanup', [StringComparison]::Ordinal)) 'A compound failure must report cleanup failure before scenario outcome.'
    Assert-Contract (-not $compoundException.Message.Contains("conclusion must be 'passed'", [StringComparison]::Ordinal)) 'A compound cleanup failure must not be reported only as scenario conclusion failure.'
    $compoundSummary = Get-Content -LiteralPath $compoundSummaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$compoundSummary.failureClassification, 'cleanup-failed', [StringComparison]::Ordinal)) 'A compound failure summary must persist cleanup-failed classification.'
    Assert-Contract ($compoundSummary.result.cleanup.ownedResourcesRemaining -eq 1) 'A compound failure summary must retain the owned-resource cleanup readback.'
    Assert-Contract ([string]::Equals(($compoundSummary.result.cleanup.errorCodes -join '|'), 'owned-resource-cleanup-failed', [StringComparison]::Ordinal)) 'A compound failure summary must retain canonical cleanup error codes.'
    $compoundSummaryJson = $compoundSummary | ConvertTo-Json -Depth 50 -Compress
    Assert-Contract (-not $compoundSummaryJson.Contains('nerv_shadow_run_1', [StringComparison]::Ordinal)) 'A failed final summary must exclude the volatile database name.'
    Assert-Contract (-not $compoundSummaryJson.Contains('attempt-1-aabbcc', [StringComparison]::Ordinal)) 'A failed final summary must exclude the volatile CAP suffix.'
    Assert-Contract ([string]::Equals([string]$compoundSummary.transitions[-1].state, 'result-validation-failed', [StringComparison]::Ordinal)) 'A compound failure must persist result-validation-failed as final transition.'

    Assert-RunnerBoundaryRejected -Name 'artifact-digest-empty' -Overrides @{ ExpectedArtifactDigest = '' } -ExpectedMessage 'runtime artifact digest must be a lowercase SHA-256 digest' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-RunnerBoundaryRejected -Name 'event-unknown' -Overrides @{ Event = 'WORKFLOW_DISPATCH_INVALID' } -ExpectedMessage 'runtime event must be one of' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-RunnerBoundaryRejected -Name 'attempt-non-integer' -Overrides @{ RunAttempt = 'abc-secret-attempt' } -ExpectedMessage 'runtime run attempt must be a canonical positive integer' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-RunnerBoundaryRejected -Name 'attempt-overflow' -Overrides @{ RunAttempt = '2147483648' } -ExpectedMessage 'runtime run attempt must fit Int32' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-RunnerBoundaryRejected -Name 'attempt-zero' -Overrides @{ RunAttempt = '0' } -ExpectedMessage 'runtime run attempt must be a canonical positive integer' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    Assert-RunnerBoundaryRejected -Name 'attempt-leading-zero' -Overrides @{ RunAttempt = '02' } -ExpectedMessage 'runtime run attempt must be a canonical positive integer' -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath

    foreach ($boundaryMutation in @(
        @{ Name = 'artifact-digest-missing'; Key = 'ExpectedArtifactDigest'; Value = $null; Message = 'runtime artifact digest must be a lowercase SHA-256 digest' },
        @{ Name = 'manifest-digest-empty'; Key = 'ExpectedManifestDigest'; Value = ''; Message = 'runtime manifest digest must be a lowercase SHA-256 digest' },
        @{ Name = 'manifest-digest-missing'; Key = 'ExpectedManifestDigest'; Value = $null; Message = 'runtime manifest digest must be a lowercase SHA-256 digest' },
        @{ Name = 'repository-empty'; Key = 'Repository'; Value = ''; Message = 'runtime repository must be a canonical owner/name identifier' },
        @{ Name = 'repository-missing'; Key = 'Repository'; Value = $null; Message = 'runtime repository must be a canonical owner/name identifier' },
        @{ Name = 'tested-sha-empty'; Key = 'TestedSha'; Value = ''; Message = 'runtime tested SHA must be a lowercase 40-character Git SHA' },
        @{ Name = 'tested-sha-missing'; Key = 'TestedSha'; Value = $null; Message = 'runtime tested SHA must be a lowercase 40-character Git SHA' },
        @{ Name = 'run-id-empty'; Key = 'RunId'; Value = ''; Message = 'runtime run id must be a canonical positive decimal identifier' },
        @{ Name = 'run-id-missing'; Key = 'RunId'; Value = $null; Message = 'runtime run id must be a canonical positive decimal identifier' },
        @{ Name = 'artifact-path-empty'; Key = 'ArtifactPath'; Value = ''; Message = 'runtime planning artifact path must identify one existing canonical file' },
        @{ Name = 'artifact-path-missing'; Key = 'ArtifactPath'; Value = $null; Message = 'runtime planning artifact path must identify one existing canonical file' },
        @{ Name = 'manifest-file-path-empty'; Key = 'ManifestFilePath'; Value = ''; Message = 'runtime acceptance manifest path must identify one existing canonical file' },
        @{ Name = 'manifest-file-path-missing'; Key = 'ManifestFilePath'; Value = $null; Message = 'runtime acceptance manifest path must identify one existing canonical file' },
        @{ Name = 'v1-manifest-path-empty'; Key = 'V1ManifestPath'; Value = ''; Message = 'runtime FullChain v1 manifest path must identify one existing canonical file' },
        @{ Name = 'v1-manifest-path-missing'; Key = 'V1ManifestPath'; Value = $null; Message = 'runtime FullChain v1 manifest path must identify one existing canonical file' },
        @{ Name = 'workflow-path-empty'; Key = 'WorkflowPath'; Value = ''; Message = 'runtime workflow path must identify one existing canonical file' },
        @{ Name = 'workflow-path-missing'; Key = 'WorkflowPath'; Value = $null; Message = 'runtime workflow path must identify one existing canonical file' }
    )) {
        Assert-RunnerBoundaryRejected -Name $boundaryMutation.Name -Overrides @{ $boundaryMutation.Key = $boundaryMutation.Value } -ExpectedMessage $boundaryMutation.Message -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    }

    foreach ($secretMutation in @(
        @{ Name = 'repository-secret'; Key = 'Repository'; Marker = 'SECRET_RAW_REPOSITORY_MARKER'; Message = 'runtime repository must be a canonical owner/name identifier' },
        @{ Name = 'tested-sha-secret'; Key = 'TestedSha'; Marker = 'SECRET_RAW_SHA_MARKER'; Message = 'runtime tested SHA must be a lowercase 40-character Git SHA' },
        @{ Name = 'run-id-secret'; Key = 'RunId'; Marker = 'SECRET_RAW_RUN_MARKER'; Message = 'runtime run id must be a canonical positive decimal identifier' },
        @{ Name = 'artifact-digest-secret'; Key = 'ExpectedArtifactDigest'; Marker = 'SECRET_RAW_ARTIFACT_DIGEST_MARKER'; Message = 'runtime artifact digest must be a lowercase SHA-256 digest' },
        @{ Name = 'manifest-digest-secret'; Key = 'ExpectedManifestDigest'; Marker = 'SECRET_RAW_MANIFEST_DIGEST_MARKER'; Message = 'runtime manifest digest must be a lowercase SHA-256 digest' },
        @{ Name = 'artifact-path-secret'; Key = 'ArtifactPath'; Marker = 'SECRET_RAW_ARTIFACT_PATH_MARKER'; Message = 'runtime planning artifact path must identify one existing canonical file' },
        @{ Name = 'manifest-file-path-secret'; Key = 'ManifestFilePath'; Marker = 'SECRET_RAW_MANIFEST_PATH_MARKER'; Message = 'runtime acceptance manifest path must identify one existing canonical file' },
        @{ Name = 'v1-manifest-path-secret'; Key = 'V1ManifestPath'; Marker = 'SECRET_RAW_V1_PATH_MARKER'; Message = 'runtime FullChain v1 manifest path must identify one existing canonical file' },
        @{ Name = 'repository-root-secret'; Key = 'RepositoryRoot'; Marker = 'SECRET_RAW_REPOSITORY_ROOT_MARKER'; Message = 'runtime repository root must identify one existing canonical directory' },
        @{ Name = 'manifest-repository-path-secret'; Key = 'ManifestPath'; Marker = 'SECRET_RAW_MANIFEST_REPOSITORY_PATH_MARKER'; Message = 'must equal the authoritative repository manifest' },
        @{ Name = 'event-secret'; Key = 'Event'; Marker = 'SECRET_RAW_EVENT_MARKER'; Message = 'runtime event must be one of' },
        @{ Name = 'run-attempt-secret'; Key = 'RunAttempt'; Marker = 'SECRET_RAW_ATTEMPT_MARKER'; Message = 'runtime run attempt must be a canonical positive integer' },
        @{ Name = 'workflow-path-secret'; Key = 'WorkflowPath'; Marker = 'SECRET_RAW_WORKFLOW_PATH_MARKER'; Message = 'runtime workflow path must identify one existing canonical file' },
        @{ Name = 'workflow-job-secret'; Key = 'WorkflowJobName'; Marker = 'SECRET_RAW_WORKFLOW_JOB_MARKER'; Message = 'must define exactly one configured runtime job' },
        @{ Name = 'workflow-step-secret'; Key = 'WorkflowStepName'; Marker = 'SECRET_RAW_WORKFLOW_STEP_MARKER'; Message = 'must define exactly one timed configured runtime step' }
    )) {
        Assert-RunnerBoundaryRejected -Name $secretMutation.Name -Overrides @{ $secretMutation.Key = $secretMutation.Marker } -SecretMarkers @($secretMutation.Marker) -ExpectedMessage $secretMutation.Message -ArtifactPath $artifactPath -ArtifactDigest $artifactDigest -ManifestDigest $manifestDigest -WorkflowPath $workflowPath
    }

    $externalManifestPath = Join-Path $repositoryFixtureRoot 'external-authority/acceptance-scenario-matrix.json'
    Write-JsonFixture -Path $externalManifestPath -Value $manifest
    $externalManifestPath = (Resolve-Path -LiteralPath $externalManifestPath).Path
    Assert-PreflightRejected -Name 'manifest-external-copy' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ManifestFilePathOverride $externalManifestPath -ExpectedMessage 'must equal the authoritative repository manifest'

    $manifestLinkPath = Join-Path $repositoryFixtureRoot 'acceptance-scenario-matrix-link.json'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $manifestLinkPath)) | Out-Null
    [IO.File]::CreateSymbolicLink($manifestLinkPath, $manifestPath) | Out-Null
    Assert-PreflightRejected -Name 'manifest-symbolic-link' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ManifestFilePathOverride $manifestLinkPath -ExpectedMessage 'must not contain a symbolic link'

    $manifestAliasPath = Join-Path $repoRoot 'scripts/../scripts/acceptance-scenario-matrix.json'
    Assert-PreflightRejected -Name 'manifest-path-alias' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ManifestFilePathOverride $manifestAliasPath -ExpectedMessage 'must identify one existing canonical file'

    $externalV1ManifestPath = Join-Path $repositoryFixtureRoot 'external-authority/full-chain-test-lane.json'
    Write-JsonFixture -Path $externalV1ManifestPath -Value (Get-Content -LiteralPath $v1ManifestPath -Raw | ConvertFrom-Json -Depth 50)
    $externalV1ManifestPath = (Resolve-Path -LiteralPath $externalV1ManifestPath).Path
    Assert-PreflightRejected -Name 'v1-manifest-external-copy' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -V1ManifestPathOverride $externalV1ManifestPath -ExpectedMessage 'must equal the authoritative FullChain v1 manifest'

    $repositoryRootAlias = Join-Path $repoRoot 'scripts/..'
    Assert-PreflightRejected -Name 'repository-root-path-alias' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -RepositoryRootOverride $repositoryRootAlias -ExpectedMessage 'repository root must identify one existing canonical directory'

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
    $failedActionCalls = [Collections.Generic.List[object]]::new()
    $failedAction = {
        param([object] $Contract)
        $failedActionCalls.Add($Contract)
        throw [InvalidOperationException]::new('fixture-runtime-action-failed')
    }.GetNewClosure()
    $failedArguments = Get-RunnerArguments -ArtifactPath $artifactPath -ExpectedArtifactDigest $artifactDigest -ManifestFilePath $manifestPath -ExpectedManifestDigest $manifestDigest -WorkflowPath $workflowPath -SummaryPath $failedSummaryPath -Action $failedAction
    $observedFailure = $null
    try { & $runnerPath @failedArguments | Out-Null }
    catch { $observedFailure = $_.Exception }
    Assert-Contract ($failedActionCalls.Count -eq 1) 'A throwing runtime action must be invoked exactly once.'
    Assert-Contract ($observedFailure -is [InvalidOperationException]) 'Runtime action failure must preserve the original exception type.'
    Assert-Contract ([string]::Equals([string]$observedFailure.Message, 'fixture-runtime-action-failed', [StringComparison]::Ordinal)) 'Runtime action failure must preserve the original exception message.'
    $persistedFailureSummary = Get-Content -LiteralPath $failedSummaryPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$persistedFailureSummary.status, 'failed', [StringComparison]::Ordinal)) 'A throwing runtime action must persist failed status.'
    Assert-Contract ([string]::Equals(($persistedFailureSummary.transitions.state -join '|'), 'preflight-started|preflight-passed|action-started|action-failed', [StringComparison]::Ordinal)) 'A throwing runtime action must atomically persist action-failed as its final transition.'
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $failedSummaryPath) -Filter '*.tmp' -File).Count -eq 0) 'Failed summary persistence must not leave temporary files.'

    foreach ($mutation in @(
        @{ Name = 'repository'; Artifact = { param($value) $value.repository = 'mang-x/Nerv-IIP' }; Message = 'repository does not match' },
        @{ Name = 'tested-sha'; Artifact = { param($value) $value.testedSha = '1123456789abcdef0123456789abcdef01234567' }; Message = 'testedSha does not match' },
        @{ Name = 'run-id'; Artifact = { param($value) $value.runId = '987654321' }; Message = 'runId does not match' },
        @{ Name = 'run-attempt'; Artifact = { param($value) $value.runAttempt = 3 }; Message = 'runAttempt does not match' },
        @{ Name = 'event-wrong-case'; Artifact = { param($value) $value.event = 'WORKFLOW_DISPATCH' }; Message = 'Planning event' },
        @{ Name = 'selection-mode-self-derived'; Artifact = { param($value) $value.selectionMode = 'workflow-dispatch-all-active' }; Message = 'all-active selection provenance is inconsistent' },
        @{ Name = 'selection-reasons-self-derived'; Artifact = { param($value) $value.selectionReasons = @('tampered-but-self-derived') }; Message = 'scenario selection provenance is inconsistent' },
        @{ Name = 'manifest-path'; Artifact = { param($value) $value.manifestPath = 'scripts/Acceptance-scenario-matrix.json' }; Message = 'manifestPath does not match' },
        @{ Name = 'manifest-digest'; Artifact = { param($value) $value.manifestDigest = ('f' * 64) }; Message = 'manifestDigest does not match'; PreserveManifestDigest = $true },
        @{ Name = 'scenario-missing'; Artifact = { param($value) $value.scenarios = @() }; Message = 'scenario selection provenance is inconsistent' },
        @{ Name = 'artifact-scenarios-scalar'; Artifact = { param($value) $value.scenarios = $value.scenarios[0] }; Message = 'scenarios must be an array' },
        @{ Name = 'scenario-extra-with-stale-projects'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], [pscustomobject]@{ id = 'wms-delivery-erp'; status = 'active'; tier = 'core' }) }; Message = 'scenario selection provenance is inconsistent' },
        @{ Name = 'scenario-duplicate'; Artifact = { param($value) $value.scenarios = @($value.scenarios[0], (Copy-JsonObject $value.scenarios[0])) }; Message = 'duplicate selected scenario' },
        @{ Name = 'scenario-wrong-case'; Artifact = { param($value) $value.scenarios[0].id = 'Sales-order-demand' }; Message = 'must identify one selected active/core' },
        @{ Name = 'scenario-blocked'; Artifact = { param($value) $value.scenarios[0].id = 'equipment-unavailable-scheduling-mes'; $value.scenarios[0].status = 'blocked'; $value.scenarios[0].tier = 'extended' }; Message = 'must identify one selected active/core' },
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
    Assert-PreflightRejected -Name 'artifact-digest-wrong-case' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'runtime artifact digest must be a lowercase' -ExpectedArtifactDigest $artifactDigest.ToUpperInvariant()
    Assert-PreflightRejected -Name 'manifest-digest-wrong-case' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'runtime manifest digest must be a lowercase' -ExpectedManifestDigest $manifestDigest.ToUpperInvariant()
    $wrongCaseEventArtifact = Copy-JsonObject $artifact
    $wrongCaseEventArtifact.event = 'WORKFLOW_DISPATCH'
    Assert-PreflightRejected -Name 'trusted-event-wrong-case' -Artifact $wrongCaseEventArtifact -Manifest (Copy-JsonObject $manifest) -WorkflowPath $workflowPath -ExpectedMessage 'runtime event must be one of' -Event 'WORKFLOW_DISPATCH'

    $shortWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-short' -StepTimeoutMinutes 37
    Assert-PreflightRejected -Name 'execution-budget-shortened' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $shortWorkflowPath -ExpectedMessage 'must be strictly less than'

    $wrongStepWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-step' -StepName 'Run drifted acceptance scenario'
    Assert-PreflightRejected -Name 'workflow-step-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $wrongStepWorkflowPath -ExpectedMessage 'exactly one timed'

    $wrongCommandWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-wrong-command' -Run 'pwsh scripts/run-full-chain-test-lane.ps1'
    Assert-PreflightRejected -Name 'workflow-command-drift' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $wrongCommandWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    $windowsCommandOnUbuntuWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-windows-command-on-ubuntu' -Run 'pwsh.exe scripts/run-acceptance-scenario-matrix.ps1'
    Assert-PreflightRejected -Name 'workflow-windows-command-on-ubuntu' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $windowsCommandOnUbuntuWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    $hereStringRun = "|`n          @'`n          pwsh scripts/run-acceptance-scenario-matrix.ps1`n          '@ | Out-Null"
    $hereStringWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-here-string' -Run $hereStringRun
    Assert-PreflightRejected -Name 'workflow-here-string-data' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $hereStringWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    foreach ($nonExecutingCommand in @(
        @{ Name = 'comment'; Run = '# pwsh scripts/run-acceptance-scenario-matrix.ps1' },
        @{ Name = 'assignment'; Run = "`$commandText = 'pwsh scripts/run-acceptance-scenario-matrix.ps1'" },
        @{ Name = 'echo'; Run = "Write-Output 'pwsh scripts/run-acceptance-scenario-matrix.ps1'" },
        @{ Name = 'string-data'; Run = "'pwsh scripts/run-acceptance-scenario-matrix.ps1'" },
        @{ Name = 'direct-call-in-data'; Run = "Write-Output './scripts/run-acceptance-scenario-matrix.ps1 -ArtifactPath `$artifactPath'" },
        @{ Name = 'nested-non-top-level'; Run = "|`n          if (`$true) {`n            ./scripts/run-acceptance-scenario-matrix.ps1 -ArtifactPath `$artifactPath`n          }" }
    )) {
        $nonExecutingWorkflowPath = Write-RuntimeWorkflowFixture -Name "runtime-workflow-$($nonExecutingCommand.Name)" -Run $nonExecutingCommand.Run
        Assert-PreflightRejected -Name "workflow-$($nonExecutingCommand.Name)-data" -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $nonExecutingWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'
    }

    $duplicateRunnerWorkflowPath = Write-RuntimeWorkflowFixture -Name 'runtime-workflow-duplicate-runner' -Run "|`n          pwsh scripts/run-acceptance-scenario-matrix.ps1`n          pwsh scripts/run-acceptance-scenario-matrix.ps1"
    Assert-PreflightRejected -Name 'workflow-duplicate-runner' -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $duplicateRunnerWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'

    foreach ($trailingCommand in @(
        @{ Name = 'literal'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 unexpected' },
        @{ Name = 'expression'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 $env:GITHUB_RUN_ID' },
        @{ Name = 'parenthesis'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 (Get-Date)' },
        @{ Name = 'splatting'; Run = 'pwsh scripts/run-acceptance-scenario-matrix.ps1 @runtimeArguments' }
    )) {
        $trailingWorkflowPath = Write-RuntimeWorkflowFixture -Name "runtime-workflow-trailing-$($trailingCommand.Name)" -Run $trailingCommand.Run
        Assert-PreflightRejected -Name "workflow-trailing-$($trailingCommand.Name)" -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $trailingWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'
    }

    foreach ($inlineParameter in @(
        @{ Name = 'switch-value'; Run = 'pwsh -NoLogo:$false scripts/run-acceptance-scenario-matrix.ps1' },
        @{ Name = 'file-value'; Run = 'pwsh -File:$false scripts/run-acceptance-scenario-matrix.ps1' }
    )) {
        $inlineParameterWorkflowPath = Write-RuntimeWorkflowFixture -Name "runtime-workflow-inline-$($inlineParameter.Name)" -Run $inlineParameter.Run
        Assert-PreflightRejected -Name "workflow-inline-$($inlineParameter.Name)" -Artifact (Copy-JsonObject $artifact) -Manifest (Copy-JsonObject $manifest) -WorkflowPath $inlineParameterWorkflowPath -ExpectedMessage 'must invoke scripts/run-acceptance-scenario-matrix.ps1'
    }

    $validManifestJson = $manifest | ConvertTo-Json -Depth 50 -Compress
    $validV1ManifestJson = Get-Content -LiteralPath $v1ManifestPath -Raw
    $validRawManifestDigest = Get-FixtureBytesDigest -Bytes ([Text.UTF8Encoding]::new($false).GetBytes($validManifestJson))
    $rawArtifact = New-SalesPlanningArtifact -Manifest $manifest -ManifestDigest $validRawManifestDigest
    $validArtifactJson = $rawArtifact | ConvertTo-Json -Depth 50 -Compress

    $duplicateArtifactJson = Replace-FirstOrdinal -Value $validArtifactJson -OldValue '"path":"backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj"' -NewValue '"path":"backend/tests/Invalid/Invalid.csproj","path":"backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj"'
    Assert-RawJsonPreflightRejected -Name 'artifact-exact-duplicate-key' -ArtifactJson $duplicateArtifactJson -ManifestJson $validManifestJson -V1ManifestJson $validV1ManifestJson -WorkflowPath $workflowPath -ExpectedMessage 'contains duplicate JSON property'

    $duplicateManifestJson = Replace-FirstOrdinal -Value $validManifestJson -OldValue '"kind":"script","path":"scripts/verify-erp-sales-order-demand-planning.ps1"' -NewValue '"kind":"script","path":"scripts/invalid.ps1","path":"scripts/verify-erp-sales-order-demand-planning.ps1"'
    $duplicateManifestDigest = Get-FixtureBytesDigest -Bytes ([Text.UTF8Encoding]::new($false).GetBytes($duplicateManifestJson))
    $duplicateManifestArtifactJson = (New-SalesPlanningArtifact -Manifest $manifest -ManifestDigest $duplicateManifestDigest) | ConvertTo-Json -Depth 50 -Compress
    Assert-RawJsonPreflightRejected -Name 'v2-manifest-exact-duplicate-key' -ArtifactJson $duplicateManifestArtifactJson -ManifestJson $duplicateManifestJson -V1ManifestJson $validV1ManifestJson -WorkflowPath $workflowPath -ExpectedMessage 'contains duplicate JSON property'

    $duplicateV1ManifestJson = Replace-FirstOrdinal -Value $validV1ManifestJson -OldValue '"dependencies": { "postgres": true, "redis": true, "externalProcesses": true }' -NewValue '"dependencies": { "postgres": false, "postgres": true, "redis": true, "externalProcesses": true }'
    Assert-RawJsonPreflightRejected -Name 'v1-manifest-exact-duplicate-key' -ArtifactJson $validArtifactJson -ManifestJson $validManifestJson -V1ManifestJson $duplicateV1ManifestJson -WorkflowPath $workflowPath -ExpectedMessage 'contains duplicate JSON property'

    foreach ($v1CasingMutation in @(
        @{ Name = 'top-schema-version'; Old = '"schemaVersion": 1'; New = '"SchemaVersion": 1'; Message = 'unknown field' },
        @{ Name = 'top-members'; Old = '"members": ['; New = '"Members": ['; Message = 'unknown field' },
        @{ Name = 'member-id'; Old = '"id": "maintenance-runtime-hours"'; New = '"Id": "maintenance-runtime-hours"'; Message = 'unknown field' },
        @{ Name = 'entrypoint-kind'; Old = '"entrypoint": { "kind": "fullstack"'; New = '"entrypoint": { "Kind": "fullstack"'; Message = 'unknown field' },
        @{ Name = 'dependencies-postgres'; Old = '"dependencies": { "postgres": true'; New = '"dependencies": { "Postgres": true'; Message = 'unknown field' },
        @{ Name = 'diagnostic-schemas'; Old = '"diagnosticSchemas":'; New = '"DiagnosticSchemas":'; Message = 'unknown field' },
        @{ Name = 'expected-test-identities'; Old = '"expectedTestIdentities":'; New = '"ExpectedTestIdentities":'; Message = 'unknown field' }
    )) {
        $wrongCaseV1Json = Replace-FirstOrdinal -Value $validV1ManifestJson -OldValue $v1CasingMutation.Old -NewValue $v1CasingMutation.New
        Assert-RawJsonPreflightRejected -Name "v1-wrong-case-$($v1CasingMutation.Name)" -ArtifactJson $validArtifactJson -ManifestJson $validManifestJson -V1ManifestJson $wrongCaseV1Json -WorkflowPath $workflowPath -ExpectedMessage $v1CasingMutation.Message
    }

    $firstEquivalenceInput.volatile.cleanupErrors = @('cleanup failed for database nerv_shadow_run_1 pid 101 cap attempt-1-aabbcc at 2026-08-19T01:01:00Z')
    $secondEquivalenceInput.volatile.cleanupErrors = @('cleanup failed for database nerv_shadow_run_2 pid 991 cap attempt-2-ddeeff at 2026-08-19T02:01:00Z')
    $firstVector = New-NervAcceptanceScenarioEquivalenceVector -Result $firstEquivalenceInput -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance
    $secondVector = New-NervAcceptanceScenarioEquivalenceVector -Result $secondEquivalenceInput -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance
    $firstVectorJson = $firstVector | ConvertTo-Json -Depth 50 -Compress
    $secondVectorJson = $secondVector | ConvertTo-Json -Depth 50 -Compress
    Assert-Contract ([string]::Equals($firstVectorJson, $secondVectorJson, [StringComparison]::Ordinal)) 'Database names, PIDs, CAP suffixes, and timestamps must not participate in equivalence.'
    Assert-Contract (-not $firstVectorJson.Contains('track', [StringComparison]::Ordinal)) 'The caller-supplied track identifier must not participate in equivalence.'
    foreach ($volatileName in @('databaseName', 'processIds', 'capSuffix', 'startedAtUtc', 'completedAtUtc', 'cleanupErrors', 'ports', 'paths')) {
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
        try { New-NervAcceptanceScenarioEquivalenceVector -Result $mutatedStableResult -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance | Out-Null }
        catch { $stableStringMessage = $_.Exception.Message }
        Assert-Contract ($stableStringMessage.Contains($stableStringMutation.Message, [StringComparison]::Ordinal)) "Stable string mutation '$($stableStringMutation.Name)' must fail with '$($stableStringMutation.Message)'; observed '$stableStringMessage'."
    }
    $stableDrift = Copy-JsonObject $secondEquivalenceInput
    $stableDrift.businessFacts.duplicateConverged = $false
    $stableDriftJson = (New-NervAcceptanceScenarioEquivalenceVector -Result $stableDrift -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance | ConvertTo-Json -Depth 50 -Compress)
    Assert-Contract (-not [string]::Equals($firstVectorJson, $stableDriftJson, [StringComparison]::Ordinal)) 'A stable business checkpoint drift must change the equivalence vector.'

    $stableCleanupCodeDrift = Copy-JsonObject $secondEquivalenceInput
    $stableCleanupCodeDrift.cleanup.errorCodes = @('owned-resource-cleanup-failed')
    $stableCleanupCodeJson = (New-NervAcceptanceScenarioEquivalenceVector -Result $stableCleanupCodeDrift -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance | ConvertTo-Json -Depth 50 -Compress)
    Assert-Contract (-not [string]::Equals($firstVectorJson, $stableCleanupCodeJson, [StringComparison]::Ordinal)) 'A stable cleanup error code drift must change the equivalence vector.'
    Assert-Contract ($stableCleanupCodeJson.Contains('owned-resource-cleanup-failed', [StringComparison]::Ordinal)) 'The equivalence vector must retain canonical cleanup error codes.'

    $invalidCleanupCode = Copy-JsonObject $firstEquivalenceInput
    $invalidCleanupCode.cleanup.errorCodes = @('cleanup failed for database nerv_shadow_run_1')
    $invalidCleanupCodeRejected = $false
    try { New-NervAcceptanceScenarioEquivalenceVector -Result $invalidCleanupCode -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance | Out-Null }
    catch { $invalidCleanupCodeRejected = $_.Exception.Message.Contains('must be canonical', [StringComparison]::Ordinal) }
    Assert-Contract $invalidCleanupCodeRejected 'A free-text cleanup error must not enter the stable error code set.'

    $extraEquivalenceField = Copy-JsonObject $firstEquivalenceInput
    $extraEquivalenceField | Add-Member -NotePropertyName ungoverned -NotePropertyValue $true
    $extraRejected = $false
    try { New-NervAcceptanceScenarioEquivalenceVector -Result $extraEquivalenceField -ValidatedScenario $runtimeResult.contract.scenario -ExpectedProvenance $runtimeResult.contract.provenance | Out-Null }
    catch { $extraRejected = $_.Exception.Message.Contains('unknown field', [StringComparison]::Ordinal) }
    Assert-Contract $extraRejected 'An extra equivalence result field must fail closed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    if (Test-Path -LiteralPath $repositoryFixtureRoot) { Remove-Item -LiteralPath $repositoryFixtureRoot -Recurse -Force }
}
