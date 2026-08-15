# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Enumerates PostgreSQL databases and deletes inactive stale nerv UUIDv7 test databases only when -Apply is supplied
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores all temporary PG* process environment variables
#     - Leaves PostgreSQL and all non-candidate databases running
#   Requires:
#     - PowerShell 7
#     - PostgreSQL psql client
#     - NERV_IIP_TEST_POSTGRES targeting a PostgreSQL administration database

[CmdletBinding()]
param(
    [ValidateRange(1, 720)]
    [int] $MinimumAgeHours = 24,

    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/PostgresTestDatabaseCleanup.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$baseConnectionString = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
if ([string]::IsNullOrWhiteSpace($baseConnectionString)) {
    throw 'Set NERV_IIP_TEST_POSTGRES before previewing or applying stale PostgreSQL test database cleanup.'
}

$pgEnvironment = ConvertTo-NervPostgresAdminEnvironment -ConnectionString $baseConnectionString
$hostName = [string] $pgEnvironment.PGHOST
$port = [string] $pgEnvironment.PGPORT

Invoke-WithScopedEnvironment -Variables $pgEnvironment -ScriptBlock {
    $listResult = Invoke-NativeCommandOutput `
        -Command 'psql' `
        -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', 'SELECT datname FROM pg_database WHERE NOT datistemplate ORDER BY datname') `
        -WorkingDirectory $repoRoot `
        -Name 'postgres-test-database-cleanup-list'
    $databaseNames = @($listResult.Stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $getActiveSessionCount = {
        param([string] $DatabaseName)
        $result = Invoke-NativeCommandOutput `
            -Command 'psql' `
            -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', "SELECT count(*) FROM pg_stat_activity WHERE datname = '$DatabaseName'") `
            -WorkingDirectory $repoRoot `
            -Name "postgres-test-database-cleanup-active-$DatabaseName"
        return [int] $result.Stdout.Trim()
    }
    $dropDatabase = {
        param([string] $DatabaseName)
        Invoke-NativeCommandOutput `
            -Command 'psql' `
            -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE `"$DatabaseName`"") `
            -WorkingDirectory $repoRoot `
            -Name "postgres-test-database-cleanup-drop-$DatabaseName" | Out-Null
    }
    $databaseExists = {
        param([string] $DatabaseName)
        $result = Invoke-NativeCommandOutput `
            -Command 'psql' `
            -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', "SELECT count(*) FROM pg_database WHERE datname = '$DatabaseName'") `
            -WorkingDirectory $repoRoot `
            -Name "postgres-test-database-cleanup-readback-$DatabaseName"
        return [int] $result.Stdout.Trim() -ne 0
    }

    $mode = if ($Apply) { 'apply' } else { 'preview' }
    Write-Diagnostic "PostgreSQL stale test database cleanup mode=$mode host=$hostName port=$port minimumAgeHours=$MinimumAgeHours."
    $results = @(Invoke-NervPostgresTestDatabaseCleanup `
        -DatabaseNames $databaseNames `
        -NowUtc ([DateTimeOffset]::UtcNow) `
        -MinimumAge ([TimeSpan]::FromHours($MinimumAgeHours)) `
        -Apply:$Apply `
        -GetActiveSessionCountAction $getActiveSessionCount `
        -DropDatabaseAction $dropDatabase `
        -DatabaseExistsAction $databaseExists)

    foreach ($result in $results) {
        Write-Diagnostic "PostgreSQL test database=$($result.DatabaseName) createdAtUtc=$($result.CreatedAtUtc.ToString('O')) outcome=$($result.Outcome)."
    }
    Write-Diagnostic "PostgreSQL stale test database cleanup completed: mode=$mode candidates=$($results.Count)."
}
