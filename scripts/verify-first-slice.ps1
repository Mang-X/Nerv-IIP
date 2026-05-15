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
Set-Location $root

dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet test backend/Nerv.IIP.sln --no-build
dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --no-build

$appHubUrl = "http://127.0.0.1:58103"
$gatewayUrl = "http://127.0.0.1:58104"
$appHubProject = Join-Path $root "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"

$appHubJob = $null
$gatewayJob = $null
try {
  $appHubJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $appHubProject, $appHubUrl

  Wait-Healthy "$appHubUrl/health"

  $gatewayJob = Start-Job -ScriptBlock {
    param($project, $url, $appHub)
    $env:ASPNETCORE_URLS = $url
    $env:AppHub__BaseUrl = $appHub
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl, $appHubUrl

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
  if ($gatewayJob) { Stop-Job $gatewayJob -ErrorAction SilentlyContinue; Remove-Job $gatewayJob -Force -ErrorAction SilentlyContinue }
  if ($appHubJob) { Stop-Job $appHubJob -ErrorAction SilentlyContinue; Remove-Job $appHubJob -Force -ErrorAction SilentlyContinue }
}
