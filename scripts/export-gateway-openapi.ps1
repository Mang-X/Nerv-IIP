# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Builds and starts a local Platform Gateway instance for OpenAPI discovery
#     - Fetches the gateway Swagger document
#   Writes:
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json
#   Cleanup:
#     - Stops the Platform Gateway PowerShell job
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-Healthy {
  param([string]$Uri)
  $deadline = (Get-Date).AddSeconds(30)
  do {
    try {
      $result = Invoke-RestMethod -Method Get -Uri $Uri
      if ($result -eq "Healthy") { return }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)
  throw "Service did not become healthy at $Uri"
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$gatewayUrl = "http://127.0.0.1:58204"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$output = Join-Path $root "frontend/packages/api-client/openapi/platform-gateway.v1.json"
$outputDirectory = Split-Path -Parent $output

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$gatewayJob = $null
try {
  dotnet build $gatewayProject

  $gatewayJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project $project --no-build --no-launch-profile --urls $url
  } -ArgumentList $gatewayProject, $gatewayUrl

  Wait-Healthy "$gatewayUrl/health"
  $openApiDocument = Invoke-RestMethod -Method Get -Uri "$gatewayUrl/swagger/v1/swagger.json"
  $openApiDocument.servers = @([pscustomobject]@{ url = "" })
  $openApiJson = ($openApiDocument | ConvertTo-Json -Depth 100) + [Environment]::NewLine
  $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
  [System.IO.File]::WriteAllText($output, $openApiJson, $utf8NoBom)
  Write-Host "Gateway OpenAPI exported to $output"
}
finally {
  if ($gatewayJob) {
    Stop-Job $gatewayJob -ErrorAction SilentlyContinue
    Remove-Job $gatewayJob -Force -ErrorAction SilentlyContinue
  }
}
