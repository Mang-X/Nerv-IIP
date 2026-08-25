# Script-Governance:
#   Category: check
#   SideEffects:
#     - Loads the source-size governance policy library
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/SourceSizeGovernance.ps1'
. $libraryPath
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-Equal {
    param(
        [AllowNull()] [object] $Actual,
        [AllowNull()] [object] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

# Mutation killed: treating empty text as one physical line.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text '') 0 'Empty text has no physical lines'
# Mutation killed: counting only newline terminators and missing the final unterminated line.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text 'one') 1 'Single unterminated line counts once'
# Mutation killed: adding a phantom line after a trailing LF.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`n") 1 'Trailing LF does not add a line'
# Mutation killed: counting CR and LF separately.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`r`ntwo") 2 'CRLF is one line boundary'
# Mutation killed: ignoring classic-Mac CR line boundaries.
Assert-Equal (Get-NervSourcePhysicalLineCount -Text "one`rtwo`r") 2 'CR boundaries are physical lines'

# Mutation killed: changing the new-file comparison from greater-than to greater-than-or-equal.
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status A -Path 'src/New.cs' -BaseLineCount $null -HeadLineCount 1000 -MaximumLines 1000)) 'New file at the limit must pass'
Assert-Equal (Get-NervSourceSizeViolation -Status A -Path 'src/New.cs' -BaseLineCount $null -HeadLineCount 1001 -MaximumLines 1000).Rule 'new-file-over-limit' 'New file over the limit must fail'
# Mutation killed: applying the fixed 1000-line ceiling to already oversized files.
Assert-Equal (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1201 -MaximumLines 1000).Rule 'oversized-file-growth' 'Oversized legacy growth must fail'
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1200 -MaximumLines 1000)) 'Oversized legacy hold must pass'
Assert-True ($null -eq (Get-NervSourceSizeViolation -Status M -Path 'src/Legacy.cs' -BaseLineCount 1200 -HeadLineCount 1100 -MaximumLines 1000)) 'Oversized legacy shrink must pass'
# Mutation killed: allowing a file that starts within the limit to cross it.
Assert-Equal (Get-NervSourceSizeViolation -Status M -Path 'src/Crosses.cs' -BaseLineCount 999 -HeadLineCount 1001 -MaximumLines 1000).Rule 'file-crosses-limit' 'Threshold crossing must fail'

function Invoke-FixtureGit {
    param(
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Name
    )

    return Invoke-NativeCommandOutput -Command 'git' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Name $Name
}

function Write-FixtureSource {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [ValidateRange(0, 5000)] [int] $LineCount
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($directory)
    }
    $content = if ($LineCount -eq 0) { '' } else { ('governed-line' + "`n") * $LineCount }
    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

function New-SourceSizeGitFixture {
    param(
        [AllowNull()] [Nullable[int]] $BaseLineCount,
        [Parameter(Mandatory)] [int] $HeadLineCount
    )

    $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('nerv-source-size-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($fixtureRoot)
    Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('init', '--quiet') -Name 'source-size-git-init' | Out-Null
    Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('config', 'user.name', 'Nerv Fixture') -Name 'source-size-git-name' | Out-Null
    Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('config', 'user.email', 'fixture@example.invalid') -Name 'source-size-git-email' | Out-Null

    $sourcePath = Join-Path $fixtureRoot 'src/Governed.cs'
    if ($null -ne $BaseLineCount) {
        Write-FixtureSource -Path $sourcePath -LineCount $BaseLineCount
        Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('add', '--', 'src/Governed.cs') -Name 'source-size-git-add' | Out-Null
    }
    else {
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'README.md'), "fixture`n", [Text.UTF8Encoding]::new($false))
        Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('add', '--', 'README.md') -Name 'source-size-git-add-readme' | Out-Null
    }
    Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('commit', '--quiet', '-m', 'base') -Name 'source-size-git-commit' | Out-Null
    $baseCommit = (Invoke-FixtureGit -WorkingDirectory $fixtureRoot -Arguments @('rev-parse', 'HEAD') -Name 'source-size-git-head').Stdout.Trim()
    Write-FixtureSource -Path $sourcePath -LineCount $HeadLineCount

    return [pscustomobject]@{ Root = $fixtureRoot; BaseCommit = $baseCommit }
}

function Invoke-SourceSizeChecker {
    param(
        [Parameter(Mandatory)] [string] $FixtureRoot,
        [Parameter(Mandatory)] [string] $BaseCommit,
        [string[]] $AdditionalArguments = @()
    )

    $entrypointPath = Join-Path $repoRoot 'scripts/check-source-size-governance.ps1'
    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $entrypointPath, '-BaseCommit', $BaseCommit, '-RepositoryRoot', $FixtureRoot) + $AdditionalArguments) `
            -WorkingDirectory $fixtureRoot `
            -Name 'source-size-checker-fixture'
        return [pscustomobject]@{ Passed = $true; Message = [string]$result.Stdout }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = [string]$_.Exception.Message }
    }
}

function New-EmptySourceSizeGitFixture {
    $fixture = New-SourceSizeGitFixture -BaseLineCount $null -HeadLineCount 0
    Remove-Item -LiteralPath (Join-Path $fixture.Root 'src/Governed.cs') -Force
    return $fixture
}

# Mutation killed: broad substring exclusions would incorrectly exempt these ordinary paths.
foreach ($governedPath in @(
    'src/MigrationsSupport.cs',
    'src/vendorized.ts',
    'frontend/packages/api-client/src/business-console.ts',
    'src/Manual.cs'
)) {
    Assert-True (Test-NervGovernedSourcePath -Path $governedPath -GovernedExtension @('.cs', '.ts')) "Ordinary source path must remain governed: $governedPath"
}
# Mutation killed: deleting a precise exclusion would make generated or vendored sources block delivery.
foreach ($excludedPath in @(
    'backend/Data/Migrations/20260825_AddThing.cs',
    'backend/Models/Thing.Designer.cs',
    'backend/Generated/Thing.g.cs',
    'frontend/src/schema.generated.ts',
    'frontend/packages/api-client/src/generated/business.ts',
    'frontend/vendor/library.ts',
    'frontend/node_modules/package/index.js',
    'backend/Project/obj/Debug/Generated.cs'
)) {
    Assert-True (-not (Test-NervGovernedSourcePath -Path $excludedPath -GovernedExtension @('.cs', '.js', '.ts'))) "Generated or vendored source path must be excluded: $excludedPath"
}

function Assert-SourceSizeWorkflowContract {
    param([Parameter(Mandatory)] [string] $WorkflowPath)

    $workflowText = [IO.File]::ReadAllText($WorkflowPath)
    $match = [regex]::Match($workflowText, '(?ms)^  script-governance:\r?\n.*?(?=^  [a-z0-9-]+:\r?\n|\z)')
    Assert-True $match.Success 'CI must define the script-governance job'
    $job = $match.Value

    # Mutation killed: returning checkout to a shallow clone makes the selected base unreadable.
    Assert-True ([regex]::IsMatch($job, '(?ms)- name: Checkout\s+timeout-minutes: 3\s+uses: actions/checkout@v4\s+with:\s+fetch-depth: 0')) 'Script Governance checkout must fetch full history'
    # Mutation killed: combining the policy tests and live checker into one shell line hides ownership and failures.
    Assert-True ([regex]::IsMatch($job, '(?ms)- name: Test source size governance\s+timeout-minutes: 5\s+shell: pwsh\s+run: ./scripts/tests/source-size-governance.Tests.ps1')) 'Script Governance must run source-size contract tests in an independent step'
    Assert-True ([regex]::IsMatch($job, '(?ms)- name: Run source size governance check\s+timeout-minutes: 5\s+shell: pwsh\s+env:\s+BASE_SHA: \$\{\{ github.event_name == ''pull_request'' && github.event.pull_request.base.sha \|\| github.event.before \}\}\s+run: ./scripts/check-source-size-governance.ps1 -BaseCommit \$env:BASE_SHA')) 'Script Governance must run the live checker against the event base in an independent step'
    # Mutation killed: omitting frontend would let a frontend-only oversized source bypass the owner job.
    Assert-True $job.Contains("needs.impact-plan.outputs.frontend != 'false'", [StringComparison]::Ordinal) 'Script Governance routing must include frontend impact'
    Assert-True $job.Contains('33 个 step', [StringComparison]::Ordinal) 'Script Governance budget must record 33 steps'
    Assert-True $job.Contains('3m checkout', [StringComparison]::Ordinal) 'Script Governance budget must retain the checkout component'
    Assert-True $job.Contains('32 × 5m', [StringComparison]::Ordinal) 'Script Governance budget must record 32 five-minute steps'
    Assert-True $job.Contains('163m', [StringComparison]::Ordinal) 'Script Governance budget must record the 163m step ceiling'

    foreach ($stepName in @('Test source size governance', 'Run source size governance check')) {
        $stepMatch = [regex]::Match($job, "(?ms)- name: $([regex]::Escape($stepName)).*?(?=\n      - name:|\z)")
        Assert-True $stepMatch.Success "Workflow step must exist: $stepName"
        Assert-True (-not $stepMatch.Value.Contains('continue-on-error', [StringComparison]::Ordinal)) "Workflow step must not continue on error: $stepName"
        Assert-True (-not $stepMatch.Value.Contains('|| true', [StringComparison]::Ordinal)) "Workflow step must not swallow failure: $stepName"
    }
}

Assert-SourceSizeWorkflowContract -WorkflowPath (Join-Path $repoRoot '.github/workflows/ci.yml')

$fixtures = [Collections.Generic.List[string]]::new()
try {
    $newOversized = New-SourceSizeGitFixture -BaseLineCount $null -HeadLineCount 1001
    $fixtures.Add($newOversized.Root)
    Assert-True (-not (Invoke-SourceSizeChecker -FixtureRoot $newOversized.Root -BaseCommit $newOversized.BaseCommit).Passed) 'A new 1001-line source file must fail'

    $legacyGrowth = New-SourceSizeGitFixture -BaseLineCount 1200 -HeadLineCount 1201
    $fixtures.Add($legacyGrowth.Root)
    Assert-True (-not (Invoke-SourceSizeChecker -FixtureRoot $legacyGrowth.Root -BaseCommit $legacyGrowth.BaseCommit).Passed) 'An oversized legacy source file must not grow'

    $legacyHold = New-SourceSizeGitFixture -BaseLineCount 1200 -HeadLineCount 1200
    $fixtures.Add($legacyHold.Root)
    $legacyHoldResult = Invoke-SourceSizeChecker -FixtureRoot $legacyHold.Root -BaseCommit $legacyHold.BaseCommit
    Assert-True $legacyHoldResult.Passed "An unchanged oversized legacy source file must pass. $($legacyHoldResult.Message)"

    $thresholdCrossing = New-SourceSizeGitFixture -BaseLineCount 999 -HeadLineCount 1001
    $fixtures.Add($thresholdCrossing.Root)
    Assert-True (-not (Invoke-SourceSizeChecker -FixtureRoot $thresholdCrossing.Root -BaseCommit $thresholdCrossing.BaseCommit).Passed) 'A source file must not cross the threshold'

    $excludedSources = New-EmptySourceSizeGitFixture
    $fixtures.Add($excludedSources.Root)
    foreach ($excludedPath in @(
        'backend/Data/Migrations/TooLarge.cs',
        'backend/Generated/TooLarge.Designer.cs',
        'backend/Generated/TooLarge.g.cs',
        'frontend/src/too-large.generated.ts',
        'frontend/packages/api-client/src/generated/too-large.ts',
        'frontend/vendor/too-large.ts'
    )) {
        Write-FixtureSource -Path (Join-Path $excludedSources.Root $excludedPath) -LineCount 1001
    }
    Assert-True (Invoke-SourceSizeChecker -FixtureRoot $excludedSources.Root -BaseCommit $excludedSources.BaseCommit).Passed 'Precisely excluded sources must pass even when oversized'

    $ignoredSource = New-EmptySourceSizeGitFixture
    $fixtures.Add($ignoredSource.Root)
    [IO.File]::WriteAllText((Join-Path $ignoredSource.Root '.gitignore'), "ignored/`n", [Text.UTF8Encoding]::new($false))
    Write-FixtureSource -Path (Join-Path $ignoredSource.Root 'ignored/TooLarge.cs') -LineCount 1001
    Assert-True (Invoke-SourceSizeChecker -FixtureRoot $ignoredSource.Root -BaseCommit $ignoredSource.BaseCommit).Passed 'Ignored untracked sources must not participate'

    $manualHeader = New-EmptySourceSizeGitFixture
    $fixtures.Add($manualHeader.Root)
    $manualPath = Join-Path $manualHeader.Root 'src/Manual.cs'
    Write-FixtureSource -Path $manualPath -LineCount 1001
    $manualText = [IO.File]::ReadAllText($manualPath)
    [IO.File]::WriteAllText($manualPath, "// <auto-generated>`n" + $manualText, [Text.UTF8Encoding]::new($false))
    Assert-True (-not (Invoke-SourceSizeChecker -FixtureRoot $manualHeader.Root -BaseCommit $manualHeader.BaseCommit).Passed) 'A generated header alone must not exempt ordinary source'

    $renameGrowth = New-SourceSizeGitFixture -BaseLineCount 1200 -HeadLineCount 1200
    $fixtures.Add($renameGrowth.Root)
    Invoke-FixtureGit -WorkingDirectory $renameGrowth.Root -Arguments @('mv', 'src/Governed.cs', 'src/Renamed.cs') -Name 'source-size-git-rename' | Out-Null
    Write-FixtureSource -Path (Join-Path $renameGrowth.Root 'src/Renamed.cs') -LineCount 1201
    $renameResult = Invoke-SourceSizeChecker -FixtureRoot $renameGrowth.Root -BaseCommit $renameGrowth.BaseCommit
    Assert-True (-not $renameResult.Passed) 'Rename must retain base identity and reject growth'
    Assert-True $renameResult.Message.Contains('status=R', [StringComparison]::Ordinal) 'Rename violation must report status R'

    $deletedSource = New-SourceSizeGitFixture -BaseLineCount 1200 -HeadLineCount 1200
    $fixtures.Add($deletedSource.Root)
    Remove-Item -LiteralPath (Join-Path $deletedSource.Root 'src/Governed.cs') -Force
    Assert-True (Invoke-SourceSizeChecker -FixtureRoot $deletedSource.Root -BaseCommit $deletedSource.BaseCommit).Passed 'Deleted source files must be ignored'

    $markdownOnly = New-EmptySourceSizeGitFixture
    $fixtures.Add($markdownOnly.Root)
    [IO.File]::AppendAllText((Join-Path $markdownOnly.Root 'README.md'), "documentation only`n", [Text.UTF8Encoding]::new($false))
    Assert-True (Invoke-SourceSizeChecker -FixtureRoot $markdownOnly.Root -BaseCommit $markdownOnly.BaseCommit).Passed 'A legal diff without governed sources must pass'

    $orderedViolations = New-EmptySourceSizeGitFixture
    $fixtures.Add($orderedViolations.Root)
    Write-FixtureSource -Path (Join-Path $orderedViolations.Root 'src/Zed.cs') -LineCount 1001
    Write-FixtureSource -Path (Join-Path $orderedViolations.Root 'src/Alpha.cs') -LineCount 1001
    $orderedResult = Invoke-SourceSizeChecker -FixtureRoot $orderedViolations.Root -BaseCommit $orderedViolations.BaseCommit
    Assert-True (-not $orderedResult.Passed) 'Multiple oversized sources must fail'
    Assert-True ($orderedResult.Message.IndexOf('src/Alpha.cs', [StringComparison]::Ordinal) -lt $orderedResult.Message.IndexOf('src/Zed.cs', [StringComparison]::Ordinal)) 'Violation diagnostics must use ordinal path order'

    $secretFixture = New-EmptySourceSizeGitFixture
    $fixtures.Add($secretFixture.Root)
    Write-FixtureSource -Path (Join-Path $secretFixture.Root 'src/Secret.cs') -LineCount 1001
    [IO.File]::AppendAllText((Join-Path $secretFixture.Root 'src/Secret.cs'), "token=super-secret`n", [Text.UTF8Encoding]::new($false))
    $secretResult = Invoke-SourceSizeChecker -FixtureRoot $secretFixture.Root -BaseCommit $secretFixture.BaseCommit
    Assert-True (-not $secretResult.Passed) 'Oversized secret fixture must fail'
    Assert-True (-not $secretResult.Message.Contains('super-secret', [StringComparison]::Ordinal)) 'Diagnostics must not disclose source content'

    $missingBase = Invoke-SourceSizeChecker -FixtureRoot $markdownOnly.Root -BaseCommit ('0' * 40)
    Assert-True (-not $missingBase.Passed) 'A missing base commit must fail closed'

    $invalidMaximum = Invoke-SourceSizeChecker -FixtureRoot $markdownOnly.Root -BaseCommit $markdownOnly.BaseCommit -AdditionalArguments @('-MaximumLines', '0')
    Assert-True (-not $invalidMaximum.Passed) 'A non-positive maximum must fail closed'

    $emptyExtension = Invoke-SourceSizeChecker -FixtureRoot $markdownOnly.Root -BaseCommit $markdownOnly.BaseCommit -AdditionalArguments @('-GovernedExtension', '')
    Assert-True (-not $emptyExtension.Passed) 'An empty extension configuration must fail closed'
}
finally {
    foreach ($fixture in $fixtures) {
        if (Test-Path -LiteralPath $fixture -PathType Container) {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}

Write-Host 'Source size governance contracts passed.'
