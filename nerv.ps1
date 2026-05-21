[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Command = 'help',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

function Write-NervHelp {
    Write-Host @'
Nerv-IIP development commands

Usage:
  .\nerv.ps1 dev [-NoBuild] [-InfraOnly] [-OpenDashboard]
  .\nerv.ps1 ports
  .\nerv.ps1 help

Commands:
  dev      Start the local platform through the governed development script.
  ports    Print the canonical local development port matrix.
  help     Print this help.
'@
}

function Write-NervPorts {
    Write-Host @'
Platform services:
  5100 PlatformGateway
  5101 AppHub
  5102 IAM
  5103 Ops
  5104 FileStorage
  5105 Console

Infrastructure services:
  15432 PostgreSQL
  6379 Redis
  5672 RabbitMQ AMQP
  15672 RabbitMQ Management
  9000 MinIO API
  9001 MinIO Console
  4317 OTLP gRPC
  4318 OTLP HTTP
'@
}

switch ($Command.ToLowerInvariant()) {
    'dev' {
        $devScript = Join-Path $repoRoot 'scripts/dev.ps1'
        & $devScript @RemainingArguments
        exit $LASTEXITCODE
    }
    'ports' {
        Write-NervPorts
        exit 0
    }
    'help' {
        Write-NervHelp
        exit 0
    }
    default {
        Write-Host "Unknown command '$Command'."
        Write-NervHelp
        exit 1
    }
}
