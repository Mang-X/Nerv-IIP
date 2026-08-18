# Script-Governance:
#   Category: check, generate
#   SideEffects:
#     - Reads the acceptance scenario manifests and a supplied GitHub Actions workflow
#     - Runs dotnet restore and Release --no-restore --list-tests for each selected test project
#     - Writes NuGet caches and project restore intermediates through dotnet restore
#   Writes:
#     - artifacts/acceptance-scenario-matrix/planning.json
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes any stale or partial success artifact on failure
#     - Restores MSBuild node-reuse and dotnet build-server environment variables
#     - Starts no Docker, Redis, database, Aspire, or business process
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'acceptance-scenario-matrix.json'),
    [string] $V1ManifestPath = (Join-Path $PSScriptRoot 'full-chain-test-lane.json'),
    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml'),
    [string] $WorkflowJobName = 'acceptance-scenario-matrix-planning',
    [string] $WorkflowStepName = 'Plan acceptance scenario matrix',
    [string] $ArtifactPath = (Join-Path $PSScriptRoot '../artifacts/acceptance-scenario-matrix/planning.json'),
    [Parameter(Mandatory)] [ValidateSet('pull_request', 'push', 'schedule', 'workflow_dispatch')] [string] $Event,
    [string[]] $ChangedPaths = @(),
    [bool] $ImpactRulesSucceeded = $true,
    [string] $DispatchSelection,
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $TestedSha = $env:GITHUB_SHA,
    [string] $RunId = $env:GITHUB_RUN_ID,
    [int] $RunAttempt = $(if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ATTEMPT)) { 0 } else { [int]$env:GITHUB_RUN_ATTEMPT })
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrix.ps1')

$resolvedArtifactPath = [IO.Path]::GetFullPath($ArtifactPath)
if (Test-Path -LiteralPath $resolvedArtifactPath -PathType Leaf) { Remove-Item -LiteralPath $resolvedArtifactPath -Force }

$manifest = Import-NervAcceptanceScenarioMatrixManifest `
    -ManifestPath $ManifestPath `
    -V1ManifestPath $V1ManifestPath `
    -RepositoryRoot $repoRoot
$selectionParameters = @{
    Manifest = $manifest
    Event = $Event
    ChangedPaths = @($ChangedPaths)
    ImpactRulesSucceeded = $ImpactRulesSucceeded
}
if (-not [string]::IsNullOrWhiteSpace($DispatchSelection)) { $selectionParameters['DispatchSelection'] = $DispatchSelection }
$selection = Select-NervAcceptanceScenarioMatrix @selectionParameters

$resolvedManifestPath = (Resolve-Path $ManifestPath).Path
$relativeManifestPath = [IO.Path]::GetRelativePath($repoRoot, $resolvedManifestPath).Replace('\', '/')
if ($relativeManifestPath.StartsWith('../', [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($relativeManifestPath)) {
    throw "Acceptance scenario manifest must be inside repository root '$repoRoot'."
}
$manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $resolvedManifestPath

$planningAction = {
    Invoke-NervAcceptanceScenarioMatrixPlanning `
        -Manifest $manifest `
        -Selection $selection `
        -RepositoryRoot $repoRoot `
        -Repository $Repository `
        -TestedSha $TestedSha `
        -RunId $RunId `
        -RunAttempt $RunAttempt `
        -ManifestPath $relativeManifestPath `
        -ManifestDigest $manifestDigest `
        -Event $Event `
        -WorkflowPath $WorkflowPath `
        -WorkflowJobName $WorkflowJobName `
        -WorkflowStepName $WorkflowStepName `
        -ArtifactPath $resolvedArtifactPath
}.GetNewClosure()

Invoke-WithScopedEnvironment `
    -Variables @{
        MSBUILDDISABLENODEREUSE = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    } `
    -ScriptBlock $planningAction
