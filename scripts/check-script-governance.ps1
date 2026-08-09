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
# `$global:a`, `$private:a` and `$variable:a` all bind the name `a` — all five measured by reading
# the plain `$a` back out afterwards — while `$env:a` and `$function:a` are provider drives and are
# not variable bindings at all, so a name carrying one of those is reported as unresolvable (which
# fails closed: `& $env:x` proves nothing and stays a violation).
#
# `using` is deliberately absent: `$using:a = 1` is a *parse error* on PowerShell 7 ("the input to an
# assignment operator must be an object that is able to accept assignments"), so listing it bought a
# branch no source can reach. #1509 round 4 measured that; it was carried here as a dead entry while
# the governance document ticked it off alongside the five real ones.
$seamScopeQualifiers = @('local', 'script', 'global', 'private', 'variable')

# The cmdlets that bind a variable by name, mapped to the cmdlet whose parameter metadata decides
# how their command elements pair up.
#
# Only the *canonical* names are written down; every other spelling is derived (#1509 round 7).
# Round 6 took the parameter-pairing half of this walk from cmdlet metadata but left the
# command-name half a hand-written list of `set-variable`/`sv`/`new-variable`/`nv`, and the review
# then measured two whole classes walking straight through it — checker exit 0 with the external
# command really running in a live process:
#
#   set -Scope Local action '/bin/echo'                        `set` is a shipped alias of Set-Variable
#   SET -Name action -Value '/bin/echo'                        …and command names ignore case
#   Microsoft.PowerShell.Utility\Set-Variable -Scope Local …   module-qualified spelling
#
# Half an enumeration is still a hand list: the same failure mode the Left-shape walk was rewritten
# to escape. So the aliases come from PowerShell's own alias table and the qualifier is normalized
# away in Resolve-SeamBinderCanonicalName below.
$seamBinderCanonicalNames = @('Set-Variable', 'New-Variable')

# Deliberately case-*insensitive*, and that is a ruling rather than an oversight: PowerShell resolves
# command names without regard to case, so `SET -Name action -Value 'dotnet'` and
# `microsoft.powershell.utility\SET-VARIABLE …` really do bind (both measured on pwsh 7.6.4) and the
# lookup has to see them. The comparison is still *ordinal* — OrdinalIgnoreCase folds case and
# nothing else — so no ignorable character can fold a foreign command name into one of these, per the
# ordinal ruling this PR applies everywhere else.
$seamBinderCommands = [System.Collections.Hashtable]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($seamBinderCanonicalName in $seamBinderCanonicalNames) {
    $seamBinderCommands[$seamBinderCanonicalName] = $seamBinderCanonicalName
    foreach ($seamBinderAlias in @(Get-Alias -Definition $seamBinderCanonicalName -ErrorAction SilentlyContinue)) {
        $seamBinderCommands[[string] $seamBinderAlias.Name] = $seamBinderCanonicalName
    }
}

# `Get-Alias` reads *session* state, and session state can only ever make this checker blinder: `set`
# ships with `Options = None`, so a profile — or anything else that ran first — may remove or
# reassign it, while the file being scanned still runs elsewhere with the alias intact. The shipped
# aliases are therefore also declared as a floor and unioned in. This is a lower bound on
# recognition, never the recognition list: adding a name here can only ever turn a pass into a
# report, and the discovery above is what keeps the list from being the recognition rule again.
# `binder-name-set-is-discovered` and `binder-alias-removed-from-session` in
# scripts/tests/script-governance-scan-boundary.Tests.ps1 pin the two halves separately.
foreach ($seamBinderFloorEntry in @(
        @{ Alias = 'set'; Canonical = 'Set-Variable' },
        @{ Alias = 'sv'; Canonical = 'Set-Variable' },
        @{ Alias = 'nv'; Canonical = 'New-Variable' })) {
    if (-not $seamBinderCommands.ContainsKey($seamBinderFloorEntry.Alias)) {
        $seamBinderCommands[$seamBinderFloorEntry.Alias] = $seamBinderFloorEntry.Canonical
    }
}

# One Get-Command per canonical cmdlet, not per call site.
$seamBinderParameterCache = [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal)

function Resolve-SeamBinderCanonicalName {
    <#
        The binder cmdlet a written command name refers to, or $null when it names something else.

        Module qualification is stripped at the last `\`. `Microsoft.PowerShell.Utility\Set-Variable
        -Scope Local action '/bin/echo'` binds `action` and really runs (measured), and it is exactly
        what a script writes when it wants to be explicit about which module it means — so the
        qualified spelling has to reach the same table as the bare one.

        A module-qualified *alias* (`Microsoft.PowerShell.Utility\sv`) is the one case where this
        over-reports: PowerShell qualifies exported commands only, the shipped aliases belong to no
        module, and the call throws CommandNotFoundException before binding anything (measured).
        Treating it as a binder therefore reports a shadowing that run time would not produce — the
        fail-closed direction, taken deliberately rather than paying for a second rule about which
        halves of a qualified name pair up. The same is true of a relative path that happens to end
        in one of these names; it can only ever cost a permission, never grant one.
    #>
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $WrittenName)

    if ([string]::IsNullOrEmpty($WrittenName)) { return $null }

    $separator = $WrittenName.LastIndexOf('\', [System.StringComparison]::Ordinal)
    $bareName = if ($separator -lt 0) { $WrittenName } else { $WrittenName.Substring($separator + 1) }
    if (-not $seamBinderCommands.ContainsKey($bareName)) { return $null }

    return [string] $seamBinderCommands[$bareName]
}

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

function Get-SeamAssignmentTargets {
    <#
        Every variable name an assignment's left-hand side binds, with whether that spelling is
        allowed to *prove* a seam.

        This is written as an exhaustive walk over the AST shapes `AssignmentStatementAst.Left` can
        take, not as a list of the spellings someone happened to report. Three rounds of #1509 each
        added the two or three spellings the previous review had named, and each time the next review
        found another (round 2: parameters; round 3: `foreach` and scope qualifiers; round 4:
        multiple and type-constrained assignment) — the enumeration has to come from the type
        hierarchy or it stays one review behind.

        Left is one of exactly these, measured by parsing each spelling on PowerShell 7:

          $a = …                      VariableExpressionAst      binds, may prove a seam
          $a, $b = …                  ArrayLiteralAst            binds each element
          [string] $a = …             ConvertExpressionAst       binds the child
          [ValidateNotNull()] $a = …  AttributedExpressionAst    binds the child (Convert's base type)
          ($a) = … / ($a, $b) = …     ParenExpressionAst         binds through the wrapped pipeline
          $h['k'] = …                 IndexExpressionAst         binds nothing — see below
          $o.P = …                    MemberExpressionAst        binds nothing — see below

        Index and member assignment are skipped because *at the syntax level* they are not variable
        bindings: `$a['k'] = …` / `$a.P = …` name a member of whatever `$a` refers to, and there is no
        binding of the name `a` for this function to record. Counting them would report a violation
        with nothing behind it, so they are skipped explicitly rather than by falling off the end, and
        pinned by `index-assignment-is-not-a-binding` / `member-assignment-is-not-a-binding`.

        What this is *not* is a claim that the variable therefore still holds the seam at run time.
        It does not follow, and it is false as measured (#1509 round 5, pwsh 7.6.4):

            $a = { 'seam' }; $r = [ref] $a; $r.Value = '/bin/echo'; & $a   # runs /bin/echo

        `[ref] $a` and `Get-Variable a` both hand out the live PSVariable, and writing `.Value` on it
        replaces the binding — the very member-assignment spelling this arm skips. `[ref]` is in real
        use in scripts/lib (18 occurrences, all `TryParse`/`ParseFile` out-parameters). Run-time
        rebinding through a PSVariable handle is therefore a *registered residual* of this static
        checker, listed with the other residuals in Get-ScopedSeamBindings below and pinned by
        `residual-ref-rebinding`; it is not something the skip is safe because of.

        Only a bare, unqualified VariableExpressionAst may prove a seam. Every wrapper form binds the
        name — so it shadows an enclosing seam — but proves nothing, which keeps this change
        monotone in the same direction as round 3's qualifier ruling: it only ever removes
        permissions. It is also the honest reading for the one wrapper where the answer is knowable
        and *negative*: `[string] $a = { … }` binds a string, not a script block.
    #>
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Left,
        [bool] $CanProveSeam = $true
    )

    $targets = [System.Collections.Generic.List[object]]::new()

    if ($Left -is [System.Management.Automation.Language.VariableExpressionAst]) {
        $name = Get-SeamBindingName -VariablePath $Left.VariablePath
        if ($null -eq $name) { return $targets }
        $isUnqualified = [string]::Equals(
            [string] $Left.VariablePath.UserPath, $name, [System.StringComparison]::Ordinal)
        $targets.Add([pscustomobject]@{ Name = $name; CanProveSeam = ($CanProveSeam -and $isUnqualified) })
        return $targets
    }

    if ($Left -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        foreach ($element in @($Left.Elements)) {
            foreach ($target in @(Get-SeamAssignmentTargets -Left $element -CanProveSeam $false)) {
                $targets.Add($target)
            }
        }
        return $targets
    }

    # ConvertExpressionAst derives from AttributedExpressionAst, so this arm covers both.
    if ($Left -is [System.Management.Automation.Language.AttributedExpressionAst]) {
        foreach ($target in @(Get-SeamAssignmentTargets -Left $Left.Child -CanProveSeam $false)) {
            $targets.Add($target)
        }
        return $targets
    }

    if ($Left -is [System.Management.Automation.Language.ParenExpressionAst]) {
        $pipeline = $Left.Pipeline
        if ($pipeline -is [System.Management.Automation.Language.PipelineAst] -and
            @($pipeline.PipelineElements).Count -eq 1 -and
            @($pipeline.PipelineElements)[0] -is [System.Management.Automation.Language.CommandExpressionAst]) {
            foreach ($target in @(Get-SeamAssignmentTargets -Left (@($pipeline.PipelineElements)[0]).Expression -CanProveSeam $false)) {
                $targets.Add($target)
            }
        }
        return $targets
    }

    # IndexExpressionAst / MemberExpressionAst: not a variable binding, by the reasoning in the
    # docstring. *Anything else* reaching here would bind nothing either — which is fail-open, not a
    # decision: a Left shape nobody enumerated would silently stop shadowing an enclosing seam. The
    # seven shapes above are all a pwsh 7.6.4 parser can put here (measured over a spelling corpus),
    # but that is a fact about today's parser, not an invariant this function enforces. It is held by
    # `Get-SeamAssignmentTargets Left type set` in
    # scripts/tests/script-governance-scan-boundary.Tests.ps1, which goes red both if a branch here is
    # deleted and if the AST assembly grows an expression type nobody has ruled on.
    return $targets
}

function Get-SeamBinderParameters {
    <#
        The binder cmdlet's own parameter table: every parameter name and alias mapped to the
        canonical parameter and to whether it consumes the *following* command element.

        Read from the cmdlet rather than hand-listed (#1509 round 6). Which spellings take a value is
        a fact about Set-Variable/New-Variable, and the two directions of getting it wrong are both
        fail-open: skip an element after a switch (`-Force action 'dotnet'`) and the positional name
        is lost, skip nothing after `-Scope` and the *value* `Local` is read as the name. Both were
        measured; `Get-SeamBinderParameters` is what makes the pairing agree with the binder instead
        of with whichever spelling a review happened to name.

        A missing cmdlet is a hard failure rather than a shrug: the alternative is silently reading
        every binder call as "binds nothing", which is exactly the hole this function closes.
    #>
    param([Parameter(Mandatory)] [string] $CanonicalName)

    if ($seamBinderParameterCache.ContainsKey($CanonicalName)) { return $seamBinderParameterCache[$CanonicalName] }

    $command = Get-Command -Name $CanonicalName -CommandType Cmdlet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "Script governance cannot resolve the '$CanonicalName' cmdlet, so it cannot tell which of its parameters consume the next command element. Refusing to guess."
    }

    # PowerShell matches parameter names case-insensitively, so the table is too; the comparison
    # itself stays ordinal so no ignorable character folds into a parameter name.
    $parameters = [System.Collections.Hashtable]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($parameter in $command.Parameters.Values) {
        $entry = [pscustomobject]@{
            Name = [string] $parameter.Name
            TakesValue = ($parameter.ParameterType -ne [switch])
        }
        $parameters[[string] $parameter.Name] = $entry
        foreach ($alias in @($parameter.Aliases)) {
            if ([string]::IsNullOrWhiteSpace([string] $alias)) { continue }
            $parameters[[string] $alias] = $entry
        }
    }

    $seamBinderParameterCache[$CanonicalName] = $parameters
    return $parameters
}

function Resolve-SeamBinderParameter {
    <#
        The parameter a written `-Foo` binds to, by PowerShell's own rules: an exact name or alias,
        otherwise a prefix that resolves to exactly one parameter.

        $null when nothing matches or a prefix is ambiguous. Both make the *command* fail at run time
        — measured on 7.6.4: `Set-Variable -Bogus x 'y'` → NamedParameterNotFound,
        `Set-Variable -V x 'y'` → AmbiguousParameter — so such a call binds no name at all and
        recording nothing for it is exact rather than permissive. (`-Sc Local zz 'y'` resolves and
        really binds `zz`, which is why prefixes are resolved instead of rejected.)
    #>
    param(
        [Parameter(Mandatory)] [hashtable] $Parameters,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Written
    )

    if ([string]::IsNullOrEmpty($Written)) { return $null }
    if ($Parameters.ContainsKey($Written)) { return $Parameters[$Written] }

    $prefixMatches = @($Parameters.Keys | Where-Object { ([string] $_).StartsWith($Written, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($prefixMatches.Count -eq 0) { return $null }
    $resolvedNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]] @($prefixMatches | ForEach-Object { [string] $Parameters[$_].Name }),
        [System.StringComparer]::OrdinalIgnoreCase)
    if ($resolvedNames.Count -ne 1) { return $null }
    return $Parameters[$prefixMatches[0]]
}

function Get-SeamBinderNameArgument {
    <#
        The AST node a `Set-Variable`/`New-Variable` call passes as its -Name, or $null when the call
        binds no name this checker can resolve.

        The walk is the parameter binder's own pairing, not "the first element that is not a
        parameter" (#1509 round 6 measured that reading exiting 0 on `Set-Variable -Scope Local
        action '/bin/echo'`, `Set-Variable -Value 'dotnet' action` and the `sv` alias — with the
        external command really running in a live process): a named parameter that takes a value
        swallows the element after it, so the first *unconsumed* element is the positional name.
        A switch swallows nothing, which is why `-Force action 'dotnet'` still resolves to `action`.
    #>
    param([Parameter(Mandatory)] [System.Management.Automation.Language.CommandAst] $Binder)

    # GetCommandName() is $null whenever the command name is an expression (`& $action build`), which
    # is most of what this walk sees.
    $binderName = [string] $Binder.GetCommandName()
    $canonicalName = Resolve-SeamBinderCanonicalName -WrittenName $binderName
    if ([string]::IsNullOrEmpty([string] $canonicalName)) { return $null }

    $parameters = Get-SeamBinderParameters -CanonicalName $canonicalName
    $elements = @($Binder.CommandElements)
    $nameArgument = $null
    $index = 1
    while ($index -lt $elements.Count) {
        $element = $elements[$index]
        if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) {
            # Splatting (`Set-Variable @splat`) lands here as a variable, not a literal, so it falls
            # out as "no resolvable name" — the registered residual, not a silent pass.
            if ($null -eq $nameArgument) { $nameArgument = $element }
            $index++
            continue
        }

        $resolved = Resolve-SeamBinderParameter -Parameters $parameters -Written ([string] $element.ParameterName)
        # Unresolvable or ambiguous: the call throws before binding anything, so neither the
        # positional candidate collected so far nor anything after it is a binding.
        if ($null -eq $resolved) { return $null }
        if ([string]::Equals([string] $resolved.Name, 'Name', [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($null -ne $element.Argument) { return $element.Argument }
            if (($index + 1) -lt $elements.Count) { return $elements[$index + 1] }
            return $null
        }
        $index += if ($resolved.TakesValue -and $null -eq $element.Argument) { 2 } else { 1 }
    }

    return $nameArgument
}

function Get-SeamBinderLiteralNames {
    <#
        Every variable name a `-Name` argument spells literally — plural, because `-Name` is plural.

        `Set-Variable`'s -Name is declared `[string[]]` (measured from `Get-Command`, and pinned by
        the `binder parameter collection types` contract), so one argument can spell several
        bindings. Reading only a `StringConstantExpressionAst` therefore dropped the whole binding
        for every multi-name call — #1509 round 8 measured `Set-Variable action,zz '/bin/echo'` and
        `Set-Variable -Name action,zz -Value '/bin/echo'` both exiting 0 while the invocation really
        executed the external command. That is not the registered "computed name" residual: those
        are names that only exist at run time, and `action,zz` is two static literals.

        This is the same failure this file has now hit on five axes — AST node type, operator,
        parameter pairing, command-name resolution, and now the *collectiveness of the parameter
        type* — so the walk is again an enumeration over AST shapes rather than a list of reported
        spellings. Measured on pwsh 7.6.4, each of these really binds every name shown:

          action                  StringConstantExpressionAst  the leaf; bareword, '…' and "…" alike
          action,zz               ArrayLiteralAst              each element, recursively
          ('action','zz')         ParenExpressionAst           grouping; wraps an ArrayLiteralAst
          @('action','zz')        ArrayExpressionAst           array construction over a statement block
          $('action')             SubExpressionAst             the same shape with `$(`

        The line is *literal*, not *constant-foldable*: this checker does not fold, so
        `-Name ([string] 'action')` and `-Name ('act' + 'ion')` stay the registered residual
        (`residual-set-variable-non-literal-name-expression`) alongside `-Name $computed`. A mixed
        list (`-Name action,$computed`) yields its literal elements and drops the rest, which is
        exact for the literal half and the existing residual for the other — recording nothing for
        the whole call would be fail-open on a name that is written right there.

        `New-Variable`'s -Name is a scalar `[string]`, so a multi-name argument does not bind
        several names there — it throws (`CannotConvertArgument` named, "positional parameter cannot
        be found" positional; both measured). Expanding it anyway therefore *over*-reports on
        `New-Variable`, which is the fail-closed direction and the same trade already taken for the
        module-qualified alias: one rule instead of two, and it can only ever cost a permission.
        `new-variable-multiple-literal-names-over-reported` records that as a decision.
    #>
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Argument)

    $names = [System.Collections.Generic.List[string]]::new()

    if ($Argument -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
        $names.Add([string] $Argument.Value)
        return $names
    }

    if ($Argument -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        foreach ($element in @($Argument.Elements)) {
            foreach ($elementName in @(Get-SeamBinderLiteralNames -Argument $element)) { $names.Add($elementName) }
        }
        return $names
    }

    # Grouping and array-construction syntax: `(…)`, `@(…)`, `$(…)`. These carry a value through
    # without computing one, so they are transparent here; anything that computes is not.
    $groupedStatements = $null
    if ($Argument -is [System.Management.Automation.Language.ParenExpressionAst]) {
        $groupedStatements = @($Argument.Pipeline)
    }
    elseif ($Argument -is [System.Management.Automation.Language.ArrayExpressionAst] -or
        $Argument -is [System.Management.Automation.Language.SubExpressionAst]) {
        $groupedStatements = @($Argument.SubExpression.Statements)
    }

    if ($null -ne $groupedStatements) {
        foreach ($statement in $groupedStatements) {
            if ($statement -isnot [System.Management.Automation.Language.PipelineAst]) { continue }
            foreach ($pipelineElement in @($statement.PipelineElements)) {
                if ($pipelineElement -isnot [System.Management.Automation.Language.CommandExpressionAst]) { continue }
                foreach ($groupedName in @(Get-SeamBinderLiteralNames -Argument $pipelineElement.Expression)) {
                    $names.Add($groupedName)
                }
            }
        }
        return $names
    }

    # Everything else is the registered residual: the name is not written as a literal, so this file
    # cannot say what it is. Falling off the end here is fail-open by construction, which is why the
    # dispatch set above is asserted structurally by `Get-SeamBinderLiteralNames dispatch set` in
    # scripts/tests/script-governance-scan-boundary.Tests.ps1 rather than left to the case list.
    return $names
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

          =  and compound assignment      AssignmentStatementAst, every Left shape — see
                                          Get-SeamAssignmentTargets, which walks the type hierarchy
                                          instead of listing spellings (round 4 found two more:
                                          `$a, $b = …` and `[string] $a = …`)
          param() and inline parameters   ParameterAst (attributed to the function body)
          foreach ($x in …)               ForEachStatementAst.Variable
          $local:/$script:/$global:/…     normalized by Get-SeamBindingName above
          data $x { … }                   DataStatementAst.Variable
          Set-Variable / New-Variable     with a literal -Name (or literal first positional), and
                                          *every* name it spells — `-Name` is `[string[]]`, so
                                          `action,zz`, `('a','b')`, `@('a','b')` and `$('a')` each
                                          bind more than the one literal an earlier version read
                                          (round 8). See Get-SeamBinderLiteralNames.

        Not covered — the registered residuals. Each was measured on pwsh 7.6.4 exiting 0 here *and*
        really executing the external command, and each has an executable case in
        scripts/tests/script-governance-scan-boundary.Tests.ps1 asserting that it is currently
        permitted, so the list and the behaviour cannot drift apart in either direction:

          Set-Variable/New-Variable -Name $computed  name exists only at run time
          -Name ([string] 'a') / -Name ('a' + 'b')   statically computable but not *written* as a
                                                     literal; this checker does not constant-fold
          $ExecutionContext.SessionState.PSVariable.Set(…)          ditto
          [ref] $a / Get-Variable a, then .Value = …  rebinding through the live PSVariable handle;
                                                     spelled as member assignment, which the Left
                                                     walk above skips by design
          -PipelineVariable a, consumed downstream    the binding is created by the pipeline
                                                     processor, not by any AST node in this file
          -OutVariable a                             ditto, and it survives the pipeline
          $script:a = … in one function, & $a in     the write and the read are in different
          another                                    ScriptBlockAst scopes (see below)

        What they have in common is that the name is bound somewhere this file's AST does not say it
        is bound. They are accepted rather than chased because each is markedly more deliberate than
        the spellings above; closing them needs data-flow, not another Left shape.

        The automatic `$_` is not a binding this function has to model — `& $_` names nothing this
        file can prove, so it is a violation under every spelling (switch, catch, ForEach-Object),
        which those cases also pin.

        Only an *unqualified* declaration can enter the Seam half. `$script:x = { … }` therefore
        shadows an outer `x` without licensing `& $x` **in the scope that writes it**, and
        `& $script:x` proves nothing at all. Scope-qualified writes are only modelled where they are
        written: `$script:x = …` inside function A does not shadow anything for function B, even
        though at run time it rebinds the same file-level `x` that B reads — the residual listed
        above. That is deliberately stricter than before this change rather than equally permissive
        wherever it does apply: the qualified spellings are unused in this repository, and a rule
        that only ever removes permissions cannot smuggle in a relaxation while claiming to fix a
        hole.
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

    # Set-Variable / New-Variable bind through a cmdlet. Which element carries the name is decided by
    # the cmdlet's own parameter binder (Get-SeamBinderNameArgument); how many names that element
    # spells is decided by Get-SeamBinderLiteralNames, because `Set-Variable -Name` is `[string[]]`
    # and one argument can be a literal list. Only *literal* names are resolvable statically; a
    # computed one, and any element of a list that is computed, is the documented residual.
    foreach ($binder in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $binder) -ne $Scope) { continue }
        $nameArgument = Get-SeamBinderNameArgument -Binder $binder
        if ($null -eq $nameArgument) { continue }
        foreach ($literalName in @(Get-SeamBinderLiteralNames -Argument $nameArgument)) {
            if ([string]::IsNullOrWhiteSpace($literalName)) { continue }
            [void] $bindings.Bound.Add($literalName.TrimStart('$'))
        }
    }

    foreach ($assignment in $Scope.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) {
        if ((Get-NearestScriptBlockScope -Node $assignment) -ne $Scope) { continue }
        # Every shape Left can take, enumerated from the AST type hierarchy rather than from the
        # spellings a review has named so far; see Get-SeamAssignmentTargets.
        $assignmentTargets = @(Get-SeamAssignmentTargets -Left $assignment.Left)
        if ($assignmentTargets.Count -eq 0) { continue }
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
        $rightIsScriptBlock = $right -is [System.Management.Automation.Language.ScriptBlockExpressionAst]
        foreach ($assignmentTarget in $assignmentTargets) {
            [void] $bindings.Bound.Add([string] $assignmentTarget.Name)
            if ($assignmentTarget.CanProveSeam -and $rightIsScriptBlock) {
                [void] $bindings.Seam.Add([string] $assignmentTarget.Name)
            }
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
