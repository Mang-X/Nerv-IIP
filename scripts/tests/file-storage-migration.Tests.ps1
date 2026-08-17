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
$removeScanningMigration = Join-Path $repoRoot 'backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/20260817080323_RemoveFileStorageScanning.cs'
$connectionVariable = 'NERV_IIP_FILE_STORAGE_DB'
$originalConnection = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')

try {
    if (-not (Test-Path -LiteralPath $removeScanningMigration)) {
        throw 'RemoveFileStorageScanning migration must exist.'
    }
    $removeScanningText = Get-Content -LiteralPath $removeScanningMigration -Raw
    $downMarker = 'protected override void Down(MigrationBuilder migrationBuilder)'
    $downIndex = $removeScanningText.IndexOf($downMarker, [StringComparison]::Ordinal)
    if ($downIndex -lt 0) {
        throw 'RemoveFileStorageScanning migration must define Down.'
    }
    $upText = $removeScanningText.Substring(0, $downIndex)
    $downText = $removeScanningText.Substring($downIndex)
    foreach ($expected in @(
        'scan_status IS DISTINCT FROM ''clean''',
        'status = ''deleted''',
        'deleted_at_utc = COALESCE(deleted_at_utc, CURRENT_TIMESTAMP)',
        'physical_delete_after_utc = COALESCE(physical_delete_after_utc, CURRENT_TIMESTAMP)',
        "'scan-removal:' || COALESCE(scan_status, 'unknown')")) {
        if (-not $upText.Contains($expected, [StringComparison]::Ordinal)) {
            throw "RemoveFileStorageScanning Up must contain '$expected'."
        }
    }
    $upOperations = @(
        @{ Kind = 'DropIndex'; Name = 'IX_stored_files_scan_status_status' },
        @{ Kind = 'DropColumn'; Name = 'scan_detail' },
        @{ Kind = 'DropColumn'; Name = 'scan_status' },
        @{ Kind = 'DropColumn'; Name = 'scanned_at_utc' })
    $previousIndex = $upText.IndexOf('scan_status IS DISTINCT FROM ''clean''', [StringComparison]::Ordinal)
    foreach ($operation in $upOperations) {
        $pattern = '(?s)migrationBuilder\.' + $operation.Kind + '\(\s*name: "' + [regex]::Escape($operation.Name) + '"\s*,\s*schema: "filestorage"\s*,\s*table: "stored_files"'
        $matches = [regex]::Matches($upText, $pattern)
        if ($matches.Count -ne 1) {
            throw "RemoveFileStorageScanning Up must contain exactly one $($operation.Kind) for '$($operation.Name)'."
        }
        if ($matches[0].Index -le $previousIndex) {
            throw "RemoveFileStorageScanning Up operation '$($operation.Name)' is out of order."
        }
        $previousIndex = $matches[0].Index
    }
    foreach ($operation in @(
        @{ Kind = 'AddColumn'; Name = 'scan_detail' },
        @{ Kind = 'AddColumn'; Name = 'scan_status' },
        @{ Kind = 'AddColumn'; Name = 'scanned_at_utc' },
        @{ Kind = 'CreateIndex'; Name = 'IX_stored_files_scan_status_status' })) {
        $pattern = '(?s)migrationBuilder\.' + $operation.Kind + '(?:<[^>]+>)?\(\s*name: "' + [regex]::Escape($operation.Name) + '"\s*,\s*schema: "filestorage"\s*,\s*table: "stored_files"'
        if ([regex]::Matches($downText, $pattern).Count -ne 1) {
            throw "RemoveFileStorageScanning Down must contain exactly one $($operation.Kind) for '$($operation.Name)'."
        }
    }

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
        "Host=localhost;Port=5432;Database=nerv_iip_iam;Username=nerv;Password=$secret",
        'Process')
    $wrongDatabaseFailure = $null
    try {
        Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-File', $migrationScript, '-ValidateOnly') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 60 `
            -Name 'file-storage-migration-wrong-database' | Out-Null
    }
    catch {
        $wrongDatabaseFailure = $_
    }
    if ($null -eq $wrongDatabaseFailure) {
        throw 'FileStorage migration validation must reject an unexpected target database.'
    }
    foreach ($expected in @('nerv_iip_iam', 'nerv_iip_filestorage')) {
        if (-not $wrongDatabaseFailure.Exception.Message.Contains($expected)) {
            throw "Wrong-database diagnostics must contain '$expected'. Output: $($wrongDatabaseFailure.Exception.Message)"
        }
    }
    if ($wrongDatabaseFailure.Exception.Message.Contains($secret)) {
        throw 'Wrong-database diagnostics must not disclose the connection string password.'
    }

    [Environment]::SetEnvironmentVariable(
        $connectionVariable,
        "Host=localhost;Port=5432;Database=nerv_iip_filestorage_release;Username=nerv;Password=$secret",
        'Process')
    $validResult = Invoke-NativeCommandOutput `
        -Command 'pwsh' `
        -Arguments @(
            '-NoProfile', '-File', $migrationScript,
            '-ValidateOnly',
            '-ReleaseId', 'release-man-533-test',
            '-ExpectedDatabase', 'nerv_iip_filestorage_release') `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 60 `
        -Name 'file-storage-migration-valid-config'
    $validText = @($validResult.Stdout, $validResult.Stderr) -join [Environment]::NewLine
    $migrationText = Get-Content -LiteralPath $migrationScript -Raw
    $connectionResolutionIndex = $migrationText.IndexOf('$connectionArgumentIndex = -1', [StringComparison]::Ordinal)
    $validateOnlyIndex = $migrationText.IndexOf('if ($ValidateOnly)', [StringComparison]::Ordinal)
    if ($connectionResolutionIndex -lt 0 -or $validateOnlyIndex -lt 0 -or $connectionResolutionIndex -gt $validateOnlyIndex) {
        throw 'The valid ValidateOnly path must execute exact sensitive-argument resolution before it exits without restore or database access.'
    }
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
