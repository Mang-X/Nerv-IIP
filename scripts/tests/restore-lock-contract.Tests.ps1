# Script-Governance:
#   Category: test
#   SideEffects:
#     - Runs scripts/verify-restore-lock-contract.ps1 against throwaway fixture repositories and
#       against the repository's real restore manifest
#   Writes:
#     - OS temporary directory: fixture manifests, .csproj and packages.lock.json files (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes the fixture directory in finally
#     - Leaves artifacts/script-logs/** to the repository's existing artifact hygiene
#   Requires:
#     - PowerShell 7

# What this file is for. Asserting only "main is currently clean" would be worth nothing here: the
# repository was clean by that standard for the entire time the MediatR fork sat in the
# BusinessGateway lock, because nothing read the file. So every case below states a shape the
# checker must reject, and the green cases state shapes it must not reject — a gate that fails on
# everything is not a gate either.
#
# The exemption table gets three cases rather than one, because it is the part of this design most
# likely to rot into an escape hatch: case 8 proves a registration silences its own tuple, case 9
# proves that moving one field of the tuple stops the registration from applying, and case 10 proves
# a registration that no longer matches anything is itself a failure.

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-restore-lock-contract.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-restore-lock-{0}" -f [Guid]::NewGuid().ToString('N'))

# Assertions are collected rather than thrown at the first failure. A fail-fast run answers "is it
# red", which is the wrong question when someone mutates the checker to find out how wide its
# defence actually is: it reports one assertion and hides whether the other twenty would also have
# caught the mutation. The run still fails — every collected failure is reported and the script
# exits nonzero at the end.
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message)

    if (-not $Condition) {
        $script:Failures.Add($Message)
    }
}

function Write-FixtureFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Content)

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not (Test-Path -LiteralPath $directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $Content -Encoding utf8 -NoNewline
}

function New-FixtureProject {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [string[]] $ProjectReference = @())

    $references = @($ProjectReference | ForEach-Object { "    <ProjectReference Include=`"$_`" />" }) -join [Environment]::NewLine
    $body = @(
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <PropertyGroup>',
        '    <TargetFramework>net10.0</TargetFramework>',
        '  </PropertyGroup>',
        '  <ItemGroup>',
        $references,
        '  </ItemGroup>',
        '</Project>'
    ) -join [Environment]::NewLine

    Write-FixtureFile -Path $Path -Content $body
}

# Builds a lock whose dependency block is supplied by the caller, so a case can state exactly the
# requested/resolved pair it is about.
function New-FixtureLock {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [hashtable] $Dependency = @{})

    $target = [ordered]@{}
    foreach ($name in @($Dependency.Keys)) {
        $target[$name] = $Dependency[$name]
    }

    $document = [ordered]@{
        version      = 2
        dependencies = [ordered]@{ 'net10.0' = $target }
    }

    Write-FixtureFile -Path $Path -Content (ConvertTo-Json $document -Depth 8)
}

function Get-FixtureHash {
    param([Parameter(Mandatory)] [string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# The baseline fixture: a seed project referencing one library, each with a lock, and a manifest that
# pins every one of those files plus both .csproj files. Every negative case starts from this exact
# shape and varies one thing, so a red is attributable to that one thing.
function New-FixtureRepository {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [hashtable] $SeedDependency = @{},
        [hashtable] $LibraryDependency = @{},
        [object[]] $Exemption = @(),
        [string[]] $OmitFromInputs = @(),
        [string[]] $OmitFromLockPaths = @(),
        [string[]] $ExtraLockPaths = @(),
        [string[]] $SeedExtraReference = @(),
        [string] $DuplicateInput = '',
        [switch] $EmptyInputs,
        [switch] $EmptyLockPaths,
        [switch] $OmitExemptionFile)

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null

    $seedProject = 'seed/Seed.csproj'
    $libraryProject = 'lib/Lib.csproj'
    $seedLock = 'seed/packages.lock.json'
    $libraryLock = 'lib/packages.lock.json'

    New-FixtureProject -Path (Join-Path $Root $seedProject) -ProjectReference (@('..\lib\Lib.csproj') + $SeedExtraReference)
    New-FixtureProject -Path (Join-Path $Root $libraryProject)

    $seedDependencies = if ($SeedDependency.Count -gt 0) { $SeedDependency } else {
        @{ 'Contoso.Widgets' = [ordered]@{ type = 'Direct'; requested = '[3.1.0, )'; resolved = '3.1.0' } }
    }

    $libraryDependencies = if ($LibraryDependency.Count -gt 0) { $LibraryDependency } else {
        @{ 'Contoso.Core' = [ordered]@{ type = 'Direct'; requested = '[2.0.0, )'; resolved = '2.0.4' } }
    }

    New-FixtureLock -Path (Join-Path $Root $seedLock) -Dependency $seedDependencies
    New-FixtureLock -Path (Join-Path $Root $libraryLock) -Dependency $libraryDependencies

    $inputCandidates = @($seedProject, $libraryProject, $seedLock, $libraryLock)
    $omitted = [System.Collections.Generic.HashSet[string]]::new([string[]] $OmitFromInputs, [System.StringComparer]::Ordinal)

    $inputs = [System.Collections.Generic.List[object]]::new()
    if (-not $EmptyInputs) {
        foreach ($candidate in $inputCandidates) {
            if ($omitted.Contains($candidate)) { continue }
            $inputs.Add([ordered]@{ path = $candidate; sha256 = (Get-FixtureHash -Path (Join-Path $Root $candidate)) })
        }

        if (-not [string]::IsNullOrEmpty($DuplicateInput)) {
            $inputs.Add([ordered]@{ path = $DuplicateInput; sha256 = (Get-FixtureHash -Path (Join-Path $Root $DuplicateInput)) })
        }
    }

    $omittedLocks = [System.Collections.Generic.HashSet[string]]::new([string[]] $OmitFromLockPaths, [System.StringComparer]::Ordinal)
    $lockPaths = [System.Collections.Generic.List[string]]::new()
    if (-not $EmptyLockPaths) {
        foreach ($candidate in @($seedLock, $libraryLock)) {
            if ($omittedLocks.Contains($candidate)) { continue }
            $lockPaths.Add($candidate)
        }

        foreach ($extra in $ExtraLockPaths) { $lockPaths.Add($extra) }
    }

    $manifest = [ordered]@{
        schema  = 1
        project = $seedProject
        inputs  = @($inputs)
        lock    = [ordered]@{ paths = @($lockPaths) }
    }

    Write-FixtureFile -Path (Join-Path $Root 'manifest.json') -Content (ConvertTo-Json $manifest -Depth 8)

    if (-not $OmitExemptionFile) {
        Write-FixtureFile -Path (Join-Path $Root 'exemptions.json') `
            -Content (ConvertTo-Json ([ordered]@{ schema = 1; exemptions = @($Exemption) }) -Depth 8)
    }
}

function Invoke-Verifier {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [string[]] $Arguments = @())

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $verifierPath) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 300 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        $stdout = [string] $_.Exception.Data['Stdout']
        $stderr = [string] $_.Exception.Data['Stderr']
        return [pscustomobject]@{ Passed = $false; Message = ("$stdout $stderr $($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

function Invoke-Case {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [hashtable] $Fixture = @{},
        [scriptblock] $Mutate = $null)

    $caseRoot = Join-Path $fixtureRoot $Name
    New-FixtureRepository -Root $caseRoot @Fixture
    if ($null -ne $Mutate) { & $Mutate $caseRoot }

    return Invoke-Verifier -Name "restore-lock-$Name" -Arguments @(
        '-RepositoryRoot', $caseRoot,
        '-ManifestPath', 'manifest.json',
        '-ExemptionPath', 'exemptions.json')
}

$forkDependency = @{
    'Contoso.Widgets' = [ordered]@{ type = 'CentralTransitive'; requested = '[3.1.0, )'; resolved = '3.0.9' }
}

$forkExemption = [ordered]@{
    lockPath  = 'seed/packages.lock.json'
    package   = 'Contoso.Widgets'
    requested = '[3.1.0, )'
    resolved  = '3.0.9'
    issue     = '#3145'
    reason    = 'fixture'
}

try {
    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    # --- Case 1: the positive control. Every negative case below differs from this by one thing, so
    # if this case were red the others would prove nothing. ---
    $case1 = Invoke-Case -Name 'baseline-clean'
    Assert-Contract -Condition $case1.Passed -Message "A consistent fixture must pass. Actual: $($case1.Message)"

    # --- Case 2 (class 4): a pinned input edited without the manifest being updated. ---
    $case2 = Invoke-Case -Name 'csproj-drift' -Mutate {
        param($root)
        New-FixtureProject -Path (Join-Path $root 'lib/Lib.csproj') -ProjectReference @()
        Add-Content -LiteralPath (Join-Path $root 'lib/Lib.csproj') -Value '<!-- edited after the manifest was written -->'
    }
    Assert-Contract -Condition (-not $case2.Passed) -Message 'An edited .csproj that the manifest still pins by its old hash must fail.'
    Assert-Contract -Condition ($case2.Message.Contains('has drifted from the manifest', [StringComparison]::Ordinal)) `
        -Message "The failure must say the input drifted. Actual: $($case2.Message)"
    Assert-Contract -Condition ($case2.Message.Contains('lib/Lib.csproj', [StringComparison]::Ordinal)) `
        -Message "The failure must name the drifted file. Actual: $($case2.Message)"

    # --- Case 3 (class 2): a tampered lock. Same mechanism as case 2, asserted separately because
    # the lock face is the one the ticket exists for and must not be able to regress on its own. ---
    $case3 = Invoke-Case -Name 'lock-tampered' -Mutate {
        param($root)
        New-FixtureLock -Path (Join-Path $root 'lib/packages.lock.json') `
            -Dependency @{ 'Contoso.Core' = [ordered]@{ type = 'Direct'; requested = '[2.0.0, )'; resolved = '2.0.5' } }
    }
    Assert-Contract -Condition (-not $case3.Passed) -Message 'A tampered lock must fail the check.'
    Assert-Contract -Condition ($case3.Message.Contains('lib/packages.lock.json', [StringComparison]::Ordinal)) `
        -Message "The failure must name the tampered lock. Actual: $($case3.Message)"

    # --- Case 4 (class 3): a project joins the ProjectReference closure with no registered lock.
    # This is exactly what happened to Contracts.IndustrialTelemetry in 33c792c04. ---
    $case4 = Invoke-Case -Name 'closure-member-without-lock' -Fixture @{ SeedExtraReference = @('..\extra\Extra.csproj') } -Mutate {
        param($root)
        New-FixtureProject -Path (Join-Path $root 'extra/Extra.csproj')
    }
    Assert-Contract -Condition (-not $case4.Passed) -Message 'A closure member with no registered lock must fail.'
    Assert-Contract -Condition ($case4.Message.Contains('extra/packages.lock.json', [StringComparison]::Ordinal)) `
        -Message "The failure must name the missing lock path. Actual: $($case4.Message)"
    Assert-Contract -Condition ($case4.Message.Contains('is not registered in', [StringComparison]::Ordinal)) `
        -Message "The failure must say the lock is unregistered. Actual: $($case4.Message)"

    # --- Case 5 (class 3): a registered lock that is gone from disk. ---
    $case5 = Invoke-Case -Name 'registered-lock-deleted' -Mutate {
        param($root)
        Remove-Item -LiteralPath (Join-Path $root 'lib/packages.lock.json') -Force
    }
    Assert-Contract -Condition (-not $case5.Passed) -Message 'A registered lock missing from disk must fail.'
    Assert-Contract -Condition ($case5.Message.Contains('but that file does not exist', [StringComparison]::Ordinal)) `
        -Message "The failure must say the file is missing. Actual: $($case5.Message)"

    # --- Case 6: the widened contract. A closure member's .csproj that the manifest does not pin
    # could gain a PackageReference without its lock being updated, and nothing would report it.
    # This case is what makes the widening from 4 pinned source files to 4 + every closure .csproj
    # load-bearing rather than decorative. ---
    $case6 = Invoke-Case -Name 'closure-csproj-not-pinned' -Fixture @{ OmitFromInputs = @('lib/Lib.csproj') }
    Assert-Contract -Condition (-not $case6.Passed) -Message "A closure member's .csproj that is not pinned by the manifest must fail."
    Assert-Contract -Condition ($case6.Message.Contains('is in the ProjectReference closure but is not listed in the', [StringComparison]::Ordinal)) `
        -Message "The failure must say the project file is unpinned. Actual: $($case6.Message)"

    # --- Case 7 (class 1): the #3136 shape. A resolved version below its own requested lower bound,
    # with no registration covering it. ---
    $case7 = Invoke-Case -Name 'fork-unregistered' -Fixture @{ SeedDependency = $forkDependency }
    Assert-Contract -Condition (-not $case7.Passed) -Message 'An unregistered requested/resolved fork must fail.'
    Assert-Contract -Condition ($case7.Message.Contains('below the requested lower bound', [StringComparison]::Ordinal)) `
        -Message "The failure must state the fork. Actual: $($case7.Message)"
    Assert-Contract -Condition ($case7.Message.Contains('Contoso.Widgets', [StringComparison]::Ordinal)) `
        -Message "The failure must name the forked package. Actual: $($case7.Message)"

    # --- Case 8: the same fork, registered. This is the positive control for cases 9 and 10: the
    # table has to be able to silence a tuple, or "changing the table makes it red" would be
    # meaningless. ---
    $case8 = Invoke-Case -Name 'fork-registered' -Fixture @{ SeedDependency = $forkDependency; Exemption = @($forkExemption) }
    Assert-Contract -Condition $case8.Passed -Message "A registered fork must pass. Actual: $($case8.Message)"

    # --- Case 9: one field of the tuple moved. The registration was approved for 3.0.9; the lock now
    # resolves 3.0.8. If the table matched on package id alone, this would stay green — which is the
    # exact way an exemption starts covering forks nobody approved. ---
    $shiftedFork = @{
        'Contoso.Widgets' = [ordered]@{ type = 'CentralTransitive'; requested = '[3.1.0, )'; resolved = '3.0.8' }
    }
    $case9 = Invoke-Case -Name 'fork-tuple-moved' -Fixture @{ SeedDependency = $shiftedFork; Exemption = @($forkExemption) }
    Assert-Contract -Condition (-not $case9.Passed) -Message 'A fork whose resolved version moved off the registered tuple must fail.'
    Assert-Contract -Condition ($case9.Message.Contains('below the requested lower bound', [StringComparison]::Ordinal)) `
        -Message "The moved fork must be reported as a fork. Actual: $($case9.Message)"
    Assert-Contract -Condition ($case9.Message.Contains('matches nothing', [StringComparison]::Ordinal)) `
        -Message "The now-unmatched registration must also be reported. Actual: $($case9.Message)"

    # --- Case 10: the reverse check on its own. The fork is gone; the registration is not. Without
    # this the table accumulates entries that outlive their defects and then cover the next one. ---
    $case10 = Invoke-Case -Name 'exemption-stale' -Fixture @{ Exemption = @($forkExemption) }
    Assert-Contract -Condition (-not $case10.Passed) -Message 'A registration that matches no live fork must fail.'
    Assert-Contract -Condition ($case10.Message.Contains('matches nothing', [StringComparison]::Ordinal)) `
        -Message "The failure must say the registration matches nothing. Actual: $($case10.Message)"

    # --- Case 11: a registration without an issue reference. ---
    $noIssue = [ordered]@{
        lockPath = 'seed/packages.lock.json'; package = 'Contoso.Widgets'
        requested = '[3.1.0, )'; resolved = '3.0.9'; issue = ''; reason = 'fixture'
    }
    $case11 = Invoke-Case -Name 'exemption-without-issue' -Fixture @{ SeedDependency = $forkDependency; Exemption = @($noIssue) }
    Assert-Contract -Condition (-not $case11.Passed) -Message 'A registration with no issue reference must fail.'
    Assert-Contract -Condition ($case11.Message.Contains("missing the required 'issue' field", [StringComparison]::Ordinal)) `
        -Message "The failure must name the missing field. Actual: $($case11.Message)"

    # --- Case 12: an issue reference that is not a '#<number>'. A free-text field would let 'TODO'
    # stand in for a tracking ticket. ---
    $badIssue = [ordered]@{
        lockPath = 'seed/packages.lock.json'; package = 'Contoso.Widgets'
        requested = '[3.1.0, )'; resolved = '3.0.9'; issue = 'TODO'; reason = 'fixture'
    }
    $case12 = Invoke-Case -Name 'exemption-issue-not-a-reference' -Fixture @{ SeedDependency = $forkDependency; Exemption = @($badIssue) }
    Assert-Contract -Condition (-not $case12.Passed) -Message "A registration whose issue is not '#<number>' must fail."
    Assert-Contract -Condition ($case12.Message.Contains("is not a '#<number>' reference", [StringComparison]::Ordinal)) `
        -Message "The failure must explain the required shape. Actual: $($case12.Message)"

    # --- Case 13: an empty ledger hash-matches every one of zero files. Refused. ---
    $case13 = Invoke-Case -Name 'manifest-inputs-emptied' -Fixture @{ EmptyInputs = $true }
    Assert-Contract -Condition (-not $case13.Passed) -Message 'An empty inputs ledger must fail rather than pass vacuously.'
    Assert-Contract -Condition ($case13.Message.Contains('hash-matches vacuously', [StringComparison]::Ordinal)) `
        -Message "The failure must name the vacuous pass. Actual: $($case13.Message)"

    # --- Case 14: an emptied lock set. The closure comparison would otherwise compare against
    # nothing. Note this is partly redundant with case 4 — with lock.paths empty, every closure
    # member is also unregistered — and that redundancy is deliberate: two independent reasons to
    # fail is what stops one edit from going quiet. ---
    $case14 = Invoke-Case -Name 'manifest-lock-paths-emptied' -Fixture @{ EmptyLockPaths = $true }
    Assert-Contract -Condition (-not $case14.Passed) -Message 'An empty lock.paths must fail rather than pass vacuously.'
    Assert-Contract -Condition ($case14.Message.Contains('makes the closure comparison vacuous', [StringComparison]::Ordinal)) `
        -Message "The failure must name the vacuous comparison. Actual: $($case14.Message)"

    # --- Case 15: a lock registered that no closure member corresponds to. A stale registration
    # keeps a file under contract after it left the closure, and reads as coverage that is not there. ---
    $case15 = Invoke-Case -Name 'lock-path-outside-closure' -Fixture @{ ExtraLockPaths = @('ghost/packages.lock.json') }
    Assert-Contract -Condition (-not $case15.Passed) -Message 'A registered lock outside the closure must fail.'
    Assert-Contract -Condition ($case15.Message.Contains('which no project in the ProjectReference', [StringComparison]::Ordinal)) `
        -Message "The failure must say the registration is outside the closure. Actual: $($case15.Message)"

    # --- Case 16: no lock entry carries both a requested range and a resolved version, so the fork
    # comparison ran over zero entries. That is a pass by absence of data, refused. ---
    $projectOnly = @{ 'lib' = [ordered]@{ type = 'Project' } }
    $case16 = Invoke-Case -Name 'no-versioned-entries' -Fixture @{ SeedDependency = $projectOnly; LibraryDependency = $projectOnly }
    Assert-Contract -Condition (-not $case16.Passed) -Message 'A run whose fork check inspected zero entries must fail.'
    Assert-Contract -Condition ($case16.Message.Contains('inspected nothing', [StringComparison]::Ordinal)) `
        -Message "The failure must say the fork check inspected nothing. Actual: $($case16.Message)"

    # --- Case 17: a duplicated input entry lets one copy be refreshed while a stale copy keeps
    # passing, so the ledger would disagree with itself and still be green. ---
    $case17 = Invoke-Case -Name 'duplicate-input-entry' -Fixture @{ DuplicateInput = 'lib/Lib.csproj' }
    Assert-Contract -Condition (-not $case17.Passed) -Message 'A duplicated manifest input must fail.'
    Assert-Contract -Condition ($case17.Message.Contains('more than once', [StringComparison]::Ordinal)) `
        -Message "The failure must name the duplication. Actual: $($case17.Message)"

    # --- Case 18: the exemption table deleted. Without this, removing the file would turn every
    # registered fork into an unreported one and the run would still be green. ---
    $case18 = Invoke-Case -Name 'exemption-file-deleted' -Fixture @{ OmitExemptionFile = $true }
    Assert-Contract -Condition (-not $case18.Passed) -Message 'A missing exemption table must fail.'
    Assert-Contract -Condition ($case18.Message.Contains('Exemption table does not exist', [StringComparison]::Ordinal)) `
        -Message "The failure must name the missing table. Actual: $($case18.Message)"

    # --- Case 19: a manifest path that does not exist must fail, not scan nothing quietly. ---
    $case19 = Invoke-Verifier -Name 'restore-lock-missing-manifest' -Arguments @(
        '-RepositoryRoot', $fixtureRoot,
        '-ManifestPath', 'does-not-exist/manifest.json',
        '-ExemptionPath', 'does-not-exist/exemptions.json')
    Assert-Contract -Condition (-not $case19.Passed) -Message 'A missing manifest must fail the check.'
    Assert-Contract -Condition ($case19.Message.Contains('Restore manifest does not exist', [StringComparison]::Ordinal)) `
        -Message "The failure must name the missing manifest. Actual: $($case19.Message)"

    # --- Case 20: the repository's own contract. This is the regression anchor — the assertion that
    # goes red when someone adds a ProjectReference without registering its lock, or edits a pinned
    # input without updating the manifest. On its own it proves nothing about the checker (cases 2-19
    # do that), but without it the checker would never be pointed at the files it exists to compare. ---
    $case20 = Invoke-Verifier -Name 'restore-lock-repository'
    Assert-Contract -Condition $case20.Passed `
        -Message "The repository's own restore lock contract must hold. Actual: $($case20.Message)"
    Assert-Contract -Condition ($case20.Message.Contains('all hash-matched', [StringComparison]::Ordinal)) `
        -Message "The success output must state the hash conclusion. Actual: $($case20.Message)"
    Assert-Contract -Condition ($case20.Message.Contains('each with a registered lock', [StringComparison]::Ordinal)) `
        -Message "The success output must state the closure conclusion. Actual: $($case20.Message)"

    if ($script:Failures.Count -gt 0) {
        Write-Host "Restore lock contract tests failed ($($script:Failures.Count) assertions):"
        foreach ($failure in $script:Failures) {
            Write-Host "  $failure"
        }

        exit 1
    }

    Write-Host 'Restore lock contract tests passed (20 cases).'
    # Explicit, because the success path would otherwise inherit the exit code of whatever native
    # command ran last. That is green today only by accident of case 20 succeeding; append a case
    # that expects a failure after it and this script would report red while every assertion passed.
    exit 0
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
