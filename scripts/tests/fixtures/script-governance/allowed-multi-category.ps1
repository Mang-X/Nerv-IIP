# Script-Governance:
#   Category: verify, generate
#   SideEffects:
#     - Writes generated test artifacts
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot '../../../lib/ScriptAutomation.ps1')

Write-Diagnostic 'multi-category fixture'
