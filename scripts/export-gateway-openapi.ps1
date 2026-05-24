# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Builds and starts local PlatformGateway and BusinessGateway instances for OpenAPI discovery
#     - Fetches gateway Swagger documents over local loopback URLs
#   Writes:
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json
#     - frontend/packages/api-client/openapi/business-gateway-console.v1.json
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

  Invoke-DotNet `
    -Arguments @("build", $ProjectPath) `
    -WorkingDirectory $root `
    -TimeoutSeconds 600 `
    -Name "export-gateway-openapi-$Name-build" | Out-Null

  $projectDirectory = Split-Path -Parent $ProjectPath
  $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
  $assemblyPath = Join-Path $projectDirectory "bin/Debug/net10.0/$assemblyName.dll"
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

$gatewayUrl = "http://127.0.0.1:58204"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$platformOutput = Join-Path $openApiDirectory "platform-gateway.v1.json"

$businessGatewayUrl = "http://127.0.0.1:58205"
$businessGatewayProject = Join-Path $root "backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj"
$businessOutput = Join-Path $openApiDirectory "business-gateway-console.v1.json"

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
    "Iam__Jwt__SigningKey" = "nerv-iip-openapi-export-signing-key-local-only-0001"
    "Iam__Jwt__Issuer" = "nerv-iip-iam"
    "Iam__Jwt__Audience" = "nerv-iip-api"
    "Iam__BaseUrl" = "http://127.0.0.1:5102"
    "MasterData__BaseUrl" = "http://127.0.0.1:5107"
    "Inventory__BaseUrl" = "http://127.0.0.1:5109"
    "Quality__BaseUrl" = "http://127.0.0.1:5110"
    "Mes__BaseUrl" = "http://127.0.0.1:5111"
  }
