# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the script governance checker as a real process
#     - Creates and removes one temporary library fixture under scripts/lib/
#     - Creates and removes one temporary fixture under scripts/tests/fixtures/script-governance/
#   Writes:
#     - scripts/lib/zz-governance-library-fixture-*.ps1 (temporarily)
#     - scripts/tests/fixtures/script-governance/claims-library-category.ps1 (temporarily)
#   Cleanup:
#     - Removes every temporary fixture in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$checker = Join-Path $repoRoot 'scripts/check-script-governance.ps1'
$fixtures = Join-Path $repoRoot 'scripts/tests/fixtures/script-governance'

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
# Two guards, because a documented boundary with no test is just a comment:
#   1. the exclusion list is asserted verbatim, so widening it is a change to a named contract;
#   2. a deliberately-violating library file is planted under scripts/lib and the *default* scan must
#      report it — re-adding `scripts/lib/*` to the exclusion list turns that scan green and fails
#      this file.
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
    [string] $node.Left.VariablePath.UserPath -ceq 'scanExclusions'
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
if ((($declaredExclusions -join '|')) -cne ($expectedExclusions -join '|')) {
    throw "Script governance scan boundary changed: expected [$($expectedExclusions -join ', ')], found [$($declaredExclusions -join ', ')]. Update docs/architecture/script-automation-governance.md and this contract together."
}

$libraryFixtureName = "zz-governance-library-fixture-$([System.Guid]::NewGuid().ToString('N')).ps1"
$libraryFixturePath = Join-Path $repoRoot "scripts/lib/$libraryFixtureName"
$libraryFixtureRelative = "scripts/lib/$libraryFixtureName"
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
    $output = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $TargetPath 2>&1) -join "`n"
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

try {
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

    # The default scan — no -Path — must reach scripts/lib. This is the assertion that goes red if
    # the exclusion list ever swallows the library directory again.
    [System.IO.File]::WriteAllText($libraryFixturePath, ($libraryHeader + @'
function Invoke-FixtureDefaultScan {
    dotnet build
}
'@), [System.Text.UTF8Encoding]::new($false))
    $defaultScanOutput = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker 2>&1) -join "`n"
    $defaultScanExitCode = $LASTEXITCODE
    if ($defaultScanExitCode -eq 0 -or -not $defaultScanOutput.Contains($libraryFixtureRelative)) {
        Write-Host $defaultScanOutput
        throw "The default script governance scan must cover scripts/lib; '$libraryFixtureRelative' was not reported (exit $defaultScanExitCode)."
    }
}
finally {
    Remove-Item -LiteralPath $libraryFixturePath -Force -ErrorAction SilentlyContinue
}

# A file outside scripts/lib must not be able to claim the library relaxations by mislabelling its
# own category.
$nonLibraryFixturePath = Join-Path $fixtures 'claims-library-category.ps1'
[System.IO.File]::WriteAllText($nonLibraryFixturePath, @'
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

function Invoke-FixtureFalseLibrary { return 1 }
'@, [System.Text.UTF8Encoding]::new($false))
try {
    $falseLibraryOutput = (& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $nonLibraryFixturePath 2>&1) -join "`n"
    if ($LASTEXITCODE -eq 0 -or -not $falseLibraryOutput.Contains('[InvalidCategory]')) {
        Write-Host $falseLibraryOutput
        throw 'A script outside scripts/lib must not be able to declare Category library.'
    }
}
finally {
    Remove-Item -LiteralPath $nonLibraryFixturePath -Force -ErrorAction SilentlyContinue
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
