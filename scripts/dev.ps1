# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts the local Nerv-IIP platform through Aspire CLI or dependency services through Docker Compose
#   Writes:
#     - artifacts/script-logs/** when -InfraOnly uses the Docker Compose helper
#   Cleanup:
#     - Aspire-managed resources remain running until `.\nerv.ps1 stop`
#     - Stops the managed command if a helper command times out through ScriptAutomation.ps1
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

function Test-IsLinkedWorktree {
    $gitPath = Join-Path $root '.git'
    if (Test-Path -LiteralPath $gitPath -PathType Leaf) {
        return $true
    }

    $normalizedRoot = $root.Path -replace '\\', '/'
    return $normalizedRoot.Contains('/worktrees/')
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

function Assert-DevelopmentHttpsCertificateTrusted {
    Write-Diagnostic 'Checking local HTTPS developer certificate trust.'

    try {
        Invoke-DotNetOutput -Arguments @('dev-certs', 'https', '--check', '--trust') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'dev-cert-check' | Out-Null
        Write-Diagnostic 'Local HTTPS developer certificate is trusted.'
    }
    catch {
        throw @"
Local HTTPS developer certificate is missing or not trusted.

Run these commands once, then retry .\nerv.ps1 dev:
  aspire certs trust
  dotnet dev-certs https --trust

If Aspire/DCP still reports certificate name mismatch, reset the local Aspire certificate cache first:
  aspire certs clean
  aspire certs trust
  dotnet dev-certs https --trust

Details:
$($_.Exception.Message)
"@
    }
}

function Write-AspireDockerContainerSummary {
    try {
        $containers = Invoke-NativeCommandOutput `
            -Command 'docker' `
            -Arguments @(
                'ps',
                '-a',
                '--filter',
                'label=com.microsoft.developer.usvc-dev.group-version=usvc-dev.developer.microsoft.com/v1',
                '--format',
                '{{.Names}} | {{.Image}} | {{.Status}}'
            ) `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name 'dev-docker-container-summary'

        $lines = @($containers.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -eq 0) {
            Write-Diagnostic -Level 'WARN' -Message 'No Aspire usvc-dev containers were found.'
            return
        }

        Write-Diagnostic -Level 'WARN' -Message 'Current Aspire usvc-dev containers:'
        foreach ($line in $lines) {
            Write-Diagnostic -Level 'WARN' -Message "  $line"
        }
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not collect Docker container summary: $($_.Exception.Message)"
    }
}

function Write-AspireDockerResourceLogs {
    param(
        [Parameter(Mandatory)]
        [string] $ResourceName,

        [int] $Tail = 80
    )

    try {
        $containers = Invoke-NativeCommandOutput `
            -Command 'docker' `
            -Arguments @(
                'ps',
                '-a',
                '--filter',
                'label=com.microsoft.developer.usvc-dev.group-version=usvc-dev.developer.microsoft.com/v1',
                '--format',
                '{{.Names}}'
            ) `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name "dev-$ResourceName-container-list"

        $containerName = @($containers.Stdout -split '\r?\n' | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and $_.StartsWith("$ResourceName-", [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

        if ($containerName.Count -eq 0) {
            return
        }

        $logs = Invoke-NativeCommandOutput `
            -Command 'docker' `
            -Arguments @('logs', $containerName[0], '--tail', "$Tail") `
            -WorkingDirectory $root `
            -TimeoutSeconds 30 `
            -Name "dev-$ResourceName-container-logs"

        $lines = @((($logs.Stdout, $logs.Stderr) -join [Environment]::NewLine) -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -eq 0) {
            return
        }

        Write-Diagnostic -Level 'WARN' -Message "Recent Docker logs for '$ResourceName':"
        foreach ($line in $lines) {
            Write-Diagnostic -Level 'WARN' -Message "  $line"
        }
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not collect Docker logs for '$ResourceName': $($_.Exception.Message)"
    }
}

function Wait-AspireResource {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [ValidateSet('healthy', 'up', 'down')]
        [string] $Status = 'up',

        [int] $TimeoutSeconds = 600
    )

    Write-Diagnostic "Waiting for Aspire resource '$Name' to become '$Status' (timeout=${TimeoutSeconds}s)."
    try {
        Invoke-Aspire -Arguments @('wait', $Name, '--status', $Status, '--timeout', "$TimeoutSeconds", '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds ($TimeoutSeconds + 30) -Name "dev-wait-$Name" | Out-Null
        Write-Diagnostic "Aspire resource '$Name' reached '$Status'."
    }
    catch {
        Write-Diagnostic -Level 'ERROR' -Message "Aspire resource '$Name' did not reach '$Status': $($_.Exception.Message)"
        Write-AspireDockerContainerSummary
        Write-AspireDockerResourceLogs -ResourceName $Name
        throw
    }
}

if ($Help) {
    Write-DevHelp
    exit 0
}

Set-Location $root

if ($InfraOnly) {
    Write-Diagnostic 'Starting dependency-only Docker Compose profile.'
    Assert-CommandAvailable -Name 'docker' -Purpose 'dependency-only startup'
    $composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', 'postgres', 'redis', 'rabbitmq', 'minio', 'otel-collector') -WorkingDirectory $root -TimeoutSeconds 240 -Name 'dev-infra-only' | Out-Null
    Write-Host 'Dependency services are starting from infra/docker-compose.dev.yml.'
    exit 0
}

Write-Diagnostic 'Checking local development prerequisites.'
Assert-CommandAvailable -Name 'dotnet' -Purpose 'AppHost user-secrets preflight'
Assert-CommandAvailable -Name 'docker' -Purpose 'Aspire container resources'
Assert-CommandAvailable -Name 'node' -Purpose 'Console Vite startup'
Assert-CommandAvailable -Name 'pnpm' -Purpose 'Console Vite startup'
Get-AspireCliCommand | Out-Null
Write-Diagnostic 'Local development prerequisites are available.'

if ($OpenDashboard) {
    Write-Host 'Aspire dashboard URL is printed by `aspire start`; use `.\nerv.ps1 status` to rediscover running resources.'
}

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
Write-Diagnostic 'Checking required AppHost user secrets.'
Assert-AppHostUserSecrets -AppHostProject $appHostProject
Write-Diagnostic 'Required AppHost user secrets are present.'
Assert-DevelopmentHttpsCertificateTrusted

$arguments = @('start', '--apphost', $appHostProject, '--non-interactive', '--nologo')
if ($NoBuild) {
    $arguments += '--no-build'
}
if (Test-IsLinkedWorktree) {
    $arguments += '--isolated'
}

$appHostEnvironment = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
}

Write-Diagnostic 'Starting Aspire AppHost. This step is bounded and writes logs under artifacts/script-logs/dev-apphost/.'
try {
    Invoke-WithScopedEnvironment -Variables $appHostEnvironment -ScriptBlock {
        Invoke-Aspire -Arguments $arguments -WorkingDirectory $root -TimeoutSeconds 900 -Name 'dev-apphost'
    } | Out-Null
    Write-Diagnostic 'Aspire AppHost start command completed.'
}
catch {
    Write-Diagnostic -Level 'ERROR' -Message "Aspire AppHost start failed: $($_.Exception.Message)"
    Write-AspireDockerContainerSummary
    throw
}

foreach ($resource in @('postgres', 'redis', 'minio')) {
    Wait-AspireResource -Name $resource -Status 'up' -TimeoutSeconds 240
}

foreach ($resource in @('gateway', 'business-gateway', 'console', 'business-console')) {
    Wait-AspireResource -Name $resource -Status 'up' -TimeoutSeconds 600
}

$status = Invoke-AspireOutput -Arguments @('ps', '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'dev-status'
Write-Host $status.Stdout

foreach ($resource in @('gateway', 'business-gateway', 'console', 'business-console')) {
    $description = Invoke-AspireOutput -Arguments @('describe', $resource, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name "dev-describe-$resource"
    Write-Host $description.Stdout
}

Write-Diagnostic 'Nerv-IIP local platform startup completed.'
exit 0
