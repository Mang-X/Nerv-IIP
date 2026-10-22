# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs scripts/verify-stable-code-frontend-vocabulary.ps1 against throwaway C#/TypeScript
#       fixtures and against the repository's real producers and vocabularies
#   Writes:
#     - OS temporary directory: fixture source files (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes the fixture directory in finally
#     - Leaves artifacts/script-logs/** to the repository's existing artifact hygiene
#   Requires:
#     - PowerShell 7

# What this file is for: the checker reads source text on both sides, and a source-text gate is one
# rename away from comparing two empty sets and reporting success. Asserting only "the repository is
# currently clean" would be exactly that — it stays green after the registry class is renamed, after
# the registry is emptied, after the frontend table is emptied, and after the containment comparison
# itself is deleted. So the assertions below are fixture-driven: each states a shape the checker must
# reject, and the green cases state the shapes it must not reject (a gate that fails on everything is
# not a gate either).

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-stable-code-frontend-vocabulary.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-stable-code-vocab-{0}" -f [Guid]::NewGuid().ToString('N'))

# Assertions are collected rather than thrown at the first failure. A fail-fast run answers "is it
# red", which is the wrong question when someone mutates the checker to find out how wide its defence
# actually is: it reports one assertion and hides whether the other ten would also have caught the
# mutation. The run still fails — every collected failure is reported and the script exits nonzero.
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

# Every fixture carries a *neighbouring* class holding its own `public const string`. That class is
# not decoration: `MesReadinessReasonTexts` sits in the same file as the real readiness registry on
# main, and a checker that regexed the whole file would fold its members into the checked set. Case
# "neighbour-class-ignored" is the probe that would go red if whole-file matching crept back in.
function New-FixtureBackend {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [AllowEmptyCollection()] [string[]] $SafeCode = @(),
        [AllowEmptyCollection()] [string[]] $RegistryCode = @(),
        [AllowEmptyCollection()] [string[]] $ReadinessCode = @(),
        [AllowEmptyCollection()] [string[]] $ReadinessPrivateCode = @(),
        [string] $ReadinessReference,
        [string] $ReferencedLiteral,
        [string] $ReadinessClassName = 'MesReadinessReasonCodes',
        [string] $RegistryClassName = 'FixtureStableWireCodes',
        [string] $NeighbourCode = 'neighbour-class-code-never-checked'
    )

    $srcDir = Join-Path $Root 'service/src'
    [System.IO.Directory]::CreateDirectory($srcDir) | Out-Null

    $index = 0
    $safeCodeClasses = @($SafeCode | ForEach-Object {
        $index++
        @(
            "public sealed class FixtureException$index : Exception",
            '{',
            "    public const string SafeCode = `"$_`";",
            '}'
        ) -join [Environment]::NewLine
    })

    $index = 0
    $registryMembers = @($RegistryCode | ForEach-Object {
        $index++
        "    public const string Code$index = `"$_`";"
    })

    @(
        'namespace Fixture.Service;',
        '',
        ($safeCodeClasses -join [Environment]::NewLine),
        '',
        "public static class $RegistryClassName",
        '{',
        ($registryMembers -join [Environment]::NewLine),
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $srcDir 'ChannelA.cs') -Encoding utf8

    $index = 0
    $readinessMembers = @($ReadinessCode | ForEach-Object {
        $index++
        "    public const string Reason$index = `"$_`";"
    })
    $index = 0
    $readinessPrivate = @($ReadinessPrivateCode | ForEach-Object {
        $index++
        "    private const string Private$index = `"$_`";"
    })
    if ($PSBoundParameters.ContainsKey('ReadinessReference')) {
        $readinessMembers += "    public const string Referenced = $ReadinessReference;"
    }

    @(
        'namespace Fixture.Service;',
        '',
        "public static class $ReadinessClassName",
        '{',
        ($readinessMembers -join [Environment]::NewLine),
        ($readinessPrivate -join [Environment]::NewLine),
        '}',
        '',
        'public static class FixtureNeighbourTexts',
        '{',
        "    public const string Sentence = `"$NeighbourCode`";",
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $srcDir 'ChannelB.cs') -Encoding utf8

    if ($PSBoundParameters.ContainsKey('ReferencedLiteral')) {
        @(
            'namespace Fixture.Contracts;',
            '',
            'public static class FixtureSharedReasonCodes',
            '{',
            "    public const string Shared = `"$ReferencedLiteral`";",
            '}'
        ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $srcDir 'Shared.cs') -Encoding utf8
    }
}

function New-FixtureFrontend {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [AllowEmptyCollection()] [string[]] $StableErrorKey = @(),
        [AllowEmptyCollection()] [string[]] $ReadinessKey = @(),
        [string] $StableErrorConstName = 'STABLE_ERROR_MESSAGES',
        [string] $ReadinessConstName = 'MES_READINESS_REASON_DISPLAYS',
        [string] $CommentedOutKey
    )

    $dir = Join-Path $Root 'frontend'
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null

    $stableEntries = @($StableErrorKey | ForEach-Object { "  '$_': '固定中文文案。'," })
    if ($PSBoundParameters.ContainsKey('CommentedOutKey')) {
        # A key that exists only inside a comment must not count as registered.
        $stableEntries = @("  // '$CommentedOutKey': '这条被注释掉了，不算登记。',") + $stableEntries
    }

    @(
        "export const ${StableErrorConstName}: Readonly<Record<string, string>> = {",
        ($stableEntries -join [Environment]::NewLine),
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $dir 'stable.ts') -Encoding utf8

    # Nested records on purpose: the inner keys (`code`, `category`, `label`, `nextStep`) are not
    # codes, and a depth-blind parser would count them as registered vocabulary.
    $readinessEntries = @($ReadinessKey | ForEach-Object {
        @(
            "  '$_': {",
            "    code: '$_',",
            "    category: '其他门禁',",
            "    label: '固定标签',",
            "    nextStep: '固定下一步',",
            '  },'
        ) -join [Environment]::NewLine
    })

    @(
        "export const ${ReadinessConstName}: Readonly<Record<string, KnownDisplay>> = {",
        ($readinessEntries -join [Environment]::NewLine),
        '}'
    ) -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $dir 'readiness.ts') -Encoding utf8
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
            -TimeoutSeconds 600 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        $stdout = [string] $_.Exception.Data['Stdout']
        $stderr = [string] $_.Exception.Data['Stderr']
        return [pscustomobject]@{ Passed = $false; Message = ("$stdout $stderr $($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

# Every case builds a *complete* pair of producers and consumers and varies exactly one thing, so a
# red is attributable. The negative cases keep several shared codes registered on both sides: if a
# fixture's only produced code were the missing one, the checker could be reporting "nothing matched"
# rather than "this code is missing", and deleting the containment comparison would still look red.
# Channel A has two producer idioms and both must be exercised in every case: `SafeCode` members on
# exception types, and `public const string` members of a `*StableWireCodes` registry class. A fixture
# that only ever used one of them would let the other idiom's discovery be deleted without a red.
$sharedSafeCodes = @('lifecycle-conflict', 'idempotency-conflict', 'request-payload-invalid')
$sharedRegistryCodes = @('idempotency-key-too-long')
$sharedEnvelopeCodes = $sharedSafeCodes + $sharedRegistryCodes
$sharedReadinessCodes = @('MATERIAL_SHORTAGE', 'QUALITY_HOLD_ACTIVE', 'equipment.downtime')

function Invoke-Case {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [hashtable] $Backend = @{},
        [hashtable] $Frontend = @{}
    )

    $caseRoot = Join-Path $fixtureRoot $Name
    [System.IO.Directory]::CreateDirectory($caseRoot) | Out-Null

    $backendArgs = @{ Root = (Join-Path $caseRoot 'backend') }
    foreach ($key in $Backend.Keys) { $backendArgs[$key] = $Backend[$key] }
    if (-not $backendArgs.ContainsKey('SafeCode')) { $backendArgs['SafeCode'] = $sharedSafeCodes }
    if (-not $backendArgs.ContainsKey('RegistryCode')) { $backendArgs['RegistryCode'] = $sharedRegistryCodes }
    if (-not $backendArgs.ContainsKey('ReadinessCode')) { $backendArgs['ReadinessCode'] = $sharedReadinessCodes }
    New-FixtureBackend @backendArgs

    $frontendArgs = @{ Root = $caseRoot }
    foreach ($key in $Frontend.Keys) { $frontendArgs[$key] = $Frontend[$key] }
    if (-not $frontendArgs.ContainsKey('StableErrorKey')) { $frontendArgs['StableErrorKey'] = $sharedEnvelopeCodes }
    if (-not $frontendArgs.ContainsKey('ReadinessKey')) { $frontendArgs['ReadinessKey'] = $sharedReadinessCodes }
    New-FixtureFrontend @frontendArgs

    return Invoke-Verifier -Name "stable-code-vocab-$Name" -Arguments @(
        '-RepositoryRoot', $caseRoot,
        '-BackendRoot', 'backend',
        '-StableErrorMessagesPath', 'frontend/stable.ts',
        '-ReadinessDisplaysPath', 'frontend/readiness.ts')
}

try {
    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    # --- Case 1: the shape this gate exists for, channel A. A SafeCode the frontend cannot map. ---
    $case1 = Invoke-Case -Name 'envelope-code-unregistered' `
        -Backend @{ SafeCode = ($sharedSafeCodes + 'stored-receipt-is-invalid') }
    Assert-Contract -Condition (-not $case1.Passed) -Message 'An envelope stable code missing from STABLE_ERROR_MESSAGES must fail the check.'
    Assert-Contract -Condition ($case1.Message.Contains('stored-receipt-is-invalid', [StringComparison]::Ordinal)) `
        -Message "The failure must name the offending code. Actual: $($case1.Message)"
    Assert-Contract -Condition (-not $case1.Message.Contains('idempotency-conflict', [StringComparison]::Ordinal)) `
        -Message "Only the unregistered code may be reported. Actual: $($case1.Message)"

    # --- Case 2: the same shape on channel B, which has a different consumer and a different harm. ---
    $case2 = Invoke-Case -Name 'readiness-code-unregistered' `
        -Backend @{ ReadinessCode = ($sharedReadinessCodes + 'WORK_ORDER_NOT_FOUND') }
    Assert-Contract -Condition (-not $case2.Passed) -Message 'A readiness code missing from MES_READINESS_REASON_DISPLAYS must fail the check.'
    Assert-Contract -Condition ($case2.Message.Contains('WORK_ORDER_NOT_FOUND', [StringComparison]::Ordinal)) `
        -Message "The failure must name the offending code. Actual: $($case2.Message)"
    Assert-Contract -Condition ($case2.Message.Contains('RELEASE_IGNORED_TASK_BLOCKERS', [StringComparison]::Ordinal)) `
        -Message "The channel B failure must state the release-blocking consequence, not channel A's. Actual: $($case2.Message)"

    # --- Case 3: the reverse direction is legal, and this is the false-positive probe. The frontend
    # keeps displays for codes no backend producer emits (EQUIPMENT_UNAVAILABLE and
    # EQUIPMENT_MAINTENANCE_CONFLICT are exactly this on main). Requiring equality would report them. ---
    $case3 = Invoke-Case -Name 'frontend-keeps-retired-codes' `
        -Frontend @{
            StableErrorKey = ($sharedEnvelopeCodes + 'downstream-timeout')
            ReadinessKey = ($sharedReadinessCodes + @('EQUIPMENT_UNAVAILABLE', 'SOURCE_SERVICE_UNAVAILABLE'))
        }
    Assert-Contract -Condition $case3.Passed `
        -Message "Frontend entries with no backend producer must pass — the rule is containment, not equality. Actual: $($case3.Message)"

    # --- Case 4: identical sets pass. Positive control for cases 1 and 2: those fixtures differ from
    # this one by exactly one code, so their red cannot be blamed on the fixture shape. ---
    $case4 = Invoke-Case -Name 'identical-sets'
    Assert-Contract -Condition $case4.Passed -Message "Identical producer and consumer sets must pass. Actual: $($case4.Message)"

    # --- Case 5: the readiness registry renamed away. An empty producer set is contained in anything,
    # so a naive checker would pass silently. It must say it cannot read the producer. ---
    $case5 = Invoke-Case -Name 'readiness-registry-renamed' -Backend @{ ReadinessClassName = 'MesReadinessReasonCodesV2' }
    Assert-Contract -Condition (-not $case5.Passed) -Message 'A renamed readiness registry class must fail the check.'
    Assert-Contract -Condition ($case5.Message.Contains('MesReadinessReasonCodes', [StringComparison]::Ordinal)) `
        -Message "The failure must name the producer it could not find. Actual: $($case5.Message)"

    # --- Case 6: the readiness registry emptied. Same disarm, one level down. ---
    $case6 = Invoke-Case -Name 'readiness-registry-emptied' -Backend @{ ReadinessCode = @() }
    Assert-Contract -Condition (-not $case6.Passed) -Message 'An emptied readiness registry must fail rather than pass vacuously.'

    # --- Case 7: a `*StableWireCodes` registry declared but holding nothing. ---
    $case7 = Invoke-Case -Name 'wire-code-registry-emptied' -Backend @{ SafeCode = @(); RegistryCode = @() }
    Assert-Contract -Condition (-not $case7.Passed) -Message 'An emptied stable wire code registry must fail rather than pass vacuously.'

    # --- Case 8: the frontend table renamed away. The consumer side can disarm the gate too. ---
    $case8 = Invoke-Case -Name 'frontend-const-renamed' -Frontend @{ StableErrorConstName = 'STABLE_ERROR_MESSAGES_V2' }
    Assert-Contract -Condition (-not $case8.Passed) -Message 'A renamed frontend vocabulary const must fail the check.'
    Assert-Contract -Condition ($case8.Message.Contains('STABLE_ERROR_MESSAGES', [StringComparison]::Ordinal)) `
        -Message "The failure must name the consumer it could not find. Actual: $($case8.Message)"

    # --- Case 9: the frontend table emptied. Reported as a broken consumer, not as a wall of
    # violations that someone might "fix" by deleting backend declarations. ---
    $case9 = Invoke-Case -Name 'frontend-table-emptied' -Frontend @{ ReadinessKey = @() }
    Assert-Contract -Condition (-not $case9.Passed) -Message 'An emptied frontend vocabulary must fail the check.'
    Assert-Contract -Condition ($case9.Message.Contains('yielded no keys', [StringComparison]::Ordinal)) `
        -Message "An emptied table must be reported as a broken consumer. Actual: $($case9.Message)"

    # --- Case 10: private consts in the readiness registry are NOT part of the contract. On main
    # `equipment.sourceUnavailable` is private and is normalised away before it can reach a reason
    # list; requiring it would be a false positive. This is the "what we deliberately do not check"
    # assertion — without it, tightening the member filter would look free. ---
    $case10 = Invoke-Case -Name 'private-const-not-required' `
        -Backend @{ ReadinessPrivateCode = @('equipment.sourceUnavailable', 'DT-PM') }
    Assert-Contract -Condition $case10.Passed `
        -Message "Private consts in the readiness registry must not be required in the frontend table. Actual: $($case10.Message)"

    # --- Case 11: class-body isolation. The neighbour class in the same file carries a const whose
    # value is registered nowhere; folding it in (whole-file regex) would turn this green case red. ---
    $case11 = Invoke-Case -Name 'neighbour-class-ignored' -Backend @{ NeighbourCode = 'WORK_ORDER_NOT_RELEASED: 工单尚未下达。' }
    Assert-Contract -Condition $case11.Passed `
        -Message "A const in a neighbouring class must not be folded into the registry. Actual: $($case11.Message)"

    # --- Case 12: depth-awareness of the frontend parser. The backend code equals an *inner* key of
    # the display records (`nextStep`), which appears in the file many times but is not a registered
    # code. A depth-blind parser would count it and go green. ---
    $case12 = Invoke-Case -Name 'nested-key-is-not-a-code' `
        -Backend @{ ReadinessCode = ($sharedReadinessCodes + 'nextStep') }
    Assert-Contract -Condition (-not $case12.Passed) `
        -Message 'An inner key of a display record must not count as a registered code.'

    # --- Case 13: a key that exists only inside a comment is not registration. ---
    $case13 = Invoke-Case -Name 'commented-out-key-is-not-registration' `
        -Backend @{ SafeCode = ($sharedSafeCodes + 'retired-but-commented') } `
        -Frontend @{ CommentedOutKey = 'retired-but-commented' }
    Assert-Contract -Condition (-not $case13.Passed) `
        -Message 'A commented-out frontend entry must not count as registration.'

    # --- Case 14: a const initialised from another type resolves across files (this is how the
    # readiness registry re-exports the shared equipment codes from Contracts). ---
    $case14 = Invoke-Case -Name 'cross-file-const-reference-resolves' `
        -Backend @{ ReadinessReference = 'FixtureSharedReasonCodes.Shared'; ReferencedLiteral = 'equipment.activeAlarm' } `
        -Frontend @{ ReadinessKey = ($sharedReadinessCodes + 'equipment.activeAlarm') }
    Assert-Contract -Condition $case14.Passed `
        -Message "A const re-exported from another type must resolve to its literal. Actual: $($case14.Message)"

    # --- Case 15: the same reference with the target missing. Skipping it would quietly shrink the
    # checked set, which has the same effect as deleting the code from the registry. ---
    $case15 = Invoke-Case -Name 'unresolvable-const-reference' `
        -Backend @{ ReadinessReference = 'FixtureSharedReasonCodes.Shared' }
    Assert-Contract -Condition (-not $case15.Passed) -Message 'An unresolvable const reference must fail rather than be skipped.'
    Assert-Contract -Condition ($case15.Message.Contains('could not be resolved', [StringComparison]::Ordinal)) `
        -Message "The failure must say the reference could not be resolved. Actual: $($case15.Message)"

    # --- Case 16: a backend root that does not exist must fail, not scan nothing quietly. ---
    $case16 = Invoke-Verifier -Name 'stable-code-vocab-missing-backend' -Arguments @(
        '-RepositoryRoot', $fixtureRoot,
        '-BackendRoot', 'does-not-exist',
        '-StableErrorMessagesPath', 'does-not-exist/stable.ts',
        '-ReadinessDisplaysPath', 'does-not-exist/readiness.ts')
    Assert-Contract -Condition (-not $case16.Passed) -Message 'A missing backend root must fail the check.'

    # --- Case 17: the repository's real producers and vocabularies. This is the regression anchor —
    # the assertion that goes red when someone renames a backend code without registering it. It is
    # deliberately last: on its own it proves nothing about the checker (cases 5-16 do that), but
    # without it the checker would never be pointed at the files it exists to compare. ---
    $case17 = Invoke-Verifier -Name 'stable-code-vocab-repository'
    Assert-Contract -Condition $case17.Passed `
        -Message "The repository's own stable codes must all have a frontend display. Actual: $($case17.Message)"
    Assert-Contract -Condition ($case17.Message.Contains('Every backend code has a frontend display', [StringComparison]::Ordinal)) `
        -Message "The success output must state the containment conclusion. Actual: $($case17.Message)"
    # Positive judgement on the real tree: "nothing was reported" is also what an empty parse looks
    # like, so the counts have to be read, not just the exit code (Assert.Empty-shaped assertions have
    # zero discrimination against a parser that returns nothing).
    Assert-Contract -Condition ([regex]::IsMatch($case17.Message, 'Channel A: backend declares (?<n>\d+) envelope stable codes') -and
            [int]([regex]::Match($case17.Message, 'Channel A: backend declares (?<n>\d+) envelope stable codes').Groups['n'].Value) -gt 0) `
        -Message "The real run must report a non-zero channel A producer count. Actual: $($case17.Message)"
    Assert-Contract -Condition ([regex]::IsMatch($case17.Message, 'Channel B: backend declares (?<n>\d+) readiness block reason codes') -and
            [int]([regex]::Match($case17.Message, 'Channel B: backend declares (?<n>\d+) readiness block reason codes').Groups['n'].Value) -gt 0) `
        -Message "The real run must report a non-zero channel B producer count. Actual: $($case17.Message)"

    if ($script:Failures.Count -gt 0) {
        Write-Host "Stable code to frontend vocabulary contract tests failed ($($script:Failures.Count) assertions):"
        foreach ($failure in $script:Failures) {
            Write-Host "  $failure"
        }

        exit 1
    }

    Write-Host 'Stable code to frontend vocabulary contract tests passed (17 cases).'
    # Explicit, because the success path would otherwise inherit the exit code of whatever native
    # command ran last.
    exit 0
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
