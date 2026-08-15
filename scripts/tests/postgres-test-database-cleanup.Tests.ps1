# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates stale PostgreSQL test database cleanup through injected actions
#   Writes:
#     - None
#   Cleanup:
#     - Does not connect to PostgreSQL or delete databases
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/PostgresTestDatabaseCleanup.ps1')
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function New-TestDatabaseName([DateTimeOffset]$CreatedAt, [string]$Prefix = 'nerv_cleanup') {
    $milliseconds = $CreatedAt.ToUnixTimeMilliseconds()
    return '{0}_{1:x12}7abc8def0123456789ab' -f $Prefix, $milliseconds
}

$now = [DateTimeOffset]::Parse('2026-08-13T05:00:00Z', [Globalization.CultureInfo]::InvariantCulture)
$minimumAge = [TimeSpan]::FromHours(24)
$exactlyOld = New-TestDatabaseName ($now - $minimumAge)
$older = New-TestDatabaseName ($now - [TimeSpan]::FromHours(48))
$young = New-TestDatabaseName ($now - $minimumAge + [TimeSpan]::FromMilliseconds(1))
$future = New-TestDatabaseName ($now + [TimeSpan]::FromMinutes(1))

$pgEnvironment = ConvertTo-NervPostgresAdminEnvironment -ConnectionString 'Host=127.0.0.1;Port=15432;Database=postgres;Username=postgres;Password=local-secret'
Assert-Contract ([string]::Equals([string] $pgEnvironment.PGHOST, '127.0.0.1', [StringComparison]::Ordinal)) 'The PostgreSQL host must be parsed from the Npgsql connection string.'
Assert-Contract ([string]::Equals([string] $pgEnvironment.PGPORT, '15432', [StringComparison]::Ordinal)) 'The PostgreSQL port must be parsed from the Npgsql connection string.'
Assert-Contract ([string]::Equals([string] $pgEnvironment.PGDATABASE, 'postgres', [StringComparison]::Ordinal)) 'Cleanup must always connect through the postgres administration database.'
Assert-Contract ([string]::Equals([string] $pgEnvironment.PGUSER, 'postgres', [StringComparison]::Ordinal)) 'The PostgreSQL username must be parsed from the Npgsql connection string.'
Assert-Contract ([string]::Equals([string] $pgEnvironment.PGPASSWORD, 'local-secret', [StringComparison]::Ordinal)) 'The PostgreSQL password must be parsed without logging it.'
$businessDatabaseRejected = $false
$businessConnectionString = 'Host=127.0.0.1;Port=15432;Database=nerv_iip_iam;Username=postgres;Password=local-secret'
try { ConvertTo-NervPostgresAdminEnvironment -ConnectionString $businessConnectionString | Out-Null } catch {
    $businessDatabaseRejected =
        $_.Exception.Message.Contains('Database=postgres', [StringComparison]::Ordinal) -and
        -not $_.Exception.Message.Contains('local-secret', [StringComparison]::Ordinal) -and
        -not $_.Exception.Message.Contains($businessConnectionString, [StringComparison]::Ordinal)
}
Assert-Contract $businessDatabaseRejected 'Cleanup must reject a base connection string that does not explicitly target Database=postgres.'

$parsed = ConvertFrom-NervPostgresTestDatabaseName -DatabaseName $older
Assert-Contract ($null -ne $parsed -and [string]::Equals([string] $parsed.DatabaseName, $older, [StringComparison]::Ordinal)) 'A canonical nerv UUIDv7 name must parse.'
Assert-Contract ($parsed.CreatedAtUtc -eq ($now - [TimeSpan]::FromHours(48))) 'UUIDv7 timestamp must round-trip exactly.'
foreach ($invalid in @(
    'postgres',
    'nerv_iip_iam',
    'other_cleanup_0198abcdefab7abc8def0123456789ab',
    'NERV_cleanup_0198abcdefab7abc8def0123456789ab',
    'nerv_cléanup_0198abcdefab7abc8def0123456789ab',
    'nerv_cleanup_0198abcdefab4abc8def0123456789ab',
    'nerv_cleanup_ffffffffffff7abc8def0123456789ab_extra'
)) {
    Assert-Contract ($null -eq (ConvertFrom-NervPostgresTestDatabaseName -DatabaseName $invalid)) "Invalid database name '$invalid' must fail closed."
}

$candidates = @(Get-NervStalePostgresTestDatabaseCandidate -DatabaseNames @($exactlyOld, $older, $young, $future, 'nerv_iip_iam') -NowUtc $now -MinimumAge $minimumAge)
Assert-Contract ($candidates.Count -eq 2) 'Only canonical names at or older than the exact threshold may become candidates.'
Assert-Contract ([string]::Equals([string] $candidates[0].DatabaseName, $older, [StringComparison]::Ordinal) -and [string]::Equals([string] $candidates[1].DatabaseName, $exactlyOld, [StringComparison]::Ordinal)) 'Candidates must be deterministic oldest-first.'

$dropped = [Collections.Generic.List[string]]::new()
$previewActiveChecks = 0
$dryRun = @(Invoke-NervPostgresTestDatabaseCleanup -DatabaseNames @($older) -NowUtc $now -MinimumAge $minimumAge -GetActiveSessionCountAction { param($name) $script:previewActiveChecks++; 0 } -DropDatabaseAction { param($name) $dropped.Add($name) } -DatabaseExistsAction { param($name) $false })
Assert-Contract ($dropped.Count -eq 0 -and [string]::Equals([string] $dryRun[0].Outcome, 'preview', [StringComparison]::Ordinal)) 'Default cleanup must only preview candidates.'
Assert-Contract ($previewActiveChecks -eq 1) 'Preview must verify that a stale candidate has no active sessions.'
$activePreview = @(Invoke-NervPostgresTestDatabaseCleanup -DatabaseNames @($older) -NowUtc $now -MinimumAge $minimumAge -GetActiveSessionCountAction { param($name) 1 } -DropDatabaseAction { param($name) throw 'must not drop in preview' } -DatabaseExistsAction { param($name) $true })
Assert-Contract ([string]::Equals([string] $activePreview[0].Outcome, 'skipped-active', [StringComparison]::Ordinal)) 'Preview must exclude an active stale database from deletable candidates.'

$active = @(Invoke-NervPostgresTestDatabaseCleanup -DatabaseNames @($older) -NowUtc $now -MinimumAge $minimumAge -Apply -GetActiveSessionCountAction { param($name) 1 } -DropDatabaseAction { param($name) throw 'must not drop active database' } -DatabaseExistsAction { param($name) $true })
Assert-Contract ([string]::Equals([string] $active[0].Outcome, 'skipped-active', [StringComparison]::Ordinal)) 'An active database must be skipped without DROP.'

$deleted = @(Invoke-NervPostgresTestDatabaseCleanup -DatabaseNames @($older) -NowUtc $now -MinimumAge $minimumAge -Apply -GetActiveSessionCountAction { param($name) 0 } -DropDatabaseAction { param($name) $dropped.Add($name) } -DatabaseExistsAction { param($name) $false })
Assert-Contract ($dropped.Count -eq 1 -and [string]::Equals([string] $dropped[0], $older, [StringComparison]::Ordinal) -and [string]::Equals([string] $deleted[0].Outcome, 'deleted', [StringComparison]::Ordinal)) 'Apply must delete and verify one inactive stale candidate.'

$readbackRejected = $false
try {
    Invoke-NervPostgresTestDatabaseCleanup -DatabaseNames @($older) -NowUtc $now -MinimumAge $minimumAge -Apply -GetActiveSessionCountAction { param($name) 0 } -DropDatabaseAction { param($name) } -DatabaseExistsAction { param($name) $true } | Out-Null
}
catch { $readbackRejected = $_.Exception.Message.Contains('still exists', [StringComparison]::Ordinal) }
Assert-Contract $readbackRejected 'A database that survives DROP must fail cleanup.'

$entryPath = Join-Path $repoRoot 'scripts/cleanup-stale-postgres-test-databases.ps1'
Assert-Contract (Test-Path -LiteralPath $entryPath -PathType Leaf) 'The governed cleanup entrypoint must exist.'
$entry = [IO.File]::ReadAllText($entryPath)
Assert-Contract ($entry.Contains("lib/ScriptAutomation.ps1", [StringComparison]::Ordinal)) 'The entrypoint must dot-source ScriptAutomation.ps1.'
Assert-Contract ($entry.Contains('NERV_IIP_TEST_POSTGRES', [StringComparison]::Ordinal)) 'The entrypoint must consume the established base variable.'
Assert-Contract (-not $entry.Contains('WITH (FORCE)', [StringComparison]::OrdinalIgnoreCase)) 'Stale cleanup must never force-disconnect active tests.'
Assert-Contract (-not $entry.Contains('NERV_IIP_TEST_POSTGRES_ADMIN', [StringComparison]::Ordinal)) 'No second admin variable contract may be introduced.'

Write-Output 'PostgreSQL test database cleanup contract tests passed.'
