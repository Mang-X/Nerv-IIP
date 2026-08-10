# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts a loopback-only disposable HTTP fixture process
#   Writes:
#     - artifacts/script-logs/man703-http-helper-tests/**
#   Cleanup:
#     - Stops the managed fixture process and removes its exact test directory
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-erp-sales-order-demand-planning.ps1'
$fixtureScript = Join-Path $repoRoot 'scripts/tests/fixtures/man703-http-fixture.ps1'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-Helper {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-RequestFailure {
    param(
        [scriptblock]$Request,
        [string[]]$ExpectedFragments,
        [string[]]$ForbiddenFragments = @(),
        [double]$MaximumSeconds = 5,
        [type]$ExpectedExceptionType
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $failure = $null
    $caughtException = $null
    try {
        & $Request | Out-Null
    }
    catch {
        $caughtException = $_.Exception
        $failure = $_.Exception.Message
    }
    finally {
        $stopwatch.Stop()
    }

    Assert-Helper (-not [string]::IsNullOrWhiteSpace($failure)) 'The request must fail.'
    if ($null -ne $ExpectedExceptionType) {
        Assert-Helper ($caughtException -is $ExpectedExceptionType) "Request failure must be $($ExpectedExceptionType.FullName). Actual: $($caughtException.GetType().FullName)"
    }
    Assert-Helper (
        $stopwatch.Elapsed.TotalSeconds -lt $MaximumSeconds
    ) "The request failure took $($stopwatch.Elapsed.TotalSeconds) seconds and exceeded $MaximumSeconds seconds."
    foreach ($fragment in $ExpectedFragments) {
        Assert-Helper ($failure.Contains($fragment)) "Request failure must contain '$fragment'. Actual: $failure"
    }
    foreach ($fragment in $ForbiddenFragments) {
        Assert-Helper (-not $failure.Contains($fragment)) "Request failure must redact '$fragment'."
    }
}

function Import-VerifyFunction {
    param(
        [System.Management.Automation.Language.ScriptBlockAst]$Ast,
        [string]$Name
    )

    $definition = $Ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $Name
    }, $true)
    if ($null -eq $definition) {
        throw "Verify script function '$Name' is missing."
    }
    Set-Item -Path "Function:\script:$Name" -Value $definition.Body.GetScriptBlock()
}

function Get-TestFreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $verifyScript,
    [ref] $tokens,
    [ref] $parseErrors)
Assert-Helper ($parseErrors.Count -eq 0) 'Verify script must parse before helper behavior is loaded.'
foreach ($functionName in @(
        'Protect-Man517DiagnosticText',
        'Get-Man517ExceptionSummary',
        'Get-Man517HttpClassification',
        'Get-Man517TransportClassification',
        'Invoke-Man517JsonRequest',
        'Invoke-JsonPost',
        'Wait-ErpSalesOrderReady',
        'Assert-DemandStable'
    )) {
    Import-VerifyFunction -Ast $ast -Name $functionName
}

$testRoot = Join-Path $repoRoot "artifacts/script-logs/man703-http-helper-tests/$([Guid]::NewGuid().ToString('N'))"
$readyFile = Join-Path $testRoot 'ready.txt'
$counterFile = Join-Path $testRoot 'sales-order-request-count.txt'
$mutationCounterFile = Join-Path $testRoot 'mutation-request-count.txt'
# 冷 handler 的模拟延迟必须明确高于旧的 5 秒默认预算，否则这条 RED/GREEN 什么都证明不了。
$coldMutationDelaySeconds = 7
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$port = Get-TestFreeTcpPort
$connectStallPort = Get-TestFreeTcpPort
$baseUrl = "http://127.0.0.1:$port"
$fixtureProcess = $null
$cleanupError = $null
$script:internalToken = 'test-internal-token-value'

try {
    $fixtureProcess = Start-ManagedBackgroundProcess `
        -Command 'pwsh' `
        -Arguments @(
            '-NoProfile',
            '-File',
            $fixtureScript,
            '-Port',
            "$port",
            '-ReadyFile',
            $readyFile,
            '-CounterFile',
            $counterFile,
            '-MutationCounterFile',
            $mutationCounterFile,
            '-ConnectStallPort',
            "$connectStallPort",
            '-ColdMutationDelaySeconds',
            "$coldMutationDelaySeconds") `
        -WorkingDirectory $testRoot `
        -Name 'man703-http-fixture' `
        -LogDirectory (Join-Path $testRoot 'fixture')

    $readyDeadline = (Get-Date).AddSeconds(10)
    while (-not (Test-Path -LiteralPath $readyFile)) {
        if ($fixtureProcess.Process.HasExited) {
            throw "HTTP fixture exited before readiness. Logs: $($fixtureProcess.LogDirectory)"
        }
        if ((Get-Date) -ge $readyDeadline) {
            throw "HTTP fixture did not become ready. Logs: $($fixtureProcess.LogDirectory)"
        }
        Start-Sleep -Milliseconds 25
    }

    $order = Wait-ErpSalesOrderReady `
        -ErpUrl $baseUrl `
        -Headers @{} `
        -TimeoutSeconds 5 `
        -PollIntervalMilliseconds 25
    Assert-Helper ($order.salesOrderNo -ceq 'SO-DEMO-001') 'ERP readiness must return the exact seeded sales order.'
    Assert-Helper ($order.status -ceq 'released') 'ERP readiness must require the released lifecycle state.'
    Assert-Helper ([decimal]$order.totalAmount -eq [decimal]200) 'ERP readiness must require the seeded total amount.'
    Assert-Helper ((Get-Content -LiteralPath $counterFile -Raw).Trim() -eq '2') 'ERP readiness must poll after an initially empty successful response.'

    # 预算和 stage 都必须显式传入。删掉隐式的 5 秒默认之后，「少写一个参数」
    # 只能得到一条明确的拒绝，而不是悄悄退回一个对冷 runner 过紧的预算。
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Post -Uri "$baseUrl/missing-success" -Headers @{} -Stage 'budget-required' -Body @{ value = 'ignored' }
    } -ExpectedFragments @('has no explicit budget', 'stage=budget-required', 'method=POST') -MaximumSeconds 2

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/missing-success" -Headers @{} -Stage 'budget-ambiguous' -TimeoutSeconds 5 -Deadline ((Get-Date).AddSeconds(5))
    } -ExpectedFragments @('budget is ambiguous', 'stage=budget-ambiguous') -MaximumSeconds 2

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/missing-success" -Headers @{} -TimeoutSeconds 5
    } -ExpectedFragments @('requires an explicit stage') -MaximumSeconds 2

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Post `
            -Uri "$baseUrl/business-error?token=uri-secret-value" `
            -Headers @{} `
            -Stage 'business-envelope' `
            -TimeoutSeconds 5 `
            -Body @{ value = 'ignored' }
    } `
        -ExpectedFragments @('classification=business', 'stage=business-envelope', 'method=POST', 'code=404', 'message=') `
        -ForbiddenFragments @('uri-secret-value', 'message-secret-value', 'classification=http') `
        -MaximumSeconds 2

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/http-error" -Headers @{} -Stage 'server-error' -TimeoutSeconds 5
    } -ExpectedFragments @('classification=http', 'stage=server-error', 'httpStatus=503') `
        -ForbiddenFragments @('classification=server-cancelled', 'classification=business')

    # 服务端主动取消（499）必须和真正的服务端错误分开：这是本 issue 里 CI 看到的那一类。
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Post -Uri "$baseUrl/server-cancelled" -Headers @{} -Stage 'server-cancel' -TimeoutSeconds 5 -Body @{ value = 'ignored' }
    } -ExpectedFragments @('request HTTP failure', 'classification=server-cancelled', 'stage=server-cancel', 'httpStatus=499') `
        -ForbiddenFragments @('classification=http', 'classification=deadline')

    # 请求已经完整发出、连接却在任何响应字节之前断开：属于 send，不属于 connect。
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/abort-after-request?token=abort-uri-secret" -Headers @{} -Stage 'transport-send' -TimeoutSeconds 5
    } -ExpectedFragments @('request transport failed', 'classification=send', 'stage=transport-send') `
        -ForbiddenFragments @('abort-uri-secret', 'classification=connect', 'deadline exceeded')

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/invalid-json" -Headers @{} -Stage 'invalid-json' -TimeoutSeconds 5
    } -ExpectedFragments @('valid JSON', 'classification=protocol', 'stage=invalid-json')

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/missing-success" -Headers @{} -Stage 'missing-success' -TimeoutSeconds 5
    } -ExpectedFragments @("missing boolean 'success'", 'classification=protocol', 'stage=missing-success')

    # RED/GREEN：这次状态变更在服务端要花 7 秒才回来。旧的 5 秒隐式默认会在服务端
    # 已经开始写事务之后取消它（服务端记 HTTP 499），新的显式有界预算必须让它跑完。
    $coldMutationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $coldMutation = Invoke-JsonPost `
        -Uri "$baseUrl/cold-mutation" `
        -Headers @{} `
        -Stage 'cold-mutation' `
        -Body @{ organizationId = 'org-001'; orderedQuantity = 4 }
    $coldMutationStopwatch.Stop()
    Assert-Helper ($coldMutation.success -eq $true) 'A slow but successful mutation must be observed as success.'
    Assert-Helper (
        $coldMutationStopwatch.Elapsed.TotalSeconds -ge ($coldMutationDelaySeconds - 1)
    ) "The mutation returned after $($coldMutationStopwatch.Elapsed.TotalSeconds) seconds; the fixture cannot have answered that fast."
    Assert-Helper (
        $coldMutationStopwatch.Elapsed.TotalSeconds -lt 60
    ) 'The mutation budget must stay bounded well inside its declared maximum.'
    Assert-Helper (
        (Get-Content -LiteralPath $mutationCounterFile -Raw).Trim() -eq '1'
    ) 'A successful mutation must reach the server exactly once.'

    # 状态变更失败之后绝不能重发：超时/失败之后提交结果是不确定的，重试就是重复写。
    Assert-RequestFailure -Request {
        Invoke-JsonPost `
            -Uri "$baseUrl/failing-mutation" `
            -Headers @{} `
            -Stage 'failing-mutation' `
            -Body @{ organizationId = 'org-001'; orderedQuantity = 5 }
    } -ExpectedFragments @('classification=business', 'stage=failing-mutation', 'method=POST', 'code=409')
    Assert-Helper (
        (Get-Content -LiteralPath $mutationCounterFile -Raw).Trim() -eq '2'
    ) 'A failed mutation must not be retried; the fixture must have seen exactly one more request.'

    # 有界性由共享请求路径强制：给一个明确的小预算，它必须在预算处放弃并归类为 deadline。
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Post `
            -Uri "$baseUrl/slow-trickle?token=bounded-mutation-uri-secret" `
            -Headers @{} `
            -Stage 'bounded-mutation' `
            -TimeoutSeconds 1 `
            -Body @{ organizationId = 'org-001' }
    } `
        -ExpectedFragments @('deadline exceeded', 'classification=deadline', 'stage=bounded-mutation', 'method=POST', 'budgetMs=') `
        -ForbiddenFragments @('bounded-mutation-uri-secret') `
        -MaximumSeconds 3 `
        -ExpectedExceptionType ([System.TimeoutException])

    $stableStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $stableDemand = Assert-DemandStable `
        -DemandPlanningUrl $baseUrl `
        -Headers @{} `
        -Version 4 `
        -Quantity 0 `
        -Status 'cancelled' `
        -Seconds 1
    $stableStopwatch.Stop()
    Assert-Helper ($stableDemand.sourceVersion -eq 4) 'Stable demand must retain the last successful observation when the final request reaches the window deadline.'
    Assert-Helper ($stableStopwatch.Elapsed.TotalSeconds -ge 0.8) 'Stable demand must observe the requested stability window instead of returning early.'
    Assert-Helper ($stableStopwatch.Elapsed.TotalSeconds -lt 3) 'Stable demand must finish near its deadline.'

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "$baseUrl/slow-trickle?token=slow-trickle-uri-secret" `
            -Headers @{} `
            -Stage 'slow-trickle' `
            -TimeoutSeconds 1
    } `
        -ExpectedFragments @('deadline exceeded', 'classification=deadline', 'stage=slow-trickle', 'method=GET', 'uri=') `
        -ForbiddenFragments @('slow-trickle-uri-secret') `
        -MaximumSeconds 3 `
        -ExpectedExceptionType ([System.TimeoutException])

    # 连不上（这里是 TLS 握手打不通）必须归为 connect，且绝不能被读成客户端预算耗尽。
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "https://127.0.0.1:$connectStallPort/connect-timeout?token=connect-timeout-uri-secret" `
            -Headers @{} `
            -Stage 'transport-connect' `
            -TimeoutSeconds 10
    } `
        -ExpectedFragments @('request transport failed', 'classification=connect', 'stage=transport-connect', 'method=GET', 'uri=') `
        -ForbiddenFragments @('deadline exceeded', 'classification=send', 'connect-timeout-uri-secret') `
        -MaximumSeconds 8

    $stalledHttpStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "$baseUrl/http-error-stalled-body?token=stalled-http-uri-secret" `
            -Headers @{} `
            -Stage 'stalled-http-error' `
            -TimeoutSeconds 2
    } `
        -ExpectedFragments @('request HTTP failure', 'classification=http', 'stage=stalled-http-error', 'httpStatus=503') `
        -ForbiddenFragments @('deadline exceeded', 'stalled-http-uri-secret') `
        -MaximumSeconds 1
    $stalledHttpStopwatch.Stop()
    Write-Host "503 stalled-body failure preserved after $($stalledHttpStopwatch.Elapsed.TotalMilliseconds) ms."

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "$baseUrl/half-open?token=half-open-uri-secret" `
            -Headers @{} `
            -Stage 'half-open' `
            -TimeoutSeconds 1
    } `
        -ExpectedFragments @('deadline exceeded', 'classification=deadline', 'stage=half-open', 'method=GET', 'uri=') `
        -ForbiddenFragments @('half-open-uri-secret') `
        -MaximumSeconds 3 `
        -ExpectedExceptionType ([System.TimeoutException])
}
finally {
    if ($null -ne $fixtureProcess) {
        try {
            $fixtureProcess.Stop.Invoke('MAN-703 helper test cleanup') | Out-Null
        }
        catch {
            $cleanupError = "Could not stop MAN-703 HTTP fixture cleanly: $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        try {
            Remove-Item -LiteralPath $testRoot -Recurse -Force
        }
        catch {
            $directoryCleanupError = "Could not remove MAN-703 HTTP fixture directory: $($_.Exception.Message)"
            $cleanupError = if ([string]::IsNullOrWhiteSpace($cleanupError)) {
                $directoryCleanupError
            } else {
                "$cleanupError; $directoryCleanupError"
            }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($cleanupError)) {
        throw $cleanupError
    }
}

Write-Host 'ERP sales-order DemandPlanning HTTP helper behavior tests passed.'
