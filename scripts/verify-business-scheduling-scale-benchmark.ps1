# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts the shared local PostgreSQL service from infra/docker-compose.dev.yml
#     - Applies BusinessScheduling migrations and writes run-scoped benchmark rows that the test removes
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - artifacts/script-logs/business-scheduling-scale-benchmark/**
#   Cleanup:
#     - Restores scoped environment variables
#     - Removes benchmark rows after every repetition
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

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI is required to run the BusinessScheduling scale benchmark."
}

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) {
    15432
}
else {
    [int] $env:NERV_IIP_POSTGRES_PORT
}
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$evidenceDirectory = Join-Path $root "artifacts/script-logs/business-scheduling-scale-benchmark/$runId"
$jsonPath = Join-Path $evidenceDirectory "aps-lite-scale-benchmark.json"
$markdownPath = Join-Path $evidenceDirectory "aps-lite-scale-benchmark.md"
$project = "backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj"
$commitResult = Invoke-NativeCommandOutput `
    -Command "git" `
    -Arguments @("rev-parse", "HEAD") `
    -WorkingDirectory $root `
    -Name "business-scheduling-scale-git-head"
$commit = $commitResult.Stdout.Trim()

Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_POSTGRES_PORT = "$postgresPort"
} -ScriptBlock {
    Invoke-DockerCompose `
        -Arguments @("-f", $composeFile, "up", "-d", "postgres") `
        -WorkingDirectory $root `
        -TimeoutSeconds 240 `
        -Name "business-scheduling-scale-postgres" | Out-Null
    Wait-TcpPort -HostName "localhost" -Port $postgresPort

    if (-not $SkipRestore) {
        Invoke-DotNet `
            -Arguments @("restore", $project) `
            -WorkingDirectory $root `
            -TimeoutSeconds 300 `
            -Name "business-scheduling-scale-restore" | Out-Null
    }

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_PERF_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip;Username=nerv;Password=nerv"
        NERV_IIP_PERF_SCENARIO = "scheduling"
        NERV_IIP_PERF_PROFILE = "windows-docker-postgresql-18"
        NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY = $evidenceDirectory
        NERV_IIP_APS_SCALE_COMMIT = $commit
    } -ScriptBlock {
        $arguments = @(
            "test",
            $project,
            "--filter",
            "FullyQualifiedName~SchedulingScale_APS_Lite",
            "--logger",
            "console;verbosity=minimal"
        )
        if ($SkipRestore) {
            $arguments += "--no-restore"
        }

        Invoke-DotNet `
            -Arguments $arguments `
            -WorkingDirectory $root `
            -TimeoutSeconds 600 `
            -Name "business-scheduling-scale-benchmark" | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $jsonPath)) {
    throw "APS Lite scale benchmark JSON evidence was not written: $jsonPath"
}
if (-not (Test-Path -LiteralPath $markdownPath)) {
    throw "APS Lite scale benchmark Markdown evidence was not written: $markdownPath"
}

$evidence = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
$profileNames = @($evidence.profiles | ForEach-Object { $_.profile })
if (($profileNames -join ",") -ne "demo,medium,stress") {
    throw "APS Lite scale benchmark profiles must be exactly demo,medium,stress; actual: $($profileNames -join ',')."
}
foreach ($profile in $evidence.profiles) {
    if (-not $profile.stable) {
        throw "APS Lite scale benchmark profile '$($profile.profile)' is not stable."
    }
    if (@($profile.runs).Count -ne 3) {
        throw "APS Lite scale benchmark profile '$($profile.profile)' must contain exactly three runs."
    }
}
if ($evidence.disclaimer -ne "APS Lite deterministic finite-capacity heuristic; no global optimality claim.") {
    throw "APS Lite scale benchmark capability disclaimer is missing or changed."
}
if ($evidence.persistenceProvider -ne "PostgreSQL") {
    throw "APS Lite scale benchmark must use PostgreSQL persistence."
}

Write-Host "BusinessScheduling APS Lite scale benchmark verified."
Write-Host "JSON evidence: $jsonPath"
Write-Host "Markdown evidence: $markdownPath"
