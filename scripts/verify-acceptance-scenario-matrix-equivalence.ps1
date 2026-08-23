# Script-Governance:
#   Category: check, generate
#   SideEffects:
#     - Reads one governed planning artifact and exactly two canonical acceptance result files
#   Writes:
#     - A caller-declared machine-readable equivalence report through atomic file replacement
#   Cleanup:
#     - Removes owned temporary report files after every persistence attempt
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
    [int] $RunAttempt = $(if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ATTEMPT)) { 0 } else { [int]$env:GITHUB_RUN_ATTEMPT }),
    [int] $PlanningRunAttempt = $RunAttempt,
    [string] $ManifestRepositoryPath = 'scripts/acceptance-scenario-matrix.json',
    [ValidateSet('pull_request', 'push', 'schedule', 'workflow_dispatch')] [string] $Event = $env:GITHUB_EVENT_NAME,
    [Parameter(Mandatory)] [string] $V1ResultPath,
    [int] $V1RunAttempt = $RunAttempt,
    [Parameter(Mandatory)] [string] $ShadowResultPath,
    [int] $ShadowRunAttempt = $RunAttempt,
    [string] $ReportPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/equivalence-report.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrixEquivalence.ps1')

Invoke-NervAcceptanceScenarioMatrixEquivalence `
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
    -ManifestRepositoryPath $ManifestRepositoryPath `
    -Event $Event `
    -V1ResultPath $V1ResultPath `
    -V1RunAttempt $V1RunAttempt `
    -ShadowResultPath $ShadowResultPath `
    -ShadowRunAttempt $ShadowRunAttempt `
    -ReportPath $ReportPath
