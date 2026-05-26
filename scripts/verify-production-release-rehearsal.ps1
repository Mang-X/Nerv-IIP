# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts a disposable Docker Compose release rehearsal project with production deployment artifacts
#     - For platform-smoke, builds selected platform images and runs Development-only auto-migration smoke against disposable PostgreSQL volumes
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables
#     - Stops and removes the disposable Compose project and its volumes unless -KeepRunning is specified
#   Requires:
#     - PowerShell 7
#     - Docker CLI with compose plugin

[CmdletBinding()]
param(
    [ValidateSet("", "dependencies", "platform-smoke")]
    [string] $Profile = "",

    [switch] $SkipBuild,

    [switch] $KeepRunning,

    [string] $ProjectName,

    [int] $HostPortBase = 18000,

    [int] $TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

function Assert-ReleaseRehearsalProfile {
    if ([string]::IsNullOrWhiteSpace($Profile)) {
        throw "Set -Profile dependencies or -Profile platform-smoke. Release rehearsal is opt-in and does not start deployment containers by default."
    }
}

function Assert-ReleaseRehearsalEnvironment {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Environment unavailable: Docker CLI is required for production release rehearsal."
    }

    Invoke-NativeCommandWithTimeout -Command "docker" -Arguments @("version") -WorkingDirectory $root -TimeoutSeconds 30 -Name "release-rehearsal-docker-version" | Out-Null
    Invoke-DockerCompose -Arguments @("version") -WorkingDirectory $root -TimeoutSeconds 30 -Name "release-rehearsal-docker-compose-version" | Out-Null
}

function New-ReleaseRehearsalEnvironment {
    param(
        [Parameter(Mandatory)]
        [int] $PortBase
    )

    $environment = @{
        ASPNETCORE_ENVIRONMENT = "Production"
        DOTNET_ENVIRONMENT = "Production"
        NERV_IIP_POSTGRES_USER = "nerv"
        NERV_IIP_POSTGRES_PASSWORD = "postgres-password-32chars-test"
        NERV_IIP_POSTGRES_DB = "nerv_iip"
        NERV_IIP_MINIO_ROOT_USER = "minioadmin"
        NERV_IIP_MINIO_ROOT_PASSWORD = "minio-password-32chars-test"
        NERV_IIP_INTERNAL_SERVICE_BEARER_TOKEN = "internal-token-32chars-test-value"
        NERV_IIP_CONNECTOR_HOST_SECRET = "connector-secret-32chars-test-value"
        NERV_IIP_IAM_JWT_SIGNING_KEY = "iam-jwt-signing-key-32chars-test-value"
        NERV_IIP_CORS_ALLOWED_ORIGINS = "https://console.example.test,https://business.example.test"
        NERV_IIP_IMAGE_TAG = "release-rehearsal"
        NERV_IIP_AUTO_MIGRATE = "false"
        NERV_IIP_POSTGRES_PORT = ($PortBase + 432).ToString()
        NERV_IIP_REDIS_PORT = ($PortBase + 379).ToString()
        NERV_IIP_RABBITMQ_PORT = ($PortBase + 672).ToString()
        NERV_IIP_RABBITMQ_MANAGEMENT_PORT = ($PortBase + 1672).ToString()
        NERV_IIP_MINIO_API_PORT = ($PortBase + 900).ToString()
        NERV_IIP_MINIO_CONSOLE_PORT = ($PortBase + 901).ToString()
        NERV_IIP_OTEL_GRPC_PORT = ($PortBase + 317).ToString()
        NERV_IIP_OTEL_HTTP_PORT = ($PortBase + 318).ToString()
        NERV_IIP_APPHUB_PORT = ($PortBase + 101).ToString()
        NERV_IIP_IAM_PORT = ($PortBase + 102).ToString()
        NERV_IIP_OPS_PORT = ($PortBase + 103).ToString()
        NERV_IIP_FILE_STORAGE_PORT = ($PortBase + 104).ToString()
        NERV_IIP_CONSOLE_PORT = ($PortBase + 105).ToString()
        NERV_IIP_NOTIFICATION_PORT = ($PortBase + 106).ToString()
        NERV_IIP_GATEWAY_PORT = ($PortBase + 100).ToString()
        NERV_IIP_BUSINESS_GATEWAY_PORT = ($PortBase + 119).ToString()
        NERV_IIP_BUSINESS_CONSOLE_PORT = ($PortBase + 125).ToString()
    }

    if ($Profile -eq "platform-smoke") {
        $environment.ASPNETCORE_ENVIRONMENT = "Development"
        $environment.DOTNET_ENVIRONMENT = "Development"
        $environment.NERV_IIP_AUTO_MIGRATE = "true"
    }

    return $environment
}

function Get-ReleaseRehearsalComposeArguments {
    $arguments = @(
        "-p",
        $effectiveProjectName,
        "-f",
        "infra/compose/nerv-iip.dependencies.yml"
    )

    if ($Profile -eq "platform-smoke") {
        $arguments += @(
            "-f",
            "infra/compose/nerv-iip.platform.yml"
        )
    }

    return $arguments
}

function Get-ReleaseRehearsalServices {
    if ($Profile -eq "dependencies") {
        return @("postgres", "redis", "minio", "otel-collector")
    }

    return @(
        "postgres",
        "redis",
        "minio",
        "otel-collector",
        "apphub",
        "iam",
        "ops",
        "file-storage",
        "notification",
        "gateway",
        "business-gateway"
    )
}

function Wait-ReleaseRehearsalHttpHealth {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Uri,

        [int] $WaitSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ([int] $response.StatusCode -ge 200 -and [int] $response.StatusCode -lt 300) {
                Write-Diagnostic "Health check passed for $Name at $Uri."
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 750
        }

        Start-Sleep -Milliseconds 750
    } while ((Get-Date) -lt $deadline)

    throw "Health check did not pass for $Name at $Uri within $WaitSeconds seconds."
}

function Invoke-ReleaseRehearsalSmokeChecks {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Environment
    )

    $composeBase = Get-ReleaseRehearsalComposeArguments

    Invoke-DockerCompose -Arguments ($composeBase + @(
            "exec",
            "-T",
            "postgres",
            "pg_isready",
            "-U",
            $Environment.NERV_IIP_POSTGRES_USER,
            "-d",
            $Environment.NERV_IIP_POSTGRES_DB
        )) -WorkingDirectory $root -TimeoutSeconds 60 -Name "release-rehearsal-postgres-smoke" | Out-Null

    Invoke-DockerCompose -Arguments ($composeBase + @(
            "exec",
            "-T",
            "redis",
            "redis-cli",
            "ping"
        )) -WorkingDirectory $root -TimeoutSeconds 60 -Name "release-rehearsal-redis-smoke" | Out-Null

    Wait-ReleaseRehearsalHttpHealth -Name "minio" -Uri "http://localhost:$($Environment.NERV_IIP_MINIO_API_PORT)/minio/health/live" -WaitSeconds 90

    if ($Profile -ne "platform-smoke") {
        return
    }

    $healthTargets = @(
        [pscustomobject]@{ Name = "apphub"; Uri = "http://localhost:$($Environment.NERV_IIP_APPHUB_PORT)/health" },
        [pscustomobject]@{ Name = "iam"; Uri = "http://localhost:$($Environment.NERV_IIP_IAM_PORT)/health" },
        [pscustomobject]@{ Name = "ops"; Uri = "http://localhost:$($Environment.NERV_IIP_OPS_PORT)/health" },
        [pscustomobject]@{ Name = "file-storage"; Uri = "http://localhost:$($Environment.NERV_IIP_FILE_STORAGE_PORT)/health" },
        [pscustomobject]@{ Name = "notification"; Uri = "http://localhost:$($Environment.NERV_IIP_NOTIFICATION_PORT)/health" },
        [pscustomobject]@{ Name = "gateway"; Uri = "http://localhost:$($Environment.NERV_IIP_GATEWAY_PORT)/health" },
        [pscustomobject]@{ Name = "business-gateway"; Uri = "http://localhost:$($Environment.NERV_IIP_BUSINESS_GATEWAY_PORT)/health" }
    )

    foreach ($target in $healthTargets) {
        Wait-ReleaseRehearsalHttpHealth -Name $target.Name -Uri $target.Uri -WaitSeconds 180
    }
}

Assert-ReleaseRehearsalProfile

if ($HostPortBase -lt 10000 -or $HostPortBase -gt 55000) {
    throw "-HostPortBase must be between 10000 and 55000."
}

if ($TimeoutSeconds -lt 60) {
    throw "-TimeoutSeconds must be at least 60."
}

Assert-ReleaseRehearsalEnvironment

$effectiveProjectName = if ([string]::IsNullOrWhiteSpace($ProjectName)) {
    "nerv-iip-rehearsal-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
}
else {
    $ProjectName
}

$summaryDirectory = New-ScriptAutomationLogDirectory -Name "production-release-rehearsal"
$summaryPath = Join-Path $summaryDirectory "summary.json"
$environment = New-ReleaseRehearsalEnvironment -PortBase $HostPortBase
$composeBaseArguments = Get-ReleaseRehearsalComposeArguments
$services = @(Get-ReleaseRehearsalServices)
$composeTouched = $true
$passed = $false

try {
    Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
        Invoke-DockerCompose -Arguments ($composeBaseArguments + @("config", "--quiet")) -WorkingDirectory $root -TimeoutSeconds 120 -Name "release-rehearsal-compose-config" | Out-Null

        $upArguments = $composeBaseArguments + @(
            "up",
            "-d",
            "--wait",
            "--wait-timeout",
            $TimeoutSeconds.ToString()
        )

        if ($Profile -eq "platform-smoke" -and -not $SkipBuild) {
            $upArguments += "--build"
        }

        Invoke-DockerCompose -Arguments ($upArguments + $services) -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds -Name "release-rehearsal-compose-up" | Out-Null
        Invoke-ReleaseRehearsalSmokeChecks -Environment $environment
    }

    $passed = $true
}
finally {
    if ($composeTouched -and -not $KeepRunning) {
        Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
            Invoke-DockerCompose -Arguments ($composeBaseArguments + @(
                    "down",
                    "--volumes",
                    "--remove-orphans",
                    "--timeout",
                    "30"
                )) -WorkingDirectory $root -TimeoutSeconds 180 -Name "release-rehearsal-compose-down" | Out-Null
        }
    }

    $summary = [pscustomobject]@{
        profile = $Profile
        projectName = $effectiveProjectName
        services = $services
        hostPortBase = $HostPortBase
        keepRunning = [bool] $KeepRunning
        skippedBuild = [bool] $SkipBuild
        passed = $passed
        summaryPath = $summaryPath
    }

    $summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding utf8NoBOM
}

Write-Host "Production release rehearsal verified for profile '$Profile'. Summary: $summaryPath"
