# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL from infra/docker-compose.dev.yml
#     - Uses disposable IAM verification database nerv_iip_iam_migration_verify
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
$iamTests = Join-Path $root "backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify IAM persistent auth foundation."
}

Invoke-WithScopedEnvironment -Variables @{
  NERV_IIP_POSTGRES_PORT = $postgresPort
} -ScriptBlock {
  Invoke-DockerCompose -Arguments @("-f", $composeFile, "up", "-d", "postgres") -WorkingDirectory $root -TimeoutSeconds 180 -Name "iam-docker-compose-postgres" | Out-Null
  Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)

  Invoke-DotNet -Arguments @("tool", "restore") -WorkingDirectory $root -TimeoutSeconds 300 -Name "iam-dotnet-tool-restore" | Out-Null

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_iam_migration_verify;Username=nerv;Password=nerv"
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $iamTests, "--filter", "FullyQualifiedName~IamPostgresProfileTests|FullyQualifiedName~IamSchemaConventionTests") -WorkingDirectory $root -TimeoutSeconds 600 -Name "iam-postgres-profile-tests" | Out-Null
  }

  Invoke-DotNet -Arguments @("test", "backend/Nerv.IIP.sln") -WorkingDirectory $root -TimeoutSeconds 900 -Name "iam-backend-solution-tests" | Out-Null
}

Write-Host "IAM persistent auth foundation verified."
