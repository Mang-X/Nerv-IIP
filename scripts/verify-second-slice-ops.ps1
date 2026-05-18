param(
  [switch]$UsePostgres
)

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
    [string]$OperationTaskId,
    [hashtable]$Headers
  )

  $taskUri = "$GatewayUrl/api/console/v1/operation-tasks/$([Uri]::EscapeDataString($OperationTaskId))?organizationId=org-001&environmentId=env-dev"
  $deadline = (Get-Date).AddSeconds(30)
  do {
    $task = Invoke-RestMethod -Method Get -Uri $taskUri -Headers $Headers
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
dotnet test (Join-Path $root "backend/Nerv.IIP.sln") --no-build -m:1
dotnet restore (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln")
dotnet build (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln") --no-restore
dotnet test (Join-Path $root "connector-hosts/Nerv.IIP.ConnectorHost.sln") --no-build -m:1

$appHubUrl = "http://127.0.0.1:58103"
$gatewayUrl = "http://127.0.0.1:58104"
$opsUrl = "http://127.0.0.1:58105"
$iamUrl = "http://127.0.0.1:58106"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubPostgresConnectionString = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_APPHUB_POSTGRES)) {
  "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub;Username=nerv;Password=nerv"
} else {
  $env:NERV_IIP_APPHUB_POSTGRES
}
$opsPostgresConnectionString = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_OPS_POSTGRES)) {
  "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops;Username=nerv;Password=nerv"
} else {
  $env:NERV_IIP_OPS_POSTGRES
}
$iamPostgresConnectionString = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_IAM_POSTGRES)) {
  "Host=localhost;Port=$postgresPort;Database=nerv_iip_iam;Username=nerv;Password=nerv"
} else {
  $env:NERV_IIP_IAM_POSTGRES
}

$appHubProject = Join-Path $root "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj"
$iamProject = Join-Path $root "backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$opsProject = Join-Path $root "backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj"
$connectorHostProject = Join-Path $root "connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj"

$jobs = @()
try {
  $appHubJob = Start-Job -Name "AppHub" -ScriptBlock {
    param($project, $url, $usePostgres, $connectionString)
    $env:ASPNETCORE_URLS = $url
    if ($usePostgres) {
      $env:Persistence__Provider = "PostgreSQL"
      $env:Persistence__AutoMigrate = "true"
      $env:ConnectionStrings__AppHubDb = $connectionString
      $env:RabbitMQ__HostName = "localhost"
      $env:RabbitMQ__Port = "5672"
      $env:RabbitMQ__UserName = "guest"
      $env:RabbitMQ__Password = "guest"
    }
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $appHubProject, $appHubUrl, $UsePostgres.IsPresent, $appHubPostgresConnectionString
  $jobs += $appHubJob
  Wait-Healthy $appHubUrl

  $opsJob = Start-Job -Name "Ops" -ScriptBlock {
    param($project, $url, $usePostgres, $connectionString)
    $env:ASPNETCORE_URLS = $url
    if ($usePostgres) {
      $env:Persistence__Provider = "PostgreSQL"
      $env:Persistence__AutoMigrate = "true"
      $env:ConnectionStrings__OpsDb = $connectionString
      $env:RabbitMQ__HostName = "localhost"
      $env:RabbitMQ__Port = "5672"
      $env:RabbitMQ__UserName = "guest"
      $env:RabbitMQ__Password = "guest"
    }
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $opsProject, $opsUrl, $UsePostgres.IsPresent, $opsPostgresConnectionString
  $jobs += $opsJob
  Wait-Healthy $opsUrl

  $iamJob = Start-Job -Name "IAM" -ScriptBlock {
    param($project, $url, $usePostgres, $connectionString)
    $env:ASPNETCORE_URLS = $url
    if ($usePostgres) {
      $env:ASPNETCORE_ENVIRONMENT = "Development"
      $env:Persistence__Provider = "PostgreSQL"
      $env:Persistence__AutoMigrate = "true"
      $env:ConnectionStrings__IamDb = $connectionString
      $env:Iam__Seed__Enabled = "true"
      $env:Iam__Seed__AdminPassword = "Admin123!"
      $env:Iam__Seed__ConnectorHostSecret = "local-connector-secret"
      $env:Iam__Jwt__SigningKey = "verify-signing-key-that-is-long-enough-for-local-tests"
      $env:RabbitMQ__HostName = "localhost"
      $env:RabbitMQ__Port = "5672"
      $env:RabbitMQ__UserName = "guest"
      $env:RabbitMQ__Password = "guest"
    }
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $iamProject, $iamUrl, $UsePostgres.IsPresent, $iamPostgresConnectionString
  $jobs += $iamJob
  Wait-Healthy $iamUrl

  $gatewayJob = Start-Job -Name "Gateway" -ScriptBlock {
    param($project, $url, $appHub, $ops, $iam)
    $env:ASPNETCORE_URLS = $url
    $env:AppHub__BaseUrl = $appHub
    $env:Ops__BaseUrl = $ops
    $env:Iam__BaseUrl = $iam
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl, $appHubUrl, $opsUrl, $iamUrl
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

  $loginBody = @{
    loginName = "admin"
    password = "Admin123!"
  }
  $auth = Invoke-RestMethod `
    -Method Post `
    -Uri "$iamUrl/api/iam/v1/auth/login" `
    -Body ($loginBody | ConvertTo-Json -Depth 10) `
    -ContentType "application/json"
  $gatewayHeaders = @{
    Authorization = "Bearer $($auth.accessToken)"
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
    -Headers $gatewayHeaders `
    -Body ($restartBody | ConvertTo-Json -Depth 10) `
    -ContentType "application/json"

  if (-not $created.operationTaskId) {
    throw "Gateway restart response did not include an operationTaskId."
  }

  $completed = Wait-TaskCompleted $gatewayUrl $created.operationTaskId $gatewayHeaders
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
