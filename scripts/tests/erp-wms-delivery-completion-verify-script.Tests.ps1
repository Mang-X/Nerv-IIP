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

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

Assert-Contract ($content.Contains('# Script-Governance:')) 'Verify script must declare script governance metadata.'
Assert-Contract ($content.Contains('scripts/lib/ScriptAutomation.ps1')) 'Verify script must use ScriptAutomation helpers.'
Assert-Contract ($content.Contains('Start-ManagedBackgroundProcess')) 'Verify script must launch managed service processes.'
Assert-Contract ($content.Contains('pg_isready')) 'Verify script must wait for PostgreSQL before creating the disposable database.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Erp.Web.csproj')) 'Verify script must launch ERP in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Wms.Web.csproj')) 'Verify script must launch WMS in its own process.'
Assert-Contract ($content.Contains('Nerv.IIP.Business.Inventory.Web.csproj')) 'Verify script must launch Inventory for the real picking reservation dependency.'
Assert-Contract ($content.Contains("Persistence__Provider = 'PostgreSQL'")) 'Verify script must use PostgreSQL persistence.'
Assert-Contract ($content.Contains("Messaging__Provider = 'Redis'")) 'Verify script must use the real Redis CAP provider.'
Assert-Contract ($content.Contains("Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'")) 'Verify script must create a delivery from the reusable released sales-order seed.'
Assert-Contract ($content.Contains('Wait-ErpSalesOrder')) 'Verify script must wait for the post-start ERP seed before releasing a delivery.'
Assert-Contract ($content.Contains('/api/business/v1/erp/delivery-orders')) 'Verify script must release and query the ERP delivery through public HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders')) 'Verify script must query and complete the WMS outbound order through public HTTP.'
Assert-Contract (
    $content.Contains('actorPrincipalId=$actor&authorizedSiteCodes=$site&scopeKind=site&scopeId=$site&siteCode=$site')) `
    'Verify script must query the WMS outbound order with trusted actor and exact site scope.'
Assert-Contract ($content.Contains('function Wait-WmsOutboundOrderEvent')) 'Verify script must observe the real Redis-created outbound before bootstrapping assignment facts.'
Assert-Contract ($content.Contains('Wait-WmsOutboundOrderEvent -ManagedProcess $wmsProcess -DeliveryOrderNo $deliveryOrderNo')) 'Verify script must wait for this run-scoped delivery event before locating the unassigned outbound.'
Assert-Contract (-not $content.Contains("$wmsProcess.Stop.Invoke('MAN-527 governed work-scope bootstrap')")) 'Verify script must not seed assignment facts after the run-scoped outbound exists.'
Assert-Contract ($content.Contains("LeaderDemo__History__Enabled = 'true'")) 'Verify script must establish the governed WMS work-pool membership fixture.'
Assert-Contract (
    $content.IndexOf("LeaderDemo__History__Enabled = 'true'", [StringComparison]::Ordinal) -lt
        $content.IndexOf('$script:erpProcess = Start-ManagedBackgroundProcess', [StringComparison]::Ordinal)) `
    'Verify script must finish the WMS work-scope fixture before ERP can publish the run-scoped outbound.'
Assert-Contract ($content.Contains('function Get-UnassignedWmsOutboundOrderReadback')) 'Verify script must use a narrow read-only lookup for the hidden unassigned outbound.'
Assert-Contract ($content.Contains('assigned_pool_code IS NULL')) 'Verify script readback must prove the run-scoped outbound has no prior pool assignment.'
Assert-Contract ($content.Contains('assigned_operator_user_id IS NULL')) 'Verify script readback must prove the run-scoped outbound has no prior operator assignment.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/assignment')) 'Verify script must assign the outbound order through public HTTP before completion.'
Assert-Contract ($content.Contains("poolCode = `$wmsShippingPoolCode")) 'Verify script must assign the outbound order to the trusted shipping pool.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$unassignedOutbound.version")) 'Verify script assignment must use the exact unassigned readback version.'
Assert-Contract ($content.Contains("assignedPoolCode -ne `$wmsShippingPoolCode")) 'Verify script must publicly read back and assert the first pool assignment.'
Assert-Contract ($content.Contains("assignedOperatorUserId -ne `$wmsActorPrincipalId")) 'Verify script must publicly read back and assert the first operator assignment.'
Assert-Contract ($content.Contains("Inventory__BaseUrl = `$inventoryUrl")) 'Verify script must wire WMS picking reservations to its managed Inventory process.'
Assert-Contract ($content.Contains('/api/inventory/v1/movements')) 'Verify script must establish real available stock through public Inventory HTTP.'
Assert-Contract ($content.Contains('foreach ($outboundLine in @($outbound.lines))')) 'Verify script must execute the picking lifecycle for every outbound line.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders/$([Uri]::EscapeDataString($outboundOrderId))/picking-tasks')) 'Verify script must create each picking task through public WMS HTTP.'
Assert-Contract ($content.Contains('function Wait-WmsPickingTask')) 'Verify script must read back each created picking task through governed public scope.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/assignment')) 'Verify script must assign each picking task through public WMS HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/start')) 'Verify script must start each picking task through the governed action endpoint.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/progress')) 'Verify script must record each picking task progress through the governed action endpoint.'
Assert-Contract ($content.Contains('/api/business/v1/wms/picking-tasks/$([Uri]::EscapeDataString($warehouseTaskId))/complete')) 'Verify script must complete each picking task through the governed action endpoint.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$pickingTask.version")) 'Verify script task assignment must use the real public picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskAssignment.data.version")) 'Verify script must start from the real post-assignment picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskStart.data.version")) 'Verify script must progress from the real post-start picking-task version.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$taskProgress.data.version")) 'Verify script must complete from the real post-progress picking-task version.'
Assert-Contract ($content.Contains('$outboundAfterPicking = Wait-WmsOutboundOrder')) 'Verify script must refresh the outbound order after task creation advances its version.'
Assert-Contract ($content.Contains("actorPrincipalId = `$wmsActorPrincipalId")) 'Verify script completion must carry the trusted actor principal.'
Assert-Contract ($content.Contains("authorizedSiteCodes = @(`$wmsSiteCode)")) 'Verify script completion must carry the exact authorized site.'
Assert-Contract ($content.Contains("scopeKind = 'site'")) 'Verify script completion must declare the selected site scope kind.'
Assert-Contract ($content.Contains("scopeId = `$wmsSiteCode")) 'Verify script completion must carry the selected site scope id.'
Assert-Contract ($content.Contains("expectedVersion = [long]`$outboundAfterPicking.version")) 'Verify script completion must use the real post-picking outbound version.'
Assert-Contract ($content.Contains('/api/business/v1/erp/finance/receivables/by-source')) 'Verify script must prove the completion-created receivable through public HTTP.'
Assert-Contract ($content.Contains('External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts')) 'Verify script must execute the real Redis repeated-event probe.'
Assert-Contract ($content.Contains('UnitTestResult')) 'Verify script must prove the external replay probe executed exactly once and passed.'
Assert-Contract ($content.Contains('shippedQuantity')) 'Verify script must assert the public line-level shipped quantity.'
Assert-Contract ($content.Contains('shippedAtUtc')) 'Verify script must assert the public first-shipment time.'
Assert-Contract ($content.Contains('completedAtUtc')) 'Verify script must assert the public completion time.'
Assert-Contract ($content.Contains('finally')) 'Verify script must clean up processes and disposable infrastructure in finally.'
Assert-Contract ($content.Contains('erp-wms-delivery-completion-evidence.json')) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('$runningResult.Stdout')) 'Verify script must preserve compose-service cleanup ownership.'
foreach ($cleanupStep in @('stop-erp', 'stop-wms', 'stop-inventory', 'drop-database', 'readback-database', 'stop-postgres', 'stop-redis', 'readback-managed-processes')) {
    Assert-Contract ($content.Contains("# Cleanup-Step: $cleanupStep")) "Verify script must independently execute cleanup step $cleanupStep."
}
Assert-Contract ($content.Contains('managedProcessRemaining')) 'Verify script evidence must record managed-process cleanup readback.'
Assert-Contract ($content.Contains('exactDatabaseRemaining')) 'Verify script evidence must record exact disposable-database cleanup readback.'
Assert-Contract ($content.Contains('pre-existing-running-not-stopped')) 'Verify script evidence must distinguish pre-existing compose services that were not owned.'
Assert-Contract ($content.Contains('cleanupErrors')) 'Verify script must aggregate cleanup errors and fail after all cleanup and evidence steps.'

Write-Host 'ERP and WMS delivery-completion cross-process verify script contract tests passed.'
