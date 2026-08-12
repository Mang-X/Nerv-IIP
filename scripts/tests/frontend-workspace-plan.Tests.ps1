# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes temporary frontend workspace mutation fixtures
#   Writes:
#     - OS temporary directory only
#   Cleanup:
#     - Removes all temporary fixtures in finally blocks
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/FrontendWorkspacePlan.ps1')

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "Contract failed: $Message" }
}

function Test-Project([object] $Plan, [string] $Name) {
    foreach ($projectName in @($Plan.projects)) {
        if ([string]::Equals([string]$projectName, $Name, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

$inventory = Get-NervFrontendWorkspaceInventory `
    -FrontendRoot (Join-Path $repoRoot 'frontend') `
    -SkipAllowlistPath (Join-Path $repoRoot 'scripts/frontend-test-skip-allowlist.json')
Assert-Contract ($inventory.project_count -gt 0) 'The live frontend inventory must discover workspace projects.'
Assert-Contract ($inventory.test_project_count -gt 0) 'The live frontend inventory must discover test projects.'
Assert-Contract ($inventory.test_file_count -gt 0) 'The live frontend inventory must discover unit tests.'
Assert-Contract ($inventory.skip_count -eq 1) 'The current frontend unit-test skip inventory must match the governed allowlist.'

$workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
foreach ($requiredWorkflowToken in @(
        "FRONTEND_MODE: `${{ github.event_name == 'push' && 'Full' || 'Affected' }}",
        'frontend_tests_selected: ${{ steps.frontend-plan.outputs.frontend_tests_selected }}',
        "if: needs.impact-plan.outputs.frontend_tests_selected == 'true'",
        'matrix: ${{ fromJSON(needs.impact-plan.outputs.frontend_test_matrix) }}',
        'matrix: ${{ fromJSON(needs.impact-plan.outputs.frontend_validation_matrix) }}',
        'name: Frontend Unit Tests',
        'name: Frontend Typecheck and Build',
        'test "$FRONTEND_TESTS_SELECTED" = "false"',
        'test "$FRONTEND_SELECTED" = "false"',
        'test "$SHARD_RESULT" = "skipped"',
        'test "$CHECK_RESULT" = "skipped"',
        'test "$VALIDATION_RESULT" = "skipped"',
        'run: ./scripts/tests/frontend-workspace-plan.Tests.ps1')) {
    Assert-Contract ($workflow.Contains($requiredWorkflowToken, [StringComparison]::Ordinal)) "Frontend workflow is missing fail-closed token '$requiredWorkflowToken'."
}
Assert-Contract (-not $workflow.Contains('continue-on-error', [StringComparison]::Ordinal)) 'Frontend affected routing must not be weakened by continue-on-error.'

$screenPlan = Get-NervFrontendWorkspacePlan `
    -Inventory $inventory `
    -ChangedPaths @('frontend/apps/screen/src/pages/warehouse.vue') `
    -FrontendImpacted $true `
    -Mode Affected
foreach ($expected in @('@nerv-iip/screen', '@nerv-iip/api-client', '@nerv-iip/auth', '@nerv-iip/ui')) {
    Assert-Contract (Test-Project -Plan $screenPlan -Name $expected) "Screen changes must select '$expected'."
}
foreach ($unrelated in @('@nerv-iip/business-console', '@nerv-iip/business-pda', '@nerv-iip/console')) {
    Assert-Contract (-not (Test-Project -Plan $screenPlan -Name $unrelated)) "Screen changes must not select unrelated app '$unrelated'."
}

$uiPlan = Get-NervFrontendWorkspacePlan `
    -Inventory $inventory `
    -ChangedPaths @('frontend/packages/ui/src/button.ts') `
    -FrontendImpacted $true `
    -Mode Affected
foreach ($consumer in @('@nerv-iip/business-console', '@nerv-iip/business-pda', '@nerv-iip/console', '@nerv-iip/screen', '@nerv-iip/design-system')) {
    Assert-Contract (Test-Project -Plan $uiPlan -Name $consumer) "Shared UI changes must select consumer '$consumer'."
}

$fullPlan = Get-NervFrontendWorkspacePlan -Inventory $inventory -ChangedPaths @('docs/readme.md') -FrontendImpacted $false -Mode Full
Assert-Contract ($fullPlan.projects.Count -eq $inventory.project_count) 'Full mode must select every frontend workspace project.'
Assert-Contract ($fullPlan.test_matrix.include.Count -eq $inventory.test_project_count) 'Full mode must execute every test-script project.'

$emptyPlan = Get-NervFrontendWorkspacePlan -Inventory $inventory -ChangedPaths @('docs/readme.md') -FrontendImpacted $false -Mode Affected
Assert-Contract (-not $emptyPlan.selected) 'A non-frontend PR must produce an explicit empty frontend plan.'
Assert-Contract ($emptyPlan.projects.Count -eq 0) 'A non-frontend PR must not select any workspace project.'

$rootConfigPlan = Get-NervFrontendWorkspacePlan -Inventory $inventory -ChangedPaths @('frontend/vite.config.ts') -FrontendImpacted $true -Mode Affected
Assert-Contract ($rootConfigPlan.projects.Count -eq $inventory.project_count) 'Frontend root configuration changes must conservatively select all projects.'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-frontend-workspace-plan-$([Guid]::NewGuid().ToString('N'))"
try {
    $fixtureFrontend = Join-Path $fixtureRoot 'frontend'
    $fixtureApp = Join-Path $fixtureFrontend 'apps/example'
    [IO.Directory]::CreateDirectory((Join-Path $fixtureApp 'src')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixtureFrontend 'packages')) | Out-Null
    $fixtureAllowlist = Join-Path $fixtureRoot 'allowlist.json'
    [IO.File]::WriteAllText($fixtureAllowlist, '{"version":1,"entries":[]}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $fixtureApp 'package.json'), '{"name":"@nerv-iip/example","scripts":{"typecheck":"tsc"}}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test('x', () => {})", [Text.UTF8Encoding]::new($false))

    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a missing test-script failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('contains unit tests but has no test script', [StringComparison]::Ordinal)) 'A tested workspace without a test script must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'package.json'), '{"name":"@nerv-iip/example","scripts":{"test":"vitest run","typecheck":"tsc","build":"vite build"}}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test.only('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a committed test.only failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('Committed test.only is forbidden', [StringComparison]::Ordinal)) 'Committed test.only must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test.only`n('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a multiline test.only failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('Committed test.only is forbidden', [StringComparison]::Ordinal)) 'A multiline committed test.only must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test?.['skip']('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a computed test.skip failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('requires an allowlist entry', [StringComparison]::Ordinal)) 'A computed committed test.skip must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test.concurrent`n  .only('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a chained test.concurrent.only failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('Committed test.only is forbidden', [StringComparison]::Ordinal)) 'A chained test.concurrent.only must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "import { test as check } from 'vitest'`ncheck.skip('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected a Vitest API alias failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('Aliasing Vitest test API', [StringComparison]::Ordinal)) 'A renamed Vitest test API import must fail closed.'
    }

    [IO.File]::WriteAllText((Join-Path $fixtureApp 'src/example.test.ts'), "test.skip('x', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected an unallowlisted test.skip failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('requires an allowlist entry', [StringComparison]::Ordinal)) 'Unallowlisted test.skip must fail closed.'
    }

    [IO.File]::WriteAllText(
        $fixtureAllowlist,
        '{"version":1,"entries":[{"path":"apps/example/src/example.test.ts","line":1,"owner":"frontend","reason":"temporary fixture","expires":"2099-01-01"}]}',
        [Text.UTF8Encoding]::new($false))
    $allowlistedInventory = Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist
    Assert-Contract ($allowlistedInventory.skip_count -eq 1) 'A complete non-expired skip allowlist row must be counted.'

    $orphanDirectory = Join-Path $fixtureFrontend 'apps/orphan/src'
    [IO.Directory]::CreateDirectory($orphanDirectory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $orphanDirectory 'ghost.test.ts'), "test('ghost', () => {})", [Text.UTF8Encoding]::new($false))
    try {
        Get-NervFrontendWorkspaceInventory -FrontendRoot $fixtureFrontend -SkipAllowlistPath $fixtureAllowlist | Out-Null
        throw 'Expected an orphan unit-test failure.'
    }
    catch {
        Assert-Contract ($_.Exception.Message.Contains('is not owned by a discovered workspace manifest src graph', [StringComparison]::Ordinal)) 'A unit test outside the discovered workspace graph must fail closed.'
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Frontend workspace plan contracts passed.'
