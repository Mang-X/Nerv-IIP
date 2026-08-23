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

$appHostResources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$appHostDatabases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$appHostPairs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$businessDatabasePattern = 'AddDatabase\("(?<resource>business-[^"]+-db)",\s*"(?<database>[^"]+)"\)'
foreach ($match in [regex]::Matches($appHostText, $businessDatabasePattern)) {
    [void]$appHostResources.Add($match.Groups['resource'].Value)
    [void]$appHostDatabases.Add($match.Groups['database'].Value)
    [void]$appHostPairs.Add("$($match.Groups['resource'].Value)=$($match.Groups['database'].Value)")
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
            [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name], 'Process')
        }
        if (-not [string]::IsNullOrWhiteSpace($PathOverride)) {
            [Environment]::SetEnvironmentVariable('PATH', "$PathOverride$([IO.Path]::PathSeparator)$originalPath", 'Process')
        }
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $wrapperPath @Arguments 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    finally {
        foreach ($name in $expectedConnectionVariables.Values) {
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

$first = $manifest[0]
$single = Invoke-MigratorProbe -Environment @{
    ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=$($first.expectedDatabase);Password=$secretMarker"
} -Arguments @('-ValidateOnly', '-Service', [string]$first.service, '-ReleaseId', 'business-single-service-test')
if ($single.ExitCode -ne 0 -or -not $single.Output.Contains('validation completed for 1 service(s)', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Business wrapper must forward Service and ValidateOnly to the shared executor. Output: $($single.Output)"
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
} -Arguments @('-ValidateOnly', '-Service', [string]$first.service)
if ($wrong.ExitCode -eq 0 -or -not $wrong.Output.Contains('allowlisted database', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected wrong business database validation to fail closed with the child cause visible. Output: $($wrong.Output)"
}
if ($wrong.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Wrong business database diagnostics leaked the connection password.'
}

$fakeCommandDirectory = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-business-migrator-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fakeCommandDirectory) | Out-Null
    if ($IsWindows) {
        Set-Content -LiteralPath (Join-Path $fakeCommandDirectory 'dotnet.cmd') -Value '@exit /b 0' -Encoding Ascii
    }
    else {
        $fakeDotNet = Join-Path $fakeCommandDirectory 'dotnet'
        Set-Content -LiteralPath $fakeDotNet -Value "#!/bin/sh`nexit 0" -Encoding utf8NoBOM
        & chmod 700 $fakeDotNet
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to make the owned fake dotnet command executable.'
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
    if (($apply.Output + $applyLogText).Contains($secretMarker, [StringComparison]::Ordinal)) {
        throw 'Business apply diagnostics or command logs leaked a sensitive connection value.'
    }
}
finally {
    if (Test-Path -LiteralPath $fakeCommandDirectory) {
        Remove-Item -LiteralPath $fakeCommandDirectory -Recurse -Force
    }
}

Write-Host 'Business release migrator contracts passed.'
