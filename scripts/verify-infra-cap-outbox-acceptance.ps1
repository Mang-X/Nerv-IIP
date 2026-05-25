# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs #171 CAP/outbox acceptance tests against a disposable PostgreSQL database
#     - Uses RabbitMQ only when -Profile rabbitmq or -Profile all is selected
#     - Creates and drops per-test PostgreSQL databases through the test suite
#   Writes:
#     - bin/ and obj/ build outputs under the Notification web test project
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables after the test command finishes
#     - Test code drops disposable PostgreSQL databases it creates
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL reachable from -PostgresConnectionString or NERV_IIP_TEST_POSTGRES
#     - RabbitMQ reachable when running rabbitmq/all profile

[CmdletBinding()]
param(
    [string] $PostgresConnectionString,

    [ValidateSet("inmemory", "rabbitmq", "all")]
    [string] $Profile = "inmemory",

    [string] $RabbitMqHost = "localhost",

    [int] $RabbitMqPort = 5672,

    [string] $RabbitMqUserName = "guest",

    [string] $RabbitMqPassword = "guest",

    [switch] $SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$effectivePostgresConnectionString = if (-not [string]::IsNullOrWhiteSpace($PostgresConnectionString)) {
    $PostgresConnectionString
}
else {
    [Environment]::GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES", "Process")
}

if ([string]::IsNullOrWhiteSpace($effectivePostgresConnectionString)) {
    throw "Set -PostgresConnectionString or NERV_IIP_TEST_POSTGRES to run #171 CAP/outbox acceptance against real PostgreSQL."
}

if (($Profile -eq "rabbitmq" -or $Profile -eq "all") -and ([string]::IsNullOrWhiteSpace($RabbitMqHost) -or $RabbitMqPort -le 0)) {
    throw "RabbitMQ profile requires -RabbitMqHost and a positive -RabbitMqPort."
}

$project = "backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj"
$profilesToRun = if ($Profile -eq "all") {
    @("inmemory", "rabbitmq")
}
else {
    @($Profile)
}

$environment = @{
    NERV_IIP_TEST_POSTGRES = $effectivePostgresConnectionString
    NERV_IIP_TEST_RABBITMQ_HOST = $RabbitMqHost
    NERV_IIP_TEST_RABBITMQ_PORT = $RabbitMqPort.ToString()
    NERV_IIP_TEST_RABBITMQ_USERNAME = $RabbitMqUserName
    NERV_IIP_TEST_RABBITMQ_PASSWORD = $RabbitMqPassword
}

Write-Diagnostic "Running #171 CAP/outbox acceptance profile=$Profile."

Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
    foreach ($profileToRun in $profilesToRun) {
        $testArguments = @("test", $project)

        if ($SkipRestore) {
            $testArguments += "--no-restore"
        }

        $categoryFilter = switch ($profileToRun) {
            "inmemory" { "Category=cap-inmemory" }
            "rabbitmq" { "Category=cap-rabbitmq" }
        }

        $testArguments += @(
            "--filter",
            $categoryFilter,
            "--logger",
            "console;verbosity=normal"
        )

        Invoke-DotNet -Name "infra-cap-outbox-acceptance-$profileToRun" -WorkingDirectory $root -Arguments $testArguments -TimeoutSeconds 900 | Out-Null
    }
}

Write-Host "Infra CAP/outbox acceptance #171 verified for profile '$Profile'."
