# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs scripts/verify-permission-code-producer-consistency.ps1 against throwaway C# fixtures
#       and against the repository's real producers
#   Writes:
#     - OS temporary directory: fixture source files (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes the fixture directory in finally
#   Requires:
#     - PowerShell 7

# What this file is for: the checker reads source text, and a source-text gate is one rename away
# from comparing two empty sets and reporting success. Asserting only "the repository is currently
# clean" would be exactly that — it stays green after the producer class is renamed, after the `All`
# array is emptied, and after the containment comparison itself is deleted. So the assertions below
# are fixture-driven: each one states a shape the checker must reject, and the green cases state the
# shapes it must not reject (a gate that fails on everything is not a gate either).

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-permission-code-producer-consistency.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-permission-producers-{0}" -f [Guid]::NewGuid().ToString('N'))

# Assertions are collected rather than thrown at the first failure. A fail-fast run answers "is it
# red", which is the wrong question when someone mutates the checker to find out how wide its
# defence actually is: it reports one assertion and hides whether the other ten would also have
# caught the mutation. The run still fails — every collected failure is reported and the script
# exits nonzero at the end.
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        $script:Failures.Add($Message)
    }
}

# The IAM producer's real shape, including the second class that also carries `business.*` literals.
# That second class is not decoration: `NervIipSeedRoles` sits in the same file on main, and a
# checker that regexes the whole file would count its codes as seeded. Every fixture keeps it, so
# any test that passes only because of whole-file matching would show up as a red in case 6.
function New-FixtureIamProducer {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $SeededCode,

        # Codes placed in the *other* class in the same file — present in the file, not grantable.
        [string[]] $RoleOnlyCode = @(),

        [string] $PermissionClassName = 'NervIipSeedPermissions',
        [string] $AllMemberName = 'All'
    )

    $seeded = @($SeededCode | ForEach-Object { "        `"$_`"," }) -join [Environment]::NewLine
    $roleOnly = @($RoleOnlyCode | ForEach-Object { "                `"$_`"," }) -join [Environment]::NewLine

    @(
        'namespace Nerv.IIP.Iam.Domain;',
        '',
        'public static class NervIipSeedRoles',
        '{',
        '    public static readonly SeedRoleDefinition[] ErpJobRoles =',
        '    [',
        '        new(',
        '            "role-fixture",',
        '            "Fixture",',
        '            [',
        $roleOnly,
        '            ]),',
        '    ];',
        '}',
        '',
        "public static class $PermissionClassName",
        '{',
        "    public static readonly string[] $AllMemberName =",
        '    [',
        $seeded,
        '    ];',
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath $Path -Encoding utf8
}

function New-FixtureGatewayProducer {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $EnforcedCode,
        [string] $PermissionClassName = 'BusinessGatewayPermissions'
    )

    $index = 0
    $constants = @($EnforcedCode | ForEach-Object {
        $index++
        "    public const string Code$index = `"$_`";"
    }) -join [Environment]::NewLine

    @(
        'namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;',
        '',
        "public static class $PermissionClassName",
        '{',
        $constants,
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Verifier {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [string[]] $Arguments = @()
    )

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

# Every case builds a *complete* pair of producers and varies exactly one thing, so a red is
# attributable. In particular the negative cases keep several shared codes on both sides: if a
# fixture's only enforced code were the missing one, the checker could be reporting "nothing
# matched" rather than "this code is missing", and deleting the containment comparison would still
# look red.
$sharedCodes = @(
    'business.inventory.ledger.read',
    'business.mes.work-orders.read',
    'business.quality.ncr.manage'
)

function Invoke-Case {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $SeededCode,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $EnforcedCode,
        [string[]] $RoleOnlyCode = @(),
        [string] $IamPermissionClassName = 'NervIipSeedPermissions',
        [string] $IamAllMemberName = 'All',
        [string] $GatewayPermissionClassName = 'BusinessGatewayPermissions'
    )

    $caseRoot = Join-Path $fixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $iamPath = Join-Path $caseRoot 'IamFacts.cs'
    $gatewayPath = Join-Path $caseRoot 'BusinessGatewayAuthorization.cs'

    New-FixtureIamProducer -Path $iamPath -SeededCode $SeededCode -RoleOnlyCode $RoleOnlyCode `
        -PermissionClassName $IamPermissionClassName -AllMemberName $IamAllMemberName
    New-FixtureGatewayProducer -Path $gatewayPath -EnforcedCode $EnforcedCode `
        -PermissionClassName $GatewayPermissionClassName

    return Invoke-Verifier -Name "permission-producers-$Name" -Arguments @(
        '-RepositoryRoot', $caseRoot,
        '-IamSeedPermissionsPath', 'IamFacts.cs',
        '-GatewayPermissionsPath', 'BusinessGatewayAuthorization.cs')
}

try {
    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    # --- Case 1: the shape this gate exists for. Gateway enforces a code IAM never seeds. ---
    $case1 = Invoke-Case -Name 'gateway-only-code' `
        -SeededCode $sharedCodes `
        -EnforcedCode ($sharedCodes + 'business.inventory.expired-stock.override')
    Assert-Contract -Condition (-not $case1.Passed) -Message 'A Gateway-only permission code must fail the check.'
    Assert-Contract -Condition ($case1.Message -match 'business\.inventory\.expired-stock\.override') `
        -Message "The failure must name the offending code. Actual: $($case1.Message)"
    # The three shared codes are legal and must not be reported; naming them would mean the checker
    # is reporting the whole enforced set rather than the difference.
    Assert-Contract -Condition ($case1.Message -notmatch 'business\.mes\.work-orders\.read') `
        -Message "Only the missing code may be reported. Actual: $($case1.Message)"

    # --- Case 2: the reverse direction is legal (ADR 0029 实施说明 1 exempts service-only codes). ---
    $case2 = Invoke-Case -Name 'iam-only-code' `
        -SeededCode ($sharedCodes + @('business.iiot.tags.manage', 'business.wms.work-pools.manage')) `
        -EnforcedCode $sharedCodes
    Assert-Contract -Condition $case2.Passed `
        -Message "Codes seeded by IAM but not enforced by Gateway must pass. Actual: $($case2.Message)"

    # --- Case 3: identical sets pass. This is the positive control for case 1: the two fixtures
    # differ by exactly one code, so case 1's red cannot be blamed on the fixture shape. ---
    $case3 = Invoke-Case -Name 'identical-sets' -SeededCode $sharedCodes -EnforcedCode $sharedCodes
    Assert-Contract -Condition $case3.Passed -Message "Identical producer sets must pass. Actual: $($case3.Message)"

    # --- Case 4: the IAM producer class renamed away. An empty grantable set contains nothing, so a
    # naive comparison would report every enforced code — or, if the parse result were treated as
    # "no constraint", would pass silently. Neither is acceptable; the checker must say it cannot
    # read the producer. ---
    $case4 = Invoke-Case -Name 'iam-class-renamed' `
        -SeededCode $sharedCodes -EnforcedCode $sharedCodes -IamPermissionClassName 'NervIipSeedPermissionsV2'
    Assert-Contract -Condition (-not $case4.Passed) -Message 'A renamed IAM producer class must fail the check.'
    Assert-Contract -Condition ($case4.Message -match 'NervIipSeedPermissions') `
        -Message "The failure must name the producer it could not find. Actual: $($case4.Message)"

    # --- Case 5: the class is there but `All` is not. Same disarm, one level down. ---
    $case5 = Invoke-Case -Name 'iam-all-member-renamed' `
        -SeededCode $sharedCodes -EnforcedCode $sharedCodes -IamAllMemberName 'AllPermissions'
    Assert-Contract -Condition (-not $case5.Passed) -Message "A missing 'All' collection initializer must fail the check."

    # --- Case 6: the reverse probe for class-body isolation. The Gateway-only code IS present in the
    # IAM file — in the role-seed class next door — but is not grantable. A checker that regexed the
    # whole file would go green here; that green is the exact failure mode this case forbids. ---
    $case6 = Invoke-Case -Name 'code-only-in-role-class' `
        -SeededCode $sharedCodes `
        -EnforcedCode ($sharedCodes + 'business.inventory.expired-stock.override') `
        -RoleOnlyCode @('business.inventory.expired-stock.override')
    Assert-Contract -Condition (-not $case6.Passed) `
        -Message 'A code present only in the role-seed class is not grantable and must still fail.'
    Assert-Contract -Condition ($case6.Message -match 'business\.inventory\.expired-stock\.override') `
        -Message "The failure must name the offending code. Actual: $($case6.Message)"

    # --- Case 7: an empty enforced set is contained in anything. Vacuous pass, refused. ---
    $case7 = Invoke-Case -Name 'gateway-set-emptied' -SeededCode $sharedCodes -EnforcedCode @()
    Assert-Contract -Condition (-not $case7.Passed) -Message 'An empty Gateway permission set must fail rather than pass vacuously.'

    # --- Case 8: an emptied IAM array. Refused for the same reason, from the other side. ---
    $case8 = Invoke-Case -Name 'iam-set-emptied' -SeededCode @() -EnforcedCode $sharedCodes
    Assert-Contract -Condition (-not $case8.Passed) -Message 'An empty IAM permission set must fail rather than pass vacuously.'

    # --- Case 9: the Gateway producer class renamed away. ---
    $case9 = Invoke-Case -Name 'gateway-class-renamed' `
        -SeededCode $sharedCodes -EnforcedCode $sharedCodes -GatewayPermissionClassName 'BusinessGatewayPermissionsV2'
    Assert-Contract -Condition (-not $case9.Passed) -Message 'A renamed Gateway producer class must fail the check.'
    Assert-Contract -Condition ($case9.Message -match 'BusinessGatewayPermissions') `
        -Message "The failure must name the producer it could not find. Actual: $($case9.Message)"

    # --- Case 10: a producer path that does not exist must fail, not scan nothing quietly. ---
    $case10 = Invoke-Verifier -Name 'permission-producers-missing-file' -Arguments @(
        '-RepositoryRoot', $fixtureRoot,
        '-IamSeedPermissionsPath', 'does-not-exist/IamFacts.cs',
        '-GatewayPermissionsPath', 'does-not-exist/BusinessGatewayAuthorization.cs')
    Assert-Contract -Condition (-not $case10.Passed) -Message 'Missing producer files must fail the check.'

    # --- Case 11: the repository's real producers. This is the regression anchor — it is the
    # assertion that goes red when someone adds a Gateway constant without seeding it in IAM. It is
    # deliberately last: on its own it proves nothing about the checker (cases 4-9 do that), but
    # without it the checker would never be pointed at the files it exists to compare. ---
    $case11 = Invoke-Verifier -Name 'permission-producers-repository'
    Assert-Contract -Condition $case11.Passed `
        -Message "The repository's own permission producers must satisfy Gateway subset-of IAM. Actual: $($case11.Message)"
    Assert-Contract -Condition ($case11.Message -match 'Every enforced code is seeded') `
        -Message "The success output must state the containment conclusion. Actual: $($case11.Message)"

    if ($script:Failures.Count -gt 0) {
        Write-Host "Permission code producer consistency contract tests failed ($($script:Failures.Count) assertions):"
        foreach ($failure in $script:Failures) {
            Write-Host "  $failure"
        }

        exit 1
    }

    Write-Host 'Permission code producer consistency contract tests passed (11 cases).'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
