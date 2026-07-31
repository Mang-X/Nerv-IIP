# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL and Redis compose services when they are not already running
#     - Builds and starts ERP, WMS, and Inventory as separate managed processes
#     - Creates a disposable PostgreSQL database and publishes real Redis CAP integration events
#   Writes:
#     - bin/ and obj/ outputs for ERP, WMS, Inventory, and the full-chain replay probe
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

function Wait-WmsOutboundOrderEvent {
    param([object]$ManagedProcess, [string]$DeliveryOrderNo)
    $stdoutPath = Join-Path $ManagedProcess.LogDirectory 'stdout.log'
    $deadline = (Get-Date).AddSeconds(30)
    do {
        if ($ManagedProcess.Process.HasExited) {
            throw "WMS exited before consuming the outbound-order event. Logs: $($ManagedProcess.LogDirectory)"
        }
        if ((Test-Path -LiteralPath $stdoutPath) -and
            (Select-String -LiteralPath $stdoutPath -SimpleMatch $DeliveryOrderNo -Quiet)) {
            Start-Sleep -Seconds 2
            return
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "WMS did not log the run-scoped outbound-order event for $DeliveryOrderNo. Logs: $($ManagedProcess.LogDirectory)"
}

function Invoke-JsonPost {
    param([string]$Uri, [hashtable]$Body, [hashtable]$Headers)
    Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 12)
}

function Get-UnassignedWmsOutboundOrderReadback {
    param(
        [string]$ComposeFile,
        [string]$DatabaseName,
        [string]$DeliveryOrderNo)
    if ($DeliveryOrderNo -notmatch '^DO-MAN527-[A-F0-9]{8}$') {
        throw "Refusing WMS outbound readback for an invalid run-scoped delivery key: $DeliveryOrderNo"
    }
    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments @(
            'compose', '-f', $ComposeFile,
            'exec', '-T', 'postgres',
            'psql', '-U', 'nerv', '-d', $DatabaseName,
            '-At', '-F', '|',
            '-c', @"
SELECT id::text, version::text
FROM wms.outbound_orders
WHERE organization_id = 'org-001'
  AND environment_id = 'env-dev'
  AND outbound_order_no = '$DeliveryOrderNo'
  AND assigned_pool_code IS NULL
  AND assigned_operator_user_id IS NULL;
"@) `
        -WorkingDirectory $root `
        -Name 'man527-read-unassigned-outbound'
    $rows = @("$($result.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($rows.Count -ne 1) {
        throw "Expected exactly one run-scoped unassigned WMS outbound readback for $DeliveryOrderNo; found $($rows.Count)."
    }
    $parts = $rows[0].Split('|')
    if ($parts.Count -ne 2) {
        throw "Run-scoped unassigned WMS outbound readback was malformed for $DeliveryOrderNo."
    }
    try {
        $outboundOrderId = [Guid]::Parse($parts[0])
        $version = [long]$parts[1]
    }
    catch {
        throw "Run-scoped unassigned WMS outbound readback was malformed for $DeliveryOrderNo."
    }
    [pscustomobject]@{
        outboundOrderId = $outboundOrderId.ToString()
        version = $version
    }
}

function Wait-WmsOutboundOrder {
    param(
        [string]$WmsUrl,
        [hashtable]$Headers,
        [string]$DeliveryOrderNo,
        [string]$ActorPrincipalId,
        [string]$SiteCode)
    $keyword = [Uri]::EscapeDataString($DeliveryOrderNo)
    $actor = [Uri]::EscapeDataString($ActorPrincipalId)
    $site = [Uri]::EscapeDataString($SiteCode)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$WmsUrl/api/business/v1/wms/outbound-orders?organizationId=org-001&environmentId=env-dev&keyword=$keyword&actorPrincipalId=$actor&authorizedSiteCodes=$site&scopeKind=site&scopeId=$site&siteCode=$site" -Headers $Headers
            $rows = @($response.data.items | Where-Object { $_.outboundOrderNo -eq $DeliveryOrderNo })
            if ($rows.Count -eq 1) { return $rows[0] }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "WMS outbound order did not converge for ERP delivery $DeliveryOrderNo."
}

function Wait-WmsPickingTask {
    param(
        [string]$WmsUrl,
        [hashtable]$Headers,
        [string]$TaskNo,
        [string]$ActorPrincipalId,
        [string]$SiteCode)
    $keyword = [Uri]::EscapeDataString($TaskNo)
    $actor = [Uri]::EscapeDataString($ActorPrincipalId)
    $site = [Uri]::EscapeDataString($SiteCode)
    $deadline = (Get-Date).AddSeconds(90)
    do {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$WmsUrl/api/business/v1/wms/picking-tasks?organizationId=org-001&environmentId=env-dev&keyword=$keyword&actorPrincipalId=$actor&authorizedSiteCodes=$site&scopeKind=site&scopeId=$site&siteCode=$site" -Headers $Headers
            $rows = @($response.data.items | Where-Object { $_.taskNo -eq $TaskNo })
            if ($rows.Count -eq 1) { return $rows[0] }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "WMS picking task did not converge for task $TaskNo."
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
if ($databaseName -notmatch '^man527_[a-f0-9]{32}$') {
    throw "Refusing to use an invalid MAN-527 disposable database name: $databaseName"
}
$databaseConnectionString = if ($PostgresAdminConnectionString -match '(?i)Database=[^;]*') {
    $PostgresAdminConnectionString -replace '(?i)Database=[^;]*', "Database=$databaseName"
} else {
    "$($PostgresAdminConnectionString.TrimEnd(';'));Database=$databaseName"
}
$capVersion = "man527-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
$internalToken = "man527-$([Guid]::NewGuid().ToString('N'))"
$deliveryOrderNo = "DO-MAN527-$([Guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant())"
$wmsActorPrincipalId = 'user-emp-049'
$wmsSiteCode = 'SITE-001'
$wmsShippingPoolCode = 'POOL-WMS-SHIPPING'
$erpUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$wmsUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$inventoryUrl = "http://127.0.0.1:$(Get-FreeTcpPort)"
$erpProcess = $null
$wmsProcess = $null
$inventoryProcess = $null
$databaseCreated = $false

$erpProject = Join-Path $root 'backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj'
$wmsProject = Join-Path $root 'backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj'
$inventoryProject = Join-Path $root 'backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj'
$probeProject = Join-Path $root 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'
$managedProcessIds = [System.Collections.Generic.List[int]]::new()
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$scenarioError = $null
$businessEvidence = $null
$evidenceDirectory = Join-Path $root 'artifacts/acceptance/man527'
$evidencePath = Join-Path $evidenceDirectory 'erp-wms-delivery-completion-evidence.json'
$cleanupEvidence = [ordered]@{
    managedProcessIds = @()
    managedProcessRemaining = $null
    databaseName = $databaseName
    exactDatabaseRemaining = $null
    postgres = if ($startedPostgres) { 'owned-pending-cleanup' } else { 'pre-existing-running-not-stopped' }
    redis = if ($startedRedis) { 'owned-pending-cleanup' } else { 'pre-existing-running-not-stopped' }
    errors = @()
}

try {
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $root -Name 'man527-infrastructure-up' | Out-Null
    Wait-PostgresReady -ComposeFile $composeFile
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $databaseName;") -WorkingDirectory $root -Name 'man527-create-database' | Out-Null
    $databaseCreated = $true

    if (-not $SkipBuild) {
        foreach ($project in @($erpProject, $wmsProject, $inventoryProject, $probeProject)) {
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

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $inventoryUrl }) -ScriptBlock {
        $script:inventoryProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $inventoryProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man527-inventory'
    }
    [void]$managedProcessIds.Add([int]$inventoryProcess.Process.Id)
    Wait-Healthy -Uri "$inventoryUrl/health" -ManagedProcess $inventoryProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{
        ASPNETCORE_URLS = $wmsUrl
        Inventory__BaseUrl = $inventoryUrl
        LeaderDemo__History__Enabled = 'true'
        LeaderDemo__Seed__OrganizationId = 'org-001'
        LeaderDemo__Seed__EnvironmentId = 'env-dev'
    }) -ScriptBlock {
        $script:wmsProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $wmsProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man527-wms-work-scope'
    }
    [void]$managedProcessIds.Add([int]$wmsProcess.Process.Id)
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
    [void]$managedProcessIds.Add([int]$erpProcess.Process.Id)
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

    Wait-WmsOutboundOrderEvent -ManagedProcess $wmsProcess -DeliveryOrderNo $deliveryOrderNo
    $unassignedOutbound = Get-UnassignedWmsOutboundOrderReadback `
        -ComposeFile $composeFile `
        -DatabaseName $databaseName `
        -DeliveryOrderNo $deliveryOrderNo
    $outboundOrderId = "$($unassignedOutbound.outboundOrderId)"
    $assignmentUri = "$wmsUrl/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/assignment"
    Invoke-JsonPost -Uri $assignmentUri -Headers $headers -Body @{
        outboundOrderId = $outboundOrderId
        organizationId = 'org-001'
        environmentId = 'env-dev'
        assignerPrincipalId = $wmsActorPrincipalId
        authorizedSiteCodes = @($wmsSiteCode)
        poolCode = $wmsShippingPoolCode
        operatorPrincipalId = $wmsActorPrincipalId
        idempotencyKey = "man527-assign-$deliveryOrderNo"
        expectedVersion = [long]$unassignedOutbound.version
    } | Out-Null
    $outbound = Wait-WmsOutboundOrder `
        -WmsUrl $wmsUrl `
        -Headers $headers `
        -DeliveryOrderNo $deliveryOrderNo `
        -ActorPrincipalId $wmsActorPrincipalId `
        -SiteCode $wmsSiteCode
    if ($outbound.assignedPoolCode -ne $wmsShippingPoolCode -or
        $outbound.assignedOperatorUserId -ne $wmsActorPrincipalId) {
        throw "Public WMS readback did not prove the first assignment for $deliveryOrderNo."
    }
    foreach ($outboundLine in @($outbound.lines)) {
        Invoke-JsonPost -Uri "$inventoryUrl/api/inventory/v1/movements" -Headers $headers -Body @{
            organizationId = 'org-001'
            environmentId = 'env-dev'
            movementType = 'inbound'
            sourceService = 'man527-acceptance'
            sourceDocumentId = $deliveryOrderNo
            sourceDocumentLineId = "$($outboundLine.lineNo)"
            idempotencyKey = "man527-stock-$deliveryOrderNo-$($outboundLine.lineNo)"
            skuCode = "$($outboundLine.skuCode)"
            uomCode = "$($outboundLine.uomCode)"
            siteCode = $wmsSiteCode
            locationCode = "$($outboundLine.locationCode)"
            lotNo = $outboundLine.lotNo
            serialNo = $outboundLine.serialNo
            qualityStatus = "$($outboundLine.qualityStatus)"
            ownerType = "$($outboundLine.ownerType)"
            ownerId = $outboundLine.ownerId
            quantity = [decimal]$outboundLine.requestedQuantity
        } | Out-Null

        $taskNo = "PICK-$deliveryOrderNo-$($outboundLine.lineNo)"
        $createTaskUri = "$wmsUrl/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/picking-tasks"
        $createdTask = Invoke-JsonPost -Uri $createTaskUri -Headers $headers -Body @{
            outboundOrderId = $outboundOrderId
            taskNo = $taskNo
            lineNo = "$($outboundLine.lineNo)"
            fromLocationCode = "$($outboundLine.locationCode)"
            toLocationCode = 'PACK-MAN527'
            quantity = [decimal]$outboundLine.requestedQuantity
        }
        $warehouseTaskId = if ($createdTask.data.warehouseTaskId -is [string]) {
            $createdTask.data.warehouseTaskId
        } elseif ($null -ne $createdTask.data.warehouseTaskId.value) {
            $createdTask.data.warehouseTaskId.value
        } else {
            "$($createdTask.data.warehouseTaskId)"
        }
        $pickingTask = Wait-WmsPickingTask `
            -WmsUrl $wmsUrl `
            -Headers $headers `
            -TaskNo $taskNo `
            -ActorPrincipalId $wmsActorPrincipalId `
            -SiteCode $wmsSiteCode

        $taskAssignmentUri = "$wmsUrl/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/assignment"
        $taskAssignment = Invoke-JsonPost -Uri $taskAssignmentUri -Headers $headers -Body @{
            warehouseTaskId = $warehouseTaskId
            organizationId = 'org-001'
            environmentId = 'env-dev'
            assignerPrincipalId = $wmsActorPrincipalId
            authorizedSiteCodes = @($wmsSiteCode)
            poolCode = $wmsShippingPoolCode
            operatorPrincipalId = $wmsActorPrincipalId
            idempotencyKey = "man527-assign-task-$deliveryOrderNo-$($outboundLine.lineNo)"
            expectedVersion = [long]$pickingTask.version
        }

        $taskActionBody = @{
            warehouseTaskId = $warehouseTaskId
            organizationId = 'org-001'
            environmentId = 'env-dev'
            actorPrincipalId = $wmsActorPrincipalId
            authorizedSiteCodes = @($wmsSiteCode)
            scopeKind = 'site'
            scopeId = $wmsSiteCode
        }
        $taskStartUri = "$wmsUrl/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/start"
        $taskStart = Invoke-JsonPost -Uri $taskStartUri -Headers $headers -Body ($taskActionBody + @{
            idempotencyKey = "man527-start-task-$deliveryOrderNo-$($outboundLine.lineNo)"
            expectedVersion = [long]$taskAssignment.data.version
        })
        $taskProgressUri = "$wmsUrl/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/progress"
        $taskProgress = Invoke-JsonPost -Uri $taskProgressUri -Headers $headers -Body ($taskActionBody + @{
            idempotencyKey = "man527-progress-task-$deliveryOrderNo-$($outboundLine.lineNo)"
            expectedVersion = [long]$taskStart.data.version
            executedQuantity = [decimal]$outboundLine.requestedQuantity
        })
        $taskCompleteUri = "$wmsUrl/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/complete"
        Invoke-JsonPost -Uri $taskCompleteUri -Headers $headers -Body ($taskActionBody + @{
            idempotencyKey = "man527-complete-task-$deliveryOrderNo-$($outboundLine.lineNo)"
            expectedVersion = [long]$taskProgress.data.version
            executedQuantity = [decimal]$outboundLine.requestedQuantity
            differenceReason = $null
        }) | Out-Null
    }

    $outboundAfterPicking = Wait-WmsOutboundOrder `
        -WmsUrl $wmsUrl `
        -Headers $headers `
        -DeliveryOrderNo $deliveryOrderNo `
        -ActorPrincipalId $wmsActorPrincipalId `
        -SiteCode $wmsSiteCode
    $completionBody = @{
        outboundOrderId = $outboundOrderId
        packReviewNo = "PACK-$deliveryOrderNo"
        passed = $true
        idempotencyKey = "man527-complete-$deliveryOrderNo"
        organizationId = 'org-001'
        environmentId = 'env-dev'
        actorPrincipalId = $wmsActorPrincipalId
        authorizedSiteCodes = @($wmsSiteCode)
        scopeKind = 'site'
        scopeId = $wmsSiteCode
        expectedVersion = [long]$outboundAfterPicking.version
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

    $businessEvidence = [ordered]@{
        verifiedAtUtc = [DateTimeOffset]::UtcNow
        scenarioStatus = 'passed'
        deliveryOrderNo = $deliveryOrderNo
        transport = 'Redis CAP across separate ERP, WMS, Inventory, and replay-probe processes'
        persistence = 'Disposable real PostgreSQL database'
        wmsOutboundOrder = [ordered]@{
            outboundOrderNo = $outbound.outboundOrderNo
            firstAssignment = [ordered]@{
                preAssignmentReadback = 'exact run-scoped row had no assigned pool or operator'
                preAssignmentVersion = $unassignedOutbound.version
                poolCode = $outbound.assignedPoolCode
                operatorPrincipalId = $outbound.assignedOperatorUserId
                establishedThrough = 'public WMS assignment endpoint'
            }
            pickingLifecycle = 'public create/read/assign/start/progress/complete for every outbound line'
            completionHttpReplay = 'same idempotency key accepted twice'
        }
        erpDelivery = [ordered]@{
            status = $deliveryAfterReplay.status
            shippedAtUtc = $deliveryAfterReplay.shippedAtUtc
            completedAtUtc = $deliveryAfterReplay.completedAtUtc
            shippedQuantity = $deliveryAfterReplay.lines[0].shippedQuantity
        }
        accountReceivable = [ordered]@{ receivableNo = $receivableAfterReplay.receivableNo; sourceDocumentNo = $receivableAfterReplay.sourceDocumentNo }
        repeatedEvent = 'same event id published twice through Redis; one delivery projection, one receivable, one target-consumer durable inbox row, no target-consumer dead letter'
    }
}
catch {
    $scenarioError = $_
}
finally {
    # Cleanup-Step: stop-erp
    try {
        if ($erpProcess) { $erpProcess.Stop.Invoke('MAN-527 verification completed') }
    }
    catch {
        [void]$cleanupErrors.Add("stop-erp: $($_.Exception.Message)")
    }
    # Cleanup-Step: stop-wms
    try {
        if ($wmsProcess) { $wmsProcess.Stop.Invoke('MAN-527 verification completed') }
    }
    catch {
        [void]$cleanupErrors.Add("stop-wms: $($_.Exception.Message)")
    }
    # Cleanup-Step: stop-inventory
    try {
        if ($inventoryProcess) { $inventoryProcess.Stop.Invoke('MAN-527 verification completed') }
    }
    catch {
        [void]$cleanupErrors.Add("stop-inventory: $($_.Exception.Message)")
    }
    # Cleanup-Step: drop-database
    try {
        if ($databaseCreated) {
            Invoke-DockerCompose -Arguments @(
                '-f', $composeFile,
                'exec', '-T', 'postgres',
                'psql', '-U', 'nerv', '-d', 'postgres',
                '-v', 'ON_ERROR_STOP=1',
                '-c', "DROP DATABASE IF EXISTS $databaseName WITH (FORCE);"
            ) -WorkingDirectory $root -Name 'man527-drop-database' | Out-Null
        }
    }
    catch {
        [void]$cleanupErrors.Add("drop-database: $($_.Exception.Message)")
    }
    # Cleanup-Step: readback-database
    try {
        if (-not $databaseCreated) {
            $cleanupEvidence.exactDatabaseRemaining = 0
        }
        else {
            $databaseReadback = Invoke-NativeCommandOutput `
                -Command 'docker' `
                -Arguments @(
                    'compose', '-f', $composeFile,
                    'exec', '-T', 'postgres',
                    'psql', '-U', 'nerv', '-d', 'postgres',
                    '-At',
                    '-c', "SELECT COUNT(*) FROM pg_database WHERE datname = '$databaseName';"
                ) `
                -WorkingDirectory $root `
                -Name 'man527-readback-database-cleanup'
            $cleanupEvidence.exactDatabaseRemaining = [int]("$($databaseReadback.Stdout)".Trim())
            if ($cleanupEvidence.exactDatabaseRemaining -ne 0) {
                throw "Disposable database still exists after cleanup: $databaseName"
            }
        }
    }
    catch {
        [void]$cleanupErrors.Add("readback-database: $($_.Exception.Message)")
    }
    # Cleanup-Step: stop-postgres
    if ($startedPostgres) {
        try {
            Invoke-DockerCompose -Arguments @('-f', $composeFile, 'stop', 'postgres') -WorkingDirectory $root -Name 'man527-stop-postgres' | Out-Null
            $runningAfterStop = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man527-readback-postgres-stop'
            $runningServices = @("$($runningAfterStop.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($runningServices -contains 'postgres') {
                throw 'Script-owned PostgreSQL service is still running after stop.'
            }
            $cleanupEvidence.postgres = 'owned-stopped'
        }
        catch {
            [void]$cleanupErrors.Add("stop-postgres: $($_.Exception.Message)")
        }
    }
    # Cleanup-Step: stop-redis
    if ($startedRedis) {
        try {
            Invoke-DockerCompose -Arguments @('-f', $composeFile, 'stop', 'redis') -WorkingDirectory $root -Name 'man527-stop-redis' | Out-Null
            $runningAfterStop = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man527-readback-redis-stop'
            $runningServices = @("$($runningAfterStop.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($runningServices -contains 'redis') {
                throw 'Script-owned Redis service is still running after stop.'
            }
            $cleanupEvidence.redis = 'owned-stopped'
        }
        catch {
            [void]$cleanupErrors.Add("stop-redis: $($_.Exception.Message)")
        }
    }
    # Cleanup-Step: readback-managed-processes
    try {
        $remainingManagedProcesses = @($managedProcessIds | Where-Object {
            $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue)
        })
        $cleanupEvidence.managedProcessIds = @($managedProcessIds)
        $cleanupEvidence.managedProcessRemaining = $remainingManagedProcesses.Count
        if ($remainingManagedProcesses.Count -ne 0) {
            throw "Managed process PIDs still exist after cleanup: $($remainingManagedProcesses -join ', ')"
        }
    }
    catch {
        [void]$cleanupErrors.Add("readback-managed-processes: $($_.Exception.Message)")
    }

    $cleanupEvidence.errors = @($cleanupErrors)
    $evidencePayload = if ($null -ne $businessEvidence) {
        $businessEvidence
    }
    else {
        [ordered]@{
            verifiedAtUtc = [DateTimeOffset]::UtcNow
            scenarioStatus = 'failed'
            deliveryOrderNo = $deliveryOrderNo
            scenarioError = if ($null -ne $scenarioError) { $scenarioError.Exception.Message } else { 'Business evidence was not produced.' }
        }
    }
    $evidencePayload.cleanup = $cleanupEvidence
    try {
        [System.IO.Directory]::CreateDirectory($evidenceDirectory) | Out-Null
        $evidencePayload | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding utf8
        Write-Diagnostic "MAN-527 ERP/WMS delivery-completion evidence written after cleanup to $evidencePath"
    }
    catch {
        [void]$cleanupErrors.Add("write-evidence: $($_.Exception.Message)")
    }
}

if ($null -ne $scenarioError -or $cleanupErrors.Count -ne 0) {
    $failureParts = [System.Collections.Generic.List[string]]::new()
    if ($null -ne $scenarioError) {
        [void]$failureParts.Add("scenario: $($scenarioError.Exception.Message)")
    }
    foreach ($cleanupError in $cleanupErrors) {
        [void]$failureParts.Add("cleanup: $cleanupError")
    }
    throw "MAN-527 verification failed. $($failureParts -join ' | ')"
}
