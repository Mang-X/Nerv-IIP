# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL and Redis compose services when they are not already running
#     - Builds and starts MasterData, ERP, and DemandPlanning as separate managed processes
#     - Creates a disposable PostgreSQL database and publishes real Redis CAP integration events
#   Writes:
#     - bin/ and obj/ outputs for the three business services and full-chain probe
#     - artifacts/script-logs/**
#     - artifacts/acceptance/man517/sales-order-demand-planning-evidence.json
#   Cleanup:
#     - Stops every managed service process in finally
#     - Drops the disposable PostgreSQL database in finally
#     - Stops only compose services started by this script
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker with local postgres:18 and redis:8 images
#     - NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS environment variables

param(
    [string]$PostgresAdminConnectionString = $env:NERV_IIP_TEST_POSTGRES,
    [string]$RedisConnectionString = $env:NERV_IIP_TEST_REDIS,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

if ([string]::IsNullOrWhiteSpace($PostgresAdminConnectionString) -or [string]::IsNullOrWhiteSpace($RedisConnectionString)) {
    throw 'Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS; credentials are never embedded in this verification script.'
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Wait-Healthy {
    param([string]$Uri, [object]$ManagedProcess)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        if ($ManagedProcess.Process.HasExited) {
            throw "Managed service exited before becoming healthy. Logs: $($ManagedProcess.LogDirectory)"
        }
        try {
            if ((Invoke-RestMethod -Method Get -Uri $Uri) -eq 'Healthy') { return }
        }
        catch { Start-Sleep -Milliseconds 500 }
    } while ((Get-Date) -lt $deadline)
    throw "Service did not become healthy at $Uri. Logs: $($ManagedProcess.LogDirectory)"
}

function Invoke-JsonPost {
    param([string]$Uri, [hashtable]$Body, [hashtable]$Headers)
    Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 12)
}

function Invoke-JsonPostEventually {
    param([string]$Uri, [hashtable]$Body, [hashtable]$Headers)
    $deadline = (Get-Date).AddSeconds(45)
    do {
        try { return Invoke-JsonPost -Uri $Uri -Body $Body -Headers $Headers }
        catch {
            if ((Get-Date) -ge $deadline) { throw }
            Start-Sleep -Milliseconds 500
        }
    } while ($true)
}

function Wait-Demand {
    param([string]$DemandPlanningUrl, [hashtable]$Headers, [int]$Version, [decimal]$Quantity, [string]$Status)
    $deadline = (Get-Date).AddSeconds(45)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$DemandPlanningUrl/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev" -Headers $Headers
            $rows = @($response.data | Where-Object { $_.sourceReference -eq 'SO-DEMO-001' })
            if ($rows.Count -eq 1 -and $rows[0].sourceVersion -eq $Version -and [decimal]$rows[0].quantity -eq $Quantity -and $rows[0].sourceStatus -eq $Status) {
                return $rows[0]
            }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "Demand SO-DEMO-001 did not converge to version=$Version quantity=$Quantity status=$Status."
}

$composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
$running = @(Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man517-compose-running')
$startedPostgres = $running -notcontains 'postgres'
$startedRedis = $running -notcontains 'redis'
$databaseName = "man517_$([Guid]::NewGuid().ToString('N'))"
$databaseConnectionString = if ($PostgresAdminConnectionString -match '(?i)Database=[^;]*') {
    $PostgresAdminConnectionString -replace '(?i)Database=[^;]*', "Database=$databaseName"
} else {
    "$($PostgresAdminConnectionString.TrimEnd(';'));Database=$databaseName"
}
$capVersion = "man517-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$internalToken = "man517-$([Guid]::NewGuid().ToString('N'))"
$masterDataUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$erpUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$demandPlanningUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$masterDataProcess = $null
$erpProcess = $null
$demandPlanningProcess = $null
$databaseCreated = $false

$masterDataProject = Join-Path $root 'backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj'
$erpProject = Join-Path $root 'backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj'
$demandPlanningProject = Join-Path $root 'backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj'
$probeProject = Join-Path $root 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'

try {
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $root -Name 'man517-infrastructure-up' | Out-Null
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $databaseName;") -WorkingDirectory $root -Name 'man517-create-database' | Out-Null
    $databaseCreated = $true

    if (-not $SkipBuild) {
        foreach ($project in @($masterDataProject, $erpProject, $demandPlanningProject, $probeProject)) {
            Invoke-DotNet -Arguments @('build', $project, '-m:1', '-nr:false') -WorkingDirectory $root -TimeoutSeconds 600 -Name 'man517-build' | Out-Null
        }
    }

    $commonEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        Persistence__Provider = 'PostgreSQL'
        Persistence__AutoMigrate = 'true'
        ConnectionStrings__PostgreSQL = $databaseConnectionString
        Messaging__Provider = 'Redis'
        Messaging__Redis__ConnectionString = $RedisConnectionString
        ConnectionStrings__Redis = $RedisConnectionString
        Cap__Version = $capVersion
        InternalService__BearerToken = $internalToken
    }

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $masterDataUrl }) -ScriptBlock {
        $script:masterDataProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $masterDataProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-masterdata'
    }
    Wait-Healthy -Uri "$masterDataUrl/health" -ManagedProcess $masterDataProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $erpUrl; MasterData__BaseUrl = $masterDataUrl }) -ScriptBlock {
        $script:erpProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $erpProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-erp'
    }
    Wait-Healthy -Uri "$erpUrl/health" -ManagedProcess $erpProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $demandPlanningUrl }) -ScriptBlock {
        $script:demandPlanningProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $demandPlanningProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-demand-planning'
    }
    Wait-Healthy -Uri "$demandPlanningUrl/health" -ManagedProcess $demandPlanningProcess

    $headers = @{
        Authorization = "Bearer $internalToken"
        'X-Correlation-Id' = 'corr-man517-cross-process'
        'X-Causation-Id' = 'acceptance-script'
        'X-Authenticated-Actor' = 'user:planner-demo'
    }
    Invoke-JsonPost -Uri "$masterDataUrl/api/business/v1/master-data/partners" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; code = 'CUST-DEMO-001'; partnerType = 'customer'; name = 'MAN-517 Demo Customer'
        partnerRoles = @('customer'); defaultCurrencyCode = 'CNY'; creditLimit = 100000; creditCurrencyCode = 'CNY'; idempotencyKey = 'man517-partner'
    } | Out-Null

    $quotation = Invoke-JsonPostEventually -Uri "$erpUrl/api/business/v1/erp/quotations" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; quotationNo = 'QUO-DEMO-001'; customerCode = 'CUST-DEMO-001'; expiresOn = '2026-12-31'; idempotencyKey = 'man517-quotation'
        lines = @(@{ lineNo = '10'; skuCode = 'SKU-DEMO-001'; uomCode = 'EA'; quantity = 2; unitPrice = 100; requiredDate = '2026-08-15' })
    }
    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/quotations/$($quotation.data.quotationId)/approve" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; quotationNo = 'QUO-DEMO-001'
    } | Out-Null
    $createOrderBody = @{ organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; quotationNo = 'QUO-DEMO-001'; siteCode = 'SITE-001'; idempotencyKey = 'man517-sales-order' }
    $salesOrder = Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders" -Headers $headers -Body $createOrderBody
    $released = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 1 -Quantity 2 -Status 'active'

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders" -Headers $headers -Body $createOrderBody | Out-Null
    $duplicateReplay = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 1 -Quantity 2 -Status 'active' # duplicate replay

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; lineNo = '10'; orderedQuantity = 4; unitPrice = 100; requiredDate = '2026-08-15'; reason = 'MAN-517 change v2'
    } | Out-Null
    $changedV2 = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 2 -Quantity 4 -Status 'active'
    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; lineNo = '10'; orderedQuantity = 5; unitPrice = 100; requiredDate = '2026-08-15'; reason = 'MAN-517 change v3'
    } | Out-Null
    $changedV3 = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 3 -Quantity 5 -Status 'active'

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = $databaseConnectionString
        NERV_IIP_TEST_REDIS = $RedisConnectionString
        NERV_IIP_TEST_CAP_VERSION = $capVersion
        NERV_IIP_TEST_SALES_ORDER_ID = "$($salesOrder.data.salesOrderId)"
    } -ScriptBlock {
        Invoke-DotNet -Arguments @('test', $probeProject, '--no-build', '--filter', 'FullyQualifiedName~External_process_injects_duplicate_and_out_of_order_sales_order_events') -WorkingDirectory $root -TimeoutSeconds 180 -Name 'man517-out-of-order-probe' | Out-Null
    }
    $outOfOrder = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 3 -Quantity 5 -Status 'active' # out-of-order v2 and duplicate v3 must not regress

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/cancel" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; reason = 'MAN-517 cancellation'
    } | Out-Null
    $cancelled = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 4 -Quantity 0 -Status 'cancelled'

    $evidencePath = Join-Path $root 'artifacts/acceptance/man517/sales-order-demand-planning-evidence.json'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $evidencePath)) | Out-Null
    @{
        scenario = 'MAN-517 ERP SalesOrder to DemandPlanning DemandSource'
        completedAtUtc = [DateTimeOffset]::UtcNow
        database = $databaseName
        capVersion = $capVersion
        processes = @{ masterData = $masterDataProcess.ProcessId; erp = $erpProcess.ProcessId; demandPlanning = $demandPlanningProcess.ProcessId }
        checkpoints = @{ released = $released; duplicateReplay = $duplicateReplay; changedV2 = $changedV2; changedV3 = $changedV3; outOfOrder = $outOfOrder; cancelled = $cancelled }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    Write-Host "MAN-517 separate-process PostgreSQL + Redis acceptance passed. Evidence: $evidencePath"
}
finally {
    if ($demandPlanningProcess) { $demandPlanningProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
    if ($erpProcess) { $erpProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
    if ($masterDataProcess) { $masterDataProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
    if ($databaseCreated) {
        Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE IF EXISTS $databaseName WITH (FORCE);") -WorkingDirectory $root -Name 'man517-drop-database' | Out-Null
    }
    $servicesToStop = @()
    if ($startedPostgres) { $servicesToStop += 'postgres' }
    if ($startedRedis) { $servicesToStop += 'redis' }
    if ($servicesToStop.Count -gt 0) {
        Invoke-DockerCompose -Arguments (@('-f', $composeFile, 'stop') + $servicesToStop) -WorkingDirectory $root -Name 'man517-infrastructure-stop' | Out-Null
    }
}
