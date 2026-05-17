# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL, Redis and RabbitMQ from infra/docker-compose.dev.yml
#     - Uses disposable AppHub and Ops migration verification databases
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables
#     - Leaves shared Docker development services running
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify release-grade persistence foundation."
}

Invoke-WithScopedEnvironment -Variables @{
  NERV_IIP_POSTGRES_PORT = $postgresPort
} -ScriptBlock {
  Invoke-DockerCompose -Arguments @("-f", $composeFile, "up", "-d", "postgres", "redis", "rabbitmq") -WorkingDirectory $root -TimeoutSeconds 240 -Name "fifth-docker-compose-dependencies" | Out-Null
  Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)
  Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
  Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

  Invoke-DotNet -Arguments @("tool", "restore") -WorkingDirectory $root -TimeoutSeconds 300 -Name "fifth-dotnet-tool-restore" | Out-Null

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_migration_verify;Username=nerv;Password=nerv"
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $appHubTests, "--filter", "FullyQualifiedName~AppHubPostgresProfileTests") -WorkingDirectory $root -TimeoutSeconds 600 -Name "fifth-apphub-postgres-profile-tests" | Out-Null
  }

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_migration_verify;Username=nerv;Password=nerv"
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $opsTests, "--filter", "FullyQualifiedName~OpsPostgresProfileTests") -WorkingDirectory $root -TimeoutSeconds 600 -Name "fifth-ops-postgres-profile-tests" | Out-Null
  }

  Invoke-DotNet -Arguments @("test", "backend/Nerv.IIP.sln") -WorkingDirectory $root -TimeoutSeconds 900 -Name "fifth-backend-solution-tests" | Out-Null
  Invoke-DotNet -Arguments @("test", "connector-hosts/Nerv.IIP.ConnectorHost.sln") -WorkingDirectory $root -TimeoutSeconds 900 -Name "fifth-connector-host-solution-tests" | Out-Null
}

Write-Host "Fifth slice release-grade persistence foundation verified."
