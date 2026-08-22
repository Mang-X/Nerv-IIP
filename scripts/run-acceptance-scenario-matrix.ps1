# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Invokes the governed ERP sales-order demand verifier once after runtime preflight succeeds
#     - Preserves a caller-supplied in-process acceptance scenario action for pure contract tests
#   Writes:
#     - A caller-declared runtime summary through atomic file replacement
#     - A caller-selected canonical sales-order-demand result through the governed verifier
#     - Existing verifier evidence, cleanup evidence, diagnostics, and script logs
#   Cleanup:
#     - Delegates exact owned-resource cleanup evidence to the injected action and validates zero remaining resources
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $ArtifactPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/planning.json')),
    [string] $ExpectedArtifactDigest = $env:NERV_IIP_ACCEPTANCE_SCENARIO_ARTIFACT_SHA256,
    [string] $ManifestFilePath = (Join-Path $PSScriptRoot 'acceptance-scenario-matrix.json'),
    [string] $ExpectedManifestDigest = $env:NERV_IIP_ACCEPTANCE_SCENARIO_MANIFEST_SHA256,
    [string] $V1ManifestPath = (Join-Path $PSScriptRoot 'full-chain-test-lane.json'),
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $TestedSha = $env:GITHUB_SHA,
    [string] $RunId = $env:GITHUB_RUN_ID,
    [string] $RunAttempt = $env:GITHUB_RUN_ATTEMPT,
    [string] $PlanningRunAttempt = $RunAttempt,
    [string] $ManifestPath = 'scripts/acceptance-scenario-matrix.json',
    [string] $Event = $env:GITHUB_EVENT_NAME,
    [string] $WorkflowPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.github/workflows/ci.yml')),
    [string] $WorkflowJobName = 'acceptance-scenario-matrix-runtime',
    [string] $WorkflowStepName = 'Run acceptance scenario matrix',
    [string] $SummaryPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/runtime-summary.json'),
    [string] $CanonicalResultPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/sales-order-demand-result.json'),
    [string] $TrackIdentifier = 'shadow',
    [scriptblock] $RuntimeAction
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrixRuntime.ps1')

function Invoke-NervAcceptanceScenarioMatrixSalesOrderAction {
    param(
        [Parameter(Mandatory)] [object] $Contract,
        [Parameter(Mandatory)] [string] $ResultPath,
        [Parameter(Mandatory)] [string] $Track
    )

    $canonicalResultPath = [IO.Path]::GetFullPath($ResultPath)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $canonicalResultPath)) | Out-Null
    Invoke-PwshScript `
        -ScriptPath (Join-Path $PSScriptRoot 'verify-erp-sales-order-demand-planning.ps1') `
        -Arguments @(
            '-CanonicalResultPath', $canonicalResultPath,
            '-TrackIdentifier', $Track,
            '-Repository', [string]$Contract.provenance.repository,
            '-RunId', [string]$Contract.provenance.runId,
            '-RunAttempt', [string]$Contract.provenance.runAttempt,
            '-TestedSha', [string]$Contract.provenance.testedSha,
            '-ManifestDigest', [string]$Contract.provenance.manifestDigest,
            '-ScenarioId', [string]$Contract.provenance.scenarioId
        ) `
        -WorkingDirectory $RepositoryRoot `
        -TimeoutSeconds ([int]$Contract.requiredSeconds) `
        -Name 'acceptance-scenario-matrix-sales-order-demand' | Out-Null
    return (Read-NervAcceptanceRuntimeJsonSnapshot -Path $canonicalResultPath -Context 'sales-order-demand canonical runtime result').value
}

$effectiveRuntimeAction = $RuntimeAction
if ($null -eq $effectiveRuntimeAction) {
    $salesOrderAction = ${function:Invoke-NervAcceptanceScenarioMatrixSalesOrderAction}
    $effectiveRuntimeAction = {
        param([object] $Contract)
        $salesOrderAction.Invoke($Contract, $CanonicalResultPath, $TrackIdentifier)
    }.GetNewClosure()
}

Invoke-NervAcceptanceScenarioRuntime `
    -ArtifactPath $ArtifactPath `
    -ExpectedArtifactDigest $ExpectedArtifactDigest `
    -ManifestFilePath $ManifestFilePath `
    -ExpectedManifestDigest $ExpectedManifestDigest `
    -V1ManifestPath $V1ManifestPath `
    -RepositoryRoot $RepositoryRoot `
    -Repository $Repository `
    -TestedSha $TestedSha `
    -RunId $RunId `
    -RunAttempt $RunAttempt `
    -PlanningRunAttempt $PlanningRunAttempt `
    -ManifestPath $ManifestPath `
    -Event $Event `
    -WorkflowPath $WorkflowPath `
    -WorkflowJobName $WorkflowJobName `
    -WorkflowStepName $WorkflowStepName `
    -SummaryPath $SummaryPath `
    -RuntimeAction $effectiveRuntimeAction
