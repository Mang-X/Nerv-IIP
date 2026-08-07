# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs scripts/verify-solution-configuration-membership.ps1 against the repository and against
#       throwaway solution fixtures
#   Writes:
#     - OS temporary directory: solution and project fixtures (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every fixture directory in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-solution-configuration-membership.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-solution-membership-{0}" -f [Guid]::NewGuid().ToString('N'))

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-FixtureProject {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [string[]] $ProjectReferenceInclude = @()
    )

    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $references = @($ProjectReferenceInclude | ForEach-Object { "    <ProjectReference Include=`"$_`" />" }) -join [Environment]::NewLine
    @(
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <ItemGroup>',
        $references,
        '  </ItemGroup>',
        '</Project>'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath $Path -Encoding utf8
}

# Emits a real solution: `Project(...)` declarations *and* the GlobalSection configuration map that
# actually decides each project's Configuration. The two switches below reproduce the only way a
# declared member can still be built as Debug — the form that no `Project(...)`-line rule can see.
function New-FixtureSolution {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $MemberRelativePath,

        # Members whose `Release|Any CPU.ActiveCfg` line is dropped entirely.
        [string[]] $OmitReleaseMapFor = @(),

        # Members whose `Release|Any CPU.ActiveCfg` points at `Debug|Any CPU`.
        [string[]] $InvertReleaseMapFor = @()
    )

    $guidByMember = @{}
    foreach ($member in $MemberRelativePath) {
        $guidByMember[$member] = [Guid]::NewGuid().ToString().ToUpperInvariant()
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('Microsoft Visual Studio Solution File, Format Version 12.00')
    foreach ($member in $MemberRelativePath) {
        $lines.Add(('Project("{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}") = "fixture", "{0}", "{{{1}}}"' -f ($member -replace '/', '\'), $guidByMember[$member]))
        $lines.Add('EndProject')
    }
    $lines.Add('Global')
    $lines.Add("`tGlobalSection(SolutionConfigurationPlatforms) = preSolution")
    $lines.Add("`t`tDebug|Any CPU = Debug|Any CPU")
    $lines.Add("`t`tRelease|Any CPU = Release|Any CPU")
    $lines.Add("`tEndGlobalSection")
    $lines.Add("`tGlobalSection(ProjectConfigurationPlatforms) = postSolution")
    foreach ($member in $MemberRelativePath) {
        $guid = $guidByMember[$member]
        $lines.Add("`t`t{$guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU")
        $lines.Add("`t`t{$guid}.Debug|Any CPU.Build.0 = Debug|Any CPU")
        if ($OmitReleaseMapFor -contains $member) { continue }
        $releaseTarget = if ($InvertReleaseMapFor -contains $member) { 'Debug|Any CPU' } else { 'Release|Any CPU' }
        $lines.Add("`t`t{$guid}.Release|Any CPU.ActiveCfg = $releaseTarget")
        $lines.Add("`t`t{$guid}.Release|Any CPU.Build.0 = $releaseTarget")
    }
    $lines.Add("`tEndGlobalSection")
    $lines.Add('EndGlobal')

    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    ($lines -join [Environment]::NewLine) | Set-Content -LiteralPath $Path -Encoding utf8
}

# Invoked through Invoke-NativeCommandOutput rather than Invoke-PwshScript because the assertions
# below are about *what the verifier said* — which project it names on failure, which solutions it
# reports on success — and only the output-capturing helper surfaces that text. scripts/tests/** is
# outside the governance forbidden-command scan, and this is the same idiom
# scripts/tests/backend-test-shards.Tests.ps1 already uses.
function Invoke-Verifier {
    param(
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    # Whitespace is collapsed because PowerShell's own error formatter hard-wraps the message at the
    # console width, and it wraps *inside* the phrases these assertions look for ("no 'Release|Any\n
    # CPU.ActiveCfg' entry"). Matching raw text would make the assertions depend on terminal width —
    # green on a wide runner, red on a narrow one. The assertions are about content, not layout.
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
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

function Invoke-FixtureVerifier {
    param(
        [Parameter(Mandatory)] [string] $FixtureDirectory,
        [Parameter(Mandatory)] [string] $SolutionRelativePath,
        [Parameter(Mandatory)] [string] $Name
    )

    return Invoke-Verifier -Name $Name -Arguments @(
        '-RepositoryRoot', $FixtureDirectory,
        '-SolutionPath', $SolutionRelativePath
    )
}

try {
    Assert-Contract (Test-Path -LiteralPath $verifierPath -PathType Leaf) 'Solution configuration membership verifier is missing.'

    # 1. The real repository must satisfy the invariant, AND the verifier must actually have looked at
    #    both solutions. This is asserted from the verifier's own *output*, not from its source text:
    #    a source-text assertion (`the file mentions this path`) stays green when the default scope is
    #    narrowed and the path survives in a comment — which is precisely the "gate narrowed back to
    #    backend-only" failure this step exists to prevent. Regression guard for MAN-669 PR-C:
    #    connector-hosts/Nerv.IIP.ConnectorHost.sln reached Nerv.IIP.Sdk.Ops, Nerv.IIP.Contracts.Ops
    #    and Nerv.IIP.Contracts.IntegrationEvents only transitively, so its Release build emitted all
    #    three into bin/Debug.
    $defaultScope = Invoke-Verifier -Name 'solution-configuration-membership'
    Assert-Contract $defaultScope.Passed "The repository must satisfy solution configuration membership; the verifier said: $($defaultScope.Message)"
    foreach ($requiredSolution in @('backend/Nerv.IIP.sln', 'connector-hosts/Nerv.IIP.ConnectorHost.sln')) {
        Assert-Contract ($defaultScope.Message.Contains("${requiredSolution}:")) "The verifier's default scope must report on ${requiredSolution}; it reported: $($defaultScope.Message)"
    }

    # 2. A transitive non-member must fail. `member` references `orphan`, only `member` is listed.
    $leakDirectory = Join-Path $fixtureRoot 'leak'
    New-FixtureProject -Path (Join-Path $leakDirectory 'orphan/Orphan.csproj')
    New-FixtureProject -Path (Join-Path $leakDirectory 'member/Member.csproj') -ProjectReferenceInclude @('..\orphan\Orphan.csproj')
    New-FixtureSolution -Path (Join-Path $leakDirectory 'Fixture.sln') -MemberRelativePath @('member/Member.csproj')
    $leak = Invoke-FixtureVerifier -FixtureDirectory $leakDirectory -SolutionRelativePath 'Fixture.sln' -Name 'solution-membership-leak-fixture'
    Assert-Contract (-not $leak.Passed) 'A transitive ProjectReference outside the solution must fail the check.'
    Assert-Contract ($leak.Message -match 'Orphan\.csproj') 'The failure must name the non-member project.'
    Assert-Contract ($leak.Message -match 'Member\.csproj') 'The failure must name the member that pulled the non-member in.'

    # 3. The same graph passes once the referenced project is a member. Without this the check could
    #    be satisfied by failing on everything.
    New-FixtureSolution -Path (Join-Path $leakDirectory 'Fixed.sln') -MemberRelativePath @('member/Member.csproj', 'orphan/Orphan.csproj')
    $fixed = Invoke-FixtureVerifier -FixtureDirectory $leakDirectory -SolutionRelativePath 'Fixed.sln' -Name 'solution-membership-fixed-fixture'
    Assert-Contract $fixed.Passed "A fully-registered closure must pass; the verifier said: $($fixed.Message)"

    # 4. A *declared* member whose configuration map is missing must fail. This is the second way a
    #    project ends up in bin/Debug under `--configuration Release`, and it is invisible to every
    #    rule that reads only `Project(...)` lines — including PR-B's directory rule. It is one
    #    hand-edit away in practice: fixing form 1 means writing 12 map lines per project by hand
    #    (42 lines for PR-C's three projects), and dropping any of them re-creates the bug silently.
    $mapDirectory = Join-Path $fixtureRoot 'map'
    New-FixtureProject -Path (Join-Path $mapDirectory 'lib/Lib.csproj')
    New-FixtureProject -Path (Join-Path $mapDirectory 'app/App.csproj') -ProjectReferenceInclude @('..\lib\Lib.csproj')
    New-FixtureSolution `
        -Path (Join-Path $mapDirectory 'MissingMap.sln') `
        -MemberRelativePath @('app/App.csproj', 'lib/Lib.csproj') `
        -OmitReleaseMapFor @('lib/Lib.csproj')
    $missingMap = Invoke-FixtureVerifier -FixtureDirectory $mapDirectory -SolutionRelativePath 'MissingMap.sln' -Name 'solution-membership-missing-map-fixture'
    Assert-Contract (-not $missingMap.Passed) 'A declared member with no Release ActiveCfg entry must fail; it would be built as Debug.'
    Assert-Contract ($missingMap.Message -match 'Lib\.csproj') 'The failure must name the member whose configuration map is incomplete.'
    Assert-Contract ($missingMap.Message -match 'Release\|Any CPU') 'The failure must name the solution configuration that has no mapping.'
    Assert-Contract ($missingMap.Message -notmatch 'App\.csproj') 'A fully mapped sibling must not be reported.'

    # 5. A *present but inverted* map entry must fail too: `Release|Any CPU` pointing at
    #    `Debug|Any CPU` produces the identical bin/Debug symptom while every ActiveCfg line exists,
    #    so a check that only counts entries would pass.
    New-FixtureSolution `
        -Path (Join-Path $mapDirectory 'InvertedMap.sln') `
        -MemberRelativePath @('app/App.csproj', 'lib/Lib.csproj') `
        -InvertReleaseMapFor @('lib/Lib.csproj')
    $invertedMap = Invoke-FixtureVerifier -FixtureDirectory $mapDirectory -SolutionRelativePath 'InvertedMap.sln' -Name 'solution-membership-inverted-map-fixture'
    Assert-Contract (-not $invertedMap.Passed) 'A Release solution configuration mapped to a Debug project configuration must fail.'
    Assert-Contract ($invertedMap.Message -match 'Lib\.csproj') 'The inverted-map failure must name the mismapped member.'
    Assert-Contract ($invertedMap.Message -match 'bin/Debug') 'The inverted-map failure must state the bin/Debug consequence.'

    # 6. The same two projects pass with a complete, correct map — so steps 4 and 5 cannot be
    #    satisfied by a verifier that rejects every fixture solution.
    New-FixtureSolution -Path (Join-Path $mapDirectory 'CompleteMap.sln') -MemberRelativePath @('app/App.csproj', 'lib/Lib.csproj')
    $completeMap = Invoke-FixtureVerifier -FixtureDirectory $mapDirectory -SolutionRelativePath 'CompleteMap.sln' -Name 'solution-membership-complete-map-fixture'
    Assert-Contract $completeMap.Passed "A complete configuration map must pass; the verifier said: $($completeMap.Message)"

    # 7. Glob includes are expanded, not treated as a literal path. backend/tests/
    #    Nerv.IIP.MigrationGovernance.Tests uses `..\..\services\**\*.Infrastructure.csproj`; a
    #    verifier that skipped globs would silently stop covering every project reached that way.
    $globDirectory = Join-Path $fixtureRoot 'glob'
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Alpha/Alpha.Infrastructure.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Beta/Nested/Beta.Infrastructure.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Gamma/Gamma.Application.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'tests/Governance/Governance.csproj') -ProjectReferenceInclude @('..\..\services\**\*.Infrastructure.csproj')
    New-FixtureSolution -Path (Join-Path $globDirectory 'Glob.sln') -MemberRelativePath @('tests/Governance/Governance.csproj')
    $glob = Invoke-FixtureVerifier -FixtureDirectory $globDirectory -SolutionRelativePath 'Glob.sln' -Name 'solution-membership-glob-fixture'
    Assert-Contract (-not $glob.Passed) 'A glob ProjectReference must be expanded and its matches checked for membership.'
    Assert-Contract ($glob.Message -match 'Alpha\.Infrastructure\.csproj') 'The glob must match a project one directory below the fixed prefix.'
    Assert-Contract ($glob.Message -match 'Beta\.Infrastructure\.csproj') 'The glob `**` must cross directory separators.'
    Assert-Contract ($glob.Message -notmatch 'Gamma\.Application\.csproj') 'The glob leaf pattern must not match unrelated projects.'
    Assert-Contract ($glob.Message -notmatch '\*') 'The literal glob text must never be reported as a project path.'

    # 8. Discovery must be real: a solution dropped into the tree is checked without being registered
    #    anywhere. This is the property that keeps the gate from acquiring the same "invisible by
    #    construction" blind spot PR-B's directory rule had.
    $discoveryDirectory = Join-Path $fixtureRoot 'discovery'
    New-FixtureProject -Path (Join-Path $discoveryDirectory 'orphan/Orphan.csproj')
    New-FixtureProject -Path (Join-Path $discoveryDirectory 'member/Member.csproj') -ProjectReferenceInclude @('..\orphan\Orphan.csproj')
    New-FixtureSolution -Path (Join-Path $discoveryDirectory 'nested/deep/Undeclared.sln') -MemberRelativePath @('../../member/Member.csproj')
    $discovery = Invoke-Verifier -Name 'solution-membership-discovery-fixture' -Arguments @('-RepositoryRoot', $discoveryDirectory)
    Assert-Contract (-not $discovery.Passed) 'An unregistered solution anywhere in the tree must still be discovered and checked.'
    Assert-Contract ($discovery.Message -match 'Undeclared\.sln') 'The discovered solution must be named in the failure.'

    Write-Host 'Solution configuration membership contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
