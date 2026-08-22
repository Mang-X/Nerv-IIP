# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes only ValidateOnly business release migration paths in child PowerShell processes
#   Writes:
#     - artifacts/script-logs/** validation summaries
#   Cleanup:
#     - Restores process environment variables after each probe
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$scriptPath = Join-Path $repoRoot 'scripts/install/migrate-platform-databases.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/install/business-release-database-migrations.json'
$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)

if ($manifest.Count -ne 13) {
    throw "Expected 13 business migration entries, found $($manifest.Count)."
}

$seenServices = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenDatabases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $manifest) {
    if (-not $seenServices.Add([string]$entry.service) -or -not $seenDatabases.Add([string]$entry.expectedDatabase)) {
        throw "Duplicate business migration entry '$($entry.service)' / '$($entry.expectedDatabase)'."
    }
    if (-not $appHostText.Contains("AddDatabase(`"$($entry.service)-db`", `"$($entry.expectedDatabase)`")", [StringComparison]::Ordinal)) {
        throw "Business migration entry '$($entry.service)' does not match an AppHost database resource."
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
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Profile business @Arguments 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    finally {
        foreach ($name in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
        }
    }
}

$secretMarker = 'do-not-log-business-password'
$environment = @{}
foreach ($entry in $manifest) {
    $environment[[string]$entry.connectionEnvironmentVariable] = "Host=localhost;Database=$($entry.expectedDatabase);Username=nerv;Password=$secretMarker"
}

$valid = Invoke-MigratorProbe -Environment $environment -Arguments @('-ValidateOnly', '-ReleaseId', 'business-release-test')
if ($valid.ExitCode -ne 0 -or -not $valid.Output.Contains('validation completed for 13 service(s)', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected all business migration inputs to validate. Output: $($valid.Output)"
}
if ($valid.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Business ValidateOnly diagnostics leaked the connection password.'
}

$first = $manifest[0]
$wrong = Invoke-MigratorProbe -Environment @{
    ([string]$first.connectionEnvironmentVariable) = "Host=localhost;Database=neighbor_database;Password=$secretMarker"
} -Arguments @('-ValidateOnly', '-Service', [string]$first.service)
if ($wrong.ExitCode -eq 0 -or -not $wrong.Output.Contains('allowlisted database', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected wrong business database validation to fail closed. Output: $($wrong.Output)"
}
if ($wrong.Output.Contains($secretMarker, [StringComparison]::Ordinal)) {
    throw 'Wrong business database diagnostics leaked the connection password.'
}

Write-Host 'Business release migrator contracts passed.'
