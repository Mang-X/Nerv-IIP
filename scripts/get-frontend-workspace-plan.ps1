# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the CI impact plan, frontend manifests, and frontend unit-test sources
#     - Writes bounded frontend selection and inventory artifacts plus optional GitHub outputs
#   Writes:
#     - artifacts/ci-impact-plan/frontend-workspace-plan.json
#     - artifacts/ci-impact-plan/frontend-test-inventory.json
#     - Caller-provided GitHub output and step-summary files
#   Cleanup:
#     - None required; outputs are bounded files owned by the current CI run
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ImpactPlanPath,
    [Parameter(Mandatory)] [ValidateSet('Affected', 'Full')] [string] $Mode,
    [string] $PlanOutputPath = 'artifacts/ci-impact-plan/frontend-workspace-plan.json',
    [string] $InventoryOutputPath = 'artifacts/ci-impact-plan/frontend-test-inventory.json',
    [string] $SkipAllowlistPath = 'scripts/frontend-test-skip-allowlist.json',
    [string] $GitHubOutputPath = $env:GITHUB_OUTPUT,
    [string] $StepSummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FrontendWorkspacePlan.ps1')

function Resolve-RepoPath([string] $Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return Join-Path $repoRoot $Path
}

$resolvedImpactPlanPath = Resolve-RepoPath $ImpactPlanPath
$impactPlan = Get-Content -LiteralPath $resolvedImpactPlanPath -Raw | ConvertFrom-Json
if ([int]$impactPlan.schema_version -ne 1 -or $null -eq $impactPlan.changed_paths -or $null -eq $impactPlan.frontend) {
    throw 'CI impact plan is missing the schema v1 changed_paths/frontend contract.'
}

$inventory = Get-NervFrontendWorkspaceInventory `
    -FrontendRoot (Join-Path $repoRoot 'frontend') `
    -SkipAllowlistPath (Resolve-RepoPath $SkipAllowlistPath)
$plan = Get-NervFrontendWorkspacePlan `
    -Inventory $inventory `
    -ChangedPaths @($impactPlan.changed_paths) `
    -FrontendImpacted ([bool]$impactPlan.frontend) `
    -Mode $Mode

$resolvedPlanOutputPath = Resolve-RepoPath $PlanOutputPath
$resolvedInventoryOutputPath = Resolve-RepoPath $InventoryOutputPath
[IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedPlanOutputPath)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedInventoryOutputPath)) | Out-Null
[IO.File]::WriteAllText($resolvedPlanOutputPath, "$(ConvertTo-Json $plan -Depth 20)`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($resolvedInventoryOutputPath, "$(ConvertTo-Json $inventory -Depth 20)`n", [Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    $outputLines = @(
        "frontend_selected=$(([string]$plan.selected).ToLowerInvariant())"
        "frontend_tests_selected=$(([string]$plan.tests_selected).ToLowerInvariant())"
        "frontend_test_matrix=$(ConvertTo-Json $plan.test_matrix -Depth 10 -Compress)"
        "frontend_validation_matrix=$(ConvertTo-Json $plan.validation_matrix -Depth 10 -Compress)"
        "frontend_plan_path=$PlanOutputPath"
        "frontend_inventory_path=$InventoryOutputPath"
    )
    [IO.File]::AppendAllText($GitHubOutputPath, "$(($outputLines -join "`n"))`n", [Text.UTF8Encoding]::new($false))
}

if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) {
    $summary = @(
        '## Frontend affected workspace plan'
        ''
        "Mode: ``$($plan.mode)``"
        "Selection: ``$($plan.selection_reason)``"
        "Projects: ``$(@($plan.projects) -join ', ')``"
        "Inventory: $($inventory.project_count) projects, $($inventory.test_project_count) test projects, $($inventory.test_file_count) test files, $($inventory.skip_count) skips."
        ''
    ) -join "`n"
    [IO.File]::AppendAllText($StepSummaryPath, $summary, [Text.UTF8Encoding]::new($false))
}

Write-Output "Frontend workspace plan written to $resolvedPlanOutputPath"
Write-Output "Frontend test inventory written to $resolvedInventoryOutputPath"
