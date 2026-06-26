# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Builds and starts local PlatformGateway and BusinessGateway instances for OpenAPI discovery
#     - Fetches gateway Swagger documents over local loopback URLs
#   Writes:
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json
#     - frontend/packages/api-client/openapi/business-gateway-console.v1.json
#     - artifacts/openapi-export/**
#     - artifacts/script-logs/export-gateway-openapi/**
#   Cleanup:
#     - Stops managed gateway process trees
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

. (Join-Path $PSScriptRoot "lib/ScriptAutomation.ps1")

function Wait-Healthy {
  param(
    [Parameter(Mandatory)]
    [string] $Uri,

    [object] $ManagedProcess
  )

  $deadline = (Get-Date).AddSeconds(45)
  do {
    if ($ManagedProcess -and $ManagedProcess.Process.HasExited) {
      throw "Service process exited before becoming healthy at $Uri. Logs: $($ManagedProcess.LogDirectory)"
    }

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

function Get-AvailableLoopbackUrl {
  $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
  try {
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    return "http://127.0.0.1:$port"
  }
  finally {
    $listener.Stop()
  }
}

function Export-GatewayOpenApi {
  param(
    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [string] $ProjectPath,

    [Parameter(Mandatory)]
    [string] $BaseUrl,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [hashtable] $Environment = @{}
  )

  $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
  $buildOutputDirectory = Join-Path $root "artifacts/openapi-export/$Name"
  New-Item -ItemType Directory -Force -Path $buildOutputDirectory | Out-Null

  Invoke-DotNet `
    -Arguments @("build", $ProjectPath, "-o", $buildOutputDirectory, "/p:UseSharedCompilation=false") `
    -WorkingDirectory $root `
    -TimeoutSeconds 600 `
    -Name "export-gateway-openapi-$Name-build" | Out-Null

  $assemblyPath = Join-Path $buildOutputDirectory "$assemblyName.dll"
  if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Built gateway assembly was not found: $assemblyPath"
  }

  $managedProcess = $null
  try {
    $processEnvironment = @{
      "ASPNETCORE_ENVIRONMENT" = "Development"
    }
    foreach ($key in $Environment.Keys) {
      $processEnvironment[$key] = $Environment[$key]
    }

    $managedProcess = Invoke-WithScopedEnvironment -Variables $processEnvironment -ScriptBlock {
      Start-ManagedBackgroundProcess `
        -Command "dotnet" `
        -Arguments @($assemblyPath, "--urls", $BaseUrl) `
        -WorkingDirectory $root `
        -Name "export-gateway-openapi-$Name-run"
    }

    Wait-Healthy "$BaseUrl/health" -ManagedProcess $managedProcess
    $openApiDocument = Invoke-RestMethod -Method Get -Uri "$BaseUrl/swagger/v1/swagger.json"
    $openApiDocument.servers = @([pscustomobject]@{ url = "" })
    $openApiJson = (($openApiDocument | ConvertTo-Json -Depth 100) -replace "`r`n", "`n") + "`n"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($OutputPath, $openApiJson, $utf8NoBom)
    Write-Diagnostic "$Name OpenAPI exported to $OutputPath"
  }
  finally {
    if ($managedProcess) {
      $managedProcess.Stop.Invoke("OpenAPI export completed for $Name")
    }
  }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$openApiDirectory = Join-Path $root "frontend/packages/api-client/openapi"

$gatewayUrl = Get-AvailableLoopbackUrl
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$platformOutput = Join-Path $openApiDirectory "platform-gateway.v1.json"

$businessGatewayUrl = Get-AvailableLoopbackUrl
$businessGatewayProject = Join-Path $root "backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj"
$businessOutput = Join-Path $openApiDirectory "business-gateway-console.v1.json"
$devJwksJson = '{"keys":[{"kty":"RSA","use":"sig","kid":"dev-rsa-2026-01","alg":"RS256","n":"tEYU0967vfBIQVtsmO87GsJUC_9PXED2hplI9VMnrKWW_5UO38OloycNOcVKFDUekblpr6YZ10SpdrkoyM9nENLoi8WYL5__VUCo96Dbd5oo7kanAi5m0FzvnY9a0Ax39TFTsUyBZ2G8alWMOkw1-BYJFtm8-z6j_kTlz93xe3griVcGyXTlNWi09pgvAC8Lj1ON42fovXiLjygnvCA5ZJeviMFZe43kftxjF0-fu0I6By6j-DyiIPGdHAIaSWn3cSl0Il2uBRmkW-aCs9GULHTs0Z3XpXklpQCc5dcn_UsFPGY5gIW-TbqqfBebZCZBROdgSnVrSNnIsdWRgplR9Q","e":"AQAB"}]}'

New-Item -ItemType Directory -Force -Path $openApiDirectory | Out-Null

Export-GatewayOpenApi `
  -Name "platform-gateway" `
  -ProjectPath $gatewayProject `
  -BaseUrl $gatewayUrl `
  -OutputPath $platformOutput

Export-GatewayOpenApi `
  -Name "business-gateway" `
  -ProjectPath $businessGatewayProject `
  -BaseUrl $businessGatewayUrl `
  -OutputPath $businessOutput `
  -Environment @{
    "Iam__Jwt__JwksJson" = $devJwksJson
    "Iam__Jwt__Issuer" = "nerv-iip-iam"
    "Iam__Jwt__Audience" = "nerv-iip-api"
    "Iam__BaseUrl" = "http://127.0.0.1:5102"
    "MasterData__BaseUrl" = "http://127.0.0.1:5107"
    "Inventory__BaseUrl" = "http://127.0.0.1:5109"
    "Quality__BaseUrl" = "http://127.0.0.1:5110"
    "DemandPlanning__BaseUrl" = "http://127.0.0.1:5112"
    "Mes__BaseUrl" = "http://127.0.0.1:5111"
    "Scheduling__BaseUrl" = "http://127.0.0.1:5120"
  }
