Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 90
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
      $connectTask = $client.ConnectAsync($HostName, $Port)
      if ($connectTask.Wait(1000) -and $client.Connected) {
        return
      }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
    finally {
      $client.Dispose()
    }

    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)

  throw "TCP port $HostName`:$Port did not become available within $TimeoutSeconds seconds."
}

function Invoke-PostgresProfileTest {
  param(
    [string]$Project,
    [string]$Filter,
    [string]$ConnectionString
  )

  $previous = $env:NERV_IIP_TEST_POSTGRES
  $env:NERV_IIP_TEST_POSTGRES = $ConnectionString
  try {
    dotnet test $Project --filter $Filter
  }
  finally {
    $env:NERV_IIP_TEST_POSTGRES = $previous
  }
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$env:NERV_IIP_POSTGRES_PORT = $postgresPort
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify release-grade persistence foundation."
}

docker compose -f $composeFile up -d postgres redis rabbitmq
Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)
Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

dotnet tool restore

Invoke-PostgresProfileTest `
  -Project $appHubTests `
  -Filter "FullyQualifiedName~AppHubPostgresProfileTests" `
  -ConnectionString "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_migration_verify;Username=nerv;Password=nerv"

Invoke-PostgresProfileTest `
  -Project $opsTests `
  -Filter "FullyQualifiedName~OpsPostgresProfileTests" `
  -ConnectionString "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_migration_verify;Username=nerv;Password=nerv"

dotnet test backend/Nerv.IIP.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln

Write-Host "Fifth slice release-grade persistence foundation verified."
