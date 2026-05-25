# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Publishes Nerv-IIP .NET services into artifacts/packages/**
#     - Builds frontend apps unless -SkipFrontend is specified
#     - Creates a zip package under artifacts/packages/**
#   Writes:
#     - artifacts/packages/**
#     - bin/ and obj/ build outputs under published projects
#     - frontend/**/dist when frontend build is enabled
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes only the package directory for the requested version before regenerating it
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js and pnpm for frontend package generation unless -SkipFrontend is specified

[CmdletBinding()]
param(
    [string] $Version = (Get-Date -Format "yyyyMMdd-HHmmss"),

    [switch] $SkipFrontend
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$packageRoot = Join-Path $root "artifacts/packages/nerv-iip-$Version"
$resolvedRoot = (Resolve-Path $root).Path
$packageParent = Join-Path $root "artifacts/packages"
New-Item -ItemType Directory -Force -Path $packageParent | Out-Null

if (Test-Path $packageRoot) {
    $resolvedPackage = (Resolve-Path $packageRoot).Path
    if (-not $resolvedPackage.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove package directory outside workspace: $resolvedPackage"
    }

    Remove-Item -LiteralPath $resolvedPackage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

$dotnetServices = @(
    @{ Name = "apphub"; Project = "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj" },
    @{ Name = "iam"; Project = "backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj" },
    @{ Name = "ops"; Project = "backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj" },
    @{ Name = "file-storage"; Project = "backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj" },
    @{ Name = "notification"; Project = "backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj" },
    @{ Name = "platform-gateway"; Project = "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj" },
    @{ Name = "business-gateway"; Project = "backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj" },
    @{ Name = "business-master-data"; Project = "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj" },
    @{ Name = "business-product-engineering"; Project = "backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj" },
    @{ Name = "business-inventory"; Project = "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj" },
    @{ Name = "business-quality"; Project = "backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj" },
    @{ Name = "business-mes"; Project = "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj" },
    @{ Name = "business-demand-planning"; Project = "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj" },
    @{ Name = "business-barcode-label"; Project = "backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj" },
    @{ Name = "business-approval"; Project = "backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj" },
    @{ Name = "business-wms"; Project = "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj" },
    @{ Name = "business-industrial-telemetry"; Project = "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj" },
    @{ Name = "business-maintenance"; Project = "backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj" },
    @{ Name = "business-erp"; Project = "backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj" },
    @{ Name = "connector-host"; Project = "connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj" }
)

foreach ($service in $dotnetServices) {
    $output = Join-Path $packageRoot "services/$($service.Name)"
    Invoke-DotNet -Name "publish-$($service.Name)" -WorkingDirectory $root -TimeoutSeconds 900 -Arguments @(
        "publish",
        $service.Project,
        "-c",
        "Release",
        "-o",
        $output,
        "/p:UseAppHost=false"
    )
}

if (-not $SkipFrontend) {
    Invoke-Pnpm -Name "frontend-build-for-package" -WorkingDirectory $root -TimeoutSeconds 900 -Arguments @(
        "-C",
        "frontend",
        "build"
    )

    Copy-Item -Path "frontend/apps/console/dist" -Destination (Join-Path $packageRoot "frontend/console") -Recurse
    Copy-Item -Path "frontend/apps/business-console/dist" -Destination (Join-Path $packageRoot "frontend/business-console") -Recurse
}

Copy-Item -Path "infra/compose" -Destination (Join-Path $packageRoot "infra/compose") -Recurse
Copy-Item -Path "infra/docker" -Destination (Join-Path $packageRoot "infra/docker") -Recurse
Copy-Item -Path "infra/postgres" -Destination (Join-Path $packageRoot "infra/postgres") -Recurse
Copy-Item -Path "scripts/install" -Destination (Join-Path $packageRoot "scripts/install") -Recurse

$manifest = [ordered]@{
    name = "nerv-iip"
    version = $Version
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    services = @($dotnetServices | ForEach-Object { $_.Name })
    includesFrontend = -not $SkipFrontend
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8NoBOM -Path (Join-Path $packageRoot "package-manifest.json")

$zipPath = Join-Path $packageParent "nerv-iip-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath
Write-Diagnostic "Nerv-IIP package generated at $zipPath."
