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
    # name. Widen Get-ScriptBlockVariableNames back to whole-file collection and this case turns
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
