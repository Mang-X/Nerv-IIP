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
        [string]::Equals([string]$node.Name, $Name, [StringComparison]::OrdinalIgnoreCase)
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

function Get-CommandParameterValueAst {
    param([System.Management.Automation.Language.CommandAst]$Call, [string]$Name)
    $elements = @($Call.CommandElements)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) { continue }
        if (-not [string]::Equals($element.ParameterName, $Name, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($null -ne $element.Argument) { return $element.Argument }
        if ($index + 1 -lt $elements.Count) { return $elements[$index + 1] }
        return $null
    }
    return $null
}

function Get-CommandParameterValueText {
    param([System.Management.Automation.Language.CommandAst]$Call, [string]$Name)
    $valueAst = Get-CommandParameterValueAst -Call $Call -Name $Name
    if ($null -eq $valueAst) { return $null }
    return $valueAst.Extent.Text
}

# 「哪些元素是位置参数」必须从 AST 枚举，而不是靠逐条列举已知的绕行写法。
# 前提是被调函数没有 switch 参数（下面单独断言）：那样「紧跟在具名参数后的元素」
# 一定是该参数的值，剩下的每个元素就都是位置参数或 splat。
function Get-UnnamedArgumentAsts {
    param([System.Management.Automation.Language.CommandAst]$Call)
    $unnamed = [System.Collections.Generic.List[System.Management.Automation.Language.Ast]]::new()
    $elements = @($Call.CommandElements)
    for ($index = 1; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -is [System.Management.Automation.Language.CommandParameterAst]) { continue }
        $previous = $elements[$index - 1]
        if ($previous -is [System.Management.Automation.Language.CommandParameterAst] -and $null -eq $previous.Argument) {
            continue
        }
        $unnamed.Add($element)
    }
    return $unnamed.ToArray()
}

function Test-CommandUsesSplatting {
    param([System.Management.Automation.Language.CommandAst]$Call)
    foreach ($element in $Call.CommandElements) {
        if ($element -is [System.Management.Automation.Language.VariableExpressionAst] -and $element.Splatted) {
            return $true
        }
    }
    return $false
}

# 「会不会被执行多次」不能靠列举 for/foreach/while/ForEach-Object/% 这些名字：
# 只要调用点落在任何嵌套 scriptblock 里，静态上就无法断定它只跑一次
# （管道 ForEach-Object、.ForEach() 方法、& $block、自建重试助手都是同一类）。
# 因此契约按 AST 类型划线：状态变更只能出现在验收主流程的语句位置上。
function Get-RepeatableAncestorKind {
    param([System.Management.Automation.Language.Ast]$Node)
    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.LoopStatementAst]) {
            return 'loop'
        }
        if ($current -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) {
            return 'script block'
        }
        $current = $current.Parent
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

Assert-Contract ($parseErrors.Count -eq 0) 'Verify script must parse before source contracts are evaluated.'
Assert-Contract ($fixtureContent.Contains('scripts/lib/ScriptAutomation.ps1', [StringComparison]::Ordinal)) 'MAN-703 HTTP fixture must dot-source the governed ScriptAutomation helper from its own path.'
Assert-Contract ($content.Contains('# Script-Governance:', [StringComparison]::Ordinal)) 'Verify script must declare script governance metadata.'
Assert-Contract ($content.Contains('scripts/lib/ScriptAutomation.ps1', [StringComparison]::Ordinal)) 'Verify script must use ScriptAutomation helpers.'
Assert-Contract ($content.Contains('Start-ManagedBackgroundProcess', [StringComparison]::Ordinal)) 'Verify script must launch managed service processes.'
Assert-Contract ($content.Contains('pg_isready', [StringComparison]::Ordinal)) 'Verify script must wait for PostgreSQL readiness before creating the disposable database.'
Assert-Contract ($content.Contains('function New-AcceptanceDatabase', [StringComparison]::Ordinal)) 'Verify script must retry the first real PostgreSQL operation after readiness.'
Assert-Contract ($content.Contains("'psql', '-h', '127.0.0.1'", [StringComparison]::Ordinal)) 'Disposable database creation must use TCP instead of the transient container socket.'
Assert-Contract ($content.Contains('New-AcceptanceDatabase -ComposeFile $composeFile -DatabaseName $databaseName', [StringComparison]::Ordinal)) 'Verify script must create its disposable database through the bounded retry helper.'
Assert-Contract ($content.Contains("SELECT 1 FROM pg_database WHERE datname = '`$DatabaseName';", [StringComparison]::Ordinal)) 'Disposable database creation retries must check whether an ambiguous CREATE already committed.'
Assert-Contract ($content.Contains('$databaseExists.Stdout', [StringComparison]::Ordinal)) 'Disposable database creation must consume the real PostgreSQL existence check result.'
$existenceCheckIndex = $content.IndexOf("SELECT 1 FROM pg_database WHERE datname = '`$DatabaseName';", [StringComparison]::Ordinal)
$createSqlIndex = $content.IndexOf('"CREATE DATABASE $DatabaseName;"', [StringComparison]::Ordinal)
Assert-Contract ($existenceCheckIndex -ge 0 -and $createSqlIndex -gt $existenceCheckIndex) 'Every retry must check for the random database before issuing CREATE DATABASE.'
$cleanupIntentIndex = $content.IndexOf('$databaseCreated = $true', [StringComparison]::Ordinal)
$createDatabaseIndex = $content.IndexOf('New-AcceptanceDatabase -ComposeFile $composeFile -DatabaseName $databaseName', [StringComparison]::Ordinal)
Assert-Contract ($cleanupIntentIndex -ge 0 -and $cleanupIntentIndex -lt $createDatabaseIndex) 'Cleanup intent must be recorded before the first possibly successful database creation attempt.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.MasterData.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch MasterData for reusable customer/credit prerequisites.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Erp.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch ERP in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.DemandPlanning.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch DemandPlanning in its own process.'
Assert-Contract ($content.Contains("Messaging__Provider = 'Redis'", [StringComparison]::Ordinal)) 'Verify script must use the real Redis CAP provider.'
Assert-Contract ($content.Contains("Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'", [StringComparison]::Ordinal)) 'Verify script must prove the reusable SO-DEMO-001 seed publishes through the real cross-process bridge.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest'))) 'Verify script must define one fail-closed JSON request path.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'invoke-man517jsonrequest'))) 'PowerShell function contract lookup must follow case-insensitive command-name semantics.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Wait-ErpSalesOrderReady'))) 'Verify script must poll the ERP sales-order query after health before mutation.'
foreach ($functionName in @('Invoke-JsonPost', 'Wait-Demand', 'Assert-DemandStable', 'Wait-ErpSalesOrderReady')) {
    $functionText = Get-FunctionContractText -Name $functionName
    Assert-Contract ($functionText.Contains('Invoke-Man517JsonRequest', [StringComparison]::Ordinal)) "$functionName must use the shared fail-closed JSON request path."
}
foreach ($functionName in @('Wait-Demand', 'Assert-DemandStable', 'Wait-ErpSalesOrderReady')) {
    Assert-Contract ((Get-FunctionContractText -Name $functionName).Contains('-Deadline $deadline', [StringComparison]::Ordinal)) "$functionName must pass its absolute deadline into every request."
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
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('has no explicit budget', [StringComparison]::Ordinal)) 'A request without -Deadline or -TimeoutSeconds must fail closed instead of inheriting a hidden default.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('budget is ambiguous', [StringComparison]::Ordinal)) 'Passing both -Deadline and -TimeoutSeconds must fail closed instead of silently preferring one.'

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
    $repeatableAncestor = Get-RepeatableAncestorKind -Node $mutationCall
    Assert-Contract (
        $null -eq $repeatableAncestor
    ) "No sales-order mutation may sit inside a $repeatableAncestor; anything a $repeatableAncestor can run twice is a duplicate write. Convergence is proven by polling the query side instead."
}

# 上面两个调用点契约都读「具名参数」。要让「读具名参数」等于「读全部实参」，
# 被调函数就不能有 switch 参数（否则紧跟其后的元素可能是位置参数而不是它的值），
# 且调用点不得使用位置参数或 splatting。这三条一起才让下面的路由/预算判定是穷举的。
foreach ($contractedFunction in @($requestFunctionAst, $mutationFunctionAst)) {
    $switchParameters = @($contractedFunction.Body.ParamBlock.Parameters | Where-Object {
        $_.Attributes | Where-Object { [string]::Equals($_.TypeName.Name, 'switch', [StringComparison]::OrdinalIgnoreCase) }
    })
    Assert-Contract (
        $switchParameters.Count -eq 0
    ) "$($contractedFunction.Name) must declare no switch parameter, otherwise an element following a named parameter can no longer be read as that parameter's value."
}
foreach ($contractedCallName in @('Invoke-Man517JsonRequest', 'Invoke-JsonPost')) {
    foreach ($contractedCall in (Get-CommandCallAsts -Name $contractedCallName)) {
        Assert-Contract (
            -not (Test-CommandUsesSplatting -Call $contractedCall)
        ) "$contractedCallName must not be called with splatting; a splatted hashtable hides the stage, the budget and the HTTP method from every contract below."
        $unnamedArguments = @(Get-UnnamedArgumentAsts -Call $contractedCall)
        Assert-Contract (
            $unnamedArguments.Count -eq 0
        ) "$contractedCallName must bind every argument by name; positional argument '$(if ($unnamedArguments.Count -gt 0) { $unnamedArguments[0].Extent.Text })' bypasses the stage, budget and routing contracts."
    }
}

# 状态变更只能走 Invoke-JsonPost 这一条路，否则「不重试 + 有界预算」的保证会被绕过。
foreach ($requestCall in (Get-CommandCallAsts -Name 'Invoke-Man517JsonRequest')) {
    $methodValueAst = Get-CommandParameterValueAst -Call $requestCall -Name 'Method'
    Assert-Contract (
        $null -ne $methodValueAst -and $methodValueAst -is [System.Management.Automation.Language.StringConstantExpressionAst]
    ) 'Every request must name its HTTP method as a literal; a computed method makes the POST routing contract unreadable.'
    if (-not [string]::Equals($methodValueAst.Value, 'Post', [StringComparison]::OrdinalIgnoreCase)) {
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
    Assert-Contract ($requestFunctionText.Contains($classification, [StringComparison]::Ordinal)) "The shared JSON request path must report $classification explicitly."
}
Assert-Contract ($requestFunctionText.Contains('classification=$(Get-Man517TransportClassification', [StringComparison]::Ordinal)) 'Transport failures must be classified as connect or send instead of collapsing into one bucket.'
Assert-Contract ($requestFunctionText.Contains('classification=$(Get-Man517HttpClassification', [StringComparison]::Ordinal)) 'Non-success HTTP status must be classified so a server-side cancellation is not read as a server error.'
$httpClassificationText = Get-FunctionContractText -Name 'Get-Man517HttpClassification'
Assert-Contract ($httpClassificationText.Contains('499', [StringComparison]::Ordinal)) 'HTTP 499 must be recognised as a server-side cancellation.'
Assert-Contract ($httpClassificationText.Contains("'server-cancelled'", [StringComparison]::Ordinal)) 'Server-side cancellation must have its own classification.'
$transportClassificationText = Get-FunctionContractText -Name 'Get-Man517TransportClassification'
foreach ($classification in @("'connect'", "'send'")) {
    Assert-Contract ($transportClassificationText.Contains($classification, [StringComparison]::Ordinal)) "Transport classification must be able to return $classification."
}
Assert-Contract ($transportClassificationText.Contains('ConnectionError', [StringComparison]::Ordinal)) 'Transport classification must read the real HttpRequestError instead of matching message text.'
Assert-Contract ($requestFunctionText.Contains('stage=$safeStage', [StringComparison]::Ordinal)) 'Every failure must carry the redacted stage.'
Assert-Contract ($requestFunctionText.Contains('uri=$safeUri', [StringComparison]::Ordinal)) 'Every failure must carry the redacted URI.'
Assert-Contract ($requestFunctionText.Contains('elapsedMs=', [StringComparison]::Ordinal)) 'Timeout diagnostics must report how long the request actually ran.'
Assert-Contract ($requestFunctionText.Contains('budgetMs=', [StringComparison]::Ordinal)) 'Timeout diagnostics must report the budget that was in force.'

Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('ResponseHeadersRead', [StringComparison]::Ordinal)) 'The shared JSON request path must stream the response under its absolute cancellation budget.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('CancellationTokenSource', [StringComparison]::Ordinal)) 'The shared JSON request path must enforce one absolute cancellation budget.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('SendAsync', [StringComparison]::Ordinal)) 'The shared JSON request path must pass cancellation into the HTTP send.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('ReadAsStringAsync', [StringComparison]::Ordinal)) 'The shared JSON request path must pass cancellation into complete response reading.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('System.TimeoutException', [StringComparison]::Ordinal)) 'Deadline expiry must use a typed TimeoutException.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains('$deadlineCancellation.IsCancellationRequested', [StringComparison]::Ordinal)) 'OperationCanceledException must be mapped to TimeoutException only when the owned absolute deadline token fired.'
$requestFunctionText = Get-FunctionContractText -Name 'Invoke-Man517JsonRequest'
$httpStatusFailureIndex = $requestFunctionText.IndexOf('$httpStatus -lt 200', [StringComparison]::Ordinal)
$responseReadIndex = $requestFunctionText.IndexOf('ReadAsStringAsync', [StringComparison]::Ordinal)
Assert-Contract ($httpStatusFailureIndex -ge 0 -and $responseReadIndex -gt $httpStatusFailureIndex) 'Non-success HTTP status must fail immediately after headers, before response body reading.'
Assert-Contract ((Get-FunctionContractText -Name 'Invoke-Man517JsonRequest').Contains("PSObject.Properties['success']", [StringComparison]::Ordinal)) 'The shared JSON request path must require a ResponseData success field.'
foreach ($functionName in @('Wait-Demand', 'Wait-ErpSalesOrderReady', 'Assert-DemandStable')) {
    $functionText = Get-FunctionContractText -Name $functionName
    Assert-Contract ($functionText.Contains('catch [System.TimeoutException]', [StringComparison]::Ordinal)) "$functionName must handle typed request deadline expiry explicitly."
    Assert-Contract ($functionText.Contains('(Get-Date) -lt $deadline', [StringComparison]::Ordinal)) "$functionName must rethrow a typed timeout that occurs before its own absolute deadline."
}
Assert-Contract ((Get-FunctionContractText -Name 'Wait-ErpSalesOrderReady').Contains('[decimal]$rows[0].totalAmount -eq 200', [StringComparison]::Ordinal)) 'ERP readiness must validate the seeded order amount, not only its identifier.'
$erpReadyIndex = $content.IndexOf('Wait-ErpSalesOrderReady -ErpUrl $erpUrl', [StringComparison]::Ordinal)
$firstMutationIndex = $content.IndexOf('Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10"', [StringComparison]::Ordinal)
Assert-Contract ($erpReadyIndex -ge 0 -and $firstMutationIndex -gt $erpReadyIndex) 'ERP query-visible readiness must complete before the first sales-order mutation.'
Assert-Contract (-not $content.Contains('NERV_IIP_TEST_SALES_ORDER_ID', [StringComparison]::Ordinal)) 'Fault injection must resolve the seeded order identity from DemandPlanning persistence instead of fragile shell output.'
Assert-Contract ($content.Contains('out-of-order', [StringComparison]::Ordinal)) 'Verify script must assert stale/out-of-order convergence.'
Assert-Contract ($content.Contains('$runningResult.Stdout', [StringComparison]::Ordinal)) 'Verify script must parse the compose service list from Invoke-NativeCommandOutput.Stdout before cleanup ownership is decided.'
Assert-Contract ($content.Contains('UnitTestResult', [StringComparison]::Ordinal)) 'Verify script must prove the external fault-injection test actually executed and passed.'
Assert-Contract ($content.Contains("local-name()='Counters'", [StringComparison]::Ordinal)) 'Verify script must read the probe TRX counters; one named passing result cannot rule out other failed or skipped tests in the same run.'
Assert-Contract ($content.Contains('executed=1 passed=1 failed=0 skipped=0', [StringComparison]::Ordinal)) 'Verify script must assert the exact FullChain probe accounting.'
Assert-Contract ($content.Contains('Assert-DemandStable', [StringComparison]::Ordinal)) 'Verify script must hold the final cancellation state stable after stale-message injection.'
Assert-Contract (-not $content.Contains('Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres', [StringComparison]::Ordinal)) 'The cross-process acceptance script must not duplicate the Redis/CAP lane identical-idempotency-key proof.'
Assert-Contract (-not $content.Contains('Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail', [StringComparison]::Ordinal)) 'The cross-process acceptance script must not duplicate the Redis/CAP lane fallback-scan proof.'
Assert-Contract ($content.Contains('changed during the stability window', [StringComparison]::Ordinal)) 'Verify script must fail immediately when the final demand changes during the stability window.'
Assert-Contract ($content.Contains("Wait-Demand -DemandPlanningUrl `$demandPlanningUrl -Headers `$headers -Version 4 -Quantity 0 -Status 'cancelled'", [StringComparison]::Ordinal)) 'Verify script must wait for cancellation convergence before entering the strict stability window.'
Assert-Contract ($content.Contains('sourceVersion', [StringComparison]::Ordinal)) 'Verify script must assert business-version convergence.'
Assert-Contract ($content.Contains('sourceStatus', [StringComparison]::Ordinal)) 'Verify script must assert lifecycle-status convergence.'
Assert-Contract ($content.Contains('finally', [StringComparison]::Ordinal)) 'Verify script must clean up processes and disposable infrastructure in finally.'
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
Assert-Contract ($content.Contains('Original acceptance failure preserved; cleanup also failed:', [StringComparison]::Ordinal)) 'Cleanup failures must be reported without masking the original acceptance failure.'
# 清理必须给出「剩余=0」的账，而不只是发出停止请求。
Assert-Contract ($content.Contains('Get-Man517RemainingProcessNames -Descriptors', [StringComparison]::Ordinal)) 'Cleanup must verify every owned process is actually gone, not only that a stop was requested.'
Assert-Contract ((Get-FunctionContractText -Name 'Get-Man517RemainingProcessNames').Contains('StartTime', [StringComparison]::Ordinal)) 'Process cleanup verification must confirm identity by start time, because PIDs are reused.'
Assert-Contract ($content.Contains("SELECT count(*) FROM pg_database WHERE datname = '`$databaseName';", [StringComparison]::Ordinal)) 'Cleanup must verify the exact disposable database is gone, and only that one.'
Assert-Contract ($content.Contains('disposable database still present', [StringComparison]::Ordinal)) 'A surviving disposable database must be reported as a cleanup failure.'
Assert-Contract ($content.Contains('script-owned compose services still running', [StringComparison]::Ordinal)) 'Cleanup must verify only the compose services this run started are gone.'
Assert-Contract ($content.Contains('cleanup-evidence.json', [StringComparison]::Ordinal)) 'Cleanup accounting must be written as reusable evidence.'
Assert-Contract ($content.Contains('sales-order-demand-planning-evidence.json', [StringComparison]::Ordinal)) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('lastHttpStatus', [StringComparison]::Ordinal)) 'Wait-Demand must preserve the last HTTP status.'
Assert-Contract ($content.Contains('lastResponseBody', [StringComparison]::Ordinal)) 'Wait-Demand must preserve the last HTTP response body.'
Assert-Contract ($content.Contains('lastRequestException', [StringComparison]::Ordinal)) 'Wait-Demand must preserve the last request exception.'
Assert-Contract ($content.Contains('lastObservedDemand', [StringComparison]::Ordinal)) 'Wait-Demand must preserve the last observed version, quantity, and status.'
Assert-Contract ($content.Contains('Export-Man517FailureDiagnostics', [StringComparison]::Ordinal)) 'The acceptance script must export DB, Redis, and log diagnostics before cleanup.'
Assert-Contract ($content.Contains('Protect-ScriptAutomationText', [StringComparison]::Ordinal)) 'Failure diagnostics must reuse the governed shared redactor.'
Assert-Contract ($content.Contains('Protect-Man517DiagnosticText -Text $lastObservation', [StringComparison]::Ordinal)) 'Wait-Demand must redact its last observation before throwing to CI logs.'
Assert-Contract ($content.Contains("Cap__FailedRetryInterval = '2'", [StringComparison]::Ordinal)) 'Acceptance must configure a short failed-message scan interval.'
Assert-Contract ($content.Contains("Cap__FallbackWindowLookbackSeconds = '30'", [StringComparison]::Ordinal)) 'Acceptance must configure CAP safe-minimum fallback eligibility.'
Assert-Contract ($content.Contains('erp.cap_published_messages', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the ERP CAP outbox state.'
Assert-Contract ($content.Contains('demand_planning.cap_received_messages', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the DemandPlanning CAP inbox state.'
Assert-Contract ($content.Contains('processed_integration_events', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the durable DemandPlanning consumer inbox.'
Assert-Contract ($content.Contains('integration_event_dead_letters', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the DemandPlanning DLQ.'
Assert-Contract ($content.Contains('sales_order_demand_projections', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the sales-order watermark projection.'
Assert-Contract ($content.Contains('demand_sources', [StringComparison]::Ordinal)) 'Failure diagnostics must capture the projected demand source.'
Assert-Contract ($content.Contains('XPENDING', [StringComparison]::Ordinal)) 'Failure diagnostics must capture Redis pending-entry metadata.'
Assert-Contract ($workflowContent.Contains('if: always()', [StringComparison]::Ordinal)) 'CI must upload MAN-517 diagnostics even when verification fails.'
Assert-Contract ($workflowContent.Contains('actions/upload-artifact@v4', [StringComparison]::Ordinal)) 'CI must retain MAN-517 diagnostics as an artifact.'

. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
Invoke-PwshScript `
    -ScriptPath $governanceScript `
    -Arguments @('-Path', $fixtureScript) `
    -WorkingDirectory $repoRoot `
    -Name 'man703-fixture-governance' | Out-Null
$unsafeDiagnostic = 'pwd=pwd-value token=token-value secret=secret-value client_secret=client-value Authorization: Bearer bearer-value Password=password-value'
$safeDiagnostic = Protect-ScriptAutomationText $unsafeDiagnostic
foreach ($sensitiveValue in @('pwd-value', 'token-value', 'secret-value', 'client-value', 'bearer-value', 'password-value')) {
    Assert-Contract (-not $safeDiagnostic.Contains($sensitiveValue, [StringComparison]::Ordinal)) "Shared diagnostic redaction leaked $sensitiveValue."
}

Write-Host 'ERP sales-order DemandPlanning cross-process verify script contract tests passed.'
