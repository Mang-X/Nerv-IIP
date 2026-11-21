# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses the ERP and WMS delivery-completion cross-process verification script
#   Writes:
#     - Temporary diagnostic artifacts for the executable privacy regression
#   Cleanup:
#     - Removes the owned temporary diagnostic directory in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verifyScript = Join-Path $repoRoot 'scripts/integration/verify-erp-wms-delivery-completion.ps1'
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
& {
    # Regression: #3203 requires retained diagnostics to exclude Redis message bodies.
    . (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
    . (Join-Path $repoRoot 'scripts/lib/AcceptanceCanonicalResult.ps1')
    foreach ($name in @('Write-Man527DiagnosticFile', 'Invoke-Man527DiagnosticCommand', 'Export-Man527FailureDiagnostics')) {
        $definition = $scriptAst.Find({ param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            [string]::Equals($node.Name, $name, [StringComparison]::Ordinal)
        }, $true)
        Set-Item -Path "Function:$name" -Value $definition.Body.GetScriptBlock()
    }
    $root = $repoRoot
    $evidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) "man527-safe-$([Guid]::NewGuid().ToString('N'))"
    $internalToken = 'man527-declared-secret'
    $PostgresAdminConnectionString = 'Host=example;Password=fixture-secret'
    $databaseConnectionString = $PostgresAdminConnectionString
    $RedisConnectionString = 'localhost,password=fixture-secret'
    $databaseName = 'man527_fixture'
    $capVersion = 'man527-fixture'
    $deliveryOrderNo = 'DO-MAN527-FIXTURE'
    $erpProcess = $wmsProcess = $inventoryProcess = $null
    $databaseCreated = $true
    $composeFile = 'fixture-compose.yml'
    function Invoke-NativeCommandOutput {
        param($Command, $Arguments, $WorkingDirectory, $Name)
        $argumentSet = [Collections.Generic.HashSet[string]]::new([string[]]$Arguments, [StringComparer]::Ordinal)
        $output = if ($argumentSet.Contains('psql')) { 'erp | delivery_orders | 1' }
        elseif ($argumentSet.Contains('XLEN')) { '1' }
        elseif ($argumentSet.Contains('GROUPS')) {
            '[{"name":"business-erp.man527-fixture","consumers":1,"pending":1,"last-delivered-id":"123-0","entries-read":1,"lag":0}]'
        }
        else { 'first-entry EventJson MAN527-BODY-SENTINEL last-entry' }
        [pscustomobject]@{ Stdout = $output }
    }
    try {
        try { throw 'EventJson MAN527-BODY-SENTINEL Authorization: Bearer man527-declared-secret' }
        catch { $failureRecord = $_ }
        $proof = Export-Man527FailureDiagnostics -FailureRecord $failureRecord
        Assert-Contract $proof.failureDiagnosticsCaptured 'Failure diagnostics must actually be persisted.'
        foreach ($path in $proof.artifactPaths) {
            $retained = [IO.File]::ReadAllText($path)
            foreach ($forbidden in @('MAN527-BODY-SENTINEL', 'EventJson', 'first-entry', 'last-entry', 'man527-declared-secret')) {
                Assert-Contract (-not $retained.Contains($forbidden, [StringComparison]::Ordinal)) "Retained diagnostic must exclude $forbidden."
            }
        }
        $redis = [IO.File]::ReadAllText((Join-Path $evidenceDirectory 'diagnostics/redis-stream-state.txt'))
        Assert-Contract ($redis.Contains('length=1', [StringComparison]::Ordinal) -and $redis.Contains('"pending":1', [StringComparison]::Ordinal)) 'Safe Redis diagnostics must preserve stream length and pending count.'
    }
    finally {
        if (Test-Path -LiteralPath $evidenceDirectory) { Remove-Item -LiteralPath $evidenceDirectory -Recurse -Force }
    }
}
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

# Capture the scoped URI at the HTTP boundary of the production readback.
& {
    Import-VerifyFunction -Name 'Wait-WmsOutboundOrder'
    $observed = [Collections.Generic.List[uri]]::new()
    function Invoke-RestMethod {
        param($Method, [uri]$Uri, $Headers)
        $observed.Add($Uri)
        return @{ data = @{ items = @(@{ outboundOrderNo = 'DO-MAN527-0123ABCD' }) } }
    }
    Wait-WmsOutboundOrder -WmsUrl 'http://fixture.invalid' -Headers @{} -DeliveryOrderNo 'DO-MAN527-0123ABCD' -ActorPrincipalId 'operator&1' -SiteCode 'site+1' | Out-Null
    Assert-Contract ($observed.Count -eq 1) 'The run-scoped outbound readback must query once for a matching response.'
    $query = [System.Web.HttpUtility]::ParseQueryString($observed[0].Query)
    foreach ($expected in @{
        keyword = 'DO-MAN527-0123ABCD'; actorPrincipalId = 'operator&1'; authorizedSiteCodes = 'site+1'
        scopeKind = 'site'; scopeId = 'site+1'; siteCode = 'site+1'
        organizationId = 'org-001'; environmentId = 'env-dev'
    }.GetEnumerator()) {
        Assert-Contract ([string]::Equals($query.Get($expected.Key), $expected.Value, [StringComparison]::Ordinal)) "The outbound readback sent an incorrect $($expected.Key)."
    }
}

# The positive FullChain fixture starts unassigned. It cannot distinguish a lookup
# that would also select an already-assigned outbound.
Assert-Contract ($content.Contains('assigned_pool_code IS NULL', [StringComparison]::Ordinal)) 'The unassigned readback must exclude an outbound with an existing pool.'
Assert-Contract ($content.Contains('assigned_operator_user_id IS NULL', [StringComparison]::Ordinal)) 'The unassigned readback must exclude an outbound with an existing operator.'
Assert-Contract ($content.Contains('$runningResult.Stdout', [StringComparison]::Ordinal)) 'Verify script must preserve compose-service cleanup ownership.'
foreach ($cleanupStep in @('stop-erp', 'stop-wms', 'stop-inventory', 'drop-database', 'readback-database', 'stop-postgres', 'stop-redis', 'readback-managed-processes')) {
    Assert-Contract ($content.Contains("# Cleanup-Step: $cleanupStep", [StringComparison]::Ordinal)) "Verify script must independently execute cleanup step $cleanupStep."
}
Assert-Contract ($content.Contains('managedProcessRemaining', [StringComparison]::Ordinal)) 'Verify script evidence must record managed-process cleanup readback.'
Assert-Contract ($content.Contains('exactDatabaseRemaining', [StringComparison]::Ordinal)) 'Verify script evidence must record exact disposable-database cleanup readback.'
Assert-Contract ($content.Contains('pre-existing-running-not-stopped', [StringComparison]::Ordinal)) 'Verify script evidence must distinguish pre-existing compose services that were not owned.'
Assert-Contract ($content.Contains('cleanupErrors', [StringComparison]::Ordinal)) 'Verify script must aggregate cleanup errors and fail after all cleanup and evidence steps.'

Write-Host 'ERP and WMS delivery-completion cross-process verify script contract tests passed.'
