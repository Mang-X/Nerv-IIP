# Script-Governance:
#   Category: library
#   SideEffects:
#     - Parses the PowerShell files its callers name
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

<#
    The scan face behind "this file compares identifiers ordinally" (#1509 round 3).

    It exists as one library because two contract tests make that claim about two different files —
    scripts/tests/test-evidence.Tests.ps1 about scripts/lib/TestEvidence.ps1, and
    scripts/tests/backend-test-shards.Tests.ps1 about scripts/lib/BackendTestShardSelectors.ps1 —
    and a claim asserted by two hand-written scanners is two different claims. It also makes the
    coverage *enumerable*: Get-NervOrdinalContractCoveredAxes and Get-NervOrdinalContractBlindSpots
    below are the two halves of what the documentation is allowed to say, and both are asserted
    against synthetic sources by the callers, so "the contract exists" can never be read as "the
    semantics are protected".

    What "culture-aware" means here was measured, not assumed (pwsh 7 on macOS, U+00AD SOFT HYPHEN):

      "Passed$([char]0x00AD)" -ne "Passed"     → False   (a not-passed result folds into Passed)
      switch ("failed$([char]0x00AD)")         → hits the 'failed' clause
      ("$([char]0x00AD)^x").StartsWith('^')    → True    (an anchor guard is bypassed)
      @('apple','Banana') | Sort-Object        → apple, Banana (culture); ordinal is Banana, apple
      Sort-Object @{Expression='name'}         → same culture collation, on a retained artifact

    `-ceq`/`-cne`/`-ccontains`/`-cnotcontains`/`-cin`/`-cnotin` are banned outright with no exception
    path: the `c` prefix only disables case-insensitivity, it does not make the comparison ordinal,
    and reading it as "the strict one" is what put these operators in the tree in the first place.
#>

# These are functions rather than `$script:` arrays for the same reason Add-NervOrdinalContractFinding
# is a function rather than a closure: *this library*, dot-sourced the way its two contract tests
# dot-source it, was measured failing that way on the CI runner — the sibling function could not be
# resolved from the captured scope while the identical file ran green locally under both invocation
# forms. The root cause was never reproduced here, so this is deliberately not stated as a general
# rule about dot-sourced libraries: FullStackSessionState.ps1, BackendTestShardTimings.ps1,
# ScriptAutomation.ps1 and LeaderDemoTelemetrySimulator.ps1 all use `$script:` state or closures in
# this repository and work. The narrow claim is what is defended: a plain function call resolves the
# same way from every caller, which removes the variable this file was bitten by without asserting
# anything about the others.
#
# The three operator sets below are a *partition* of PowerShell's case-sensitivity-paired comparison
# operators, not a list of the spellings a review has named so far (#1509 round 6). The family is
# enumerable from the parser's own type system — a TokenKind `I…` whose `C…` sibling also exists —
# and the callers assert that banned ∪ culture ∪ pattern is exactly that family, so a PowerShell
# release adding a comparison operator forces a ruling instead of landing in the silent tail. The
# relational operators (`-lt`/`-le`/`-gt`/`-ge`) were the ones missing: they compare strings by
# culture collation, the same defect as `Sort-Object`, and only the equality/membership half of the
# family had been enumerated.
function Get-NervOrdinalContractBannedOperators { return @('Ceq', 'Cne', 'Cge', 'Cgt', 'Clt', 'Cle', 'Ccontains', 'Cnotcontains', 'Cin', 'Cnotin') }
function Get-NervOrdinalContractCultureOperators { return @('Ieq', 'Ine', 'Ige', 'Igt', 'Ilt', 'Ile', 'Icontains', 'Inotcontains', 'Iin', 'Inotin') }
# Ruled out by the #1507 boundary rather than covered: `-like`/`-match` are pattern matching against
# human-facing text, and `-replace`/`-split` produce strings instead of deciding an identity. They
# are still culture-aware, which is why they are named here and carried in the blind-spot list rather
# than left unmentioned.
function Get-NervOrdinalContractPatternOperators {
    return @(
        'Ilike', 'Inotlike', 'Imatch', 'Inotmatch', 'Ireplace', 'Isplit',
        'Clike', 'Cnotlike', 'Cmatch', 'Cnotmatch', 'Creplace', 'Csplit'
    )
}
function Get-NervOrdinalContractWhereSwitches {
    return @('eq', 'ne', 'ceq', 'cne', 'contains', 'notcontains', 'ccontains', 'cnotcontains', 'in', 'notin', 'cin', 'cnotin')
}
# String methods that are unambiguously string methods: no collection type in this tree exposes them,
# so a missing [StringComparison] argument is always a culture-aware comparison.
function Get-NervOrdinalContractStringMethods { return @('StartsWith', 'EndsWith', 'IndexOf', 'LastIndexOf') }
# Ordering comparisons spelled as methods. `[string]::Compare($a, $b)` and `$a.CompareTo($b)` are
# culture collation exactly like `-lt` and `Sort-Object`, and neither was in the string-method set
# (#1509 round 6). `CompareTo` also exists on numbers and dates, where the finding would be a false
# positive; that direction is deliberate — a spurious finding is answered by writing the comparison
# out, a missed one is a silent locale dependency in a retained artifact. An ordinal
# [StringComparison] argument or an ordinal [StringComparer] receiver clears it.
function Get-NervOrdinalContractComparisonMethods { return @('Compare', 'CompareTo') }
# …unlike Contains/Equals, which HashSet[string] and Hashtable also expose. Only the spelling that
# cannot be a set lookup in practice is flagged: a single *string-literal* argument.
function Get-NervOrdinalContractAmbiguousMethods { return @('Contains', 'Equals') }

function Get-NervOrdinalContractCoveredAxes {
    <#
        The constructs this scan actually reports. Documentation may claim exactly these and no more.
        Each name has a positive case in the callers' discrimination fixtures.
    #>
    return @(
        'banned-c-operator',
        'culture-operator-with-string-literal',
        'sort-object',
        'group-object',
        'compare-object',
        'select-object-unique',
        'where-object-comparison-switch',
        'switch-statement-string-clause',
        'string-method-without-ordinal-comparison',
        'comparison-method-without-ordinal-comparison',
        'parameterless-sort-method',
        'ambiguous-method-with-string-literal',
        'non-ordinal-stringcomparison',
        'non-ordinal-stringcomparer',
        'culture-created-stringcomparer'
    )
}

function Get-NervOrdinalContractBlindSpots {
    <#
        What the scan cannot see. These are behaviours, not aspirations: every entry has a negative
        case in the callers' discrimination fixtures asserting that the scan stays silent, so the day
        one of them becomes detectable the fixture goes red and this list has to be edited with it.
    #>
    return @(
        'both-operands-non-literal-eq',
        'both-operands-non-literal-in',
        'ambiguous-method-with-variable-argument',
        'like-and-match-operators',
        'validateset-attribute',
        'sort-object-via-splatted-parameters'
    )
}

function Get-NervOrdinalContractEnclosingSite {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            return [string] $current.Name
        }
        $current = $current.Parent
    }

    return '<file>'
}

function Test-NervOrdinalContractStringOperand {
    param([Parameter(Mandatory)] [AllowNull()] [System.Management.Automation.Language.Ast] $Node)

    if ($null -eq $Node) { return $false }
    if ($Node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $Node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        return $true
    }
    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        $typeName = [string] $Node.Type.TypeName.FullName
        return [string]::Equals($typeName, 'string', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($typeName, 'string[]', [StringComparison]::OrdinalIgnoreCase)
    }
    if ($Node -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        $elements = @($Node.Elements)
        if ($elements.Count -eq 0) { return $false }
        return @($elements | Where-Object { -not (Test-NervOrdinalContractStringOperand -Node $_) }).Count -eq 0
    }
    return $false
}

function Test-NervOrdinalContractOrdinalArgument {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Arguments)

    foreach ($argument in @($Arguments)) {
        if ($argument -isnot [System.Management.Automation.Language.MemberExpressionAst]) { continue }
        $typeName = if ($argument.Expression -is [System.Management.Automation.Language.TypeExpressionAst]) {
            [string] $argument.Expression.TypeName.FullName
        }
        else { '' }
        if (-not $typeName.EndsWith('StringComparison', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $member = [string] $argument.Member.Value
        if ([string]::Equals($member, 'Ordinal', [StringComparison]::Ordinal) -or
            [string]::Equals($member, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Test-NervOrdinalContractOrdinalComparerReceiver {
    <#
        True when the call is made *on* an ordinal comparer — `[StringComparer]::Ordinal.Compare(…)`.
        Without this the comparison-method axis would report the one spelling that is already right.
    #>
    param([Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Node)

    $receiver = $Node.Expression
    if ($receiver -isnot [System.Management.Automation.Language.MemberExpressionAst]) { return $false }
    if ($receiver -is [System.Management.Automation.Language.InvokeMemberExpressionAst]) { return $false }
    if ($receiver.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst]) { return $false }
    if (-not ([string] $receiver.Expression.TypeName.FullName).EndsWith('StringComparer', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $receiverMember = [string] $receiver.Member.Value
    return [string]::Equals($receiverMember, 'Ordinal', [StringComparison]::Ordinal) -or
        [string]::Equals($receiverMember, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)
}

function Test-NervOrdinalContractHasParameter {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ParameterNames,
        [Parameter(Mandatory)] [string] $Name
    )

    return @($ParameterNames | Where-Object { [string]::Equals($_, $Name, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
}

function Add-NervOrdinalContractFinding {
    <#
        Records one finding, unless a named exception claims this exact node.

        A plain function rather than a `{ … }.GetNewClosure()` helper (#1509 round 3, second CI-only
        defect): in *this* file, dot-sourced the way its contract tests dot-source it, the closure
        form could not resolve the library's own sibling functions on the CI runner —
        `Get-NervOrdinalContractEnclosingSite` came back "not recognized" while the same file ran
        green locally on 7.6.4 under both `-File` and the `-command ". '…'"` form CI uses. The root
        cause was never reproduced locally, so the claim stays that narrow rather than becoming a
        rule about closures in general; see the note above Get-NervOrdinalContractBannedOperators.
        Passing the two accumulators as parameters removes the question entirely: both are reference
        types, so the caller sees the mutations without anything being captured.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Collections.Generic.List[string]] $Findings,
        [Parameter(Mandatory)] [hashtable] $ExceptionHits,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [string] $Axis,
        [Parameter(Mandatory)] [string] $Message
    )

    $key = "$(Get-NervOrdinalContractEnclosingSite -Node $Node)|$([string] $Node.Extent.Text)"
    if ($ExceptionHits.ContainsKey($key)) {
        $ExceptionHits[$key] = [int] $ExceptionHits[$key] + 1
        return
    }

    # The offending expression is always carried in the finding, first line only and clipped: a
    # finding that names a rule but not the code is unactionable, and a multi-line `switch` would
    # otherwise bury the rest of the report.
    $excerpt = @(([string] $Node.Extent.Text) -split "`r?`n")[0]
    if ($excerpt.Length -gt 160) { $excerpt = $excerpt.Substring(0, 157) + '...' }
    $Findings.Add("[$Axis] ${Label}:$($Node.Extent.StartLineNumber) $Message | $excerpt")
}

function Get-NervOrdinalComparisonFindings {
    <#
        Scans one PowerShell file for the culture-aware comparison constructs enumerated above.

        -Exceptions takes rows of @{ Text = <exact extent text>; Site = <enclosing function name or
        '<file>'>; Reason = <why culture-aware is the right reading here> }. The match is *exact
        ordinal equality* — the lookup table is built with [StringComparer]::Ordinal, since a bare
        `@{}` would have made this claim OrdinalIgnoreCase — on the offending node's own extent
        text, not a substring test: a substring
        match lets an exception be widened from one expression to a whole cmdlet name ("Group-Object")
        and silently absorb a future, unrelated call site. Site has to agree too, so moving the
        exempt expression into another function re-reports it.

        Returns @{ Findings = [string[]]; ExceptionHits = [hashtable Key -> count] } so the caller can
        assert both "nothing is reported" and "no exception is dead or over-broad".
    #>
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [AllowEmptyCollection()] [AllowNull()] [object[]] $Exceptions = @(),
        [string] $DisplayName
    )

    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $null, [ref] $parseErrors)
    if (@($parseErrors).Count -gt 0) {
        throw "Ordinal comparison contract cannot parse '$ScriptPath': $($parseErrors[0].Message)"
    }

    $label = if ([string]::IsNullOrWhiteSpace($DisplayName)) { [System.IO.Path]::GetFileName($ScriptPath) } else { $DisplayName }
    $findings = [System.Collections.Generic.List[string]]::new()
    # Ordinal, explicitly. A bare `@{}` is a Hashtable with PowerShell's case-insensitive comparer,
    # so the docstring's "exact ordinal equality" was OrdinalIgnoreCase in fact (#1509 round 4). An
    # exception is a licence to keep one culture-aware expression, and this file's whole argument is
    # that the comparison deciding such a thing must be the one that was written down.
    $exceptionHits = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)
    foreach ($exception in @($Exceptions)) {
        if ($null -eq $exception) { continue }
        $exceptionHits["$([string]$exception.Site)|$([string]$exception.Text)"] = 0
    }

    foreach ($binary in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.BinaryExpressionAst]
    }, $true)) {
        $operator = [string] $binary.Operator
        if (@((Get-NervOrdinalContractBannedOperators) | Where-Object { [string]::Equals($_, $operator, [StringComparison]::Ordinal) }).Count -gt 0) {
            # Deliberately not routed through Add-NervOrdinalContractFinding: the banned operators have no exception path.
            $findings.Add("[banned-c-operator] ${label}:$($binary.Extent.StartLineNumber) uses -$($operator.ToLowerInvariant()); the c-prefixed operators only disable case-insensitivity and still fold ignorable characters.")
            continue
        }
        if (@((Get-NervOrdinalContractCultureOperators) | Where-Object { [string]::Equals($_, $operator, [StringComparison]::Ordinal) }).Count -eq 0) { continue }
        if (-not ((Test-NervOrdinalContractStringOperand -Node $binary.Left) -or
                  (Test-NervOrdinalContractStringOperand -Node $binary.Right))) {
            continue
        }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $binary -Axis 'culture-operator-with-string-literal' -Message "compares strings with -$($operator.ToLowerInvariant()), which is culture-aware."
    }

    foreach ($command in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.CommandAst]
    }, $true)) {
        $name = [string] $command.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $parameterNames = @($command.CommandElements |
            Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
            ForEach-Object { [string] $_.ParameterName })
        $axis = $null
        $message = $null
        if ([string]::Equals($name, 'Sort-Object', [StringComparison]::OrdinalIgnoreCase)) {
            # Both spellings, not just -Unique: ordering a retained artifact by culture collation makes
            # the bytes depend on the machine's locale, which is the same defect one step later.
            $axis = 'sort-object'
            $message = if (Test-NervOrdinalContractHasParameter -ParameterNames $parameterNames -Name 'Unique') {
                'deduplicates with Sort-Object -Unique, which folds ignorable characters. Use an ordinal set or sort helper.'
            }
            else {
                'orders with Sort-Object, whose key comparison is culture collation. Use an ordinal sort helper.'
            }
        }
        elseif ([string]::Equals($name, 'Group-Object', [StringComparison]::OrdinalIgnoreCase)) {
            $axis = 'group-object'
            $message = 'groups with Group-Object, whose key comparison is culture-aware.'
        }
        elseif ([string]::Equals($name, 'Compare-Object', [StringComparison]::OrdinalIgnoreCase)) {
            $axis = 'compare-object'
            $message = 'diffs with Compare-Object, whose comparison is culture-aware.'
        }
        elseif ([string]::Equals($name, 'Select-Object', [StringComparison]::OrdinalIgnoreCase) -and (Test-NervOrdinalContractHasParameter -ParameterNames $parameterNames -Name 'Unique')) {
            $axis = 'select-object-unique'
            $message = 'deduplicates with Select-Object -Unique, which compares culture-aware.'
        }
        elseif ([string]::Equals($name, 'Where-Object', [StringComparison]::OrdinalIgnoreCase)) {
            $comparisonParameter = @($parameterNames | Where-Object {
                $candidate = $_
                @((Get-NervOrdinalContractWhereSwitches) | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            })
            if ($comparisonParameter.Count -gt 0) {
                $axis = 'where-object-comparison-switch'
                $message = "filters with Where-Object -$($comparisonParameter[0]), which compares culture-aware. Use a script block with an ordinal equality helper."
            }
        }

        if ($null -eq $axis) { continue }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $command -Axis $axis -Message $message
    }

    foreach ($switchStatement in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.SwitchStatementAst]
    }, $true)) {
        # PowerShell's `switch` is case-insensitive *and* culture-aware by default, and `-CaseSensitive`
        # fixes only the first half: `switch ("failed$([char]0x00AD)") { 'failed' {...} }` still hits the
        # 'failed' clause under -CaseSensitive. There is no switch flag that makes it ordinal, so a
        # string-labelled switch over an identifier has to be rewritten as explicit comparisons.
        # A clause is a Tuple[ExpressionAst, StatementBlockAst]; Item1 is the label being matched.
        $stringClauses = @($switchStatement.Clauses | ForEach-Object { $_.Item1 } | Where-Object {
            Test-NervOrdinalContractStringOperand -Node $_
        })
        if ($stringClauses.Count -eq 0) { continue }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $switchStatement -Axis 'switch-statement-string-clause' -Message "branches on string clauses with switch, whose matching is culture-aware even under -CaseSensitive: $(($stringClauses | ForEach-Object { $_.Extent.Text }) -join ', ')"
    }

    foreach ($invocation in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
    }, $true)) {
        $member = [string] $invocation.Member.Value
        if ([string]::IsNullOrWhiteSpace($member)) { continue }
        $arguments = @($invocation.Arguments)
        if ([string]::Equals($member, 'Create', [StringComparison]::OrdinalIgnoreCase) -and
            $invocation.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
            ([string] $invocation.Expression.TypeName.FullName).EndsWith('StringComparer', [StringComparison]::OrdinalIgnoreCase)) {
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'culture-created-stringcomparer' -Message 'creates a StringComparer from a CultureInfo; use Ordinal or OrdinalIgnoreCase.'
            continue
        }
        if (@((Get-NervOrdinalContractStringMethods) | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            if (Test-NervOrdinalContractOrdinalArgument -Arguments $arguments) { continue }
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'string-method-without-ordinal-comparison' -Message "calls .$member() without an explicit ordinal [StringComparison]."
            continue
        }
        if (@((Get-NervOrdinalContractComparisonMethods) | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            if (Test-NervOrdinalContractOrdinalArgument -Arguments $arguments) { continue }
            if (Test-NervOrdinalContractOrdinalComparerReceiver -Node $invocation) { continue }
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'comparison-method-without-ordinal-comparison' -Message "orders with .$member() without an explicit ordinal comparer, which is culture collation."
            continue
        }
        # `$list.Sort()` uses Comparer<string>.Default, which is culture-aware — the reason
        # Get-NervOrdinalSorted passes [StringComparer]::Ordinal explicitly. Only the no-argument
        # spelling is flagged: any argument is either a comparer or a comparison, both of which the
        # reader can see.
        # `$invocation.Arguments` is $null — not an empty collection — for a no-argument call, and
        # `@($null)` counts as one element, so the emptiness test cannot go through $arguments.
        if ([string]::Equals($member, 'Sort', [StringComparison]::OrdinalIgnoreCase) -and $null -eq $invocation.Arguments) {
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'parameterless-sort-method' -Message 'sorts with .Sort() and no comparer, which is Comparer<string>.Default and therefore culture collation.'
            continue
        }
        if (@((Get-NervOrdinalContractAmbiguousMethods) | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) { continue }
        if ($arguments.Count -ne 1) { continue }
        # A *literal* argument only. `[string]$x` would be a string operand for the binary-operator
        # rule above, but here it is the ordinary spelling of a HashSet/Hashtable lookup, which is
        # already ordinal by construction. Flagging it would drown the axis in false positives and
        # the exception table would then be where the real findings hide.
        if ($arguments[0] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $arguments[0] -isnot [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
            continue
        }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'ambiguous-method-with-string-literal' -Message "calls .$member() on a string literal without an explicit ordinal [StringComparison]."
    }

    foreach ($memberExpression in $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
        $node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
        $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst]
    }, $true)) {
        $typeName = [string] $memberExpression.Expression.TypeName.FullName
        # Two type names, not one. The `non-ordinal-stringcomparison` axis matched on a name ending in
        # "StringComparison", and [StringComparer] does not end in that — so `[StringComparer]::
        # InvariantCulture` scanned clean while being exactly the same mistake (#1509 round 6). The
        # irony that mattered: every ordinal fix in this library and its two subjects is spelled by
        # *passing* [StringComparer]::Ordinal, so the one construct the whole contract is built on was
        # the one construct nothing checked.
        $isComparison = $typeName.EndsWith('StringComparison', [StringComparison]::OrdinalIgnoreCase)
        $isComparer = $typeName.EndsWith('StringComparer', [StringComparison]::OrdinalIgnoreCase)
        if (-not ($isComparison -or $isComparer)) { continue }
        $member = [string] $memberExpression.Member.Value
        if ([string]::Equals($member, 'Ordinal', [StringComparison]::Ordinal) -or
            [string]::Equals($member, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)) {
            continue
        }
        if ($isComparison) {
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $memberExpression -Axis 'non-ordinal-stringcomparison' -Message "names [StringComparison]::$member, which is culture-aware even though it is written out explicitly."
            continue
        }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $memberExpression -Axis 'non-ordinal-stringcomparer' -Message "names [StringComparer]::$member, which is a culture-aware comparer even though it is written out explicitly."
    }

    return [pscustomobject]@{
        Findings = @($findings)
        ExceptionHits = $exceptionHits
    }
}
