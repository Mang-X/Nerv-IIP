param(
  [switch]$UsePostgres
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

if ($UsePostgres) {
  pwsh scripts/verify-second-slice-ops.ps1 -UsePostgres
}
else {
  pwsh scripts/verify-second-slice-ops.ps1
}
pwsh scripts/export-gateway-openapi.ps1
pnpm -C frontend install --frozen-lockfile --config.confirmModulesPurge=false
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build

Write-Host "Third vertical slice console verified."
