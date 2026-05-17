Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Invoke-NativeCommand {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
  )

  $global:LASTEXITCODE = 0
  & $FilePath @Arguments
  if ($global:LASTEXITCODE -ne 0) {
    throw "Native command '$FilePath' failed with exit code $global:LASTEXITCODE."
  }
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

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$env:NERV_IIP_POSTGRES_PORT = $postgresPort
$iamTests = Join-Path $root "backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify IAM persistent auth foundation."
}

Invoke-NativeCommand docker compose -f $composeFile up -d postgres
Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)

Invoke-NativeCommand dotnet tool restore

$previous = $env:NERV_IIP_TEST_POSTGRES
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_iam_migration_verify;Username=nerv;Password=nerv"
try {
  Invoke-NativeCommand dotnet test $iamTests --filter "FullyQualifiedName~IamPostgresProfileTests|FullyQualifiedName~IamSchemaConventionTests"
}
finally {
  $env:NERV_IIP_TEST_POSTGRES = $previous
}

Invoke-NativeCommand dotnet test backend/Nerv.IIP.sln

Write-Host "IAM persistent auth foundation verified."
