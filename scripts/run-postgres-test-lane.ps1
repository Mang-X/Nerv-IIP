# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Probes PostgreSQL and creates one job-scoped database per governed test member
#   Writes:
#     - bin/ and obj/ under the selected test project
#     - The caller-owned TRX results directory and machine-readable lane summary
#     - artifacts/script-logs/**
#   Cleanup:
#     - Drops each job-scoped database in finally and records member and aggregate cleanup results
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL psql client
#     - NERV_IIP_TEST_POSTGRES targeting a PostgreSQL administration database

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $MemberId,
    [Parameter(Mandatory)] [string] $DatabaseSuffix,
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $SummaryPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'postgres-test-lane.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/PostgresTestLane.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($MemberId.Count -eq 0) { throw 'At least one PostgreSQL lane member is required.' }
if ($DatabaseSuffix -cnotmatch '^[a-z0-9_]{1,20}$') { throw 'DatabaseSuffix must contain 1-20 PostgreSQL-safe lowercase characters.' }
$memberIdSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$selectedMembers = @(
    foreach ($selectedMemberId in $MemberId) {
        if ([string]::IsNullOrWhiteSpace($selectedMemberId) -or -not $memberIdSet.Add($selectedMemberId)) { throw "PostgreSQL lane member ids must be non-empty and unique; observed '$selectedMemberId'." }
        Import-NervPostgresTestLaneMember -ManifestPath $ManifestPath -MemberId $selectedMemberId -RepositoryRoot $repoRoot
    }
)
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
$summary = [ordered]@{ schemaVersion = 2; lane = 'postgres'; selectedMemberIds = @($MemberId); readiness = 'not-run'; postgresVersion = ''; expected = 0; discovered = 0; passed = 0; failed = 0; skipped = 0; cleanup = 'not-run'; members = @() }
$memberSummaries = [Collections.Generic.List[object]]::new()
$failure = $null
$savedTestPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
try {
    $probe = Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', 'SELECT current_setting(''server_version'')') -WorkingDirectory $repoRoot -Name 'postgres-lane-readiness'
    $summary.readiness = 'passed'
    $summary.postgresVersion = $probe.Stdout.Trim()
    foreach ($member in $selectedMembers) {
        $databaseName = "$([string]$member.databasePrefix)_$DatabaseSuffix"
        if ($databaseName.Length -gt 63) { throw "Database name for PostgreSQL lane member '$($member.id)' exceeds 63 characters." }
        $memberResultsDirectory = Join-Path $ResultsDirectory ([string]$member.id)
        $memberSummary = [ordered]@{ memberId = [string]$member.id; service = [string]$member.service; database = $databaseName; expected = @($member.expectedTestIdentities).Count; discovered = 0; passed = 0; failed = 0; skipped = 0; diagnostics = $null; cleanup = 'not-run'; outcome = 'not-run' }
        $memberFailure = $null
        $databaseCreated = $false
        try {
            Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE `"$databaseName`"") -WorkingDirectory $repoRoot -Name "postgres-lane-$($member.id)-create-database" | Out-Null
            $databaseCreated = $true
            $targetConnection = "Host=$($parsed.values.host);Port=$($parsed.values.port);Database=$databaseName;Username=$($parsed.values.username);Password=$($parsed.values.password)"
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $targetConnection)
            $discovery = Invoke-DotNetOutput -Name "postgres-lane-$($member.id)-discovery" -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--list-tests', '--filter', [string]$member.filter)
            $expectedIdentitySet = [Collections.Generic.HashSet[string]]::new([string[]]@($member.expectedTestIdentities), [StringComparer]::Ordinal)
            $discovered = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $expectedIdentitySet.Contains([string]$_) })
            $memberSummary.discovered = $discovered.Count
            if ($discovered.Count -ne @($member.expectedTestIdentities).Count) { throw "PostgreSQL lane member '$($member.id)' discovery expected $(@($member.expectedTestIdentities).Count) frozen tests but found $($discovered.Count)." }
            [IO.Directory]::CreateDirectory($memberResultsDirectory) | Out-Null
            Invoke-DotNetOutput -Name "postgres-lane-$($member.id)-execution" -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--no-restore', '--filter', [string]$member.filter, '--logger', "trx;LogFilePrefix=postgres-$($member.id)", '--results-directory', $memberResultsDirectory) | Out-Null
            $trxResult = Get-NervPostgresTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
            $memberSummary.passed = $trxResult.passed
            $memberSummary.failed = $trxResult.failed
            $memberSummary.skipped = $trxResult.skipped
            if (-not $trxResult.valid) {
                if (-not $trxResult.identitiesMatch) { throw "PostgreSQL lane member '$($member.id)' TRX identities do not equal its frozen identities." }
                throw "PostgreSQL lane member '$($member.id)' requires $($memberSummary.expected) passed, 0 failed and 0 skipped; observed $($memberSummary.passed) passed, $($memberSummary.failed) failed and $($memberSummary.skipped) skipped."
            }
            $memberSummary.outcome = 'passed'
        }
        catch {
            $memberFailure = $_
            $memberSummary.outcome = 'failed'
            try {
                if (Test-Path -LiteralPath $memberResultsDirectory -PathType Container) {
                    $trxResult = Get-NervPostgresTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
                    $memberSummary.passed = $trxResult.passed
                    $memberSummary.failed = $trxResult.failed
                    $memberSummary.skipped = $trxResult.skipped
                }
            }
            catch { Write-Diagnostic -Level 'WARN' -Message "PostgreSQL member '$($member.id)' failure TRX could not be summarized: $($_.Exception.Message)" }
        }
        finally {
            if ($null -ne $memberFailure -and $databaseCreated) {
                try {
                    [Environment]::SetEnvironmentVariable('PGDATABASE', $databaseName)
                    $diagnosticSchemaLiterals = @($member.diagnosticSchemas | ForEach-Object { "'$($_)'" }) -join ', '
                    $diagnosticQuery = @"
SELECT json_build_object(
  'schemas', COALESCE((SELECT json_agg(nspname ORDER BY nspname) FROM pg_namespace WHERE nspname IN ($diagnosticSchemaLiterals)), '[]'::json),
  'relations', COALESCE((SELECT json_agg(json_build_object('schema', n.nspname, 'name', c.relname, 'estimatedRows', GREATEST(c.reltuples::bigint, 0)) ORDER BY n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ($diagnosticSchemaLiterals) AND c.relkind IN ('r', 'p')), '[]'::json)
)::text;
"@
                    $diagnostic = Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', $diagnosticQuery) -WorkingDirectory $repoRoot -Name "postgres-lane-$($member.id)-failure-diagnostics"
                    $memberSummary.diagnostics = $diagnostic.Stdout.Trim() | ConvertFrom-Json -Depth 10
                }
                catch {
                    $memberSummary.diagnostics = [ordered]@{ capture = 'failed'; message = Protect-ScriptAutomationText $_.Exception.Message }
                    Write-Diagnostic -Level 'WARN' -Message "PostgreSQL member '$($member.id)' failure diagnostics could not be captured: $($_.Exception.Message)"
                }
            }
            try {
                if ($databaseCreated) {
                    [Environment]::SetEnvironmentVariable('PGDATABASE', [string]$parsed.values.database)
                    Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE `"$databaseName`" WITH (FORCE)") -WorkingDirectory $repoRoot -Name "postgres-lane-$($member.id)-drop-database" | Out-Null
                }
                $memberSummary.cleanup = 'passed'
            }
            catch {
                $memberSummary.cleanup = 'failed'
                $memberSummary.outcome = 'failed'
                if ($null -eq $memberFailure) { $memberFailure = $_ }
            }
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
            $memberSummaries.Add([pscustomobject]$memberSummary)
        }
        if ($null -ne $memberFailure -and $null -eq $failure) { $failure = $memberFailure }
    }
}
catch {
    $failure = $_
}
finally {
    foreach ($entry in $savedPg.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
    $summary.members = @($memberSummaries)
    foreach ($memberSummary in $memberSummaries) {
        $summary.expected += [int]$memberSummary.expected
        $summary.discovered += [int]$memberSummary.discovered
        $summary.passed += [int]$memberSummary.passed
        $summary.failed += [int]$memberSummary.failed
        $summary.skipped += [int]$memberSummary.skipped
    }
    if ($memberSummaries.Count -ne $MemberId.Count) {
        $summary.cleanup = 'incomplete'
        if ($null -eq $failure) { $failure = [InvalidOperationException]::new("PostgreSQL lane selected $($MemberId.Count) members but summarized $($memberSummaries.Count).") }
    }
    elseif (@($memberSummaries | Where-Object { -not [string]::Equals([string]$_.cleanup, 'passed', [StringComparison]::Ordinal) }).Count -gt 0) { $summary.cleanup = 'failed' }
    else { $summary.cleanup = 'passed' }
    try { Assert-NervPostgresTestLaneSummary -SelectedMemberIds @($MemberId) -MemberSummaries @($memberSummaries) }
    catch { if ($null -eq $failure) { $failure = $_ } }
    $summaryDirectory = Split-Path -Parent $SummaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) { [IO.Directory]::CreateDirectory($summaryDirectory) | Out-Null }
    [IO.File]::WriteAllText($SummaryPath, (($summary | ConvertTo-Json -Depth 10) + "`n"), [Text.UTF8Encoding]::new($false))
}
if ($null -ne $failure) { throw $failure }
Write-Host "PostgreSQL lane members '$($MemberId -join ',')' passed: discovered=$($summary.discovered) passed=$($summary.passed) skipped=$($summary.skipped) cleanup=$($summary.cleanup)."
