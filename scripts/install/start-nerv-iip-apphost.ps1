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

    [string] $IamSecretsPepper,

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

    [string] $InventorySiteCode,

    [string] $InventorySourceLocationCodes,

    [string] $InventoryLineSideLocationCode,

    [string] $InventoryFinishedGoodsLocationCode,

    [string] $MaterialIssueSourceLocationCode,

    [string] $MaterialIssueLineSideLocationCode,

    [switch] $UsePostgreSql,

    [switch] $AutoMigrate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if ((-not [string]::Equals([string]($EnvironmentName), [string]("Development"), [StringComparison]::OrdinalIgnoreCase))) {
    if ([string]::IsNullOrWhiteSpace($IamJwtSigningKeyId)) {
        throw "-IamJwtSigningKeyId is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamJwtPrivateKeyPem)) {
        throw "-IamJwtPrivateKeyPem is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamJwtJwksJson)) {
        throw "-IamJwtJwksJson is required outside Development."
    }

    if ([string]::IsNullOrWhiteSpace($IamSecretsPepper)) {
        throw "-IamSecretsPepper is required outside Development."
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
    if ((-not [string]::Equals([string]($EnvironmentName), [string]("Development"), [StringComparison]::OrdinalIgnoreCase))) {
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

if (-not [string]::IsNullOrWhiteSpace($IamSecretsPepper)) {
    $environment["Iam__Secrets__Pepper"] = $IamSecretsPepper
    $environment["Parameters__iam-secrets-pepper"] = $IamSecretsPepper
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

# 仓储站点/库位：AppHost 只在 Development 回落到主线产品位置词汇（SITE-001 + loc-*），非 Development
# 必须由这里显式给出真实值，否则相关键根本不下发，MES 线边收料与 WMS 领料按各自 fail-closed
# 路径显式失败（#2008）。键名与服务读取的配置节同名。
if (-not [string]::IsNullOrWhiteSpace($InventorySiteCode)) {
    $environment["Inventory__SiteCode"] = $InventorySiteCode
}

if (-not [string]::IsNullOrWhiteSpace($InventorySourceLocationCodes)) {
    # 逗号/分号分隔的候选来源库位，按索引键下发；不下发标量键，避免同一配置路径既有值又有子节点。
    $sourceLocationCodes = @($InventorySourceLocationCodes -split '[,;]' |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    for ($sourceLocationIndex = 0; $sourceLocationIndex -lt $sourceLocationCodes.Count; $sourceLocationIndex++) {
        $environment["Inventory__SourceLocationCodes__$sourceLocationIndex"] = $sourceLocationCodes[$sourceLocationIndex]
    }
}

if (-not [string]::IsNullOrWhiteSpace($InventoryLineSideLocationCode)) {
    $environment["Inventory__LineSideLocationCode"] = $InventoryLineSideLocationCode
}

if (-not [string]::IsNullOrWhiteSpace($InventoryFinishedGoodsLocationCode)) {
    $environment["Inventory__FinishedGoodsLocationCode"] = $InventoryFinishedGoodsLocationCode
}

if (-not [string]::IsNullOrWhiteSpace($MaterialIssueSourceLocationCode)) {
    $environment["MaterialIssue__SourceLocationCode"] = $MaterialIssueSourceLocationCode
}

if (-not [string]::IsNullOrWhiteSpace($MaterialIssueLineSideLocationCode)) {
    $environment["MaterialIssue__LineSideLocationCode"] = $MaterialIssueLineSideLocationCode
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
