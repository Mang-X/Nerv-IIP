# Script-Governance:
#   Category: check
#   SideEffects:
#     - None
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot '../../../lib/ScriptAutomation.ps1')

$tool = 'dotnet'
& $tool --info
