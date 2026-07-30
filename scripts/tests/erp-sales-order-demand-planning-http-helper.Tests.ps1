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
        'Invoke-Man517JsonRequest',
        'Wait-ErpSalesOrderReady',
        'Assert-DemandStable'
    )) {
    Import-VerifyFunction -Ast $ast -Name $functionName
}

$testRoot = Join-Path $repoRoot "artifacts/script-logs/man703-http-helper-tests/$([Guid]::NewGuid().ToString('N'))"
$readyFile = Join-Path $testRoot 'ready.txt'
$counterFile = Join-Path $testRoot 'sales-order-request-count.txt'
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
            '-ConnectStallPort',
            "$connectStallPort") `
        -WorkingDirectory $repoRoot `
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

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Post `
            -Uri "$baseUrl/business-error?token=uri-secret-value" `
            -Headers @{} `
            -Body @{ value = 'ignored' }
    } `
        -ExpectedFragments @('method=POST', 'code=404', 'message=') `
        -ForbiddenFragments @('uri-secret-value', 'message-secret-value') `
        -MaximumSeconds 2

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/http-error" -Headers @{}
    } -ExpectedFragments @('httpStatus=503')

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/invalid-json" -Headers @{}
    } -ExpectedFragments @('valid JSON')

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/missing-success" -Headers @{}
    } -ExpectedFragments @("missing boolean 'success'")

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
            -TimeoutSeconds 1
    } `
        -ExpectedFragments @('deadline exceeded', 'method=GET', 'uri=') `
        -ForbiddenFragments @('slow-trickle-uri-secret') `
        -MaximumSeconds 3 `
        -ExpectedExceptionType ([System.TimeoutException])

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "https://127.0.0.1:$connectStallPort/connect-timeout?token=connect-timeout-uri-secret" `
            -Headers @{} `
            -TimeoutSeconds 10
    } `
        -ExpectedFragments @('request transport failed', 'method=GET', 'uri=') `
        -ForbiddenFragments @('deadline exceeded', 'connect-timeout-uri-secret') `
        -MaximumSeconds 8

    $stalledHttpStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "$baseUrl/http-error-stalled-body?token=stalled-http-uri-secret" `
            -Headers @{} `
            -TimeoutSeconds 2
    } `
        -ExpectedFragments @('request HTTP failure', 'httpStatus=503') `
        -ForbiddenFragments @('deadline exceeded', 'stalled-http-uri-secret') `
        -MaximumSeconds 1
    $stalledHttpStopwatch.Stop()
    Write-Host "503 stalled-body failure preserved after $($stalledHttpStopwatch.Elapsed.TotalMilliseconds) ms."

    Assert-RequestFailure -Request {
        Invoke-Man517JsonRequest `
            -Method Get `
            -Uri "$baseUrl/half-open?token=half-open-uri-secret" `
            -Headers @{} `
            -TimeoutSeconds 1
    } `
        -ExpectedFragments @('deadline exceeded', 'method=GET', 'uri=') `
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
