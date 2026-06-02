# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Publishes, prepares, or deploys the Aspire Docker Compose deployment target
#     - Deploy mode can build images and start/update local Docker Compose containers through Aspire CLI
#   Writes:
#     - artifacts/aspire-output/compose/** by default
#     - artifacts/script-logs/**
#   Cleanup:
#     - Does not destroy deployments; use Aspire destroy explicitly after review
#   Requires:
#     - PowerShell 7
#     - Aspire CLI 13.4
#     - Docker or Podman for Prepare/Deploy modes

[CmdletBinding()]
param(
    [ValidateSet('ListSteps', 'Publish', 'Prepare', 'Deploy')]
    [string] $Mode = 'Publish',

    [string] $EnvironmentName = 'Production',

    [string] $OutputPath = 'artifacts/aspire-output/compose',

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

Set-Location $root

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
$resolvedOutputPath = Join-Path $root $OutputPath
Get-AspireCliCommand | Out-Null

switch ($Mode) {
    'ListSteps' {
        $arguments = @('publish', '--list-steps')
    }
    'Publish' {
        $arguments = @('publish', '--output-path', $resolvedOutputPath)
    }
    'Prepare' {
        $arguments = @('do', 'prepare-compose', '--output-path', $resolvedOutputPath)
    }
    'Deploy' {
        $arguments = @('deploy', '--output-path', $resolvedOutputPath)
    }
}

$arguments += @(
    '--environment',
    $EnvironmentName,
    '--apphost',
    $appHostProject,
    '--non-interactive',
    '--nologo'
)

if ($NoBuild) {
    $arguments += '--no-build'
}

Write-Diagnostic "Running Aspire Compose mode=$Mode environment=$EnvironmentName output=$resolvedOutputPath."
$result = Invoke-AspireInteractive -Arguments $arguments -WorkingDirectory $root -Name "aspire-compose-$($Mode.ToLowerInvariant())"
exit $result.ExitCode
