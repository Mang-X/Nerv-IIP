# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Probes PostgreSQL and Redis, creates one job-scoped database per governed member, and runs real Redis Streams tests
#   Writes:
#     - bin/ and obj/ under the selected test project
#     - The caller-owned TRX results directory and machine-readable lane summary
#     - artifacts/script-logs/**
#   Cleanup:
#     - Drops each job-scoped database and removes only Redis keys created by the selected member
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL psql client
#     - Redis redis-cli client
#     - NERV_IIP_TEST_POSTGRES targeting a PostgreSQL administration database
#     - NERV_IIP_TEST_REDIS targeting a Redis endpoint

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $MemberId,
    [Parameter(Mandatory)] [string] $DatabaseSuffix,
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $SummaryPath,
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'redis-cap-test-lane.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/RedisCapTestLane.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($MemberId.Count -eq 0) { throw 'At least one Redis/CAP lane member is required.' }
if ($DatabaseSuffix -cnotmatch '^[a-z0-9_]{1,20}$') { throw 'DatabaseSuffix must contain 1-20 PostgreSQL-safe lowercase characters.' }
$memberIdSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$selectedMembers = @(
    foreach ($selectedMemberId in $MemberId) {
        if ([string]::IsNullOrWhiteSpace($selectedMemberId) -or -not $memberIdSet.Add($selectedMemberId)) { throw "Redis/CAP lane member ids must be non-empty and unique; observed '$selectedMemberId'." }
        Import-NervRedisCapTestLaneMember -ManifestPath $ManifestPath -MemberId $selectedMemberId -RepositoryRoot $repoRoot
    }
)

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

function ConvertTo-RedisCliContext {
    param([Parameter(Mandatory)] [string] $ConnectionString)
    $segments = @($ConnectionString.Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
    if ($segments.Count -eq 0 -or $segments[0] -notmatch '^(?<host>\[[^\]]+\]|[^:]+):(?<port>[0-9]+)$') { throw 'NERV_IIP_TEST_REDIS must begin with host:port.' }
    $options = @{}
    foreach ($segment in @($segments | Select-Object -Skip 1)) {
        $parts = $segment.Split('=', 2)
        if ($parts.Count -eq 2) { $options[$parts[0].Trim().ToLowerInvariant()] = $parts[1].Trim() }
    }
    $arguments = @('--raw', '-h', $Matches.host.Trim('[', ']'), '-p', $Matches.port)
    if ($options.ContainsKey('ssl') -and [string]::Equals([string]$options.ssl, 'true', [StringComparison]::OrdinalIgnoreCase)) { $arguments += '--tls' }
    return [pscustomobject]@{ arguments = $arguments; password = if ($options.ContainsKey('password')) { [string]$options.password } else { $null } }
}

function Invoke-RedisCli {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string[]] $Arguments)
    Invoke-NativeCommandOutput -Command 'redis-cli' -Arguments (@($redisContext.arguments) + $Arguments) -WorkingDirectory $repoRoot -Name $Name
}

function Get-RedisKeys {
    param([Parameter(Mandatory)] [string] $Name)
    $scan = Invoke-RedisCli -Name $Name -Arguments @('--scan', '--pattern', '*')
    return @($scan.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-RedisStreamDiagnostics {
    param([Parameter(Mandatory)] [string[]] $Keys, [Parameter(Mandatory)] [string] $NamePrefix)
    $streams = [Collections.Generic.List[object]]::new()
    foreach ($key in $Keys) {
        $type = (Invoke-RedisCli -Name "$NamePrefix-type" -Arguments @('TYPE', $key)).Stdout.Trim()
        if (-not [string]::Equals($type, 'stream', [StringComparison]::Ordinal)) { continue }
        $lengthText = (Invoke-RedisCli -Name "$NamePrefix-length" -Arguments @('XLEN', $key)).Stdout.Trim()
        $groupsOutput = (Invoke-RedisCli -Name "$NamePrefix-groups" -Arguments @('XINFO', 'GROUPS', $key)).Stdout
        $groupLines = @($groupsOutput -split "`r?`n")
        $groupNames = [Collections.Generic.List[string]]::new()
        for ($index = 0; $index + 1 -lt $groupLines.Count; $index++) {
            if ([string]::Equals($groupLines[$index].Trim(), 'name', [StringComparison]::Ordinal)) { $groupNames.Add($groupLines[$index + 1].Trim()) }
        }
        $pending = 0
        foreach ($groupName in $groupNames) {
            $pendingText = (Invoke-RedisCli -Name "$NamePrefix-pending" -Arguments @('XPENDING', $key, $groupName)).Stdout -split "`r?`n" | Select-Object -First 1
            $pendingCount = 0
            if ([int]::TryParse([string]$pendingText, [ref]$pendingCount)) { $pending += $pendingCount }
        }
        $streams.Add([pscustomobject]@{ key = $key; length = [int64]$lengthText; groups = $groupNames.Count; pending = $pending })
    }
    return ,$streams.ToArray()
}

$adminConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
$redisConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
if ([string]::IsNullOrWhiteSpace($adminConnection)) { throw 'Set NERV_IIP_TEST_POSTGRES before Redis/CAP lane discovery.' }
if ([string]::IsNullOrWhiteSpace($redisConnection)) { throw 'Set NERV_IIP_TEST_REDIS before Redis/CAP lane discovery.' }
$parsed = ConvertTo-PgEnvironment $adminConnection
$redisContext = ConvertTo-RedisCliContext $redisConnection
$savedPg = @{}
foreach ($entry in $parsed.environment.GetEnumerator()) {
    $savedPg[$entry.Value] = [Environment]::GetEnvironmentVariable($entry.Value)
    [Environment]::SetEnvironmentVariable($entry.Value, [string]$parsed.values[$entry.Key])
}
$savedRedisCliAuth = [Environment]::GetEnvironmentVariable('REDISCLI_AUTH')
if (-not [string]::IsNullOrWhiteSpace([string]$redisContext.password)) { [Environment]::SetEnvironmentVariable('REDISCLI_AUTH', [string]$redisContext.password) }
$savedTestPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
$savedCapVersion = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_CAP_VERSION')
$savedDatabaseLifecycle = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_DATABASE_LIFECYCLE')
$summary = [ordered]@{ schemaVersion = 2; lane = 'redis-cap'; selectedMemberIds = @($MemberId); readiness = [ordered]@{ postgres = 'not-run'; redis = 'not-run' }; postgresVersion = ''; redisVersion = ''; expected = 0; discovered = 0; passed = 0; failed = 0; skipped = 0; cleanup = 'not-run'; members = @() }
$memberSummaries = [Collections.Generic.List[object]]::new()
$failure = $null
try {
    $postgresProbe = Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', 'SELECT current_setting(''server_version'')') -WorkingDirectory $repoRoot -Name 'redis-cap-lane-postgres-readiness'
    $summary.readiness.postgres = 'passed'
    $summary.postgresVersion = $postgresProbe.Stdout.Trim()
    $redisProbe = Invoke-RedisCli -Name 'redis-cap-lane-redis-readiness' -Arguments @('PING')
    if (-not [string]::Equals($redisProbe.Stdout.Trim(), 'PONG', [StringComparison]::Ordinal)) { throw 'Redis readiness probe did not return PONG.' }
    $redisInfo = Invoke-RedisCli -Name 'redis-cap-lane-redis-version' -Arguments @('INFO', 'server')
    $versionLine = @($redisInfo.Stdout -split "`r?`n" | Where-Object { $_.StartsWith('redis_version:', [StringComparison]::Ordinal) })
    if ($versionLine.Count -ne 1) { throw 'Redis readiness probe did not report exactly one redis_version.' }
    $summary.readiness.redis = 'passed'
    $summary.redisVersion = $versionLine[0].Substring('redis_version:'.Length).Trim()

    foreach ($member in $selectedMembers) {
        $databaseName = "$([string]$member.databasePrefix)_$DatabaseSuffix"
        if ($databaseName.Length -gt 63) { throw "Database name for Redis/CAP lane member '$($member.id)' exceeds 63 characters." }
        $capVersion = "$([string]$member.capVersionPrefix)-$($DatabaseSuffix.Replace('_', '-'))"
        if ($capVersion.Length -gt 20) { $capVersion = $capVersion.Substring(0, 20) }
        $memberResultsDirectory = Join-Path $ResultsDirectory ([string]$member.id)
        $memberSummary = [ordered]@{ memberId = [string]$member.id; service = [string]$member.service; database = $databaseName; capVersion = $capVersion; expected = @($member.expectedTestIdentities).Count; discovered = 0; passed = 0; failed = 0; skipped = 0; diagnostics = $null; cleanup = 'not-run'; outcome = 'not-run' }
        $memberFailure = $null
        $databaseCreated = $false
        $beforeKeys = @()
        try {
            $beforeKeys = @(Get-RedisKeys -Name "redis-cap-lane-$($member.id)-keys-before")
            Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE `"$databaseName`"") -WorkingDirectory $repoRoot -Name "redis-cap-lane-$($member.id)-create-database" | Out-Null
            $databaseCreated = $true
            $targetConnection = "Host=$($parsed.values.host);Port=$($parsed.values.port);Database=$databaseName;Username=$($parsed.values.username);Password=$($parsed.values.password)"
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $targetConnection)
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_CAP_VERSION', $capVersion)
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_DATABASE_LIFECYCLE', 'external')
            $discovery = Invoke-DotNetOutput -Name "redis-cap-lane-$($member.id)-discovery" -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--list-tests', '--filter', [string]$member.filter)
            $expectedIdentitySet = [Collections.Generic.HashSet[string]]::new([string[]]@($member.expectedTestIdentities), [StringComparer]::Ordinal)
            $discovered = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $expectedIdentitySet.Contains([string]$_) })
            $memberSummary.discovered = $discovered.Count
            if ($discovered.Count -ne @($member.expectedTestIdentities).Count) { throw "Redis/CAP lane member '$($member.id)' discovery expected $(@($member.expectedTestIdentities).Count) frozen tests but found $($discovered.Count)." }
            [IO.Directory]::CreateDirectory($memberResultsDirectory) | Out-Null
            Invoke-DotNetOutput -Name "redis-cap-lane-$($member.id)-execution" -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--no-restore', '--filter', [string]$member.filter, '--logger', "trx;LogFilePrefix=redis-cap-$($member.id)", '--results-directory', $memberResultsDirectory) | Out-Null
            $trxResult = Get-NervRedisCapTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
            $memberSummary.passed = $trxResult.passed
            $memberSummary.failed = $trxResult.failed
            $memberSummary.skipped = $trxResult.skipped
            if (-not $trxResult.valid) {
                if (-not $trxResult.identitiesMatch) { throw "Redis/CAP lane member '$($member.id)' TRX identities do not equal its frozen identities." }
                throw "Redis/CAP lane member '$($member.id)' requires $($memberSummary.expected) passed, 0 failed and 0 skipped; observed $($memberSummary.passed) passed, $($memberSummary.failed) failed and $($memberSummary.skipped) skipped."
            }
            $memberSummary.outcome = 'passed'
        }
        catch {
            $memberFailure = $_
            $memberSummary.outcome = 'failed'
            try {
                if (Test-Path -LiteralPath $memberResultsDirectory -PathType Container) {
                    $trxResult = Get-NervRedisCapTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
                    $memberSummary.passed = $trxResult.passed
                    $memberSummary.failed = $trxResult.failed
                    $memberSummary.skipped = $trxResult.skipped
                }
            }
            catch { Write-Diagnostic -Level 'WARN' -Message "Redis/CAP member '$($member.id)' failure TRX could not be summarized: $($_.Exception.Message)" }
        }
        finally {
            $afterKeys = @()
            try { $afterKeys = @(Get-RedisKeys -Name "redis-cap-lane-$($member.id)-keys-after") }
            catch { Write-Diagnostic -Level 'WARN' -Message "Redis/CAP member '$($member.id)' Redis keys could not be enumerated: $($_.Exception.Message)" }
            if ($null -ne $memberFailure) {
                $diagnostics = [ordered]@{ postgres = $null; redis = $null }
                if ($databaseCreated) {
                    try {
                        [Environment]::SetEnvironmentVariable('PGDATABASE', $databaseName)
                        $schemaLiterals = @($member.diagnosticSchemas | ForEach-Object { "'$($_)'" }) -join ', '
                        $query = "SELECT json_build_object('schemas', COALESCE((SELECT json_agg(nspname ORDER BY nspname) FROM pg_namespace WHERE nspname IN ($schemaLiterals)), '[]'::json), 'relations', COALESCE((SELECT json_agg(json_build_object('schema', n.nspname, 'name', c.relname, 'estimatedRows', GREATEST(c.reltuples::bigint, 0)) ORDER BY n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ($schemaLiterals) AND c.relkind IN ('r', 'p')), '[]'::json))::text;"
                        $diagnostics.postgres = (Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', $query) -WorkingDirectory $repoRoot -Name "redis-cap-lane-$($member.id)-postgres-diagnostics").Stdout.Trim() | ConvertFrom-Json -Depth 10
                    }
                    catch { $diagnostics.postgres = [ordered]@{ capture = 'failed'; message = Protect-ScriptAutomationText $_.Exception.Message } }
                }
                try { $diagnostics.redis = [ordered]@{ keys = $afterKeys.Count; streams = @(Get-RedisStreamDiagnostics -Keys $afterKeys -NamePrefix "redis-cap-lane-$($member.id)-redis-diagnostics") } }
                catch { $diagnostics.redis = [ordered]@{ capture = 'failed'; message = Protect-ScriptAutomationText $_.Exception.Message } }
                $memberSummary.diagnostics = $diagnostics
            }
            try {
                $beforeKeySet = [Collections.Generic.HashSet[string]]::new([string[]]$beforeKeys, [StringComparer]::Ordinal)
                $ownedKeys = @($afterKeys | Where-Object { -not $beforeKeySet.Contains([string]$_) })
                foreach ($ownedKey in $ownedKeys) { Invoke-RedisCli -Name "redis-cap-lane-$($member.id)-cleanup-key" -Arguments @('UNLINK', $ownedKey) | Out-Null }
                if ($databaseCreated) {
                    [Environment]::SetEnvironmentVariable('PGDATABASE', [string]$parsed.values.database)
                    Invoke-NativeCommandOutput -Command 'psql' -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE `"$databaseName`" WITH (FORCE)") -WorkingDirectory $repoRoot -Name "redis-cap-lane-$($member.id)-drop-database" | Out-Null
                }
                $memberSummary.cleanup = 'passed'
            }
            catch {
                $memberSummary.cleanup = 'failed'
                $memberSummary.outcome = 'failed'
                if ($null -eq $memberFailure) { $memberFailure = $_ }
            }
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_CAP_VERSION', $savedCapVersion)
            [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_DATABASE_LIFECYCLE', $savedDatabaseLifecycle)
            $memberSummaries.Add([pscustomobject]$memberSummary)
        }
        if ($null -ne $memberFailure -and $null -eq $failure) { $failure = $memberFailure }
    }
}
catch { $failure = $_ }
finally {
    foreach ($entry in $savedPg.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
    [Environment]::SetEnvironmentVariable('REDISCLI_AUTH', $savedRedisCliAuth)
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_CAP_VERSION', $savedCapVersion)
    [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_DATABASE_LIFECYCLE', $savedDatabaseLifecycle)
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
        if ($null -eq $failure) { $failure = [InvalidOperationException]::new("Redis/CAP lane selected $($MemberId.Count) members but summarized $($memberSummaries.Count).") }
    }
    elseif (@($memberSummaries | Where-Object { -not [string]::Equals([string]$_.cleanup, 'passed', [StringComparison]::Ordinal) }).Count -gt 0) { $summary.cleanup = 'failed' }
    else { $summary.cleanup = 'passed' }
    try { Assert-NervRedisCapTestLaneSummary -SelectedMemberIds @($MemberId) -MemberSummaries @($memberSummaries) }
    catch { if ($null -eq $failure) { $failure = $_ } }
    $summaryDirectory = Split-Path -Parent $SummaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) { [IO.Directory]::CreateDirectory($summaryDirectory) | Out-Null }
    [IO.File]::WriteAllText($SummaryPath, (($summary | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
}
if ($null -ne $failure) { throw $failure }
Write-Host "Redis/CAP lane members '$($MemberId -join ',')' passed: discovered=$($summary.discovered) passed=$($summary.passed) skipped=$($summary.skipped) cleanup=$($summary.cleanup)."
