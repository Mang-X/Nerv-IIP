# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads and structurally validates the CI required-summary workflow contract
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

[CmdletBinding()]
param(
    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/CiRequiredSummary.ps1')

$findings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $WorkflowPath -RepositoryRoot $repoRoot)
if ($findings.Count -gt 0) {
    Write-Host 'CI required-summary governance failed:'
    foreach ($finding in $findings) {
        Write-Host "  $finding"
    }
    exit 1
}

Write-Output 'CI required-summary governance passed: CI Summary fails closed over the four current required jobs.'
