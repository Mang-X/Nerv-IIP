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

$allowedCategories = @('check', 'verify', 'generate', 'release-install')
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
                    -not (
                        $relative -eq 'scripts/check-script-governance.ps1' -or
                        $relative -like 'scripts/lib/*' -or
                        $relative -like 'scripts/tests/*'
                    )
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

function Test-ScriptGovernance {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath,

        [Parameter(Mandatory)]
        [hashtable] $Baseline
    )

    $relativePath = Get-RepoRelativePath -CandidatePath $ScriptPath
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

    $categoryMatch = [regex]::Match($content, '(?m)^\s*#\s*Category:\s*(?<category>[A-Za-z-]+)\s*$')
    if (-not $categoryMatch.Success) {
        Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'MissingCategory' -Message 'Missing Script-Governance Category.'
    }
    else {
        $category = $categoryMatch.Groups['category'].Value.ToLowerInvariant()
        if ($allowedCategories -notcontains $category) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'InvalidCategory' -Message "Invalid Script-Governance Category '$category'."
        }
    }

    $commands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true)
    $dotSourcesHelper = $false

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
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'DynamicInvocation' -Line $line -Message "Dynamic invocation is not allowed outside ScriptAutomation.ps1: $($command.Extent.Text)"
            continue
        }

        if ([string]::IsNullOrWhiteSpace($commandName)) {
            continue
        }

        if ($forbiddenCommands -contains $commandName.ToLowerInvariant()) {
            Add-GovernanceViolation -Violations $violations -Path $relativePath -Rule 'ForbiddenCommand' -Line $line -Message "Direct command '$commandName' must be wrapped by ScriptAutomation.ps1."
        }
    }

    if (-not $dotSourcesHelper) {
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
