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
# scripts/tests/check-script-governance.Tests.ps1 asserts this exact list: widening the boundary is
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
function Get-ScriptBlockVariableNames {
    param([Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Ast)

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($parameter in $Ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.ParameterAst] }, $true)) {
        if ($null -ne $parameter.StaticType -and $parameter.StaticType -eq [scriptblock]) {
            [void] $names.Add([string] $parameter.Name.VariablePath.UserPath)
        }
    }

    foreach ($assignment in $Ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) {
        if ($assignment.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { continue }
        $right = $assignment.Right
        if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
        # `{ … }.GetNewClosure()` is the same literal with its variables captured — still provably a
        # script block, and the idiom the telemetry simulator uses to freeze injected actions.
        if ($right -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            $right.Expression -is [System.Management.Automation.Language.ScriptBlockExpressionAst] -and
            [string] $right.Member.Value -eq 'GetNewClosure') {
            $right = $right.Expression
        }
        if ($right -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) {
            [void] $names.Add([string] $assignment.Left.VariablePath.UserPath)
        }
    }

    return ,$names
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
        $declaresLibrary = $categories -contains 'library'
        if ($isLibrary -and -not $declaresLibrary) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingLibraryCategory' -Message 'A dot-sourced library under scripts/lib/ must declare Script-Governance Category library.'
        }
        elseif (-not $isLibrary -and $declaresLibrary) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'InvalidCategory' -Message "Script-Governance Category 'library' is only for dot-sourced libraries under scripts/lib/."
        }
    }

    $commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
    $dotSourcesHelper = $false
    $scriptBlockVariables = if ($isLibrary) { Get-ScriptBlockVariableNames -Ast $ast } else { $null }

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
            # variable this file proves holds a script block. `& 'dotnet'`, `& "$exe"`,
            # `& (Get-Command …)` and `& $stringVariable` all remain violations, so the rule still
            # covers the case ForbiddenCommand cannot see.
            $target = @($command.CommandElements)[0]
            $isSeamInvocation = $isLibrary -and
                $target -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $scriptBlockVariables.Contains([string] $target.VariablePath.UserPath)
            if (-not $isSeamInvocation) {
                $message = if ($isLibrary) {
                    "A library may only invoke a script block it declares (a [scriptblock] parameter or a `{ … }` assignment): $($command.Extent.Text)"
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
