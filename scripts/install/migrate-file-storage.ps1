# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Applies repository FileStorage EF Core migrations to the explicitly configured PostgreSQL database
#     - Updates only the filestorage schema and its __EFMigrationsHistory table
#   Writes:
#     - FileStorage migration history and schema objects in the target database
#     - bin/ and obj/ build outputs for the FileStorage Infrastructure project
#     - artifacts/script-logs/**
#   Cleanup:
#     - Does not delete, recreate, or roll back the target database
#     - Leaves the target database and applied migrations intact
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - NERV_IIP_FILE_STORAGE_DB set in the current process to the target PostgreSQL connection string

[CmdletBinding()]
param(
    [string] $ReleaseId = "release-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))",

    [string] $CorrelationId = [Guid]::NewGuid().ToString('D'),

    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw 'ReleaseId may contain only letters, digits, dot, underscore, and hyphen.'
}

$connectionVariable = 'NERV_IIP_FILE_STORAGE_DB'
$connectionString = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "$connectionVariable must be set in the current process before running the FileStorage migrator."
}

$databaseMatch = [regex]::Match($connectionString, '(?i)(?:^|;)\s*Database\s*=\s*([^;]+)')
if (-not $databaseMatch.Success -or [string]::IsNullOrWhiteSpace($databaseMatch.Groups[1].Value)) {
    throw "$connectionVariable must include a non-empty Database field."
}

$targetDatabase = $databaseMatch.Groups[1].Value.Trim()
$validationLogDirectory = New-ScriptAutomationLogDirectory -Name 'file-storage-migration-validation'
$summary = "releaseId=$ReleaseId service=file-storage dbProfile=PostgreSQL targetDatabase=$targetDatabase migrationFrom=database-current migrationTo=repository-latest seedStep=none correlationId=$CorrelationId logPath=$validationLogDirectory"
Write-Diagnostic $summary

if ($ValidateOnly) {
    Write-Diagnostic 'FileStorage migration configuration validation completed; no database command was executed.'
    exit 0
}

$project = 'backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Nerv.IIP.FileStorage.Infrastructure.csproj'
$restore = Invoke-DotNet `
    -Arguments @('tool', 'restore') `
    -WorkingDirectory $root `
    -TimeoutSeconds 300 `
    -Name "file-storage-migration-tool-restore-$ReleaseId"

$migrationArguments = @(
    'tool', 'run', 'dotnet-ef',
    'database', 'update',
    '--project', $project,
    '--context', 'ApplicationDbContext',
    '--connection', $connectionString
)
$migration = Invoke-DotNet `
    -Arguments $migrationArguments `
    -WorkingDirectory $root `
    -TimeoutSeconds 900 `
    -Name "file-storage-migration-apply-$ReleaseId" `
    -SensitiveArgumentIndexes @(10)

Write-Diagnostic "FileStorage migration completed releaseId=$ReleaseId service=file-storage targetDatabase=$targetDatabase correlationId=$CorrelationId restoreLog=$($restore.LogDirectory) migrationLog=$($migration.LogDirectory) exitCode=0."
