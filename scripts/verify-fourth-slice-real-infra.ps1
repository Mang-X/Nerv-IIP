Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 60
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

function Reset-PostgresDatabase {
  param(
    [string]$ComposeFile,
    [string]$DatabaseName
  )

  docker compose -f $ComposeFile exec -T postgres psql -U nerv -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $DatabaseName WITH (FORCE);"
  docker compose -f $ComposeFile exec -T postgres psql -U nerv -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $DatabaseName OWNER nerv;"
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
$opsTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
$appHubVerifyDatabase = "nerv_iip_apphub_verify"
$opsVerifyDatabase = "nerv_iip_ops_verify"
$appHubVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$appHubVerifyDatabase;Username=nerv;Password=nerv"
$opsVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$opsVerifyDatabase;Username=nerv;Password=nerv"
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify fourth slice real infrastructure."
}

$env:NERV_IIP_POSTGRES_PORT = $postgresPort
docker compose -f $composeFile up -d postgres redis rabbitmq

Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort) -TimeoutSeconds 90
Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $appHubVerifyDatabase
Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $opsVerifyDatabase

Invoke-PostgresProfileTest `
  -Project $appHubTests `
  -Filter "FullyQualifiedName~AppHubPostgresProfileTests" `
  -ConnectionString $appHubTestConnectionString

Invoke-PostgresProfileTest `
  -Project $opsTests `
  -Filter "FullyQualifiedName~OpsPostgresProfileTests" `
  -ConnectionString $opsTestConnectionString

$previousAppHubPostgres = $env:NERV_IIP_APPHUB_POSTGRES
$previousOpsPostgres = $env:NERV_IIP_OPS_POSTGRES
$env:NERV_IIP_APPHUB_POSTGRES = $appHubVerifyConnectionString
$env:NERV_IIP_OPS_POSTGRES = $opsVerifyConnectionString
try {
  pwsh scripts/verify-third-slice-console.ps1 -UsePostgres
}
finally {
  $env:NERV_IIP_APPHUB_POSTGRES = $previousAppHubPostgres
  $env:NERV_IIP_OPS_POSTGRES = $previousOpsPostgres
}

Write-Host "Fourth vertical slice real infrastructure verified."
