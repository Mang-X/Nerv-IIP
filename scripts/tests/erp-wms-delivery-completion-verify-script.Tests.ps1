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
Assert-Contract ($content.Contains("Persistence__Provider = 'PostgreSQL'")) 'Verify script must use PostgreSQL persistence.'
Assert-Contract ($content.Contains("Messaging__Provider = 'Redis'")) 'Verify script must use the real Redis CAP provider.'
Assert-Contract ($content.Contains("Erp__Seed__SalesOrderDemandDemo__Enabled = 'true'")) 'Verify script must create a delivery from the reusable released sales-order seed.'
Assert-Contract ($content.Contains('Wait-ErpSalesOrder')) 'Verify script must wait for the post-start ERP seed before releasing a delivery.'
Assert-Contract ($content.Contains('/api/business/v1/erp/delivery-orders')) 'Verify script must release and query the ERP delivery through public HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/wms/outbound-orders')) 'Verify script must query and complete the WMS outbound order through public HTTP.'
Assert-Contract ($content.Contains('/api/business/v1/erp/finance/receivables/by-source')) 'Verify script must prove the completion-created receivable through public HTTP.'
Assert-Contract ($content.Contains('External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts')) 'Verify script must execute the real Redis repeated-event probe.'
Assert-Contract ($content.Contains('UnitTestResult')) 'Verify script must prove the external replay probe executed exactly once and passed.'
Assert-Contract ($content.Contains('shippedQuantity')) 'Verify script must assert the public line-level shipped quantity.'
Assert-Contract ($content.Contains('shippedAtUtc')) 'Verify script must assert the public first-shipment time.'
Assert-Contract ($content.Contains('completedAtUtc')) 'Verify script must assert the public completion time.'
Assert-Contract ($content.Contains('finally')) 'Verify script must clean up processes and disposable infrastructure in finally.'
Assert-Contract ($content.Contains('erp-wms-delivery-completion-evidence.json')) 'Verify script must write reusable acceptance evidence.'
Assert-Contract ($content.Contains('$runningResult.Stdout')) 'Verify script must preserve compose-service cleanup ownership.'

Write-Host 'ERP and WMS delivery-completion cross-process verify script contract tests passed.'
