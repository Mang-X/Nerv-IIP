# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts the local Nerv-IIP platform through Aspire AppHost or dependency services through Docker Compose
#   Writes:
#     - artifacts/script-logs/** when -InfraOnly uses the Docker Compose helper
#   Cleanup:
#     - Stops the managed command if it times out through ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop for container resources
#     - Node.js 22.22.3
#     - pnpm 11.1.2

[CmdletBinding()]
param(
    [switch] $NoBuild,
    [switch] $InfraOnly,
    [switch] $OpenDashboard,
    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Write-DevHelp {
    Write-Host @'
Nerv-IIP local development startup

Usage:
  .\nerv.ps1 dev [-NoBuild] [-InfraOnly] [-OpenDashboard]

Options:
  -NoBuild        Run Aspire AppHost with --no-build.
  -InfraOnly     Start only dependency services from infra/docker-compose.dev.yml.
  -OpenDashboard Print a note that Aspire dashboard URL discovery is manual in this version.
  -Help          Print this help.

Default behavior:
  Starts the full local platform through the Aspire AppHost.
'@
}

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Purpose
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is required for $Purpose."
    }
}

function Get-AppHostUserSecrets {
    param(
        [Parameter(Mandatory)]
        [string] $AppHostProject
    )

    try {
        $result = Invoke-DotNetOutput -Arguments @('user-secrets', 'list', '--project', $AppHostProject) -WorkingDirectory $root -Name 'apphost-user-secrets'
    }
    catch {
        $message = "$($_.Exception.Message)"
        if ($message.Contains("Could not find the global property 'UserSecretsId'") -or $message.Contains('No UserSecretsId')) {
            Write-Diagnostic -Level 'WARN' -Message "AppHost project has no initialized user-secrets store; treating all required AppHost secrets as missing."
            return @{}
        }

        throw
    }

    $output = $result.Stdout -split '\r?\n'

    $secrets = @{}
    foreach ($line in $output) {
        $parts = "$line" -split ' = ', 2
        if ($parts.Count -eq 2) {
            $secrets[$parts[0]] = $parts[1]
        }
    }

    return $secrets
}

function Assert-AppHostUserSecrets {
    param(
        [Parameter(Mandatory)]
        [string] $AppHostProject
    )

    $requiredSecrets = @(
        'Parameters:iam-jwt-signing-key',
        'Parameters:internal-service-bearer-token',
        'Parameters:postgres-password',
        'Parameters:redis-password',
        'Parameters:minio-root-user',
        'Parameters:minio-root-password',
        'Parameters:iam-seed-admin-password',
        'Parameters:iam-seed-connector-host-secret'
    )
    $secrets = Get-AppHostUserSecrets -AppHostProject $AppHostProject
    $missing = @($requiredSecrets | Where-Object {
        -not $secrets.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($secrets[$_])
    })

    if ($missing.Count -gt 0) {
        $commands = $missing | ForEach-Object {
            "dotnet user-secrets set ""$_"" ""<local-value>"" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
        }
        throw @"
Missing required AppHost user secrets:
$($missing -join "`n")

Set them before running .\nerv.ps1 dev, for example:
$($commands -join "`n")
"@
    }
}

if ($Help) {
    Write-DevHelp
    exit 0
}

Set-Location $root

if ($InfraOnly) {
    Assert-CommandAvailable -Name 'docker' -Purpose 'dependency-only startup'
    $composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', 'postgres', 'redis', 'rabbitmq', 'minio', 'otel-collector') -WorkingDirectory $root -TimeoutSeconds 240 -Name 'dev-infra-only' | Out-Null
    Write-Host 'Dependency services are starting from infra/docker-compose.dev.yml.'
    exit 0
}

Assert-CommandAvailable -Name 'dotnet' -Purpose 'Aspire AppHost startup'
Assert-CommandAvailable -Name 'docker' -Purpose 'Aspire container resources'
Assert-CommandAvailable -Name 'node' -Purpose 'Console Vite startup'
Assert-CommandAvailable -Name 'pnpm' -Purpose 'Console Vite startup'

if ($OpenDashboard) {
    Write-Host 'Aspire dashboard URL discovery is manual in this version. Use the URL printed by dotnet run.'
}

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
Assert-AppHostUserSecrets -AppHostProject $appHostProject

$arguments = @('run', '--project', $appHostProject)
if ($NoBuild) {
    $arguments += '--no-build'
}

$appHostEnvironment = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
}

$result = Invoke-WithScopedEnvironment -Variables $appHostEnvironment -ScriptBlock {
    Invoke-DotNetInteractive -Arguments $arguments -WorkingDirectory $root -Name 'dev-apphost'
}
exit $result.ExitCode
