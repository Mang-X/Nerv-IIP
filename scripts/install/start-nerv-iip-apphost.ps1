# Script-Governance:
#   Category: release-install
#   SideEffects:
#     - Starts the platform Aspire AppHost through Aspire CLI
#     - Sets scoped process environment variables for the AppHost run
#   Writes:
#     - bin/ and obj/ build outputs under projects built by Aspire
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables after AppHost exits
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Required external dependencies reachable or started separately, for example via infra/compose/nerv-iip.dependencies.yml

[CmdletBinding()]
param(
    [ValidateSet("Development", "Staging", "Production")]
    [string] $EnvironmentName = "Development",

    [ValidateSet("InMemory", "RabbitMQ")]
    [string] $MessagingProvider = "InMemory",

    [string] $IamJwtSigningKeyId,

    [string] $IamJwtPrivateKeyPem,

    [string] $IamJwtJwksJson,

    [string] $IamSeedAdminPassword,

    [string] $InternalServiceBearerToken,

    [string] $ConnectorHostSecret,

    [string] $ConnectorHostId,

    [string] $ConnectorHostOrganizationId,

    [string] $ConnectorHostEnvironmentId,

    [string] $ConnectorIngestionTokenSigningKey,

    [string] $ExternalClientSecret,

    [string] $MinioRootUser,

    [string] $MinioRootPassword,

    [string] $CorsAllowedOrigins,

    [switch] $UsePostgreSql,

    [switch] $AutoMigrate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if ($EnvironmentName -ne "Development") {
    if ([string]::IsNullOrWhiteSpace($IamJwtSigningKeyId)) {
        throw "-IamJwtSigningKeyId is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamJwtPrivateKeyPem)) {
        throw "-IamJwtPrivateKeyPem is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamJwtJwksJson)) {
        throw "-IamJwtJwksJson is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($InternalServiceBearerToken)) {
        throw "-InternalServiceBearerToken is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectorHostSecret)) {
        throw "-ConnectorHostSecret is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectorHostId)) {
        throw "-ConnectorHostId is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectorHostOrganizationId)) {
        throw "-ConnectorHostOrganizationId is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectorHostEnvironmentId)) {
        throw "-ConnectorHostEnvironmentId is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($ConnectorIngestionTokenSigningKey)) {
        throw "-ConnectorIngestionTokenSigningKey is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamSeedAdminPassword)) {
        throw "-IamSeedAdminPassword is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($MinioRootUser)) {
        throw "-MinioRootUser is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($MinioRootPassword)) {
        throw "-MinioRootPassword is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($CorsAllowedOrigins)) {
        throw "-CorsAllowedOrigins is required outside Development."
    }
}

$environment = @{
    ASPNETCORE_ENVIRONMENT = $EnvironmentName
    DOTNET_ENVIRONMENT = $EnvironmentName
    Messaging__Provider = $MessagingProvider
}

if ($UsePostgreSql) {
    $environment["Persistence__Provider"] = "PostgreSQL"
}

if ($AutoMigrate) {
    if ($EnvironmentName -ne "Development") {
        throw "-AutoMigrate is only allowed in Development."
    }

    $environment["Persistence__AutoMigrate"] = "true"
}

if (-not [string]::IsNullOrWhiteSpace($IamJwtSigningKeyId)) {
    $environment["Iam__Jwt__SigningKeys__0__Kid"] = $IamJwtSigningKeyId
    $environment["Parameters__iam-jwt-signing-key-id"] = $IamJwtSigningKeyId
}

if (-not [string]::IsNullOrWhiteSpace($IamJwtPrivateKeyPem)) {
    $environment["Iam__Jwt__SigningKeys__0__PrivateKeyPem"] = $IamJwtPrivateKeyPem
    $environment["Parameters__iam-jwt-private-key-pem"] = $IamJwtPrivateKeyPem
}

if (-not [string]::IsNullOrWhiteSpace($IamJwtJwksJson)) {
    $environment["Iam__Jwt__JwksJson"] = $IamJwtJwksJson
    $environment["Parameters__iam-jwt-jwks-json"] = $IamJwtJwksJson
}

if (-not [string]::IsNullOrWhiteSpace($IamSeedAdminPassword)) {
    $environment["Iam__Seed__AdminPassword"] = $IamSeedAdminPassword
    $environment["Parameters__iam-seed-admin-password"] = $IamSeedAdminPassword
}

if (-not [string]::IsNullOrWhiteSpace($InternalServiceBearerToken)) {
    $environment["InternalService__BearerToken"] = $InternalServiceBearerToken
    $environment["Parameters__internal-service-bearer-token"] = $InternalServiceBearerToken
}

if (-not [string]::IsNullOrWhiteSpace($ConnectorHostSecret)) {
    $environment["Iam__Seed__ConnectorHostSecret"] = $ConnectorHostSecret
    $environment["ConnectorHostCredential__Secret"] = $ConnectorHostSecret
    $environment["Parameters__iam-seed-connector-host-secret"] = $ConnectorHostSecret
}

if (-not [string]::IsNullOrWhiteSpace($ConnectorHostId)) {
    $environment["ConnectorHost__ConnectorHostId"] = $ConnectorHostId
    $environment["ConnectorHostCredential__ConnectorHostId"] = $ConnectorHostId
}

if (-not [string]::IsNullOrWhiteSpace($ConnectorHostOrganizationId)) {
    $environment["ConnectorHost__OrganizationId"] = $ConnectorHostOrganizationId
    $environment["ConnectorHostCredential__OrganizationId"] = $ConnectorHostOrganizationId
}

if (-not [string]::IsNullOrWhiteSpace($ConnectorHostEnvironmentId)) {
    $environment["ConnectorHost__EnvironmentId"] = $ConnectorHostEnvironmentId
    $environment["ConnectorHostCredential__EnvironmentId"] = $ConnectorHostEnvironmentId
}

if (-not [string]::IsNullOrWhiteSpace($ConnectorIngestionTokenSigningKey)) {
    $environment["ConnectorIngestionToken__SigningKey"] = $ConnectorIngestionTokenSigningKey
    $environment["Parameters__connector-ingestion-token-signing-key"] = $ConnectorIngestionTokenSigningKey
}

if (-not [string]::IsNullOrWhiteSpace($ExternalClientSecret)) {
    $environment["Iam__Seed__ExternalClientSecret"] = $ExternalClientSecret
}

if (-not [string]::IsNullOrWhiteSpace($MinioRootUser)) {
    $environment["Parameters__minio-root-user"] = $MinioRootUser
}

if (-not [string]::IsNullOrWhiteSpace($MinioRootPassword)) {
    $environment["Parameters__minio-root-password"] = $MinioRootPassword
}

if (-not [string]::IsNullOrWhiteSpace($CorsAllowedOrigins)) {
    $environment["Security__Cors__AllowedOrigins"] = $CorsAllowedOrigins
}

$appHostProject = "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
Write-Diagnostic "Starting Nerv-IIP AppHost environment=$EnvironmentName messaging=$MessagingProvider postgres=$UsePostgreSql."

Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
    Invoke-AspireInteractive -Name "nerv-iip-apphost" -WorkingDirectory $root -Arguments @(
        "start",
        "--apphost",
        $appHostProject,
        "--non-interactive",
        "--nologo"
    ) | Out-Null
}
