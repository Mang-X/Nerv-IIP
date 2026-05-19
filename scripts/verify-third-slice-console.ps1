# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs second-slice verification as a nested script
#     - Exports Platform Gateway OpenAPI and regenerates the frontend API client
#     - Runs frontend typecheck, test and build steps
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/node_modules/**
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json
#     - frontend/packages/api-client/src/**
#     - frontend/**/.nuxt/**
#     - frontend/**/.output/**
#     - frontend/**/dist/**
#     - frontend/**/coverage/**
#   Cleanup:
#     - Stops managed nested script or pnpm process trees when they time out through ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop
#     - Node.js 22.22.3
#     - pnpm 11.1.2

param(
  [switch]$UsePostgres
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$secondSliceScript = Join-Path $root "scripts/verify-second-slice-ops.ps1"
if ($UsePostgres) {
  Invoke-PwshScript -ScriptPath $secondSliceScript -Arguments @("-UsePostgres") -WorkingDirectory $root -TimeoutSeconds 1200 -Name "third-second-slice-ops-postgres" | Out-Null
}
else {
  Invoke-PwshScript -ScriptPath $secondSliceScript -WorkingDirectory $root -TimeoutSeconds 1200 -Name "third-second-slice-ops" | Out-Null
}

Invoke-PwshScript -ScriptPath (Join-Path $root "scripts/export-gateway-openapi.ps1") -WorkingDirectory $root -TimeoutSeconds 600 -Name "third-export-gateway-openapi" | Out-Null
Invoke-Pnpm -Arguments @("-C", "frontend", "install", "--frozen-lockfile", "--config.confirmModulesPurge=false") -WorkingDirectory $root -TimeoutSeconds 900 -Name "third-frontend-install" | Out-Null
Invoke-Pnpm -Arguments @("-C", "frontend", "generate:api") -WorkingDirectory $root -TimeoutSeconds 600 -Name "third-frontend-generate-api" | Out-Null
Invoke-Pnpm -Arguments @("-C", "frontend", "typecheck") -WorkingDirectory $root -TimeoutSeconds 600 -Name "third-frontend-typecheck" | Out-Null
Invoke-Pnpm -Arguments @("-C", "frontend", "test") -WorkingDirectory $root -TimeoutSeconds 600 -Name "third-frontend-test" | Out-Null
Invoke-Pnpm -Arguments @("-C", "frontend", "build") -WorkingDirectory $root -TimeoutSeconds 900 -Name "third-frontend-build" | Out-Null

Write-Host "Third vertical slice console verified."
