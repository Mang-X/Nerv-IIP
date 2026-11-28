# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the production Compose port boundary against in-memory fixtures
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '../lib/ProductionComposePorts.ps1')

$compose = @'
services:
  iam:
    expose: ["8080"]
  business-approval:
    environment:
      ASPNETCORE_HTTP_PORTS: "8080"
  gateway:
    ports: ["5100:8080"]
  business-gateway:
    ports: ["5119:8080"]
  business-console:
    ports: ["5125:8080"]
volumes:
  business-approval:
    name: "approval-data"
'@

Assert-ProductionComposeInternalPorts -ServiceBlocks (Get-ComposeServiceBlocks -ComposeText $compose)

foreach ($case in @(
    @{ Service = 'iam'; Ports = '    ports: ["8080"]' }
    @{ Service = 'business-approval'; Ports = "    ports:`n      - target: 8080`n        published: `"5114`"" }
)) {
    $changed = $compose -creplace "(?m)^(  $($case.Service):)\r?(?=\n    (?:expose|environment):)", "`$1`n$($case.Ports)"
    $rejected = $false
    try {
        Assert-ProductionComposeInternalPorts -ServiceBlocks (Get-ComposeServiceBlocks -ComposeText $changed)
    }
    catch {
        if (-not [string]::Equals($_.Exception.Message, "服务 $($case.Service) 不得发布宿主机端口。", [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Production Compose accepted a host port on $($case.Service)." }
}

Write-Host 'Production Compose internal port contracts passed.'
