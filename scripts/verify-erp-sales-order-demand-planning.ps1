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
#     - artifacts/acceptance/man517/diagnostics/** on failure
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
            Invoke-DockerCompose -Arguments @('-f', $ComposeFile, 'exec', '-T', 'postgres', 'pg_isready', '-U', 'nerv', '-d', 'postgres') -WorkingDirectory $root -Name 'man517-postgres-ready' | Out-Null
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

function Wait-Demand {
    param([string]$DemandPlanningUrl, [hashtable]$Headers, [int]$Version, [decimal]$Quantity, [string]$Status)
    # CAP 10.0.1 scans failed messages every 60 seconds by default. Cover one
    # complete retry scan plus scheduling slack without weakening the assertion.
    $deadline = (Get-Date).AddSeconds(90)
    $lastHttpStatus = $null
    $lastResponseBody = $null
    $lastRequestException = $null
    $lastObservedDemand = $null
    do {
        try {
            $httpResponse = Invoke-WebRequest -Method Get -Uri "$DemandPlanningUrl/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev" -Headers $Headers -SkipHttpErrorCheck
            $lastHttpStatus = [int]$httpResponse.StatusCode
            $fullResponseBody = "$($httpResponse.Content)"
            $lastResponseBody = if ($fullResponseBody.Length -gt 8192) { $fullResponseBody.Substring(0, 8192) } else { $fullResponseBody }
            $lastRequestException = $null
            $response = $fullResponseBody | ConvertFrom-Json
            $rows = @($response.data | Where-Object { $_.sourceReference -eq 'SO-DEMO-001' })
            $lastObservedDemand = if ($rows.Count -eq 1) {
                [ordered]@{ version = $rows[0].sourceVersion; quantity = $rows[0].quantity; status = $rows[0].sourceStatus }
            } else {
                [ordered]@{ matchingRowCount = $rows.Count }
            }
            if ($rows.Count -eq 1 -and $rows[0].sourceVersion -eq $Version -and [decimal]$rows[0].quantity -eq $Quantity -and $rows[0].sourceStatus -eq $Status) {
                return $rows[0]
            }
        }
        catch {
            $lastRequestException = $_.Exception.Message
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    $lastObservation = [ordered]@{
        lastHttpStatus = $lastHttpStatus
        lastResponseBody = $lastResponseBody
        lastRequestException = $lastRequestException
        lastObservedDemand = $lastObservedDemand
    } | ConvertTo-Json -Depth 8 -Compress
    throw "Demand SO-DEMO-001 did not converge to version=$Version quantity=$Quantity status=$Status. Last observation: $lastObservation"
}

function Assert-DemandStable {
    param([string]$DemandPlanningUrl, [hashtable]$Headers, [int]$Version, [decimal]$Quantity, [string]$Status, [int]$Seconds = 5)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $response = Invoke-RestMethod -Method Get -Uri "$DemandPlanningUrl/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev" -Headers $Headers
        $rows = @($response.data | Where-Object { $_.sourceReference -eq 'SO-DEMO-001' })
        if ($rows.Count -ne 1 -or $rows[0].sourceVersion -ne $Version -or [decimal]$rows[0].quantity -ne $Quantity -or $rows[0].sourceStatus -ne $Status) {
            throw "Demand SO-DEMO-001 changed during the stability window; expected version=$Version quantity=$Quantity status=$Status."
        }
        $row = $rows[0]
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $row
}

function Protect-Man517DiagnosticText {
    param([AllowNull()][string]$Text)
    if ($null -eq $Text) { return $null }
    $safe = Protect-ScriptAutomationText $Text
    if (-not [string]::IsNullOrWhiteSpace($internalToken)) {
        $safe = $safe.Replace($internalToken, '[REDACTED_TOKEN]', [StringComparison]::Ordinal)
    }
    return Protect-ScriptAutomationText $safe
}

function Write-Man517DiagnosticFile {
    param([string]$Path, [AllowNull()][string]$Content)
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    Protect-Man517DiagnosticText -Text $Content | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Man517DiagnosticCommand {
    param([string]$Name, [string]$Command, [string[]]$Arguments, [string]$OutputPath)
    try {
        $result = Invoke-NativeCommandOutput -Command $Command -Arguments $Arguments -WorkingDirectory $root -Name $Name
        Write-Man517DiagnosticFile -Path $OutputPath -Content $result.Stdout
    }
    catch {
        Write-Man517DiagnosticFile -Path $OutputPath -Content "Diagnostic command failed: $($_.Exception.Message)"
    }
}

function Export-Man517FailureDiagnostics {
    param([object]$FailureRecord)
    $diagnosticsRoot = Join-Path $root 'artifacts/acceptance/man517/diagnostics'
    [System.IO.Directory]::CreateDirectory($diagnosticsRoot) | Out-Null
    Write-Man517DiagnosticFile -Path (Join-Path $diagnosticsRoot 'failure-summary.json') -Content (@{
        capturedAtUtc = [DateTimeOffset]::UtcNow
        database = $databaseName
        capVersion = $capVersion
        failure = $FailureRecord.Exception.Message
    } | ConvertTo-Json -Depth 8)

    foreach ($entry in @{
        masterdata = $masterDataProcess
        erp = $erpProcess
        demandplanning = $demandPlanningProcess
    }.GetEnumerator()) {
        if ($null -eq $entry.Value) { continue }
        foreach ($stream in @('stdout', 'stderr')) {
            $source = Join-Path $entry.Value.LogDirectory "$stream.log"
            $target = Join-Path $diagnosticsRoot "$($entry.Key)-$stream-tail.log"
            try {
                $tail = Get-Content -LiteralPath $source -Tail 400 -ErrorAction Stop
                Write-Man517DiagnosticFile -Path $target -Content ($tail -join [Environment]::NewLine)
            }
            catch {
                Write-Man517DiagnosticFile -Path $target -Content "Could not read service log tail: $($_.Exception.Message)"
            }
        }
    }

    if ($databaseCreated) {
        $databaseSql = @"
SELECT 'erp.cap_published_messages' AS diagnostic_source,
       COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb) AS rows
FROM (SELECT "Id", "Name", "StatusName", "Retries", "Added", "ExpiresAt", "Version"
      FROM erp.cap_published_messages WHERE "Version" = '$capVersion' ORDER BY "Id" DESC LIMIT 100) row_data
UNION ALL
SELECT 'erp.cap_received_messages', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT "Id", "Name", "Group", "StatusName", "Retries", "Added", "ExpiresAt", "Version"
      FROM erp.cap_received_messages WHERE "Version" = '$capVersion' ORDER BY "Id" DESC LIMIT 100) row_data
UNION ALL
SELECT 'demand_planning.cap_published_messages', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT "Id", "Name", "StatusName", "Retries", "Added", "ExpiresAt", "Version"
      FROM demand_planning.cap_published_messages WHERE "Version" = '$capVersion' ORDER BY "Id" DESC LIMIT 100) row_data
UNION ALL
SELECT 'demand_planning.cap_received_messages', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT "Id", "Name", "Group", "StatusName", "Retries", "Added", "ExpiresAt", "Version"
      FROM demand_planning.cap_received_messages WHERE "Version" = '$capVersion' ORDER BY "Id" DESC LIMIT 100) row_data
UNION ALL
SELECT 'demand_planning.processed_integration_events', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT consumer_name, event_id, event_type, event_version, source_service, idempotency_key, processed_at_utc
      FROM demand_planning.processed_integration_events ORDER BY processed_at_utc DESC LIMIT 100) row_data
UNION ALL
SELECT 'demand_planning.integration_event_dead_letters', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT consumer_name, event_id, event_type, event_version, source_service, idempotency_key, failure_code, failure_message, status, dead_lettered_at_utc
      FROM demand_planning.integration_event_dead_letters ORDER BY dead_lettered_at_utc DESC LIMIT 100) row_data
UNION ALL
SELECT 'demand_planning.sales_order_demand_projections', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT organization_id, environment_id, sales_order_id, sales_order_no, order_version, status, last_event_id, occurred_at_utc
      FROM demand_planning.sales_order_demand_projections WHERE sales_order_no = 'SO-DEMO-001') row_data
UNION ALL
SELECT 'demand_planning.demand_sources', COALESCE(jsonb_agg(to_jsonb(row_data)), '[]'::jsonb)
FROM (SELECT organization_id, environment_id, source_document_id, source_reference, source_line_reference, quantity, source_version, source_status, updated_at_utc
      FROM demand_planning.demand_sources WHERE source_reference = 'SO-DEMO-001') row_data;
"@
        Invoke-Man517DiagnosticCommand -Name 'man517-diagnostics-postgres' -Command 'docker' -Arguments @(
            'compose', '-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', $databaseName,
            '-X', '-v', 'ON_ERROR_STOP=1', '-P', 'pager=off', '-c', $databaseSql
        ) -OutputPath (Join-Path $diagnosticsRoot 'postgres-state.txt')
    }

    $redisLines = [System.Collections.Generic.List[string]]::new()
    $redisGroup = "business-demand-planning.erp-sales-order-demand.$capVersion"
    foreach ($streamName in @(
        'SalesOrderReleasedIntegrationEvent',
        'SalesOrderChangedIntegrationEvent',
        'SalesOrderCancelledIntegrationEvent',
        'Nerv.IIP.Contracts.Erp.SalesOrderReleasedIntegrationEvent',
        'Nerv.IIP.Contracts.Erp.SalesOrderChangedIntegrationEvent',
        'Nerv.IIP.Contracts.Erp.SalesOrderCancelledIntegrationEvent'
    )) {
        foreach ($redisArguments in @(
            @('XINFO', 'STREAM', $streamName),
            @('XINFO', 'GROUPS', $streamName),
            @('XPENDING', $streamName, $redisGroup)
        )) {
            try {
                $result = Invoke-NativeCommandOutput -Command 'docker' -Arguments (@('compose', '-f', $composeFile, 'exec', '-T', 'redis', 'redis-cli') + $redisArguments) -WorkingDirectory $root -Name 'man517-diagnostics-redis'
                $redisLines.Add("COMMAND redis-cli $($redisArguments -join ' ')")
                $redisLines.Add("$($result.Stdout)")
            }
            catch {
                $redisLines.Add("COMMAND redis-cli $($redisArguments -join ' ') FAILED: $($_.Exception.Message)")
            }
        }
    }
    Write-Man517DiagnosticFile -Path (Join-Path $diagnosticsRoot 'redis-stream-state.txt') -Content ($redisLines -join [Environment]::NewLine)
    Write-Diagnostic -Level 'WARN' -Message "MAN-517 failure diagnostics captured before cleanup: $diagnosticsRoot"
}

$composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
$runningResult = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man517-compose-running'
$running = @("$($runningResult.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
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
$demandPlanningTestsProject = Join-Path $root 'backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj'

try {
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $root -Name 'man517-infrastructure-up' | Out-Null
    Wait-PostgresReady -ComposeFile $composeFile
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $databaseName;") -WorkingDirectory $root -Name 'man517-create-database' | Out-Null
    $databaseCreated = $true

    if (-not $SkipBuild) {
        foreach ($project in @($masterDataProject, $erpProject, $demandPlanningProject, $probeProject, $demandPlanningTestsProject)) {
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

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $demandPlanningUrl }) -ScriptBlock {
        $script:demandPlanningProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $demandPlanningProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-demand-planning'
    }
    Wait-Healthy -Uri "$demandPlanningUrl/health" -ManagedProcess $demandPlanningProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{
        ASPNETCORE_URLS = $erpUrl
        MasterData__BaseUrl = $masterDataUrl
        Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'
        Erp__Seed__OrganizationId = 'org-001'
        Erp__Seed__EnvironmentId = 'env-dev'
    }) -ScriptBlock {
        $script:erpProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $erpProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-erp'
    }
    Wait-Healthy -Uri "$erpUrl/health" -ManagedProcess $erpProcess

    $headers = @{
        Authorization = "Bearer $internalToken"
        'X-Correlation-Id' = 'corr-man517-cross-process'
        'X-Causation-Id' = 'acceptance-script'
        'X-Authenticated-Actor' = 'user:planner-demo'
    }
    $released = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 1 -Quantity 2 -Status 'active'

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
        NERV_IIP_TEST_PROBE_RUN_ID = [Guid]::NewGuid().ToString('N')
    } -ScriptBlock {
        $probeResultsDirectory = Join-Path $root 'artifacts/acceptance/man517'
        [System.IO.Directory]::CreateDirectory($probeResultsDirectory) | Out-Null
        $probeResultsFile = "probe-$([Guid]::NewGuid().ToString('N')).trx"
        $probeResults = Join-Path $probeResultsDirectory $probeResultsFile
        Invoke-DotNet -Arguments @('test', $probeProject, '--no-build', '--filter', 'FullyQualifiedName~External_process_injects_duplicate_and_out_of_order_sales_order_events', '--results-directory', $probeResultsDirectory, '--logger', "trx;LogFileName=$probeResultsFile") -WorkingDirectory $root -TimeoutSeconds 180 -Name 'man517-out-of-order-probe' | Out-Null
        if (-not (Test-Path -LiteralPath $probeResults)) {
            throw 'MAN-517 fault-injection probe produced no TRX result; the selected test may be absent from a stale build.'
        }
        [xml]$probeTrx = Get-Content -LiteralPath $probeResults -Raw
        $probeExecutions = @($probeTrx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName').EndsWith('.External_process_injects_duplicate_and_out_of_order_sales_order_events', [StringComparison]::Ordinal) })
        if ($probeExecutions.Count -ne 1 -or $probeExecutions[0].GetAttribute('outcome') -ne 'Passed') {
            throw 'MAN-517 fault-injection probe did not execute exactly once and pass.'
        }
    }
    $outOfOrder = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 3 -Quantity 5 -Status 'active' # out-of-order v2 and duplicate v3 must not regress
    $duplicateReplay = $outOfOrder # probes above and below exercise duplicate delivery through the real Redis transport

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = $PostgresAdminConnectionString
        NERV_IIP_TEST_REDIS = $RedisConnectionString
    } -ScriptBlock {
        $duplicateResultsDirectory = Join-Path $root 'artifacts/acceptance/man517'
        [System.IO.Directory]::CreateDirectory($duplicateResultsDirectory) | Out-Null
        $duplicateResultsFile = "duplicate-$([Guid]::NewGuid().ToString('N')).trx"
        $duplicateResults = Join-Path $duplicateResultsDirectory $duplicateResultsFile
        $redisProofFilter = 'FullyQualifiedName~Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres|FullyQualifiedName~Redis_cap_retry_converges_changed_v2_after_first_consumer_failure'
        Invoke-DotNet -Arguments @('test', $demandPlanningTestsProject, '--no-build', '--filter', $redisProofFilter, '--results-directory', $duplicateResultsDirectory, '--logger', "trx;LogFileName=$duplicateResultsFile") -WorkingDirectory $root -TimeoutSeconds 180 -Name 'man517-redis-reliability-probes' | Out-Null
        if (-not (Test-Path -LiteralPath $duplicateResults)) {
            throw 'MAN-517 identical-key Redis duplicate probe produced no TRX result.'
        }
        [xml]$duplicateTrx = Get-Content -LiteralPath $duplicateResults -Raw
        $expectedRedisProofs = @(
            '.Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres',
            '.Redis_cap_retry_converges_changed_v2_after_first_consumer_failure'
        )
        $redisProofExecutions = @($duplicateTrx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object {
            $testName = $_.GetAttribute('testName')
            $expectedRedisProofs | Where-Object { $testName.EndsWith($_, [StringComparison]::Ordinal) }
        })
        if ($redisProofExecutions.Count -ne 2 -or @($redisProofExecutions | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' }).Count -ne 0) {
            throw 'MAN-517 Redis duplicate and first-attempt-failure retry probes did not both execute exactly once and pass.'
        }
    }

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/cancel" -Headers $headers -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; reason = 'MAN-517 cancellation'
    } | Out-Null
    Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 4 -Quantity 0 -Status 'cancelled' | Out-Null
    $cancelled = Assert-DemandStable -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 4 -Quantity 0 -Status 'cancelled'

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
catch {
    $acceptanceFailure = $_
    try { Export-Man517FailureDiagnostics -FailureRecord $acceptanceFailure }
    catch { Write-Diagnostic -Level 'WARN' -Message "MAN-517 diagnostic export failed: $($_.Exception.Message)" }
    throw $acceptanceFailure
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
