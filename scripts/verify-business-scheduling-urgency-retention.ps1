# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts the shared local PostgreSQL service from infra/docker-compose.dev.yml
#     - Applies BusinessScheduling migrations in a disposable test database
#     - Seeds and removes 10,002 representative urgency snapshots in that disposable database
#   Writes:
#     - bin/ and obj/ build outputs under the tested .NET projects
#     - artifacts/script-logs/business-scheduling-urgency-retention/**
#   Cleanup:
#     - Restores scoped environment variables
#     - Drops the run-scoped PostgreSQL test database
#     - Leaves the shared Docker development PostgreSQL service running
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop

[CmdletBinding()]
param(
    [switch] $SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Wait-TcpPort {
    param(
        [Parameter(Mandatory)] [string] $HostName,
        [Parameter(Mandatory)] [int] $Port,
        [int] $TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $connectTask = $client.ConnectAsync($HostName, $Port)
            if ($connectTask.Wait(1000) -and $client.Connected) { return }
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

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI is required to run the BusinessScheduling urgency retention verification."
}

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { 15432 } else { [int] $env:NERV_IIP_POSTGRES_PORT }
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$evidenceDirectory = Join-Path $root "artifacts/script-logs/business-scheduling-urgency-retention/$runId"
$evidencePath = Join-Path $evidenceDirectory "order-urgency-retention-capacity.json"
$project = "backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj"

Invoke-WithScopedEnvironment -Variables @{ NERV_IIP_POSTGRES_PORT = "$postgresPort" } -ScriptBlock {
    Invoke-DockerCompose `
        -Arguments @("-f", $composeFile, "up", "-d", "postgres") `
        -WorkingDirectory $root `
        -TimeoutSeconds 240 `
        -Name "business-scheduling-urgency-retention-postgres" | Out-Null
    Wait-TcpPort -HostName "localhost" -Port $postgresPort

    if (-not $SkipRestore) {
        Invoke-DotNet `
            -Arguments @("restore", $project) `
            -WorkingDirectory $root `
            -TimeoutSeconds 300 `
            -Name "business-scheduling-urgency-retention-restore" | Out-Null
    }

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip;Username=nerv;Password=nerv"
        NERV_IIP_URGENCY_RETENTION_EVIDENCE = $evidencePath
    } -ScriptBlock {
        $arguments = @(
            "test",
            $project,
            "--filter",
            "FullyQualifiedName~OrderUrgencyRetentionPostgresCapacityTests",
            "--logger",
            "console;verbosity=minimal"
        )
        if ($SkipRestore) { $arguments += "--no-restore" }

        Invoke-DotNet `
            -Arguments $arguments `
            -WorkingDirectory $root `
            -TimeoutSeconds 600 `
            -Name "business-scheduling-urgency-retention-capacity" | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $evidencePath)) {
    throw "Order urgency retention capacity evidence was not written: $evidencePath"
}
$evidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
if ($evidence.provider -ne "PostgreSQL" -or $evidence.seededSnapshots -ne 10002) {
    throw "Order urgency retention capacity evidence does not match the governed PostgreSQL profile."
}
if ($evidence.overlappingWorkerLeaseAcquired -ne $false -or $evidence.latestSnapshotsRemaining -ne 5001) {
    throw "Order urgency retention concurrency or latest-snapshot safety evidence is invalid."
}

Write-Host "BusinessScheduling urgency retention verified."
Write-Host "Capacity evidence: $evidencePath"
