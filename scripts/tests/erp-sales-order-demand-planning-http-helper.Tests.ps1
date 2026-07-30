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
        'Wait-ErpSalesOrderReady'
    )) {
    Import-VerifyFunction -Ast $ast -Name $functionName
}

$testRoot = Join-Path $repoRoot "artifacts/script-logs/man703-http-helper-tests/$([Guid]::NewGuid().ToString('N'))"
$readyFile = Join-Path $testRoot 'ready.txt'
$counterFile = Join-Path $testRoot 'sales-order-request-count.txt'
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$port = Get-TestFreeTcpPort
$baseUrl = "http://127.0.0.1:$port"
$fixtureProcess = $null
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
            $counterFile) `
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

    $errorStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $businessError = $null
    try {
        Invoke-Man517JsonRequest `
            -Method Post `
            -Uri "$baseUrl/business-error?token=uri-secret-value" `
            -Headers @{} `
            -Body @{ value = 'ignored' } | Out-Null
    }
    catch {
        $businessError = $_.Exception.Message
    }
    $errorStopwatch.Stop()
    Assert-Helper (-not [string]::IsNullOrWhiteSpace($businessError)) 'HTTP 200 with success=false must throw.'
    Assert-Helper ($errorStopwatch.Elapsed.TotalSeconds -lt 2) 'HTTP 200 with success=false must fail immediately instead of entering a convergence timeout.'
    Assert-Helper ($businessError.Contains('method=POST')) 'Business-envelope failure must identify the HTTP method.'
    Assert-Helper ($businessError.Contains('code=404')) 'Business-envelope failure must expose the ResponseData code.'
    Assert-Helper ($businessError.Contains('message=')) 'Business-envelope failure must expose the redacted ResponseData message.'
    Assert-Helper (-not $businessError.Contains('uri-secret-value')) 'Business-envelope failure must redact sensitive URI query values.'
    Assert-Helper (-not $businessError.Contains('message-secret-value')) 'Business-envelope failure must redact sensitive message values.'

    $httpError = $null
    try {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/http-error" -Headers @{} | Out-Null
    }
    catch {
        $httpError = $_.Exception.Message
    }
    Assert-Helper ($httpError.Contains('httpStatus=503')) 'Non-success HTTP must fail through the shared request path.'

    $jsonError = $null
    try {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/invalid-json" -Headers @{} | Out-Null
    }
    catch {
        $jsonError = $_.Exception.Message
    }
    Assert-Helper ($jsonError.Contains('valid JSON')) 'Malformed JSON must fail through the shared request path.'

    $missingSuccessError = $null
    try {
        Invoke-Man517JsonRequest -Method Get -Uri "$baseUrl/missing-success" -Headers @{} | Out-Null
    }
    catch {
        $missingSuccessError = $_.Exception.Message
    }
    Assert-Helper ($missingSuccessError.Contains("missing boolean 'success'")) 'A JSON object without ResponseData success=true must fail closed.'
}
finally {
    if ($null -ne $fixtureProcess) {
        try {
            $fixtureProcess.Stop.Invoke('MAN-703 helper test cleanup') | Out-Null
        }
        catch {
            Write-Warning "Could not stop MAN-703 HTTP fixture cleanly: $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'ERP sales-order DemandPlanning HTTP helper behavior tests passed.'
