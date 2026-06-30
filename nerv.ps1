[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Command = 'help',

    [switch] $NoBuild,

    [switch] $InfraOnly,

    [switch] $OpenDashboard,

    [switch] $InstallMissing,

    [switch] $SkipRestore,

    [switch] $SkipLocalSecrets,

    [switch] $Start,

    [switch] $Help,

    [string] $LocalAdminPassword,

    [ValidateSet('healthy', 'up', 'down')]
    [string] $Status = 'healthy',

    [int] $TimeoutSeconds = 120,

    [int] $Tail = 120,

    [switch] $Follow,

    [switch] $IncludeHidden,

    [switch] $All,

    [string] $EnvironmentName = 'Production',

    [string] $OutputPath,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

function Write-NervHelp {
    Write-Host @'
Nerv-IIP development commands

Usage:
  .\nerv.ps1 bootstrap [-InstallMissing] [-Start]
  .\nerv.ps1 dev [-NoBuild] [-InfraOnly] [-OpenDashboard]
  .\nerv.ps1 stop
  .\nerv.ps1 status
  .\nerv.ps1 wait <resource> [-Status healthy|up|down] [-TimeoutSeconds 120]
  .\nerv.ps1 logs [resource] [-Tail 120] [-Follow]
  .\nerv.ps1 describe [resource] [-IncludeHidden]
  .\nerv.ps1 publish-compose [-EnvironmentName Production] [-OutputPath artifacts/aspire-output/compose]
  .\nerv.ps1 prepare-compose [-EnvironmentName Production]
  .\nerv.ps1 deploy-compose [-EnvironmentName Production]
  .\nerv.ps1 ports
  .\nerv.ps1 help

Commands:
  bootstrap        Prepare a connected blank machine for local Aspire startup.
  dev              Start the local platform with Aspire CLI.
  stop             Stop the Aspire AppHost with Aspire CLI.
  status           Show running Aspire AppHosts.
  wait             Wait for an Aspire resource.
  logs             Show Aspire resource logs.
  describe         Describe Aspire resources.
  publish-compose  Generate Aspire Docker Compose artifacts.
  prepare-compose  Generate env-specific Compose artifacts and images through Aspire.
  deploy-compose   Deploy the Aspire Docker Compose target through Aspire.
  ports            Print the canonical local development port matrix.
  help             Print this help.
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
  5106 Notification
  5107 BusinessMasterData
  5108 BusinessProductEngineering
  5109 BusinessInventory
  5110 BusinessQuality
  5111 BusinessMES
  5112 BusinessDemandPlanning
  5113 BusinessBarcodeLabel
  5114 BusinessApproval
  5115 BusinessWMS
  5116 BusinessIndustrialTelemetry
  5117 BusinessMaintenance
  5118 BusinessERP
  5119 BusinessGateway
  5120 BusinessScheduling
  5125 BusinessConsole
  5126 BusinessPDA (dev)
  5128 Screen
  5180 DesignSystem (docs)

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
    'bootstrap' {
        $bootstrapScript = Join-Path $repoRoot 'scripts/bootstrap-online.ps1'
        if (-not (Test-Path -LiteralPath $bootstrapScript -PathType Leaf)) {
            Write-Host "Bootstrap script not found: $bootstrapScript"
            exit 1
        }

        $bootstrapParameters = @{}
        if ($InstallMissing) {
            $bootstrapParameters['InstallMissing'] = $true
        }
        if ($SkipRestore) {
            $bootstrapParameters['SkipRestore'] = $true
        }
        if ($SkipLocalSecrets) {
            $bootstrapParameters['SkipLocalSecrets'] = $true
        }
        if ($Start) {
            $bootstrapParameters['Start'] = $true
        }
        if ($NoBuild) {
            $bootstrapParameters['NoBuild'] = $true
        }
        if ($Help) {
            $bootstrapParameters['Help'] = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($LocalAdminPassword)) {
            $bootstrapParameters['LocalAdminPassword'] = $LocalAdminPassword
        }

        & $bootstrapScript @bootstrapParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'dev' {
        $devScript = Join-Path $repoRoot 'scripts/dev.ps1'
        if (-not (Test-Path -LiteralPath $devScript -PathType Leaf)) {
            Write-Host "Development script not found: $devScript"
            exit 1
        }

        $devParameters = @{}
        if ($NoBuild) {
            $devParameters['NoBuild'] = $true
        }
        if ($InfraOnly) {
            $devParameters['InfraOnly'] = $true
        }
        if ($OpenDashboard) {
            $devParameters['OpenDashboard'] = $true
        }
        if ($Help) {
            $devParameters['Help'] = $true
        }

        & $devScript @devParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'stop' {
        $controlScript = Join-Path $repoRoot 'scripts/aspire-control.ps1'
        $controlParameters = @{ Action = 'stop' }
        if ($All) {
            $controlParameters['All'] = $true
        }

        & $controlScript @controlParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'status' {
        $controlScript = Join-Path $repoRoot 'scripts/aspire-control.ps1'
        & $controlScript -Action status @RemainingArguments
        exit $LASTEXITCODE
    }
    'wait' {
        $controlScript = Join-Path $repoRoot 'scripts/aspire-control.ps1'
        & $controlScript -Action wait -Status $Status -TimeoutSeconds $TimeoutSeconds @RemainingArguments
        exit $LASTEXITCODE
    }
    'logs' {
        $controlScript = Join-Path $repoRoot 'scripts/aspire-control.ps1'
        $controlParameters = @{
            Action = 'logs'
            Tail = $Tail
        }
        if ($Follow) {
            $controlParameters['Follow'] = $true
        }
        if ($IncludeHidden) {
            $controlParameters['IncludeHidden'] = $true
        }

        & $controlScript @controlParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'describe' {
        $controlScript = Join-Path $repoRoot 'scripts/aspire-control.ps1'
        $controlParameters = @{ Action = 'describe' }
        if ($IncludeHidden) {
            $controlParameters['IncludeHidden'] = $true
        }

        & $controlScript @controlParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'publish-compose' {
        $composeScript = Join-Path $repoRoot 'scripts/aspire-compose.ps1'
        $composeParameters = @{
            Mode = 'Publish'
            EnvironmentName = $EnvironmentName
        }
        if ($NoBuild) {
            $composeParameters['NoBuild'] = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $composeParameters['OutputPath'] = $OutputPath
        }

        & $composeScript @composeParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'prepare-compose' {
        $composeScript = Join-Path $repoRoot 'scripts/aspire-compose.ps1'
        $composeParameters = @{
            Mode = 'Prepare'
            EnvironmentName = $EnvironmentName
        }
        if ($NoBuild) {
            $composeParameters['NoBuild'] = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $composeParameters['OutputPath'] = $OutputPath
        }

        & $composeScript @composeParameters @RemainingArguments
        exit $LASTEXITCODE
    }
    'deploy-compose' {
        $composeScript = Join-Path $repoRoot 'scripts/aspire-compose.ps1'
        $composeParameters = @{
            Mode = 'Deploy'
            EnvironmentName = $EnvironmentName
        }
        if ($NoBuild) {
            $composeParameters['NoBuild'] = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            $composeParameters['OutputPath'] = $OutputPath
        }

        & $composeScript @composeParameters @RemainingArguments
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
