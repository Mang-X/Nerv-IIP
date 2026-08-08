# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses PowerShell scripts under scripts/
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string[]] $Path = @((Join-Path $PSScriptRoot '.')),

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'script-governance-baseline.json')
)

$ErrorActionPreference = 'Stop'

$allowedCategories = @('check', 'verify', 'generate', 'release-install', 'library')

# Scan boundary (#1509 ruling; the narrative and its rationale live in
# docs/architecture/script-automation-governance.md, "scripts/lib 的治理扫描边界").
#
# This used to exclude `scripts/lib/*` wholesale, which meant ForbiddenCommand, DynamicInvocation,
# ForbiddenProcessStart and even ParseError were simply not enforced on the shared libraries — the
# files with the widest blast radius in the whole tree. The exclusion is now the two files that
# cannot meaningfully be judged by rules that point at them, plus the test tree; libraries are
# scanned under a declared library scope (see $libraryScopePattern below).
#
# It is a data table rather than an inline boolean chain because
# scripts/tests/script-governance-scan-boundary.Tests.ps1 asserts this exact list: widening the boundary is
# then a reviewable change to a named contract, not an edit inside a `Where-Object`.
$scanExclusions = @(
    # The checker cannot be its own subject: it names every forbidden command as a literal.
    'scripts/check-script-governance.ps1',
    # The wrapper every rule redirects to. ForbiddenCommand/DynamicInvocation exist to force callers
    # into this file, so applying them here is circular by construction.
    'scripts/lib/ScriptAutomation.ps1',
    # Test scripts run the governed programs as real processes and author deliberately invalid
    # fixtures; both are the point of a test and neither is a governance finding.
    'scripts/tests/*'
)

# Files under this path are libraries: dot-sourced into a caller's scope, never invoked as programs.
$libraryScopePattern = 'scripts/lib/*'

$forbiddenCommands = @(
    'dotnet',
    'docker',
    'pnpm',
    'pwsh',
    'powershell',
    'start-job',
    'start-process',
    'invoke-expression',
    'iex'
)

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $CandidatePath
    )

    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
    $fullPath = (Resolve-Path $CandidatePath).Path
    $relative = [System.IO.Path]::GetRelativePath($repoRoot.Path, $fullPath)
    return ($relative -replace '\\', '/')
}

function Get-GovernanceScripts {
    param(
        [Parameter(Mandatory)]
        [string[]] $InputPaths
    )

    $scripts = New-Object System.Collections.Generic.List[string]

    foreach ($inputPath in $InputPaths) {
        $resolved = Resolve-Path $inputPath -ErrorAction Stop
        foreach ($item in $resolved) {
            if (Test-Path $item.Path -PathType Leaf) {
                if ([System.IO.Path]::GetExtension($item.Path) -eq '.ps1') {
                    $scripts.Add($item.Path)
                }
                continue
            }

            Get-ChildItem -Path $item.Path -Recurse -File -Filter '*.ps1' |
                Where-Object {
                    $relative = Get-RepoRelativePath -CandidatePath $_.FullName
                    -not (@($scanExclusions | Where-Object { $relative -like $_ }).Count -gt 0)
                } |
                ForEach-Object { $scripts.Add($_.FullName) }
        }
    }

    return @($scripts | Sort-Object -Unique)
}

function Get-GovernanceBaseline {
    param(
        [Parameter(Mandatory)]
        [string] $InputBaselinePath
    )

    $map = @{}

    if (-not (Test-Path $InputBaselinePath)) {
        return $map
    }

    $json = Get-Content $InputBaselinePath -Raw | ConvertFrom-Json
    foreach ($exemption in $json.exemptions) {
        $pathKey = (($exemption.path -replace '\\', '/') ).Trim()
        $map[$pathKey] = @($exemption.rules)
    }

    return $map
}

function Add-GovernanceViolation {
    param(
        [Parameter(Mandatory)]
        [object] $Violations,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Rule,

        [Parameter(Mandatory)]
        [string] $Message,

        [int] $Line = 1
    )

    $Violations.Add([pscustomobject]@{
        Path = $Path
        Rule = $Rule
        Line = $Line
        Message = $Message
    })
}

function Test-IsExempted {
    param(
        [hashtable] $Baseline,

        [string] $Path,

        [string] $Rule
    )

    if (-not $Baseline.ContainsKey($Path)) {
        return $false
    }

    return @($Baseline[$Path]) -contains $Rule
}

# The names a library is allowed to invoke with `&`: variables the file itself proves are script
# blocks, either as a `[scriptblock]`-typed parameter or as a variable assigned a `{ ... }` literal.
# That is exactly the injected-action seam this repository builds testable libraries out of (see
# AGENTS.md, 后端测试确定性), and it is provable from the AST — unlike `& $someString`, which is the
# arbitrary-command hole ForbiddenCommand cannot see. PowerShell variable names are case-insensitive,
# so the set is too.
#
# The proof is *scoped*, not file-wide (#1509 review). Collecting every script-block name in the file
# would let one function's `$action = { … }` license a different function's
# `$action = 'dotnet'; & $action`, i.e. reopen the exact hole this rule exists for by picking a
# popular parameter name — and the relaxation is meant to be as strong as its wording, which says
# "a variable this file proves holds a script block *here*". A scope is one ScriptBlockAst: a
# function body, or a `{ … }` literal. Lookup walks the enclosing chain outward, which is what
# PowerShell's own scoping does, so an outer seam stays visible to an inner block while a sibling's
# never is — and an inner *rebinding* shadows the outer seam, for the same reason.
function Get-NearestScriptBlockScope {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node)

    $child = $Node
    $current = $Node.Parent
    while ($null -ne $current -and $current -isnot [System.Management.Automation.Language.ScriptBlockAst]) {
        # An inline parameter list — `function Foo([scriptblock] $Action) { … }` — hangs off the
        # FunctionDefinitionAst, *beside* the body rather than inside it. Walking straight on to the
        # next enclosing ScriptBlockAst would therefore file that parameter under the whole file and
        # make it visible to every sibling function, so `function Bar { $Action = 'dotnet'; & $Action }`
        # passed while the byte-identical `param()` spelling failed (#1509 round 2, measured). The
        # runtime scope of a parameter is the function body under either spelling; say so.
        if ($current -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            -not [object]::ReferenceEquals($child, $current.Body)) {
            return $current.Body
        }
        $child = $current
        $current = $current.Parent
    }

    return $current
}

# The scope qualifiers that still name an ordinary PowerShell variable. `$local:a`, `$script:a`,
# `$global:a`, `$private:a` and `$variable:a` all bind the name `a`; `$env:a` and `$function:a` are
# provider drives and are not variable bindings at all, so a name carrying one of those is reported
# as unresolvable (which fails closed: `& $env:x` proves nothing and stays a violation).
$seamScopeQualifiers = @('local', 'script', 'global', 'private', 'using', 'variable')

function Get-SeamBindingName {
    <#
        A binding's name with any scope qualifier removed, or $null when the path is not a variable.

        Without this, `$local:action = 'dotnet'` recorded the binding under the literal name
        `local:action`, which never matched the `action` that `& $action` looks up — so the local
        rebinding did not shadow an enclosing `$action = { … }` seam and the invocation was licensed
        while the *runtime* resolved it to the string. #1509 round 3 measured that with `$local:`,
        `$script:`, `$global:` and `$private:`: the checker exited 0 and the process really ran the
        external command.
    #>
    param([Parameter(Mandatory)] [System.Management.Automation.VariablePath] $VariablePath)

    $userPath = [string] $VariablePath.UserPath
    $separator = $userPath.IndexOf(':', [System.StringComparison]::Ordinal)
    if ($separator -lt 0) { return $userPath }

    $qualifier = $userPath.Substring(0, $separator)
    if (@($seamScopeQualifiers | Where-Object { [string]::Equals($_, $qualifier, [System.StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
        return $null
    }

    return $userPath.Substring($separator + 1)
}

function Get-ScopedSeamBindings {
    <#
        The variables one scope binds, split into "bound at all" and "proven to hold a script block".

        Both halves are needed because lookup has to model shadowing, not just visibility: a function
        that writes `$action = 'dotnet'` owns a *local* $action, so an enclosing `$action = { … }`
        seam is not what `& $action` reaches at run time and must not license it either.

        "Binds" is enumerated, not assumed (#1509 round 3). PowerShell spells a binding in more ways
        than `=`, and the first version of this function only knew AssignmentStatementAst, so
        `foreach ($a in @('dotnet')) { & $a }`, `$local:a = 'dotnet'; & $a` and the `$script:` /
        `$global:` / `$private:` spellings all failed to shadow an enclosing seam — measured, checker
        exit 0 with the external command actually executing. Covered now:

          =  and compound assignment      AssignmentStatementAst
          param() and inline parameters   ParameterAst (attributed to the function body)
          foreach ($x in …)               ForEachStatementAst.Variable
          $local:/$script:/$global:/…     normalized by Get-SeamBindingName above
          data $x { … }                   DataStatementAst.Variable
          Set-Variable / New-Variable     with a literal -Name (or literal first positional)

        Not covered, and pinned as such by executable cases in
        scripts/tests/script-governance-scan-boundary.Tests.ps1: a Set-Variable/New-Variable whose
        name is computed at run time, and $ExecutionContext.SessionState.PSVariable.Set(…). The
        automatic `$_` is not a binding this function has to model — `& $_` names nothing this file
        can prove, so it is a violation under every spelling (switch, catch, ForEach-Object), which
        those cases also pin.

        Only an *unqualified* declaration can enter the Seam half. `$script:x = { … }` therefore
        shadows an outer `x` without licensing `& $x`, and `& $script:x` proves nothing at all. That
        is deliberately stricter than before this change rather than equally permissive: the
        qualified spellings are unused in this repository, and a rule that only ever removes
        permissions cannot smuggle in a relaxation while claiming to fix a hole.
    #>
    param([Parameter(Mandatory)] [System.Management.Automation.Language.ScriptBlockAst] $Scope)

    # PowerShell variable names are case-insensitive, so the sets are too.
    $bindings = [pscustomobject]@{
        Bound = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        Seam = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    }

    # An inline parameter list is not inside the body's subtree, so FindAll over the scope cannot see
    # it; it is added explicitly, and Get-NearestScriptBlockScope above agrees it belongs here.
    $parameters = @($Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.ParameterAst] }, $true))
    if ($Scope.Parent -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        [object]::ReferenceEquals($Scope.Parent.Body, $Scope) -and
        $null -ne $Scope.Parent.Parameters) {
        $parameters += @($Scope.Parent.Parameters)
    }

    foreach ($parameter in $parameters) {
        if ((Get-NearestScriptBlockScope -Node $parameter) -ne $Scope) { continue }
        $parameterName = Get-SeamBindingName -VariablePath $parameter.Name.VariablePath
        if ($null -eq $parameterName) { continue }
        [void] $bindings.Bound.Add($parameterName)
        if ($null -ne $parameter.StaticType -and $parameter.StaticType -eq [scriptblock]) {
            [void] $bindings.Seam.Add($parameterName)
        }
    }

    # Iteration variables (`foreach ($a in …)`) and `data $a { … }` bind a name for the enclosing
    # scope without ever being an AssignmentStatementAst.
    foreach ($iteration in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.ForEachStatementAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $iteration) -ne $Scope) { continue }
        if ($null -eq $iteration.Variable) { continue }
        $iterationName = Get-SeamBindingName -VariablePath $iteration.Variable.VariablePath
        if ($null -ne $iterationName) { [void] $bindings.Bound.Add($iterationName) }
    }

    foreach ($dataStatement in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.DataStatementAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $dataStatement) -ne $Scope) { continue }
        if ([string]::IsNullOrWhiteSpace([string] $dataStatement.Variable)) { continue }
        [void] $bindings.Bound.Add([string] $dataStatement.Variable)
    }

    # Set-Variable / New-Variable bind through a cmdlet. Only a literal name is resolvable here; a
    # computed one is the documented residual.
    foreach ($binder in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $binder) -ne $Scope) { continue }
        $binderName = [string] $binder.GetCommandName()
        if (@('set-variable', 'new-variable', 'sv', 'nv') |
                Where-Object { [string]::Equals($_, $binderName.ToLowerInvariant(), [System.StringComparison]::Ordinal) } |
                Select-Object -First 1) {
            $elements = @($binder.CommandElements)
            $nameArgument = $null
            for ($index = 1; $index -lt $elements.Count; $index++) {
                $element = $elements[$index]
                if ($element -is [System.Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string] $element.ParameterName, 'Name', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $nameArgument = if ($null -ne $element.Argument) { $element.Argument } elseif (($index + 1) -lt $elements.Count) { $elements[$index + 1] } else { $null }
                    break
                }
                if ($null -eq $nameArgument -and $element -isnot [System.Management.Automation.Language.CommandParameterAst]) {
                    $nameArgument = $element
                }
            }
            if ($nameArgument -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
                $literalName = [string] $nameArgument.Value
                if (-not [string]::IsNullOrWhiteSpace($literalName)) {
                    [void] $bindings.Bound.Add($literalName.TrimStart('$'))
                }
            }
        }
    }

    foreach ($assignment in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $assignment) -ne $Scope) { continue }
        if ($assignment.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { continue }
        $assignedName = Get-SeamBindingName -VariablePath $assignment.Left.VariablePath
        if ($null -eq $assignedName) { continue }
        [void] $bindings.Bound.Add($assignedName)
        # Only an unqualified declaration can prove a seam; see the note in the docstring.
        $isUnqualifiedDeclaration = [string]::Equals(
            [string] $assignment.Left.VariablePath.UserPath, $assignedName, [System.StringComparison]::Ordinal)
        $right = $assignment.Right
        if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
        # `{ … }.GetNewClosure()` is the same literal with its variables captured — still provably a
        # script block, and the idiom the telemetry simulator uses to freeze injected actions.
        # OrdinalIgnoreCase: a .NET member name is matched case-insensitively by PowerShell, but the
        # comparison itself must be ordinal so no ignorable character folds into the name.
        if ($right -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            $right.Expression -is [System.Management.Automation.Language.ScriptBlockExpressionAst] -and
            [string]::Equals([string] $right.Member.Value, 'GetNewClosure', [System.StringComparison]::OrdinalIgnoreCase)) {
            $right = $right.Expression
        }
        if ($isUnqualifiedDeclaration -and $right -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) {
            [void] $bindings.Seam.Add($assignedName)
        }
    }

    return $bindings
}

function Test-IsSeamVariableInvocation {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Invocation,
        [Parameter(Mandatory)] [string] $VariableName,
        [Parameter(Mandatory)] [object] $ScopeCache
    )

    $scope = Get-NearestScriptBlockScope -Node $Invocation
    while ($null -ne $scope) {
        if (-not $ScopeCache.ContainsKey($scope)) {
            $ScopeCache[$scope] = Get-ScopedSeamBindings -Scope $scope
        }
        $bindings = $ScopeCache[$scope]
        if ($bindings.Bound.Contains($VariableName)) {
            # Innermost binding wins, the way PowerShell resolves the name at run time. Without this,
            # a file-level `$action = { … }` licensed every function body's `$action = 'dotnet';
            # & $action` — a leak the documented residual ("re-assigned later *in the same scope*")
            # never covered.
            return $bindings.Seam.Contains($VariableName)
        }
        $scope = Get-NearestScriptBlockScope -Node $scope
    }

    return $false
}

function Test-ScriptGovernance {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath,

        [Parameter(Mandatory)]
        [hashtable] $Baseline
    )

    $relativePath = Get-RepoRelativePath -CandidatePath $ScriptPath
    $isLibrary = $relativePath -like $libraryScopePattern
    $violations = New-Object System.Collections.Generic.List[object]
    $content = Get-Content $ScriptPath -Raw

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $parseErrors)

    foreach ($parseError in $parseErrors) {
        Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'ParseError' -Line $parseError.Extent.StartLineNumber -Message $parseError.Message
    }

    if ($parseErrors.Count -gt 0) {
        return $violations
    }

    if ($content -notmatch '(?m)^\s*#\s*Script-Governance:\s*$') {
        Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingGovernanceHeader' -Message 'Missing Script-Governance header block.'
    }

    $categoryMatch = [regex]::Match($content, '(?m)^\s*#\s*Category:\s*(?<category>[A-Za-z-]+(?:\s*,\s*[A-Za-z-]+)*)\s*$')
    if (-not $categoryMatch.Success) {
        Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingCategory' -Message 'Missing Script-Governance Category.'
    }
    else {
        $categories = @($categoryMatch.Groups['category'].Value -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() })
        foreach ($category in $categories) {
            if ($allowedCategories -notcontains $category) {
                Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'InvalidCategory' -Message "Invalid Script-Governance Category '$category'."
            }
        }

        # Library scope is declared, not only inferred from the path. A file that relaxes two rules
        # for itself has to say so in its own header, and a file outside scripts/lib cannot claim
        # the relaxation by mislabelling itself.
        # Ordinal: the categories were lowercased above, so this is an exact identifier match and
        # `-contains` would compare culture-aware (#1507 ruling on identifier comparison).
        $declaresLibrary = @($categories | Where-Object { [string]::Equals([string] $_, 'library', [System.StringComparison]::Ordinal) }).Count -gt 0
        if ($isLibrary -and -not $declaresLibrary) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingLibraryCategory' -Message 'A dot-sourced library under scripts/lib/ must declare Script-Governance Category library.'
        }
        elseif (-not $isLibrary -and $declaresLibrary) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'InvalidCategory' -Message "Script-Governance Category 'library' is only for dot-sourced libraries under scripts/lib/."
        }
    }

    $commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
    $dotSourcesHelper = $false
    # Keyed by AST node identity, so a scope is walked once per file rather than once per invocation.
    $scriptBlockScopeCache = [System.Collections.Generic.Dictionary[System.Management.Automation.Language.Ast, object]]::new()

    foreach ($command in $commands) {
        $commandName = $command.GetCommandName()
        $line = $command.Extent.StartLineNumber

        if (
            $command.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Dot -and
            $command.Extent.Text -match 'ScriptAutomation\.ps1'
        ) {
            $dotSourcesHelper = $true
        }

        if ($command.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand) {
            # Entry-point scripts: no `&` at all. Libraries: only the injected-action seam, i.e. a
            # variable this invocation's own scope chain proves holds a script block. `& 'dotnet'`,
            # `& "$exe"`, `& (Get-Command …)` and `& $stringVariable` all remain violations, so the
            # rule still covers the case ForbiddenCommand cannot see — including the case where a
            # sibling function happens to use the same variable name for a real script block.
            $target = @($command.CommandElements)[0]
            $isSeamInvocation = $isLibrary -and
                $target -is [System.Management.Automation.Language.VariableExpressionAst] -and
                (Test-IsSeamVariableInvocation -Invocation $command -VariableName ([string] $target.VariablePath.UserPath) -ScopeCache $scriptBlockScopeCache)
            if (-not $isSeamInvocation) {
                $message = if ($isLibrary) {
                    "A library may only invoke a script block proven in this scope (a [scriptblock] parameter or a `{ … }` assignment in the same or an enclosing block): $($command.Extent.Text)"
                }
                else {
                    "Dynamic invocation is not allowed outside ScriptAutomation.ps1: $($command.Extent.Text)"
                }
                Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'DynamicInvocation' -Line $line -Message $message
            }
            continue
        }

        if ([string]::IsNullOrWhiteSpace($commandName)) {
            continue
        }

        if ($forbiddenCommands -contains $commandName.ToLowerInvariant()) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'ForbiddenCommand' -Line $line -Message "Direct command '$commandName' must be wrapped by ScriptAutomation.ps1."
        }
    }

    # MissingHelper is an entry-point rule. A library is dot-sourced into a caller that has already
    # loaded the wrapper, and several libraries (BackendTestShardSelectors, CiWorkflowBudgets) invoke
    # no external process at all — forcing an unused import on them would buy nothing. What the rule
    # is really there to prevent, a library shelling out around the wrapper, stays covered:
    # ForbiddenCommand, ForbiddenProcessStart and the narrowed DynamicInvocation above all apply in
    # library scope, and libraries that do shell out (BackendTestShardTimings, FullStackSessionRuntime)
    # dot-source the wrapper for their own sake.
    if (-not $dotSourcesHelper -and -not $isLibrary) {
        Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingHelper' -Message 'Script must dot-source scripts/lib/ScriptAutomation.ps1.'
    }

    $memberInvocations = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true)
    foreach ($memberInvocation in $memberInvocations) {
        $extent = $memberInvocation.Extent.Text
        $line = $memberInvocation.Extent.StartLineNumber

        if ($extent -match '(?i)\[scriptblock\]\s*::\s*Create') {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'ForbiddenDynamicScriptBlock' -Line $line -Message '[scriptblock]::Create is not allowed.'
        }

        if ($extent -match '(?i)\[System\.Diagnostics\.Process\]\s*::\s*Start') {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'ForbiddenProcessStart' -Line $line -Message 'System.Diagnostics.Process.Start must be wrapped by ScriptAutomation.ps1.'
        }
    }

    return $violations
}

$baseline = Get-GovernanceBaseline -InputBaselinePath $BaselinePath
$allViolations = New-Object System.Collections.Generic.List[object]

foreach ($script in Get-GovernanceScripts -InputPaths $Path) {
    foreach ($violation in Test-ScriptGovernance -ScriptPath $script -Baseline $baseline) {
        if (Test-IsExempted -Baseline $baseline -Path $violation.Path -Rule $violation.Rule) {
            continue
        }

        $allViolations.Add($violation)
    }
}

if ($allViolations.Count -gt 0) {
    Write-Host 'Script governance check failed:'
    foreach ($violation in $allViolations) {
        Write-Host "  $($violation.Path):$($violation.Line) [$($violation.Rule)] $($violation.Message)"
    }

    exit 1
}

Write-Host 'Script governance check passed.'
exit 0
