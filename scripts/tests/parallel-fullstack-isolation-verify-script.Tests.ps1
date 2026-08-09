# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses and invokes the path-boundary helper from the verifier source
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$verifyScript = Join-Path $repoRoot 'scripts/verify-parallel-fullstack-isolation.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($verifyScript, [ref] $tokens, [ref] $parseErrors)
Assert-True ($parseErrors.Count -eq 0) 'Parallel full-stack verifier must parse before its path boundary can be tested.'
$pathBelow = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and [string]::Equals([string] $node.Name, 'Test-PathBelow', [StringComparison]::OrdinalIgnoreCase) }, $true))
Assert-True ($pathBelow.Count -eq 1) 'Parallel full-stack verifier must define exactly one Test-PathBelow helper.'
. ([scriptblock]::Create($pathBelow[0].Extent.Text))

$root = Join-Path ([System.IO.Path]::GetTempPath()) 'nerv-path-boundary-parent'
$caseVariant = Join-Path ([System.IO.Path]::GetTempPath()) 'NERV-PATH-BOUNDARY-PARENT'
$sibling = Join-Path ([System.IO.Path]::GetTempPath()) 'nerv-path-boundary-parent-sibling'
Assert-True (Test-PathBelow -Path "$root/child" -Parent $root) 'A true child path must be accepted.'
Assert-True ((Test-PathBelow -Path (Join-Path $caseVariant 'child') -Parent $root) -eq $IsWindows) 'Case-variant parent handling must match the operating-system path contract.'
Assert-True (-not (Test-PathBelow -Path (Join-Path $sibling 'child') -Parent $root)) 'A same-prefix sibling must not be accepted.'

Write-Host 'Parallel full-stack isolation verify-script path-boundary tests passed.'
