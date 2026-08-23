# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Builds and publishes two temporary Aspire Docker Compose artifacts
#   Writes:
#     - A uniquely owned temporary directory under the system temporary directory
#     - artifacts/script-logs/**
#   Cleanup:
#     - Deletes only the temporary directory created by this invocation
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Aspire CLI 13.4.6

[CmdletBinding()]
param(
    [ValidateRange(60, 1800)]
    [int] $PublishTimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Get-ComposeProjectEnvironments {
    param([Parameter(Mandatory)][string] $ComposePath)

    $services = @{}
    $currentService = $null
    $inEnvironment = $false
    foreach ($line in [IO.File]::ReadLines($ComposePath)) {
        if ($line -match '^  (?<name>[a-z0-9-]+):$') {
            $currentService = $Matches['name']
            $services[$currentService] = @{}
            $inEnvironment = $false
            continue
        }
        if ($null -ne $currentService -and $line -match '^    environment:$') {
            $inEnvironment = $true
            continue
        }
        if ($inEnvironment -and $line -match '^      (?<key>[A-Za-z0-9_]+): "(?<value>[^"]*)"$') {
            $services[$currentService][$Matches['key']] = $Matches['value']
            continue
        }
        if ($line -notmatch '^      ' -and $line -notmatch '^    environment:$') {
            $inEnvironment = $false
        }
    }
    return $services
}

function Test-OrdinalSetEquals {
    param([string[]] $Actual, [string[]] $Expected)
    $actualSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Actual) { [void] $actualSet.Add($value) }
    foreach ($value in $Expected) { [void] $expectedSet.Add($value) }
    return $actualSet.Count -eq $Expected.Count -and $actualSet.SetEquals($expectedSet)
}

function Assert-EnvironmentArtifact {
    param(
        [Parameter(Mandatory)][hashtable] $Services,
        [Parameter(Mandatory)][string] $EnvironmentName
    )

    $expectedEnabled = if ([string]::Equals($EnvironmentName, 'Development', [StringComparison]::Ordinal)) { 'true' } else { 'false' }
    $expectedProjects = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling', 'gateway', 'business-gateway', 'connector-host')
    $projectServices = @($Services.GetEnumerator() | Where-Object { $_.Value.ContainsKey('ASPNETCORE_ENVIRONMENT') })
    if (-not (Test-OrdinalSetEquals -Actual @($projectServices | ForEach-Object Key) -Expected $expectedProjects)) { throw 'Published project resource identity set differs from the #2031 contract.' }
    foreach ($service in $projectServices) {
        if ($service.Value['ASPNETCORE_ENVIRONMENT'] -ne $EnvironmentName -or $service.Value['DOTNET_ENVIRONMENT'] -ne $EnvironmentName) {
            throw "Service '$($service.Key)' did not inherit $EnvironmentName for both .NET environment variables."
        }
    }

    $expectedPersistent = @('apphub', 'iam', 'ops', 'file-storage', 'notification', 'business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling')
    $persistentServices = @($projectServices | Where-Object { $_.Value.ContainsKey('Persistence__AutoMigrate') })
    if (-not (Test-OrdinalSetEquals -Actual @($persistentServices | ForEach-Object Key) -Expected $expectedPersistent)) { throw 'Published AutoMigrate resource identity set differs from the #2031 contract.' }
    foreach ($service in $persistentServices) {
        if ($service.Value['Persistence__AutoMigrate'] -ne $expectedEnabled) {
            throw "Service '$($service.Key)' has Persistence__AutoMigrate='$($service.Value['Persistence__AutoMigrate'])', expected '$expectedEnabled'."
        }
    }

    $expectedProfiles = @{
        'Iam__Seed__Enabled' = @{ Services = @('iam'); Value = $expectedEnabled }
        'Erp__Seed__SalesOrderDemandDemo__Enabled' = @{ Services = @('business-erp'); Value = $expectedEnabled }
        'Walkthrough__Seed__Enabled' = @{ Services = @('business-master-data', 'business-product-engineering', 'business-erp'); Value = $expectedEnabled }
        'LeaderDemo__Seed__Enabled' = @{ Services = @('business-master-data', 'business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-industrial-telemetry', 'business-maintenance'); Value = 'false' }
        'LeaderDemo__World__Enabled' = @{ Services = @('iam', 'business-master-data', 'business-product-engineering', 'business-industrial-telemetry'); Value = 'false' }
        'LeaderDemo__History__Enabled' = @{ Services = @('business-product-engineering', 'business-inventory', 'business-quality', 'business-mes', 'business-demand-planning', 'business-barcode-label', 'business-approval', 'business-wms', 'business-industrial-telemetry', 'business-maintenance', 'business-erp', 'business-scheduling'); Value = 'false' }
    }
    foreach ($profile in $expectedProfiles.GetEnumerator()) {
        $matching = @($projectServices | Where-Object { $_.Value.ContainsKey($profile.Key) })
        if (-not (Test-OrdinalSetEquals -Actual @($matching | ForEach-Object Key) -Expected $profile.Value.Services)) { throw "Published $EnvironmentName artifact has an invalid service set for $($profile.Key)." }
        foreach ($service in $matching) {
            if ($service.Value[$profile.Key] -ne $profile.Value.Value) { throw "Service '$($service.Key)' has $($profile.Key)='$($service.Value[$profile.Key])', expected '$($profile.Value.Value)'." }
        }
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-apphost-environment-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    foreach ($environmentName in @('Development', 'Production')) {
        $outputPath = Join-Path $temporaryRoot $environmentName.ToLowerInvariant()
        $publishEnvironment = @{}
        if ([string]::Equals($environmentName, 'Production', [StringComparison]::Ordinal)) {
            $publishEnvironment = @{
                Messaging__Provider = 'Redis'
                Security__Cors__AllowedOrigins = 'https://console.example.test,https://business.example.test'
                ConnectorHost__ConnectorHostId = 'verify-connector-host'
                ConnectorHost__OrganizationId = 'verify-organization'
                ConnectorHost__EnvironmentId = 'verify-environment'
            }
        }
        Invoke-WithScopedEnvironment -Variables $publishEnvironment -ScriptBlock {
            Invoke-Aspire -Arguments @('publish', '--output-path', $outputPath, '--environment', $environmentName, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name "verify-apphost-$($environmentName.ToLowerInvariant())-publish" | Out-Null
        }
        $composePath = Join-Path $outputPath 'docker-compose.yaml'
        if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Aspire publish did not produce $composePath." }
        Assert-EnvironmentArtifact -Services (Get-ComposeProjectEnvironments -ComposePath $composePath) -EnvironmentName $environmentName
    }
    Write-Diagnostic 'Aspire Development and Production Compose artifacts preserve the environment, migration, and seed profiles.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
