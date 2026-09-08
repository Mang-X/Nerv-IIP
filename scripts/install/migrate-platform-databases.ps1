# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Applies repository EF Core migrations to explicitly allowlisted platform or business PostgreSQL databases
#     - Updates only each selected service schema and its __EFMigrationsHistory table
#   Writes:
#     - Selected service migration history and schema objects
#     - bin/ and obj/ build outputs for selected Infrastructure projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - Does not delete, recreate, seed, or roll back databases
#     - Leaves successfully applied migrations intact when a later service fails
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10 and repository dotnet tools
#     - PostgreSQL client psql for fail-closed target database existence checks
#     - Process-scoped connection variables declared by release-database-migrations.json or business-release-database-migrations.json

[CmdletBinding()]
param(
    [string] $ReleaseId = "release-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))",

    [string] $CorrelationId = [Guid]::NewGuid().ToString('D'),

    [string[]] $Service = @(),

    [ValidateSet('platform', 'business')]
    [Alias('Profile')]
    [string] $ManifestProfile = 'platform',

    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Get-ConnectionStringField {
    param(
        [Parameter(Mandatory)] [System.Data.Common.DbConnectionStringBuilder] $Builder,
        [Parameter(Mandatory)] [string[]] $Names,
        [switch] $Required
    )

    foreach ($key in $Builder.Keys) {
        foreach ($name in $Names) {
            if ([string]::Equals([string]$key, $name, [StringComparison]::OrdinalIgnoreCase)) {
                return [string]$Builder[[string]$key]
            }
        }
    }
    if ($Required) {
        throw "PostgreSQL connection string must include '$($Names[0])' for the database existence preflight."
    }
    return $null
}

function Get-LibPqSslMode {
    param([AllowNull()] [string] $SslMode)

    if ([string]::IsNullOrWhiteSpace($SslMode)) {
        return $null
    }
    $normalized = $SslMode.Trim().Replace(' ', '').Replace('-', '').ToLowerInvariant()
    foreach ($mapping in @(
        @('disable', 'disable'),
        @('allow', 'allow'),
        @('prefer', 'prefer'),
        @('require', 'require'),
        @('verifyca', 'verify-ca'),
        @('verifyfull', 'verify-full')
    )) {
        if ([string]::Equals($normalized, $mapping[0], [StringComparison]::Ordinal)) {
            return $mapping[1]
        }
    }
    throw "Unsupported PostgreSQL SSL Mode '$SslMode' for the psql database existence preflight."
}

function Assert-TargetDatabaseExists {
    param(
        [Parameter(Mandatory)] [string] $ConnectionString,
        [Parameter(Mandatory)] [string] $ExpectedDatabase,
        [Parameter(Mandatory)] [string] $ServiceName
    )

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    # PowerShell 的适配视图会把 `.ConnectionString =` 当作 indexer key；显式调用
    # CLR setter，确保 provider 字段被逐项解析。
    $builder.set_ConnectionString($ConnectionString)
    $hostName = Get-ConnectionStringField -Builder $builder -Names @('Host', 'Server') -Required
    $userName = Get-ConnectionStringField -Builder $builder -Names @('Username', 'User ID', 'UserID') -Required
    $port = Get-ConnectionStringField -Builder $builder -Names @('Port')
    if ([string]::IsNullOrWhiteSpace($port)) {
        $port = '5432'
    }
    $password = Get-ConnectionStringField -Builder $builder -Names @('Password', 'Pwd')
    $sslMode = Get-LibPqSslMode (Get-ConnectionStringField -Builder $builder -Names @('SSL Mode', 'SslMode'))
    $preflightEnvironment = @{
        PGHOST = $hostName
        PGPORT = $port
        PGUSER = $userName
        PGPASSWORD = $password
        PGDATABASE = $ExpectedDatabase
        PGCONNECT_TIMEOUT = '10'
        PGSSLMODE = $sslMode
    }
    $preflight = Invoke-WithScopedEnvironment -Variables $preflightEnvironment -ScriptBlock {
        Invoke-NativeCommandWithTimeout `
            -Command 'psql' `
            -Arguments @('-X', '-v', 'ON_ERROR_STOP=1', '-Atqc', 'SELECT current_database();') `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name "$ManifestProfile-migration-database-exists-$ServiceName-$ReleaseId"
    }
    $observedDatabase = (Get-Content -LiteralPath $preflight.StdoutPath -Raw).Trim()
    if (-not [string]::Equals($observedDatabase, $ExpectedDatabase, [StringComparison]::Ordinal)) {
        throw "PostgreSQL database existence preflight for service '$ServiceName' connected to '$observedDatabase' instead of allowlisted database '$ExpectedDatabase'."
    }
    return $preflight
}

function Get-DbContextMigrationState {
    param(
        [Parameter(Mandatory)] [object] $Entry,
        [Parameter(Mandatory)] [string] $ConnectionString,
        [Parameter(Mandatory)] [string] $Phase,
        [switch] $NoBuild
    )

    $arguments = @(
        'tool', 'run', 'dotnet-ef',
        'migrations', 'list',
        '--json', '--prefix-output',
        '--project', [string]$Entry.project,
        '--startup-project', [string]$Entry.startupProject,
        '--context', [string]$Entry.context
    )
    if ($NoBuild) {
        $arguments += '--no-build'
    }
    $arguments += @('--connection', $ConnectionString)
    $connectionArgumentIndex = $arguments.Count - 1
    $migrationList = Invoke-DotNet `
        -Arguments $arguments `
        -WorkingDirectory $root `
        -TimeoutSeconds 900 `
        -Name "$ManifestProfile-migration-history-$Phase-$($Entry.service)-$ReleaseId" `
        -SensitiveArgumentIndexes @($connectionArgumentIndex) `
        -SensitiveValues @($ConnectionString)

    $jsonLines = @(Get-Content -LiteralPath $migrationList.StdoutPath |
        Where-Object { $_.StartsWith('data:', [StringComparison]::Ordinal) } |
        ForEach-Object { $_.Substring(5) })
    if ($jsonLines.Count -eq 0) {
        throw "dotnet-ef returned no JSON migration state for service '$($Entry.service)'. Logs: $($migrationList.LogDirectory)"
    }
    $migrations = @(($jsonLines -join [Environment]::NewLine) | ConvertFrom-Json)
    $appliedMigrations = @($migrations | Where-Object { $_.applied -eq $true })
    $migrationId = if ($appliedMigrations.Count -eq 0) {
        'none'
    }
    else {
        [string]$appliedMigrations[-1].id
    }
    return [pscustomobject]@{
        LogDirectory = $migrationList.LogDirectory
        MigrationId = $migrationId
    }
}

if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw 'ReleaseId may contain only letters, digits, dot, underscore, and hyphen.'
}

$manifestFileName = if ([string]::Equals($ManifestProfile, 'business', [StringComparison]::OrdinalIgnoreCase)) {
    'business-release-database-migrations.json'
}
else {
    'release-database-migrations.json'
}
$manifestPath = Join-Path $PSScriptRoot $manifestFileName
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($manifest.Count -eq 0) {
    throw 'Release database migration manifest is empty.'
}

$serviceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.service) -or
        -not $serviceNames.Add([string]$entry.service)) {
        throw 'Release database migration manifest contains an empty or duplicate service.'
    }

    foreach ($requiredProperty in @('connectionEnvironmentVariable', 'expectedDatabase', 'project', 'startupProject', 'context')) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.$requiredProperty)) {
            throw "Release database migration manifest service '$($entry.service)' is missing '$requiredProperty'."
        }
    }
}

$requestedServices = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($requestedService in $Service) {
    if (-not [string]::IsNullOrWhiteSpace($requestedService)) {
        [void]$requestedServices.Add($requestedService.Trim())
    }
}

if ($requestedServices.Count -gt 0) {
    foreach ($requestedService in $requestedServices) {
        if (-not $serviceNames.Contains($requestedService)) {
            throw "Unknown migration service '$requestedService'. Allowed services: $(@($manifest.service) -join ', ')."
        }
    }
}

$selected = @($manifest | Where-Object {
    $requestedServices.Count -eq 0 -or $requestedServices.Contains([string]$_.service)
})

foreach ($entry in $selected) {
    $projectPath = Join-Path $root ([string]$entry.project)
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Release database migration manifest project is missing for service '$($entry.service)'."
    }
    $startupProjectPath = Join-Path $root ([string]$entry.startupProject)
    if (-not (Test-Path -LiteralPath $startupProjectPath -PathType Leaf)) {
        throw "Release database migration manifest startup project is missing for service '$($entry.service)'."
    }

    $contextName = [string]$entry.context
    $contextSeparator = $contextName.LastIndexOf([string]'.', [StringComparison]::Ordinal)
    if ($contextSeparator -le 0 -or $contextSeparator -eq ($contextName.Length - 1)) {
        throw "Release database migration manifest context is invalid for service '$($entry.service)'."
    }
    $contextNamespace = $contextName.Substring(0, $contextSeparator)
    $contextClassName = $contextName.Substring($contextSeparator + 1)
    $namespacePattern = '(?m)^\s*namespace\s+' + [regex]::Escape($contextNamespace) + '\s*[;{]'

    $projectDirectory = Split-Path -Parent $projectPath
    $contextSourcePath = Join-Path $projectDirectory "$contextClassName.cs"
    if (-not (Test-Path -LiteralPath $contextSourcePath -PathType Leaf)) {
        throw "Release database migration manifest context source '$contextClassName.cs' is missing inside project '$($entry.project)' for service '$($entry.service)'."
    }
    $contextSource = Get-Content -LiteralPath $contextSourcePath -Raw
    $contextDeclarationPattern = '(?m)^\s*(?:public|internal)?\s*(?:(?:abstract|sealed|static|partial)\s+)*class\s+' + [regex]::Escape($contextClassName) + '\b'
    if ($contextSource -notmatch $namespacePattern -or $contextSource -notmatch $contextDeclarationPattern) {
        throw "Release database migration manifest context '$contextName' does not match '$contextSourcePath' for service '$($entry.service)'."
    }

    $startupProjectDirectory = Split-Path -Parent $startupProjectPath
    $factoryPattern = 'IDesignTimeDbContextFactory\s*<\s*' + [regex]::Escape($contextClassName) + '\s*>'
    $factoryMatches = @(Get-ChildItem -LiteralPath $startupProjectDirectory -Filter '*Factory.cs' -File |
        Where-Object {
            $factorySource = Get-Content -LiteralPath $_.FullName -Raw
            $factorySource -match $namespacePattern -and $factorySource -match $factoryPattern
        })
    if ($factoryMatches.Count -ne 1) {
        throw "Release database migration manifest startup project '$($entry.startupProject)' does not contain the design-time factory paired with context '$contextName' for service '$($entry.service)'."
    }
}

$validated = [Collections.Generic.List[object]]::new()
foreach ($entry in $selected) {
    $connectionVariable = [string]$entry.connectionEnvironmentVariable
    $connectionString = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "$connectionVariable must be set in the current process before migrating '$($entry.service)'."
    }

    $connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connectionBuilder.set_ConnectionString($connectionString)
    $targetDatabase = Get-ConnectionStringField -Builder $connectionBuilder -Names @('Database') -Required
    $targetDatabase = $targetDatabase.Trim()
    if (-not $targetDatabase.Equals([string]$entry.expectedDatabase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target database '$targetDatabase' for service '$($entry.service)' does not match allowlisted database '$($entry.expectedDatabase)'."
    }

    $validationLogDirectory = New-ScriptAutomationLogDirectory -Name "$ManifestProfile-migration-$($entry.service)-validation"
    Write-Diagnostic "releaseId=$ReleaseId service=$($entry.service) dbProfile=PostgreSQL targetDatabase=$targetDatabase migrationFrom=not-queried migrationTo=not-queried seedStep=none correlationId=$CorrelationId logPath=$validationLogDirectory"
    $validated.Add([pscustomobject]@{
        Entry = $entry
        ConnectionString = $connectionString
        TargetDatabase = $targetDatabase
    })
}

if ($ValidateOnly) {
    Write-Diagnostic "$ManifestProfile migration configuration validation completed for $($validated.Count) service(s); no database command was executed."
    exit 0
}

foreach ($item in $validated) {
    $item | Add-Member -NotePropertyName DatabasePreflight -NotePropertyValue (
        Assert-TargetDatabaseExists `
            -ConnectionString ([string]$item.ConnectionString) `
            -ExpectedDatabase ([string]$item.TargetDatabase) `
            -ServiceName ([string]$item.Entry.service))
}

$restore = Invoke-DotNet `
    -Arguments @('tool', 'restore') `
    -WorkingDirectory $root `
    -TimeoutSeconds 300 `
    -Name "$ManifestProfile-migration-tool-restore-$ReleaseId"

foreach ($item in $validated) {
    $entry = $item.Entry
    $migrationStateBefore = Get-DbContextMigrationState `
        -Entry $entry `
        -ConnectionString ([string]$item.ConnectionString) `
        -Phase 'before'
    $migrationArguments = @(
        'tool', 'run', 'dotnet-ef',
        'database', 'update',
        '--project', [string]$entry.project,
        '--startup-project', [string]$entry.startupProject,
        '--context', [string]$entry.context,
        '--no-build',
        '--connection', [string]$item.ConnectionString
    )
    $connectionArgumentIndex = $migrationArguments.Count - 1
    $migration = Invoke-DotNet `
        -Arguments $migrationArguments `
        -WorkingDirectory $root `
        -TimeoutSeconds 900 `
        -Name "$ManifestProfile-migration-apply-$($entry.service)-$ReleaseId" `
        -SensitiveArgumentIndexes @($connectionArgumentIndex) `
        -SensitiveValues @([string]$item.ConnectionString)

    $migrationStateAfter = Get-DbContextMigrationState `
        -Entry $entry `
        -ConnectionString ([string]$item.ConnectionString) `
        -Phase 'after' `
        -NoBuild

    Write-Diagnostic "$ManifestProfile migration completed releaseId=$ReleaseId service=$($entry.service) targetDatabase=$($item.TargetDatabase) migrationFrom=$($migrationStateBefore.MigrationId) migrationTo=$($migrationStateAfter.MigrationId) durationMs=$([long]$migration.Duration.TotalMilliseconds) correlationId=$CorrelationId databasePreflightLog=$($item.DatabasePreflight.LogDirectory) migrationFromLog=$($migrationStateBefore.LogDirectory) migrationToLog=$($migrationStateAfter.LogDirectory) restoreLog=$($restore.LogDirectory) migrationLog=$($migration.LogDirectory) exitCode=0."
}
