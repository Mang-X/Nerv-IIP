function Save-BackendTestShardTimeoutDiagnostics {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.ErrorRecord] $ErrorRecord,
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string] $TrxFilePrefix
    )

    $timeoutStdout = $ErrorRecord.Exception.Data['Stdout']
    $timeoutStderr = $ErrorRecord.Exception.Data['Stderr']
    New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    if ($null -eq $timeoutStdout) { $timeoutStdout = $ErrorRecord.Exception.Message }
    if ($null -eq $timeoutStderr) { $timeoutStderr = '' }
    Set-Content -LiteralPath (Join-Path $ResultsDirectory "$TrxFilePrefix.timeout.stdout.log") -Value ([string] $timeoutStdout) -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $ResultsDirectory "$TrxFilePrefix.timeout.stderr.log") -Value ([string] $timeoutStderr) -Encoding utf8NoBOM
}
