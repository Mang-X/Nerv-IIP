# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Probes PostgreSQL, creates one job-scoped database, and runs one governed test member
#   Writes:
#     - bin/ and obj/ under the selected test project
#     - The caller-owned TRX results directory and machine-readable lane summary
#     - artifacts/script-logs/**
#   Cleanup:
#     - Drops the job-scoped database in finally and records the cleanup result
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL psql client
#     - NERV_IIP_TEST_POSTGRES targeting a PostgreSQL administration database

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $MemberId,
    [Parameter(Mandatory)] [string] $DatabaseName,
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $SummaryPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'postgres-test-lane.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/PostgresTestLane.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$member = Import-NervPostgresTestLaneMember -ManifestPath $ManifestPath -MemberId $MemberId -RepositoryRoot $repoRoot
if ($DatabaseName -cnotmatch "^$([regex]::Escape([string]$member.databasePrefix))_[a-z0-9_]{1,20}$" -or $DatabaseName.Length -gt 63) { throw "DatabaseName must use the governed '$($member.databasePrefix)' prefix and PostgreSQL-safe characters." }
$adminConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
if ([string]::IsNullOrWhiteSpace($adminConnection)) { throw 'Set NERV_IIP_TEST_POSTGRES before PostgreSQL lane discovery.' }

function ConvertTo-PgEnvironment {
    param([Parameter(Mandatory)] [string] $ConnectionString)
    $values = @{}
    foreach ($segment in $ConnectionString.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        $parts = $segment.Split('=', 2)
        if ($parts.Count -eq 2) { $values[$parts[0].Trim().ToLowerInvariant()] = $parts[1] }
    }
    $required = @{ host = 'PGHOST'; port = 'PGPORT'; database = 'PGDATABASE'; username = 'PGUSER'; password = 'PGPASSWORD' }
    foreach ($key in $required.Keys) { if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$values[$key])) { throw "NERV_IIP_TEST_POSTGRES is missing '$key'." } }
    return [pscustomobject]@{ values = $values; environment = $required }
}

$parsed = ConvertTo-PgEnvironment $adminConnection
$savedPg = @{}
foreach ($entry in $parsed.environment.GetEnumerator()) {
    $savedPg[$entry.Value] = [Environment]::GetEnvironmentVariable($entry.Value)
    [Environment]::SetEnvironmentVariable($entry.Value, [string]$parsed.values[$entry.Key])
}
$summary = [ordered]@{ schemaVersion = 1; lane = 'postgres'; memberId = $MemberId; service = [string]$member.service; database = $DatabaseName; readiness = 'not-run'; postgresVersion = ''; expected = @($member.expectedTestIdentities).Count; discovered = 0; passed = 0; failed = 0; skipped = 0; diagnostics = $null; cleanup = 'not-run' }
$failure = $null
$databaseCreated = $false
$savedTestPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
try {
    $probe = Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', 'SELECT current_setting(''server_version'')') -WorkingDirectory $repoRoot -Name 'postgres-lane-readiness'
    $summary.readiness = 'passed'
    $summary.postgresVersion = $probe.Stdout.Trim()
    Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE `"$DatabaseName`"") -WorkingDirectory $repoRoot -Name 'postgres-lane-create-database' | Out-Null
    $databaseCreated = $true
    $targetConnection = "Host=$($parsed.values.host);Port=$($parsed.values.port);Database=$DatabaseName;Username=$($parsed.values.username);Password=$($parsed.values.password)"
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $targetConnection)
    $discovery = Invoke-DotNetOutput -Name 'postgres-lane-discovery' -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--list-tests', '--filter', [string]$member.filter)
    $expectedIdentitySet = [Collections.Generic.HashSet[string]]::new([string[]]@($member.expectedTestIdentities), [StringComparer]::Ordinal)
    $discovered = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $expectedIdentitySet.Contains([string]$_) })
    $summary.discovered = $discovered.Count
    if ($discovered.Count -ne @($member.expectedTestIdentities).Count) { throw "PostgreSQL lane discovery expected $(@($member.expectedTestIdentities).Count) frozen tests but found $($discovered.Count)." }
    [IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
    Invoke-DotNetOutput -Name 'postgres-lane-execution' -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--no-restore', '--filter', [string]$member.filter, '--logger', 'trx;LogFilePrefix=postgres-inventory', '--results-directory', $ResultsDirectory) | Out-Null
    $trxResult = Get-NervPostgresTrxResult -ResultsDirectory $ResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
    $summary.passed = $trxResult.passed
    $summary.failed = $trxResult.failed
    $summary.skipped = $trxResult.skipped
    if (-not $trxResult.valid) {
        if (-not $trxResult.identitiesMatch) { throw 'PostgreSQL lane TRX identities do not equal the frozen member identities.' }
        throw "PostgreSQL lane requires $($summary.expected) passed, 0 failed and 0 skipped; observed $($summary.passed) passed, $($summary.failed) failed and $($summary.skipped) skipped."
    }
}
catch {
    $failure = $_
    try {
        if (Test-Path -LiteralPath $ResultsDirectory -PathType Container) {
            $trxResult = Get-NervPostgresTrxResult -ResultsDirectory $ResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
            $summary.passed = $trxResult.passed
            $summary.failed = $trxResult.failed
            $summary.skipped = $trxResult.skipped
        }
    }
    catch { Write-Diagnostic -Level 'WARN' -Message "PostgreSQL failure TRX could not be summarized: $($_.Exception.Message)" }
}
finally {
    if ($null -ne $failure -and $databaseCreated) {
        try {
            [Environment]::SetEnvironmentVariable('PGDATABASE', $DatabaseName)
            $diagnosticSchemaLiterals = @($member.diagnosticSchemas | ForEach-Object { "'$($_)'" }) -join ', '
            $diagnosticQuery = @"
SELECT json_build_object(
  'schemas', COALESCE((SELECT json_agg(nspname ORDER BY nspname) FROM pg_namespace WHERE nspname IN ($diagnosticSchemaLiterals)), '[]'::json),
  'relations', COALESCE((SELECT json_agg(json_build_object('schema', n.nspname, 'name', c.relname, 'estimatedRows', GREATEST(c.reltuples::bigint, 0)) ORDER BY n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ($diagnosticSchemaLiterals) AND c.relkind IN ('r', 'p')), '[]'::json)
)::text;
"@
            $diagnostic = Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', $diagnosticQuery) -WorkingDirectory $repoRoot -Name 'postgres-lane-failure-diagnostics'
            $summary.diagnostics = $diagnostic.Stdout.Trim() | ConvertFrom-Json -Depth 10
        }
        catch {
            $summary.diagnostics = [ordered]@{ capture = 'failed'; message = Protect-ScriptAutomationText $_.Exception.Message }
            Write-Diagnostic -Level 'WARN' -Message "PostgreSQL failure diagnostics could not be captured: $($_.Exception.Message)"
        }
    }
    try {
        if ($databaseCreated) {
            [Environment]::SetEnvironmentVariable('PGDATABASE', [string]$parsed.values.database)
            Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE `"$DatabaseName`" WITH (FORCE)") -WorkingDirectory $repoRoot -Name 'postgres-lane-drop-database' | Out-Null
        }
        $summary.cleanup = 'passed'
    }
    catch { $summary.cleanup = 'failed'; if ($null -eq $failure) { $failure = $_ } }
    foreach ($entry in $savedPg.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
    $summaryDirectory = Split-Path -Parent $SummaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) { [IO.Directory]::CreateDirectory($summaryDirectory) | Out-Null }
    [IO.File]::WriteAllText($SummaryPath, (($summary | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
}
if ($null -ne $failure) { throw $failure }
Write-Host "PostgreSQL lane member '$MemberId' passed: discovered=$($summary.discovered) passed=$($summary.passed) skipped=$($summary.skipped) cleanup=$($summary.cleanup)."
