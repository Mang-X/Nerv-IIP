# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL and Redis compose services when they are not already running
#     - Builds and starts ERP and WMS as separate managed processes
#     - Creates a disposable PostgreSQL database and publishes real Redis CAP integration events
#   Writes:
#     - bin/ and obj/ outputs for ERP, WMS, and the full-chain replay probe
#     - artifacts/script-logs/**
#     - artifacts/acceptance/man527/erp-wms-delivery-completion-evidence.json
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

function Wait-PostgresReady {
    param([string]$ComposeFile)
    $deadline = (Get-Date).AddSeconds(60)
    do {
        try {
            Invoke-DockerCompose -Arguments @('-f', $ComposeFile, 'exec', '-T', 'postgres', 'pg_isready', '-U', 'nerv', '-d', 'postgres') -WorkingDirectory $root -Name 'man527-postgres-ready' | Out-Null
            return
        }
        catch {
            if ((Get-Date) -ge $deadline) { throw }
            Start-Sleep -Milliseconds 500
        }
    } while ($true)
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

function Wait-WmsOutboundOrder {
    param([string]$WmsUrl, [hashtable]$Headers, [string]$DeliveryOrderNo)
    $keyword = [Uri]::EscapeDataString($DeliveryOrderNo)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$WmsUrl/api/business/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&keyword=$keyword" -Headers $Headers
            $rows = @($response.data.items | Where-Object { $_.outboundOrderNo -eq $DeliveryOrderNo })
            if ($rows.Count -eq 1) { return $rows[0] }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "WMS outbound order did not converge for ERP delivery $DeliveryOrderNo."
}

function Wait-ErpSalesOrder {
    param([string]$ErpUrl, [hashtable]$Headers)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$ErpUrl/api/business/v1/erp/sales-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=SO-DEMO-001" -Headers $Headers
            $rows = @($response.data.items | Where-Object { $_.salesOrderNo -eq 'SO-DEMO-001' })
            if ($rows.Count -eq 1) { return $rows[0] }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw 'Reusable ERP sales-order seed SO-DEMO-001 did not become queryable after service startup.'
}

function Wait-ErpDeliveryOrder {
    param([string]$ErpUrl, [hashtable]$Headers, [string]$DeliveryOrderNo)
    $keyword = [Uri]::EscapeDataString($DeliveryOrderNo)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$ErpUrl/api/business/v1/erp/delivery-orders?organizationId=org-001&environmentId=env-dev&status=completed&keyword=$keyword" -Headers $Headers
            $rows = @($response.data.items | Where-Object { $_.deliveryOrderNo -eq $DeliveryOrderNo })
            if ($rows.Count -eq 1) {
                $row = $rows[0]
                $lines = @($row.lines)
                if ($row.status -eq 'completed' -and
                    -not [string]::IsNullOrWhiteSpace("$($row.shippedAtUtc)") -and
                    -not [string]::IsNullOrWhiteSpace("$($row.completedAtUtc)") -and
                    $lines.Count -eq 1 -and
                    [decimal]$lines[0].shippedQuantity -eq 2) {
                    return $row
                }
            }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "ERP delivery $DeliveryOrderNo did not converge to completed with shippedQuantity, shippedAtUtc, and completedAtUtc."
}

function Wait-Receivable {
    param([string]$ErpUrl, [hashtable]$Headers, [string]$DeliveryOrderNo)
    $source = [Uri]::EscapeDataString($DeliveryOrderNo)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$ErpUrl/api/business/v1/erp/finance/receivables/by-source?organizationId=org-001&environmentId=env-dev&sourceDocumentNo=$source" -Headers $Headers
            if ($response.data.sourceDocumentNo -eq $DeliveryOrderNo) { return $response.data }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "ERP receivable did not converge for completed delivery $DeliveryOrderNo."
}

$composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
$runningResult = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man527-compose-running'
$running = @("$($runningResult.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
$startedPostgres = $running -notcontains 'postgres'
$startedRedis = $running -notcontains 'redis'
$databaseName = "man527_$([Guid]::NewGuid().ToString('N'))"
$databaseConnectionString = if ($PostgresAdminConnectionString -match '(?i)Database=[^;]*') {
    $PostgresAdminConnectionString -replace '(?i)Database=[^;]*', "Database=$databaseName"
} else {
    "$($PostgresAdminConnectionString.TrimEnd(';'));Database=$databaseName"
}
$capVersion = "man527-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$internalToken = "man527-$([Guid]::NewGuid().ToString('N'))"
$deliveryOrderNo = "DO-MAN527-$([Guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant())"
$erpUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$wmsUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$erpProcess = $null
$wmsProcess = $null
$databaseCreated = $false

$erpProject = Join-Path $root 'backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj'
$wmsProject = Join-Path $root 'backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj'
$probeProject = Join-Path $root 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'

try {
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $root -Name 'man527-infrastructure-up' | Out-Null
    Wait-PostgresReady -ComposeFile $composeFile
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $databaseName;") -WorkingDirectory $root -Name 'man527-create-database' | Out-Null
    $databaseCreated = $true

    if (-not $SkipBuild) {
        foreach ($project in @($erpProject, $wmsProject, $probeProject)) {
            Invoke-DotNet -Arguments @('build', $project, '-m:1', '-nr:false', '/p:UseSharedCompilation=false') -WorkingDirectory $root -TimeoutSeconds 600 -Name 'man527-build' | Out-Null
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
        Cap__FailedRetryInterval = '2'
        Cap__FallbackWindowLookbackSeconds = '30'
        InternalService__BearerToken = $internalToken
    }

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $wmsUrl }) -ScriptBlock {
        $script:wmsProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $wmsProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man527-wms'
    }
    Wait-Healthy -Uri "$wmsUrl/health" -ManagedProcess $wmsProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{
        ASPNETCORE_URLS = $erpUrl
        Wms__BaseUrl = $wmsUrl
        Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'
        Erp__Seed__OrganizationId = 'org-001'
        Erp__Seed__EnvironmentId = 'env-dev'
    }) -ScriptBlock {
        $script:erpProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $erpProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man527-erp'
    }
    Wait-Healthy -Uri "$erpUrl/health" -ManagedProcess $erpProcess

    $headers = @{
        Authorization = "Bearer $internalToken"
        'X-Correlation-Id' = 'corr-man527-cross-process'
        'X-Causation-Id' = 'acceptance-script'
        'X-Authenticated-Actor' = 'user:man527-acceptance'
    }
    Wait-ErpSalesOrder -ErpUrl $erpUrl -Headers $headers | Out-Null
    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/delivery-orders" -Headers $headers -Body @{
        organizationId = 'org-001'
        environmentId = 'env-dev'
        deliveryOrderNo = $deliveryOrderNo
        salesOrderNo = 'SO-DEMO-001'
        idempotencyKey = "man527-release-$deliveryOrderNo"
        lines = @(@{
            salesOrderLineNo = '10'
            quantity = 2
            locationCode = 'LOC-A-01'
            lotNo = 'LOT-MAN527'
        })
    } | Out-Null

    $outbound = Wait-WmsOutboundOrder -WmsUrl $wmsUrl -Headers $headers -DeliveryOrderNo $deliveryOrderNo
    $outboundOrderId = if ($outbound.outboundOrderId -is [string]) { $outbound.outboundOrderId } elseif ($null -ne $outbound.outboundOrderId.value) { $outbound.outboundOrderId.value } else { "$($outbound.outboundOrderId)" }
    $completionBody = @{
        outboundOrderId = $outboundOrderId
        packReviewNo = "PACK-$deliveryOrderNo"
        passed = $true
        idempotencyKey = "man527-complete-$deliveryOrderNo"
    }
    $completionUri = "$wmsUrl/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/complete"
    Invoke-JsonPost -Uri $completionUri -Headers $headers -Body $completionBody | Out-Null
    Invoke-JsonPost -Uri $completionUri -Headers $headers -Body $completionBody | Out-Null

    $deliveryBeforeReplay = Wait-ErpDeliveryOrder -ErpUrl $erpUrl -Headers $headers -DeliveryOrderNo $deliveryOrderNo
    $receivableBeforeReplay = Wait-Receivable -ErpUrl $erpUrl -Headers $headers -DeliveryOrderNo $deliveryOrderNo

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = $databaseConnectionString
        NERV_IIP_TEST_REDIS = $RedisConnectionString
        NERV_IIP_TEST_CAP_VERSION = $capVersion
        NERV_IIP_TEST_DELIVERY_ORDER_NO = $deliveryOrderNo
    } -ScriptBlock {
        $probeResultsDirectory = Join-Path $root 'artifacts/acceptance/man527'
        [System.IO.Directory]::CreateDirectory($probeResultsDirectory) | Out-Null
        $probeResultsFile = "replay-$([Guid]::NewGuid().ToString('N')).trx"
        $probeResults = Join-Path $probeResultsDirectory $probeResultsFile
        Invoke-DotNet -Arguments @('test', $probeProject, '--no-build', '--filter', 'FullyQualifiedName~External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts', '--results-directory', $probeResultsDirectory, '--logger', "trx;LogFileName=$probeResultsFile") -WorkingDirectory $root -TimeoutSeconds 180 -Name 'man527-replay-probe' | Out-Null
        if (-not (Test-Path -LiteralPath $probeResults)) {
            throw 'MAN-527 replay probe produced no TRX result; the selected test may be absent from a stale build.'
        }
        [xml]$probeTrx = Get-Content -LiteralPath $probeResults -Raw
        $probeExecutions = @($probeTrx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName').EndsWith('.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts', [StringComparison]::Ordinal) })
        if ($probeExecutions.Count -ne 1 -or $probeExecutions[0].GetAttribute('outcome') -ne 'Passed') {
            throw 'MAN-527 repeated-event probe did not execute exactly once and pass.'
        }
    }

    $deliveryAfterReplay = Wait-ErpDeliveryOrder -ErpUrl $erpUrl -Headers $headers -DeliveryOrderNo $deliveryOrderNo
    $receivableAfterReplay = Wait-Receivable -ErpUrl $erpUrl -Headers $headers -DeliveryOrderNo $deliveryOrderNo
    if ($deliveryAfterReplay.shippedAtUtc -ne $deliveryBeforeReplay.shippedAtUtc -or
        $deliveryAfterReplay.completedAtUtc -ne $deliveryBeforeReplay.completedAtUtc -or
        [decimal]$deliveryAfterReplay.lines[0].shippedQuantity -ne [decimal]$deliveryBeforeReplay.lines[0].shippedQuantity -or
        $receivableAfterReplay.receivableNo -ne $receivableBeforeReplay.receivableNo) {
        throw 'Repeated completion changed the public ERP delivery or receivable facts.'
    }

    $evidenceDirectory = Join-Path $root 'artifacts/acceptance/man527'
    [System.IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
    $evidencePath = Join-Path $evidenceDirectory 'erp-wms-delivery-completion-evidence.json'
    [ordered]@{
        verifiedAtUtc = [DateTimeOffset]::UtcNow
        deliveryOrderNo = $deliveryOrderNo
        transport = 'Redis CAP across separate ERP, WMS, and replay-probe processes'
        persistence = 'Disposable real PostgreSQL database'
        wmsOutboundOrder = [ordered]@{ outboundOrderNo = $outbound.outboundOrderNo; completionHttpReplay = 'same idempotency key accepted twice' }
        erpDelivery = [ordered]@{
            status = $deliveryAfterReplay.status
            shippedAtUtc = $deliveryAfterReplay.shippedAtUtc
            completedAtUtc = $deliveryAfterReplay.completedAtUtc
            shippedQuantity = $deliveryAfterReplay.lines[0].shippedQuantity
        }
        accountReceivable = [ordered]@{ receivableNo = $receivableAfterReplay.receivableNo; sourceDocumentNo = $receivableAfterReplay.sourceDocumentNo }
        repeatedEvent = 'same event id published twice through Redis; one delivery projection, one receivable, one target-consumer durable inbox row, no target-consumer dead letter'
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    Write-Diagnostic "MAN-527 ERP/WMS delivery-completion evidence written to $evidencePath"
}
finally {
    if ($erpProcess) { $erpProcess.Stop.Invoke('MAN-527 verification completed') }
    if ($wmsProcess) { $wmsProcess.Stop.Invoke('MAN-527 verification completed') }
    if ($databaseCreated) {
        Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE IF EXISTS $databaseName WITH (FORCE);") -WorkingDirectory $root -Name 'man527-drop-database' | Out-Null
    }
    $servicesToStop = @()
    if ($startedPostgres) { $servicesToStop += 'postgres' }
    if ($startedRedis) { $servicesToStop += 'redis' }
    if ($servicesToStop.Count -gt 0) {
        Invoke-DockerCompose -Arguments (@('-f', $composeFile, 'stop') + $servicesToStop) -WorkingDirectory $root -Name 'man527-infrastructure-stop' | Out-Null
    }
}
