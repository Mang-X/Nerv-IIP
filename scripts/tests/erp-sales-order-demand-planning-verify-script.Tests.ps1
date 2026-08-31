# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the ERP sales-order to DemandPlanning cross-process verification script
#     - Runs the exact script-governance gate for the MAN-703 HTTP fixture
#   Writes:
#     - artifacts/script-logs/man703-fixture-governance/**
#     - A temporary canonical-result failure fixture under .superpowers/sdd/**
#   Cleanup:
#     - Removes the temporary canonical-result failure fixture
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-erp-sales-order-demand-planning.ps1'
$runtimeRunner = Join-Path $repoRoot 'scripts/run-acceptance-scenario-matrix.ps1'
$fixtureScript = Join-Path $repoRoot 'scripts/tests/fixtures/man703-http-fixture.ps1'
$governanceScript = Join-Path $repoRoot 'scripts/check-script-governance.ps1'
$ciWorkflow = Join-Path $repoRoot '.github/workflows/ci.yml'
if (-not (Test-Path -LiteralPath $verifyScript)) {
    throw 'ERP sales-order DemandPlanning cross-process verify script is missing.'
}

$content = Get-Content -LiteralPath $verifyScript -Raw
$runtimeRunnerContent = Get-Content -LiteralPath $runtimeRunner -Raw
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

# --- NERV-1874 / MAN-517 readiness identity contract ----------------------------------------
# Generic /health is shared by every business process. Readiness therefore has
# to prove the managed process still owns its reserved port and that the
# response came from the service-specific read-only route before business flow.
$readinessFunctionAst = Get-FunctionDefinitionAst -Name 'Wait-Healthy'
$readinessFunctionText = Get-FunctionContractText -Name 'Wait-Healthy'
Assert-Contract ($null -ne $readinessFunctionAst) 'MAN-517 readiness must have one shared readiness function.'
foreach ($parameterName in @('ServiceContract', 'Headers', 'Ownership', 'ManagedProcess', 'Observation')) {
    Assert-Contract ($null -ne (Get-ParameterAst -Function $readinessFunctionAst -Name $parameterName)) "MAN-517 readiness must bind its canonical $parameterName explicitly."
}
foreach ($forbiddenParameterName in @('ServiceName', 'IdentityUri', 'ExpectedCommand', 'ExpectedArguments')) {
    Assert-Contract ($null -eq (Get-ParameterAst -Function $readinessFunctionAst -Name $forbiddenParameterName)) "MAN-517 readiness must not accept caller-selected $forbiddenParameterName."
}
Assert-Contract ($readinessFunctionText.Contains('Read-Man517ListenerAuthority', [StringComparison]::Ordinal)) 'Readiness must re-check the exact managed listener authority before accepting health.'
Assert-Contract ($readinessFunctionText.Contains('Test-Man517ServiceIdentityResponse', [StringComparison]::Ordinal)) 'Readiness must validate a service-specific response shape instead of accepting generic /health.'
Assert-Contract ($readinessFunctionText.Contains('service identity mismatch', [StringComparison]::Ordinal)) 'Readiness failures must identify a wrong service on the invocation port.'
Assert-Contract ($readinessFunctionText.Contains('Read-Man517ProcessIdentity', [StringComparison]::Ordinal)) 'Readiness must read the actual operating-system process identity before any HTTP request.'
Assert-Contract ($readinessFunctionText.Contains('canonical process identity mismatch', [StringComparison]::Ordinal)) 'Readiness must bind the managed process executable and arguments to the canonical service contract.'
Assert-Contract ($readinessFunctionText.Contains('unavailable', [StringComparison]::Ordinal)) 'Readiness observations must report unavailable when an HTTP response was not reached.'
Assert-Contract ($readinessFunctionText.Contains('ConvertTo-Json', [StringComparison]::Ordinal)) 'Readiness observations must be normalized from the response returned by the real HTTP request.'
Assert-Contract (-not $readinessFunctionText.Contains('AllowEmptyDemandPlanning', [StringComparison]::Ordinal)) 'DemandPlanning empty-result policy must come from the canonical contract, not a readiness call-site switch.'
$readinessFailureText = Get-FunctionContractText -Name 'New-Man517ReadinessFailure'
Assert-Contract ($readinessFailureText.Contains('Get-Man517ProcessFailureCause', [StringComparison]::Ordinal)) 'Early process exit diagnostics must retain the bind/root failure cause.'
Assert-Contract ($readinessFailureText.Contains('exitCode=', [StringComparison]::Ordinal)) 'Early process exit diagnostics must retain the managed process exit code.'
foreach ($readinessCall in (Get-CommandCallAsts -Name 'Wait-Healthy')) {
    foreach ($parameterName in @('ServiceContract', 'Headers', 'Ownership', 'ManagedProcess')) {
        Assert-Contract (Test-CommandHasParameter -Call $readinessCall -Name $parameterName) "Every MAN-517 readiness call must bind -$parameterName so service identity cannot be inferred from /health."
    }
    foreach ($forbiddenParameterName in @('ServiceName', 'IdentityUri', 'ExpectedCommand', 'ExpectedArguments')) {
        Assert-Contract (-not (Test-CommandHasParameter -Call $readinessCall -Name $forbiddenParameterName)) "Every MAN-517 readiness call must reject caller-selected -$forbiddenParameterName."
    }
}
Assert-Contract (-not $content.Contains('ExpectedCommand', [StringComparison]::Ordinal)) 'The verifier must have one canonical launch contract and no caller-selected ExpectedCommand boundary.'
Assert-Contract (-not $content.Contains('ExpectedArguments', [StringComparison]::Ordinal)) 'The verifier must have one canonical launch contract and no caller-selected ExpectedArguments boundary.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Get-Man517CanonicalServiceContract'))) 'MAN-517 must define one canonical service contract producer.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Compare-Man517CanonicalServiceContract'))) 'MAN-517 must compare caller input with a freshly produced canonical service contract.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Resolve-Man517CanonicalServiceContract'))) 'MAN-517 readiness entry points must resolve canonical service provenance before using launch or response fields.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Read-Man517ProcessIdentity'))) 'MAN-517 must read process executable and full arguments from the operating system.'
Assert-Contract ((Get-FunctionContractText -Name 'Read-Man517ProcessIdentity').Contains('Win32_Process', [StringComparison]::Ordinal) -or
    (Get-FunctionContractText -Name 'Read-Man517ProcessIdentity').Contains('/proc/', [StringComparison]::Ordinal) -or
    (Get-FunctionContractText -Name 'Read-Man517ProcessIdentity').Contains("'/bin/ps'", [StringComparison]::Ordinal)) 'MAN-517 OS process readback must use a platform process authority.'
Assert-Contract ((Get-FunctionContractText -Name 'Start-Man517OwnedProcess').Contains('ServiceContract', [StringComparison]::Ordinal)) 'Managed process startup must accept the canonical service contract.'
Assert-Contract (-not (Get-FunctionContractText -Name 'Start-Man517OwnedProcess').Contains('ActualProcessOverride', [StringComparison]::Ordinal)) 'Canonical managed process startup must not expose a caller-selected actual-process override.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Start-Man517ForgedResponderProcess'))) 'The forged production-entry responder must have one centralized launch recipe.'
foreach ($identityRoute in @(
        '/api/business/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=work-center',
        '/api/business/v1/planning/demands?organizationId=org-001&environmentId=env-dev',
        '/api/business/v1/erp/sales-orders?organizationId=org-001&environmentId=env-dev')) {
    Assert-Contract ($content.Contains($identityRoute, [StringComparison]::Ordinal)) "MAN-517 readiness must use the existing service-specific read-only route '$identityRoute'."
}
Assert-Contract ($content.Contains('identityUri=', [StringComparison]::Ordinal)) 'Readiness diagnostics must retain the exact identity route that failed.'

# The readiness contract is also exercised from the production verifier entry
# point. Each negative control must use a real managed process/HTTP listener and
# retain its own failure plus zero-process/zero-port cleanup readback; helper
# shape tests alone cannot prove ownership or bind diagnostics.
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Invoke-Man517ReadinessNegativeProbes'))) 'MAN-517 must expose a production-entry readiness negative-probe matrix.'
foreach ($negativeScenarioId in @('wrong-service-port', 'bind-address-in-use', 'wrong-port', 'pid-reuse', 'response-identity-forged')) {
    Assert-Contract ($content.Contains($negativeScenarioId, [StringComparison]::Ordinal)) "MAN-517 production readiness evidence must retain the '$negativeScenarioId' counterexample."
}
Assert-Contract ($content.Contains('readiness-negative-evidence.json', [StringComparison]::Ordinal)) 'MAN-517 readiness negative probes must write retained evidence independently of positive FullChain evidence.'
Assert-Contract ($content.Contains('remainingPorts', [StringComparison]::Ordinal)) 'Each readiness negative probe must verify exact port cleanup.'
Assert-Contract ($content.Contains('remainingProcesses', [StringComparison]::Ordinal)) 'Each readiness negative probe must verify exact process cleanup.'
Assert-Contract ($content.Contains('full-shape', [StringComparison]::OrdinalIgnoreCase)) 'The forged production-entry case must exercise a full-shape DemandPlanning response.'
Assert-Contract ($content.Contains('readinessAcceptedUnexpectedly', [StringComparison]::Ordinal)) 'A negative probe must fail when readiness unexpectedly returns success, rather than synthesizing a failure observation.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Assert-Man517ReadinessNegativeEvidence'))) 'MAN-517 must validate retained negative evidence instead of trusting the probe summary.'
Assert-Contract ((Get-FunctionContractText -Name 'Start-Man517ForgedResponderProcess').Contains("Get-Command -Name 'pwsh'", [StringComparison]::Ordinal) -and
    (Get-FunctionContractText -Name 'Start-Man517ForgedResponderProcess').Contains('Start-ManagedBackgroundProcess', [StringComparison]::Ordinal)) 'Forged identity response must come from a real managed HTTP responder, not an injected function mock.'
Assert-Contract ($content.Contains('TcpListener', [StringComparison]::Ordinal)) 'Bind and wrong-port readiness negatives must use real TCP listeners.'

# The response-shape guard is deliberately exercised with the shared /health
# envelope and each service's existing read-only envelope. A generic 200/true
# response must never be accepted as one of the three service identities.
$identityFunctionText = Get-FunctionContractText -Name 'Test-Man517ServiceIdentityResponse'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($identityFunctionText)) 'The service identity response validator must be present for behavioral contract coverage.'
$canonicalContractFunctionText = Get-FunctionContractText -Name 'Get-Man517CanonicalServiceContract'
Invoke-Expression $canonicalContractFunctionText
$canonicalContractComparisonText = Get-FunctionContractText -Name 'Compare-Man517CanonicalServiceContract'
Invoke-Expression $canonicalContractComparisonText
$canonicalContractResolverText = Get-FunctionContractText -Name 'Resolve-Man517CanonicalServiceContract'
Invoke-Expression $canonicalContractResolverText
$canonicalContractValidatorText = Get-FunctionContractText -Name 'Test-Man517CanonicalServiceContract'
Invoke-Expression $canonicalContractValidatorText
Invoke-Expression $identityFunctionText
$genericHealthEnvelope = [pscustomobject]@{ success = $true; data = [pscustomobject]@{} }
foreach ($serviceName in @('masterdata', 'demand-planning', 'erp')) {
    $serviceContract = Get-Man517CanonicalServiceContract -ServiceName $serviceName -Port 54321
    Assert-Contract (-not (Test-Man517ServiceIdentityResponse -ServiceContract $serviceContract -Response $genericHealthEnvelope)) "Generic /health response must not identify '$serviceName'."
}
$masterDataShapeContract = Get-Man517CanonicalServiceContract -ServiceName 'masterdata' -Port 54321
Assert-Contract (Test-Man517ServiceIdentityResponse -ServiceContract $masterDataShapeContract -Response ([pscustomobject]@{
    success = $true; data = [pscustomobject]@{ resources = @(); total = 0 }
})) 'MasterData identity must accept the existing resources-list response shape.'
$demandPlanningShapeContract = Get-Man517CanonicalServiceContract -ServiceName 'demand-planning' -Port 54321
Assert-Contract (-not (Test-Man517ServiceIdentityResponse -ServiceContract $demandPlanningShapeContract -Response ([pscustomobject]@{
    success = $true; data = @()
}))) 'DemandPlanning identity must reject an empty array unless an independently verified managed process allows the legitimate empty read result.'
Assert-Contract (-not (Test-Man517ServiceIdentityResponse -ServiceContract $demandPlanningShapeContract -Response ([pscustomobject]@{
    success = $true; data = @([pscustomobject]@{ sourceReference = 'forged' })
}))) 'DemandPlanning identity must reject a legal envelope containing a forged row without the complete service response contract.'
Assert-Contract (Test-Man517ServiceIdentityResponse -ServiceContract $demandPlanningShapeContract -Response ([pscustomobject]@{
    success = $true; data = @([pscustomobject]@{
        demandSourceId = 'demand-001'
        demandType = 'sales-order'
        sourceReference = 'SO-DEMO-001'
        sourceLineReference = '10'
        customerCode = 'CUST-001'
        sourceVersion = 1
        sourceStatus = 'active'
        skuCode = 'SKU-001'
        uomCode = 'EA'
        siteCode = 'SITE-001'
        quantity = 2
        dueDate = '2026-08-15'
    })
})) 'DemandPlanning identity must accept the existing demand-list response shape.'
$erpShapeContract = Get-Man517CanonicalServiceContract -ServiceName 'erp' -Port 54321
Assert-Contract (Test-Man517ServiceIdentityResponse -ServiceContract $erpShapeContract -Response ([pscustomobject]@{
    success = $true; data = [pscustomobject]@{ items = @(); total = 0 }
})) 'ERP identity must accept the existing sales-order-list response shape.'

# Exercise the exact readiness function with an equivalent wrong-service
# response and an already-exited managed process. The injected responders are
# limited to this source-contract test; the production FullChain run below
# remains the real-process/real-HTTP evidence owner.
$failureCauseFunctionText = Get-FunctionContractText -Name 'Get-Man517ProcessFailureCause'
Invoke-Expression $failureCauseFunctionText
Invoke-Expression $readinessFailureText
$canonicalProcessIdentityText = Get-FunctionContractText -Name 'Test-Man517CanonicalProcessIdentity'
Invoke-Expression $canonicalProcessIdentityText
Invoke-Expression $readinessFunctionText
function Protect-Man517DiagnosticText([string]$Text) { return $Text }
function Read-Man517ListenerAuthority([object]$Ownership) {
    return [pscustomobject]@{
        ServiceName = $Ownership.ServiceName
        Port = $Ownership.Port
        OwnerProcessId = $Ownership.ProcessId
        OwnerProcessStartTime = $Ownership.ProcessStartTime
        ListenerProcessId = $Ownership.ProcessId
        ListenerProcessStartTime = $Ownership.ProcessStartTime
        ObservedAtUtc = [DateTimeOffset]::UtcNow
    }
}
function Read-Man517ProcessIdentity([int]$ProcessId) {
    $managedProcess = if ($ProcessId -eq 8) { $forgedProcess } else { $readinessProcess }
    return [pscustomobject]@{
        ProcessId = $managedProcess.ProcessId
        ProcessStartTime = $managedProcess.ProcessStartTime
        ExecutablePath = $managedProcess.ExecutablePath
        Arguments = @($managedProcess.Arguments)
        CommandLine = "$($managedProcess.ExecutablePath) $($managedProcess.Arguments -join ' ')"
        Provenance = 'test-process-authority'
    }
}
$readinessContract = Get-Man517CanonicalServiceContract -ServiceName 'demand-planning' -Port 54321
$script:readinessHealthCallCount = 0
$script:readinessIdentityCallCount = 0
function Invoke-RestMethod {
    param([string]$Method, [string]$Uri, [int]$TimeoutSec)
    $script:readinessHealthCallCount++
    return 'Healthy'
}
function Invoke-Man517JsonRequest {
    param([hashtable]$Headers, [string]$Uri, [string]$Stage, [datetime]$Deadline, [string]$Method, [AllowNull()][hashtable]$Observation)
    $script:readinessIdentityCallCount++
    return [pscustomobject]@{ success = $true; data = [pscustomobject]@{} }
}
$readinessOwnership = [pscustomobject]@{
    ServiceName = 'demand-planning'
    Port = 54321
    ProcessId = 7
    ProcessStartTime = [datetime]::Now
}
$readinessProcess = [pscustomobject]@{
    ProcessId = 7
    ExecutablePath = $readinessContract.LaunchExecutable
    Arguments = @($readinessContract.LaunchArguments)
    ProcessStartTime = $readinessOwnership.ProcessStartTime
    LogDirectory = 'test-logs'
    StderrPath = ''
    StdoutPath = ''
    Process = [pscustomobject]@{ HasExited = $false; ExitCode = 0 }
}
$wrongServiceFailure = $null
$wrongServiceObservation = @{}
try {
    Wait-Healthy -ServiceContract $readinessContract -Headers @{} -ManagedProcess $readinessProcess -Ownership $readinessOwnership -Observation $wrongServiceObservation -TimeoutSeconds 2 | Out-Null
}
catch { $wrongServiceFailure = $_.Exception }
Assert-Contract ($null -ne $wrongServiceFailure -and $wrongServiceFailure.Message.Contains('service identity mismatch', [StringComparison]::Ordinal)) "A wrong service returning generic Healthy must fail closed as an identity mismatch. Actual: failure=$($wrongServiceFailure | Out-String) observation=$($wrongServiceObservation | ConvertTo-Json -Compress) calls=$script:readinessHealthCallCount/$script:readinessIdentityCallCount"
Assert-Contract ($script:readinessHealthCallCount -eq 1 -and $script:readinessIdentityCallCount -eq 1) 'The wrong-service counterexample must reach the identity route once and must not proceed to business requests.'
Assert-Contract ($wrongServiceObservation.healthResponseObserved -eq $true -and $wrongServiceObservation.identityResponseObserved -eq $true) 'Readiness observation must record that both real HTTP responses were reached before rejecting the identity shape.'
Assert-Contract ([string]::Equals([string]$wrongServiceObservation.healthObservation, 'Healthy', [StringComparison]::Ordinal) -and [string]$wrongServiceObservation.identityObservation -match 'success') 'Readiness observation must retain normalized values returned by the actual health and identity responders.'

$forgedProcess = [pscustomobject]@{
    ProcessId = 8
    ExecutablePath = '/usr/local/microsoft/powershell/7/pwsh'
    Arguments = @([IO.Path]::GetFullPath($verifyScript))
    ProcessStartTime = $readinessOwnership.ProcessStartTime
    LogDirectory = 'test-logs'
    StderrPath = ''
    StdoutPath = ''
    Process = [pscustomobject]@{ HasExited = $false; ExitCode = 0 }
}
$callerMutatedContract = $readinessContract.PSObject.Copy()
$callerMutatedContract.LaunchExecutable = $forgedProcess.ExecutablePath
$callerMutatedContract.LaunchArguments = @($forgedProcess.Arguments)
$forgedProcessFailure = $null
$forgedProcessObservation = @{}
$script:readinessHealthCallCount = 0
$script:readinessIdentityCallCount = 0
try {
    Wait-Healthy -ServiceContract $readinessContract -Headers @{} -ManagedProcess $forgedProcess -Ownership $readinessOwnership -Observation $forgedProcessObservation -TimeoutSeconds 2 | Out-Null
}
catch { $forgedProcessFailure = $_.Exception }
Assert-Contract ($null -ne $forgedProcessFailure -and $forgedProcessFailure.Message.Contains('canonical process identity mismatch', [StringComparison]::Ordinal)) "A forged responder executable must fail before a generic health response can self-identify as DemandPlanning. Actual: failure=$($forgedProcessFailure | Out-String) observation=$($forgedProcessObservation | ConvertTo-Json -Compress) calls=$script:readinessHealthCallCount/$script:readinessIdentityCallCount"
Assert-Contract ($script:readinessHealthCallCount -eq 0 -and $script:readinessIdentityCallCount -eq 0) 'A forged responder command mismatch must not issue HTTP requests.'
Assert-Contract (-not $forgedProcessObservation.healthResponseObserved -and -not $forgedProcessObservation.identityResponseObserved -and [string]::Equals([string]$forgedProcessObservation.healthObservation, 'unavailable', [StringComparison]::Ordinal) -and [string]::Equals([string]$forgedProcessObservation.identityObservation, 'unavailable', [StringComparison]::Ordinal)) 'A command mismatch must retain unavailable HTTP observations.'

# A caller can make the forged process look canonical by rewriting the launch
# fields on an otherwise valid contract. The production entry must reject that
# mutation before consulting either HTTP endpoint, even when the OS readback
# exactly matches the rewritten fields.
$callerMutationFailure = $null
$callerMutationOwnership = $readinessOwnership.PSObject.Copy()
$callerMutationOwnership.ProcessId = $forgedProcess.ProcessId
$callerMutationObservation = @{
    healthResponseObserved = $false
    identityResponseObserved = $false
    healthObservation = 'unavailable'
    identityObservation = 'unavailable'
}
$script:readinessHealthCallCount = 0
$script:readinessIdentityCallCount = 0
try {
    Wait-Healthy -ServiceContract $callerMutatedContract -Headers @{} -ManagedProcess $forgedProcess -Ownership $callerMutationOwnership -Observation $callerMutationObservation -TimeoutSeconds 2 | Out-Null
}
catch { $callerMutationFailure = $_.Exception }
Assert-Contract ($null -ne $callerMutationFailure -and $callerMutationFailure.Message.Contains('canonical service contract producer', [StringComparison]::Ordinal)) "A caller-mutated launch contract must fail at the production readiness entry before process identity or HTTP acceptance. Actual: failure=$($callerMutationFailure | Out-String) observation=$($callerMutationObservation | ConvertTo-Json -Compress) calls=$script:readinessHealthCallCount/$script:readinessIdentityCallCount"
Assert-Contract ($script:readinessHealthCallCount -eq 0 -and $script:readinessIdentityCallCount -eq 0) 'A caller-mutated launch contract must not issue health or identity requests.'
Assert-Contract (-not $callerMutationObservation.healthResponseObserved -and -not $callerMutationObservation.identityResponseObserved -and [string]::Equals([string]$callerMutationObservation.healthObservation, 'unavailable', [StringComparison]::Ordinal) -and [string]::Equals([string]$callerMutationObservation.identityObservation, 'unavailable', [StringComparison]::Ordinal)) 'A caller-mutated contract must retain unavailable HTTP observations.'

$readinessProcess.Process.HasExited = $true
$readinessProcess.Process.ExitCode = 73
$script:readinessHealthCallCount = 0
$script:readinessIdentityCallCount = 0
$earlyExitFailure = $null
$earlyExitObservation = @{}
try {
    Wait-Healthy -ServiceContract $readinessContract -Headers @{} -ManagedProcess $readinessProcess -Ownership $readinessOwnership -Observation $earlyExitObservation -TimeoutSeconds 2 | Out-Null
}
catch { $earlyExitFailure = $_.Exception }
Assert-Contract ($null -ne $earlyExitFailure -and $earlyExitFailure.Message.Contains('exitCode=73', [StringComparison]::Ordinal)) 'An exited target process must fail closed with its exact exit code.'
Assert-Contract ($script:readinessHealthCallCount -eq 0 -and $script:readinessIdentityCallCount -eq 0) 'An exited target process must not issue health, identity, or business requests.'
Assert-Contract (-not $earlyExitObservation.healthResponseObserved -and -not $earlyExitObservation.identityResponseObserved -and [string]::Equals([string]$earlyExitObservation.healthObservation, 'unavailable', [StringComparison]::Ordinal) -and [string]::Equals([string]$earlyExitObservation.identityObservation, 'unavailable', [StringComparison]::Ordinal)) 'Readiness observation must retain unavailable for both HTTP stages when the managed process exits before any request.'
$failureLogPath = Join-Path ([IO.Path]::GetTempPath()) "man517-bind-cause-$([Guid]::NewGuid().ToString('N')).log"
try {
    [IO.File]::WriteAllText($failureLogPath, "System.IO.IOException: Failed to bind to address`n ---> Microsoft.AspNetCore.Connections.AddressInUseException: Address already in use`n ---> System.Net.Sockets.SocketException (48): Address already in use")
    $readinessProcess.StderrPath = $failureLogPath
    $bindCause = Get-Man517ProcessFailureCause -ManagedProcess $readinessProcess
    Assert-Contract ($bindCause.Contains('AddressInUseException', [StringComparison]::Ordinal) -and $bindCause.Contains('SocketException', [StringComparison]::Ordinal)) 'Early bind diagnostics must retain both the AddressInUseException and innermost SocketException evidence.'
}
finally {
    if (Test-Path -LiteralPath $failureLogPath) { Remove-Item -LiteralPath $failureLogPath -Force }
}

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
Assert-Contract ($content.Contains('script-owned compose cleanup did not converge before deadline', [StringComparison]::Ordinal)) 'Cleanup must fail when its owned Compose services do not converge before the deadline.'
Assert-Contract ($content.Contains('cleanup-evidence.json', [StringComparison]::Ordinal)) 'Cleanup accounting must be written as reusable evidence.'
Assert-Contract ($content.Contains('sales-order-demand-planning-evidence.json', [StringComparison]::Ordinal)) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('$readinessIdentityReadback', [StringComparison]::Ordinal)) 'Acceptance evidence must retain the verified service-specific identity route for every managed process.'
Assert-Contract ($content.Contains('readinessIdentity =', [StringComparison]::Ordinal)) 'Acceptance evidence must publish readiness identity separately from generic health and port ownership.'

# #2957 Regression：Compose stop 返回后的第一次状态读取仍可能短暂看到 owned service。
# 直接执行生产 observation core，并以单一可控 runtime 证明收敛、尾窗、永久残留、readback 失败与 ownership。
$composeWaitFunctionText = Get-FunctionContractText -Name 'Wait-Man517OwnedComposeServicesStopped'
$composeObservationCoreFunctionText = Get-FunctionContractText -Name 'Invoke-Man517OwnedComposeServicesStoppedObservation'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($composeWaitFunctionText)) 'Verify script must define bounded observation for owned Compose services.'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($composeObservationCoreFunctionText)) 'Verify script must define the production Compose observation core.'
Assert-Contract ($composeWaitFunctionText.Contains('[System.Diagnostics.Stopwatch]::StartNew()', [StringComparison]::Ordinal)) 'The observer must own its monotonic clock lifecycle.'
Assert-Contract ($composeWaitFunctionText.Contains('[System.Threading.Tasks.Task]::Delay(', [StringComparison]::Ordinal)) 'The observer must own its observation cadence.'
Assert-Contract (-not $composeWaitFunctionText.Contains('[object]$Clock', [StringComparison]::Ordinal)) 'The observer must not expose a weak replaceable clock.'
Assert-Contract ([string]::IsNullOrWhiteSpace((Get-FunctionContractText -Name 'Wait-Man517ComposeObservationCadence'))) 'Compose cadence must not be split into a thin global wrapper.'
$composeWaitFunctionAst = Get-FunctionDefinitionAst -Name 'Wait-Man517OwnedComposeServicesStopped'
Assert-Contract ((Get-CommandCallAsts -Name 'Invoke-Man517OwnedComposeServicesStoppedObservation' -Scope $composeWaitFunctionAst).Count -eq 1) 'The real-clock observer must delegate exactly once to the tested production observation core.'
Invoke-Expression $composeObservationCoreFunctionText

$script:composeObservedBudgets = [System.Collections.Generic.List[int]]::new()
$script:composeRuntimeDelayCalls = [System.Collections.Generic.List[int]]::new()
$script:composeObservationMode = 'sequence'
$script:composeObservationQueue = $null
$script:composeFixtureElapsedMilliseconds = [long]0
$script:composeQueryDurationMilliseconds = 0
$script:composeFixtureRuntime = [System.Func[string, int, long]]{
    param([string]$Operation, [int]$Milliseconds)
    if ([string]::Equals($Operation, 'delay', [StringComparison]::Ordinal)) {
        $script:composeRuntimeDelayCalls.Add($Milliseconds)
        $script:composeFixtureElapsedMilliseconds += $Milliseconds
    }
    return $script:composeFixtureElapsedMilliseconds
}
function Protect-ScriptAutomationText { param([AllowNull()][string]$Text) return $Text }
function Get-Man517ComposeRunningServicesObservation {
    param([string]$ComposeFile, [int]$Attempt, [int]$RemainingDeadlineMilliseconds)
    $script:composeObservedBudgets.Add($RemainingDeadlineMilliseconds)
    if ($script:composeQueryDurationMilliseconds -gt 0) {
        $script:composeFixtureElapsedMilliseconds += $script:composeQueryDurationMilliseconds
        if (-not [string]::Equals($script:composeObservationMode, 'persistent', [StringComparison]::Ordinal)) {
            $script:composeQueryDurationMilliseconds = 0
        }
    }
    if ([string]::Equals($script:composeObservationMode, 'readback-failure', [StringComparison]::Ordinal)) {
        $failure = [InvalidOperationException]::new("fixture canonical readback unavailable at attempt $Attempt")
        $failure.Data['Query'] = 'fixture compose ps'
        $failure.Data['LogPath'] = $null
        $failure.Data['LogStatus'] = 'unavailable'
        $failure.Data['LogUnavailableReason'] = 'fixture canonical readback unavailable'
        throw $failure
    }
    if ([string]::Equals($script:composeObservationMode, 'persistent', [StringComparison]::Ordinal)) {
        return [pscustomobject]@{ runningServices = @('postgres'); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = "fixture://persistent/attempt-$Attempt"; logStatus = 'available'; logUnavailableReason = $null }
    }
    return $script:composeObservationQueue.Dequeue()
}

$transientSequence = [System.Collections.Generic.Queue[object]]::new()
$transientSequence.Enqueue([pscustomobject]@{ runningServices = @('postgres'); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://transient/attempt-1'; logStatus = 'available'; logUnavailableReason = $null })
$transientSequence.Enqueue([pscustomobject]@{ runningServices = @(); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://transient/attempt-2'; logStatus = 'available'; logUnavailableReason = $null })
$oldSingleSample = $transientSequence.Peek()
Assert-Contract (@($oldSingleSample.runningServices).Count -eq 1) 'The regression fixture must make the old single-sample implementation fail on its first observation.'
$script:composeObservationMode = 'sequence'
$script:composeObservationQueue = $transientSequence
$script:composeFixtureElapsedMilliseconds = 0
$script:composeQueryDurationMilliseconds = 40
$script:composeObservedBudgets.Clear()
$script:composeRuntimeDelayCalls.Clear()
$transientResult = Invoke-Man517OwnedComposeServicesStoppedObservation -OwnedServices @('postgres') -ComposeFile 'fixture-compose.yml' -DeadlineMilliseconds 3000 -Runtime $script:composeFixtureRuntime
Assert-Contract $transientResult.converged 'The production observer must converge across postgres -> empty.'
Assert-Contract ($transientResult.attempts -eq 2 -and @($transientResult.remainingNames).Count -eq 0) 'Transient convergence must consume both observations and report remaining=0.'
Assert-Contract ($transientResult.elapsedMilliseconds -eq 290 -and $script:composeRuntimeDelayCalls.Count -eq 1 -and $script:composeRuntimeDelayCalls[0] -eq 250) 'Transient convergence must deterministically pace its two observations without a busy loop.'

$script:composeObservationMode = 'persistent'
$script:composeFixtureElapsedMilliseconds = 0
$script:composeQueryDurationMilliseconds = 10
$script:composeObservedBudgets.Clear()
$script:composeRuntimeDelayCalls.Clear()
$persistentResult = Invoke-Man517OwnedComposeServicesStoppedObservation -OwnedServices @('postgres') -ComposeFile 'fixture-compose.yml' -DeadlineMilliseconds 1000 -Runtime $script:composeFixtureRuntime
Assert-Contract (-not $persistentResult.converged -and [string]::Equals($persistentResult.status, 'timed-out', [StringComparison]::Ordinal)) 'A permanent owned residual must fail closed at the deadline.'
Assert-Contract ($persistentResult.attempts -gt 2 -and $persistentResult.elapsedMilliseconds -eq $persistentResult.deadlineMilliseconds) 'Permanent residual must use repeated observation and stop at the controlled deadline.'
Assert-Contract (@($persistentResult.remainingNames).Count -eq 1 -and [string]::Equals([string]$persistentResult.remainingNames[0], 'postgres', [StringComparison]::Ordinal)) 'Permanent residual evidence must retain the owned service name.'
foreach ($diagnosticField in @('lastObservation', 'query', 'logPath')) {
    Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$persistentResult.$diagnosticField)) "Permanent residual evidence must retain $diagnosticField."
}

# 第一次查询占用 1600ms，尾窗首次 readback 仍为残留；下一次 fresh readback 才为空。
$tailWindowSequence = [System.Collections.Generic.Queue[object]]::new()
$tailWindowSequence.Enqueue([pscustomobject]@{ runningServices = @('postgres'); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://tail/attempt-1'; logStatus = 'available'; logUnavailableReason = $null })
$tailWindowSequence.Enqueue([pscustomobject]@{ runningServices = @('postgres'); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://tail/attempt-2'; logStatus = 'available'; logUnavailableReason = $null })
$tailWindowSequence.Enqueue([pscustomobject]@{ runningServices = @(); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://tail/attempt-3'; logStatus = 'available'; logUnavailableReason = $null })
$script:composeObservationMode = 'sequence'
$script:composeObservationQueue = $tailWindowSequence
$script:composeFixtureElapsedMilliseconds = 0
$script:composeQueryDurationMilliseconds = 1600
$script:composeObservedBudgets.Clear()
$script:composeRuntimeDelayCalls.Clear()
$tailWindowResult = Invoke-Man517OwnedComposeServicesStoppedObservation -OwnedServices @('postgres') -ComposeFile 'fixture-compose.yml' -DeadlineMilliseconds 2000 -Runtime $script:composeFixtureRuntime
Assert-Contract ($tailWindowResult.converged -and $tailWindowResult.attempts -eq 3 -and $tailWindowResult.elapsedMilliseconds -eq 1750) 'A service that stops after the first tail-window readback must still converge on the next fresh observation.'
Assert-Contract ($script:composeObservedBudgets.Count -eq 3 -and $script:composeObservedBudgets[0] -eq 2000 -and $script:composeObservedBudgets[1] -eq 250 -and $script:composeObservedBudgets[2] -eq 250) 'Every tail query must receive the exact controlled remaining millisecond budget without rounding up.'
Assert-Contract ($tailWindowSequence.Count -eq 0) 'Tail-window convergence must consume the third empty observation instead of reusing the second stale state.'

$foreignSequence = [System.Collections.Generic.Queue[object]]::new()
$foreignSequence.Enqueue([pscustomobject]@{ runningServices = @('postgres'); observedAtUtc = [DateTimeOffset]::UtcNow; query = 'fixture compose ps'; logPath = 'fixture://foreign/attempt-1'; logStatus = 'available'; logUnavailableReason = $null })
$script:composeObservationMode = 'sequence'
$script:composeObservationQueue = $foreignSequence
$script:composeFixtureElapsedMilliseconds = 0
$script:composeQueryDurationMilliseconds = 0
$script:composeObservedBudgets.Clear()
$script:composeRuntimeDelayCalls.Clear()
$foreignServiceResult = Invoke-Man517OwnedComposeServicesStoppedObservation -OwnedServices @('redis') -ComposeFile 'fixture-compose.yml' -DeadlineMilliseconds 3000 -Runtime $script:composeFixtureRuntime
Assert-Contract ($foreignServiceResult.converged -and @($foreignServiceResult.remainingNames).Count -eq 0) 'A running service not owned by this invocation must not enter the cleanup verdict.'

$readbackFailure = $null
try {
    $script:composeObservationMode = 'readback-failure'
    $script:composeFixtureElapsedMilliseconds = 0
    $script:composeQueryDurationMilliseconds = 25
    $script:composeObservedBudgets.Clear()
    $script:composeRuntimeDelayCalls.Clear()
    Invoke-Man517OwnedComposeServicesStoppedObservation -OwnedServices @('postgres') -ComposeFile 'fixture-compose.yml' -DeadlineMilliseconds 3000 -Runtime $script:composeFixtureRuntime | Out-Null
}
catch { $readbackFailure = $_.Exception }
Assert-Contract ($null -ne $readbackFailure) 'A failed Compose readback must fail closed instead of becoming remaining=0.'
foreach ($diagnosticField in @('deadlineMilliseconds', 'attempts', 'elapsedMilliseconds', 'lastObservation', 'fixture canonical readback unavailable')) {
    Assert-Contract ($readbackFailure.Message.Contains($diagnosticField, [StringComparison]::Ordinal)) "Readback failure must retain $diagnosticField."
}
Assert-Contract ([string]::Equals([string]$readbackFailure.Data['Query'], 'fixture compose ps', [StringComparison]::Ordinal)) 'Readback failure must retain the actual query.'
Assert-Contract ([string]::Equals([string]$readbackFailure.Data['LogStatus'], 'unavailable', [StringComparison]::Ordinal) -and -not [string]::IsNullOrWhiteSpace([string]$readbackFailure.Data['LogUnavailableReason'])) 'Readback failure must explicitly record unavailable log evidence and its reason.'
Assert-Contract (@($readbackFailure.Data['RemainingNames']).Count -eq 1) 'Readback failure must retain owned services as remaining rather than claiming zero.'

# cleanup-evidence.json 的失败态必须从生产投影写出 deadline、attempts、elapsed 与最后观察诊断。
$composeCleanupEvidenceFunctionText = Get-FunctionContractText -Name 'New-Man517ComposeCleanupEvidence'
Assert-Contract (-not [string]::IsNullOrWhiteSpace($composeCleanupEvidenceFunctionText)) 'Verify script must define the production Compose cleanup evidence projection.'
Assert-Contract ($content.Contains('composeServices = New-Man517ComposeCleanupEvidence', [StringComparison]::Ordinal)) 'cleanup-evidence.json must consume the tested Compose failure-state projection.'
Invoke-Expression $composeCleanupEvidenceFunctionText
$readbackObservation = [pscustomobject]@{
    status = 'readback-failed'
    deadlineMilliseconds = $readbackFailure.Data['DeadlineMilliseconds']
    attempts = $readbackFailure.Data['Attempts']
    elapsedMilliseconds = $readbackFailure.Data['ElapsedMilliseconds']
    remainingNames = [string[]]@($readbackFailure.Data['RemainingNames'])
    lastObservation = $readbackFailure.Data['LastObservation']
    query = $readbackFailure.Data['Query']
    logPath = $readbackFailure.Data['LogPath']
    logStatus = $readbackFailure.Data['LogStatus']
    logUnavailableReason = $readbackFailure.Data['LogUnavailableReason']
}
$cleanupEvidenceFixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-man517-cleanup-evidence-$([Guid]::NewGuid().ToString('N'))"
try {
    [System.IO.Directory]::CreateDirectory($cleanupEvidenceFixtureRoot) | Out-Null
    foreach ($failureEvidenceCase in @(
        @{ Name = 'persistent'; Observation = $persistentResult },
        @{ Name = 'readback'; Observation = $readbackObservation }
    )) {
        $cleanupEvidencePath = Join-Path $cleanupEvidenceFixtureRoot "$($failureEvidenceCase.Name)-cleanup-evidence.json"
        @{
            composeServices = New-Man517ComposeCleanupEvidence `
                -OwnedServices @('postgres') `
                -Observation $failureEvidenceCase.Observation
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cleanupEvidencePath -Encoding utf8
        $writtenComposeEvidence = (Get-Content -LiteralPath $cleanupEvidencePath -Raw | ConvertFrom-Json).composeServices
        Assert-Contract ([string]::Equals([string]$writtenComposeEvidence.status, [string]$failureEvidenceCase.Observation.status, [StringComparison]::Ordinal)) "$($failureEvidenceCase.Name) cleanup evidence must retain status."
        Assert-Contract ([int]$writtenComposeEvidence.deadlineMilliseconds -eq [int]$failureEvidenceCase.Observation.deadlineMilliseconds) "$($failureEvidenceCase.Name) cleanup evidence must retain deadlineMilliseconds."
        Assert-Contract ([int]$writtenComposeEvidence.attempts -eq [int]$failureEvidenceCase.Observation.attempts -and [int]$writtenComposeEvidence.attempts -gt 0) "$($failureEvidenceCase.Name) cleanup evidence must retain attempts."
        Assert-Contract ([long]$writtenComposeEvidence.elapsedMilliseconds -eq [long]$failureEvidenceCase.Observation.elapsedMilliseconds) "$($failureEvidenceCase.Name) cleanup evidence must retain elapsedMilliseconds."
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$writtenComposeEvidence.lastObservation) -and -not [string]::IsNullOrWhiteSpace([string]$writtenComposeEvidence.query)) "$($failureEvidenceCase.Name) cleanup evidence must retain the last observation and query."
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$writtenComposeEvidence.logStatus)) "$($failureEvidenceCase.Name) cleanup evidence must retain structured log availability."
    }
}
finally {
    if (Test-Path -LiteralPath $cleanupEvidenceFixtureRoot) { Remove-Item -LiteralPath $cleanupEvidenceFixtureRoot -Recurse -Force }
}

# 查询 adapter 只消费 canonical 一次性命令 seam；不拥有第二套 process wait/exit/stream/cleanup。
$composeObservationFunctionAst = Get-FunctionDefinitionAst -Name 'Get-Man517ComposeRunningServicesObservation'
$composeObservationFunctionText = Get-FunctionContractText -Name 'Get-Man517ComposeRunningServicesObservation'
Assert-Contract ($null -ne $composeObservationFunctionAst) 'Verify script must define the Compose state-query adapter.'
$canonicalObservationCalls = Get-CommandCallAsts -Name 'Invoke-NativeCommandOutput' -Scope $composeObservationFunctionAst
Assert-Contract ($canonicalObservationCalls.Count -eq 1) 'Compose readback must use exactly one canonical native-command call.'
Assert-Contract (-not (Test-CommandHasParameter -Call $canonicalObservationCalls[0] -Name 'PersistOutput')) 'Best-effort observation logging must not make canonical readback persistence verdict-affecting.'
Assert-Contract (-not (Test-CommandHasParameter -Call $canonicalObservationCalls[0] -Name 'LogDirectory')) 'Canonical readback must not touch an observation artifact path before the query succeeds.'
Assert-Contract ([string]::Equals((Get-CommandParameterValueText -Call $canonicalObservationCalls[0] -Name 'TimeoutMilliseconds'), '$RemainingDeadlineMilliseconds', [StringComparison]::Ordinal)) 'Compose readback must pass the exact remaining millisecond budget to the canonical seam.'
foreach ($forbiddenCommand in @('Start-ManagedBackgroundProcess', 'Start-Process', 'Stop-Process', 'Get-Content')) {
    Assert-Contract ((Get-CommandCallAsts -Name $forbiddenCommand -Scope $composeObservationFunctionAst).Count -eq 0) "Compose readback must not recreate local process lifecycle command '$forbiddenCommand'."
}
$composeStopCalls = @(Get-CommandCallAsts -Name 'Invoke-DockerCompose' | Where-Object { $_.Extent.Text.Contains("'stop'", [StringComparison]::Ordinal) })
Assert-Contract ($composeStopCalls.Count -eq 1) 'MAN-517 must issue exactly one owned Compose stop request.'

Invoke-Expression $composeObservationFunctionText
$script:capturedComposeQueryTimeoutMilliseconds = $null
$script:composeCanonicalFailure = $null
$script:composeEvidenceWriteFailure = $false
function Invoke-NativeCommandOutput {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$Name,
        [string]$LogDirectory,
        [switch]$PersistOutput,
        [int]$TimeoutMilliseconds
    )
    $script:capturedComposeQueryTimeoutMilliseconds = $TimeoutMilliseconds
    Assert-Contract (-not $PSBoundParameters.ContainsKey('LogDirectory') -and -not $PSBoundParameters.ContainsKey('PersistOutput')) 'Compose readback must run independently of best-effort observation persistence.'
    if ($null -ne $script:composeCanonicalFailure) { throw $script:composeCanonicalFailure }
    return [pscustomobject]@{ Stdout = "postgres`n"; Stderr = ''; LogDirectory = $null }
}
function Write-Man517DiagnosticFile {
    param([string]$Path, [AllowNull()][string]$Content)
    if ($script:composeEvidenceWriteFailure) { throw 'fixture observation artifact unavailable' }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    Set-Content -LiteralPath $Path -Value $Content -Encoding utf8
}
$adapterFixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-man517-compose-adapter-$([Guid]::NewGuid().ToString('N'))"
try {
    $root = $adapterFixtureRoot
    $adapterResult = Get-Man517ComposeRunningServicesObservation -ComposeFile 'fixture-compose.yml' -Attempt 1 -RemainingDeadlineMilliseconds 1950
    Assert-Contract ($script:capturedComposeQueryTimeoutMilliseconds -eq 1950) 'The production adapter must preserve the exact 1950ms remaining budget.'
    Assert-Contract (@($adapterResult.runningServices).Count -eq 1 -and [string]::Equals([string]$adapterResult.runningServices[0], 'postgres', [StringComparison]::Ordinal)) 'The production adapter must consume canonical stdout.'
    Assert-Contract ([string]::Equals([string]$adapterResult.logStatus, 'available', [StringComparison]::Ordinal) -and (Test-Path -LiteralPath $adapterResult.logPath -PathType Container)) 'Successful best-effort observation logging must publish its available path.'

    $script:composeEvidenceWriteFailure = $true
    $adapterWithoutLog = Get-Man517ComposeRunningServicesObservation -ComposeFile 'fixture-compose.yml' -Attempt 2 -RemainingDeadlineMilliseconds 825
    Assert-Contract (@($adapterWithoutLog.runningServices).Count -eq 1 -and [string]::Equals([string]$adapterWithoutLog.runningServices[0], 'postgres', [StringComparison]::Ordinal)) 'A valid readback verdict must survive observation-log persistence failure.'
    Assert-Contract ([string]::Equals([string]$adapterWithoutLog.logStatus, 'unavailable', [StringComparison]::Ordinal) -and $adapterWithoutLog.logUnavailableReason.Contains('fixture observation artifact unavailable', [StringComparison]::Ordinal)) 'Observation-log persistence failure must be recorded as unavailable with its reason.'
    $script:composeEvidenceWriteFailure = $false

    foreach ($failureCase in @(
        @{ Name = 'nonzero'; Exception = [InvalidOperationException]::new("Command 'docker' exited with 17. Output: fixture-nonzero") },
        @{ Name = 'signal'; Exception = [InvalidOperationException]::new("Command 'docker' exited with 137. Terminated by signal SIGKILL (9): fixture-signal") },
        @{ Name = 'timeout'; Exception = [TimeoutException]::new("Command 'docker' timed out after 125 milliseconds while reading output. Logs: fixture-timeout") },
        @{ Name = 'unavailable'; Exception = [InvalidOperationException]::new("Failed to start command 'docker'. fixture-unavailable") }
    )) {
        $script:composeCanonicalFailure = $failureCase.Exception
        $adapterFailure = $null
        try {
            Get-Man517ComposeRunningServicesObservation -ComposeFile 'fixture-compose.yml' -Attempt 3 -RemainingDeadlineMilliseconds 125 | Out-Null
        }
        catch { $adapterFailure = $_.Exception }
        Assert-Contract ($null -ne $adapterFailure) "Canonical $($failureCase.Name) readback failure must fail closed."
        Assert-Contract ($adapterFailure.Message.Contains($failureCase.Exception.Message, [StringComparison]::Ordinal)) "Canonical $($failureCase.Name) diagnostics must be preserved by the adapter."
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$adapterFailure.Data['Query'])) "Canonical $($failureCase.Name) failure must retain the actual query."
        Assert-Contract ([string]::Equals([string]$adapterFailure.Data['LogStatus'], 'unavailable', [StringComparison]::Ordinal) -and -not [string]::IsNullOrWhiteSpace([string]$adapterFailure.Data['LogUnavailableReason'])) "Canonical $($failureCase.Name) failure must record unavailable log evidence and its reason."
    }
}
finally {
    if (Test-Path -LiteralPath $adapterFixtureRoot) { Remove-Item -LiteralPath $adapterFixtureRoot -Recurse -Force }
}
$script:composeCanonicalFailure = $null

foreach ($parameterName in @('CanonicalResultPath', 'TrackIdentifier', 'Repository', 'RunId', 'RunAttempt', 'TestedSha', 'ManifestDigest', 'ScenarioId')) {
    $parameterMatches = @($scriptAst.ParamBlock.Parameters | Where-Object { [string]::Equals($_.Name.VariablePath.UserPath, $parameterName, [StringComparison]::OrdinalIgnoreCase) })
    Assert-Contract ($parameterMatches.Count -eq 1) "Verify script must accept caller-supplied canonical result parameter '$parameterName'."
}
Assert-Contract ($content.Contains('Write-NervAcceptanceCanonicalJson', [StringComparison]::Ordinal)) 'Canonical result must use the shared atomic JSON replacement helper.'
Assert-Contract ($content.Contains('Resolve-NervAcceptanceCanonicalOutputPath', [StringComparison]::Ordinal)) 'Canonical result must use the shared canonical physical output-path helper.'
Assert-Contract ($content.Contains('failureCaptureSupported = $true', [StringComparison]::Ordinal)) 'Canonical result must report the existing failure-diagnostic capability.'
Assert-Contract ($content.Contains('failureDiagnosticsCaptured = $false', [StringComparison]::Ordinal)) 'A successful canonical result must truthfully report that failure diagnostics were not captured.'
foreach ($businessFact in @('sourceStateCommittedBeforeMutation', 'changeV2Converged', 'changeV3Converged', 'duplicateConverged', 'outOfOrderConverged', 'cancellationConverged')) {
    Assert-Contract ($content.Contains($businessFact, [StringComparison]::Ordinal)) "Canonical result must publish actual business fact '$businessFact'."
}
foreach ($unsupportedClaim in @('http200BusinessErrorRejected', 'firstConsumeFailureRecovered', 'capturedBeforeCleanup')) {
    Assert-Contract (-not $content.Contains($unsupportedClaim, [StringComparison]::Ordinal)) "Verifier must not publish unsupported canonical claim '$unsupportedClaim'."
}
Assert-Contract ($content.Contains('Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events', [StringComparison]::Ordinal)) 'Canonical result must use the exact frozen test identity.'
$canonicalWriteIndex = $content.LastIndexOf('Write-NervAcceptanceCanonicalJson', [StringComparison]::Ordinal)
$acceptanceRethrowIndexForCanonical = $content.LastIndexOf('throw $acceptanceFailure', [StringComparison]::Ordinal)
$cleanupThrowIndexForCanonical = $content.LastIndexOf('throw "MAN-517 cleanup failed:', [StringComparison]::Ordinal)
Assert-Contract ($canonicalWriteIndex -gt $acceptanceRethrowIndexForCanonical -and $canonicalWriteIndex -gt $cleanupThrowIndexForCanonical) 'Canonical success may be atomically written only after acceptance and cleanup failures have been rejected.'
Assert-Contract ($runtimeRunnerContent.Contains('Invoke-PwshScript', [StringComparison]::Ordinal)) 'The default runtime action must invoke the governed ERP verifier adapter exactly once.'
Assert-Contract ($runtimeRunnerContent.Contains('-TimeoutSeconds ([int]$Contract.requiredSeconds)', [StringComparison]::Ordinal)) 'The default runtime adapter must preserve the full checked readiness, execution, diagnostics, cleanup, evidence, and safety budget.'
Assert-Contract ($runtimeRunnerContent.Contains('CanonicalResultPath', [StringComparison]::Ordinal)) 'The default runtime adapter must supply a caller-selected canonical result path.'
Assert-Contract ($runtimeRunnerContent.Contains('Read-NervAcceptanceRuntimeJsonSnapshot', [StringComparison]::Ordinal)) 'The default runtime adapter must consume the canonical result without reimplementing business steps.'
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

$canonicalFailureFixtureRoot = Join-Path $repoRoot ".superpowers/sdd/canonical-result-failure-$([Guid]::NewGuid().ToString('N'))"
$canonicalFailureResultPath = Join-Path $canonicalFailureFixtureRoot 'result.json'
try {
    [IO.Directory]::CreateDirectory($canonicalFailureFixtureRoot) | Out-Null
    [IO.File]::WriteAllText($canonicalFailureResultPath, '{"conclusion":"passed"}', [Text.UTF8Encoding]::new($false))
    $canonicalFailureObserved = $false
    try {
        Invoke-PwshScript `
            -ScriptPath $verifyScript `
            -Arguments @(
                '-PostgresAdminConnectionString', '',
                '-RedisConnectionString', '',
                '-CanonicalResultPath', $canonicalFailureResultPath,
                '-TrackIdentifier', 'shadow',
                '-Repository', 'Mang-X/Nerv-IIP',
                '-RunId', '123456789',
                '-RunAttempt', '2',
                '-TestedSha', '0123456789abcdef0123456789abcdef01234567',
                '-ManifestDigest', ('a' * 64),
                '-ScenarioId', 'sales-order-demand'
            ) `
            -WorkingDirectory $repoRoot `
            -Name 'man517-canonical-failure-fixture' | Out-Null
    }
    catch { $canonicalFailureObserved = $_.Exception.Message.Contains('exited with 1', [StringComparison]::Ordinal) }
    Assert-Contract $canonicalFailureObserved 'The controlled verifier failure fixture must fail before any infrastructure action.'
    Assert-Contract (-not (Test-Path -LiteralPath $canonicalFailureResultPath)) 'A verifier failure must not leave a stale canonical success result.'
}
finally {
    if (Test-Path -LiteralPath $canonicalFailureFixtureRoot) { Remove-Item -LiteralPath $canonicalFailureFixtureRoot -Recurse -Force }
}

Write-Host 'ERP sales-order DemandPlanning cross-process verify script contract tests passed.'
