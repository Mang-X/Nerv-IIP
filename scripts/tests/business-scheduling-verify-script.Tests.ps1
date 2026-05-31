# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the BusinessScheduling APS lite verify script
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-business-scheduling-aps-lite.ps1'
$content = Get-Content -Path $verifyScript -Raw

if (-not $content.Contains('business-scheduling-gateway-openapi-tests')) {
    throw 'BusinessScheduling APS lite verify script must run the BusinessGateway OpenAPI enum regression tests.'
}

if (-not $content.Contains('FullyQualifiedName~BusinessGatewayOpenApiTests')) {
    throw 'BusinessScheduling APS lite verify script must target BusinessGatewayOpenApiTests explicitly.'
}

Write-Host 'Business Scheduling verify script coverage tests passed.'
