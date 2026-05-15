Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-Healthy {
  param([string]$Uri)

  $healthUri = if ($Uri.EndsWith("/health", [StringComparison]::OrdinalIgnoreCase)) { $Uri } else { "$Uri/health" }
  $deadline = (Get-Date).AddSeconds(30)
  do {
    try {
      $result = Invoke-RestMethod -Method Get -Uri $healthUri
      if ($result -eq "Healthy") { return }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)

  throw "Service did not become healthy at $healthUri"
}

function Wait-TaskCompleted {
  param(
    [string]$GatewayUrl,
    [string]$OperationTaskId
  )

  $taskUri = "$GatewayUrl/api/console/v1/operation-tasks/$([Uri]::EscapeDataString($OperationTaskId))"
  $deadline = (Get-Date).AddSeconds(30)
  do {
    $task = Invoke-RestMethod -Method Get -Uri $taskUri
    if ($task.status -eq "completed") { return $task }
    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)

  throw "Operation task $OperationTaskId did not complete within 30 seconds."
}

function Write-JobDiagnostics {
  param([object[]]$Jobs)

  foreach ($job in $Jobs) {
    if ($null -eq $job) { continue }
    Write-Host ""
    Write-Host "Diagnostics for job '$($job.Name)' (state: $($job.State)):"
    Receive-Job $job -Keep -ErrorAction SilentlyContinue | Select-Object -Last 80 | ForEach-Object { Write-Host $_ }
  }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

dotnet restore (Join-Path $root "backend/Nerv.IIP.sln")
dotnet build (Join-Path $root "backend/Nerv.IIP.sln") --no-restore
dotnet test (Join-Path $root "backend/Nerv.IIP.sln") --no-build
dotnet restore (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln")
dotnet build (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln") --no-restore
dotnet test (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln") --no-build

$appHubUrl = "http://127.0.0.1:58103"
$gatewayUrl = "http://127.0.0.1:58104"
$opsUrl = "http://127.0.0.1:58105"

$appHubProject = Join-Path $root "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$opsProject = Join-Path $root "backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj"
$connectorHostProject = Join-Path $root "connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj"

$jobs = @()
try {
  $appHubJob = Start-Job -Name "AppHub" -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $appHubProject, $appHubUrl
  $jobs += $appHubJob
  Wait-Healthy $appHubUrl

  $opsJob = Start-Job -Name "Ops" -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $opsProject, $opsUrl
  $jobs += $opsJob
  Wait-Healthy $opsUrl

  $gatewayJob = Start-Job -Name "Gateway" -ScriptBlock {
    param($project, $url, $appHub, $ops)
    $env:ASPNETCORE_URLS = $url
    $env:AppHub__BaseUrl = $appHub
    $env:Ops__BaseUrl = $ops
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl, $appHubUrl, $opsUrl
  $jobs += $gatewayJob
  Wait-Healthy $gatewayUrl

  $connectorHostJob = Start-Job -Name "Connector Host" -ScriptBlock {
    param($project, $appHub, $ops)
    $env:Platform__AppHubBaseUrl = $appHub
    $env:Platform__OpsBaseUrl = $ops
    $env:ConnectorHost__CycleSeconds = "1"
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $connectorHostProject, $appHubUrl, $opsUrl
  $jobs += $connectorHostJob

  Start-Sleep -Seconds 4
  if ($connectorHostJob.State -ne "Running") {
    Write-JobDiagnostics $jobs
    throw "Connector Host job exited before verification could run."
  }

  $restartBody = @{
    organizationId = "org-001"
    environmentId = "env-dev"
    reason = "verify second slice restart"
    idempotencyKey = "verify-second-slice-restart-001"
  }
  $created = Invoke-RestMethod `
    -Method Post `
    -Uri "$gatewayUrl/api/console/v1/instances/docker-container-local-demo-001/operations/restart" `
    -Body ($restartBody | ConvertTo-Json -Depth 10) `
    -ContentType "application/json"

  if (-not $created.operationTaskId) {
    throw "Gateway restart response did not include an operationTaskId."
  }

  $completed = Wait-TaskCompleted $gatewayUrl $created.operationTaskId
  if ($completed.status -ne "completed") {
    throw "Expected operation task to be completed, got '$($completed.status)'."
  }

  $auditActions = @($completed.auditRecords | ForEach-Object { $_.action })
  if ($auditActions -notcontains "operation.requested") {
    throw "Completed operation task did not include operation.requested audit record."
  }
  if ($auditActions -notcontains "operation.completed") {
    throw "Completed operation task did not include operation.completed audit record."
  }

  Write-Host "Second vertical slice verified with operationTaskId $($completed.operationTaskId)."
}
catch {
  Write-JobDiagnostics $jobs
  throw
}
finally {
  foreach ($job in $jobs) {
    if ($null -eq $job) { continue }
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
  }
}
