# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL, Redis and RabbitMQ from infra/docker-compose.dev.yml
#     - Recreates disposable AppHub, IAM and Ops verification databases
#     - Runs the third-stage console verification under PostgreSQL profile
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/node_modules/** through the nested third-stage pnpm install
#     - frontend/**/.nuxt/**, frontend/**/.output/**, frontend/**/dist/** and frontend/**/coverage/** through nested frontend typecheck/test/build steps
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json through the nested third-stage verification
#     - frontend/packages/api-client/src/** through the nested third-stage verification
#   Cleanup:
#     - Restores scoped environment variables
#     - Stops managed nested script process if it times out through ScriptAutomation.ps1
#     - Leaves shared Docker development services running
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop
#     - Node.js 22.22.3
#     - pnpm 11.1.2

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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
    [string]$ConnectionString,
    [string]$Name
  )

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = $ConnectionString
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $Project, "--filter", $Filter) -WorkingDirectory $root -TimeoutSeconds 600 -Name $Name | Out-Null
  }
}

function Reset-PostgresDatabase {
  param(
    [string]$ComposeFile,
    [string]$DatabaseName,
    [string]$Name
  )

  Invoke-DockerCompose -Arguments @("-f", $ComposeFile, "exec", "-T", "postgres", "psql", "-U", "nerv", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", "DROP DATABASE IF EXISTS $DatabaseName WITH (FORCE);") -WorkingDirectory $root -TimeoutSeconds 120 -Name "$Name-drop" | Out-Null
  Invoke-DockerCompose -Arguments @("-f", $ComposeFile, "exec", "-T", "postgres", "psql", "-U", "nerv", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", "CREATE DATABASE $DatabaseName OWNER nerv;") -WorkingDirectory $root -TimeoutSeconds 120 -Name "$Name-create" | Out-Null
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
$opsTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
$appHubVerifyDatabase = "nerv_iip_apphub_verify"
$iamVerifyDatabase = "nerv_iip_iam_verify"
$opsVerifyDatabase = "nerv_iip_ops_verify"
$appHubVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$appHubVerifyDatabase;Username=nerv;Password=nerv"
$iamVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$iamVerifyDatabase;Username=nerv;Password=nerv"
$opsVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$opsVerifyDatabase;Username=nerv;Password=nerv"
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"
$thirdStageScript = Join-Path $root "scripts/verify-third-slice-console.ps1"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify fourth slice real infrastructure."
}

Invoke-WithScopedEnvironment -Variables @{
  NERV_IIP_POSTGRES_PORT = $postgresPort
} -ScriptBlock {
  Invoke-DockerCompose -Arguments @("-f", $composeFile, "up", "-d", "postgres", "redis", "rabbitmq") -WorkingDirectory $root -TimeoutSeconds 240 -Name "fourth-docker-compose-dependencies" | Out-Null

  Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort) -TimeoutSeconds 90
  Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
  Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

  Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $appHubVerifyDatabase -Name "fourth-apphub-verify-database"
  Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $iamVerifyDatabase -Name "fourth-iam-verify-database"
  Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $opsVerifyDatabase -Name "fourth-ops-verify-database"

  Invoke-PostgresProfileTest -Project $appHubTests -Filter "FullyQualifiedName~AppHubPostgresProfileTests" -ConnectionString $appHubTestConnectionString -Name "fourth-apphub-postgres-profile-tests"
  Invoke-PostgresProfileTest -Project $opsTests -Filter "FullyQualifiedName~OpsPostgresProfileTests" -ConnectionString $opsTestConnectionString -Name "fourth-ops-postgres-profile-tests"

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_APPHUB_POSTGRES = $appHubVerifyConnectionString
    NERV_IIP_IAM_POSTGRES = $iamVerifyConnectionString
    NERV_IIP_OPS_POSTGRES = $opsVerifyConnectionString
  } -ScriptBlock {
    Invoke-PwshScript -ScriptPath $thirdStageScript -Arguments @("-UsePostgres") -WorkingDirectory $root -TimeoutSeconds 1200 -Name "fourth-third-stage-console-postgres" | Out-Null
  }
}

Write-Host "Fourth vertical slice real infrastructure verified."
