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
        'culture-operator-with-identity-variable',
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
        'non-identity-variable-eq',
        'both-operands-non-literal-in',
        'ambiguous-method-with-variable-argument',
        'variable-write-via-dynamic-provider-path',
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
    if ($Node -is [System.Management.Automation.Language.ArrayExpressionAst]) {
        $statements = @($Node.SubExpression.Statements)
        if (($null -ne $Node.SubExpression.Traps -and $Node.SubExpression.Traps.Count -ne 0) -or
            $statements.Count -ne 1 -or $statements[0] -isnot [System.Management.Automation.Language.PipelineAst]) {
            return $false
        }
        $pipelineElements = @($statements[0].PipelineElements)
        if ($pipelineElements.Count -ne 1 -or
            $pipelineElements[0] -isnot [System.Management.Automation.Language.CommandExpressionAst] -or
            @($pipelineElements[0].Redirections).Count -ne 0) {
            return $false
        }
        return Test-NervOrdinalContractStringOperand -Node $pipelineElements[0].Expression
    }
    return $false
}

function Test-NervOrdinalContractIdentityOperand {
    <#
        Detects variables and members whose names express string identity. The suffix set remains
        narrow so counters, attempts, dates and collections do not become false-positive identity
        comparisons merely because both operands are variables.
    #>
    param([Parameter(Mandatory)] [AllowNull()] [System.Management.Automation.Language.Ast] $Node)

    if ($null -eq $Node) { return $false }
    while ($Node -is [System.Management.Automation.Language.ParenExpressionAst] -or
        $Node -is [System.Management.Automation.Language.SubExpressionAst]) {
        $pipeline = if ($Node -is [System.Management.Automation.Language.ParenExpressionAst]) {
            $Node.Pipeline
        }
        else {
            $statements = @($Node.SubExpression.Statements)
            if (($null -ne $Node.SubExpression.Traps -and $Node.SubExpression.Traps.Count -ne 0) -or
                $statements.Count -ne 1) { return $false }
            $statements[0]
        }
        if ($pipeline -isnot [System.Management.Automation.Language.PipelineAst]) { return $false }
        $pipelineElements = @($pipeline.PipelineElements)
        if ($pipelineElements.Count -ne 1 -or
            $pipelineElements[0] -isnot [System.Management.Automation.Language.CommandExpressionAst] -or
            @($pipelineElements[0].Redirections).Count -ne 0) {
            return $false
        }
        $Node = $pipelineElements[0].Expression
    }
    $name = if ($Node -is [System.Management.Automation.Language.VariableExpressionAst]) {
        if (Test-NervOrdinalContractTypedNonStringLocal -Variable $Node -Context $Node) { return $false }
        [string] $Node.VariablePath.UserPath
    }
    elseif ($Node -is [System.Management.Automation.Language.MemberExpressionAst]) {
        [string] $Node.Member.Value
    }
    elseif ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        $resolvedType = $Node.Type.TypeName.GetReflectionType()
        $isStringIdentityType = [object]::ReferenceEquals($resolvedType, [string]) -or
            [object]::ReferenceEquals($resolvedType, [string[]])
        if (-not $isStringIdentityType) { return $false }
        return Test-NervOrdinalContractIdentityOperand -Node $Node.Child
    }
    else { '' }
    if ([string]::IsNullOrWhiteSpace($name) -or $name -cmatch '(?i)(?:ExitCode|StatusCode|ProcessId|Pid)$') { return $false }
    return $name -cmatch '(?:Id|ID|Identity|SHA|Sha|Name|Lane|Outcome|Status|Code|Key|Path|URI|Uri|Namespace|Prefix)$'
}

function Test-NervOrdinalContractTypedNonStringLocal {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.VariableExpressionAst] $Variable,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context
    )

    $scope = Get-NervOrdinalContractEnclosingFunction -Node $Context
    if ($null -eq $scope) { return $false }
    $name = [string] $Variable.VariablePath.UserPath
    $contextBlock = Get-NervOrdinalContractDirectSequentialBlock -Node $Context
    if ($null -eq $contextBlock) { return $false }
    $writes = @($scope.FindAll({
        param($candidate)
        if ($candidate.Extent.StartOffset -ge $Context.Extent.StartOffset) {
            return $false
        }
        return ($candidate -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                (Test-NervOrdinalContractAssignmentWritesVariable -Assignment $candidate -Name $name)) -or
            ($candidate -is [System.Management.Automation.Language.CommandAst] -and
                (Test-NervOrdinalContractCommandMayWriteVariable -Command $candidate -Name $name))
    }, $true) | Where-Object {
        [object]::ReferenceEquals((Get-NervOrdinalContractEnclosingFunction -Node $_), $scope)
    })
    if ($writes.Count -ne 1 -or
        $writes[0] -isnot [System.Management.Automation.Language.AssignmentStatementAst] -or
        -not [object]::ReferenceEquals((Get-NervOrdinalContractDirectSequentialBlock -Node $writes[0]), $contextBlock) -or
        $writes[0].Left -isnot [System.Management.Automation.Language.ConvertExpressionAst] -or
        $writes[0].Left.Child -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
        return $false
    }
    $type = $writes[0].Left.Type.TypeName.GetReflectionType()
    return $null -ne $type -and
        -not [object]::ReferenceEquals($type, [string]) -and
        -not [object]::ReferenceEquals($type, [string[]]) -and
        $type.IsValueType
}

function Get-NervOrdinalContractDirectSequentialBlock {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.StatementBlockAst] -or
            $current -is [System.Management.Automation.Language.NamedBlockAst]) {
            return $current
        }
        $current = $current.Parent
    }
    return $null
}

function Get-NervOrdinalContractEnclosingFunction {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $current }
        $current = $current.Parent
    }
    return $null
}

function Test-NervOrdinalContractTypedParameter {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.VariableExpressionAst] $Variable,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context,
        [Parameter(Mandatory)] [Type[]] $AllowedTypes
    )

    $scope = Get-NervOrdinalContractEnclosingFunction -Node $Context
    if ($null -eq $scope) { return $false }
    $name = [string] $Variable.VariablePath.UserPath
    $parameters = @($scope.Parameters)
    if ($null -ne $scope.Body.ParamBlock) { $parameters += @($scope.Body.ParamBlock.Parameters) }
    $parameters = @($parameters | Where-Object { $_ -is [System.Management.Automation.Language.ParameterAst] })
    foreach ($parameter in $parameters) {
        if (-not [string]::Equals([string] $parameter.Name.VariablePath.UserPath, $name, [StringComparison]::OrdinalIgnoreCase)) { continue }
        foreach ($allowedType in $AllowedTypes) {
            if ([object]::ReferenceEquals($parameter.StaticType, $allowedType)) { return $true }
        }
        return $false
    }
    return $false
}

function Get-NervOrdinalContractLocalAssignments {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.VariableExpressionAst] $Variable,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context
    )

    $scope = Get-NervOrdinalContractEnclosingFunction -Node $Context
    $searchScope = if ($null -ne $scope) { $scope } else {
        $root = $Context
        while ($null -ne $root.Parent) { $root = $root.Parent }
        $root
    }
    $name = [string] $Variable.VariablePath.UserPath
    $parameters = if ($null -ne $scope) { @($scope.Parameters) } else { @() }
    if ($null -ne $scope -and $null -ne $scope.Body.ParamBlock) { $parameters += @($scope.Body.ParamBlock.Parameters) }
    $parameters = @($parameters | Where-Object { $_ -is [System.Management.Automation.Language.ParameterAst] })
    if (@($parameters | Where-Object {
        [string]::Equals([string] $_.Name.VariablePath.UserPath, $name, [StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0) { return @() }

    $assignments = @($searchScope.FindAll({
        param($node)
        if ($node -isnot [System.Management.Automation.Language.AssignmentStatementAst] -or
            $node.Extent.StartOffset -ge $Context.Extent.StartOffset) { return $false }
        Test-NervOrdinalContractAssignmentWritesVariable -Assignment $node -Name $name
    }, $true) | Where-Object {
        $owner = Get-NervOrdinalContractEnclosingFunction -Node $_
        [object]::ReferenceEquals($owner, $scope)
    })
    $commandWrites = @($searchScope.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.Extent.StartOffset -lt $Context.Extent.StartOffset -and
            (Test-NervOrdinalContractCommandMayWriteVariable -Command $node -Name $name)
    }, $true) | Where-Object {
        $owner = Get-NervOrdinalContractEnclosingFunction -Node $_
        [object]::ReferenceEquals($owner, $scope)
    })
    if ($commandWrites.Count -gt 0) { return @() }
    return $assignments
}

function Test-NervOrdinalContractDirectFunctionAssignment {
    <#
        A local proof may not fall back to a value from an outer scope.  The narrow form accepted
        here is a direct statement in the same enclosing execution block as the later reachable
        call site.  A conditional assignment followed by a call outside that conditional therefore
        stays unknown, while statements sequenced within one branch are proven together.
    #>
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.AssignmentStatementAst] $Assignment,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context
    )

    $scope = Get-NervOrdinalContractEnclosingFunction -Node $Context
    if ($null -eq $scope -or
        -not [object]::ReferenceEquals((Get-NervOrdinalContractEnclosingFunction -Node $Assignment), $scope)) {
        return $false
    }
    $contextBlock = $Context.Parent
    while ($null -ne $contextBlock -and
        $contextBlock -isnot [System.Management.Automation.Language.NamedBlockAst] -and
        $contextBlock -isnot [System.Management.Automation.Language.StatementBlockAst]) {
        $contextBlock = $contextBlock.Parent
    }
    return $null -ne $contextBlock -and
        ($Assignment.Parent -is [System.Management.Automation.Language.NamedBlockAst] -or
            $Assignment.Parent -is [System.Management.Automation.Language.StatementBlockAst]) -and
        [object]::ReferenceEquals($Assignment.Parent, $contextBlock)
}

function Test-NervOrdinalContractTypedStringExpression {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context
    )

    if ($Node -is [System.Management.Automation.Language.VariableExpressionAst]) {
        return Test-NervOrdinalContractTypedParameter -Variable $Node -Context $Context -AllowedTypes @([string])
    }
    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        return [object]::ReferenceEquals($Node.Type.TypeName.GetReflectionType(), [string])
    }
    return $Node -is [System.Management.Automation.Language.StringConstantExpressionAst]
}

function Test-NervOrdinalContractCharacterValue {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [System.Management.Automation.Language.BinaryExpressionAst] $Comparison
    )

    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        return [object]::ReferenceEquals($Node.Type.TypeName.GetReflectionType(), [char])
    }
    if ($Node -is [System.Management.Automation.Language.IndexExpressionAst]) {
        return Test-NervOrdinalContractTypedStringExpression -Node $Node.Target -Context $Comparison
    }
    if ($Node -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
    if (Test-NervOrdinalContractTypedParameter -Variable $Node -Context $Comparison -AllowedTypes @([char])) { return $true }

    $assignments = @(Get-NervOrdinalContractLocalAssignments -Variable $Node -Context $Comparison)
    $assignedValue = if ($assignments.Count -eq 1 -and
        $assignments[0].Right -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $assignments[0].Right.Expression
    }
    elseif ($assignments.Count -eq 1) { $assignments[0].Right }
    else { $null }
    if ($assignments.Count -eq 1 -and
        $assignments[0].Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
        $assignedValue -is [System.Management.Automation.Language.IndexExpressionAst] -and
        (Test-NervOrdinalContractTypedStringExpression -Node $assignedValue.Target -Context $Comparison)) {
        return $true
    }

    # The iterator is a char only when this exact enclosing foreach enumerates ToCharArray() on a
    # proven string. A same-named variable in another loop or a custom enumerable stays unknown.
    $ancestor = $Comparison.Parent
    while ($null -ne $ancestor) {
        if ($ancestor -is [System.Management.Automation.Language.ForEachStatementAst] -and
            [string]::Equals([string] $ancestor.Variable.VariablePath.UserPath, [string] $Node.VariablePath.UserPath, [StringComparison]::OrdinalIgnoreCase)) {
            $toCharArrayCalls = @($ancestor.Condition.FindAll({
                param($candidate)
                $candidate -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                    [string]::Equals([string] $candidate.Member.Value, 'ToCharArray', [StringComparison]::OrdinalIgnoreCase)
            }, $true))
            return $toCharArrayCalls.Count -eq 1 -and
                (Test-NervOrdinalContractForeachIteratorIsUnmodified -ForEach $ancestor -Variable $Node -Comparison $Comparison) -and
                (Test-NervOrdinalContractTypedStringExpression -Node $toCharArrayCalls[0].Expression -Context $Comparison)
        }
        if ($ancestor -is [System.Management.Automation.Language.FunctionDefinitionAst]) { break }
        $ancestor = $ancestor.Parent
    }
    return $false
}

function Test-NervOrdinalContractForeachIteratorIsUnmodified {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.ForEachStatementAst] $ForEach,
        [Parameter(Mandatory)] [System.Management.Automation.Language.VariableExpressionAst] $Variable,
        [Parameter(Mandatory)] [System.Management.Automation.Language.BinaryExpressionAst] $Comparison
    )

    $name = [string] $Variable.VariablePath.UserPath
    $writes = @($ForEach.Body.FindAll({
        param($candidate)
        $candidate -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            (Test-NervOrdinalContractAssignmentWritesVariable -Assignment $candidate -Name $name)
    }, $true))
    if ($writes.Count -gt 0) { return $false }

    $commandWrites = @($ForEach.Body.FindAll({
        param($candidate)
        $candidate -is [System.Management.Automation.Language.CommandAst] -and
            (Test-NervOrdinalContractCommandMayWriteVariable -Command $candidate -Name $name)
    }, $true))
    return $commandWrites.Count -eq 0
}

function Test-NervOrdinalContractAssignmentWritesVariable {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.AssignmentStatementAst] $Assignment,
        [Parameter(Mandatory)] [string] $Name
    )

    $target = if ($Assignment.Left -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        $Assignment.Left.Child
    }
    else {
        $Assignment.Left
    }
    return $target -is [System.Management.Automation.Language.VariableExpressionAst] -and
        [string]::Equals([string] $target.VariablePath.UserPath, $Name, [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervOrdinalContractCommandMayWriteVariable {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Command,
        [Parameter(Mandatory)] [string] $Name
    )

    if (Test-NervOrdinalContractBuiltinVariableMutationCommand -Command $Command) {
        return Test-NervOrdinalContractCommandNameMayMatch -Command $Command -Name $Name
    }
    return Test-NervOrdinalContractItemCommandMayWriteVariable -Command $Command -Name $Name
}

function Test-NervOrdinalContractBuiltinVariableMutationCommand {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Command)

    $commandName = [string] $Command.GetCommandName()
    foreach ($candidate in @(
        'Set-Variable', 'Microsoft.PowerShell.Utility\Set-Variable', 'sv',
        'New-Variable', 'Microsoft.PowerShell.Utility\New-Variable', 'nv',
        'Clear-Variable', 'Microsoft.PowerShell.Utility\Clear-Variable', 'clv',
        'Remove-Variable', 'Microsoft.PowerShell.Utility\Remove-Variable', 'rv'
    )) {
        if ([string]::Equals($commandName, $candidate, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-NervOrdinalContractItemCommandMayWriteVariable {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Command,
        [Parameter(Mandatory)] [string] $Name
    )

    $commandName = [string] $Command.GetCommandName()
    $kind = $null
    foreach ($entry in @(
        [pscustomobject]@{ Kind = 'path'; Names = @('Set-Item', 'Microsoft.PowerShell.Management\Set-Item', 'si', 'Clear-Item', 'Microsoft.PowerShell.Management\Clear-Item', 'cli', 'Remove-Item', 'Microsoft.PowerShell.Management\Remove-Item', 'ri', 'rm', 'del', 'erase', 'rd', 'rmdir', 'Rename-Item', 'Microsoft.PowerShell.Management\Rename-Item', 'rni', 'ren') },
        [pscustomobject]@{ Kind = 'source-and-destination'; Names = @('Move-Item', 'Microsoft.PowerShell.Management\Move-Item', 'mi', 'mv', 'move') },
        [pscustomobject]@{ Kind = 'destination'; Names = @('Copy-Item', 'Microsoft.PowerShell.Management\Copy-Item', 'cpi', 'cp', 'copy') }
    )) {
        foreach ($candidate in $entry.Names) {
            if ([string]::Equals($commandName, [string] $candidate, [StringComparison]::OrdinalIgnoreCase)) {
                $kind = [string] $entry.Kind
                break
            }
        }
        if ($null -ne $kind) { break }
    }
    if ($null -eq $kind) { return $false }

    $elements = @($Command.CommandElements)
    $positionals = [Collections.Generic.List[System.Management.Automation.Language.Ast]]::new()
    $namedTargets = [Collections.Generic.List[System.Management.Automation.Language.Ast]]::new()
    $hasNamedSource = $false
    $hasNamedDestination = $false
    for ($index = 1; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) {
            $positionals.Add($element)
            continue
        }

        $argument = $element.Argument
        if ($null -eq $argument -and $index + 1 -lt $elements.Count -and
            $elements[$index + 1] -isnot [System.Management.Automation.Language.CommandParameterAst]) {
            $argument = $elements[$index + 1]
            $index += 1
        }
        $parameterName = [string] $element.ParameterName
        $isSourcePath = [string]::Equals($parameterName, 'Path', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($parameterName, 'LiteralPath', [StringComparison]::OrdinalIgnoreCase)
        $isDestination = [string]::Equals($parameterName, 'Destination', [StringComparison]::OrdinalIgnoreCase)
        if (($isSourcePath -and -not [string]::Equals($kind, 'destination', [StringComparison]::Ordinal)) -or
            ($isDestination -and -not [string]::Equals($kind, 'path', [StringComparison]::Ordinal))) {
            $namedTargets.Add($argument)
            if ($isSourcePath) { $hasNamedSource = $true }
            if ($isDestination) { $hasNamedDestination = $true }
        }
    }

    foreach ($candidate in $namedTargets) {
        if (Test-NervOrdinalContractVariableProviderPathMayMatch -Candidate $candidate -Name $Name) { return $true }
    }
    if ([string]::Equals($kind, 'path', [StringComparison]::Ordinal)) {
        if ($hasNamedSource) { return $false }
        return $positionals.Count -lt 1 -or
            (Test-NervOrdinalContractVariableProviderPathMayMatch -Candidate $positionals[0] -Name $Name)
    }
    if ([string]::Equals($kind, 'source-and-destination', [StringComparison]::Ordinal)) {
        if (-not $hasNamedSource -and $positionals.Count -lt 1) { return $true }
        if (-not $hasNamedSource -and
            (Test-NervOrdinalContractVariableProviderPathMayMatch -Candidate $positionals[0] -Name $Name)) { return $true }
        if ($hasNamedDestination) { return $false }
        return $positionals.Count -ge 2 -and
            (Test-NervOrdinalContractVariableProviderPathMayMatch -Candidate $positionals[1] -Name $Name)
    }
    if ($hasNamedDestination) { return $false }
    return $positionals.Count -ge 2 -and
        (Test-NervOrdinalContractVariableProviderPathMayMatch -Candidate $positionals[1] -Name $Name)
}

function Test-NervOrdinalContractVariableProviderPathMayMatch {
    param(
        [AllowNull()] [System.Management.Automation.Language.Ast] $Candidate,
        [Parameter(Mandatory)] [string] $Name
    )

    # A dynamic provider path does not prove which provider is selected. Treat only an explicit
    # variable: path as a write here; dynamic variable names remain fail-closed in the dedicated
    # *-Variable command family above.
    if ($Candidate -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { return $false }
    $path = [string] $Candidate.Value
    if (-not $path.StartsWith('variable:', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $variablePath = $path.Substring('variable:'.Length).TrimStart('/', '\')
    return Test-NervOrdinalContractVariablePathTextMayMatch -CandidateName $variablePath -Name $Name
}

function Test-NervOrdinalContractCommandNameMayMatch {
    <#
        A dynamic `-Name` cannot prove that it leaves the protected binding unchanged.  The two
        callers use only built-in command identities, so an unknown target must invalidate their
        narrow type/source proof rather than become a broad alias resolver.
    #>
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Command,
        [Parameter(Mandatory)] [string] $Name
    )

    $elements = @($Command.CommandElements)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        if ($elements[$index] -is [System.Management.Automation.Language.CommandParameterAst] -and
            [string]::Equals([string] $elements[$index].ParameterName, 'Name', [StringComparison]::OrdinalIgnoreCase)) {
            $candidate = if ($null -ne $elements[$index].Argument) {
                $elements[$index].Argument
            }
            elseif ($index + 1 -lt $elements.Count) {
                $elements[$index + 1]
            }
            else {
                $null
            }
            return Test-NervOrdinalContractVariablePathMayMatch -Candidate $candidate -Name $Name
        }
    }
    if ($elements.Count -lt 2 -or $elements[1] -is [System.Management.Automation.Language.CommandParameterAst]) { return $true }
    return Test-NervOrdinalContractVariablePathMayMatch -Candidate $elements[1] -Name $Name
}

function Test-NervOrdinalContractVariablePathMayMatch {
    param(
        [AllowNull()] [System.Management.Automation.Language.Ast] $Candidate,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Candidate -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { return $true }
    return Test-NervOrdinalContractVariablePathTextMayMatch -CandidateName ([string] $Candidate.Value) -Name $Name
}

function Test-NervOrdinalContractVariablePathTextMayMatch {
    param(
        [Parameter(Mandatory)] [string] $CandidateName,
        [Parameter(Mandatory)] [string] $Name
    )

    $separator = $candidateName.IndexOf(':', [StringComparison]::Ordinal)
    if ($separator -ge 0) {
        $scope = $candidateName.Substring(0, $separator)
        if (-not ([string]::Equals($scope, 'local', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'script', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'global', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'private', [StringComparison]::OrdinalIgnoreCase))) { return $true }
        $candidateName = $candidateName.Substring($separator + 1)
    }
    if ([string]::IsNullOrWhiteSpace($candidateName)) { return $true }
    return [string]::Equals($candidateName, $Name, [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervOrdinalContractCharacterComparison {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.BinaryExpressionAst] $Node)

    $leftLiteral = $Node.Left -is [System.Management.Automation.Language.StringConstantExpressionAst] -and ([string] $Node.Left.Value).Length -eq 1
    $rightLiteral = $Node.Right -is [System.Management.Automation.Language.StringConstantExpressionAst] -and ([string] $Node.Right.Value).Length -eq 1
    if ($leftLiteral -eq $rightLiteral) { return $false }
    $other = if ($leftLiteral) { $Node.Right } else { $Node.Left }
    return Test-NervOrdinalContractCharacterValue -Node $other -Comparison $Node
}

function Test-NervOrdinalContractOrdinalComparerExpression {
    param([Parameter(Mandatory)] [AllowNull()] [System.Management.Automation.Language.Ast] $Node)

    if ($null -eq $Node -or
        $Node -isnot [System.Management.Automation.Language.MemberExpressionAst] -or
        $Node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        $Node.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst] -or
        -not ([string] $Node.Expression.TypeName.FullName).EndsWith('StringComparer', [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    $member = [string] $Node.Member.Value
    return [string]::Equals($member, 'Ordinal', [StringComparison]::Ordinal) -or
        [string]::Equals($member, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)
}

function Test-NervOrdinalContractOrdinalHashSetConstructor {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    if ($Node -isnot [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
        $Node.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst] -or
        -not [string]::Equals([string] $Node.Member.Value, 'new', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $typeName = [string] $Node.Expression.TypeName.FullName
    if (-not ($typeName.EndsWith('Collections.Generic.HashSet[string]', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($typeName, 'HashSet[string]', [StringComparison]::OrdinalIgnoreCase))) { return $false }
    return @($Node.Arguments | Where-Object { Test-NervOrdinalContractOrdinalComparerExpression -Node $_ }).Count -eq 1
}

function Test-NervOrdinalContractOrdinalHashSetReceiver {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Invocation)

    if (Test-NervOrdinalContractOrdinalHashSetConstructor -Node $Invocation.Expression) { return $true }
    if ($Invocation.Expression -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
    $assignments = @(Get-NervOrdinalContractLocalAssignments -Variable $Invocation.Expression -Context $Invocation)
    $assignedValue = if ($assignments.Count -eq 1 -and
        $assignments[0].Right -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $assignments[0].Right.Expression
    }
    elseif ($assignments.Count -eq 1) { $assignments[0].Right }
    else { $null }
    return $assignments.Count -eq 1 -and
        $assignments[0].Operator -eq [System.Management.Automation.Language.TokenKind]::Equals -and
        (Test-NervOrdinalContractDirectFunctionAssignment -Assignment $assignments[0] -Context $Invocation) -and
        (Test-NervOrdinalContractOrdinalHashSetConstructor -Node $assignedValue)
}

function Test-NervOrdinalContractArrayIndexInvocation {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Node)

    if ($Node.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst] -or
        -not [string]::Equals([string] $Node.Member.Value, 'IndexOf', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $typeName = [string] $Node.Expression.TypeName.FullName
    return [string]::Equals($typeName, 'Array', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($typeName, 'System.Array', [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervOrdinalContractIntegerValue {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Context
    )

    if ($Node -is [System.Management.Automation.Language.ConstantExpressionAst]) { return $Node.Value -is [int] }
    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        return [object]::ReferenceEquals($Node.Type.TypeName.GetReflectionType(), [int])
    }
    if ($Node -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
    if (Test-NervOrdinalContractTypedParameter -Variable $Node -Context $Context -AllowedTypes @([int])) { return $true }
    $assignments = @(Get-NervOrdinalContractLocalAssignments -Variable $Node -Context $Context)
    if ($assignments.Count -ne 1 -or $assignments[0].Operator -ne [System.Management.Automation.Language.TokenKind]::Equals) { return $false }
    $right = if ($assignments[0].Right -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $assignments[0].Right.Expression
    }
    else { $assignments[0].Right }
    return $right -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
        ([string]::Equals([string] $right.Member.Value, 'IndexOf', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals([string] $right.Member.Value, 'LastIndexOf', [StringComparison]::OrdinalIgnoreCase)) -and
        (Test-NervOrdinalContractOrdinalArgument -Arguments @($right.Arguments) -Invocation $right)
}

function Test-NervOrdinalContractStringValue {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Context
    )

    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        return [object]::ReferenceEquals($Node.Type.TypeName.GetReflectionType(), [string])
    }
    if ($Node -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
    if (Test-NervOrdinalContractTypedParameter -Variable $Node -Context $Context -AllowedTypes @([string])) { return $true }

    # `Get-Content -Raw` is the only inferred string-producing command needed by the tree. Every
    # prior assignment in the same scope must have that exact shape; an unknown command, missing
    # -Raw, or a mixed reassignment keeps the receiver unknown and therefore red.
    $assignments = @(Get-NervOrdinalContractLocalAssignments -Variable $Node -Context $Context)
    if ($assignments.Count -eq 0) { return $false }
    foreach ($assignment in $assignments) {
        if ($assignment.Operator -ne [System.Management.Automation.Language.TokenKind]::Equals -or
            $assignment.Right -isnot [System.Management.Automation.Language.PipelineAst]) { return $false }
        $elements = @($assignment.Right.PipelineElements)
        if ($elements.Count -ne 1 -or $elements[0] -isnot [System.Management.Automation.Language.CommandAst] -or
            -not [string]::Equals([string] $elements[0].GetCommandName(), 'Get-Content', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-NervOrdinalContractUnshadowedCommand -Node $elements[0] -Name 'Get-Content')) { return $false }
        $parameters = @($elements[0].CommandElements |
            Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
            ForEach-Object { [string] $_.ParameterName })
        if (-not (Test-NervOrdinalContractHasParameter -ParameterNames $parameters -Name 'Raw')) { return $false }
    }
    return $true
}

function Test-NervOrdinalContractUnshadowedCommand {
    <#
        Command names alone do not prove a type: a script-local function, filter, or literal alias
        may replace a built-in cmdlet.  Only the two narrowly inferred producers call this helper;
        any redefinition in their parsed script unit makes that producer unknown.
    #>
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [string] $Name
    )

    $root = $Node
    while ($null -ne $root.Parent) { $root = $root.Parent }
    $functions = @($root.FindAll({
        param($candidate)
        if ($candidate -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { return $false }
        return Test-NervOrdinalContractFunctionDefinitionNameMayMatch -CandidateName ([string] $candidate.Name) -Name $Name
    }, $true))
    if ($functions.Count -gt 0) { return $false }

    $aliasDefinitions = @($root.FindAll({
        param($candidate)
        if ($candidate -isnot [System.Management.Automation.Language.CommandAst] -or
            -not (Test-NervOrdinalContractBuiltinAliasDefinitionCommand -Command $candidate)) {
            return $false
        }
        return Test-NervOrdinalContractCommandNameMayMatch -Command $candidate -Name $Name
    }, $true))
    return $aliasDefinitions.Count -eq 0
}

function Test-NervOrdinalContractBuiltinAliasDefinitionCommand {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Command)

    $commandName = [string] $Command.GetCommandName()
    return [string]::Equals($commandName, 'Set-Alias', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($commandName, 'New-Alias', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($commandName, 'Microsoft.PowerShell.Utility\Set-Alias', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($commandName, 'Microsoft.PowerShell.Utility\New-Alias', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($commandName, 'sal', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($commandName, 'nal', [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervOrdinalContractFunctionDefinitionNameMayMatch {
    param(
        [Parameter(Mandatory)] [string] $CandidateName,
        [Parameter(Mandatory)] [string] $Name
    )

    $separator = $CandidateName.IndexOf(':', [StringComparison]::Ordinal)
    if ($separator -ge 0) {
        $scope = $CandidateName.Substring(0, $separator)
        if (-not ([string]::Equals($scope, 'local', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'script', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'global', [StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($scope, 'private', [StringComparison]::OrdinalIgnoreCase))) { return $true }
        $CandidateName = $CandidateName.Substring($separator + 1)
    }
    return [string]::IsNullOrWhiteSpace($CandidateName) -or
        [string]::Equals($CandidateName, $Name, [StringComparison]::OrdinalIgnoreCase)
}

function Test-NervOrdinalContractCharacterIndexInvocation {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Node)

    if (-not [string]::Equals([string] $Node.Member.Value, 'IndexOf', [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $arguments = @($Node.Arguments)
    if ($arguments.Count -lt 1 -or $arguments.Count -gt 3 -or
        -not (Test-NervOrdinalContractStringValue -Node $Node.Expression -Context $Node) -or
        -not (Test-NervOrdinalContractStaticCharacterIndexArgument -Node $arguments[0] -Context $Node)) {
        return $false
    }
    if ($arguments.Count -ge 2 -and -not (Test-NervOrdinalContractIntegerValue -Node $arguments[1] -Context $Node)) {
        return $false
    }
    return $arguments.Count -lt 3 -or
        (Test-NervOrdinalContractIntegerValue -Node $arguments[2] -Context $Node)
}

function Test-NervOrdinalContractStaticCharacterIndexArgument {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Context
    )

    if ($Node -is [System.Management.Automation.Language.ConvertExpressionAst]) {
        return [object]::ReferenceEquals($Node.Type.TypeName.GetReflectionType(), [char])
    }
    return $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
        (Test-NervOrdinalContractTypedParameter -Variable $Node -Context $Context -AllowedTypes @([char]))
}

function Test-NervOrdinalContractDateTimeSort {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Node)

    if (Test-NervOrdinalContractHasParameter -ParameterNames @($Node.CommandElements |
        Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
        ForEach-Object { [string] $_.ParameterName }) -Name 'Unique') { return $false }
    $properties = @($Node.CommandElements | Select-Object -Skip 1 | Where-Object {
        $_ -isnot [System.Management.Automation.Language.CommandParameterAst]
    })
    if ($properties.Count -ne 1 -or $properties[0] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -or
        -not [string]::Equals([string] $properties[0].Value, 'LastWriteTimeUtc', [StringComparison]::Ordinal)) { return $false }
    if ($Node.Parent -isnot [System.Management.Automation.Language.PipelineAst]) { return $false }
    $elements = @($Node.Parent.PipelineElements)
    $position = [Array]::IndexOf($elements, $Node)
    return $position -gt 0 -and
        $elements[$position - 1] -is [System.Management.Automation.Language.CommandAst] -and
        [string]::Equals([string] $elements[$position - 1].GetCommandName(), 'Get-ChildItem', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-NervOrdinalContractUnshadowedCommand -Node $elements[$position - 1] -Name 'Get-ChildItem')
}

function Test-NervOrdinalContractOrdinalArgument {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Arguments,
        [System.Management.Automation.Language.InvokeMemberExpressionAst] $Invocation
    )

    foreach ($argument in @($Arguments)) {
        if ($null -ne $Invocation -and $argument -is [System.Management.Automation.Language.VariableExpressionAst] -and
            (Test-NervOrdinalContractOrdinalLocalArgument -Argument $argument -Invocation $Invocation)) {
            return $true
        }
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

function Test-NervOrdinalContractOrdinalValueExpression {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    while ($Node -is [System.Management.Automation.Language.CommandExpressionAst] -or
        $Node -is [System.Management.Automation.Language.ParenExpressionAst]) {
        $Node = if ($Node -is [System.Management.Automation.Language.CommandExpressionAst]) { $Node.Expression } else { $Node.Pipeline }
    }
    if ($Node -is [System.Management.Automation.Language.MemberExpressionAst] -and
        $Node.Expression -is [System.Management.Automation.Language.TypeExpressionAst] -and
        ([string] $Node.Expression.TypeName.FullName).EndsWith('StringComparison', [StringComparison]::OrdinalIgnoreCase)) {
        $member = [string] $Node.Member.Value
        return [string]::Equals($member, 'Ordinal', [StringComparison]::Ordinal) -or
            [string]::Equals($member, 'OrdinalIgnoreCase', [StringComparison]::Ordinal)
    }
    if ($Node -isnot [System.Management.Automation.Language.IfStatementAst] -or $null -eq $Node.ElseClause) { return $false }
    $branches = @($Node.Clauses | ForEach-Object { $_.Item2 }) + @($Node.ElseClause)
    foreach ($branch in $branches) {
        $statements = @($branch.Statements)
        if ($statements.Count -ne 1 -or $statements[0] -isnot [System.Management.Automation.Language.PipelineAst]) { return $false }
        $elements = @($statements[0].PipelineElements)
        if ($elements.Count -ne 1 -or $elements[0] -isnot [System.Management.Automation.Language.CommandExpressionAst]) { return $false }
        if (-not (Test-NervOrdinalContractOrdinalValueExpression -Node $elements[0])) { return $false }
    }
    return $true
}

function Test-NervOrdinalContractOrdinalLocalArgument {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.VariableExpressionAst] $Argument,
        [Parameter(Mandatory)] [System.Management.Automation.Language.InvokeMemberExpressionAst] $Invocation
    )

    $function = Get-NervOrdinalContractEnclosingSite -Node $Invocation
    $scope = $Invocation.Parent
    while ($null -ne $scope -and $scope -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $scope = $scope.Parent }
    if ($null -eq $scope -or -not [string]::Equals([string] $scope.Name, $function, [StringComparison]::Ordinal)) { return $false }
    $variableName = [string] $Argument.VariablePath.UserPath
    foreach ($parameter in @($scope.Parameters)) {
        if ($parameter -is [System.Management.Automation.Language.ParameterAst] -and
            [string]::Equals([string] $parameter.Name.VariablePath.UserPath, $variableName, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    $assignments = @(Get-NervOrdinalContractLocalAssignments -Variable $Argument -Context $Invocation)
    if ($assignments.Count -ne 1 -or
        $assignments[0].Operator -ne [System.Management.Automation.Language.TokenKind]::Equals -or
        -not (Test-NervOrdinalContractDirectFunctionAssignment -Assignment $assignments[0] -Context $Invocation)) { return $false }
    return Test-NervOrdinalContractOrdinalValueExpression -Node $assignments[0].Right
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
        if (([string]::Equals($operator, 'Ieq', [StringComparison]::Ordinal) -or
            [string]::Equals($operator, 'Ine', [StringComparison]::Ordinal)) -and
            (Test-NervOrdinalContractCharacterComparison -Node $binary)) {
            continue
        }
        $hasStringLiteral = (Test-NervOrdinalContractStringOperand -Node $binary.Left) -or
            (Test-NervOrdinalContractStringOperand -Node $binary.Right)
        $hasIdentityVariable = ([string]::Equals($operator, 'Ieq', [StringComparison]::Ordinal) -or
            [string]::Equals($operator, 'Ine', [StringComparison]::Ordinal)) -and
            (Test-NervOrdinalContractIdentityOperand -Node $binary.Left) -and
            (Test-NervOrdinalContractIdentityOperand -Node $binary.Right)
        if (-not $hasStringLiteral -and -not $hasIdentityVariable) {
            continue
        }
        $axis = if ($hasStringLiteral) { 'culture-operator-with-string-literal' } else { 'culture-operator-with-identity-variable' }
        Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $binary -Axis $axis -Message "compares identity-sensitive values with -$($operator.ToLowerInvariant()), which is culture-aware."
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
            if (Test-NervOrdinalContractDateTimeSort -Node $command) { continue }
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
        if (Test-NervOrdinalContractArrayIndexInvocation -Node $invocation) { continue }
        if (@((Get-NervOrdinalContractStringMethods) | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            if (Test-NervOrdinalContractOrdinalArgument -Arguments $arguments -Invocation $invocation) { continue }
            if (Test-NervOrdinalContractCharacterIndexInvocation -Node $invocation) { continue }
            Add-NervOrdinalContractFinding -Findings $findings -ExceptionHits $exceptionHits -Label $label -Node $invocation -Axis 'string-method-without-ordinal-comparison' -Message "calls .$member() without an explicit ordinal [StringComparison]."
            continue
        }
        if (@((Get-NervOrdinalContractComparisonMethods) | Where-Object { [string]::Equals($_, $member, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            if (Test-NervOrdinalContractOrdinalArgument -Arguments $arguments -Invocation $invocation) { continue }
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
        if ([string]::Equals($member, 'Contains', [StringComparison]::OrdinalIgnoreCase) -and
            (Test-NervOrdinalContractOrdinalHashSetReceiver -Invocation $invocation)) { continue }
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
