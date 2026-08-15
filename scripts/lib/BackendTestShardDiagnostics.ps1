# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads the failure records and TRX paths its callers hand it
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

function Get-BackendTestShardFailureDiagnostics {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.ErrorRecord] $ErrorRecord,
        [Parameter(Mandatory)] [string] $TrxFilePrefix
    )

    $bufferedStdout = $ErrorRecord.Exception.Data['Stdout']
    $bufferedStderr = $ErrorRecord.Exception.Data['Stderr']
    if ($null -eq $bufferedStdout) { $bufferedStdout = $ErrorRecord.Exception.Message }
    if ($null -eq $bufferedStderr) { $bufferedStderr = '' }

    return @(
        "${TrxFilePrefix}: buffered shard stdout (redacted)",
        (Protect-ScriptAutomationText ([string] $bufferedStdout)),
        "${TrxFilePrefix}: buffered shard stderr (redacted)",
        (Protect-ScriptAutomationText ([string] $bufferedStderr))
    ) -join [Environment]::NewLine
}
