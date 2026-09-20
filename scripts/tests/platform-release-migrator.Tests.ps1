# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes only ValidateOnly release migration paths in child PowerShell processes
#   Writes:
#     - artifacts/script-logs/** validation summaries
#   Cleanup:
#     - Restores process environment variables after each probe
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$scriptPath = Join-Path $repoRoot 'scripts/install/migrate-platform-databases.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/install/release-database-migrations.json'
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)

if ($manifest.Count -ne 4) {
    throw "Expected 4 platform migration entries, found $($manifest.Count)."
}

$expected = @{
    apphub = 'nerv_iip_apphub'
    iam = 'nerv_iip_iam'
    ops = 'nerv_iip_ops'
    notification = 'nerv_iip_notification'
}
foreach ($entry in $manifest) {
    if (-not $expected.ContainsKey([string]$entry.service) -or
        -not [string]::Equals([string]$entry.expectedDatabase, [string]$expected[[string]$entry.service], [StringComparison]::Ordinal)) {
        throw "Unexpected platform migration manifest entry '$($entry.service)'."
    }
}

function Invoke-MigratorProbe {
    param(
        [Parameter(Mandatory)] [hashtable] $Environment,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $saved = @{}
    try {
        foreach ($name in $Environment.Keys) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name], 'Process')
        }
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    finally {
        foreach ($name in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
        }
    }
}

$secretMarker = 'do-not-log-platform-password'
$environment = @{}
foreach ($entry in $manifest) {
    $environment[[string]$entry.connectionEnvironmentVariable] = "Host=localhost;Database=$($entry.expectedDatabase);Username=nerv;Password=$secretMarker"
}

$valid = Invoke-MigratorProbe -Environment $environment -Arguments @('-ValidateOnly', '-ReleaseId', 'release-test')
if ($valid.ExitCode -ne 0 -or -not $valid.Output.Contains('validation completed for 4 service(s)', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected all platform migration inputs to validate. Output: $($valid.Output)"
}
if ($valid.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'ValidateOnly diagnostics leaked the connection password.'
}

$wrongDatabaseEnvironment = @{ NERV_IIP_APPHUB_DB = "Host=localhost;Database=neighbor_database;Password=$secretMarker" }
$wrongDatabase = Invoke-MigratorProbe -Environment $wrongDatabaseEnvironment -Arguments @('-ValidateOnly', '-Service', 'apphub')
if ($wrongDatabase.ExitCode -eq 0 -or -not $wrongDatabase.Output.Contains('allowlisted database', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected wrong database validation to fail closed. Output: $($wrongDatabase.Output)"
}
if ($wrongDatabase.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Wrong-database diagnostics leaked the connection password.'
}

$missingVariable = Invoke-MigratorProbe `
    -Environment @{ NERV_IIP_APPHUB_DB = $null } `
    -Arguments @('-ValidateOnly', '-Service', 'apphub')
if ($missingVariable.ExitCode -eq 0 -or
    -not $missingVariable.Output.Contains('NERV_IIP_APPHUB_DB must be set in the current process', [StringComparison]::Ordinal)) {
    throw "Expected missing connection variable validation to fail closed. Output: $($missingVariable.Output)"
}

$unknown = Invoke-MigratorProbe -Environment @{} -Arguments @('-ValidateOnly', '-Service', 'unknown-service')
if ($unknown.ExitCode -eq 0 -or -not $unknown.Output.Contains('Unknown migration service', [StringComparison]::Ordinal)) {
    throw "Expected unknown service validation to fail closed. Output: $($unknown.Output)"
}

Write-Host 'Platform release migrator contracts passed.'
# GitHub Actions dot-sources the generated pwsh step script, so the final expected-failure
# child probe would otherwise leak LASTEXITCODE=1 after every assertion has passed.
$global:LASTEXITCODE = 0
