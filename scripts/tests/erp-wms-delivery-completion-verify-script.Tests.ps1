# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the ERP and WMS delivery-completion cross-process verification script
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-erp-wms-delivery-completion.ps1'
if (-not (Test-Path -LiteralPath $verifyScript)) {
    throw 'ERP and WMS delivery-completion cross-process verify script is missing.'
}

$content = Get-Content -LiteralPath $verifyScript -Raw
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

function Import-VerifyFunction {
    param([string]$Name)

    $definition = $scriptAst.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        [string]::Equals([string]$node.Name, $Name, [StringComparison]::OrdinalIgnoreCase)
    }, $true)
    if ($null -eq $definition) {
        throw "Verify script function '$Name' is missing."
    }
    Set-Item -Path "Function:\script:$Name" -Value $definition.Body.GetScriptBlock()
}

Assert-Contract ($parseErrors.Count -eq 0) 'Verify script must parse before executable contracts are evaluated.'
Import-VerifyFunction -Name 'ConvertFrom-Man527RedisStreamGroupOutput'
Import-VerifyFunction -Name 'Wait-Man527ErpCapConsumerReady'
Import-VerifyFunction -Name 'Invoke-Man527FirstBusinessActionAfterConsumerReady'

$rawGroupOutput = @'
name
business-erp.wms-outbound-cancelled-delivery-projection.man527-current-run
consumers
1
pending
0
name
business-erp.wms-outbound-completed-ar-accrual.man527-current-run
consumers
1
pending
0
'@
$parsedGroups = @(ConvertFrom-Man527RedisStreamGroupOutput -Output $rawGroupOutput)
Assert-Contract (
    [string]::Equals(
        ($parsedGroups -join '|'),
        'business-erp.wms-outbound-cancelled-delivery-projection.man527-current-run|business-erp.wms-outbound-completed-ar-accrual.man527-current-run',
        [StringComparison]::Ordinal)
) 'The real Redis raw parser must return only values of XINFO GROUPS name fields in ordinal order.'

$script:observedRedisGroups = @()
$script:redisGroupObservationCount = 0
function Get-Man527RedisStreamGroupNames {
    param([string]$ComposeFile, [string]$StreamName)
    $script:redisGroupObservationCount++
    return @($script:observedRedisGroups)
}
function New-WmsWorkPoolFixture {
    param(
        [string]$WmsUrl,
        [hashtable]$Headers,
        [string]$SiteCode,
        [string]$PoolCode,
        [string]$DisplayName,
        [string]$AssignerPrincipalId,
        [string]$OperatorPrincipalId
    )
    $script:businessActionCount++
    return 'business-started'
}

$readinessProcess = [pscustomobject]@{
    Process = [pscustomobject]@{ HasExited = $false }
    LogDirectory = '/tmp/man527-readiness-test'
}
$readinessTopic = 'WmsIntegrationEvent'
$readinessConsumer = 'WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable'
$readinessGroupBase = 'business-erp.wms-outbound-completed-ar-accrual'
$readinessCapVersion = 'man527-current-run'
$readinessGroup = "$readinessGroupBase.$readinessCapVersion"

$script:observedRedisGroups = @(
    'business-erp.wms-outbound-cancelled-delivery-projection.man527-current-run',
    'business-erp.wms-outbound-completed-ar-accrual.man527-old-run'
)
$script:redisGroupObservationCount = 0
$script:businessActionCount = 0
$wrongGroupFailure = $null
try {
    Invoke-Man527FirstBusinessActionAfterConsumerReady `
        -ComposeFile 'unused-compose.yml' `
        -ManagedProcess $readinessProcess `
        -Topic $readinessTopic `
        -Consumer $readinessConsumer `
        -GroupBase $readinessGroupBase `
        -CapVersion $readinessCapVersion `
        -TimeoutSeconds 0 `
        -WmsUrl 'http://unused-wms' `
        -Headers @{} `
        -SiteCode 'SITE-001' `
        -PoolCode 'POOL-001' `
        -DisplayName '测试作业池' `
        -AssignerPrincipalId 'assigner' `
        -OperatorPrincipalId 'operator' | Out-Null
}
catch {
    $wrongGroupFailure = $_.Exception.Message
}
Assert-Contract (-not [string]::IsNullOrWhiteSpace($wrongGroupFailure)) 'A neighboring consumer and an old-run target group must fail readiness.'
foreach ($identity in @($readinessTopic, $readinessConsumer, $readinessGroup, $readinessCapVersion, 'first registration boundary')) {
    Assert-Contract ($wrongGroupFailure.Contains($identity, [StringComparison]::Ordinal)) "Wrong-group readiness diagnostics must name '$identity'. Actual: $wrongGroupFailure"
}
Assert-Contract ($script:redisGroupObservationCount -eq 1) 'A zero-budget wrong-group readiness probe must fail after its first real observation.'
Assert-Contract ($script:businessActionCount -eq 0) 'Wrong-group readiness must fail before the first MAN-527 business action executes.'

$script:redisGroupObservations = @(
    @('business-erp.wms-outbound-completed-ar-accrual.man527-old-run'),
    [InvalidOperationException]::new('later registration failure must not replace the first boundary')
)
$script:redisGroupObservationCount = 0
function Get-Man527RedisStreamGroupNames {
    param([string]$ComposeFile, [string]$StreamName)
    $observationIndex = $script:redisGroupObservationCount
    $script:redisGroupObservationCount++
    $observation = $script:redisGroupObservations[[Math]::Min($observationIndex, $script:redisGroupObservations.Count - 1)]
    if ($observation -is [Exception]) { throw $observation }
    return @($observation)
}
$script:businessActionCount = 0
$firstBoundaryFailure = $null
try {
    Invoke-Man527FirstBusinessActionAfterConsumerReady `
        -ComposeFile 'unused-compose.yml' `
        -ManagedProcess $readinessProcess `
        -Topic $readinessTopic `
        -Consumer $readinessConsumer `
        -GroupBase $readinessGroupBase `
        -CapVersion $readinessCapVersion `
        -TimeoutSeconds 1 `
        -WmsUrl 'http://unused-wms' `
        -Headers @{} `
        -SiteCode 'SITE-001' `
        -PoolCode 'POOL-001' `
        -DisplayName '测试作业池' `
        -AssignerPrincipalId 'assigner' `
        -OperatorPrincipalId 'operator' | Out-Null
}
catch {
    $firstBoundaryFailure = $_.Exception.Message
}
Assert-Contract ($script:redisGroupObservationCount -ge 2) 'The first-boundary contract must observe a later registration failure before timing out.'
Assert-Contract ($firstBoundaryFailure.Contains('target group missing; observed groups=business-erp.wms-outbound-completed-ar-accrual.man527-old-run', [StringComparison]::Ordinal)) 'Readiness diagnostics must retain the first missing-group observation.'
Assert-Contract (-not $firstBoundaryFailure.Contains('later registration failure must not replace the first boundary', [StringComparison]::Ordinal)) 'A later registration failure must not overwrite the first registration boundary.'
Assert-Contract ($script:businessActionCount -eq 0) 'A later registration failure must still fail before the first MAN-527 business action.'

$script:observedRedisGroups = @(
    'business-erp.wms-outbound-cancelled-delivery-projection.man527-current-run',
    $readinessGroup
)
$script:redisGroupObservationCount = 0
function Get-Man527RedisStreamGroupNames {
    param([string]$ComposeFile, [string]$StreamName)
    $script:redisGroupObservationCount++
    return @($script:observedRedisGroups)
}
$script:businessActionCount = 0
$admission = Invoke-Man527FirstBusinessActionAfterConsumerReady `
    -ComposeFile 'unused-compose.yml' `
    -ManagedProcess $readinessProcess `
    -Topic $readinessTopic `
    -Consumer $readinessConsumer `
    -GroupBase $readinessGroupBase `
    -CapVersion $readinessCapVersion `
    -TimeoutSeconds 0 `
    -WmsUrl 'http://unused-wms' `
    -Headers @{} `
    -SiteCode 'SITE-001' `
    -PoolCode 'POOL-001' `
    -DisplayName '测试作业池' `
    -AssignerPrincipalId 'assigner' `
    -OperatorPrincipalId 'operator'
Assert-Contract (
    [string]::Equals([string]$admission.readiness.topic, $readinessTopic, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$admission.readiness.consumer, $readinessConsumer, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$admission.readiness.groupBase, $readinessGroupBase, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$admission.readiness.group, $readinessGroup, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$admission.readiness.capVersion, $readinessCapVersion, [StringComparison]::Ordinal) -and
    [string]::Equals([string]$admission.businessResult, 'business-started', [StringComparison]::Ordinal)
) 'Only the exact topic/group/consumer/run identity may satisfy readiness.'
Assert-Contract ($script:redisGroupObservationCount -eq 1) 'Exact target readiness must converge on the first matching observation.'
Assert-Contract ($script:businessActionCount -eq 1) 'Exact target readiness must execute the first MAN-527 business action exactly once.'

Assert-Contract ($content.Contains('# Script-Governance:', [StringComparison]::Ordinal)) 'Verify script must declare script governance metadata.'
Assert-Contract ($content.Contains('scripts/lib/ScriptAutomation.ps1', [StringComparison]::Ordinal)) 'Verify script must use ScriptAutomation helpers.'
Assert-Contract ($content.Contains('Start-ManagedBackgroundProcess', [StringComparison]::Ordinal)) 'Verify script must launch managed service processes.'
Assert-Contract ($content.Contains('pg_isready', [StringComparison]::Ordinal)) 'Verify script must wait for PostgreSQL before creating the disposable database.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Erp.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch ERP in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Wms.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch WMS in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Inventory.Web.csproj', [StringComparison]::Ordinal)) 'Verify script must launch Inventory for the real picking reservation dependency.'
Assert-Contract ($content.Contains("Persistence__Provider = 'PostgreSQL'", [StringComparison]::Ordinal)) 'Verify script must use PostgreSQL persistence.'
Assert-Contract ($content.Contains("Messaging__Provider = 'Redis'", [StringComparison]::Ordinal)) 'Verify script must use the real Redis CAP provider.'
Assert-Contract ($content.Contains("Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'", [StringComparison]::Ordinal)) 'Verify script must create a delivery from the reusable released sales-order seed.'
Assert-Contract ($content.Contains('Wait-ErpSalesOrder', [StringComparison]::Ordinal)) 'Verify script must wait for the post-start ERP seed before releasing a delivery.'
Assert-Contract ($content.Contains('/api/business/v1/erp/delivery-orders', [StringComparison]::Ordinal)) 'Verify script must release and query the ERP delivery through public HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders', [StringComparison]::Ordinal)) 'Verify script must query and complete the WMS outbound order through public HTTP.'
Assert-Contract (
    $content.Contains('actorPrincipalId=$actor&authorizedSiteCodes=$site&scopeKind=site&scopeId=$site&siteCode=$site', [StringComparison]::Ordinal)) `
    'Verify script must query the WMS outbound order with trusted actor and exact site scope.'
Assert-Contract ($content.Contains('function Wait-WmsOutboundOrderEvent', [StringComparison]::Ordinal)) 'Verify script must observe the real Redis-created outbound before bootstrapping assignment facts.'
Assert-Contract ($content.Contains('Wait-WmsOutboundOrderEvent -ManagedProcess $wmsProcess -DeliveryOrderNo $deliveryOrderNo', [StringComparison]::Ordinal)) 'Verify script must wait for this run-scoped delivery event before locating the unassigned outbound.'
Assert-Contract (-not $content.Contains("$wmsProcess.Stop.Invoke('MAN-527 governed work-scope bootstrap')", [StringComparison]::Ordinal)) 'Verify script must not seed assignment facts after the run-scoped outbound exists.'
Assert-Contract ($content.Contains('function Get-UnassignedWmsOutboundOrderReadback', [StringComparison]::Ordinal)) 'Verify script must use a narrow read-only lookup for the hidden unassigned outbound.'
Assert-Contract ($content.Contains('assigned_pool_code IS NULL', [StringComparison]::Ordinal)) 'Verify script readback must prove the run-scoped outbound has no prior pool assignment.'
Assert-Contract ($content.Contains('assigned_operator_user_id IS NULL', [StringComparison]::Ordinal)) 'Verify script readback must prove the run-scoped outbound has no prior operator assignment.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/assignment', [StringComparison]::Ordinal)) 'Verify script must assign the outbound order through public HTTP before completion.'
Assert-Contract ($content.Contains("poolCode = `$wmsShippingPoolCode", [StringComparison]::Ordinal)) 'Verify script must assign the outbound order to the trusted shipping pool.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$unassignedOutbound.version", [StringComparison]::Ordinal)) 'Verify script assignment must use the exact unassigned readback version.'
Assert-Contract ((-not $content.Contains("assignedPoolCode -ne `$wmsShippingPoolCode", [StringComparison]::Ordinal)) -and $content.Contains('[string]::Equals([string]$outbound.assignedPoolCode, $wmsShippingPoolCode, [StringComparison]::Ordinal)', [StringComparison]::Ordinal)) 'Verify script must publicly read back and assert the first pool assignment with ordinal identity.'
Assert-Contract ((-not $content.Contains("assignedOperatorUserId -ne `$wmsActorPrincipalId", [StringComparison]::Ordinal)) -and $content.Contains('[string]::Equals([string]$outbound.assignedOperatorUserId, $wmsActorPrincipalId, [StringComparison]::Ordinal)', [StringComparison]::Ordinal)) 'Verify script must publicly read back and assert the first operator assignment with ordinal identity.'
Assert-Contract ($content.Contains("Inventory__BaseUrl = `$inventoryUrl", [StringComparison]::Ordinal)) 'Verify script must wire WMS picking reservations to its managed Inventory process.'
Assert-Contract ($content.Contains('/api/inventory/v1/movements', [StringComparison]::Ordinal)) 'Verify script must establish real available stock through public Inventory HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/picking-tasks', [StringComparison]::Ordinal)) 'Verify script must create each picking task through public WMS HTTP.'
Assert-Contract ($content.Contains('function Wait-WmsPickingTask', [StringComparison]::Ordinal)) 'Verify script must read back each created picking task through governed public scope.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/assignment', [StringComparison]::Ordinal)) 'Verify script must assign each picking task through public WMS HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/start', [StringComparison]::Ordinal)) 'Verify script must start each picking task through the governed action endpoint.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/progress', [StringComparison]::Ordinal)) 'Verify script must record each picking task progress through the governed action endpoint.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/complete', [StringComparison]::Ordinal)) 'Verify script must complete each picking task through the governed action endpoint.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$pickingTask.version", [StringComparison]::Ordinal)) 'Verify script task assignment must use the real public picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskAssignment.data.version", [StringComparison]::Ordinal)) 'Verify script must start from the real post-assignment picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskStart.data.version", [StringComparison]::Ordinal)) 'Verify script must progress from the real post-start picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskProgress.data.version", [StringComparison]::Ordinal)) 'Verify script must complete from the real post-progress picking-task version.'
Assert-Contract ($content.Contains('$outboundAfterPicking = Wait-WmsOutboundOrder', [StringComparison]::Ordinal)) 'Verify script must refresh the outbound order after task creation advances its version.'
Assert-Contract ($content.Contains("actorPrincipalId = `$wmsActorPrincipalId", [StringComparison]::Ordinal)) 'Verify script completion must carry the trusted actor principal.'
Assert-Contract ($content.Contains("authorizedSiteCodes = @(`$wmsSiteCode)", [StringComparison]::Ordinal)) 'Verify script completion must carry the exact authorized site.'
Assert-Contract ($content.Contains("scopeKind = 'site'", [StringComparison]::Ordinal)) 'Verify script completion must declare the selected site scope kind.'
Assert-Contract ($content.Contains("scopeId = `$wmsSiteCode", [StringComparison]::Ordinal)) 'Verify script completion must carry the selected site scope id.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$outboundAfterPicking.version", [StringComparison]::Ordinal)) 'Verify script completion must use the real post-picking outbound version.'
Assert-Contract ($content.Contains('/api/business/v1/erp/finance/receivables/by-source', [StringComparison]::Ordinal)) 'Verify script must prove the completion-created receivable through public HTTP.'
Assert-Contract ($content.Contains('External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts', [StringComparison]::Ordinal)) 'Verify script must execute the real Redis repeated-event probe.'
Assert-Contract ($content.Contains('UnitTestResult', [StringComparison]::Ordinal)) 'Verify script must prove the external replay probe executed exactly once and passed.'
Assert-Contract ($content.Contains('shippedQuantity', [StringComparison]::Ordinal)) 'Verify script must assert the public line-level shipped quantity.'
Assert-Contract ($content.Contains('shippedAtUtc', [StringComparison]::Ordinal)) 'Verify script must assert the public first-shipment time.'
Assert-Contract ($content.Contains('completedAtUtc', [StringComparison]::Ordinal)) 'Verify script must assert the public completion time.'
Assert-Contract ($content.Contains('finally', [StringComparison]::Ordinal)) 'Verify script must clean up processes and disposable infrastructure in finally.'
Assert-Contract ($content.Contains('erp-wms-delivery-completion-evidence.json', [StringComparison]::Ordinal)) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('$runningResult.Stdout', [StringComparison]::Ordinal)) 'Verify script must preserve compose-service cleanup ownership.'
foreach ($cleanupStep in @('stop-erp', 'stop-wms', 'stop-inventory', 'drop-database', 'readback-database', 'stop-postgres', 'stop-redis', 'readback-managed-processes')) {
    Assert-Contract ($content.Contains("# Cleanup-Step: $cleanupStep", [StringComparison]::Ordinal)) "Verify script must independently execute cleanup step $cleanupStep."
}
Assert-Contract ($content.Contains('managedProcessRemaining', [StringComparison]::Ordinal)) 'Verify script evidence must record managed-process cleanup readback.'
Assert-Contract ($content.Contains('exactDatabaseRemaining', [StringComparison]::Ordinal)) 'Verify script evidence must record exact disposable-database cleanup readback.'
Assert-Contract ($content.Contains('pre-existing-running-not-stopped', [StringComparison]::Ordinal)) 'Verify script evidence must distinguish pre-existing compose services that were not owned.'
Assert-Contract ($content.Contains('cleanupErrors', [StringComparison]::Ordinal)) 'Verify script must aggregate cleanup errors and fail after all cleanup and evidence steps.'

Write-Host 'ERP and WMS delivery-completion cross-process verify script contract tests passed.'
