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
#     - artifacts/acceptance/man517/cleanup-evidence.json
#     - artifacts/acceptance/man517/diagnostics/** on failure
#   Cleanup:
#     - Stops every managed service process in finally
#     - Drops the disposable PostgreSQL database in finally
#     - Stops only compose services started by this script
#     - Verifies every owned process, the exact database, and owned compose services are gone
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

function New-AcceptanceDatabase {
    param(
        [string]$ComposeFile,
        [string]$DatabaseName
    )

    $deadline = (Get-Date).AddSeconds(30)
    do {
        try {
            # pg_isready can briefly succeed while the container is still
            # restarting. Use TCP and check the reserved random name before
            # CREATE on every retry. If CREATE commits but the client loses its
            # response, the next existence check converges without a duplicate.
            $databaseExists = Invoke-NativeCommandOutput -Command 'docker' -Arguments @(
                'compose', '-f', $ComposeFile, 'exec', '-T', 'postgres',
                'psql', '-h', '127.0.0.1', '-U', 'nerv', '-d', 'postgres',
                '-X', '-tA', '-v', 'ON_ERROR_STOP=1', '-c', "SELECT 1 FROM pg_database WHERE datname = '$DatabaseName';"
            ) -WorkingDirectory $root -Name 'man517-check-database'
            if ([string]::Equals([string]("$($databaseExists.Stdout)".Trim()), [string]('1'), [StringComparison]::OrdinalIgnoreCase)) {
                return
            }

            Invoke-DockerCompose -Arguments @(
                '-f', $ComposeFile, 'exec', '-T', 'postgres',
                'psql', '-h', '127.0.0.1', '-U', 'nerv', '-d', 'postgres',
                '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $DatabaseName;"
            ) -WorkingDirectory $root -Name 'man517-create-database' | Out-Null
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
            if ([string]::Equals([string]((Invoke-RestMethod -Method Get -Uri $Uri)), [string]('Healthy'), [StringComparison]::OrdinalIgnoreCase)) { return }
        }
        catch { Start-Sleep -Milliseconds 500 }
    } while ((Get-Date) -lt $deadline)
    throw "Service did not become healthy at $Uri. Logs: $($ManagedProcess.LogDirectory)"
}

function Get-Man517ExceptionSummary {
    param([AllowNull()][System.Exception]$Exception)
    $messages = [System.Collections.Generic.List[string]]::new()
    $current = $Exception
    while ($null -ne $current -and $messages.Count -lt 4) {
        $messages.Add("$($current.GetType().Name): $($current.Message)")
        $current = $current.InnerException
    }
    return ($messages -join ' <- ')
}

function Get-Man517HttpClassification {
    param([int]$HttpStatus)
    # 499 是服务端主动放弃这次请求（客户端预算过紧时 ASP.NET Core 就是这样记账的），
    # 必须和真正的服务端错误分开，否则「我们自己把它取消了」会被读成「服务端坏了」。
    if ($HttpStatus -eq 499) {
        return 'server-cancelled'
    }
    return 'http'
}

function Get-Man517TransportClassification {
    param(
        [AllowNull()][System.Exception]$Exception,
        [switch]$Cancelled
    )

    # 传输失败必须分成「连不上」和「连上之后失败」两类，否则冷 runner 上的
    # 首次连接问题和请求发出后的中断在 CI 日志里长得一模一样。
    # HttpClient.Timeout 保持无限，所以 handler 自己拥有的超时只有
    # SocketsHttpHandler.ConnectTimeout；它以 TaskCanceledException 抛出、内层是
    # TimeoutException，这正是 -Cancelled 分支要认的形状（本机 pwsh 7.6/.NET 10 实测）。
    $connectRequestErrors = @('ConnectionError', 'NameResolutionError', 'SecureConnectionError', 'ProxyTunnelError')
    $current = $Exception
    while ($null -ne $current) {
        if ($current -is [System.Net.Http.HttpRequestException]) {
            $requestError = "$($current.HttpRequestError)"
            foreach ($connectRequestError in $connectRequestErrors) {
                if ([string]::Equals($connectRequestError, $requestError, [StringComparison]::Ordinal)) {
                    return 'connect'
                }
            }
            return 'send'
        }
        if ($current -is [System.Net.Sockets.SocketException]) {
            return 'connect'
        }
        if ($Cancelled.IsPresent -and $current -is [System.TimeoutException]) {
            return 'connect'
        }
        $current = $current.InnerException
    }
    return 'send'
}

function Invoke-Man517JsonRequest {
    param(
        [ValidateSet('Get', 'Post')]
        [string]$Method,
        [string]$Uri,
        [hashtable]$Headers,
        [AllowNull()][hashtable]$Body,
        [string]$Stage,
        [ValidateRange(1, 300)]
        [int]$TimeoutSeconds,
        [datetime]$Deadline
    )

    if ([string]::IsNullOrWhiteSpace($Stage)) {
        throw 'MAN-517 request requires an explicit stage so every failure names the acceptance step it belongs to.'
    }
    $safeStage = Protect-Man517DiagnosticText -Text $Stage
    $safeMethod = Protect-Man517DiagnosticText -Text $Method.ToUpperInvariant()
    $safeUri = Protect-Man517DiagnosticText -Text $Uri
    # 这里没有隐式预算：轮询查询传入自己的绝对 -Deadline，状态变更由 Invoke-JsonPost
    # 传入一次性有界 -TimeoutSeconds。此前的 5 秒隐式默认会在冷 CI runner 上取消
    # 已经进入 ERP handler 的 POST（服务端记 HTTP 499、v2 事件不发布），因此默认值被删除
    # 而不是调大：任何新调用点都必须自己说明预算。
    $hasDeadline = $PSBoundParameters.ContainsKey('Deadline')
    $hasTimeout = $PSBoundParameters.ContainsKey('TimeoutSeconds')
    if ($hasDeadline -and $hasTimeout) {
        throw "MAN-517 request budget is ambiguous: stage=$safeStage method=$safeMethod uri=$safeUri; pass either -Deadline or -TimeoutSeconds."
    }
    if (-not $hasDeadline -and -not $hasTimeout) {
        throw "MAN-517 request has no explicit budget: stage=$safeStage method=$safeMethod uri=$safeUri; pass -Deadline for a polled query or -TimeoutSeconds for a single-shot mutation."
    }
    $effectiveDeadline = if ($hasDeadline) { $Deadline } else { (Get-Date).AddSeconds($TimeoutSeconds) }
    $requestStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $remaining = $effectiveDeadline - (Get-Date)
    $budgetMilliseconds = [int][Math]::Max(0, [Math]::Round($remaining.TotalMilliseconds))
    $requestContext = "stage=$safeStage method=$safeMethod uri=$safeUri budgetMs=$budgetMilliseconds"
    if ($remaining -le [TimeSpan]::Zero) {
        throw [System.TimeoutException]::new(
            "MAN-517 request deadline exceeded: classification=deadline $requestContext elapsedMs=0")
    }

    $handler = $null
    $client = $null
    $requestMessage = $null
    $responseMessage = $null
    $deadlineCancellation = $null
    $httpStatus = $null
    $responseContent = $null
    try {
        $deadlineCancellation = [System.Threading.CancellationTokenSource]::new($remaining)
        $handler = [System.Net.Http.SocketsHttpHandler]::new()
        $handler.ConnectTimeout = [TimeSpan]::FromSeconds(5)
        $client = [System.Net.Http.HttpClient]::new($handler, $true)
        $client.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan
        $requestMessage = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::new($Method.ToUpperInvariant()),
            $Uri)
        foreach ($header in $Headers.GetEnumerator()) {
            if (-not $requestMessage.Headers.TryAddWithoutValidation([string]$header.Key, [string]$header.Value)) {
                throw "MAN-517 request header was rejected: classification=protocol $requestContext header=$($header.Key)"
            }
        }
        if ($PSBoundParameters.ContainsKey('Body')) {
            $requestMessage.Content = [System.Net.Http.StringContent]::new(
                ($Body | ConvertTo-Json -Depth 12),
                [System.Text.Encoding]::UTF8,
                'application/json')
        }

        $responseMessage = $client.SendAsync(
            $requestMessage,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead,
            $deadlineCancellation.Token).GetAwaiter().GetResult()
        $httpStatus = [int]$responseMessage.StatusCode
        if ($httpStatus -lt 200 -or $httpStatus -ge 300) {
            throw "MAN-517 request HTTP failure: classification=$(Get-Man517HttpClassification -HttpStatus $httpStatus) $requestContext httpStatus=$httpStatus elapsedMs=$($requestStopwatch.ElapsedMilliseconds)"
        }
        $responseContent = $responseMessage.Content.ReadAsStringAsync(
            $deadlineCancellation.Token).GetAwaiter().GetResult()
    }
    catch [System.OperationCanceledException] {
        if (($null -ne $deadlineCancellation -and $deadlineCancellation.IsCancellationRequested) -or
            (Get-Date) -ge $effectiveDeadline) {
            throw [System.TimeoutException]::new(
                "MAN-517 request deadline exceeded: classification=deadline $requestContext elapsedMs=$($requestStopwatch.ElapsedMilliseconds)",
                $_.Exception)
        }
        $safeError = Protect-Man517DiagnosticText -Text (Get-Man517ExceptionSummary -Exception $_.Exception)
        throw "MAN-517 request transport failed: classification=$(Get-Man517TransportClassification -Exception $_.Exception -Cancelled) $requestContext elapsedMs=$($requestStopwatch.ElapsedMilliseconds) error=$safeError"
    }
    catch {
        # try 块内抛出的 MAN-517 字符串已经是最终的、脱敏过的、带 classification 的消息，
        # 原样传出去，避免被重新包装成 transport 失败而丢掉分类。
        if ($_.Exception.Message.StartsWith('MAN-517 ', [StringComparison]::Ordinal)) {
            throw $_
        }
        if ($null -ne $httpStatus -and ($httpStatus -lt 200 -or $httpStatus -ge 300)) {
            throw "MAN-517 request HTTP failure: classification=$(Get-Man517HttpClassification -HttpStatus $httpStatus) $requestContext httpStatus=$httpStatus elapsedMs=$($requestStopwatch.ElapsedMilliseconds)"
        }
        $safeError = Protect-Man517DiagnosticText -Text (Get-Man517ExceptionSummary -Exception $_.Exception)
        throw "MAN-517 request transport failed: classification=$(Get-Man517TransportClassification -Exception $_.Exception) $requestContext elapsedMs=$($requestStopwatch.ElapsedMilliseconds) error=$safeError"
    }
    finally {
        if ($null -ne $responseMessage) {
            $responseMessage.Dispose()
        }
        if ($null -ne $requestMessage) {
            $requestMessage.Dispose()
        }
        if ($null -ne $client) {
            $client.Dispose()
        }
        if ($null -ne $deadlineCancellation) {
            $deadlineCancellation.Dispose()
        }
    }

    try {
        $response = "$responseContent" | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "MAN-517 request did not return valid JSON: classification=protocol $requestContext httpStatus=$httpStatus"
    }

    $successProperty = $response.PSObject.Properties['success']
    if ($null -eq $successProperty -or $successProperty.Value -isnot [bool]) {
        throw "MAN-517 response is missing boolean 'success': classification=protocol $requestContext httpStatus=$httpStatus"
    }
    if (-not $successProperty.Value) {
        $codeProperty = $response.PSObject.Properties['code']
        $messageProperty = $response.PSObject.Properties['message']
        $safeCode = Protect-Man517DiagnosticText -Text $(if ($null -eq $codeProperty) { 'missing' } else { "$($codeProperty.Value)" })
        $safeMessage = Protect-Man517DiagnosticText -Text $(if ($null -eq $messageProperty) { 'missing' } else { "$($messageProperty.Value)" })
        throw "MAN-517 business request failed: classification=business $requestContext httpStatus=$httpStatus code=$safeCode message=$safeMessage"
    }

    return $response
}

function Invoke-JsonPost {
    param(
        [string]$Uri,
        [hashtable]$Body,
        [hashtable]$Headers,
        [string]$Stage,
        # 冷 CI runner 上，首次进入 ERP change-line handler 要付 JIT、EF 首次查询编译和
        # CAP outbox 首次发布的钱；5 秒预算会在服务端已经开始写事务之后取消请求（HTTP 499）。
        # 预算因此放宽到一个明确、有界、单次的值——不是无限等待，也不允许回到 5 秒。
        [ValidateRange(60, 180)]
        [int]$MutationTimeoutSeconds = 90
    )

    # 状态变更只发一次。POST 超时之后提交结果是不确定的，重试就是重复写；
    # 这里不允许出现任何循环或重试包装，收敛只能靠后面的查询轮询。
    Invoke-Man517JsonRequest -Method Post -Uri $Uri -Headers $Headers -Body $Body -Stage $Stage -TimeoutSeconds $MutationTimeoutSeconds
}

function Wait-ErpSalesOrderReady {
    param(
        [string]$ErpUrl,
        [hashtable]$Headers,
        [int]$TimeoutSeconds = 90,
        [int]$PollIntervalMilliseconds = 500
    )

    $keyword = [Uri]::EscapeDataString('SO-DEMO-001')
    $uri = "$ErpUrl/api/business/v1/erp/sales-orders?organizationId=org-001&environmentId=env-dev&status=released&keyword=$keyword&skip=0&take=10"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastObservedOrder = $null
    do {
        if ((Get-Date) -ge $deadline) {
            break
        }
        try {
            $response = Invoke-Man517JsonRequest -Method Get -Uri $uri -Headers $Headers -Stage 'erp-sales-order-readiness-query' -Deadline $deadline
        }
        catch [System.TimeoutException] {
            if ((Get-Date) -lt $deadline) {
                throw
            }
            break
        }
        $rows = @($response.data.items | Where-Object { [string]::Equals([string]($_.salesOrderNo), [string]('SO-DEMO-001'), [StringComparison]::Ordinal) })
        $lastObservedOrder = if ($rows.Count -eq 1) {
            [ordered]@{
                salesOrderNo = $rows[0].salesOrderNo
                status = $rows[0].status
                totalAmount = $rows[0].totalAmount
            }
        } else {
            [ordered]@{ matchingRowCount = $rows.Count }
        }
        if ($rows.Count -eq 1 -and
            [string]::Equals([string]("$($rows[0].status)"), [string]('released'), [StringComparison]::OrdinalIgnoreCase) -and
            [decimal]$rows[0].totalAmount -eq 200) {
            return $rows[0]
        }
        $remainingMilliseconds = [int][Math]::Floor(($deadline - (Get-Date)).TotalMilliseconds)
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds ([Math]::Min($PollIntervalMilliseconds, $remainingMilliseconds))
        }
    } while ((Get-Date) -lt $deadline)

    $safeObservation = Protect-Man517DiagnosticText -Text ($lastObservedOrder | ConvertTo-Json -Depth 4 -Compress)
    throw "ERP sales order SO-DEMO-001 did not become query-visible as released with totalAmount=200. Last observation: $safeObservation"
}

function Wait-Demand {
    param([string]$DemandPlanningUrl, [hashtable]$Headers, [int]$Version, [decimal]$Quantity, [string]$Status)
    # The acceptance profile uses a 30-second fallback lookback and a two-second
    # failed-message scan interval. Keep enough scheduling slack for that path.
    $deadline = (Get-Date).AddSeconds(90)
    $lastHttpStatus = $null
    $lastResponseBody = $null
    $lastRequestException = $null
    $lastObservedDemand = $null
    do {
        if ((Get-Date) -ge $deadline) {
            break
        }
        try {
            $response = Invoke-Man517JsonRequest -Method Get -Uri "$DemandPlanningUrl/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev" -Headers $Headers -Stage "demand-convergence-query-v$Version" -Deadline $deadline
        }
        catch [System.TimeoutException] {
            if ((Get-Date) -lt $deadline) {
                throw
            }
            break
        }
        $lastHttpStatus = 200
        $fullResponseBody = $response | ConvertTo-Json -Depth 12 -Compress
        $lastResponseBody = if ($fullResponseBody.Length -gt 8192) { $fullResponseBody.Substring(0, 8192) } else { $fullResponseBody }
        $lastRequestException = $null
        $rows = @($response.data | Where-Object { [string]::Equals([string]($_.sourceReference), [string]('SO-DEMO-001'), [StringComparison]::OrdinalIgnoreCase) })
        $lastObservedDemand = if ($rows.Count -eq 1) {
            [ordered]@{ version = $rows[0].sourceVersion; quantity = $rows[0].quantity; status = $rows[0].sourceStatus }
        } else {
            [ordered]@{ matchingRowCount = $rows.Count }
        }
        if ($rows.Count -eq 1 -and $rows[0].sourceVersion -eq $Version -and (([decimal]$rows[0].quantity) -eq ($Quantity)) -and [string]::Equals([string]$rows[0].sourceStatus, $Status, [StringComparison]::Ordinal)) {
            return $rows[0]
        }
        $remainingMilliseconds = [int][Math]::Floor(($deadline - (Get-Date)).TotalMilliseconds)
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds ([Math]::Min(500, $remainingMilliseconds))
        }
    } while ((Get-Date) -lt $deadline)
    $lastObservation = [ordered]@{
        lastHttpStatus = $lastHttpStatus
        lastResponseBody = $lastResponseBody
        lastRequestException = $lastRequestException
        lastObservedDemand = $lastObservedDemand
    } | ConvertTo-Json -Depth 8 -Compress
    $safeLastObservation = Protect-Man517DiagnosticText -Text $lastObservation
    throw "Demand SO-DEMO-001 did not converge to version=$Version quantity=$Quantity status=$Status. Last observation: $safeLastObservation"
}

function Assert-DemandStable {
    param([string]$DemandPlanningUrl, [hashtable]$Headers, [int]$Version, [decimal]$Quantity, [string]$Status, [int]$Seconds = 5)
    $deadline = (Get-Date).AddSeconds($Seconds)
    $row = $null
    do {
        if ((Get-Date) -ge $deadline) {
            break
        }
        try {
            $response = Invoke-Man517JsonRequest -Method Get -Uri "$DemandPlanningUrl/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev" -Headers $Headers -Stage 'demand-stability-window-query' -Deadline $deadline
        }
        catch [System.TimeoutException] {
            if ((Get-Date) -lt $deadline -or $null -eq $row) {
                throw
            }
            break
        }
        $rows = @($response.data | Where-Object { [string]::Equals([string]($_.sourceReference), [string]('SO-DEMO-001'), [StringComparison]::OrdinalIgnoreCase) })
        if ($rows.Count -ne 1 -or $rows[0].sourceVersion -ne $Version -or (-not (([decimal]$rows[0].quantity) -eq ($Quantity))) -or -not [string]::Equals([string]$rows[0].sourceStatus, $Status, [StringComparison]::Ordinal)) {
            throw "Demand SO-DEMO-001 changed during the stability window; expected version=$Version quantity=$Quantity status=$Status."
        }
        $row = $rows[0]
        $remainingMilliseconds = [int][Math]::Floor(($deadline - (Get-Date)).TotalMilliseconds)
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds ([Math]::Min(500, $remainingMilliseconds))
        }
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
    return $safe
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

function Get-Man517TrxCounter {
    param([System.Xml.XmlElement]$Counters, [string]$Name)
    $raw = $Counters.GetAttribute($Name)
    $value = 0
    if (-not [int]::TryParse($raw, [ref]$value)) {
        throw "MAN-517 TRX counter '$Name' is not an integer; exact test accounting cannot be proven."
    }
    return $value
}

function Get-Man517RemainingProcessNames {
    param([object[]]$Descriptors)
    $remaining = [System.Collections.Generic.List[string]]::new()
    foreach ($descriptor in $Descriptors) {
        $existing = Get-Process -Id $descriptor.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $existing) { continue }
        # PID 会被复用，所以「还有同号进程」不等于「我们的进程还活着」。
        # 用启动时间确认身份，避免清理证据出现假阳性。
        $startTime = $null
        try { $startTime = $existing.StartTime }
        catch { continue }
        if ($startTime -eq $descriptor.StartTime) {
            $remaining.Add($descriptor.Name)
        }
    }
    return $remaining.ToArray()
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
    # CAP appends Cap:Version to the [CapSubscribe] group. This exact shape is
    # verified by the preceding XINFO GROUPS output on the real Redis transport.
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
$startedPostgres = (-not [Collections.Generic.HashSet[string]]::new([string[]]@($running), [StringComparer]::OrdinalIgnoreCase).Contains([string]('postgres')))
$startedRedis = (-not [Collections.Generic.HashSet[string]]::new([string[]]@($running), [StringComparer]::OrdinalIgnoreCase).Contains([string]('redis')))
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
$acceptanceFailure = $null
$cleanupFailures = [System.Collections.Generic.List[string]]::new()
# 清理证据按「这次运行拥有的东西」逐项记账：托管进程按 pid+启动时间确认身份，
# 数据库按精确名字，容器只算本脚本启动的那几个。
$ownedProcesses = [System.Collections.Generic.List[object]]::new()
$fullChainProbeCounters = $null

$masterDataProject = Join-Path $root 'backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj'
$erpProject = Join-Path $root 'backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj'
$demandPlanningProject = Join-Path $root 'backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj'
$probeProject = Join-Path $root 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'

try {
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $root -Name 'man517-infrastructure-up' | Out-Null
    Wait-PostgresReady -ComposeFile $composeFile
    # This random database name is reserved by this run. Record cleanup intent
    # before the first SQL attempt because the server may commit CREATE and the
    # client can still lose the response; the idempotent helper then safely retries.
    $databaseCreated = $true
    New-AcceptanceDatabase -ComposeFile $composeFile -DatabaseName $databaseName

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
        Cap__FailedRetryInterval = '2'
        Cap__FallbackWindowLookbackSeconds = '30'
        InternalService__BearerToken = $internalToken
    }

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $masterDataUrl }) -ScriptBlock {
        $script:masterDataProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $masterDataProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-masterdata'
    }
    $ownedProcesses.Add([pscustomobject]@{ Name = 'masterdata'; ProcessId = $masterDataProcess.ProcessId; StartTime = $masterDataProcess.Process.StartTime })
    Wait-Healthy -Uri "$masterDataUrl/health" -ManagedProcess $masterDataProcess

    Invoke-WithScopedEnvironment -Variables ($commonEnvironment + @{ ASPNETCORE_URLS = $demandPlanningUrl }) -ScriptBlock {
        $script:demandPlanningProcess = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('run', '--project', $demandPlanningProject, '--no-build', '--no-launch-profile') -WorkingDirectory $root -Name 'man517-demand-planning'
    }
    $ownedProcesses.Add([pscustomobject]@{ Name = 'demand-planning'; ProcessId = $demandPlanningProcess.ProcessId; StartTime = $demandPlanningProcess.Process.StartTime })
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
    $ownedProcesses.Add([pscustomobject]@{ Name = 'erp'; ProcessId = $erpProcess.ProcessId; StartTime = $erpProcess.Process.StartTime })
    Wait-Healthy -Uri "$erpUrl/health" -ManagedProcess $erpProcess

    $headers = @{
        Authorization = "Bearer $internalToken"
        'X-Correlation-Id' = 'corr-man517-cross-process'
        'X-Causation-Id' = 'acceptance-script'
        'X-Authenticated-Actor' = 'user:planner-demo'
    }
    $erpSalesOrder = Wait-ErpSalesOrderReady -ErpUrl $erpUrl -Headers $headers
    $released = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 1 -Quantity 2 -Status 'active'

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10" -Headers $headers -Stage 'erp-change-line-v2' -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; lineNo = '10'; orderedQuantity = 4; unitPrice = 100; requiredDate = '2026-08-15'; reason = 'MAN-517 change v2'
    } | Out-Null
    $changedV2 = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 2 -Quantity 4 -Status 'active'
    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10" -Headers $headers -Stage 'erp-change-line-v3' -Body @{
        organizationId = 'org-001'; environmentId = 'env-dev'; salesOrderNo = 'SO-DEMO-001'; lineNo = '10'; orderedQuantity = 5; unitPrice = 100; requiredDate = '2026-08-15'; reason = 'MAN-517 change v3'
    } | Out-Null
    $changedV3 = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 3 -Quantity 5 -Status 'active'

    Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_TEST_POSTGRES = $databaseConnectionString
        NERV_IIP_TEST_REDIS = $RedisConnectionString
        NERV_IIP_TEST_CAP_VERSION = $capVersion
        NERV_IIP_TEST_PROBE_RUN_ID = [Guid]::NewGuid().ToString('N')
    } -ScriptBlock {
        $probeResultsDirectory = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY)) { Join-Path $root 'artifacts/acceptance/man517' } else { $env:NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY }
        [System.IO.Directory]::CreateDirectory($probeResultsDirectory) | Out-Null
        $probeResultsFile = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULL_CHAIN_RESULT_FILE)) { "probe-$([Guid]::NewGuid().ToString('N')).trx" } else { $env:NERV_IIP_FULL_CHAIN_RESULT_FILE }
        $probeResults = Join-Path $probeResultsDirectory $probeResultsFile
        Invoke-DotNet -Arguments @('test', $probeProject, '--no-build', '--filter', 'FullyQualifiedName~External_process_injects_duplicate_and_out_of_order_sales_order_events', '--results-directory', $probeResultsDirectory, '--logger', "trx;LogFileName=$probeResultsFile") -WorkingDirectory $root -TimeoutSeconds 180 -Name 'man517-out-of-order-probe' | Out-Null
        if (-not (Test-Path -LiteralPath $probeResults)) {
            throw 'MAN-517 fault-injection probe produced no TRX result; the selected test may be absent from a stale build.'
        }
        [xml]$probeTrx = Get-Content -LiteralPath $probeResults -Raw
        $probeExecutions = @($probeTrx.SelectNodes("//*[local-name()='UnitTestResult']") | Where-Object { $_.GetAttribute('testName').EndsWith('.External_process_injects_duplicate_and_out_of_order_sales_order_events', [StringComparison]::Ordinal) })
        if ($probeExecutions.Count -ne 1 -or (-not [string]::Equals([string]($probeExecutions[0].GetAttribute('outcome')), [string]('Passed'), [StringComparison]::OrdinalIgnoreCase))) {
            throw 'MAN-517 fault-injection probe did not execute exactly once and pass.'
        }
        # 「按名字找到一条 Passed」还不足以排除同一次 run 里另有失败或被跳过的用例，
        # 因此把 TRX 的整体计数也钉死为 executed=1/passed=1/failed=0/skipped=0。
        $probeCounters = $probeTrx.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $probeCounters) {
            throw 'MAN-517 fault-injection probe TRX has no Counters element; exact executed/passed/failed/skipped accounting cannot be proven.'
        }
        $probeTotal = Get-Man517TrxCounter -Counters $probeCounters -Name 'total'
        $probeExecuted = Get-Man517TrxCounter -Counters $probeCounters -Name 'executed'
        $script:fullChainProbeCounters = [ordered]@{
            total = $probeTotal
            executed = $probeExecuted
            passed = Get-Man517TrxCounter -Counters $probeCounters -Name 'passed'
            failed = Get-Man517TrxCounter -Counters $probeCounters -Name 'failed'
            skipped = $probeTotal - $probeExecuted
        }
        if ($script:fullChainProbeCounters.total -ne 1 -or
            $script:fullChainProbeCounters.executed -ne 1 -or
            $script:fullChainProbeCounters.passed -ne 1 -or
            $script:fullChainProbeCounters.failed -ne 0 -or
            $script:fullChainProbeCounters.skipped -ne 0) {
            $observedCounters = $script:fullChainProbeCounters | ConvertTo-Json -Depth 4 -Compress
            throw "MAN-517 fault-injection probe must report executed=1 passed=1 failed=0 skipped=0. Actual: $observedCounters"
        }
    }
    $outOfOrder = Wait-Demand -DemandPlanningUrl $demandPlanningUrl -Headers $headers -Version 3 -Quantity 5 -Status 'active' # out-of-order v2 and duplicate v3 must not regress
    $duplicateReplay = $outOfOrder # probes above and below exercise duplicate delivery through the real Redis transport

    Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/cancel" -Headers $headers -Stage 'erp-cancel-order' -Body @{
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
        fullChainProbeCounters = $fullChainProbeCounters
        checkpoints = @{ erpSalesOrder = $erpSalesOrder; released = $released; duplicateReplay = $duplicateReplay; changedV2 = $changedV2; changedV3 = $changedV3; outOfOrder = $outOfOrder; cancelled = $cancelled }
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $evidencePath -Encoding utf8
    Write-Host "MAN-517 separate-process PostgreSQL + Redis acceptance passed. Evidence: $evidencePath"
}
catch {
    $acceptanceFailure = $_
    try { Export-Man517FailureDiagnostics -FailureRecord $acceptanceFailure }
    catch { Write-Diagnostic -Level 'WARN' -Message "MAN-517 diagnostic export failed: $($_.Exception.Message)" }
}
finally {
    # 剩余量默认按 0 初始化，任何一项复核失败都必须显式写进 $cleanupFailures，
    # 而不是让证据文件因为变量未定义而写不出来。
    $remainingProcessNames = @()
    $remainingDatabases = 0
    $remainingOwnedServices = @()
    if ($demandPlanningProcess) {
        try { $demandPlanningProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
        catch { $cleanupFailures.Add("demand-planning process: $($_.Exception.Message)") }
    }
    if ($erpProcess) {
        try { $erpProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
        catch { $cleanupFailures.Add("erp process: $($_.Exception.Message)") }
    }
    if ($masterDataProcess) {
        try { $masterDataProcess.Stop.Invoke('MAN-517 verification cleanup') | Out-Null }
        catch { $cleanupFailures.Add("master-data process: $($_.Exception.Message)") }
    }
    # 停止请求返回不等于进程没了；逐个按 pid + 启动时间复核，剩余必须为 0。
    try {
        $remainingProcessNames = @(Get-Man517RemainingProcessNames -Descriptors $ownedProcesses.ToArray())
        if ($remainingProcessNames.Count -gt 0) {
            $cleanupFailures.Add("managed processes still running: $($remainingProcessNames -join ', ')")
        }
    }
    catch { $cleanupFailures.Add("process cleanup verification: $($_.Exception.Message)") }
    if ($databaseCreated) {
        try {
            Invoke-DockerCompose -Arguments @('-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE IF EXISTS $databaseName WITH (FORCE);") -WorkingDirectory $root -Name 'man517-drop-database' | Out-Null
        }
        catch { $cleanupFailures.Add("database: $($_.Exception.Message)") }
        # 只复核这次运行创建的那个随机库名，绝不扫描或触碰同一台 PostgreSQL 上的其他库。
        try {
            $remainingDatabaseResult = Invoke-NativeCommandOutput -Command 'docker' -Arguments @(
                'compose', '-f', $composeFile, 'exec', '-T', 'postgres',
                'psql', '-h', '127.0.0.1', '-U', 'nerv', '-d', 'postgres',
                '-X', '-tA', '-v', 'ON_ERROR_STOP=1', '-c', "SELECT count(*) FROM pg_database WHERE datname = '$databaseName';"
            ) -WorkingDirectory $root -Name 'man517-verify-database-dropped'
            $parsedRemainingDatabases = 0
            if (-not [int]::TryParse("$($remainingDatabaseResult.Stdout)".Trim(), [ref]$parsedRemainingDatabases)) {
                $cleanupFailures.Add('database cleanup verification returned no countable result.')
            }
            else {
                $remainingDatabases = $parsedRemainingDatabases
                if ($parsedRemainingDatabases -ne 0) {
                    $cleanupFailures.Add("disposable database still present: $databaseName")
                }
            }
        }
        catch { $cleanupFailures.Add("database cleanup verification: $($_.Exception.Message)") }
    }
    $servicesToStop = @()
    if ($startedPostgres) { $servicesToStop += 'postgres' }
    if ($startedRedis) { $servicesToStop += 'redis' }
    if ($servicesToStop.Count -gt 0) {
        try {
            Invoke-DockerCompose -Arguments (@('-f', $composeFile, 'stop') + $servicesToStop) -WorkingDirectory $root -Name 'man517-infrastructure-stop' | Out-Null
        }
        catch { $cleanupFailures.Add("infrastructure: $($_.Exception.Message)") }
        # 只对本脚本启动的服务记账；脚本运行前就在跑的基础设施不属于这次运行，也不许被算进来。
        try {
            $stillRunningResult = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $root -Name 'man517-verify-infrastructure-stopped'
            $stillRunning = @("$($stillRunningResult.Stdout)" -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
            $remainingOwnedServices = @($servicesToStop | Where-Object { $stillRunning -contains $_ })
            if ($remainingOwnedServices.Count -gt 0) {
                $cleanupFailures.Add("script-owned compose services still running: $($remainingOwnedServices -join ', ')")
            }
        }
        catch { $cleanupFailures.Add("infrastructure cleanup verification: $($_.Exception.Message)") }
    }
    try {
        $cleanupEvidencePath = Join-Path $root 'artifacts/acceptance/man517/cleanup-evidence.json'
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $cleanupEvidencePath)) | Out-Null
        @{
            scenario = 'MAN-517 cleanup accounting'
            completedAtUtc = [DateTimeOffset]::UtcNow
            managedProcesses = @{
                owned = @($ownedProcesses | ForEach-Object { @{ name = $_.Name; processId = $_.ProcessId } })
                remaining = $remainingProcessNames.Count
                remainingNames = $remainingProcessNames
            }
            disposableDatabase = @{
                owned = $databaseCreated
                name = $databaseName
                remaining = $remainingDatabases
            }
            composeServices = @{
                owned = $servicesToStop
                remaining = $remainingOwnedServices.Count
                remainingNames = $remainingOwnedServices
            }
            cleanupFailures = @($cleanupFailures | ForEach-Object { Protect-ScriptAutomationText -Text $_ })
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cleanupEvidencePath -Encoding utf8
    }
    catch { $cleanupFailures.Add("cleanup evidence: $($_.Exception.Message)") }
}

if ($cleanupFailures.Count -gt 0) {
    $cleanupSummary = @($cleanupFailures | ForEach-Object { Protect-ScriptAutomationText -Text $_ }) -join '; '
    if ($null -ne $acceptanceFailure) {
        Write-Diagnostic -Level 'WARN' -Message "Original acceptance failure preserved; cleanup also failed: $cleanupSummary"
    }
    else {
        throw "MAN-517 cleanup failed: $cleanupSummary"
    }
}

if ($null -ne $acceptanceFailure) {
    throw $acceptanceFailure
}
