# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts the local Nerv-IIP platform through Aspire CLI or dependency services through Docker Compose
#   Writes:
#     - artifacts/script-logs/** when -InfraOnly uses the Docker Compose helper
#     - frontend/node_modules/** when the default startup path restores frontend workspace dependencies
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

    $appHostSource = Join-Path (Split-Path -Parent $AppHostProject) 'Program.cs'
    $sourceText = Get-Content -LiteralPath $appHostSource -Raw
    $explicitSecrets = [regex]::Matches($sourceText, 'AddParameter\s*\(\s*"(?<name>[^"]+)"\s*,\s*secret\s*:\s*true\s*\)') |
        ForEach-Object { "Parameters:$($_.Groups['name'].Value)" }

    $requiredSecrets = @(
        $explicitSecrets
        # Aspire's Postgres integration owns this parameter implicitly; it is not declared
        # through AddParameter(...) in the AppHost source, but local startup still needs it.
        'Parameters:postgres-password'
    ) | Sort-Object -Unique

    $matchedExplicitCount = @($explicitSecrets).Count
    if ($matchedExplicitCount -eq 0) {
        throw "Could not discover required AppHost secret parameters from $appHostSource."
    }

    $appHostSecretParameterNames = @(
        [regex]::Matches($sourceText, 'AddParameter\s*\(\s*"(?<name>[^"]+)"') |
            ForEach-Object { $_.Groups['name'].Value }
    )

    $nonSecretAppHostParameters = @($appHostSecretParameterNames | Where-Object {
        $requiredSecrets -notcontains "Parameters:$_"
    })
    if ($nonSecretAppHostParameters.Count -gt 0) {
        Write-Diagnostic -Level 'WARN' -Message "Found AppHost parameters that are not marked secret and were not required by dev preflight: $($nonSecretAppHostParameters -join ', ')"
    }

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

Run .\nerv.ps1 bootstrap -SkipRestore to initialize local Development secrets, or set them manually, for example:
$($commands -join "`n")
"@
    }
}

function Get-AspireResourceSnapshot {
    $snapshot = Invoke-AspireOutput -Arguments @('describe', '--format', 'Json', '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'dev-describe-resources'
    return $snapshot.Stdout | ConvertFrom-Json
}

function Get-AspireResourceProperty {
    param(
        [Parameter(Mandatory)]
        [object] $Resource,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $property = $Resource.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Write-AspireProjectResourceLogs {
    param(
        [Parameter(Mandatory)]
        [string] $ResourceName,

        [int] $Tail = 120
    )

    try {
        $logs = Invoke-AspireOutput -Arguments @('logs', $ResourceName, '--tail', "$Tail", '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name "dev-logs-$ResourceName"
        $lines = @($logs.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($lines.Count -eq 0) {
            return
        }

        Write-Diagnostic -Level 'ERROR' -Message "Recent Aspire logs for '$ResourceName':"
        foreach ($line in $lines) {
            Write-Diagnostic -Level 'ERROR' -Message "  $line"
        }
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not collect Aspire logs for '$ResourceName': $($_.Exception.Message)"
    }
}

function Assert-NoStoppedAspireProjectResources {
    try {
        $snapshot = Get-AspireResourceSnapshot
        $stoppedProjects = @($snapshot.resources | Where-Object {
            (Get-AspireResourceProperty -Resource $_ -Name 'resourceType') -eq 'Project' -and
            ((Get-AspireResourceProperty -Resource $_ -Name 'state') -eq 'Finished' -or
                (Get-AspireResourceProperty -Resource $_ -Name 'state') -eq 'Failed')
        } | Sort-Object displayName)
    }
    catch {
        Write-Diagnostic -Level 'WARN' -Message "Could not inspect Aspire project resource states: $($_.Exception.Message)"
        return
    }

    if ($stoppedProjects.Count -eq 0) {
        return
    }

    Write-Diagnostic -Level 'ERROR' -Message 'One or more Aspire project resources stopped during startup:'
    foreach ($resource in $stoppedProjects) {
        $exitCodeValue = Get-AspireResourceProperty -Resource $resource -Name 'exitCode'
        $exitCode = if ($null -ne $exitCodeValue) { $exitCodeValue } else { '<none>' }
        $displayName = Get-AspireResourceProperty -Resource $resource -Name 'displayName'
        $name = Get-AspireResourceProperty -Resource $resource -Name 'name'
        $state = Get-AspireResourceProperty -Resource $resource -Name 'state'
        Write-Diagnostic -Level 'ERROR' -Message "  $displayName [$name] state=$state exitCode=$exitCode"
        Write-AspireProjectResourceLogs -ResourceName $displayName
    }

    throw "Aspire project resource startup failed: $(@($stoppedProjects | ForEach-Object { Get-AspireResourceProperty -Resource $_ -Name 'displayName' }) -join ', ')"
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

function Restore-FrontendWorkspaceDependencies {
    Write-Diagnostic 'Restoring frontend workspace dependencies before Aspire starts Vite resources.'
    Invoke-Pnpm `
        -Arguments @('install', '--frozen-lockfile') `
        -WorkingDirectory (Join-Path $root 'frontend') `
        -TimeoutSeconds 900 `
        -Name 'dev-frontend-install' | Out-Null
    Write-Diagnostic 'Frontend workspace dependencies are ready.'
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
        Assert-NoStoppedAspireProjectResources
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
Restore-FrontendWorkspaceDependencies

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

Assert-NoStoppedAspireProjectResources

foreach ($resource in @('gateway', 'business-gateway', 'console', 'business-console')) {
    Wait-AspireResource -Name $resource -Status 'up' -TimeoutSeconds 600
}

Assert-NoStoppedAspireProjectResources

$status = Invoke-AspireOutput -Arguments @('ps', '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name 'dev-status'
Write-Host $status.Stdout

foreach ($resource in @('gateway', 'business-gateway', 'console', 'business-console')) {
    $description = Invoke-AspireOutput -Arguments @('describe', $resource, '--apphost', $appHostProject, '--non-interactive', '--nologo') -WorkingDirectory $root -TimeoutSeconds 60 -Name "dev-describe-$resource"
    Write-Host $description.Stdout
}

Write-Diagnostic 'Nerv-IIP local platform startup completed.'
exit 0
