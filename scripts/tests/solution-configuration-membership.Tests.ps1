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

function New-FixtureSolution {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $MemberRelativePath
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('Microsoft Visual Studio Solution File, Format Version 12.00')
    foreach ($member in $MemberRelativePath) {
        $lines.Add(('Project("{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}") = "fixture", "{0}", "{{{1}}}"' -f ($member -replace '/', '\'), [Guid]::NewGuid().ToString().ToUpperInvariant()))
        $lines.Add('EndProject')
    }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    ($lines -join [Environment]::NewLine) | Set-Content -LiteralPath $Path -Encoding utf8
}

function Test-VerifierOutcome {
    param(
        [Parameter(Mandatory)] [string] $FixtureDirectory,
        [Parameter(Mandatory)] [string] $SolutionRelativePath,
        [Parameter(Mandatory)] [string] $Name
    )

    # Invoked through Invoke-NativeCommandOutput rather than Invoke-PwshScript because the assertions
    # below are about *which* project the verifier names, and only the output-capturing helper
    # surfaces the failure text. scripts/tests/** is outside the governance forbidden-command scan,
    # and this is the same idiom scripts/tests/backend-test-shards.Tests.ps1 already uses.
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-File', $verifierPath, '-RepositoryRoot', $FixtureDirectory, '-SolutionPath', $SolutionRelativePath) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name $Name | Out-Null
        return [pscustomobject]@{ Passed = $true; Message = '' }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = "$($_.Exception.Message)" }
    }
}

try {
    Assert-Contract (Test-Path -LiteralPath $verifierPath -PathType Leaf) 'Solution configuration membership verifier is missing.'

    # 1. The real repository must satisfy the invariant. This is the regression guard for MAN-669
    #    PR-C: connector-hosts/Nerv.IIP.ConnectorHost.sln reached Nerv.IIP.Sdk.Ops,
    #    Nerv.IIP.Contracts.Ops and Nerv.IIP.Contracts.IntegrationEvents only transitively, so its
    #    Release build emitted all three into bin/Debug.
    Invoke-PwshScript -ScriptPath $verifierPath -WorkingDirectory $repoRoot -Name 'solution-configuration-membership' | Out-Null

    # 2. Both solutions must stay in the checked set. A gate narrowed back to the backend solution
    #    would go green while the connector-host leak returned, which is exactly how PR-B's
    #    backend-only directory rule missed it.
    $verifierText = Get-Content -LiteralPath $verifierPath -Raw
    foreach ($requiredSolution in @('backend/Nerv.IIP.sln', 'connector-hosts/Nerv.IIP.ConnectorHost.sln')) {
        Assert-Contract ($verifierText -match [regex]::Escape("'$requiredSolution'")) "The verifier must check $requiredSolution by default."
    }

    # 3. A transitive non-member must fail. `member` references `orphan`, only `member` is listed.
    $leakDirectory = Join-Path $fixtureRoot 'leak'
    New-FixtureProject -Path (Join-Path $leakDirectory 'orphan/Orphan.csproj')
    New-FixtureProject -Path (Join-Path $leakDirectory 'member/Member.csproj') -ProjectReferenceInclude @('..\orphan\Orphan.csproj')
    New-FixtureSolution -Path (Join-Path $leakDirectory 'Fixture.sln') -MemberRelativePath @('member/Member.csproj')
    $leak = Test-VerifierOutcome -FixtureDirectory $leakDirectory -SolutionRelativePath 'Fixture.sln' -Name 'solution-membership-leak-fixture'
    Assert-Contract (-not $leak.Passed) 'A transitive ProjectReference outside the solution must fail the check.'
    Assert-Contract ($leak.Message -match 'Orphan\.csproj') 'The failure must name the non-member project.'
    Assert-Contract ($leak.Message -match 'Member\.csproj') 'The failure must name the member that pulled the non-member in.'

    # 4. The same graph passes once the referenced project is a member. Without this the check could
    #    be satisfied by failing on everything.
    New-FixtureSolution -Path (Join-Path $leakDirectory 'Fixed.sln') -MemberRelativePath @('member/Member.csproj', 'orphan/Orphan.csproj')
    $fixed = Test-VerifierOutcome -FixtureDirectory $leakDirectory -SolutionRelativePath 'Fixed.sln' -Name 'solution-membership-fixed-fixture'
    Assert-Contract $fixed.Passed "A fully-registered closure must pass; the verifier said: $($fixed.Message)"

    # 5. Glob includes are expanded, not treated as a literal path. backend/tests/
    #    Nerv.IIP.MigrationGovernance.Tests uses `..\..\services\**\*.Infrastructure.csproj`; a
    #    verifier that skipped globs would silently stop covering every project reached that way.
    $globDirectory = Join-Path $fixtureRoot 'glob'
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Alpha/Alpha.Infrastructure.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Beta/Nested/Beta.Infrastructure.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'services/Gamma/Gamma.Application.csproj')
    New-FixtureProject -Path (Join-Path $globDirectory 'tests/Governance/Governance.csproj') -ProjectReferenceInclude @('..\..\services\**\*.Infrastructure.csproj')
    New-FixtureSolution -Path (Join-Path $globDirectory 'Glob.sln') -MemberRelativePath @('tests/Governance/Governance.csproj')
    $glob = Test-VerifierOutcome -FixtureDirectory $globDirectory -SolutionRelativePath 'Glob.sln' -Name 'solution-membership-glob-fixture'
    Assert-Contract (-not $glob.Passed) 'A glob ProjectReference must be expanded and its matches checked for membership.'
    Assert-Contract ($glob.Message -match 'Alpha\.Infrastructure\.csproj') 'The glob must match a project one directory below the fixed prefix.'
    Assert-Contract ($glob.Message -match 'Beta\.Infrastructure\.csproj') 'The glob `**` must cross directory separators.'
    Assert-Contract ($glob.Message -notmatch 'Gamma\.Application\.csproj') 'The glob leaf pattern must not match unrelated projects.'
    Assert-Contract ($glob.Message -notmatch '\*') 'The literal glob text must never be reported as a project path.'

    Write-Host 'Solution configuration membership contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
