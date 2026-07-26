# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the ERP sales-order to DemandPlanning cross-process verification script
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/verify-erp-sales-order-demand-planning.ps1'
$ciWorkflow = Join-Path $repoRoot '.github/workflows/ci.yml'
if (-not (Test-Path -LiteralPath $verifyScript)) {
    throw 'ERP sales-order DemandPlanning cross-process verify script is missing.'
}

$content = Get-Content -LiteralPath $verifyScript -Raw
$workflowContent = Get-Content -LiteralPath $ciWorkflow -Raw

function Assert-Contract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

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
Assert-Contract (-not $content.Contains('NERV_IIP_TEST_SALES_ORDER_ID')) 'Fault injection must resolve the seeded order identity from DemandPlanning persistence instead of fragile shell output.'
Assert-Contract ($content.Contains('out-of-order')) 'Verify script must assert stale/out-of-order convergence.'
Assert-Contract ($content.Contains('$runningResult.Stdout')) 'Verify script must parse the compose service list from Invoke-NativeCommandOutput.Stdout before cleanup ownership is decided.'
Assert-Contract ($content.Contains('UnitTestResult')) 'Verify script must prove the external fault-injection test actually executed and passed.'
Assert-Contract ($content.Contains('Assert-DemandStable')) 'Verify script must hold the final cancellation state stable after stale-message injection.'
Assert-Contract ($content.Contains('Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres')) 'Verify script must execute the real Redis identical-idempotency-key duplicate test.'
Assert-Contract ($content.Contains('Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail')) 'Verify script must execute the real Redis fallback-scan retry test.'
Assert-Contract ($content.Contains('changed during the stability window')) 'Verify script must fail immediately when the final demand changes during the stability window.'
Assert-Contract ($content.Contains("Wait-Demand -DemandPlanningUrl `$demandPlanningUrl -Headers `$headers -Version 4 -Quantity 0 -Status 'cancelled'")) 'Verify script must wait for cancellation convergence before entering the strict stability window.'
Assert-Contract ($content.Contains('sourceVersion')) 'Verify script must assert business-version convergence.'
Assert-Contract ($content.Contains('sourceStatus')) 'Verify script must assert lifecycle-status convergence.'
Assert-Contract ($content.Contains('function Wait-ErpSalesOrderSource')) 'Acceptance must poll the committed ERP source row.'
Assert-Contract ($content.Contains('erp.sales_orders')) 'Source readiness must inspect the ERP-owned source table.'
Assert-Contract ($content.Contains('erp.sales_order_lines')) 'Source readiness must verify line 10.'
Assert-Contract ($content.Contains('sourceStage')) 'Failure diagnostics must identify the ERP source stage.'
$sourceReadyCall = $content.IndexOf('Wait-ErpSalesOrderSource -ComposeFile $composeFile -DatabaseName $databaseName', [StringComparison]::Ordinal)
$firstChangePost = $content.IndexOf('Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10"', [StringComparison]::Ordinal)
Assert-Contract ($sourceReadyCall -ge 0 -and $firstChangePost -gt $sourceReadyCall) 'Committed ERP source readiness must complete before the first change POST.'
Assert-Contract ($content.Contains('[string]$ExpectedData')) 'State-changing POST validation must require expected business data.'
Assert-Contract ($content.Contains('SkipHttpErrorCheck')) 'POST validation must inspect bounded HTTP responses itself.'
Assert-Contract ($content.Contains('$responseEnvelope.success')) 'POST validation must reject a business-error envelope.'
Assert-Contract ($content.Contains('postResponse')) 'Failure diagnostics must identify the bounded POST response.'
Assert-Contract (-not $content.Contains('Wait-ErpSalesOrderSource -ComposeFile $composeFile -DatabaseName $databaseName -RetryPost')) 'Readiness must never introduce POST retry.'
Assert-Contract (($content.Split('-ExpectedData ''changed''').Count - 1) -eq 2) 'Both change POSTs must require data=changed.'
Assert-Contract (($content.Split('-ExpectedData ''cancelled''').Count - 1) -eq 1) 'The cancellation POST must require data=cancelled.'
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
$unsafeDiagnostic = 'pwd=pwd-value token=token-value secret=secret-value client_secret=client-value Authorization: Bearer bearer-value Password=password-value'
$safeDiagnostic = Protect-ScriptAutomationText $unsafeDiagnostic
foreach ($sensitiveValue in @('pwd-value', 'token-value', 'secret-value', 'client-value', 'bearer-value', 'password-value')) {
    Assert-Contract (-not $safeDiagnostic.Contains($sensitiveValue)) "Shared diagnostic redaction leaked $sensitiveValue."
}

$tokens = $null
$parseErrors = $null
$verifyAst = [System.Management.Automation.Language.Parser]::ParseFile($verifyScript, [ref]$tokens, [ref]$parseErrors)
Assert-Contract ($parseErrors.Count -eq 0) 'Verify script must remain parseable for behavioral contract tests.'
$functionAsts = @($verifyAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))
$sourceReadinessAst = @($functionAsts | Where-Object Name -eq 'Wait-ErpSalesOrderSource')
$invokeJsonPostAst = @($functionAsts | Where-Object Name -eq 'Invoke-JsonPost')
$diagnosticRedactorAst = @($functionAsts | Where-Object Name -eq 'Protect-Man517DiagnosticText')
Assert-Contract ($sourceReadinessAst.Count -eq 1) 'Source readiness helper must have one inspectable function definition.'
Assert-Contract ($invokeJsonPostAst.Count -eq 1) 'POST validator must have one inspectable function definition.'
Assert-Contract ($diagnosticRedactorAst.Count -eq 1) 'POST validator must use the production MAN-517 diagnostic redactor.'

$commandAsts = @($verifyAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))
$releasedV1Wait = @($commandAsts | Where-Object {
    $_.CommandElements.Count -gt 0 -and
    $_.CommandElements[0].Extent.Text -eq 'Wait-Demand' -and
    $_.Extent.Text.Contains("-Version 1 -Quantity 2 -Status 'active'")
})
$sourceReadinessCall = @($commandAsts | Where-Object {
    $_.CommandElements.Count -gt 0 -and $_.CommandElements[0].Extent.Text -eq 'Wait-ErpSalesOrderSource'
})
$firstMutationPost = @($commandAsts | Where-Object {
    $_.CommandElements.Count -gt 0 -and
    $_.CommandElements[0].Extent.Text -eq 'Invoke-JsonPost' -and
    $_.Extent.Text.Contains('/sales-orders/SO-DEMO-001/lines/10')
} | Sort-Object { $_.Extent.StartOffset } | Select-Object -First 1)
Assert-Contract ($releasedV1Wait.Count -eq 1 -and $sourceReadinessCall.Count -eq 1 -and $firstMutationPost.Count -eq 1) 'Acceptance ordering must have one released-v1 wait, one source probe, and one first mutation POST.'
Assert-Contract (
    $releasedV1Wait[0].Extent.StartOffset -lt $sourceReadinessCall[0].Extent.StartOffset -and
    $sourceReadinessCall[0].Extent.StartOffset -lt $firstMutationPost[0].Extent.StartOffset
) 'Released-v1 convergence must precede source readiness, which must precede the first mutation POST.'

$postLoopNodes = @($invokeJsonPostAst[0].Body.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.ForStatementAst] -or
    $node -is [System.Management.Automation.Language.ForEachStatementAst] -or
    $node -is [System.Management.Automation.Language.WhileStatementAst] -or
    $node -is [System.Management.Automation.Language.DoWhileStatementAst] -or
    $node -is [System.Management.Automation.Language.DoUntilStatementAst]
}, $true))
$postRequestAsts = @($invokeJsonPostAst[0].Body.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
    $node.CommandElements.Count -gt 0 -and
    $node.CommandElements[0].Extent.Text -eq 'Invoke-WebRequest'
}, $true))
Assert-Contract ($postLoopNodes.Count -eq 0) 'State-changing POST validation must not contain a retry loop.'
Assert-Contract ($postRequestAsts.Count -eq 1) 'State-changing POST validation must issue exactly one HTTP request.'

$sourceReadinessText = $sourceReadinessAst[0].Extent.Text
Assert-Contract ($sourceReadinessText.Contains('-TimeoutSeconds $remainingTimeoutSeconds')) 'Source readiness must bound every PostgreSQL probe by its remaining deadline.'
Assert-Contract ($sourceReadinessText.Contains('if ($remainingMilliseconds -le 0) { break }')) 'Source readiness must not sleep after its deadline expires.'

$internalToken = 'test-internal-token'
. ([scriptblock]::Create($diagnosticRedactorAst[0].Extent.Text))
. ([scriptblock]::Create($invokeJsonPostAst[0].Extent.Text))
$script:postRequestCount = 0
$script:postMaximumRedirection = $null
$script:postHttpStatus = 200
$script:postResponseContent = '{"success":true,"data":"changed"}'

function Invoke-WebRequest {
    param(
        [string]$Method,
        [string]$Uri,
        [hashtable]$Headers,
        [string]$ContentType,
        [string]$Body,
        [Nullable[int]]$MaximumRedirection,
        [switch]$SkipHttpErrorCheck
    )

    $script:postRequestCount++
    $script:postMaximumRedirection = $MaximumRedirection
    return [pscustomobject]@{ StatusCode = $script:postHttpStatus; Content = $script:postResponseContent }
}

function Assert-PostRejected {
    param(
        [string]$Payload,
        [string]$CaseName,
        [int]$HttpStatus = 200,
        [string[]]$SensitiveValues = @()
    )

    $script:postRequestCount = 0
    $script:postMaximumRedirection = $null
    $script:postHttpStatus = $HttpStatus
    $script:postResponseContent = $Payload
    $threw = $false
    $failureMessage = $null
    try {
        Invoke-JsonPost -Uri 'http://127.0.0.1/post' -Headers @{} -ExpectedData 'changed' -Body @{ value = 'test' } | Out-Null
    }
    catch {
        $threw = $true
        $failureMessage = $_.Exception.Message
    }

    Assert-Contract $threw "POST validator must reject $CaseName immediately."
    Assert-Contract ($script:postRequestCount -eq 1) "POST validator must issue exactly one request for $CaseName."
    Assert-Contract ($script:postMaximumRedirection -eq 0) "POST validator must disable redirects for $CaseName."
    foreach ($sensitiveValue in $SensitiveValues) {
        Assert-Contract (-not $failureMessage.Contains($sensitiveValue)) "POST validator leaked JSON credential value for $CaseName."
    }
}

$script:postRequestCount = 0
$script:postMaximumRedirection = $null
$response = Invoke-JsonPost -Uri 'http://127.0.0.1/post' -Headers @{} -ExpectedData 'changed' -Body @{ value = 'test' }
Assert-Contract ($response.success -is [bool] -and $response.success -eq $true -and $response.data -is [string] -and $response.data -ceq 'changed') 'POST validator must return the valid boolean/string envelope.'
Assert-Contract ($script:postRequestCount -eq 1) 'POST validator must issue exactly one request for a valid envelope.'
Assert-Contract ($script:postMaximumRedirection -eq 0) 'POST validator must disable redirects for a valid envelope.'

Assert-PostRejected -CaseName 'numeric success' -Payload '{"success":1,"data":"changed"}'
Assert-PostRejected -CaseName 'string success' -Payload '{"success":"true","data":"changed"}'
Assert-PostRejected -CaseName 'array success' -Payload '{"success":[true],"data":"changed"}'
Assert-PostRejected -CaseName 'array data' -Payload '{"success":true,"data":["changed"]}'
Assert-PostRejected -CaseName 'null success' -Payload '{"success":null,"data":"changed"}'
Assert-PostRejected -CaseName 'missing success' -Payload '{"data":"changed"}'
Assert-PostRejected -CaseName 'false success' -Payload '{"success":false,"data":"changed"}'
Assert-PostRejected -CaseName 'redirect response' -HttpStatus 307 -Payload '{"success":true,"data":"changed"}'
Assert-PostRejected -CaseName 'business-error JSON credentials' -Payload '{"success":false,"data":"changed","password":"quoted-password","token":"quoted-token","authorization":"Bearer quoted-bearer"}' -SensitiveValues @('quoted-password', 'quoted-token', 'quoted-bearer')
Assert-PostRejected -CaseName 'malformed JSON credentials' -Payload '{"success":false,"password":"quoted-password","token":"quoted-token","authorization":"Bearer quoted-bearer"' -SensitiveValues @('quoted-password', 'quoted-token', 'quoted-bearer')

Write-Host 'ERP sales-order DemandPlanning cross-process verify script contract tests passed.'
