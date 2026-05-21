# Script-Governance:
#   Category: check
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
$arguments = @('run', '--project', $appHostProject)
if ($NoBuild) {
    $arguments += '--no-build'
}

$result = Invoke-DotNetInteractive -Arguments $arguments -WorkingDirectory $root -Name 'dev-apphost'
exit $result.ExitCode
