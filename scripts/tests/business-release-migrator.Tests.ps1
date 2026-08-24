# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes the business release migration wrapper against ValidateOnly inputs and a fake dotnet apply command
#   Writes:
#     - OS temporary fake command files
#     - artifacts/script-logs/** validation and fake apply summaries
#   Cleanup:
#     - Restores process environment variables and PATH, then removes the owned temporary directory
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$wrapperPath = Join-Path $repoRoot 'scripts/install/migrate-business-databases.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/install/business-release-database-migrations.json'
$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)

$expectedConnectionVariables = @{
    'business-master-data' = 'NERV_IIP_BUSINESS_MASTER_DATA_DB'
    'business-product-engineering' = 'NERV_IIP_BUSINESS_PRODUCT_ENGINEERING_DB'
    'business-inventory' = 'NERV_IIP_BUSINESS_INVENTORY_DB'
    'business-quality' = 'NERV_IIP_BUSINESS_QUALITY_DB'
    'business-mes' = 'NERV_IIP_BUSINESS_MES_DB'
    'business-demand-planning' = 'NERV_IIP_BUSINESS_DEMAND_PLANNING_DB'
    'business-barcode-label' = 'NERV_IIP_BUSINESS_BARCODE_LABEL_DB'
    'business-approval' = 'NERV_IIP_BUSINESS_APPROVAL_DB'
    'business-wms' = 'NERV_IIP_BUSINESS_WMS_DB'
    'business-industrial-telemetry' = 'NERV_IIP_BUSINESS_INDUSTRIAL_TELEMETRY_DB'
    'business-maintenance' = 'NERV_IIP_BUSINESS_MAINTENANCE_DB'
    'business-erp' = 'NERV_IIP_BUSINESS_ERP_DB'
    'business-scheduling' = 'NERV_IIP_BUSINESS_SCHEDULING_DB'
}

if ($manifest.Count -ne 13) {
    throw "Expected 13 business migration entries, found $($manifest.Count)."
}

$manifestResources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$manifestDatabases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$manifestPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $manifest) {
    $service = [string]$entry.service
    $resource = "$service-db"
    if (-not $manifestResources.Add($resource) -or -not $manifestDatabases.Add([string]$entry.expectedDatabase)) {
        throw "Duplicate business migration entry '$service' / '$($entry.expectedDatabase)'."
    }
    [void]$manifestPairs.Add("$resource=$($entry.expectedDatabase)")
    if (-not $expectedConnectionVariables.ContainsKey($service) -or
        -not [string]::Equals([string]$entry.connectionEnvironmentVariable, [string]$expectedConnectionVariables[$service], [StringComparison]::Ordinal)) {
        throw "Business migration entry '$service' has an unexpected connection environment variable."
    }
    foreach ($requiredProperty in @('project', 'startupProject', 'context')) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.$requiredProperty)) {
            throw "Business migration entry '$service' is missing '$requiredProperty'."
        }
    }
}

$nonBusinessPairs = [Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        'apphub-db=nerv_iip_apphub',
        'iam-db=nerv_iip_iam',
        'ops-db=nerv_iip_ops',
        'notification-db=nerv_iip_notification',
        'file-storage-db=nerv_iip_filestorage'
    ),
    [StringComparer]::Ordinal)
$appHostResources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$appHostDatabases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$appHostPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$allDatabasePattern = 'AddDatabase\("(?<resource>[^"]+)",\s*"(?<database>[^"]+)"\)'
foreach ($match in [regex]::Matches($appHostText, $allDatabasePattern)) {
    $pair = "$($match.Groups['resource'].Value)=$($match.Groups['database'].Value)"
    if ($nonBusinessPairs.Contains($pair)) {
        continue
    }
    [void]$appHostResources.Add($match.Groups['resource'].Value)
    [void]$appHostDatabases.Add($match.Groups['database'].Value)
    [void]$appHostPairs.Add($pair)
}
if ($appHostResources.Count -ne 13 -or
    -not $manifestResources.SetEquals($appHostResources) -or
    -not $manifestDatabases.SetEquals($appHostDatabases) -or
    -not $manifestPairs.SetEquals($appHostPairs)) {
    throw "Business AppHost resources and migration manifest must be the same exact 13-entry resource/database sets. AppHost resources: $(@($appHostResources) -join ', '); manifest resources: $(@($manifestResources) -join ', ')."
}

function Invoke-MigratorProbe {
    param(
        [Parameter(Mandatory)] [hashtable] $Environment,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [string] $PathOverride
    )

    $saved = @{}
    $originalPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
    try {
        foreach ($name in $expectedConnectionVariables.Values) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        foreach ($name in $Environment.Keys) {
            if (-not $saved.ContainsKey($name)) {
                $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            }
            [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name], 'Process')
        }
        if (-not [string]::IsNullOrWhiteSpace($PathOverride)) {
            [Environment]::SetEnvironmentVariable('PATH', "$PathOverride$([IO.Path]::PathSeparator)$originalPath", 'Process')
        }
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath @Arguments 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    finally {
        foreach ($name in $saved.Keys) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
        }
        [Environment]::SetEnvironmentVariable('PATH', $originalPath, 'Process')
    }
}

$secretMarker = 'do-not-log-business-password'
$environment = @{}
foreach ($entry in $manifest) {
    $environment[[string]$entry.connectionEnvironmentVariable] = "Host=localhost;Database=$($entry.expectedDatabase);Username=nerv;Password=$secretMarker"
}

$valid = Invoke-MigratorProbe -Environment $environment -Arguments @('-ValidateOnly', '-ReleaseId', 'business-release-test', '-CorrelationId', 'business-correlation-test')
foreach ($expected in @('validation completed for 13 service(s)', 'releaseId=business-release-test', 'service=business-master-data', 'dbProfile=PostgreSQL', 'migrationFrom=database-current', 'migrationTo=repository-latest', 'seedStep=none', 'correlationId=business-correlation-test', 'logPath=')) {
    if ($valid.ExitCode -ne 0 -or -not $valid.Output.Contains($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected wrapper-visible business migration diagnostic '$expected'. Output: $($valid.Output)"
    }
}
if ($valid.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Business ValidateOnly diagnostics leaked the connection password.'
}
$wrapperLogs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/script-logs') -File -Recurse |
    Where-Object { $_.FullName.Contains('business-release-migration-business-release-test', [StringComparison]::Ordinal) })
if ($wrapperLogs.Count -lt 2 -or -not ($wrapperLogs | Where-Object Length -GT 0)) {
    throw 'Business wrapper must persist independent child stdout/stderr logs on the successful ValidateOnly path.'
}

$first = $manifest[0]
$single = Invoke-MigratorProbe -Environment @{
    ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=$($first.expectedDatabase);Password=$secretMarker"
} -Arguments @('-ValidateOnly', '-Service', [string]$first.service, '-ReleaseId', 'business-single-service-test')
if ($single.ExitCode -ne 0 -or -not $single.Output.Contains('validation completed for 1 service(s)', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Business wrapper must forward Service and ValidateOnly to the shared executor. Output: $($single.Output)"
}

function New-BusinessMigratorSourceFixture {
    param(
        [string] $StartupService = 'Quality',
        [string] $Context = 'Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext'
    )

    $fixtureContainer = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-migrator-source-$([Guid]::NewGuid().ToString('N'))"
    $fixtureRoot = Join-Path $fixtureContainer 'bin/repo'
    foreach ($relativeDirectory in @(
        'scripts/install',
        'scripts/lib',
        'backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure',
        'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure'
    )) {
        [IO.Directory]::CreateDirectory((Join-Path $fixtureRoot $relativeDirectory)) | Out-Null
    }
    foreach ($relativeFile in @(
        'scripts/install/migrate-platform-databases.ps1',
        'scripts/lib/ScriptAutomation.ps1',
        'scripts/lib/OrdinalString.ps1',
        'backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj',
        'backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs',
        'backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/DesignTimeApplicationDbContextFactory.cs',
        'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj',
        'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs',
        'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/DesignTimeApplicationDbContextFactory.cs'
    )) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $relativeFile) -Destination (Join-Path $fixtureRoot $relativeFile)
    }
    $qualityEntry = $manifest |
        Where-Object { [string]::Equals([string]$_.service, 'business-quality', [StringComparison]::Ordinal) } |
        Select-Object -First 1
    $fixtureEntry = [ordered]@{
        service = [string]$qualityEntry.service
        connectionEnvironmentVariable = [string]$qualityEntry.connectionEnvironmentVariable
        expectedDatabase = [string]$qualityEntry.expectedDatabase
        project = [string]$qualityEntry.project
        startupProject = if ([string]::Equals($StartupService, 'Mes', [StringComparison]::Ordinal)) {
            'backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj'
        }
        else {
            [string]$qualityEntry.startupProject
        }
        context = $Context
    }
    ConvertTo-Json @($fixtureEntry) -Depth 4 |
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'scripts/install/business-release-database-migrations.json') -Encoding utf8NoBOM
    return [pscustomobject]@{ Container = $fixtureContainer; Root = $fixtureRoot }
}

$pathFixture = New-BusinessMigratorSourceFixture
try {
    $savedQualityConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', 'Process')
    [Environment]::SetEnvironmentVariable(
        'NERV_IIP_BUSINESS_QUALITY_DB',
        'Host=localhost;Database=nerv_iip_quality;Username=nerv;Password=fixture-only',
        'Process')
    $pathOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pathFixture.Root 'scripts/install/migrate-platform-databases.ps1') `
        -ManifestProfile business -ValidateOnly -Service business-quality 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (($pathOutput | Out-String).Contains('validation completed for 1 service(s)', [StringComparison]::OrdinalIgnoreCase))) {
        throw "Migrator source validation must not depend on parent installation path segments such as bin. Output: $($pathOutput | Out-String)"
    }
}
finally {
    [Environment]::SetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', $savedQualityConnection, 'Process')
    Remove-Item -LiteralPath $pathFixture.Container -Recurse -Force
}

$invalidContextFixture = New-BusinessMigratorSourceFixture -Context 'Nerv.IIP.Business.Quality.Infrastructure.MissingDbContext'
try {
    $savedQualityConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', 'Process')
    [Environment]::SetEnvironmentVariable(
        'NERV_IIP_BUSINESS_QUALITY_DB',
        'Host=localhost;Database=nerv_iip_quality;Username=nerv;Password=fixture-only',
        'Process')
    $invalidContextOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $invalidContextFixture.Root 'scripts/install/migrate-platform-databases.ps1') `
        -ManifestProfile business -ValidateOnly -Service business-quality 2>&1
    if ($LASTEXITCODE -eq 0 -or -not (($invalidContextOutput | Out-String).Contains('context source', [StringComparison]::OrdinalIgnoreCase))) {
        throw "A context missing from the selected project must fail closed before database actions. Output: $($invalidContextOutput | Out-String)"
    }
}
finally {
    [Environment]::SetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', $savedQualityConnection, 'Process')
    Remove-Item -LiteralPath $invalidContextFixture.Container -Recurse -Force
}

$mismatchedStartupFixture = New-BusinessMigratorSourceFixture -StartupService Mes
try {
    $savedQualityConnection = [Environment]::GetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', 'Process')
    [Environment]::SetEnvironmentVariable(
        'NERV_IIP_BUSINESS_QUALITY_DB',
        'Host=localhost;Database=nerv_iip_quality;Username=nerv;Password=fixture-only',
        'Process')
    $mismatchOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $mismatchedStartupFixture.Root 'scripts/install/migrate-platform-databases.ps1') `
        -ManifestProfile business -ValidateOnly -Service business-quality 2>&1
    if ($LASTEXITCODE -eq 0 -or -not (($mismatchOutput | Out-String).Contains('does not contain the design-time factory paired with context', [StringComparison]::OrdinalIgnoreCase))) {
        throw "A startup project from another business service must not satisfy the selected context pairing. Output: $($mismatchOutput | Out-String)"
    }
}
finally {
    [Environment]::SetEnvironmentVariable('NERV_IIP_BUSINESS_QUALITY_DB', $savedQualityConnection, 'Process')
    Remove-Item -LiteralPath $mismatchedStartupFixture.Container -Recurse -Force
}

$missing = Invoke-MigratorProbe -Environment @{} -Arguments @('-ValidateOnly', '-Service', [string]$first.service)
if ($missing.ExitCode -eq 0 -or -not $missing.Output.Contains([string]$first.connectionEnvironmentVariable, [StringComparison]::Ordinal)) {
    throw "Expected a missing business connection variable to fail closed and name the variable. Output: $($missing.Output)"
}

$unknown = Invoke-MigratorProbe -Environment @{} -Arguments @('-ValidateOnly', '-Service', 'business-not-registered')
if ($unknown.ExitCode -eq 0 -or -not $unknown.Output.Contains('Unknown migration service', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected an unknown business service to fail closed. Output: $($unknown.Output)"
}

$wrong = Invoke-MigratorProbe -Environment @{
    ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=neighbor_database;Password=$secretMarker"
} -Arguments @('-ValidateOnly', '-Service', [string]$first.service, '-ReleaseId', 'business-release-wrong-database')
if ($wrong.ExitCode -eq 0 -or -not $wrong.Output.Contains('allowlisted database', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected wrong business database validation to fail closed with the child cause visible. Output: $($wrong.Output)"
}
if ($wrong.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Wrong business database diagnostics leaked the connection password.'
}
$wrongDatabaseLogs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/script-logs') -File -Recurse |
    Where-Object { $_.FullName.Contains('business-release-migration-business-release-wrong-database', [StringComparison]::Ordinal) })
if ($wrongDatabaseLogs.Count -lt 2 -or -not ($wrongDatabaseLogs | Where-Object Length -GT 0)) {
    throw 'Business wrapper must persist child logs when the executor exits nonzero.'
}

$fakeCommandDirectory = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-business-migrator-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fakeCommandDirectory) | Out-Null
    if ($IsWindows) {
        Set-Content -LiteralPath (Join-Path $fakeCommandDirectory 'dotnet.cmd') -Value @(
            '@echo %* | powershell -NoProfile -Command "$input -replace ''--connection\s+\S+'',''--connection ^<redacted^>''"',
            '@exit /b 0'
        ) -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $fakeCommandDirectory 'psql.cmd') -Value @(
            '@if not "%NERV_IIP_FAKE_PSQL_MISSING%"=="1" goto database_exists',
            '@echo database does not exist 1>&2',
            '@exit /b 1',
            ':database_exists',
            '@echo %PGDATABASE%',
            '@exit /b 0'
        ) -Encoding Ascii
    }
    else {
        $fakeDotNet = Join-Path $fakeCommandDirectory 'dotnet'
        Set-Content -LiteralPath $fakeDotNet -Value @'
#!/bin/sh
redact_next=0
for argument in "$@"; do
  if [ "$redact_next" -eq 1 ]; then
    printf '<redacted> '
    redact_next=0
  else
    printf '%s ' "$argument"
    if [ "$argument" = '--connection' ]; then
      redact_next=1
    fi
  fi
done
printf '\n'
'@ -Encoding utf8NoBOM
        $fakePsql = Join-Path $fakeCommandDirectory 'psql'
        Set-Content -LiteralPath $fakePsql -Value @'
#!/bin/sh
if [ "$NERV_IIP_FAKE_PSQL_MISSING" = '1' ]; then
  printf 'database does not exist\n' >&2
  exit 1
fi
printf '%s\n' "$PGDATABASE"
'@ -Encoding utf8NoBOM
        & chmod 700 $fakeDotNet $fakePsql
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to make the owned fake database commands executable.'
        }
    }

    $applyReleaseId = 'business-release-apply-redaction'
    $apply = Invoke-MigratorProbe -Environment @{
        # Username is intentionally used for the marker: the generic Password= redactor
        # cannot satisfy this assertion, so only whole-argument sensitivity kills M9.
        ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=$($first.expectedDatabase);Username=$secretMarker;Password=redacted-by-generic-filter"
    } -Arguments @('-Service', [string]$first.service, '-ReleaseId', $applyReleaseId) -PathOverride $fakeCommandDirectory
    if ($apply.ExitCode -ne 0 -or -not $apply.Output.Contains('migration completed', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected the fake apply path to complete through the business wrapper. Output: $($apply.Output)"
    }
    $applyLogText = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/script-logs') -File -Recurse |
        Where-Object { $_.FullName.Contains($applyReleaseId, [StringComparison]::Ordinal) } |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
    foreach ($requiredArgument in @('--startup-project', '--context', '<redacted>', [string]$first.expectedDatabase)) {
        if (-not $applyLogText.Contains($requiredArgument, [StringComparison]::Ordinal)) {
            throw "Business fake apply logs must prove the '$requiredArgument' migration preflight or dotnet argument."
        }
    }
    if (($apply.Output + $applyLogText).Contains($secretMarker, [StringComparison]::Ordinal)) {
        throw 'Business apply diagnostics or command logs leaked a sensitive connection value.'
    }

    $missingDatabase = Invoke-MigratorProbe -Environment @{
        ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=$($first.expectedDatabase);Username=nerv;Password=fixture-only"
        NERV_IIP_FAKE_PSQL_MISSING = '1'
    } -Arguments @('-Service', [string]$first.service, '-ReleaseId', 'business-release-missing-database') -PathOverride $fakeCommandDirectory
    if ($missingDatabase.ExitCode -eq 0 -or
        -not $missingDatabase.Output.Contains("Command 'psql' exited with 1", [StringComparison]::OrdinalIgnoreCase) -or
        $missingDatabase.Output.Contains('migration completed', [StringComparison]::OrdinalIgnoreCase)) {
        throw "A missing allowlisted database must fail closed before dotnet migration actions. Output: $($missingDatabase.Output)"
    }
}
finally {
    if (Test-Path -LiteralPath $fakeCommandDirectory) {
        Remove-Item -LiteralPath $fakeCommandDirectory -Recurse -Force
    }
}

Write-Host 'Business release migrator contracts passed.'
# GitHub Actions dot-sources the generated pwsh step script. Keep an expected-failure
# child probe from leaking LASTEXITCODE after all assertions have passed.
$global:LASTEXITCODE = 0
