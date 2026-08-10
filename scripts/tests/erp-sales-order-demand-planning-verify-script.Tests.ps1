# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the ERP sales-order to DemandPlanning cross-process verification script
#     - Runs the exact script-governance gate for the MAN-703 HTTP fixture
#   Writes:
#     - artifacts/script-logs/man703-fixture-governance/**
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-erp-sales-order-demand-planning.ps1'
$fixtureScript = Join-Path $repoRoot 'scripts/tests/fixtures/man703-http-fixture.ps1'
$governanceScript = Join-Path $repoRoot 'scripts/check-script-governance.ps1'
$ciWorkflow = Join-Path $repoRoot '.github/workflows/ci.yml'
if (-not (Test-Path -LiteralPath $verifyScript)) {
    throw 'ERP sales-order DemandPlanning cross-process verify script is missing.'
}

$content = Get-Content -LiteralPath $verifyScript -Raw
$fixtureContent = Get-Content -LiteralPath $fixtureScript -Raw
$workflowContent = Get-Content -LiteralPath $ciWorkflow -Raw
$tokens = $null
$parseErrors = $null
$scriptAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $verifyScript,
    [ref] $tokens,
    [ref] $parseErrors)

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Get-FunctionDefinitionAst {
    param([string]$Name)
    return $scriptAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $Name
    }, $true)
}

function Get-FunctionContractText {
    param([string]$Name)
    $definition = Get-FunctionDefinitionAst -Name $Name
    if ($null -eq $definition) {
        return ''
    }
    return $definition.Extent.Text
}

# 调用点的检查一律走 AST 而不是字符串搜索：漏一个拼写就等于漏一个调用点，
# 而 AST 能把「所有调用点」当成可枚举集合来断言。
function Get-CommandCallAsts {
    param(
        [string]$Name,
        [System.Management.Automation.Language.Ast]$Scope
    )
    $searchScope = if ($null -eq $Scope) { $scriptAst } else { $Scope }
    return @($searchScope.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
        $null -ne $node.GetCommandName() -and
        [string]::Equals($node.GetCommandName(), $Name, [StringComparison]::OrdinalIgnoreCase)
    }, $true))
}

function Get-CommandParameterNames {
    param([System.Management.Automation.Language.CommandAst]$Call)
    return @($Call.CommandElements |
        Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } |
        ForEach-Object { $_.ParameterName })
}

function Test-CommandHasParameter {
    param([System.Management.Automation.Language.CommandAst]$Call, [string]$Name)
    foreach ($parameterName in Get-CommandParameterNames -Call $Call) {
        if ([string]::Equals($parameterName, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Get-CommandParameterValueText {
    param([System.Management.Automation.Language.CommandAst]$Call, [string]$Name)
    $elements = @($Call.CommandElements)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) { continue }
        if (-not [string]::Equals($element.ParameterName, $Name, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($null -ne $element.Argument) { return $element.Argument.Extent.Text }
        if ($index + 1 -lt $elements.Count) { return $elements[$index + 1].Extent.Text }
        return ''
    }
    return $null
}

function Get-ParameterAst {
    param([System.Management.Automation.Language.FunctionDefinitionAst]$Function, [string]$Name)
    if ($null -eq $Function -or $null -eq $Function.Body.ParamBlock) { return $null }
    return $Function.Body.ParamBlock.Parameters |
        Where-Object { [string]::Equals($_.Name.VariablePath.UserPath, $Name, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
}

function Test-AstIsInsideLoop {
    param([System.Management.Automation.Language.Ast]$Node)
    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.LoopStatementAst]) {
            return $true
        }
        $current = $current.Parent
    }
    return $false
}

Assert-Contract ($parseErrors.Count -eq 0) 'Verify script must parse before source contracts are evaluated.'
Assert-Contract ($fixtureContent.Contains('scripts/lib/ScriptAutomation.ps1')) 'MAN-703 HTTP fixture must dot-source the governed ScriptAutomation helper from its own path.'
Assert-Contract ($content.Contains('# Script-Governance:')) 'Verify script must declare script governance metadata.'
Assert-Contract ($content.Contains('scripts/lib/ScriptAutomation.ps1')) 'Verify script must use ScriptAutomation helpers.'
Assert-Contract ($content.Contains('Start-ManagedBackgroundProcess')) 'Verify script must launch managed service processes.'
Assert-Contract ($content.Contains('pg_isready')) 'Verify script must wait for PostgreSQL readiness before creating the disposable database.'
Assert-Contract ($content.Contains('function New-AcceptanceDatabase')) 'Verify script must retry the first real PostgreSQL operation after readiness.'
Assert-Contract ($content.Contains("'psql', '-h', '127.0.0.1'")) 'Disposable database creation must use TCP instead of the transient container socket.'
Assert-Contract ($content.Contains('New-AcceptanceDatabase -ComposeFile $composeFile -DatabaseName $databaseName')) 'Verify script must create its disposable database through the bounded retry helper.'
Assert-Contract ($content.Contains("SELECT 1 FROM pg_database WHERE datname = '`$DatabaseName';")) 'Disposable database creation retries must check whether an ambiguous CREATE already committed.'
Assert-Contract ($content.Contains('$databaseExists.Stdout')) 'Disposable database creation must consume the real PostgreSQL existence check result.'
$existenceCheckIndex = $content.IndexOf("SELECT 1 FROM pg_database WHERE datname = '`$DatabaseName';", [StringComparison]::Ordinal)
$createSqlIndex = $content.IndexOf('"CREATE DATABASE $DatabaseName;"', [StringComparison]::Ordinal)
Assert-Contract ($existenceCheckIndex -ge 0 -and $createSqlIndex -gt $existenceCheckIndex) 'Every retry must check for the random database before issuing CREATE DATABASE.'
$cleanupIntentIndex = $content.IndexOf('$databaseCreated = $true', [StringComparison]::Ordinal)
$createDatabaseIndex = $content.IndexOf('New-AcceptanceDatabase -ComposeFile $composeFile -DatabaseName $databaseName', [StringComparison]::Ordinal)
Assert-Contract ($cleanupIntentIndex -ge 0 -and $cleanupIntentIndex -lt $createDatabaseIndex) 'Cleanup intent must be recorded before the first possibly successful database creation attempt.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.MasterData.Web.csproj')) 'Verify script must launch MasterData for reusable customer/credit prerequisites.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Erp.Web.csproj')) 'Verify script must launch ERP in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.DemandPlanning.Web.csproj')) 'Verify script must launch DemandPlanning in its own process.'
Assert-Contract ($content.Contains("Messaging__Provider = 'Redis'")) 'Verify script must use the real Redis CAP provider.'
Assert-Contract ($content.Contains("Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'")) 'Verify script must prove the reusable SO-DEMO-001 seed publishes through the real cross-process bridge.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest'))) 'Verify script must define one fail-closed JSON request path.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Wait-ErpSalesOrderReady'))) 'Verify script must poll the ERP sales-order query after health before mutation.'
foreach ($functionName in @('Invoke-JsonPost', 'Wait-Demand', 'Assert-DemandStable', 'Wait-ErpSalesOrderReady')) {
    $functionText = Get-FunctionContractText -Name $functionName
    Assert-Contract ($functionText.Contains('Invoke-Man517JsonRequest')) "$functionName must use the shared fail-closed JSON request path."
}
foreach ($functionName in @('Wait-Demand', 'Assert-DemandStable', 'Wait-ErpSalesOrderReady')) {
    Assert-Contract ((Get-FunctionContractText -Name $functionName).Contains('-Deadline $deadline')) "$functionName must pass its absolute deadline into every request."
}

# --- MAN-517 mutation budget contract (#1334) -------------------------------------------------
# 冷 CI runner 上，5 秒隐式默认预算会在 ERP handler 已经开始写事务之后取消状态变更 POST
# （服务端记 HTTP 499、v2 事件不发布）。因此这里钉三件事：预算必须显式、状态变更预算必须
# 有界且远离 5 秒、状态变更绝不重发。
$requestFunctionAst = Get-FunctionDefinitionAst -Name 'Invoke-Man517JsonRequest'
$mutationFunctionAst = Get-FunctionDefinitionAst -Name 'Invoke-JsonPost'
Assert-Contract ($null -ne $requestFunctionAst) 'The shared JSON request path must exist.'
Assert-Contract ($null -ne $mutationFunctionAst) 'The single-shot mutation helper must exist.'

$sharedTimeoutParameter = Get-ParameterAst -Function $requestFunctionAst -Name 'TimeoutSeconds'
Assert-Contract ($null -ne $sharedTimeoutParameter) 'The shared JSON request path must accept an explicit -TimeoutSeconds budget.'
Assert-Contract ($null -eq $sharedTimeoutParameter.DefaultValue) 'The shared JSON request path must not carry any implicit request budget; the 5-second default is what cancelled committed mutations on cold runners.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('has no explicit budget')) 'A request without -Deadline or -TimeoutSeconds must fail closed instead of inheriting a hidden default.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('budget is ambiguous')) 'Passing both -Deadline and -TimeoutSeconds must fail closed instead of silently preferring one.'

$mutationTimeoutParameter = Get-ParameterAst -Function $mutationFunctionAst -Name 'MutationTimeoutSeconds'
Assert-Contract ($null -ne $mutationTimeoutParameter) 'The mutation helper must name its own bounded budget parameter.'
Assert-Contract ($null -ne $mutationTimeoutParameter.DefaultValue) 'The mutation budget must be declared at the mutation helper, not left to the caller.'
$mutationTimeoutDefault = 0
Assert-Contract (
    [int]::TryParse($mutationTimeoutParameter.DefaultValue.Extent.Text, [ref]$mutationTimeoutDefault)
) 'The mutation budget default must be a literal number that can be reviewed.'
Assert-Contract ($mutationTimeoutDefault -ge 60) "The mutation budget must tolerate a cold CI runner; $mutationTimeoutDefault seconds is not enough (the 5-second default is what this issue removed)."
Assert-Contract ($mutationTimeoutDefault -le 180) "The mutation budget must stay bounded; $mutationTimeoutDefault seconds is an open-ended wait."
$mutationRangeAttribute = $mutationTimeoutParameter.Attributes |
    Where-Object { [string]::Equals($_.TypeName.Name, 'ValidateRange', [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
Assert-Contract ($null -ne $mutationRangeAttribute) 'The mutation budget must declare a validated range so no caller can restore a too-short budget.'
$mutationRangeBounds = @($mutationRangeAttribute.PositionalArguments | ForEach-Object { [int]$_.Extent.Text })
Assert-Contract ($mutationRangeBounds.Count -eq 2) 'The mutation budget range must declare both a minimum and a maximum.'
Assert-Contract ($mutationRangeBounds[0] -ge 60) "The mutation budget minimum must stay above cold-runner latency; $($mutationRangeBounds[0]) seconds allows the failure this issue fixed."
Assert-Contract ($mutationRangeBounds[1] -le 180) "The mutation budget maximum must stay bounded; $($mutationRangeBounds[1]) seconds is effectively an unbounded wait."

$mutationRequestCalls = Get-CommandCallAsts -Name 'Invoke-Man517JsonRequest' -Scope $mutationFunctionAst
Assert-Contract ($mutationRequestCalls.Count -eq 1) 'The mutation helper must send its state change exactly once.'
Assert-Contract (
    @($mutationFunctionAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.LoopStatementAst] }, $true)).Count -eq 0
) 'The mutation helper must contain no loop; retrying a state change after an uncertain commit is a duplicate write.'
foreach ($mutationCall in (Get-CommandCallAsts -Name 'Invoke-JsonPost')) {
    Assert-Contract (-not (Test-AstIsInsideLoop -Node $mutationCall)) 'No sales-order mutation may be wrapped in a retry loop; convergence is proven by polling the query side instead.'
}

# 状态变更只能走 Invoke-JsonPost 这一条路，否则「不重试 + 有界预算」的保证会被绕过。
foreach ($requestCall in (Get-CommandCallAsts -Name 'Invoke-Man517JsonRequest')) {
    $methodValue = Get-CommandParameterValueText -Call $requestCall -Name 'Method'
    if ($null -eq $methodValue -or -not [string]::Equals($methodValue.Trim("'", '"'), 'Post', [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    $insideMutationHelper = $requestCall.Extent.StartOffset -ge $mutationFunctionAst.Extent.StartOffset -and
        $requestCall.Extent.EndOffset -le $mutationFunctionAst.Extent.EndOffset
    Assert-Contract $insideMutationHelper 'Every state-changing POST must go through the single-shot mutation helper.'
}

# 每个请求调用点都必须自报 stage 和自己的预算；诊断和预算都不能靠默认值蒙混过去。
foreach ($requestCall in (Get-CommandCallAsts -Name 'Invoke-Man517JsonRequest')) {
    Assert-Contract (Test-CommandHasParameter -Call $requestCall -Name 'Stage') 'Every request must name the acceptance stage it belongs to so a timeout says which step failed.'
    $hasDeadline = Test-CommandHasParameter -Call $requestCall -Name 'Deadline'
    $hasTimeout = Test-CommandHasParameter -Call $requestCall -Name 'TimeoutSeconds'
    Assert-Contract ($hasDeadline -xor $hasTimeout) 'Every request must pass exactly one explicit budget: -Deadline for a polled query or -TimeoutSeconds for a single-shot mutation.'
}
foreach ($mutationCall in (Get-CommandCallAsts -Name 'Invoke-JsonPost')) {
    Assert-Contract (Test-CommandHasParameter -Call $mutationCall -Name 'Stage') 'Every mutation must name the acceptance stage it belongs to.'
}

# 四类失败必须在诊断里彼此可分：connect / send / 服务端取消（499）/ 业务信封。
$requestFunctionText = Get-FunctionContractText -Name 'Invoke-Man517JsonRequest'
foreach ($classification in @('classification=deadline', 'classification=business', 'classification=protocol')) {
    Assert-Contract ($requestFunctionText.Contains($classification)) "The shared JSON request path must report $classification explicitly."
}
Assert-Contract ($requestFunctionText.Contains('classification=$(Get-Man517TransportClassification')) 'Transport failures must be classified as connect or send instead of collapsing into one bucket.'
Assert-Contract ($requestFunctionText.Contains('classification=$(Get-Man517HttpClassification')) 'Non-success HTTP status must be classified so a server-side cancellation is not read as a server error.'
$httpClassificationText = Get-FunctionContractText -Name 'Get-Man517HttpClassification'
Assert-Contract ($httpClassificationText.Contains('499')) 'HTTP 499 must be recognised as a server-side cancellation.'
Assert-Contract ($httpClassificationText.Contains("'server-cancelled'")) 'Server-side cancellation must have its own classification.'
$transportClassificationText = Get-FunctionContractText -Name 'Get-Man517TransportClassification'
foreach ($classification in @("'connect'", "'send'")) {
    Assert-Contract ($transportClassificationText.Contains($classification)) "Transport classification must be able to return $classification."
}
Assert-Contract ($transportClassificationText.Contains('ConnectionError')) 'Transport classification must read the real HttpRequestError instead of matching message text.'
Assert-Contract ($requestFunctionText.Contains('stage=$safeStage')) 'Every failure must carry the redacted stage.'
Assert-Contract ($requestFunctionText.Contains('uri=$safeUri')) 'Every failure must carry the redacted URI.'
Assert-Contract ($requestFunctionText.Contains('elapsedMs=')) 'Timeout diagnostics must report how long the request actually ran.'
Assert-Contract ($requestFunctionText.Contains('budgetMs=')) 'Timeout diagnostics must report the budget that was in force.'

Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('ResponseHeadersRead')) 'The shared JSON request path must stream the response under its absolute cancellation budget.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('CancellationTokenSource')) 'The shared JSON request path must enforce one absolute cancellation budget.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('SendAsync')) 'The shared JSON request path must pass cancellation into the HTTP send.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('ReadAsStringAsync')) 'The shared JSON request path must pass cancellation into complete response reading.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('System.TimeoutException')) 'Deadline expiry must use a typed TimeoutException.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('$deadlineCancellation.IsCancellationRequested')) 'OperationCanceledException must be mapped to TimeoutException only when the owned absolute deadline token fired.'
$requestFunctionText = Get-FunctionContractText -Name 'Invoke-Man517JsonRequest'
$httpStatusFailureIndex = $requestFunctionText.IndexOf('$httpStatus -lt 200', [StringComparison]::Ordinal)
$responseReadIndex = $requestFunctionText.IndexOf('ReadAsStringAsync', [StringComparison]::Ordinal)
Assert-Contract ($httpStatusFailureIndex -ge 0 -and $responseReadIndex -gt $httpStatusFailureIndex) 'Non-success HTTP status must fail immediately after headers, before response body reading.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains("PSObject.Properties['success']")) 'The shared JSON request path must require a ResponseData success field.'
foreach ($functionName in @('Wait-Demand', 'Wait-ErpSalesOrderReady', 'Assert-DemandStable')) {
    $functionText = Get-FunctionContractText -Name $functionName
    Assert-Contract ($functionText.Contains('catch [System.TimeoutException]')) "$functionName must handle typed request deadline expiry explicitly."
    Assert-Contract ($functionText.Contains('(Get-Date) -lt $deadline')) "$functionName must rethrow a typed timeout that occurs before its own absolute deadline."
}
Assert-Contract ((Get-FunctionContractText -Name 'Wait-ErpSalesOrderReady').Contains('[decimal]$rows[0].totalAmount -eq 200')) 'ERP readiness must validate the seeded order amount, not only its identifier.'
$erpReadyIndex = $content.IndexOf('Wait-ErpSalesOrderReady -ErpUrl $erpUrl', [StringComparison]::Ordinal)
$firstMutationIndex = $content.IndexOf('Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10"', [StringComparison]::Ordinal)
Assert-Contract ($erpReadyIndex -ge 0 -and $firstMutationIndex -gt $erpReadyIndex) 'ERP query-visible readiness must complete before the first sales-order mutation.'
Assert-Contract (-not $content.Contains('NERV_IIP_TEST_SALES_ORDER_ID')) 'Fault injection must resolve the seeded order identity from DemandPlanning persistence instead of fragile shell output.'
Assert-Contract ($content.Contains('out-of-order')) 'Verify script must assert stale/out-of-order convergence.'
Assert-Contract ($content.Contains('$runningResult.Stdout')) 'Verify script must parse the compose service list from Invoke-NativeCommandOutput.Stdout before cleanup ownership is decided.'
Assert-Contract ($content.Contains('UnitTestResult')) 'Verify script must prove the external fault-injection test actually executed and passed.'
Assert-Contract ($content.Contains("local-name()='Counters'")) 'Verify script must read the probe TRX counters; one named passing result cannot rule out other failed or skipped tests in the same run.'
Assert-Contract ($content.Contains('executed=1 passed=1 failed=0 skipped=0')) 'Verify script must assert the exact FullChain probe accounting.'
Assert-Contract ($content.Contains('Assert-DemandStable')) 'Verify script must hold the final cancellation state stable after stale-message injection.'
Assert-Contract ($content.Contains('Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres')) 'Verify script must execute the real Redis identical-idempotency-key duplicate test.'
Assert-Contract ($content.Contains('Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail')) 'Verify script must execute the real Redis fallback-scan retry test.'
Assert-Contract ($content.Contains('changed during the stability window')) 'Verify script must fail immediately when the final demand changes during the stability window.'
Assert-Contract ($content.Contains("Wait-Demand -DemandPlanningUrl `$demandPlanningUrl -Headers `$headers -Version 4 -Quantity 0 -Status 'cancelled'")) 'Verify script must wait for cancellation convergence before entering the strict stability window.'
Assert-Contract ($content.Contains('sourceVersion')) 'Verify script must assert business-version convergence.'
Assert-Contract ($content.Contains('sourceStatus')) 'Verify script must assert lifecycle-status convergence.'
Assert-Contract ($content.Contains('finally')) 'Verify script must clean up processes and disposable infrastructure in finally.'
$cleanupFailureListIndex = $content.IndexOf('$cleanupFailures = [System.Collections.Generic.List[string]]::new()', [StringComparison]::Ordinal)
$cleanupFinallyIndex = $content.IndexOf('finally {', [Math]::Max(0, $cleanupFailureListIndex), [StringComparison]::Ordinal)
$demandPlanningCleanupFailureIndex = $content.IndexOf('$cleanupFailures.Add("demand-planning process: $($_.Exception.Message)")', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
$erpCleanupFailureIndex = $content.IndexOf('$cleanupFailures.Add("erp process: $($_.Exception.Message)")', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
$masterDataCleanupFailureIndex = $content.IndexOf('$cleanupFailures.Add("master-data process: $($_.Exception.Message)")', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
$databaseCleanupFailureIndex = $content.IndexOf('$cleanupFailures.Add("database: $($_.Exception.Message)")', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
$infrastructureCleanupFailureIndex = $content.IndexOf('$cleanupFailures.Add("infrastructure: $($_.Exception.Message)")', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
$cleanupOnlyThrowIndex = $content.IndexOf('throw "MAN-517 cleanup failed: $cleanupSummary"', [Math]::Max(0, $infrastructureCleanupFailureIndex), [StringComparison]::Ordinal)
$acceptanceRethrowIndex = $content.IndexOf('throw $acceptanceFailure', [Math]::Max(0, $cleanupFinallyIndex), [StringComparison]::Ordinal)
Assert-Contract (
    $cleanupFailureListIndex -ge 0 -and
    $cleanupFinallyIndex -gt $cleanupFailureListIndex -and
    $demandPlanningCleanupFailureIndex -gt $cleanupFinallyIndex -and
    $erpCleanupFailureIndex -gt $demandPlanningCleanupFailureIndex -and
    $masterDataCleanupFailureIndex -gt $erpCleanupFailureIndex -and
    $databaseCleanupFailureIndex -gt $masterDataCleanupFailureIndex -and
    $infrastructureCleanupFailureIndex -gt $databaseCleanupFailureIndex -and
    $cleanupOnlyThrowIndex -gt $infrastructureCleanupFailureIndex -and
    $acceptanceRethrowIndex -gt $cleanupOnlyThrowIndex
) 'Every process, database, and infrastructure cleanup failure must be captured independently; cleanup-only failures must throw, and the original acceptance failure must be rethrown only after all cleanup attempts.'
Assert-Contract ($content.Contains('Original acceptance failure preserved; cleanup also failed:')) 'Cleanup failures must be reported without masking the original acceptance failure.'
# 清理必须给出「剩余=0」的账，而不只是发出停止请求。
Assert-Contract ($content.Contains('Get-Man517RemainingProcessNames -Descriptors')) 'Cleanup must verify every owned process is actually gone, not only that a stop was requested.'
Assert-Contract ((Get-FunctionContractText -Name 'Get-Man517RemainingProcessNames').Contains('StartTime')) 'Process cleanup verification must confirm identity by start time, because PIDs are reused.'
Assert-Contract ($content.Contains("SELECT count(*) FROM pg_database WHERE datname = '`$databaseName';")) 'Cleanup must verify the exact disposable database is gone, and only that one.'
Assert-Contract ($content.Contains('disposable database still present')) 'A surviving disposable database must be reported as a cleanup failure.'
Assert-Contract ($content.Contains('script-owned compose services still running')) 'Cleanup must verify only the compose services this run started are gone.'
Assert-Contract ($content.Contains('cleanup-evidence.json')) 'Cleanup accounting must be written as reusable evidence.'
Assert-Contract ($content.Contains('sales-order-demand-planning-evidence.json')) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('lastHttpStatus')) 'Wait-Demand must preserve the last HTTP status.'
Assert-Contract ($content.Contains('lastResponseBody')) 'Wait-Demand must preserve the last HTTP response body.'
Assert-Contract ($content.Contains('lastRequestException')) 'Wait-Demand must preserve the last request exception.'
Assert-Contract ($content.Contains('lastObservedDemand')) 'Wait-Demand must preserve the last observed version, quantity, and status.'
Assert-Contract ($content.Contains('Export-Man517FailureDiagnostics')) 'The acceptance script must export DB, Redis, and log diagnostics before cleanup.'
Assert-Contract ($content.Contains('Protect-ScriptAutomationText')) 'Failure diagnostics must reuse the governed shared redactor.'
Assert-Contract ($content.Contains('Protect-Man517DiagnosticText -Text $lastObservation')) 'Wait-Demand must redact its last observation before throwing to CI logs.'
Assert-Contract ($content.Contains("Cap__FailedRetryInterval = '2'")) 'Acceptance must configure a short failed-message scan interval.'
Assert-Contract ($content.Contains("Cap__FallbackWindowLookbackSeconds = '30'")) 'Acceptance must configure CAP safe-minimum fallback eligibility.'
Assert-Contract ($content.Contains('erp.cap_published_messages')) 'Failure diagnostics must capture the ERP CAP outbox state.'
Assert-Contract ($content.Contains('demand_planning.cap_received_messages')) 'Failure diagnostics must capture the DemandPlanning CAP inbox state.'
Assert-Contract ($content.Contains('processed_integration_events')) 'Failure diagnostics must capture the durable DemandPlanning consumer inbox.'
Assert-Contract ($content.Contains('integration_event_dead_letters')) 'Failure diagnostics must capture the DemandPlanning DLQ.'
Assert-Contract ($content.Contains('sales_order_demand_projections')) 'Failure diagnostics must capture the sales-order watermark projection.'
Assert-Contract ($content.Contains('demand_sources')) 'Failure diagnostics must capture the projected demand source.'
Assert-Contract ($content.Contains('XPENDING')) 'Failure diagnostics must capture Redis pending-entry metadata.'
Assert-Contract ($workflowContent.Contains('if: always()')) 'CI must upload MAN-517 diagnostics even when verification fails.'
Assert-Contract ($workflowContent.Contains('actions/upload-artifact@v4')) 'CI must retain MAN-517 diagnostics as an artifact.'

. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
Invoke-PwshScript `
    -ScriptPath $governanceScript `
    -Arguments @('-Path', $fixtureScript) `
    -WorkingDirectory $repoRoot `
    -Name 'man703-fixture-governance' | Out-Null
$unsafeDiagnostic = 'pwd=pwd-value token=token-value secret=secret-value client_secret=client-value Authorization: Bearer bearer-value Password=password-value'
$safeDiagnostic = Protect-ScriptAutomationText $unsafeDiagnostic
foreach ($sensitiveValue in @('pwd-value', 'token-value', 'secret-value', 'client-value', 'bearer-value', 'password-value')) {
    Assert-Contract (-not $safeDiagnostic.Contains($sensitiveValue)) "Shared diagnostic redaction leaked $sensitiveValue."
}

Write-Host 'ERP sales-order DemandPlanning cross-process verify script contract tests passed.'
