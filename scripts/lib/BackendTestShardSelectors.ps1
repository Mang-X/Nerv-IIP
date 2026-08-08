# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the shard manifest objects and TRX documents its callers hand it
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

function Get-BackendTestShardOptionalArray {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return @()
    }

    return @($property.Value)
}

function Get-BackendTestShardExcludedSelectors {
    param(
        [Parameter(Mandatory)] [object] $Shard,
        [ValidateSet('all', 'class', 'method')] [string] $Kind = 'all'
    )

    $selectors = @()
    if ($Kind -in @('all', 'class')) {
        $selectors += @(Get-BackendTestShardOptionalArray -Object $Shard -PropertyName 'excludedTestClasses')
    }
    if ($Kind -in @('all', 'method')) {
        $selectors += @(Get-BackendTestShardOptionalArray -Object $Shard -PropertyName 'excludedTests')
    }

    return @(
        $selectors |
            Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string] $_) } |
            ForEach-Object { [string] $_ } |
            Sort-Object -Unique
    )
}

function Get-BackendTestShardPolicyIdentityMatches {
    <#
        Resolves one fast-shard exclusion selector to the MAN-661 policy identities it covers.

        A selector covers an identity when it *is* that identity (a method selector) or when it is
        that identity's class prefix (a class selector, matched with a trailing dot so a sibling
        class sharing the prefix is not swallowed). A blank or null identity is covered by nothing,
        including the empty selector it would otherwise compare equal to. Comparison is ordinal
        throughout: these are identifiers, and PowerShell's default — including `-ceq` — is
        culture-aware and folds ignorable characters.

        Each clause above has an executable counterpart in scripts/tests/backend-test-shards.Tests.ps1
        under "Discrimination controls for the key derivation itself"; none of them is documentation
        alone.

        This exists as one function because it is the derivation the shard policy gate runs and the
        one its contract tests have to check. A test that re-derived the same keys by hand could not
        fail: it would be asserting its own arithmetic, not the gate's.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Selector,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Rules
    )

    return @(
        foreach ($rule in @($Rules)) {
            if ($null -eq $rule) { continue }
            foreach ($identity in @(Get-BackendTestShardOptionalArray -Object $rule -PropertyName 'testIdentities')) {
                $identityText = [string] $identity
                if ([string]::IsNullOrWhiteSpace($identityText)) { continue }
                if (-not ([string]::Equals($identityText, $Selector, [StringComparison]::Ordinal) -or
                        $identityText.StartsWith("$Selector.", [StringComparison]::Ordinal))) {
                    continue
                }
                [pscustomobject][ordered]@{
                    selector = $Selector
                    sourceId = [string] $rule.sourceId
                    ruleId = [string] $rule.id
                    identity = $identityText
                    requiredLane = [string] $rule.requiredLane
                    classification = [string] $rule.classification
                }
            }
        }
    )
}

function Get-BackendTestShardPolicyIdentityKey {
    <#
        The identity of one policy match, as a single comparable string.

        Called only by scripts/tests/backend-test-shards.Tests.ps1 today: the gate itself needs the
        *matches* (Get-BackendTestShardPolicyIdentityMatches, which it does call), not a flattened
        key. It lives here rather than in the test because a key the test builds for itself would be
        the test asserting its own arithmetic — and because the thing being frozen is what a policy
        row's identity *is*, which is a property of the derivation, not of one test.

        It carries the registering source, the rule and the frozen test identity — and deliberately
        carries no lane and no shard. That absence is the whole #1507 property: re-homing a project
        between shards changes which shard *holds* an exclusion, never which policy row governs it,
        so a rearrangement cannot invalidate a key. See docs/architecture/test-evidence-governance.md
        ("Timing data is a cache, not a governed asset").

        "No lane and no shard" is enforced, not just written down: scripts/tests/backend-test-shards.Tests.ps1
        splits the key back into its three segments and compares each against the match it came from,
        so a fourth field — `requiredLane` is the tempting one — fails there. That is a statement
        about the *key*; lane keeps its separate job as a rule's applicability condition inside
        `Test-NervRuleApplies`, which is what the `zero-execution` hard gate is built on.
    #>
    param([Parameter(Mandatory)] [object] $Match)

    return "$([string] $Match.sourceId)|$([string] $Match.ruleId)|$([string] $Match.identity)"
}

function Get-BackendTestShardExecutedAssemblies {
    param([Parameter(Mandatory)] [string] $ResultsDirectory)

    $assemblies = [System.Collections.Generic.List[string]]::new()
    foreach ($trxPath in @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse | Sort-Object FullName)) {
        $document = [xml] (Get-Content -LiteralPath $trxPath.FullName -Raw)
        foreach ($definition in @($document.GetElementsByTagName('UnitTest', '*'))) {
            $storage = [string] $definition.GetAttribute('storage')
            if (-not [string]::IsNullOrWhiteSpace($storage)) {
                # Same storage-to-assembly rule as the MAN-661 collector.
                $assemblies.Add([System.IO.Path]::GetFileName($storage))
            }
        }
    }

    return @($assemblies | Sort-Object -Unique)
}

function Assert-BackendTestShardProjectExecution {
    param(
        [Parameter(Mandatory)] [string] $ShardId,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ClassifiedProjects,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ExecutedAssemblies
    )

    $expected = @(
        $ClassifiedProjects |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string] $_) } |
            Sort-Object -Unique
    )
    $observed = @(
        $ExecutedAssemblies |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string] $_) } |
            Sort-Object -Unique
    )

    $missing = @($expected | Where-Object { $observed -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Fast shard '$ShardId' produced no executed test result for classified projects: $($missing -join ', '). Narrow the excluded real-dependency selectors or move the project to an explicit heavy lane."
    }

    $unexpected = @($observed | Where-Object { $expected -notcontains $_ })
    if ($unexpected.Count -gt 0) {
        throw "Fast shard '$ShardId' executed assemblies it does not classify: $($unexpected -join ', '). The solution filter and the shard manifest have drifted."
    }
}

function Assert-BackendTestShardSelectorDiscovery {
    param(
        [Parameter(Mandatory)] [string] $Selector,
        [Parameter(Mandatory)] [bool] $MethodSelector,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $DiscoveredTests
    )

    $matchedTests = @($DiscoveredTests | Where-Object { $_.StartsWith($Selector, [StringComparison]::Ordinal) })
    if ($matchedTests.Count -eq 0 -or ($MethodSelector -and $matchedTests.Count -ne 1)) {
        $expected = if ($MethodSelector) { 'exactly one test' } else { 'at least one test' }
        throw "Real PostgreSQL selector '$Selector' discovery must match $expected; matched $($matchedTests.Count)."
    }

    return $matchedTests
}

function Assert-BackendTestShardSelectorExecution {
    param(
        [Parameter(Mandatory)] [string] $Selector,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $DiscoveredTests,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $TrxResults
    )

    $expectedTests = @($DiscoveredTests | Where-Object { $_.StartsWith($Selector, [StringComparison]::Ordinal) })
    $matchedResults = @($TrxResults | Where-Object { ([string] $_.testName).StartsWith($Selector, [StringComparison]::Ordinal) })
    $executedNames = @($matchedResults | ForEach-Object { [string] $_.testName })
    $missingTests = @($expectedTests | Where-Object { $executedNames -notcontains $_ })
    $failedResults = @($matchedResults | Where-Object { [string] $_.outcome -ne 'Passed' })
    if ($missingTests.Count -gt 0 -or $failedResults.Count -gt 0) {
        throw "Real PostgreSQL selector '$Selector' must execute every discovered test as Passed; discovered=$($expectedTests.Count), trx=$($matchedResults.Count), missing=$($missingTests.Count)."
    }
}
