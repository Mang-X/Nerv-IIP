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

function Assert-EnvironmentArtifact {
    param(
        [Parameter(Mandatory)][hashtable] $Services,
        [Parameter(Mandatory)][string] $EnvironmentName
    )

    $expectedEnabled = if ([string]::Equals($EnvironmentName, 'Development', [StringComparison]::Ordinal)) { 'true' } else { 'false' }
    $projectServices = @($Services.GetEnumerator() | Where-Object { $_.Value.ContainsKey('ASPNETCORE_ENVIRONMENT') })
    if ($projectServices.Count -ne 21) { throw "Expected 21 published project resources, got $($projectServices.Count)." }
    foreach ($service in $projectServices) {
        if ($service.Value['ASPNETCORE_ENVIRONMENT'] -ne $EnvironmentName -or $service.Value['DOTNET_ENVIRONMENT'] -ne $EnvironmentName) {
            throw "Service '$($service.Key)' did not inherit $EnvironmentName for both .NET environment variables."
        }
    }

    $persistentServices = @($projectServices | Where-Object { $_.Value.ContainsKey('Persistence__AutoMigrate') })
    if ($persistentServices.Count -ne 18) { throw "Expected 18 published persistent project resources, got $($persistentServices.Count)." }
    foreach ($service in $persistentServices) {
        if ($service.Value['Persistence__AutoMigrate'] -ne $expectedEnabled) {
            throw "Service '$($service.Key)' has Persistence__AutoMigrate='$($service.Value['Persistence__AutoMigrate'])', expected '$expectedEnabled'."
        }
    }

    foreach ($key in @('Iam__Seed__Enabled', 'Erp__Seed__SalesOrderDemandDemo__Enabled')) {
        $matching = @($projectServices | Where-Object { $_.Value.ContainsKey($key) })
        if ($matching.Count -ne 1 -or $matching[0].Value[$key] -ne $expectedEnabled) {
            throw "Published $EnvironmentName artifact must set $key to '$expectedEnabled'."
        }
    }
    foreach ($service in @($projectServices | Where-Object { $_.Value.ContainsKey('Walkthrough__Seed__Enabled') })) {
        if ($service.Value['Walkthrough__Seed__Enabled'] -ne $expectedEnabled) {
            throw "Service '$($service.Key)' has an invalid Walkthrough seed value."
        }
    }
    foreach ($service in @($projectServices | Where-Object { $_.Value.ContainsKey('LeaderDemo__Seed__Enabled') })) {
        if ($service.Value['LeaderDemo__Seed__Enabled'] -ne 'false') {
            throw "Service '$($service.Key)' unexpectedly enables LeaderDemo seed without the opt-in."
        }
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-apphost-environment-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    foreach ($environmentName in @('Development', 'Production')) {
        $outputPath = Join-Path $temporaryRoot $environmentName.ToLowerInvariant()
        Invoke-Aspire -Arguments @('publish', '--output-path', $outputPath, '--environment', $environmentName, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds $PublishTimeoutSeconds -Name "verify-apphost-$($environmentName.ToLowerInvariant())-publish" | Out-Null
        $composePath = Join-Path $outputPath 'docker-compose.yaml'
        if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) { throw "Aspire publish did not produce $composePath." }
        Assert-EnvironmentArtifact -Services (Get-ComposeProjectEnvironments -ComposePath $composePath) -EnvironmentName $environmentName
    }
    Write-Diagnostic 'Aspire Development and Production Compose artifacts preserve the environment, migration, and seed profiles.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
