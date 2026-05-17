# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts a background job
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed child process trees
#   Requires:
#     - PowerShell 7

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot '../../../lib/ScriptAutomation.ps1')

Start-Job -ScriptBlock { dotnet --info } | Out-Null
