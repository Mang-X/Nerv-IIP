# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Invokes only a caller-supplied in-process acceptance scenario action after runtime preflight succeeds
#   Writes:
#     - A caller-declared runtime summary through atomic file replacement
#   Cleanup:
#     - Delegates exact owned-resource cleanup evidence to the injected action and validates zero remaining resources
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $ArtifactPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/planning.json'),
    [string] $ExpectedArtifactDigest = $env:NERV_IIP_ACCEPTANCE_SCENARIO_ARTIFACT_SHA256,
    [string] $ManifestFilePath = (Join-Path $PSScriptRoot 'acceptance-scenario-matrix.json'),
    [string] $ExpectedManifestDigest = $env:NERV_IIP_ACCEPTANCE_SCENARIO_MANIFEST_SHA256,
    [string] $V1ManifestPath = (Join-Path $PSScriptRoot 'full-chain-test-lane.json'),
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $TestedSha = $env:GITHUB_SHA,
    [string] $RunId = $env:GITHUB_RUN_ID,
    [string] $RunAttempt = $env:GITHUB_RUN_ATTEMPT,
    [string] $ManifestPath = 'scripts/acceptance-scenario-matrix.json',
    [string] $Event = $env:GITHUB_EVENT_NAME,
    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml'),
    [string] $WorkflowJobName = 'acceptance-scenario-matrix-runtime',
    [string] $WorkflowStepName = 'Run acceptance scenario matrix',
    [string] $SummaryPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/runtime-summary.json'),
    [scriptblock] $RuntimeAction
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrixRuntime.ps1')

function Invoke-NervAcceptanceScenarioMatrixFutureAction {
    param([Parameter(Mandatory)] [object] $Contract)

    throw [InvalidOperationException]::new("Acceptance scenario '$($Contract.scenario.id)' real runtime action is not wired in this shadow contract.")
}

$effectiveRuntimeAction = $RuntimeAction
if ($null -eq $effectiveRuntimeAction) {
    $effectiveRuntimeAction = {
        param([object] $Contract)
        Invoke-NervAcceptanceScenarioMatrixFutureAction -Contract $Contract
    }
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
    -ManifestPath $ManifestPath `
    -Event $Event `
    -WorkflowPath $WorkflowPath `
    -WorkflowJobName $WorkflowJobName `
    -WorkflowStepName $WorkflowStepName `
    -SummaryPath $SummaryPath `
    -RuntimeAction $effectiveRuntimeAction
