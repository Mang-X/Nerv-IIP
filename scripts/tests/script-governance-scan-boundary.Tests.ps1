# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the script governance checker as a real process
#     - Builds a throwaway mirror of the scripts/ tree under the platform temp directory
#   Writes:
#     - <temp>/nerv-iip-script-governance-boundary-<guid>/** (temporarily)
#   Cleanup:
#     - Removes the temporary mirror tree in finally; nothing is ever written inside the repository
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$checker = Join-Path $repoRoot 'scripts/check-script-governance.ps1'

# Split out of check-script-governance.Tests.ps1 so it can gate in CI: that file also exercises the
# ScriptAutomation stream-drain and detached-process fixtures, which are far heavier than a boundary
# contract needs to be. Everything here is the checker run as a process against small fixtures.

# ---------------------------------------------------------------------------------------------
# The scripts/lib scan boundary (#1509). Before this, `scripts/lib/*` was excluded from the default
# scan wholesale, so ForbiddenCommand / DynamicInvocation / ForbiddenProcessStart were not enforced
# on the shared libraries at all. The ruling — libraries are scanned under a declared library scope,
# with MissingHelper dropped and DynamicInvocation narrowed to the injected-action seam — is written
# up in docs/architecture/script-automation-governance.md and guarded here, executably.
#
# Three guards, because a documented boundary with no test is just a comment:
#   1. the exclusion list is asserted verbatim, so widening it is a change to a named contract;
#   2. no exclusion pattern may swallow a real scripts/lib file other than the wrapper itself — a
#      data check against the actual tree, so "the default scan reaches scripts/lib here" is not
#      inferred from the mirror below;
#   3. a deliberately-violating library file is planted in a mirrored scripts/ tree and the *default*
#      scan must report it — re-adding `scripts/lib/*` to the exclusion list turns that scan green
#      and fails this file.
$checkerText = Get-Content -LiteralPath $checker -Raw
$checkerParseErrors = $null
$checkerAst = [System.Management.Automation.Language.Parser]::ParseInput($checkerText, [ref] $null, [ref] $checkerParseErrors)
if ($checkerParseErrors -and $checkerParseErrors.Count -gt 0) {
    throw "Failed to parse the script governance checker: $($checkerParseErrors[0].Message)"
}
$scanExclusionAssignment = $checkerAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
    [string]::Equals([string] $node.Left.VariablePath.UserPath, 'scanExclusions', [StringComparison]::Ordinal)
}, $true)
if (-not $scanExclusionAssignment) {
    throw 'The script governance checker must keep its scan boundary in a named $scanExclusions list.'
}
$declaredExclusions = @(
    $scanExclusionAssignment.Right.FindAll({
        param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { [string] $_.Value }
)
$expectedExclusions = @('scripts/check-script-governance.ps1', 'scripts/lib/ScriptAutomation.ps1', 'scripts/tests/*')
# Ordinal, per the #1507 ruling: `-cne` is still culture-aware, so two exclusion lists differing only
# by an ignorable character would compare equal and this contract would miss the widening.
if (-not [string]::Equals(($declaredExclusions -join '|'), ($expectedExclusions -join '|'), [StringComparison]::Ordinal)) {
    throw "Script governance scan boundary changed: expected [$($expectedExclusions -join ', ')], found [$($declaredExclusions -join ', ')]. Update docs/architecture/script-automation-governance.md and this contract together."
}

# Guard 2, against the real tree: every scripts/lib file except the wrapper must survive the
# exclusion filter. The behavioural default-scan case below runs against a mirror, so this is the
# assertion that ties the ruling to *this* repository's actual library inventory.
$realLibraryFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts/lib') -Recurse -File -Filter '*.ps1' |
        ForEach-Object { ([System.IO.Path]::GetRelativePath($repoRoot, $_.FullName)) -replace '\\', '/' }
)
if ($realLibraryFiles.Count -eq 0) {
    throw 'scripts/lib must contain PowerShell libraries; an empty inventory would make the scan-boundary check vacuous.'
}
$excludedLibraryFiles = @(
    $realLibraryFiles | Where-Object {
        $candidate = $_
        @($expectedExclusions | Where-Object { $candidate -like $_ }).Count -gt 0
    }
)
if (($excludedLibraryFiles -join '|') -ne 'scripts/lib/ScriptAutomation.ps1') {
    throw "Only the wrapper may be excluded from the scripts/lib scan; excluded: [$($excludedLibraryFiles -join ', ')]."
}

# ---------------------------------------------------------------------------------------------
# Get-SeamAssignmentTargets: the Left type set, as a contract rather than as a comment (#1509 round
# 5).
#
# The function ends in `return $targets` for anything it did not recognise, which binds nothing —
# fail *open*: an unenumerated Left shape silently stops shadowing an enclosing seam, and the case
# fixtures below would all stay green because none of them spells that shape. "Only these seven can
# occur" was measured on one parser version; that is a fact about today's PowerShell, not an
# invariant the function enforces, and this whole PR's recurring failure mode has been exactly that
# substitution. So the four things that make it an invariant are asserted here:
#
#   1. the dispatch set in the source is exactly the four types the walk branches on — deleting or
#      renaming a branch turns this red before any behavioural case notices;
#   2. ConvertExpressionAst really is a subclass of AttributedExpressionAst, which is the only reason
#      four branches cover five named shapes;
#   3. parsing a corpus of assignment spellings yields exactly the seven documented Left types, and
#      every one of them is either dispatched on or explicitly ruled a non-binding — no shape is
#      handled by falling off the end;
#   4. the AST assembly contains no *other* concrete expression type that has never been ruled on.
#      Only additions are failures: a type disappearing on a different runtime cannot introduce an
#      unhandled Left shape, so the assertion is one-sided on purpose and does not go red merely
#      because CI's pwsh differs from a developer's.
$seamTargetsFunction = $checkerAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    [string]::Equals([string] $node.Name, 'Get-SeamAssignmentTargets', [StringComparison]::OrdinalIgnoreCase)
}, $true)
if (-not $seamTargetsFunction) {
    throw 'The script governance checker must keep its Left-shape walk in a function named Get-SeamAssignmentTargets.'
}
# Only `-is` tests applied to the $Left parameter itself count as dispatch; the function also uses
# `-is` on the ParenExpression pipeline internals, and folding those in would make the contract
# assert something other than "which Left shapes are recognised".
# Ordinal dedup+sort, per the #1507 ruling: `Sort-Object -Unique` folds case and ignorable
# characters under the current culture, so the folding would run *before* the [StringComparison]::Ordinal
# join check below and cancel it out — the same "序数化被自己上一行抵消" shape already ruled on here.
$dispatchedLeftTypes = @(
    [System.Collections.Generic.SortedSet[string]]::new(
        [string[]] @(
            $seamTargetsFunction.Body.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.BinaryExpressionAst] -and
                $node.Operator -eq [System.Management.Automation.Language.TokenKind]::Is -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                [string]::Equals([string] $node.Left.VariablePath.UserPath, 'Left', [StringComparison]::OrdinalIgnoreCase) -and
                $node.Right -is [System.Management.Automation.Language.TypeExpressionAst]
            }, $true) | ForEach-Object { [string] $_.Right.TypeName.Name -replace '^.*\.', '' }
        ),
        [StringComparer]::Ordinal)
)
$expectedDispatchedLeftTypes = @('ArrayLiteralAst', 'AttributedExpressionAst', 'ParenExpressionAst', 'VariableExpressionAst')
if (-not [string]::Equals(($dispatchedLeftTypes -join '|'), ($expectedDispatchedLeftTypes -join '|'), [StringComparison]::Ordinal)) {
    throw "Get-SeamAssignmentTargets dispatches on [$($dispatchedLeftTypes -join ', ')]; the ruling in docs/architecture/script-automation-governance.md says [$($expectedDispatchedLeftTypes -join ', ')]. Change both together."
}
if (-not [System.Management.Automation.Language.ConvertExpressionAst].IsSubclassOf([System.Management.Automation.Language.AttributedExpressionAst])) {
    throw 'ConvertExpressionAst is no longer an AttributedExpressionAst; `[string] $a = …` now needs its own branch in Get-SeamAssignmentTargets.'
}

# The shapes that are recognised but bind nothing, named here because the implementation reaches them
# by falling through and so contains no token a reader can grep for.
$nonBindingLeftTypes = @('IndexExpressionAst', 'MemberExpressionAst')
$leftShapeCorpus = @(
    '$a = 1', '$a += 1', '$a ??= 1', '$a = $b = 1', '${a b} = 1', '$script:a = 1', '$env:X = 1',
    '$a, $b = 1, 2', '($a, $b), $c = 1',
    '[string] $a = 1', '[string[]] ($a, $b) = 1, 2', '[int] ($a) = 1', '[ref] $a = 1',
    '[ValidateNotNullOrEmpty()] $a = 1',
    '($a) = 1', '(($a)) = 1',
    '$h[0] = 1', '$h[$i] = 1', '$h[0][1] = 1', '$global:h[0] = 1', '$h[0] ??= 1',
    '$o.P = 1', '$o.B.C = 1', '$o?.P = 1', '$o::B = 1', '$o."$n" = 1', '$o[0].P = 1', '$o.P += 1'
)
$observedLeftTypes = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($spelling in $leftShapeCorpus) {
    $corpusErrors = $null
    $corpusAst = [System.Management.Automation.Language.Parser]::ParseInput($spelling, [ref] $null, [ref] $corpusErrors)
    if ($corpusErrors -and $corpusErrors.Count -gt 0) {
        throw "Left-shape corpus entry '$spelling' no longer parses ($($corpusErrors[0].ErrorId)); the corpus must stay a corpus of real assignments."
    }
    $corpusAssignments = @($corpusAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
    if ($corpusAssignments.Count -eq 0) {
        throw "Left-shape corpus entry '$spelling' produced no AssignmentStatementAst."
    }
    foreach ($corpusAssignment in $corpusAssignments) { [void] $observedLeftTypes.Add($corpusAssignment.Left.GetType().Name) }
}
$expectedLeftTypes = @('ArrayLiteralAst', 'AttributedExpressionAst', 'ConvertExpressionAst', 'IndexExpressionAst', 'MemberExpressionAst', 'ParenExpressionAst', 'VariableExpressionAst')
if (-not [string]::Equals((@($observedLeftTypes) -join '|'), ($expectedLeftTypes -join '|'), [StringComparison]::Ordinal)) {
    throw "AssignmentStatementAst.Left shapes measured on PowerShell $($PSVersionTable.PSVersion): [$(@($observedLeftTypes) -join ', ')]; the ruling enumerates [$($expectedLeftTypes -join ', ')]. Rule on the difference in Get-SeamAssignmentTargets and docs/architecture/script-automation-governance.md."
}
foreach ($observedLeftType in @($observedLeftTypes)) {
    $observedType = [System.Management.Automation.Language.AssignmentStatementAst].Assembly.GetType("System.Management.Automation.Language.$observedLeftType")
    $isDispatched = @($expectedDispatchedLeftTypes | Where-Object {
        $dispatchType = [System.Management.Automation.Language.AssignmentStatementAst].Assembly.GetType("System.Management.Automation.Language.$_")
        $dispatchType.IsAssignableFrom($observedType)
    }).Count -gt 0
    $isRuledNonBinding = @($nonBindingLeftTypes | Where-Object { [string]::Equals($_, $observedLeftType, [StringComparison]::Ordinal) }).Count -gt 0
    if ($isDispatched -eq $isRuledNonBinding) {
        throw "Left shape '$observedLeftType' must be either dispatched on or explicitly ruled a non-binding, and exactly one of the two; dispatched=$isDispatched ruledNonBinding=$isRuledNonBinding."
    }
}

# Every other concrete expression type in the AST assembly has been measured never to appear as a
# Left. Frozen so that a PowerShell release adding an expression form forces someone to rule on it
# instead of it landing in the fail-open tail. One-sided: extras are a failure, absences are not.
$ruledOutExpressionTypes = @(
    'ArrayExpressionAst', 'BaseCtorInvokeMemberExpressionAst', 'BinaryExpressionAst',
    'ConstantExpressionAst', 'ErrorExpressionAst', 'ExpandableStringExpressionAst', 'HashtableAst',
    'InvokeMemberExpressionAst', 'ScriptBlockExpressionAst', 'StringConstantExpressionAst',
    'SubExpressionAst', 'TernaryExpressionAst', 'TypeExpressionAst', 'UnaryExpressionAst',
    'UsingExpressionAst'
)
$knownExpressionTypes = @($expectedLeftTypes + $ruledOutExpressionTypes)
# Ordinal ordering, matching the ordinal membership filter in the same pipeline: this list only feeds the
# failure message, but a culture-aware `Sort-Object` in the same expression as an Ordinal comparison
# is the shape this file has already ruled against, so it is not left to read as an exception.
$unruledExpressionTypes = @(
    [System.Collections.Generic.SortedSet[string]]::new(
        [string[]] @(
            [System.Management.Automation.Language.ExpressionAst].Assembly.GetTypes() |
                Where-Object { $_.IsPublic -and -not $_.IsAbstract -and $_.IsSubclassOf([System.Management.Automation.Language.ExpressionAst]) } |
                ForEach-Object { $_.Name } |
                Where-Object { $candidateType = $_; @($knownExpressionTypes | Where-Object { [string]::Equals($_, $candidateType, [StringComparison]::Ordinal) }).Count -eq 0 }
        ),
        [StringComparer]::Ordinal)
)
if ($unruledExpressionTypes.Count -gt 0) {
    throw "PowerShell $($PSVersionTable.PSVersion) has expression AST types nobody has ruled on as an assignment Left: [$($unruledExpressionTypes -join ', ')]. Decide whether each can appear there, then add it to the corpus or to the ruled-out list."
}

# ---------------------------------------------------------------------------------------------
# Fixtures live in a mirrored scripts/ tree under the platform temp directory, never in the
# repository. The checker derives its repo root as `$PSScriptRoot/..` and its default -Path as
# `$PSScriptRoot/.`, so a verbatim copy at <temp>/…/scripts/check-script-governance.ps1 classifies
# <temp>/…/scripts/lib/x.ps1 as the repo-relative `scripts/lib/x.ps1` and runs exactly the same
# $scanExclusions and library-scope decisions. Planting into the real scripts/lib instead would leave
# a violating file behind whenever the process is killed or a CI step times out — after which every
# governance run goes red — and would dirty the working tree of whichever of this repo's parallel
# worktrees happened to be running the gate.
$mirrorRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-script-governance-boundary-{0}" -f [Guid]::NewGuid().ToString('N'))
$mirrorScripts = Join-Path $mirrorRoot 'scripts'
$mirrorLib = Join-Path $mirrorScripts 'lib'
$mirrorChecker = Join-Path $mirrorScripts 'check-script-governance.ps1'
$libraryFixturePath = Join-Path $mirrorLib 'zz-governance-library-fixture.ps1'
$nonLibraryFixturePath = Join-Path $mirrorScripts 'zz-claims-library-category.ps1'
$libraryHeader = @'
# Script-Governance:
#   Category: library
#   SideEffects:
#     - None; contract fixture
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

'@

function Invoke-LibraryScopeCase {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Body,
        [Parameter(Mandatory)] [int] $ExpectedExitCode,
        [string] $ExpectedRule,
        [string] $TargetPath = $libraryFixturePath
    )

    [System.IO.File]::WriteAllText($TargetPath, $Body, [System.Text.UTF8Encoding]::new($false))
    try {
        $output = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $mirrorChecker -Path $TargetPath 2>&1) -join "`n"
        $actualExitCode = $LASTEXITCODE
        if ($actualExitCode -ne $ExpectedExitCode) {
            Write-Host $output
            throw "Library scope case '$Name' expected exit $ExpectedExitCode, got $actualExitCode."
        }
        if ($ExpectedRule -and -not $output.Contains("[$ExpectedRule]")) {
            Write-Host $output
            throw "Library scope case '$Name' expected rule '$ExpectedRule'."
        }
    }
    finally {
        Remove-Item -LiteralPath $TargetPath -Force -ErrorAction SilentlyContinue
    }
}

try {
    New-Item -ItemType Directory -Path $mirrorLib -Force | Out-Null
    Copy-Item -LiteralPath $checker -Destination $mirrorChecker -Force

    # The injected-action seam a library is allowed to keep: a [scriptblock] parameter, and a local
    # assigned a script-block literal. Neither dot-sources the wrapper, which is the MissingHelper
    # relaxation being asserted at the same time.
    Invoke-LibraryScopeCase -Name 'seam-invocation-allowed' -ExpectedExitCode 0 -Body ($libraryHeader + @'
function Invoke-FixtureSeam {
    param([Parameter(Mandatory)] [scriptblock] $Action)
    $local = { param($Value) $Value }
    & $local (& $Action)
}
'@)

    # …and the holes it must not open. `& 'dotnet'` is the literal case; `& $exe` is the one
    # ForbiddenCommand structurally cannot see, which is the whole reason DynamicInvocation is kept
    # in library scope rather than exempted.
    Invoke-LibraryScopeCase -Name 'literal-command-invocation' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureLiteral {
    & 'dotnet' build
}
'@)

    Invoke-LibraryScopeCase -Name 'string-variable-invocation' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureStringVariable {
    $exe = 'dotnet'
    & $exe build
}
'@)

    # The seam proof is scoped, not file-wide (#1509 review). One function proving `$action` holds a
    # script block must not license a *different* function's `$action = 'dotnet'; & $action` — that
    # is the arbitrary-command hole the rule exists for, reachable by picking a popular parameter
    # name. Widen Get-ScopedSeamBindings back to whole-file collection and this case turns
    # green while the string-variable case above stays red, so it is the one that pins the scope.
    Invoke-LibraryScopeCase -Name 'cross-function-name-collision' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureSeamOwner {
    $action = { 'seam' }
    & $action
}

function Invoke-FixtureNameBorrower {
    $action = 'dotnet'
    & $action build
}
'@)

    # The same scoping rule for the *parameter* half of the proof, which the assignment case above
    # cannot reach: one function declaring `[scriptblock] $Action` must not license another function
    # that only invokes `$Action`. Two cases are needed because the implementation makes the scope
    # decision separately for parameters and for assignments, and dropping either filter alone leaves
    # the other case green.
    Invoke-LibraryScopeCase -Name 'cross-function-parameter-leak' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureSeamParameterOwner {
    param([Parameter(Mandatory)] [scriptblock] $Action)
    & $Action
}

function Invoke-FixtureParameterBorrower {
    & $Action build
}
'@)

    # The same leak in PowerShell's *other* spelling of a parameter list. An inline list hangs off the
    # FunctionDefinitionAst rather than the body, so a scope walk that only looks for the nearest
    # enclosing ScriptBlockAst files the parameter under the whole file and hands it to every sibling
    # function — measured in #1509 round 2, where this exact fixture exited 0 while the `param()`
    # spelling above exited 1 for byte-identical runtime semantics. Two spellings, two cases: the
    # `param()` case alone cannot see this regression. The borrower deliberately does *not* rebind the
    # name, so this case fails on the scope attribution alone rather than on the shadowing rule below.
    Invoke-LibraryScopeCase -Name 'inline-parameter-cross-function-leak' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureInlineParameterOwner([Parameter(Mandatory)] [scriptblock] $Action) {
    & $Action
}

function Invoke-FixtureInlineParameterBorrower {
    & $Action build
}
'@)

    # …and the inline spelling must still *earn* the relaxation for its own body, so the case above
    # is pinned by tightening the scope rather than by dropping inline parameters on the floor.
    Invoke-LibraryScopeCase -Name 'inline-parameter-seam-allowed' -ExpectedExitCode 0 -Body ($libraryHeader + @'
function Invoke-FixtureInlineParameterSeam([Parameter(Mandatory)] [scriptblock] $Action) {
    & $Action
}
'@)

    # A file-level `$action = { … }` is an enclosing scope, so it really is visible to a function that
    # never rebinds the name — that half of the ruling is asserted here so the shadowing case below
    # cannot be satisfied by simply refusing to walk outward.
    Invoke-LibraryScopeCase -Name 'file-level-seam-visible-to-inner-scope' -ExpectedExitCode 0 -Body ($libraryHeader + @'
$fixtureAction = { 'seam' }

function Invoke-FixtureUsesFileLevelSeam {
    & $fixtureAction
}
'@)

    # …but a function that assigns the name owns a *local*, so the file-level seam is shadowed and
    # must not license it. This is not the documented residual ("re-assigned later *in the same
    # scope*"): here the seam and the string live in different scopes, and PowerShell resolves
    # `& $fixtureAction` to the string. Drop the innermost-binding-wins rule and this exits 0.
    Invoke-LibraryScopeCase -Name 'file-level-seam-shadowed-by-local' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
$fixtureAction = { 'seam' }

function Invoke-FixtureShadowsFileLevelSeam {
    $fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@)

    # ---------------------------------------------------------------------------------------------
    # The other spellings of "this scope binds that name" (#1509 round 3). Round 2 fixed the shadowing
    # rule for `=` only, and the review then measured four more spellings that rebind a name locally
    # while the checker still handed them the enclosing seam — and, in a real process, ran the external
    # command. Every case below plants a file-level `$fixtureAction = { … }` seam and rebinds the name
    # some other way, so it is exactly the round-2 case with the spelling changed.
    $shadowSeamHeader = $libraryHeader + @'
$fixtureAction = { 'seam' }

'@
    $shadowingSpellings = [ordered]@{
        # ForEachStatementAst, not AssignmentStatementAst — invisible to a scan that only walks
        # assignments, which is why the whole enumeration is written out in Get-ScopedSeamBindings.
        'foreach-iteration-variable-shadows-seam' = @'
function Invoke-FixtureForeachShadow {
    foreach ($fixtureAction in @('dotnet')) { & $fixtureAction build }
}
'@
        # VariablePath.UserPath keeps the scope qualifier, so `local:fixtureAction` never matched the
        # `fixtureAction` that `& $fixtureAction` looks up. One case per qualifier the checker
        # accepts (`using` is not one of them — `$using:a = 1` does not parse): the
        # normalization is one line and dropping it turns all five green at once (local, script,
        # global, private, variable), but a reviewer reading a single case cannot tell which
        # qualifiers were considered.
        'local-qualifier-shadows-seam' = @'
function Invoke-FixtureLocalShadow {
    $local:fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        'script-qualifier-shadows-seam' = @'
function Invoke-FixtureScriptShadow {
    $script:fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        'global-qualifier-shadows-seam' = @'
function Invoke-FixtureGlobalShadow {
    $global:fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        'private-qualifier-shadows-seam' = @'
function Invoke-FixturePrivateShadow {
    $private:fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        # Binding through a cmdlet rather than an operator.
        'set-variable-shadows-seam' = @'
function Invoke-FixtureSetVariableShadow {
    Set-Variable -Name fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        'new-variable-shadows-seam' = @'
function Invoke-FixtureNewVariableShadow {
    New-Variable -Name fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        # …and the positional spelling of the same binding, which is where "the first element that is
        # not a parameter is the name" fell over (#1509 round 6). A named parameter that takes a value
        # consumes the element after it, so the first *unconsumed* element is the name; all four of
        # these were measured exiting 0 with the external command really running in a live process.
        'set-variable-positional-name-after-valued-parameter' = @'
function Invoke-FixtureSetVariableScopeFirst {
    Set-Variable -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-positional-name-after-value-parameter' = @'
function Invoke-FixtureSetVariableValueFirst {
    Set-Variable -Value 'dotnet' fixtureAction
    & $fixtureAction build
}
'@
        'new-variable-positional-name-after-valued-parameter' = @'
function Invoke-FixtureNewVariableScopeFirst {
    New-Variable -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-alias-positional-name-after-valued-parameter' = @'
function Invoke-FixtureSetVariableAliasScopeFirst {
    sv -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # ---------------------------------------------------------------------------------------
        # …and the spellings the *command-name* half of round 6 still missed (#1509 round 7). The
        # pairing half was read from cmdlet metadata; the name half stayed a hand list of
        # set-variable/sv/new-variable/nv, so `set` — the alias PowerShell actually ships for
        # Set-Variable — and every module-qualified spelling exited 0 with the external command
        # really running in a live process. Both classes are pinned here, and separately, so that
        # fixing one does not look like fixing the other.
        'set-variable-shipped-alias-shadows-seam' = @'
function Invoke-FixtureSetAliasShadow {
    set -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # Command names resolve case-insensitively, so the lookup must too — the one place in this
        # PR where a comparison is deliberately not case-sensitive.
        'set-variable-shipped-alias-uppercase-shadows-seam' = @'
function Invoke-FixtureSetAliasUppercaseShadow {
    SET -Name fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-module-qualified-shadows-seam' = @'
function Invoke-FixtureSetVariableModuleQualifiedShadow {
    Microsoft.PowerShell.Utility\Set-Variable -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # The qualifier is stripped ordinally at the last `\` and the remainder is matched
        # case-insensitively; this case is red unless *both* of those hold.
        'set-variable-module-qualified-mixed-case-shadows-seam' = @'
function Invoke-FixtureSetVariableModuleQualifiedMixedCaseShadow {
    microsoft.powershell.utility\SET-VARIABLE -Name fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        'new-variable-module-qualified-shadows-seam' = @'
function Invoke-FixtureNewVariableModuleQualifiedShadow {
    Microsoft.PowerShell.Utility\New-Variable -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # A module-qualified *alias* does not resolve at run time (PowerShell qualifies exported
        # commands, and the shipped aliases belong to no module — measured:
        # CommandNotFoundException). The checker reports it anyway, because stripping the qualifier
        # before the lookup is one rule rather than two and the error is in the fail-closed
        # direction. Asserted so that the over-report is a recorded decision instead of a surprise
        # someone later "fixes" into a hole.
        'set-variable-module-qualified-alias-shadows-seam' = @'
function Invoke-FixtureSetVariableModuleQualifiedAliasShadow {
    Microsoft.PowerShell.Utility\sv -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # The control that keeps the fix honest in the other direction: a *switch* consumes nothing,
        # so the element after `-Force` is the positional name. This case reports correctly today and
        # a blunt "skip the element after every parameter" rule turns it green — which is why the
        # pairing is read from the cmdlet's parameter metadata instead of being hand-listed.
        'set-variable-positional-name-after-switch-parameter' = @'
function Invoke-FixtureSetVariableForceFirst {
    Set-Variable -Force fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        # Prefixes bind too (`-Sc Local zz 'y'` measured really binding `zz`), so the resolver
        # resolves them rather than treating an abbreviation as an unknown parameter.
        'set-variable-abbreviated-valued-parameter' = @'
function Invoke-FixtureSetVariableAbbreviatedScope {
    Set-Variable -Sc Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-abbreviated-name-parameter' = @'
function Invoke-FixtureSetVariableAbbreviatedName {
    Set-Variable -Na fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        # ---------------------------------------------------------------------------------------
        # …and the axis rounds 6 and 7 still had not asked about: the *collectiveness of the
        # parameter's declared type* (#1509 round 8). `Set-Variable -Name` is `[string[]]`, so one
        # argument spells several bindings — and reading only a StringConstantExpressionAst dropped
        # the whole call. Measured exit 0 with `& $fixtureAction` really executing `/bin/echo`
        # (`/bin/echo` prints nothing, the seam returns 'seam' — the empty output is the proof).
        # Both spellings, because the named argument and the positional element reach the extraction
        # by different paths in Get-SeamBinderNameArgument and fixing one alone leaves the other.
        'set-variable-multiple-literal-names-positional' = @'
function Invoke-FixtureMultipleNamesPositional {
    Set-Variable fixtureAction,zz 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-multiple-literal-names-named' = @'
function Invoke-FixtureMultipleNamesNamed {
    Set-Variable -Name fixtureAction,zz -Value 'dotnet'
    & $fixtureAction build
}
'@
        # A list whose elements are not all literal still binds the literal ones — measured: after
        # `Set-Variable -Name a,$computed`, both `$a` and the computed name hold the value. Recording
        # nothing for the whole call would be fail-open on a name written right there; the non-literal
        # element stays the registered computed-name residual. Rule this "record nothing when the list
        # is mixed" instead and this case alone turns green.
        'set-variable-mixed-literal-and-computed-names' = @'
function Invoke-FixtureMixedNames {
    $computed = 'zz'
    Set-Variable -Name fixtureAction,$computed -Value 'dotnet'
    & $fixtureAction build
}
'@
        # The grouping spellings of the same argument. `(…)`, `@(…)` and `$(…)` carry a value through
        # without computing one, so each of these really binds — including the *single*-name ones,
        # which are not about plurality at all and were equally invisible before round 8.
        'set-variable-parenthesized-name-list' = @'
function Invoke-FixtureParenthesizedNameList {
    Set-Variable ('fixtureAction','zz') 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-array-expression-name-list' = @'
function Invoke-FixtureArrayExpressionNameList {
    Set-Variable @('fixtureAction','zz') 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-parenthesized-single-name' = @'
function Invoke-FixtureParenthesizedSingleName {
    Set-Variable ('fixtureAction') 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-subexpression-single-name' = @'
function Invoke-FixtureSubExpressionSingleName {
    Set-Variable $('fixtureAction') 'dotnet'
    & $fixtureAction build
}
'@
        # `-Name:x` attaches the argument to the CommandParameterAst instead of leaving it as the
        # next command element, so it reaches the extraction by a *third* path — and it had no
        # fixture at all until the round-8 mutation matrix went looking (mutating that return turned
        # nothing red). Both spellings are pinned, because the leaf and the array shape travel the
        # same path and one case cannot tell which of the two a regression broke. Measured: the
        # multi-name colon spelling really binds both names and runs the external command.
        'set-variable-colon-argument-single-name' = @'
function Invoke-FixtureColonArgumentSingleName {
    Set-Variable -Name:fixtureAction -Value 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-colon-argument-multiple-names' = @'
function Invoke-FixtureColonArgumentMultipleNames {
    Set-Variable -Name:fixtureAction,zz -Value 'dotnet'
    & $fixtureAction build
}
'@
        # A trailing comma turns the next token into a second name — measured: this really binds both
        # `fixtureAction` and a variable literally called `-Value`, and the checker now records both
        # because it reads the array literal the parser actually built rather than the one the author
        # meant.
        'set-variable-trailing-comma-name-list' = @'
function Invoke-FixtureTrailingCommaNameList {
    Set-Variable -Name fixtureAction, -Value 'dotnet'
    & $fixtureAction build
}
'@
        # The multi-name argument reached through the alias and the module-qualified spelling: the
        # name half (round 7) and the argument half (round 8) have to compose, and each fixture goes
        # green if either half is reverted.
        'set-variable-alias-multiple-literal-names' = @'
function Invoke-FixtureAliasMultipleNames {
    sv fixtureAction,zz 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-module-qualified-multiple-literal-names' = @'
function Invoke-FixtureModuleQualifiedMultipleNames {
    Microsoft.PowerShell.Utility\Set-Variable -Scope Local fixtureAction,zz 'dotnet'
    & $fixtureAction build
}
'@
        # `New-Variable -Name` is a scalar `[string]`, so a multi-name argument binds nothing there —
        # it throws (`CannotConvertArgument` named, "positional parameter cannot be found"
        # positional; both measured). The checker expands it anyway, which *over*-reports: one rule
        # instead of two, in the fail-closed direction, exactly the trade already recorded for the
        # module-qualified alias. Pinned so the over-report stays a decision rather than a surprise
        # someone later "fixes" into a per-cmdlet branch — and so that a per-cmdlet branch, if anyone
        # does want one, has to change this line rather than slip past.
        'new-variable-multiple-literal-names-over-reported' = @'
function Invoke-FixtureNewVariableMultipleNames {
    New-Variable fixtureAction,zz 'dotnet'
    & $fixtureAction build
}
'@
        'data-statement-shadows-seam' = @'
function Invoke-FixtureDataShadow {
    data fixtureAction { 'dotnet' }
    & $fixtureAction build
}
'@
        'variable-qualifier-shadows-seam' = @'
function Invoke-FixtureVariableQualifierShadow {
    $variable:fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        # Compound assignment is an AssignmentStatementAst too, so this one was already covered; it is
        # kept as the control that says so rather than being assumed.
        'compound-assignment-shadows-seam' = @'
function Invoke-FixtureCompoundShadow {
    $fixtureAction += 'dotnet'
    & $fixtureAction build
}
'@
        # ---------------------------------------------------------------------------------------
        # AssignmentStatementAst.Left is itself an expression with several shapes, and rounds 2–4
        # each shipped a fix covering only the shapes that review had named. These four are the rest
        # of the hierarchy (#1509 round 4 measured the first two exiting 0 with the external command
        # really running); Get-SeamAssignmentTargets now walks the type hierarchy, so the case list
        # and the implementation are enumerations of the same thing.
        'multiple-assignment-shadows-seam' = @'
function Invoke-FixtureMultipleAssignmentShadow {
    $fixtureAction, $other = @('dotnet', 'x')
    & $fixtureAction build
}
'@
        'type-constrained-assignment-shadows-seam' = @'
function Invoke-FixtureTypeConstrainedShadow {
    [string] $fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        'attributed-assignment-shadows-seam' = @'
function Invoke-FixtureAttributedShadow {
    [ValidateNotNullOrEmpty()] $fixtureAction = 'dotnet'
    & $fixtureAction build
}
'@
        'parenthesized-assignment-shadows-seam' = @'
function Invoke-FixtureParenthesizedShadow {
    ($fixtureAction) = 'dotnet'
    & $fixtureAction build
}
'@
        # …and the three wrappers nested, which only shadows if the walk is recursive rather than a
        # one-level unwrap.
        'nested-left-shapes-shadow-seam' = @'
function Invoke-FixtureNestedLeftShapesShadow {
    [string[]] ($fixtureAction, $other) = @('dotnet', 'x')
    & $fixtureAction build
}
'@
    }
    foreach ($spelling in $shadowingSpellings.Keys) {
        Invoke-LibraryScopeCase -Name $spelling -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($shadowSeamHeader + $shadowingSpellings[$spelling])
    }

    # The two Left shapes that are *not* bindings, asserted as such. `$a['k'] = …` and `$a.P = …`
    # name a member of whatever `$a` refers to; at the syntax level there is no binding of the name
    # `a` to record, so reporting a violation here would be reporting one with nothing behind it.
    # Skipping them is therefore a decision, not an omission — treat either as a binding and these
    # two turn red, which is what stops the "exhaustive over Left" claim from being exhaustive only
    # in the permissive direction.
    #
    # These cases assert the *syntactic* ruling and nothing more. They are emphatically not evidence
    # that the variable still holds the seam at run time: `$r = [ref] $a; $r.Value = '/bin/echo'`
    # replaces the binding through exactly this member-assignment spelling, measured executing the
    # external command (#1509 round 5). That is a registered residual, pinned separately by
    # `residual-ref-rebinding` below.
    $nonBindingLeftShapes = [ordered]@{
        'index-assignment-is-not-a-binding' = @'
function Invoke-FixtureIndexAssignment {
    $fixtureAction[0] = 'dotnet'
    & $fixtureAction
}
'@
        'member-assignment-is-not-a-binding' = @'
function Invoke-FixtureMemberAssignment {
    $fixtureAction.Extra = 'dotnet'
    & $fixtureAction
}
'@
    }
    foreach ($shape in $nonBindingLeftShapes.Keys) {
        Invoke-LibraryScopeCase -Name $shape -ExpectedExitCode 0 -Body ($shadowSeamHeader + $nonBindingLeftShapes[$shape])
    }

    # Wrapped spellings bind the name but prove nothing, so a seam declared through one of them is
    # still a violation. That asymmetry is the same "only ever remove permissions" trade the scope
    # qualifiers were given in round 3, and it is the honest reading for the one wrapper whose answer
    # is knowable and negative: `[string] $x = { … }` binds a string. Pinned so a later change cannot
    # quietly turn the wrappers into seam proofs while pointing at the shadowing cases as evidence.
    Invoke-LibraryScopeCase -Name 'type-constrained-declaration-proves-no-seam' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureTypeConstrainedSeamDeclaration {
    [scriptblock] $fixtureSeam = { 'seam' }
    & $fixtureSeam
}
'@)

    # Normalizing the *binding* side is what makes the qualifier cases above shadow correctly, and it is
    # deliberately not paired with normalizing the *invocation* side. Two cases, because they pin the
    # two halves of that asymmetry and each is green if only the other filter is dropped:
    #
    #   1. a scope-qualified declaration binds the name but proves no seam, so an unqualified `& $x`
    #      elsewhere is still a violation;
    #   2. a scope-qualified *invocation* resolves through nothing this file proved, so it is a
    #      violation even when the plain name is a proven seam in the same scope.
    #
    # Both are stricter than PowerShell's own resolution in one direction, which is the intended
    # trade: a rule that only ever removes permissions cannot smuggle in a relaxation while claiming
    # to fix a hole. Asserted so the tightening cannot be quietly dropped as inconvenient.
    Invoke-LibraryScopeCase -Name 'qualified-declaration-proves-no-seam' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
$script:fixtureAction = { 'seam' }

function Invoke-FixtureQualifiedSeamDeclaration {
    & $fixtureAction
}
'@)

    Invoke-LibraryScopeCase -Name 'qualified-invocation-proves-nothing' -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + @'
function Invoke-FixtureQualifiedSeamInvocation {
    $fixtureSeam = { 'seam' }
    & $script:fixtureSeam
}
'@)

    # The automatic `$_` is not a binding the checker models, and it does not have to be: it names
    # nothing this file can prove, so every spelling of it is a violation. Pinned because "we did not
    # model $_" and "$_ is safe" are different claims.
    $automaticVariableSpellings = [ordered]@{
        'foreach-object-automatic-variable' = @'
function Invoke-FixtureForEachObjectAutomatic {
    @('dotnet') | ForEach-Object { & $_ build }
}
'@
        'switch-automatic-variable' = @'
function Invoke-FixtureSwitchAutomatic {
    switch (@('dotnet')) { default { & $_ build } }
}
'@
        'catch-automatic-variable' = @'
function Invoke-FixtureCatchAutomatic {
    try { throw 'x' } catch { & $_ }
}
'@
    }
    foreach ($spelling in $automaticVariableSpellings.Keys) {
        Invoke-LibraryScopeCase -Name $spelling -ExpectedExitCode 1 -ExpectedRule 'DynamicInvocation' -Body ($libraryHeader + $automaticVariableSpellings[$spelling])
    }

    # …and the residuals, pinned as *currently permitted* so that the documented "known residual"
    # list and the implementation cannot drift apart in either direction. Every one of them binds the
    # name somewhere this file's AST does not say it is bound, which is why the walk above cannot see
    # them; all are far more deliberate than the spellings above, which is why they are accepted
    # rather than chased. Each was measured on pwsh 7.6.4 both exiting 0 here *and* really running the
    # external command in a live process — a residual that could not actually be reached would be a
    # comfortable fiction, so these are the uncomfortable ones written down.
    $residualSpellings = [ordered]@{
        'residual-set-variable-computed-name' = @'
function Invoke-FixtureComputedBindingName {
    $name = 'fixtureAction'
    Set-Variable -Name $name -Value 'dotnet'
    & $fixtureAction build
}
'@
        # Round 8 drew the line at *literal*, not at *constant-foldable*: `(…)`, `@(…)` and `$(…)`
        # are grouping, so they are read through, but a name produced by a cast or a concatenation is
        # computed and this checker does not fold constants. Both spellings really bind at run time
        # (measured), so this is a residual and not a non-binding — registered here so the line
        # between "read through" and "not read" is executable rather than asserted in a comment.
        'residual-set-variable-non-literal-name-expression' = @'
function Invoke-FixtureCastNameExpression {
    Set-Variable -Name ([string] 'fixtureAction') -Value 'dotnet'
    & $fixtureAction build
}
'@
        'residual-set-variable-concatenated-name-expression' = @'
function Invoke-FixtureConcatenatedNameExpression {
    Set-Variable -Name ('fixture' + 'Action') -Value 'dotnet'
    & $fixtureAction build
}
'@
        # A splatted binder carries its -Name inside a hashtable the AST cannot resolve, so it is the
        # same residual as a computed name, reached by a different spelling (#1509 round 6).
        'residual-set-variable-splatted-parameters' = @'
function Invoke-FixtureSplattedBinder {
    $binderSplat = @{ Name = 'fixtureAction'; Value = 'dotnet' }
    Set-Variable @binderSplat
    & $fixtureAction build
}
'@
        'residual-psvariable-set' = @'
function Invoke-FixtureSessionStateBinding {
    $ExecutionContext.SessionState.PSVariable.Set('fixtureAction', 'dotnet')
    & $fixtureAction build
}
'@
        # #1509 round 5. `[ref] $a` (and `Get-Variable a`) hand out the live PSVariable; writing
        # `.Value` replaces the binding. The write is spelled as member assignment, which
        # Get-SeamAssignmentTargets skips *because it is not a syntactic binding* — which is true, and
        # is exactly why the run-time hole exists and is registered rather than argued away.
        'residual-ref-rebinding' = @'
function Invoke-FixtureRefRebinding {
    $reference = [ref] $fixtureAction
    $reference.Value = 'dotnet'
    & $fixtureAction build
}
'@
        # The pipeline processor creates the binding, so there is no AST node in the file that binds
        # the name. -PipelineVariable is visible to *downstream* pipeline elements (measured: not to
        # the producing command's own body, and torn down after the pipeline), so the reachable
        # spelling is a downstream consumer.
        'residual-pipeline-variable' = @'
function Invoke-FixturePipelineVariable {
    Write-Output 'dotnet' -PipelineVariable fixtureAction |
        ForEach-Object { & $fixtureAction build }
}
'@
        # -OutVariable is the same class of binding and, unlike -PipelineVariable, survives the
        # pipeline — so the invocation does not even have to sit inside it.
        'residual-out-variable' = @'
function Invoke-FixtureOutVariable {
    Write-Output 'dotnet' -OutVariable fixtureAction | Out-Null
    & $fixtureAction build
}
'@
        # Scope-qualified writes are modelled only in the scope that spells them. `$script:` in one
        # function rebinds the file-level name that another function reads, but the two are different
        # ScriptBlockAst scopes and the reader's scope chain never sees a binding. This is the case
        # that keeps the `$script:` row in the covered table from being read as "cross-function
        # `$script:` writes are handled" — they are not.
        'residual-cross-scope-script-assignment' = @'
function Set-FixtureCrossScopeAction {
    $script:fixtureAction = 'dotnet'
}

function Invoke-FixtureCrossScopeAction {
    Set-FixtureCrossScopeAction
    & $fixtureAction build
}
'@
    }
    foreach ($spelling in $residualSpellings.Keys) {
        Invoke-LibraryScopeCase -Name $spelling -ExpectedExitCode 0 -Body ($shadowSeamHeader + $residualSpellings[$spelling])
    }

    # The premise the two binder-pairing controls rest on, asserted rather than assumed: `-Force` is a
    # switch (consumes nothing, so the next element is the positional name) and `-Scope` is not
    # (consumes the next element, so it is not). If PowerShell ever changed either, the fixtures above
    # would still pass while asserting something other than what they say.
    $setVariableParameters = (Get-Command Set-Variable -CommandType Cmdlet).Parameters
    if ($setVariableParameters['Force'].ParameterType -ne [switch]) {
        throw 'Set-Variable -Force is no longer a switch; the "positional name after a switch" control asserts nothing.'
    }
    if ($setVariableParameters['Scope'].ParameterType -eq [switch]) {
        throw 'Set-Variable -Scope no longer takes a value; the "positional name after a valued parameter" cases assert nothing.'
    }

    # ---------------------------------------------------------------------------------------------
    # The fifth axis: the *collectiveness of the declared parameter type* (#1509 round 8). Rounds 2–7
    # each enumerated one axis of the binder walk from metadata — AST node type, operator, parameter
    # pairing, command-name resolution — and round 8 found the one nobody had asked about:
    # `Set-Variable -Name` is `[string[]]`, so a single argument spells several bindings.
    #
    # This guard is the axis itself, derived from `Get-Command` rather than hand-listed: every
    # parameter of every binder cmdlet whose declared type is a collection must be accounted for. The
    # checker reads a value out of exactly one parameter (`-Name`), and it is the one that must
    # expand array shapes; `-Include`/`-Exclude` are collections too but the checker only ever needs
    # to know that they consume the next element. One-sided on purpose: a PowerShell release adding a
    # collection-typed parameter turns this red and forces a ruling, while a parameter disappearing
    # cannot introduce a new unhandled shape.
    $binderCollectionParameters = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    $binderNameParameterTypes = [ordered]@{}
    foreach ($canonicalBinder in @('Set-Variable', 'New-Variable')) {
        $binderCommand = Get-Command -Name $canonicalBinder -CommandType Cmdlet -ErrorAction SilentlyContinue
        if ($null -eq $binderCommand) {
            throw "Cannot resolve the '$canonicalBinder' cmdlet; the binder parameter-type contract asserts nothing."
        }
        foreach ($binderParameter in $binderCommand.Parameters.Values) {
            $binderParameterType = $binderParameter.ParameterType
            # "Collection" as the parameter binder means it: an array or a non-string IEnumerable.
            # [string] is IEnumerable<char> and is emphatically not a list of names, which is exactly
            # the difference between Set-Variable -Name and New-Variable -Name.
            $isCollection = $binderParameterType.IsArray -or
                ($binderParameterType -ne [string] -and [System.Collections.IEnumerable].IsAssignableFrom($binderParameterType))
            if ($isCollection) {
                [void] $binderCollectionParameters.Add("$canonicalBinder.$($binderParameter.Name)")
            }
            if ([string]::Equals([string] $binderParameter.Name, 'Name', [StringComparison]::Ordinal)) {
                $binderNameParameterTypes[$canonicalBinder] = $binderParameterType
            }
        }
    }
    $expectedBinderCollectionParameters = @(
        'Set-Variable.Exclude', 'Set-Variable.Include', 'Set-Variable.Name')
    if (-not [string]::Equals((@($binderCollectionParameters) -join '|'), ($expectedBinderCollectionParameters -join '|'), [StringComparison]::Ordinal)) {
        throw "Binder cmdlet collection-typed parameters on PowerShell $($PSVersionTable.PSVersion): [$(@($binderCollectionParameters) -join ', ')]; the round-8 ruling in docs/architecture/script-automation-governance.md enumerates [$($expectedBinderCollectionParameters -join ', ')]. Rule on the difference: any parameter the checker reads a value from must expand array shapes."
    }
    # The two halves of the ruling, stated as the measurement they rest on rather than as prose: the
    # multi-name fixtures above are only reachable because Set-Variable's -Name is plural, and the
    # `new-variable-multiple-literal-names-over-reported` case is only an over-report because
    # New-Variable's is not.
    if ($binderNameParameterTypes['Set-Variable'] -ne [string[]]) {
        throw "Set-Variable -Name is $($binderNameParameterTypes['Set-Variable']), not [string[]]; the multi-name binder fixtures assert nothing."
    }
    if ($binderNameParameterTypes['New-Variable'] -ne [string]) {
        throw "New-Variable -Name is $($binderNameParameterTypes['New-Variable']), not [string]; 'new-variable-multiple-literal-names-over-reported' is no longer an over-report and must be re-ruled."
    }

    # …and the shape half, structurally, in the same form as the `Left type set` contract above.
    # Get-SeamBinderLiteralNames returns nothing for a shape it does not recognise — fail *open*, so
    # a deleted branch silently stops shadowing and every fixture that does not spell that exact
    # shape stays green. Requiring the dispatch set verbatim turns a deletion red on its own.
    $binderLiteralNamesFunction = $checkerAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        [string]::Equals([string] $node.Name, 'Get-SeamBinderLiteralNames', [StringComparison]::OrdinalIgnoreCase)
    }, $true)
    if (-not $binderLiteralNamesFunction) {
        throw 'The script governance checker must keep its -Name argument walk in a function named Get-SeamBinderLiteralNames.'
    }
    # Ordinal dedup+sort, per the #1507 ruling, for the same reason as the Left dispatch set above.
    $dispatchedNameArgumentTypes = @(
        [System.Collections.Generic.SortedSet[string]]::new(
            [string[]] @(
                $binderLiteralNamesFunction.Body.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.BinaryExpressionAst] -and
                    $node.Operator -eq [System.Management.Automation.Language.TokenKind]::Is -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                    [string]::Equals([string] $node.Left.VariablePath.UserPath, 'Argument', [StringComparison]::OrdinalIgnoreCase) -and
                    $node.Right -is [System.Management.Automation.Language.TypeExpressionAst]
                }, $true) | ForEach-Object { [string] $_.Right.TypeName.Name -replace '^.*\.', '' }
            ),
            [StringComparer]::Ordinal)
    )
    $expectedDispatchedNameArgumentTypes = @(
        'ArrayExpressionAst', 'ArrayLiteralAst', 'ParenExpressionAst', 'StringConstantExpressionAst', 'SubExpressionAst')
    if (-not [string]::Equals(($dispatchedNameArgumentTypes -join '|'), ($expectedDispatchedNameArgumentTypes -join '|'), [StringComparison]::Ordinal)) {
        throw "Get-SeamBinderLiteralNames dispatches on [$($dispatchedNameArgumentTypes -join ', ')]; the ruling in docs/architecture/script-automation-governance.md says [$($expectedDispatchedNameArgumentTypes -join ', ')]. Change both together."
    }
    # …and the shapes really are what the parser produces for those spellings, so the dispatch set is
    # pinned to measured AST types rather than to remembered ones.
    $nameArgumentCorpus = [ordered]@{
        'StringConstantExpressionAst' = "Set-Variable fixtureAction 'x'"
        'ArrayLiteralAst' = "Set-Variable fixtureAction,zz 'x'"
        'ParenExpressionAst' = "Set-Variable ('fixtureAction','zz') 'x'"
        'ArrayExpressionAst' = "Set-Variable @('fixtureAction','zz') 'x'"
        'SubExpressionAst' = "Set-Variable `$('fixtureAction') 'x'"
    }
    foreach ($expectedShape in $nameArgumentCorpus.Keys) {
        $corpusErrors = $null
        $corpusAst = [System.Management.Automation.Language.Parser]::ParseInput($nameArgumentCorpus[$expectedShape], [ref] $null, [ref] $corpusErrors)
        if ($corpusErrors -and $corpusErrors.Count -gt 0) {
            throw "Binder -Name corpus entry for '$expectedShape' no longer parses ($($corpusErrors[0].ErrorId))."
        }
        $corpusCommand = $corpusAst.Find({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
        $observedShape = (@($corpusCommand.CommandElements)[1]).GetType().Name
        if (-not [string]::Equals($observedShape, $expectedShape, [StringComparison]::Ordinal)) {
            throw "PowerShell $($PSVersionTable.PSVersion) parses '$($nameArgumentCorpus[$expectedShape])' with a $observedShape -Name argument, not a $expectedShape; the round-8 dispatch set is pinned to the wrong types."
        }
    }

    # ---------------------------------------------------------------------------------------------
    # The command-name half of the binder walk (#1509 round 7). Three separate guards, because the
    # three ways of getting this wrong are independent:
    #
    #   1. the premise — `set`/`sv`/`nv` really are the aliases the alias fixtures above rely on;
    #   2. the *source* derives its name set from PowerShell's alias table rather than listing
    #      spellings, so reverting to a hand list is red even where the list happens to be complete;
    #   3. recognition survives a session that has lost an alias, which is the one thing discovery
    #      alone cannot promise.
    $shippedBinderAliases = [ordered]@{ 'set' = 'Set-Variable'; 'sv' = 'Set-Variable'; 'nv' = 'New-Variable' }
    foreach ($aliasName in $shippedBinderAliases.Keys) {
        $alias = Get-Alias -Name $aliasName -ErrorAction SilentlyContinue
        if (-not $alias -or -not [string]::Equals([string] $alias.Definition, [string] $shippedBinderAliases[$aliasName], [StringComparison]::Ordinal)) {
            throw "PowerShell no longer aliases '$aliasName' to $($shippedBinderAliases[$aliasName]); the binder-alias fixtures assert nothing."
        }
    }
    # …and the qualified spelling really is a spelling of the same cmdlet, so the module-qualified
    # fixtures are pinning a reachable hole rather than a typo.
    if (-not (Get-Command 'Microsoft.PowerShell.Utility\Set-Variable' -ErrorAction SilentlyContinue)) {
        throw 'Microsoft.PowerShell.Utility\Set-Variable no longer resolves; the module-qualified binder fixtures assert nothing.'
    }

    # Guard 2, structural. The behavioural cases above go green again the moment someone hand-lists
    # `set` and the qualified spellings — which is precisely the failure mode this round exists to
    # end (#1510: "按审核点名补特例每轮必复发"). So the source is required to *derive* the set: the
    # canonical cmdlets are declared in one place and the aliases come from `Get-Alias -Definition`
    # over exactly that declaration. Delete the discovery and this is red even though every fixture
    # still passes.
    $binderCanonicalAssignment = $checkerAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        [string]::Equals([string] $node.Left.VariablePath.UserPath, 'seamBinderCanonicalNames', [StringComparison]::Ordinal)
    }, $true)
    if (-not $binderCanonicalAssignment) {
        throw 'The script governance checker must declare its binder cmdlets in a named $seamBinderCanonicalNames list.'
    }
    $declaredBinderCanonicalNames = @(
        $binderCanonicalAssignment.Right.FindAll({
            param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
        }, $true) | ForEach-Object { [string] $_.Value }
    )
    $expectedBinderCanonicalNames = @('Set-Variable', 'New-Variable')
    if (-not [string]::Equals(($declaredBinderCanonicalNames -join '|'), ($expectedBinderCanonicalNames -join '|'), [StringComparison]::Ordinal)) {
        throw "Binder cmdlet set changed: expected [$($expectedBinderCanonicalNames -join ', ')], found [$($declaredBinderCanonicalNames -join ', ')]. Update docs/architecture/script-automation-governance.md and this contract together."
    }
    $binderAliasDiscovery = @(
        $checkerAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            [string]::Equals([string] $node.GetCommandName(), 'Get-Alias', [StringComparison]::OrdinalIgnoreCase)
        }, $true)
    )
    if ($binderAliasDiscovery.Count -ne 1) {
        throw "The script governance checker must read its binder aliases from exactly one Get-Alias call; found $($binderAliasDiscovery.Count)."
    }
    $binderAliasDiscoveryText = [string] $binderAliasDiscovery[0].Extent.Text
    if ($binderAliasDiscoveryText.IndexOf('-Definition', [StringComparison]::Ordinal) -lt 0 -or
        $binderAliasDiscoveryText.IndexOf('$seamBinderCanonicalName', [StringComparison]::Ordinal) -lt 0) {
        throw "Binder aliases must be discovered with Get-Alias -Definition over \$seamBinderCanonicalNames, not hand-listed; found: $binderAliasDiscoveryText"
    }

    # Guard 3, behavioural, against the one thing discovery cannot promise. `set` ships with
    # `Options = None`, so anything that ran before the checker can remove or reassign it — and the
    # file being scanned still runs elsewhere with the alias intact. Run the checker in a session
    # that has lost the alias and the report must not move. `-Command` rather than `-File` because
    # the degradation has to happen inside the checker's own session; that is the whole variable.
    $aliasRemovalFixture = $shadowSeamHeader + @'
function Invoke-FixtureSetAliasShadowDegraded {
    set -Scope Local fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
    [System.IO.File]::WriteAllText($libraryFixturePath, $aliasRemovalFixture, [System.Text.UTF8Encoding]::new($false))
    try {
        $degradedCommand = "Remove-Item Alias:set -Force; & '$mirrorChecker' -Path '$libraryFixturePath'"
        $degradedOutput = (& pwsh -NoProfile -ExecutionPolicy Bypass -Command $degradedCommand 2>&1) -join "`n"
        $degradedExitCode = $LASTEXITCODE
        if ($degradedExitCode -eq 0 -or -not $degradedOutput.Contains('[DynamicInvocation]')) {
            Write-Host $degradedOutput
            throw "Library scope case 'binder-alias-removed-from-session' expected the binder to stay recognised after the alias was removed from the session (exit $degradedExitCode)."
        }
    }
    finally {
        Remove-Item -LiteralPath $libraryFixturePath -Force -ErrorAction SilentlyContinue
    }

    # Binder calls that bind nothing — and, unlike the residuals above, bind nothing *at run time
    # either*, so exiting 0 is the exact answer rather than a gap. Measured on 7.6.4:
    # `Set-Variable -Bogus x 'y'` → NamedParameterNotFound and `Set-Variable -V x 'y'` →
    # AmbiguousParameter, i.e. the call throws before assigning anything. Pinned so that a later
    # "just keep scanning past a parameter we could not resolve" turns them red instead of quietly
    # inventing a binding.
    $binderCallsBindingNothing = [ordered]@{
        'set-variable-unknown-parameter-binds-nothing' = @'
function Invoke-FixtureUnknownBinderParameter {
    Set-Variable -Bogus fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
        'set-variable-ambiguous-parameter-binds-nothing' = @'
function Invoke-FixtureAmbiguousBinderParameter {
    Set-Variable -V fixtureAction 'dotnet'
    & $fixtureAction build
}
'@
    }
    foreach ($spelling in $binderCallsBindingNothing.Keys) {
        Invoke-LibraryScopeCase -Name $spelling -ExpectedExitCode 0 -Body ($shadowSeamHeader + $binderCallsBindingNothing[$spelling])
    }

    # A nested script block inside the same function is still the same seam: the proof travels down
    # into `& { … }` bodies, so tightening the scope must not break the pattern libraries actually
    # use (a seam captured by a helper closure).
    Invoke-LibraryScopeCase -Name 'nested-scope-seam-allowed' -ExpectedExitCode 0 -Body ($libraryHeader + @'
function Invoke-FixtureNestedSeam {
    param([Parameter(Mandatory)] [scriptblock] $Action)
    $wrapped = { & $Action }.GetNewClosure()
    & $wrapped
}
'@)

    # ForbiddenCommand and ForbiddenProcessStart are enforced in library scope; that is the coverage
    # the old wholesale exclusion was throwing away.
    Invoke-LibraryScopeCase -Name 'forbidden-command' -ExpectedExitCode 1 -ExpectedRule 'ForbiddenCommand' -Body ($libraryHeader + @'
function Invoke-FixtureDirectCommand {
    dotnet build
}
'@)

    Invoke-LibraryScopeCase -Name 'forbidden-process-start' -ExpectedExitCode 1 -ExpectedRule 'ForbiddenProcessStart' -Body ($libraryHeader + @'
function Invoke-FixtureProcessStart {
    [System.Diagnostics.Process]::Start('dotnet')
}
'@)

    # Library scope is declared, not merely inherited from the directory.
    Invoke-LibraryScopeCase -Name 'library-must-declare-category' -ExpectedExitCode 1 -ExpectedRule 'MissingLibraryCategory' -Body @'
# Script-Governance:
#   Category: check
#   SideEffects:
#     - None; contract fixture
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Invoke-FixtureUndeclared { return 1 }
'@

    # …and a file outside scripts/lib must not be able to claim the library relaxations by
    # mislabelling its own category. Same helper, different -TargetPath: the position in the tree is
    # the whole variable under test, so re-inlining the checker invocation here would be a second
    # copy of the same shape.
    Invoke-LibraryScopeCase -Name 'outside-lib-claims-library-category' -TargetPath $nonLibraryFixturePath -ExpectedExitCode 1 -ExpectedRule 'InvalidCategory' -Body ($libraryHeader + @'
function Invoke-FixtureFalseLibrary { return 1 }
'@)

    # The default scan — no -Path — must reach scripts/lib. This is the assertion that goes red if
    # the exclusion list ever swallows the library directory again.
    [System.IO.File]::WriteAllText($libraryFixturePath, ($libraryHeader + @'
function Invoke-FixtureDefaultScan {
    dotnet build
}
'@), [System.Text.UTF8Encoding]::new($false))
    $defaultScanOutput = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $mirrorChecker 2>&1) -join "`n"
    $defaultScanExitCode = $LASTEXITCODE
    if ($defaultScanExitCode -eq 0 -or -not $defaultScanOutput.Contains('scripts/lib/zz-governance-library-fixture.ps1')) {
        Write-Host $defaultScanOutput
        throw "The default script governance scan must cover scripts/lib; the planted fixture was not reported (exit $defaultScanExitCode)."
    }
}
finally {
    if (Test-Path -LiteralPath $mirrorRoot) {
        Remove-Item -LiteralPath $mirrorRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host 'Script governance scan boundary tests passed.'

# Every case above runs the checker as a native process, and most of them *expect* it to exit 1.
# GitHub's `shell: pwsh` wrapper is `pwsh -command ". '<script>'"` and re-exits with whatever
# `$LASTEXITCODE` is left behind, so without this line a fully passing run reports failure — measured
# on run 31251016878, where this file printed the success message and the step still went red.
# Failures are unaffected: they `throw`, so control never reaches here.
#
# `exit` is only safe because this file owns its whole `run:` block in .github/workflows/ci.yml. If
# it is ever chained behind another script in one block, the exit code stops being this file's.
exit 0
