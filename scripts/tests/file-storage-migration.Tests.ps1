# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs FileStorage migration entry validation without connecting to a database
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores the scoped migration connection environment variable
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$migrationScript = Join-Path $repoRoot 'scripts/install/migrate-file-storage.ps1'
$connectionVariable = 'NERV_IIP_FILE_STORAGE_DB'
$originalConnection = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')

try {
    [Environment]::SetEnvironmentVariable($connectionVariable, $null, 'Process')
    $missingFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-File', $migrationScript, '-ValidateOnly') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 60 `
            -Name 'file-storage-migration-missing-config' | Out-Null
    }
    catch {
        $missingFailure = $_
    }
    if ($null -eq $missingFailure) {
        throw 'FileStorage migration validation must reject a missing connection environment variable.'
    }
    if (-not $missingFailure.Exception.Message.Contains($connectionVariable)) {
        throw "Missing-connection diagnostics must name $connectionVariable. Output: $($missingFailure.Exception.Message)"
    }

    $secret = 'migration-test-secret'
    [Environment]::SetEnvironmentVariable(
        $connectionVariable,
        "Host=localhost;Port=5432;Database=nerv_iip_filestorage_release;Username=nerv;Password=$secret",
        'Process')
    $validResult = Invoke-NativeCommandOutput `
        -Command 'pwsh' `
        -Arguments @('-NoProfile', '-File', $migrationScript, '-ValidateOnly', '-ReleaseId', 'release-man-533-test') `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 60 `
        -Name 'file-storage-migration-valid-config'
    $validText = @($validResult.Stdout, $validResult.Stderr) -join [Environment]::NewLine
    foreach ($expected in @('release-man-533-test', 'service=file-storage', 'targetDatabase=nerv_iip_filestorage_release')) {
        if (-not $validText.Contains($expected)) {
            throw "FileStorage migration validation output must contain '$expected'. Output: $validText"
        }
    }
    if ($validText.Contains($secret)) {
        throw 'FileStorage migration validation output must not disclose the connection string password.'
    }
}
finally {
    [Environment]::SetEnvironmentVariable($connectionVariable, $originalConnection, 'Process')
}

Write-Host 'FileStorage migration entry tests passed.'
