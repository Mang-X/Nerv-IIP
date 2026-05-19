# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local AppHub and Platform Gateway verification services
#     - Exercises connector registration, heartbeat, state snapshot and console read APIs
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed AppHub and Platform Gateway process trees
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

Invoke-DotNet -Arguments @("restore", "backend/Nerv.IIP.sln") -WorkingDirectory $root -TimeoutSeconds 600 -Name "first-backend-restore" | Out-Null
Invoke-DotNet -Arguments @("build", "backend/Nerv.IIP.sln", "--no-restore") -WorkingDirectory $root -TimeoutSeconds 600 -Name "first-backend-build" | Out-Null
Invoke-DotNet -Arguments @("test", "backend/Nerv.IIP.sln", "--no-build") -WorkingDirectory $root -TimeoutSeconds 900 -Name "first-backend-test" | Out-Null
Invoke-DotNet -Arguments @("restore", "connector-hosts/Nerv.IIP.ConnectorHost.sln") -WorkingDirectory $root -TimeoutSeconds 600 -Name "first-connector-host-restore" | Out-Null
Invoke-DotNet -Arguments @("build", "connector-hosts/Nerv.IIP.ConnectorHost.sln", "--no-restore") -WorkingDirectory $root -TimeoutSeconds 600 -Name "first-connector-host-build" | Out-Null
Invoke-DotNet -Arguments @("test", "connector-hosts/Nerv.IIP.ConnectorHost.sln", "--no-build") -WorkingDirectory $root -TimeoutSeconds 900 -Name "first-connector-host-test" | Out-Null

$appHubUrl = "http://127.0.0.1:58103"
$gatewayUrl = "http://127.0.0.1:58104"
$appHubProject = Join-Path $root "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"

$appHubProcess = $null
$gatewayProcess = $null
try {
  Use-ScopedEnvironmentVariable -Name "ASPNETCORE_URLS" -Value $appHubUrl -ScriptBlock {
    $script:appHubProcess = Start-ManagedBackgroundProcess -Command "dotnet" -Arguments @("run", "--project", $appHubProject, "--no-build", "--no-launch-profile") -WorkingDirectory $root -Name "first-apphub-service"
  }

  Wait-Healthy "$appHubUrl/health"

  Invoke-WithScopedEnvironment -Variables @{
    ASPNETCORE_URLS = $gatewayUrl
    AppHub__BaseUrl = $appHubUrl
  } -ScriptBlock {
    $script:gatewayProcess = Start-ManagedBackgroundProcess -Command "dotnet" -Arguments @("run", "--project", $gatewayProject, "--no-build", "--no-launch-profile") -WorkingDirectory $root -Name "first-platform-gateway-service"
  }

  Wait-Healthy "$gatewayUrl/health"

  $headers = @{
    "X-Connector-Host-Id" = "connector-host-001"
    "X-Connector-Secret" = "local-connector-secret"
    "X-Correlation-Id" = "corr-first-slice"
  }
  $context = @{
    protocolVersion = "1.0"
    sdkVersion = "1.0"
    correlationId = "corr-first-slice"
    occurredAtUtc = "2026-05-14T00:00:00Z"
    organizationId = "org-001"
    environmentId = "env-dev"
    connectorHostId = "connector-host-001"
  }
  $registration = @{
    context = $context
    idempotencyKey = "verify-first-slice-001"
    nodeKey = "node-001"
    nodeName = "local-docker"
    deploymentKind = "docker"
    applicationKey = "demo-api"
    applicationName = "Demo API"
    version = "1.0.0"
    instanceKey = "demo-api-001"
    instanceName = "demo-api"
    capabilities = @(@{ capabilityCode = "lifecycle.restart"; capabilityVersion = "1.0"; category = "lifecycle"; supportedOperations = @("restart"); metadata = @{} })
    metadata = @{ containerId = "abc123" }
  }
  Invoke-RestMethod -Method Post -Uri "$appHubUrl/api/connectors/v1/registrations" -Headers $headers -Body ($registration | ConvertTo-Json -Depth 10) -ContentType "application/json" | Out-Null

  $heartbeat = @{
    context = $context
    instanceKey = "demo-api-001"
    heartbeatAtUtc = "2026-05-14T00:00:05Z"
    reachable = $true
    connectorHostStartedAtUtc = "2026-05-14T00:00:00Z"
    latencyMs = 8
    metadata = @{}
  }
  Invoke-RestMethod -Method Post -Uri "$appHubUrl/api/connectors/v1/heartbeats" -Headers $headers -Body ($heartbeat | ConvertTo-Json -Depth 10) -ContentType "application/json" | Out-Null

  $state = @{
    context = $context
    instanceKey = "demo-api-001"
    observedAtUtc = "2026-05-14T00:00:10Z"
    reportedStatus = "running"
    healthStatus = "healthy"
    summary = "demo-api is running"
    detail = @{}
    metrics = @{}
    metadata = @{ containerId = "abc123" }
  }
  Invoke-RestMethod -Method Post -Uri "$appHubUrl/api/connectors/v1/state-snapshots" -Headers $headers -Body ($state | ConvertTo-Json -Depth 10) -ContentType "application/json" | Out-Null

  $list = Invoke-RestMethod -Method Get -Uri "$gatewayUrl/api/console/v1/instances?organizationId=org-001&environmentId=env-dev&pageNumber=1&pageSize=20"
  if ($list.totalCount -lt 1) { throw "Gateway instance list did not return the registered instance." }

  $detail = Invoke-RestMethod -Method Get -Uri "$gatewayUrl/api/console/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev"
  if ($detail.instanceKey -ne "demo-api-001" -or $detail.reportedStatus -ne "running" -or $detail.healthStatus -ne "healthy") {
    throw "Gateway detail did not return the expected instance state."
  }
  if (-not ($detail.capabilities | Where-Object { $_.capabilityCode -eq "lifecycle.restart" })) {
    throw "Gateway detail did not return expected capabilities."
  }

  Write-Host "First vertical slice verified with correlationId corr-first-slice."
}
finally {
  if ($gatewayProcess) { $gatewayProcess.Stop.Invoke("verify-first-slice cleanup") | Out-Null }
  if ($appHubProcess) { $appHubProcess.Stop.Invoke("verify-first-slice cleanup") | Out-Null }
}
