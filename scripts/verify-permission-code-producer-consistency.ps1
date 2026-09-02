# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the IAM seed permission producer and the BusinessGateway permission constants
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
    Fails when BusinessGateway enforces a `business.*` permission code that IAM cannot grant.

.DESCRIPTION
    A `business.*` permission code has two producers that are written by hand and never compared:

      * IAM  — `NervIipSeedPermissions.All` in
        backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs. This is the closed world of
        grantable codes: `IamPermissionCatalog.EnsureSeeded` throws
        `KnownException("Unknown permission code '<code>'.")` for anything outside it, so a code
        missing here can be held by no role at all.
      * BusinessGateway — the `BusinessGatewayPermissions` constants in
        backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs.
        This is what the facade actually enforces at the browser-facing edge.

    A code that Gateway enforces but IAM does not seed is not a naming inconsistency: it is an
    endpoint that returns 403 to every principal in production, forever, because no role can be
    granted the code in the first place. Neither test suite can see it — each producer is internally
    consistent, and the Gateway proxy tests authorize through a stub client that allows everything.
    Both directions of the single-sided rollback were measured on PR #2858 and stayed fully green
    (issue #2863).

    Direction. The rule is containment, `Gateway ⊆ IAM`, not set equality. IAM legitimately seeds
    codes that no Gateway facade exposes — service endpoints reached only through the internal
    service policy — which ADR 0029 (实施说明 1) exempts in as many words: 「凡经 Gateway facade
    暴露的新码，必须在 IAM 与 Gateway 两处 producer 同时落地……仅经 internal service policy 使用的
    码不在此列」. Requiring equality would report those legal codes as violations and push the
    repository toward an allowlist of permanent exceptions.

    Scope. Only these two producers. The 12 per-service `*PermissionCodes.cs` files are consumers
    holding a per-domain subset, not a registration surface, and
    docs/reference/security/authorization-catalog.md is transcription prose that would need a parser
    rather than a set comparison. Both exclusions were ruled on in issue #2863 and are deliberately
    not smuggled back in here.

    A gate that reads source text can disarm itself silently: rename the class, empty the array, and
    an "everything matched" comparison over two empty sets passes. So every step that could produce
    an empty set fails loudly instead — a missing class declaration, an unparseable `All` array and a
    zero-code result are each reported as failures, and the contract test
    scripts/tests/permission-code-producer-consistency.Tests.ps1 pins that behaviour with fixtures.
#>

[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,

    # Overridden only by the contract test, which points the checker at throwaway fixture files.
    [string] $IamSeedPermissionsPath = 'backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs',

    [string] $GatewayPermissionsPath = 'backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs'
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

# Returns the text between the braces of `<...> class <Name>`, brace-matched rather than
# line-counted: `NervIipSeedRoles` in the same file also contains `business.*` literals (the ERP job
# role seeds), and a regex over the whole file would silently fold those into the IAM set and make
# the containment check pass for the wrong reason.
function Get-CSharpClassBody {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $ClassName
    )

    $declaration = [regex]::Match($Text, "(?m)^\s*public\s+static\s+class\s+$([regex]::Escape($ClassName))\b")
    if (-not $declaration.Success) {
        return $null
    }

    $open = $Text.IndexOf('{', $declaration.Index)
    if ($open -lt 0) {
        return $null
    }

    $depth = 0
    for ($i = $open; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($open + 1, $i - $open - 1)
            }
        }
    }

    return $null
}

# The collection-expression body of `public static readonly string[] All = [ ... ];`.
function Get-CollectionInitializerBody {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $MemberName
    )

    $declaration = [regex]::Match($Text, "\b$([regex]::Escape($MemberName))\s*=\s*\[")
    if (-not $declaration.Success) {
        return $null
    }

    $open = $Text.IndexOf('[', $declaration.Index)
    $depth = 0
    for ($i = $open; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq '[') { $depth++ }
        elseif ($ch -eq ']') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($open + 1, $i - $open - 1)
            }
        }
    }

    return $null
}

function Get-BusinessPermissionCode {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text
    )

    return @(
        Get-NervStringsSorted -Values @([regex]::Matches($Text, '"(?<code>business\.[^"]*)"') |
            ForEach-Object { $_.Groups['code'].Value }) -Comparer ([StringComparer]::Ordinal) -Unique
    )
}

$errors = [System.Collections.Generic.List[string]]::new()

$iamFullPath = Join-Path $RepositoryRoot $IamSeedPermissionsPath
$gatewayFullPath = Join-Path $RepositoryRoot $GatewayPermissionsPath

$iamCodes = @()
$gatewayCodes = @()

if (-not (Test-Path -LiteralPath $iamFullPath -PathType Leaf)) {
    $errors.Add("IAM seed permission producer does not exist: $IamSeedPermissionsPath.")
}
else {
    $iamText = Get-Content -LiteralPath $iamFullPath -Raw
    $iamClassBody = Get-CSharpClassBody -Text $iamText -ClassName 'NervIipSeedPermissions'
    if ($null -eq $iamClassBody) {
        $errors.Add(
            "$IamSeedPermissionsPath no longer declares 'public static class NervIipSeedPermissions', so the " +
            'grantable permission set cannot be read. If the producer moved, update this checker with it; ' +
            'leaving it unable to find the producer would turn the gate into a no-op.')
    }
    else {
        $iamArrayBody = Get-CollectionInitializerBody -Text $iamClassBody -MemberName 'All'
        if ($null -eq $iamArrayBody) {
            $errors.Add(
                "$IamSeedPermissionsPath declares NervIipSeedPermissions but its 'All' collection initializer " +
                'could not be parsed, so the grantable permission set is unknown.')
        }
        else {
            $iamCodes = @(Get-BusinessPermissionCode -Text $iamArrayBody)
            if ($iamCodes.Count -eq 0) {
                $errors.Add(
                    "NervIipSeedPermissions.All in $IamSeedPermissionsPath yielded no 'business.*' codes; a " +
                    'containment check against an empty grantable set would pass vacuously.')
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $gatewayFullPath -PathType Leaf)) {
    $errors.Add("BusinessGateway permission producer does not exist: $GatewayPermissionsPath.")
}
else {
    $gatewayText = Get-Content -LiteralPath $gatewayFullPath -Raw
    $gatewayClassBody = Get-CSharpClassBody -Text $gatewayText -ClassName 'BusinessGatewayPermissions'
    if ($null -eq $gatewayClassBody) {
        $errors.Add(
            "$GatewayPermissionsPath no longer declares 'public static class BusinessGatewayPermissions', so the " +
            'enforced permission set cannot be read. If the producer moved, update this checker with it.')
    }
    else {
        $gatewayCodes = @(Get-BusinessPermissionCode -Text $gatewayClassBody)
        if ($gatewayCodes.Count -eq 0) {
            $errors.Add(
                "BusinessGatewayPermissions in $GatewayPermissionsPath yielded no 'business.*' codes; an empty " +
                'enforced set is contained in anything, so the check would pass vacuously.')
        }
    }
}

if ($errors.Count -eq 0) {
    $iamSet = [System.Collections.Generic.HashSet[string]]::new([string[]] $iamCodes, [System.StringComparer]::Ordinal)
    $missing = @($gatewayCodes | Where-Object { -not $iamSet.Contains($_) })
    foreach ($code in $missing) {
        $errors.Add(
            "BusinessGateway enforces '$code' but $IamSeedPermissionsPath does not seed it, so " +
            "IamPermissionCatalog.EnsureSeeded rejects it and no role can hold it — every principal is denied " +
            'at that endpoint. Add it to NervIipSeedPermissions.All (and IamPermissionCatalog.Descriptions), or ' +
            'remove the Gateway enforcement if the capability is gone.')
    }
}

if ($errors.Count -gt 0) {
    Write-Host 'Permission code producer consistency failed:'
    foreach ($failure in $errors) {
        Write-Host "  $failure"
    }

    exit 1
}

Write-Host 'Permission code producer consistency passed:'
Write-Host "  $GatewayPermissionsPath enforces $($gatewayCodes.Count) 'business.*' codes."
Write-Host "  $IamSeedPermissionsPath seeds $($iamCodes.Count) 'business.*' codes."
Write-Host '  Every enforced code is seeded (Gateway is contained in IAM).'

exit 0
