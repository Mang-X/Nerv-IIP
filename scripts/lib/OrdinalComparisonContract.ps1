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

$script:NervOrdinalContractBannedOperators = @('Ceq', 'Cne', 'Ccontains', 'Cnotcontains', 'Cin', 'Cnotin')
$script:NervOrdinalContractCultureOperators = @('Ieq', 'Ine', 'Icontains', 'Inotcontains', 'Iin', 'Inotin')
$script:NervOrdinalContractWhereSwitches = @(
    'eq', 'ne', 'ceq', 'cne', 'contains', 'notcontains', 'ccontains', 'cnotcontains', 'in', 'notin', 'cin', 'cnotin')
# String methods that are unambiguously string methods: no collection type in this tree exposes them,
# so a missing [StringComparison] argument is always a culture-aware comparison.
$script:NervOrdinalContractStringMethods = @('StartsWith', 'EndsWith', 'IndexOf', 'LastIndexOf')
# …unlike Contains/Equals, which HashSet[string] and Hashtable also expose. Only the spelling that
# cannot be a set lookup in practice is flagged: a single *string-literal* argument.
$script:NervOrdinalContractAmbiguousMethods = @('Contains', 'Equals')

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
        'ambiguous-method-with-string-literal',
        'non-ordinal-stringcomparison'
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

function Get-NervOrdinalComparisonFindings {
    <#
        Scans one PowerShell file for the culture-aware comparison constructs enumerated above.

        -Exceptions takes rows of @{ Text = <exact extent text>; Site = <enclosing function name or
        '<file>'>; Reason = <why culture-aware is the right reading here> }. The match is *exact
        ordinal equality* on the offending node's own extent text, not a substring test: a substring
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
    $exceptionHits = @{}
    foreach ($exception in @($Exceptions)) {
        if ($null -eq $exception) { continue }
        $exceptionHits["$([string]$exception.Site)|$([string]$exception.Text)"] = 0
    }

    $report = {
        param($Node, $Axis, $Message)

        $key = "$(Get-NervOrdinalContractEnclosingSite -Node $Node)|$([string] $Node.Extent.Text)"
        if ($exceptionHits.ContainsKey($key)) {
            $exceptionHits[$key] = [int] $exceptionHits[$key] + 1
            return
        }
        # The offending expression is always carried in the finding, first line only and clipped:
        # a finding that names a rule but not the code is unactionable, and a multi-line `switch`
        # would otherwise bury the rest of the report.
        $excerpt = ([string] $Node.Extent.Text) -split "`r?`n" | Select-Object -First 1
        if ($excerpt.Length -gt 160) { $excerpt = $excerpt.Substring(0, 157) + '...' }
        $findings.Add("[$Axis] ${label}:$($Node.Extent.StartLineNumber) $Message | $excerpt")
    }.GetNewClosure()

    foreach ($binary in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.BinaryExpressionAst]
    }, $true)) {
        $operator = [string] $binary.Operator
        if (@($script:NervOrdinalContractBannedOperators | Where-Object { [string]::Equals($_, $operator, [StringComparison]::Ordinal) }).Count -gt 0) {
            # Deliberately not routed through $report: the banned operators have no exception path.
            $findings.Add("[banned-c-operator] ${label}:$($binary.Extent.StartLineNumber) uses -$($operator.ToLowerInvariant()); the c-prefixed operators only disable case-insensitivity and still fold ignorable characters.")
            continue
        }
        if (@($script:NervOrdinalContractCultureOperators | Where-Object { [string]::Equals($_, $operator, [StringComparison]::Ordinal) }).Count -eq 0) { continue }
        if (-not ((Test-NervOrdinalContractStringOperand -Node $binary.Left) -or
                  (Test-NervOrdinalContractStringOperand -Node $binary.Right))) {
            continue
        }
        & $report $binary 'culture-operator-with-string-literal' "compares strings with -$($operator.ToLowerInvariant()), which is culture-aware."
    }

    foreach ($command in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.CommandAst]
    }, $true)) {
        $name = [string] $command.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $parameterNames = @($command.CommandElements |
            Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
            ForEach-Object { [string] $_.ParameterName })
        $hasParameter = {
            param($candidate)
            @($parameterNames | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
        }.GetNewClosure()

        $axis = $null
        $message = $null
        if ([string]::Equals($name, 'Sort-Object', [StringComparison]::OrdinalIgnoreCase)) {
            # Both spellings, not just -Unique: ordering a retained artifact by culture collation makes
            # the bytes depend on the machine's locale, which is the same defect one step later.
            $axis = 'sort-object'
            $message = if (& $hasParameter 'Unique') {
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
        elseif ([string]::Equals($name, 'Select-Object', [StringComparison]::OrdinalIgnoreCase) -and (& $hasParameter 'Unique')) {
            $axis = 'select-object-unique'
            $message = 'deduplicates with Select-Object -Unique, which compares culture-aware.'
        }
        elseif ([string]::Equals($name, 'Where-Object', [StringComparison]::OrdinalIgnoreCase)) {
            $comparisonParameter = @($parameterNames | Where-Object {
                $candidate = $_
                @($script:NervOrdinalContractWhereSwitches | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            })
            if ($comparisonParameter.Count -gt 0) {
                $axis = 'where-object-comparison-switch'
                $message = "filters with Where-Object -$($comparisonParameter[0]), which compares culture-aware. Use a script block with an ordinal equality helper."
            }
        }

        if ($null -eq $axis) { continue }
        & $report $command $axis $message
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
        & $report $switchStatement 'switch-statement-string-clause' "branches on string clauses with switch, whose matching is culture-aware even under -CaseSensitive: $(($stringClauses | ForEach-Object { $_.Extent.Text }) -join ', ')"
    }

    foreach ($invocation in $ast.FindAll({
        param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
    }, $true)) {
        $member = [string] $invocation.Member.Value
        if ([string]::IsNullOrWhiteSpace($member)) { continue }
        $arguments = @($invocation.Arguments)
        if (@($script:NervOrdinalContractStringMethods | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            if (Test-NervOrdinalContractOrdinalArgument -Arguments $arguments) { continue }
            & $report $invocation 'string-method-without-ordinal-comparison' "calls .$member() without an explicit ordinal [StringComparison]."
            continue
        }
        if (@($script:NervOrdinalContractAmbiguousMethods | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) { continue }
        if ($arguments.Count -ne 1) { continue }
        # A *literal* argument only. `[string]$x` would be a string operand for the binary-operator
        # rule above, but here it is the ordinary spelling of a HashSet/Hashtable lookup, which is
        # already ordinal by construction. Flagging it would drown the axis in false positives and
        # the exception table would then be where the real findings hide.
        if ($arguments[0] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $arguments[0] -isnot [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
            continue
        }
        & $report $invocation 'ambiguous-method-with-string-literal' "calls .$member() on a string literal without an explicit ordinal [StringComparison]."
    }

    foreach ($memberExpression in $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
        $node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
        $node.Expression -is [System.Management.Automation.Language.TypeExpressionAst]
    }, $true)) {
        $typeName = [string] $memberExpression.Expression.TypeName.FullName
        if (-not $typeName.EndsWith('StringComparison', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $member = [string] $memberExpression.Member.Value
        if ([string]::Equals($member, 'Ordinal', [StringComparison]::Ordinal) -or
            [string]::Equals($member, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)) {
            continue
        }
        & $report $memberExpression 'non-ordinal-stringcomparison' "names [StringComparison]::$member, which is culture-aware even though it is written out explicitly."
    }

    return [pscustomobject]@{
        Findings = @($findings)
        ExceptionHits = $exceptionHits
    }
}
