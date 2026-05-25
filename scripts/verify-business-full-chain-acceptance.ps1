# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores the #77 business full-chain acceptance and supporting WMS/MES/ERP web test projects unless -SkipRestore is set
#     - Runs the WMS service-local live HTTP TestServer acceptance proof plus MES/ERP public-surface support tests and the #77 business full-chain acceptance suite
#     - Does not start Docker, PostgreSQL, RabbitMQ, external WCS hardware, or long-running service processes
#   Writes:
#     - bin/ and obj/ build outputs under verified .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - No long-running services or external dependencies are started
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

param(
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$acceptanceProject = "backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj"
$supportingProjects = @(
    "backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj",
    "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj",
    "backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj"
)

if (-not $SkipRestore) {
    foreach ($project in $supportingProjects) {
        Invoke-DotNet -Name "business-full-chain-support-restore" -WorkingDirectory $root -Arguments @(
            "restore",
            $project
        )
    }

    Invoke-DotNet -Name "business-full-chain-acceptance-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        $acceptanceProject
    )
}

Invoke-DotNet -Name "business-full-chain-wms-support-test" -WorkingDirectory $root -Arguments @(
    "test",
    $supportingProjects[0],
    "--no-restore",
    "--filter",
    "FullyQualifiedName~WmsEndpointContractTests|FullyQualifiedName~WmsIntegrationEventTests"
)

Invoke-DotNet -Name "business-full-chain-mes-support-test" -WorkingDirectory $root -Arguments @(
    "test",
    $supportingProjects[1],
    "--no-restore",
    "--filter",
    "FullyQualifiedName~MesEndpointContractTests"
)

Invoke-DotNet -Name "business-full-chain-erp-support-test" -WorkingDirectory $root -Arguments @(
    "test",
    $supportingProjects[2],
    "--no-restore",
    "--filter",
    "FullyQualifiedName~ErpSalesFinanceEndpointContractTests"
)

$testArguments = @(
    "test",
    $acceptanceProject,
    "--no-restore"
)

Invoke-DotNet -Name "business-full-chain-acceptance-test" -WorkingDirectory $root -Arguments $testArguments

Write-Host "Business full-chain acceptance #77 verified: WMS live HTTP TestServer proof, MES/ERP public surfaces, and seven-chain acceptance evidence are covered by the governed suite."
