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
    $contextPattern = '(?ms)namespace\s+' + [regex]::Escape($contextNamespace) + '\s*[;{].*?\b(?:partial\s+)?class\s+' + [regex]::Escape($contextClassName) + '\b'
    $projectDirectory = Split-Path -Parent $projectPath
    $contextSources = @(Get-ChildItem -LiteralPath $projectDirectory -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|Migrations)[\\/]' })
    $contextMatches = @($contextSources | Where-Object {
        (Get-Content -LiteralPath $_.FullName -Raw) -match $contextPattern
    })
    if ($contextMatches.Count -ne 1) {
        throw "Release database migration manifest context '$contextName' must resolve exactly once inside project '$($entry.project)' for service '$($entry.service)'."
    }

    $startupProjectDirectory = Split-Path -Parent $startupProjectPath
    $startupSources = @(Get-ChildItem -LiteralPath $startupProjectDirectory -Filter '*.cs' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|Migrations)[\\/]' })
    $factoryPattern = 'IDesignTimeDbContextFactory\s*<\s*' + [regex]::Escape($contextClassName) + '\s*>'
    $factoryMatches = @($startupSources | Where-Object {
        (Get-Content -LiteralPath $_.FullName -Raw) -match $factoryPattern
    })
    if ($factoryMatches.Count -ne 1) {
        throw "Release database migration manifest startup project '$($entry.startupProject)' must contain exactly one design-time factory for context '$contextName' and service '$($entry.service)'."
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
$validated = [Collections.Generic.List[object]]::new()
foreach ($entry in $selected) {
    $connectionVariable = [string]$entry.connectionEnvironmentVariable
    $connectionString = [Environment]::GetEnvironmentVariable($connectionVariable, 'Process')
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "$connectionVariable must be set in the current process before migrating '$($entry.service)'."
    }

    $databaseMatch = [regex]::Match($connectionString, '(?i)(?:^|;)\s*Database\s*=\s*([^;]+)')
    if (-not $databaseMatch.Success -or [string]::IsNullOrWhiteSpace($databaseMatch.Groups[1].Value)) {
        throw "$connectionVariable must include a non-empty Database field."
    }

    $targetDatabase = $databaseMatch.Groups[1].Value.Trim()
    if (-not $targetDatabase.Equals([string]$entry.expectedDatabase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target database '$targetDatabase' for service '$($entry.service)' does not match allowlisted database '$($entry.expectedDatabase)'."
    }

    $validationLogDirectory = New-ScriptAutomationLogDirectory -Name "$ManifestProfile-migration-$($entry.service)-validation"
    Write-Diagnostic "releaseId=$ReleaseId service=$($entry.service) dbProfile=PostgreSQL targetDatabase=$targetDatabase migrationFrom=database-current migrationTo=repository-latest seedStep=none correlationId=$CorrelationId logPath=$validationLogDirectory"
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

$restore = Invoke-DotNet `
    -Arguments @('tool', 'restore') `
    -WorkingDirectory $root `
    -TimeoutSeconds 300 `
    -Name "$ManifestProfile-migration-tool-restore-$ReleaseId"

foreach ($item in $validated) {
    $entry = $item.Entry
    $migrationArguments = @(
        'tool', 'run', 'dotnet-ef',
        'database', 'update',
        '--project', [string]$entry.project,
        '--startup-project', [string]$entry.startupProject,
        '--context', [string]$entry.context,
        '--connection', [string]$item.ConnectionString
    )
    $connectionArgumentIndex = $migrationArguments.Count - 1
    $migration = Invoke-DotNet `
        -Arguments $migrationArguments `
        -WorkingDirectory $root `
        -TimeoutSeconds 900 `
        -Name "$ManifestProfile-migration-apply-$($entry.service)-$ReleaseId" `
        -SensitiveArgumentIndexes @($connectionArgumentIndex)

    Write-Diagnostic "$ManifestProfile migration completed releaseId=$ReleaseId service=$($entry.service) targetDatabase=$($item.TargetDatabase) correlationId=$CorrelationId restoreLog=$($restore.LogDirectory) migrationLog=$($migration.LogDirectory) exitCode=0."
}
