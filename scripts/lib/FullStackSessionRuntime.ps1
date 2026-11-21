# Script-Governance:
#   Category: library
#   SideEffects:
#     - Drives full-stack session lifecycle through ScriptAutomation-wrapped commands
#     - Reads Docker, Aspire and HTTP state through caller-injected actions
#   Writes:
#     - The session state and evidence roots the caller supplies
#   Cleanup:
#     - Stops the sessions it starts through the caller-supplied stop action
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

$runtimeLibraryRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/FullStackSessionState.ps1')

function Wait-NervFullStackPostgresRelations {
    param(
        [Parameter(Mandatory)] [string] $ContainerId,
        [Parameter(Mandatory)] [string] $Database,
        [Parameter(Mandatory)] [string[]] $Relations,
        [ValidateRange(1, 600)] [int] $TimeoutSeconds = 180,
        [scriptblock] $QueryAction,
        [scriptblock] $DelayAction
    )

    if ($Relations.Count -eq 0) { throw 'PostgreSQL relation readiness requires at least one frozen relation.' }
    foreach ($relation in $Relations) {
        if ("$relation" -notmatch '^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$') {
            throw "PostgreSQL readiness relation '$relation' must be a schema-qualified lowercase identifier."
        }
    }
    if ($null -eq $QueryAction) {
        $QueryAction = {
            param($OwnedContainerId, $OwnedDatabase, $RequiredRelations)
            $values = @($RequiredRelations | ForEach-Object { "('$_')" }) -join ','
            $sql = "SELECT COUNT(*) FROM (VALUES $values) AS required(name) WHERE to_regclass(required.name) IS NOT NULL;"
            $result = Invoke-NativeCommandOutput `
                -Command 'docker' `
                -Arguments @(
                    'exec', $OwnedContainerId, 'sh', '-c',
                    'PGPASSWORD="$POSTGRES_PASSWORD" exec psql -U "$POSTGRES_USER" -d "$1" -X -Atqc "$2"',
                    'nerv-fullstack-readiness', $OwnedDatabase, $sql
                ) `
                -WorkingDirectory $runtimeLibraryRoot `
                -TimeoutSeconds 30 `
                -Name "fullstack-postgres-relations-$OwnedDatabase" `
                -Environment (Get-NervFullStackNonSeedEnvironment)
            $count = 0
            if (-not [int]::TryParse("$($result.Stdout)".Trim(), [ref] $count)) {
                throw "PostgreSQL relation readiness returned a non-integer result for '$OwnedDatabase'."
            }
            return $count
        }
    }
    if ($null -eq $DelayAction) { $DelayAction = { param($Seconds) Start-Sleep -Seconds $Seconds } }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempts = 0
    do {
        $attempts++
        $observedCount = [int] (& $QueryAction $ContainerId $Database $Relations)
        if ($observedCount -eq $Relations.Count) {
            return [pscustomobject]@{ Attempts = $attempts; RelationCount = $observedCount }
        }
        if ($observedCount -lt 0 -or $observedCount -gt $Relations.Count) {
            throw "PostgreSQL relation readiness returned impossible count $observedCount/$($Relations.Count) for '$Database'."
        }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        & $DelayAction 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "PostgreSQL relations did not become ready in '$Database' within $TimeoutSeconds seconds: $($Relations -join ', ')."
}

function Test-NervDockerResourceOwnership {
    param(
        [Parameter(Mandatory)] [object] $InspectObject,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $RecordedIds
    )

    $id = "$($InspectObject.Id)"
    if (-not [Collections.Generic.HashSet[string]]::new([string[]]@($RecordedIds), [StringComparer]::Ordinal).Contains([string]$id)) {
        return $false
    }

    $labels = if ($null -ne $InspectObject.PSObject.Properties['Config'] -and
        $null -ne $InspectObject.Config -and
        $null -ne $InspectObject.Config.PSObject.Properties['Labels']) {
        $InspectObject.Config.Labels
    }
    elseif ($null -ne $InspectObject.PSObject.Properties['Labels']) {
        $InspectObject.Labels
    }
    else {
        $null
    }
    if ($null -eq $labels) {
        return $false
    }

    $sessionLabel = $labels.PSObject.Properties['com.nerv-iip.session']
    return $null -ne $sessionLabel -and [string]::Equals([string]$sessionLabel.Value, $SessionId, [StringComparison]::Ordinal)
}

function Test-NervDockerRecordedNameOwnership {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $RecordedNames
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        return $false
    }

    return ([Collections.Generic.HashSet[string]]::new([string[]]@($RecordedNames), [StringComparer]::Ordinal).Contains($Name)) -and
        $Name.EndsWith("-$SessionId", [StringComparison]::Ordinal)
}

function Test-NervDockerOptionalSessionLabel {
    param(
        [AllowNull()] [object] $Labels,
        [Parameter(Mandatory)] [string] $SessionId
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        return $false
    }
    if ($null -eq $Labels) {
        return $true
    }

    $sessionLabel = $Labels.PSObject.Properties['com.nerv-iip.session']
    return $null -eq $sessionLabel -or [string]::Equals([string]$sessionLabel.Value, $SessionId, [StringComparison]::Ordinal)
}

function Test-NervDockerNetworkOwnership {
    param(
        [Parameter(Mandatory)] [object] $InspectObject,
        [Parameter(Mandatory)] [string] $SessionId,
        [string[]] $RecordedIds = @()
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) { return $false }
    if ([Collections.Generic.HashSet[string]]::new([string[]]@('bridge', 'host', 'none'), [StringComparer]::Ordinal).Contains([string]$InspectObject.Name)) { return $false }

    $id = "$($InspectObject.Id)"
    $labels = if ($null -ne $InspectObject.PSObject.Properties['Labels']) { $InspectObject.Labels } else { $null }
    $sessionLabel = if ($null -ne $labels) { $labels.PSObject.Properties['com.nerv-iip.session'] } else { $null }
    if ($null -ne $sessionLabel) { return [string]::Equals([string]$sessionLabel.Value, $SessionId, [StringComparison]::Ordinal) }
    return [Collections.Generic.HashSet[string]]::new([string[]]@($RecordedIds), [StringComparer]::Ordinal).Contains($id)
}

function Get-NervFullStackCleanupWorkingDirectory {
    param([string] $StateRoot = (Get-NervFullStackStateRoot))

    $path = [System.IO.Path]::GetFullPath($StateRoot)
    [void] [System.IO.Directory]::CreateDirectory($path)
    return $path
}

function Test-NervFullStackAppHostAvailable {
    param([Parameter(Mandatory)] [object] $Manifest)

    return (Test-Path -LiteralPath "$($Manifest.worktreeRoot)" -PathType Container) -and
        (Test-Path -LiteralPath "$($Manifest.appHostProject)" -PathType Leaf)
}

function Read-NervAspireJson {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [ValidateRange(1, 4194304)] [int] $MaxCharacters = 1048576,
        [switch] $RequireResources
    )

    if ($Text.Length -gt $MaxCharacters) {
        throw "Aspire output exceeded the $MaxCharacters character parsing limit."
    }

    $payloads = [System.Collections.Generic.List[object]]::new()
    for ($start = 0; $start -lt $Text.Length; $start++) {
        if ($Text[$start] -ne '{') { continue }

        $depth = 0
        $inString = $false
        $escaped = $false
        $end = -1
        for ($index = $start; $index -lt $Text.Length; $index++) {
            $character = $Text[$index]
            if ($inString) {
                if ($escaped) { $escaped = $false; continue }
                if ($character -eq '\') { $escaped = $true; continue }
                if ($character -eq '"') { $inString = $false }
                continue
            }
            if ($character -eq '"') { $inString = $true; continue }
            if ($character -eq '{') { $depth++ }
            elseif ($character -eq '}') {
                $depth--
                if ($depth -eq 0) { $end = $index; break }
            }
        }
        if ($end -lt $start) { continue }

        $candidate = $Text.Substring($start, $end - $start + 1)
        try { $payloads.Add(($candidate | ConvertFrom-Json -Depth 40)) } catch { }
        $start = $end
    }

    if ($payloads.Count -ne 1) {
        $safeText = Protect-ScriptAutomationText -Text $Text
        throw "Expected exactly one Aspire JSON object, found $($payloads.Count); redacted output length was $($safeText.Length)."
    }
    $payload = $payloads[0]
    if ($RequireResources) {
        $resourcesProperty = $payload.PSObject.Properties['resources']
        $resources = if ($null -eq $resourcesProperty -or $null -eq $resourcesProperty.Value) {
            @()
        }
        else {
            @($resourcesProperty.Value)
        }
        if ($resources.Count -eq 0) {
            throw 'Aspire describe JSON did not contain a non-empty resources collection.'
        }
    }
    return $payload
}

function Get-NervAspireStartIdentity {
    param([Parameter(Mandatory)] [object] $StartObject)

    $appHostPid = [int] $StartObject.appHostPid
    $cliPid = [int] $StartObject.cliPid
    if ($appHostPid -le 0 -or $cliPid -le 0 -or [string]::IsNullOrWhiteSpace("$($StartObject.appHostPath)")) {
        throw 'Aspire detached-start JSON did not contain its AppHost path and process identities.'
    }

    return [pscustomobject]@{
        AppHostId = "pid:$appHostPid"
        AppHostPath = [System.IO.Path]::GetFullPath("$($StartObject.appHostPath)")
        AppHostPid = $appHostPid
        CliPid = $cliPid
        LogFile = "$($StartObject.logFile)"
    }
}

function Get-NervAspireResourceSnapshot {
    param(
        [Parameter(Mandatory)] [object] $DescribeObject,
        [Parameter(Mandatory)] [string] $ResourceName
    )

    $matches = @($DescribeObject.resources | Where-Object { [string]::Equals([string]$_.displayName, $ResourceName, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        throw "Expected one Aspire resource named '$ResourceName', found $($matches.Count)."
    }
    return $matches[0]
}

function Get-NervAspireResourceEndpoint {
    param(
        [Parameter(Mandatory)] [object] $DescribeObject,
        [Parameter(Mandatory)] [string] $ResourceName,
        [Parameter(Mandatory)] [string] $EndpointName
    )

    $resource = Get-NervAspireResourceSnapshot -DescribeObject $DescribeObject -ResourceName $ResourceName
    $matches = @($resource.urls | Where-Object { [string]::Equals([string]$_.name, $EndpointName, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1 -or [string]::IsNullOrWhiteSpace("$($matches[0].url)")) {
        throw "Expected one '$EndpointName' endpoint for Aspire resource '$ResourceName'."
    }
    return "$($matches[0].url)"
}

function Get-NervAspireDescribeObject {
    param(
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $result = Invoke-AspireOutput `
        -Arguments @('describe', '--format', 'Json', '--apphost', $AppHostProject, '--non-interactive', '--nologo') `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 60 `
        -Name 'fullstack-aspire-describe' `
        -AllowPartialOutput `
        -Environment (Get-NervFullStackNonSeedEnvironment)
    return (Read-NervAspireJson -Text "$($result.Stdout)" -RequireResources)
}

function Wait-NervAspireResource {
    param(
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $ResourceName,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [ValidateRange(1, 1200)] [int] $TimeoutSeconds = 600
    )

    Invoke-AspireOutput `
        -Arguments @('wait', $ResourceName, '--status', 'healthy', '--timeout', "$TimeoutSeconds", '--apphost', $AppHostProject, '--non-interactive', '--nologo') `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds ($TimeoutSeconds + 30) `
        -Name "fullstack-aspire-wait-$ResourceName" `
        -AllowPartialOutput `
        -Environment (Get-NervFullStackNonSeedEnvironment) | Out-Null
}

function Get-NervAspireDescribeObjectWithEndpoints {
    param(
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [string[]] $ResourceNames = @('gateway', 'business-gateway', 'console', 'business-console', 'screen'),
        [string] $EndpointName = 'http',
        [int] $TimeoutSeconds = 120,
        [int] $PollSeconds = 3
    )

    # Aspire reports a resource healthy before its endpoint URL is guaranteed to be present in
    # `aspire describe` output, so a single snapshot can miss a just-started resource. Poll until
    # every required endpoint is published or the bounded deadline expires.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastErrorMessage = ''
    while ($true) {
        $describe = Get-NervAspireDescribeObject -AppHostProject $AppHostProject -WorkingDirectory $WorkingDirectory
        $resolved = $true
        foreach ($resourceName in $ResourceNames) {
            try {
                Get-NervAspireResourceEndpoint `
                    -DescribeObject $describe `
                    -ResourceName $resourceName `
                    -EndpointName $EndpointName | Out-Null
            }
            catch {
                $resolved = $false
                $lastErrorMessage = "$($_.Exception.Message)"
                break
            }
        }
        if ($resolved) { return $describe }
        if ((Get-Date) -ge $deadline) {
            throw "Aspire endpoints were not published within $TimeoutSeconds seconds. Last failure: $lastErrorMessage"
        }
        Start-Sleep -Seconds $PollSeconds
    }
}

function Save-NervFullStackEndpoints {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $DescribeObject
    )

    $endpoints = [ordered]@{}
    foreach ($resourceName in @('gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
        $endpoints[$resourceName] = Get-NervAspireResourceEndpoint `
            -DescribeObject $DescribeObject `
            -ResourceName $resourceName `
            -EndpointName 'http'
    }
    $Manifest.endpoints = $endpoints
    return $Manifest
}

function Get-NervFullStackEndpointValue {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ResourceName
    )

    $value = if ($Manifest.endpoints -is [System.Collections.IDictionary]) {
        $Manifest.endpoints[$ResourceName]
    }
    else {
        $property = $Manifest.endpoints.PSObject.Properties[$ResourceName]
        if ($null -ne $property) { $property.Value } else { $null }
    }
    if ([string]::IsNullOrWhiteSpace("$value")) {
        throw "Manifest '$($Manifest.sessionId)' has no endpoint named '$ResourceName'."
    }
    return "$value"
}

function Protect-NervFullStackDiagnosticText {
    param(
        [AllowNull()] [string] $Text,
        [string[]] $SensitiveValues = @()
    )

    $safe = Protect-ScriptAutomationText -Text $Text
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $safe = $safe.Replace($sensitiveValue, '<redacted>')
        }
    }
    return $safe
}

function Invoke-NervFullStackSmokeScenario {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $SessionAdminPassword,
        [string] $SessionWorkerPassword,
        [scriptblock] $WaitAction,
        [scriptblock] $HttpCheckAction,
        [scriptblock] $AspireSnapshotAction,
        [scriptblock] $BrowserAction
    )

    if ($null -eq $WaitAction) {
        $WaitAction = {
            param($Name, $InputManifest)
            Wait-NervAspireResource `
                -AppHostProject "$($InputManifest.appHostProject)" `
                -ResourceName $Name `
                -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $HttpCheckAction) {
        $HttpCheckAction = {
            param($Name, $Url)
            try {
                Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 30 -UseBasicParsing | Out-Null
            }
            catch {
                $statusCode = if ($null -ne $_.Exception.Response) { [int] $_.Exception.Response.StatusCode } else { 0 }
                if ($statusCode -le 0 -or $statusCode -ge 500) { throw "HTTP check failed for '$Name' at '$Url': $($_.Exception.Message)" }
            }
        }
    }
    if ($null -eq $AspireSnapshotAction) {
        $AspireSnapshotAction = {
            param($InputManifest)
            Get-NervAspireDescribeObject -AppHostProject "$($InputManifest.appHostProject)" -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $BrowserAction) {
        $BrowserAction = {
            param($Environment, $InputManifest)
            Invoke-NervFullStackProxyBrowserCheck -Environment $Environment -Manifest $InputManifest | Out-Null
        }
    }

    $resourceNames = @('gateway', 'business-gateway', 'console', 'business-console', 'screen')
    foreach ($resourceName in $resourceNames) {
        & $WaitAction $resourceName $Manifest | Out-Null
        $url = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName $resourceName
        & $HttpCheckAction $resourceName $url | Out-Null
    }

    $snapshot = & $AspireSnapshotAction $Manifest
    $finishedProjects = @($snapshot.resources | Where-Object {
        "$($_.resourceType)" -like 'Project*' -and [string]::Equals([string]$_.state, 'Finished', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($finishedProjects.Count -gt 0) {
        throw "Aspire project resources finished unexpectedly: $($finishedProjects.displayName -join ', ')."
    }

    $childEnvironment = @{
        NERV_IIP_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'gateway'
        NERV_IIP_BUSINESS_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-gateway'
        NERV_IIP_PLAYWRIGHT_BASE_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-console'
        NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $SessionAdminPassword
    }
    if (-not [string]::IsNullOrWhiteSpace($SessionWorkerPassword)) {
        $childEnvironment.NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD = $SessionWorkerPassword
    }
    & $BrowserAction $childEnvironment $Manifest | Out-Null
    return [pscustomobject]@{ ExitCode = 0; ChildEnvironment = $childEnvironment; CheckedResources = $resourceNames }
}

function Assert-NervPlaywrightJsonReport {
    param([Parameter(Mandatory)] [string] $ReportPath)

    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) {
        throw "Playwright JSON report was not created at '$ReportPath'."
    }
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json -Depth 100
    if ($null -eq $report.stats) { throw "Playwright JSON report '$ReportPath' has no stats object." }
    $expected = [int] $report.stats.expected
    $skipped = [int] $report.stats.skipped
    $unexpected = [int] $report.stats.unexpected
    if ($expected -lt 1 -or $skipped -ne 0 -or $unexpected -ne 0) {
        throw "Playwright full-stack gate requires at least one expected test and zero skipped or unexpected tests; expected=$expected skipped=$skipped unexpected=$unexpected."
    }
    return $report
}

function Invoke-NervFullStackProxyBrowserCheck {
    param(
        [Parameter(Mandatory)] [hashtable] $Environment,
        [Parameter(Mandatory)] [object] $Manifest
    )

    [void] [System.IO.Directory]::CreateDirectory("$($Manifest.artifactPath)")
    $reportPath = Join-Path "$($Manifest.artifactPath)" 'playwright-fullstack-proxy.json'
    Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    $browserEnvironment = @{}
    foreach ($entry in $Environment.GetEnumerator()) { $browserEnvironment[$entry.Key] = "$($entry.Value)" }
    $browserEnvironment.PLAYWRIGHT_JSON_OUTPUT_FILE = $reportPath
    if (-not $browserEnvironment.ContainsKey('NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD')) {
        $browserEnvironment.NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD = $null
    }
    $browserEnvironment['Parameters__iam-seed-demo-worker-password'] = $null
    Invoke-Pnpm `
        -Arguments @(
            '-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test',
            'e2e/fullstack-proxy.spec.ts', '--project=desktop', '--reporter=json',
            '--output', (Join-Path "$($Manifest.artifactPath)" 'test-results')
        ) `
        -WorkingDirectory "$($Manifest.worktreeRoot)" `
        -TimeoutSeconds 300 `
        -Name "fullstack-$($Manifest.sessionId)-playwright" `
        -Environment $browserEnvironment `
        -SensitiveValues @($Environment['NERV_IIP_FULLSTACK_ADMIN_PASSWORD'], $Environment['NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD']) | Out-Null
    return Assert-NervPlaywrightJsonReport -ReportPath $reportPath
}

function Assert-NervIssue1912WalkthroughEvidence {
    param([Parameter(Mandatory)] [string] $EvidencePath)

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        throw "Issue #1912 walkthrough evidence was not created at '$EvidencePath'."
    }
    $evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -Depth 100
    if (-not [string]::Equals([string]$evidence.issue, 'GitHub #1912 / NERV-1127', [StringComparison]::Ordinal)) {
        throw 'Issue #1912 walkthrough evidence must identify GitHub #1912 / NERV-1127.'
    }
    $requiredProfile = [ordered]@{
        runtimeProfileSource = 'session-manifest'
        transport = 'redis-cross-process'
        persistence = 'postgresql'
        walkthroughSeedEnabled = 'true'
    }
    foreach ($profile in $requiredProfile.GetEnumerator()) {
        if (-not [string]::Equals([string]$evidence.($profile.Key), [string]$profile.Value, [StringComparison]::Ordinal)) {
            throw "Issue #1912 walkthrough evidence must declare $($profile.Key)=$($profile.Value)."
        }
    }
    $requiredIdentity = [ordered]@{
        rfqNo = 'RFQ-WALK-001'
        supplierQuotationNo = 'SQ-WALK-001'
        salesQuotationNo = 'QUO-WALK-001'
        purchaseOrderNo = 'PO-WALK-001'
        salesOrderNo = 'SO-WALK-001'
        deliveryOrderNo = 'DO-WALK-001'
    }
    foreach ($identity in $requiredIdentity.GetEnumerator()) {
        if (-not [string]::Equals([string]$evidence.($identity.Key), [string]$identity.Value, [StringComparison]::Ordinal)) {
            throw "Issue #1912 walkthrough evidence must identify $($identity.Key)=$($identity.Value)."
        }
    }

    $requiredNodes = @(
        'rfq-supplier-quotation',
        'supplier-quotation-purchase-order',
        'purchase-order-approval',
        'purchase-order-receipt',
        'receipt-inbound-inventory',
        'sales-quotation-sales-order',
        'sales-order-demand',
        'demand-mrp-suggestion',
        'mrp-suggestion-mes-work-order',
        'mes-work-order-production',
        'production-finished-goods-receipt',
        'finished-goods-inventory',
        'sales-order-delivery',
        'delivery-wms-outbound',
        'wms-completed-erp-delivery',
        'erp-account-receivable'
    )
    $requiredNodeSet = [Collections.Generic.HashSet[string]]::new([string[]]@($requiredNodes), [StringComparer]::Ordinal)
    $entries = @($evidence.entries)
    if ($entries.Count -ne $requiredNodes.Count) {
        throw "Issue #1912 walkthrough evidence must contain exactly $($requiredNodes.Count) entries; found $($entries.Count)."
    }
    foreach ($node in $requiredNodes) {
        $nodeEntries = @($entries | Where-Object { [string]::Equals([string]$_.node, $node, [StringComparison]::Ordinal) })
        if ($nodeEntries.Count -ne 1) {
            throw "Issue #1912 walkthrough evidence must contain exactly one '$node' entry; found $($nodeEntries.Count)."
        }
    }
    foreach ($entry in $entries) {
        if (-not [string]::Equals([string]$entry.conclusion, 'runtime-confirmed', [StringComparison]::Ordinal)) {
            throw "Issue #1912 walkthrough evidence cannot pass with node '$($entry.node)' conclusion '$($entry.conclusion)'."
        }
        foreach ($field in @('sourceObject', 'downstreamObject', 'stableKey', 'demoWording')) {
            if ([string]::IsNullOrWhiteSpace([string]$entry.$field)) {
                throw "Issue #1912 walkthrough evidence node '$($entry.node)' is missing '$field'."
            }
        }
    }

    $receipt = $entries | Where-Object { [string]::Equals([string]$_.node, 'purchase-order-receipt', [StringComparison]::Ordinal) }
    $inbound = $entries | Where-Object { [string]::Equals([string]$_.node, 'receipt-inbound-inventory', [StringComparison]::Ordinal) }
    $receiptNo = [string]$receipt.responseOrLog.receipt.data.purchaseReceiptNo
    if ([string]::IsNullOrWhiteSpace($receiptNo) -or
        -not [string]::Equals([string]$evidence.purchaseReceiptNo, $receiptNo, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$receipt.downstreamObject, $receiptNo, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$inbound.sourceObject, $receiptNo, [StringComparison]::Ordinal)) {
        throw 'Issue #1912 must trace the purchase receipt number returned by ERP into the warehouse evidence.'
    }
    $uiProofs = @($evidence.uiEvidence)
    $uiProofNodeSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($proof in $uiProofs) {
        if ([int]$proof.pageHttpStatus -ne 200 -or [int]$proof.listHttpStatus -ne 200) {
            throw "Issue #1912 UI proof for '$($proof.page)' must record page/list HTTP 200."
        }
        if (-not $requiredNodeSet.Contains([string]$proof.node)) {
            throw "Issue #1912 UI proof references unknown walkthrough node '$($proof.node)'."
        }
        [void] $uiProofNodeSet.Add([string]$proof.node)
        foreach ($field in @('node', 'page', 'listPath', 'stableKey', 'renderedRowText', 'emptyText', 'screenshot')) {
            if ([string]::IsNullOrWhiteSpace([string]$proof.$field)) {
                throw "Issue #1912 UI proof is missing '$field'."
            }
        }
        if (-not (Test-Path -LiteralPath ([string]$proof.screenshot) -PathType Leaf)) {
            throw "Issue #1912 UI proof screenshot was not created at '$($proof.screenshot)'."
        }
    }
    foreach ($node in $requiredNodes) {
        if (-not $uiProofNodeSet.Contains($node)) {
            throw "Issue #1912 walkthrough evidence is missing a UI proof for required node '$node'."
        }
    }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.requestFailurePolicy)) {
        throw 'Issue #1912 walkthrough evidence must declare its request failure policy.'
    }
    if (@($evidence.failedRequests).Count -ne 0) { throw 'Issue #1912 walkthrough evidence contains failed browser requests.' }
    if (@($evidence.pageErrors).Count -ne 0) { throw 'Issue #1912 walkthrough evidence contains browser page errors.' }
    if (-not [string]::Equals([string]$evidence.conclusion, 'runtime-confirmed', [StringComparison]::Ordinal)) {
        throw 'Issue #1912 walkthrough evidence conclusion is not runtime-confirmed.'
    }
    if ([int]$evidence.summary.'runtime-confirmed' -ne $requiredNodes.Count -or [int]$evidence.summary.gap -ne 0 -or [int]$evidence.summary.'not-verified' -ne 0) {
        throw 'Issue #1912 walkthrough evidence summary is not a complete runtime-confirmed set.'
    }
    $raw = Get-Content -LiteralPath $EvidencePath -Raw
    foreach ($forbiddenPattern in @('(?i)authorization', '(?i)bearer\s+', '(?i)password', '(?i)access[_-]?token', '(?i)refresh[_-]?token', '(?i)\b(?:token|secret|connectionString|jwt)\b\s*["'']?\s*[:=]\s*(?!["'']?<redacted)')) {
        if ($raw -match $forbiddenPattern) { throw "Issue #1912 walkthrough evidence contains forbidden secret-shaped text matching '$forbiddenPattern'." }
    }
    return $evidence
}

function Invoke-NervIssue1912WalkthroughBrowserCheck {
    param(
        [Parameter(Mandatory)] [hashtable] $Environment,
        [Parameter(Mandatory)] [object] $Manifest
    )

    [void] [System.IO.Directory]::CreateDirectory("$($Manifest.artifactPath)")
    $reportPath = Join-Path "$($Manifest.artifactPath)" 'playwright-issue1912-real-machine-walkthrough.json'
    $evidencePath = Join-Path "$($Manifest.artifactPath)" 'issue1912-real-machine-walkthrough.json'
    Remove-Item -LiteralPath $reportPath, $evidencePath -Force -ErrorAction SilentlyContinue
    $browserEnvironment = @{}
    foreach ($entry in $Environment.GetEnumerator()) { $browserEnvironment[$entry.Key] = "$($entry.Value)" }
    $browserEnvironment.PLAYWRIGHT_JSON_OUTPUT_FILE = $reportPath
    $browserEnvironment.NERV_IIP_ISSUE_1912_EVIDENCE_PATH = $evidencePath
    Invoke-WithScopedEnvironment -Variables $browserEnvironment -ScriptBlock {
        Invoke-Pnpm `
            -Arguments @(
                '-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test',
                'e2e/issue1912-real-machine-walkthrough.spec.ts', '--project=desktop', '--reporter=json',
                '--output', (Join-Path "$($Manifest.artifactPath)" 'test-results')
            ) `
            -WorkingDirectory "$($Manifest.worktreeRoot)" `
            -TimeoutSeconds 1800 `
            -Name "fullstack-$($Manifest.sessionId)-issue1912-real-machine-walkthrough" `
            -SensitiveValues @($Environment['NERV_IIP_FULLSTACK_ADMIN_PASSWORD'], $Environment['NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD']) | Out-Null
    }
    Assert-NervPlaywrightJsonReport -ReportPath $reportPath | Out-Null
    return Assert-NervIssue1912WalkthroughEvidence -EvidencePath $evidencePath
}

function Invoke-NervIssue1912WalkthroughScenario {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $SessionAdminPassword,
        [Parameter(Mandatory)] [string] $SessionWorkerPassword,
        [scriptblock] $WaitAction,
        [scriptblock] $AspireSnapshotAction,
        [scriptblock] $BrowserAction
    )

    if ([string]::IsNullOrWhiteSpace($SessionWorkerPassword)) {
        throw 'Issue #1912 walkthrough requires the managed WMS worker password; rerun with -EnableWmsDemoWorker.'
    }

    if ($null -eq $WaitAction) {
        $WaitAction = {
            param($Name, $InputManifest)
            Wait-NervAspireResource `
                -AppHostProject "$($InputManifest.appHostProject)" `
                -ResourceName $Name `
                -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $AspireSnapshotAction) {
        $AspireSnapshotAction = {
            param($InputManifest)
            Get-NervAspireDescribeObject -AppHostProject "$($InputManifest.appHostProject)" -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $BrowserAction) {
        $BrowserAction = {
            param($Environment, $InputManifest)
            Invoke-NervIssue1912WalkthroughBrowserCheck -Environment $Environment -Manifest $InputManifest | Out-Null
        }
    }
    if (-not [string]::Equals([string]$Manifest.runtime.messagingProvider, 'Redis', [StringComparison]::Ordinal)) {
        throw "Issue #1912 walkthrough requires a Redis session profile; manifest recorded '$($Manifest.runtime.messagingProvider)'."
    }
    if (-not [string]::Equals([string]$Manifest.runtime.persistenceProvider, 'PostgreSQL', [StringComparison]::Ordinal)) {
        throw "Issue #1912 walkthrough requires a PostgreSQL session profile; manifest recorded '$($Manifest.runtime.persistenceProvider)'."
    }
    $resourceNames = @(
        'postgres', 'redis', 'iam', 'gateway', 'business-gateway', 'business-console',
        'business-master-data', 'business-product-engineering', 'business-inventory',
        'business-quality', 'business-mes', 'business-demand-planning', 'business-wms',
        'business-erp', 'business-approval'
    )
    foreach ($resourceName in $resourceNames) { & $WaitAction $resourceName $Manifest | Out-Null }
    $snapshot = & $AspireSnapshotAction $Manifest
    $resourceNameSet = [Collections.Generic.HashSet[string]]::new([string[]]@($resourceNames), [StringComparer]::Ordinal)
    $finishedProjects = @($snapshot.resources | Where-Object {
        "$($_.resourceType)" -like 'Project*' -and [string]::Equals([string]$_.state, 'Finished', [StringComparison]::OrdinalIgnoreCase) -and
        $resourceNameSet.Contains([string]$_.displayName)
    })
    if ($finishedProjects.Count -gt 0) {
        throw "Aspire project resources finished unexpectedly: $($finishedProjects.displayName -join ', ')."
    }
    $childEnvironment = @{
        NERV_IIP_PLAYWRIGHT_BASE_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-console'
        NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $SessionAdminPassword
        NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD = $SessionWorkerPassword
        NERV_IIP_FULLSTACK_STATE_ROOT = Get-NervFullStackStateRoot
        NERV_IIP_ISSUE_1912_RUNTIME_PROFILE_SOURCE = 'session-manifest'
        NERV_IIP_ISSUE_1912_TRANSPORT = 'redis-cross-process'
        NERV_IIP_ISSUE_1912_PERSISTENCE = 'postgresql'
        NERV_IIP_ISSUE_1912_WALKTHROUGH_SEED_ENABLED = 'true'
    }
    & $BrowserAction $childEnvironment $Manifest | Out-Null
    return [pscustomobject]@{ ExitCode = 0; ChildEnvironment = $childEnvironment; CheckedResources = $resourceNames }
}

function Get-NervFullStackGuardianSensitiveValues {
    $environmentNames = @(
        'NERV_IIP_FULLSTACK_ADMIN_PASSWORD',
        'NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD',
        'Parameters__iam-jwt-private-key-pem',
        'Parameters__iam-secrets-pepper',
        'Parameters__iam-enterprise-identity-mfa-code',
        'Parameters__internal-service-bearer-token',
        'Parameters__template-asset-retirement-proof-secret-base64',
        'Parameters__redis-password',
        'Parameters__minio-root-password',
        'Parameters__iam-seed-admin-password',
        'Parameters__iam-seed-demo-worker-password',
        'Parameters__iam-seed-connector-host-secret',
        'Parameters__connector-ingestion-token-signing-key'
    )
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $values = [Collections.Generic.List[string]]::new()
    foreach ($environmentName in $environmentNames) {
        $value = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
        if (-not [string]::IsNullOrEmpty($value) -and $seen.Add($value)) {
            $values.Add($value)
        }
    }
    return @($values)
}

function Invoke-NervFullStackGuardian {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [ValidateSet('Automated', 'Interactive')] [string] $Mode,
        [int] $CoordinatorPid,
        [string] $CoordinatorStartTimeUtc,
        [ValidateRange(1, 3600)] [int] $IntervalSeconds = 60,
        [ValidateRange(1, 10)] [int] $MaximumCoordinatorIdentityObservations = 3,
        [ValidateRange(1, 60)] [int] $CoordinatorIdentityRetryDelaySeconds = 1,
        [ValidateRange(1, 10)] [int] $MaximumObservationFailures = 3,
        [ValidateRange(1, 10)] [int] $MaximumStopAttempts = 3,
        [scriptblock] $ReadAction,
        [scriptblock] $CoordinatorIdentityAction,
        [scriptblock] $IdentityEvidenceAction,
        [scriptblock] $DiagnosticAction,
        [scriptblock] $StopAction,
        [scriptblock] $DelayAction,
        [scriptblock] $UtcNowAction,
        [string[]] $SensitiveValues = @()
    )

    $effectiveSensitiveValueSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $effectiveSensitiveValues = [Collections.Generic.List[string]]::new()
    foreach ($sensitiveValue in @($SensitiveValues) + @(Get-NervFullStackGuardianSensitiveValues)) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue) -and $effectiveSensitiveValueSet.Add($sensitiveValue)) {
            $effectiveSensitiveValues.Add($sensitiveValue)
        }
    }

    if ($null -eq $ReadAction) { $ReadAction = { Read-NervFullStackManifest -SessionId $SessionId } }
    if ($null -eq $CoordinatorIdentityAction) {
        $CoordinatorIdentityAction = {
            Get-NervProcessIdentityStatus `
                -ProcessId $CoordinatorPid `
                -ProcessStartTimeUtc $CoordinatorStartTimeUtc `
                -Detailed
        }
    }
    if ($null -eq $IdentityEvidenceAction) {
        $IdentityEvidenceAction = {
            param($InputManifest, $Observation, $InputLeaseExpired, $InputCoordinatorMissing)
            Write-NervFullStackGuardianIdentityEvidence `
                -Manifest $InputManifest `
                -Observation $Observation `
                -LeaseExpired $InputLeaseExpired `
                -CoordinatorMissing $InputCoordinatorMissing `
                -SensitiveValues @($effectiveSensitiveValues) | Out-Null
        }
    }
    if ($null -eq $DiagnosticAction) {
        $DiagnosticAction = {
            param($InputManifest)
            Invoke-NervFullStackDiagnosticCollection `
                -Manifest $InputManifest `
                -SensitiveValues @($effectiveSensitiveValues) | Out-Null
        }
    }
    if ($null -eq $StopAction) {
        $StopAction = {
            $stopEnvironment = Get-NervFullStackNonSeedEnvironment
            $stopEnvironment.NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID = "$PID"
            Invoke-PwshScript `
                -ScriptPath (Join-Path $runtimeLibraryRoot 'scripts/fullstack/manage-fullstack-session.ps1') `
                -Arguments @('stop', '-SessionId', $SessionId) `
                -WorkingDirectory $runtimeLibraryRoot `
                -TimeoutSeconds 300 `
                -Name "fullstack-$SessionId-guardian-stop" `
                -Environment $stopEnvironment `
                -SensitiveValues @($effectiveSensitiveValues) | Out-Null
        }
    }
    if ($null -eq $DelayAction) { $DelayAction = { param($Seconds) Start-Sleep -Seconds $Seconds } }
    if ($null -eq $UtcNowAction) { $UtcNowAction = { [DateTimeOffset]::UtcNow } }
    $coordinatorIdentityInputComplete = $CoordinatorPid -gt 0 -and -not [string]::IsNullOrWhiteSpace($CoordinatorStartTimeUtc)
    $observeCoordinatorIdentity = {
        if (-not $coordinatorIdentityInputComplete) {
            New-NervProcessIdentityObservation `
                -Status Unknown `
                -ProcessId $CoordinatorPid `
                -ExpectedStartTimeUtc $CoordinatorStartTimeUtc `
                -FailureReason 'Coordinator identity input is incomplete.'
        }
        else {
            try {
                & $CoordinatorIdentityAction
            }
            catch {
                New-NervProcessIdentityObservation `
                    -Status Unknown `
                    -ProcessId $CoordinatorPid `
                    -ExpectedStartTimeUtc $CoordinatorStartTimeUtc `
                    -FailureReason $_.Exception.Message
            }
        }
    }

    Write-Host "Guardian started for '$SessionId' in $Mode mode."
    $observationFailures = 0
    while ($true) {
        try {
            $manifest = & $ReadAction
            $observationFailures = 0
        }
        catch {
            $observationFailures++
            $safeManifestObservationError = Protect-NervFullStackDiagnosticText `
                -Text "$($_.Exception.Message)" `
                -SensitiveValues @($effectiveSensitiveValues)
            Write-Warning "Guardian manifest observation $observationFailures/$MaximumObservationFailures failed for '$SessionId': $safeManifestObservationError"
            if ($observationFailures -ge $MaximumObservationFailures) {
                throw "Guardian manifest observation failed for '$SessionId': $safeManifestObservationError"
            }
            & $DelayAction $IntervalSeconds
            continue
        }

        if ([string]::Equals([string]$manifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase)) { return [pscustomobject]@{ State = 'Stopped' } }
        $leaseExpired = [DateTimeOffset] $manifest.leaseExpiresAtUtc -le [DateTimeOffset] (& $UtcNowAction)
        $coordinatorObservation = & $observeCoordinatorIdentity
        $coordinatorIdentityObservationCount = 1
        if (
            -not $leaseExpired -and
            [string]::Equals($Mode, 'Automated', [StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$coordinatorObservation.status, 'Unknown', [StringComparison]::Ordinal)
        ) {
            if ($coordinatorIdentityInputComplete) {
                for ($identityObservation = 2; $identityObservation -le $MaximumCoordinatorIdentityObservations; $identityObservation++) {
                    & $DelayAction $CoordinatorIdentityRetryDelaySeconds
                    $coordinatorObservation = & $observeCoordinatorIdentity
                    $coordinatorIdentityObservationCount++
                    if (-not [string]::Equals([string]$coordinatorObservation.status, 'Unknown', [StringComparison]::Ordinal)) { break }
                }
                if ([DateTimeOffset] $manifest.leaseExpiresAtUtc -le [DateTimeOffset] (& $UtcNowAction)) {
                    try {
                        $manifest = & $ReadAction
                        $observationFailures = 0
                    }
                    catch {
                        $observationFailures++
                        $safeManifestObservationError = Protect-NervFullStackDiagnosticText `
                            -Text "$($_.Exception.Message)" `
                            -SensitiveValues @($effectiveSensitiveValues)
                        Write-Warning "Guardian manifest observation $observationFailures/$MaximumObservationFailures failed for '$SessionId': $safeManifestObservationError"
                        if ($observationFailures -ge $MaximumObservationFailures) {
                            throw "Guardian manifest observation failed for '$SessionId': $safeManifestObservationError"
                        }
                        & $DelayAction $IntervalSeconds
                        continue
                    }
                    if ([string]::Equals([string]$manifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase)) { return [pscustomobject]@{ State = 'Stopped' } }
                    $leaseExpired = [DateTimeOffset] $manifest.leaseExpiresAtUtc -le [DateTimeOffset] (& $UtcNowAction)
                }
            }
            if (
                -not $leaseExpired -and
                [string]::Equals([string]$coordinatorObservation.status, 'Unknown', [StringComparison]::Ordinal)
            ) {
                Write-Warning "Guardian retained '$SessionId' after coordinator identity remained Unknown for $coordinatorIdentityObservationCount observation(s)."
                & $DelayAction $IntervalSeconds
                continue
            }
        }
        $coordinatorMissing = [string]::Equals($Mode, 'Automated', [StringComparison]::OrdinalIgnoreCase) -and
            (
                [string]::Equals([string]$coordinatorObservation.status, 'Absent', [StringComparison]::Ordinal) -or
                [string]::Equals([string]$coordinatorObservation.status, 'Mismatched', [StringComparison]::Ordinal)
            )
        if (-not ($leaseExpired -or $coordinatorMissing)) {
            & $DelayAction $IntervalSeconds
            continue
        }

        Write-Host "Guardian cleanup triggered for '$SessionId': leaseExpired=$leaseExpired coordinatorMissing=$coordinatorMissing coordinatorStatus=$($coordinatorObservation.status)."
        try {
            & $IdentityEvidenceAction $manifest $coordinatorObservation $leaseExpired $coordinatorMissing
        }
        catch {
            $safeIdentityEvidenceError = Protect-NervFullStackDiagnosticText `
                -Text "$($_.Exception.Message)" `
                -SensitiveValues @($effectiveSensitiveValues)
            Write-Warning "Guardian coordinator identity evidence failed for '$SessionId'; cleanup will continue: $safeIdentityEvidenceError"
        }
        try {
            & $DiagnosticAction $manifest
        }
        catch {
            $safeDiagnosticError = Protect-NervFullStackDiagnosticText `
                -Text "$($_.Exception.Message)" `
                -SensitiveValues @($effectiveSensitiveValues)
            Write-Warning "Guardian diagnostic collection failed for '$SessionId'; cleanup will continue: $safeDiagnosticError"
        }
        $lastStopError = 'cleanup did not reach Stopped'
        for ($attempt = 1; $attempt -le $MaximumStopAttempts; $attempt++) {
            try {
                & $StopAction
                $afterStop = & $ReadAction
                if ([string]::Equals([string]$afterStop.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase)) { return [pscustomobject]@{ State = 'Stopped' } }
                $lastStopError = "cleanup returned state '$($afterStop.state)'"
            }
            catch { $lastStopError = $_.Exception.Message }
            Write-Warning "Guardian stop attempt $attempt/$MaximumStopAttempts failed for '$SessionId': $lastStopError"
            if ($attempt -lt $MaximumStopAttempts) { & $DelayAction $IntervalSeconds }
        }
        throw "Guardian could not stop session '$SessionId' after $MaximumStopAttempts attempts: $lastStopError"
    }
}

function Write-NervFullStackGuardianIdentityEvidence {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $Observation,
        [Parameter(Mandatory)] [bool] $LeaseExpired,
        [Parameter(Mandatory)] [bool] $CoordinatorMissing,
        [string[]] $SensitiveValues = @()
    )

    $artifactPath = [System.IO.Path]::GetFullPath("$($Manifest.artifactPath)")
    [System.IO.Directory]::CreateDirectory($artifactPath) | Out-Null
    $evidencePath = Join-Path $artifactPath 'guardian-coordinator-identity.json'
    $temporaryPath = "$evidencePath.tmp-$([guid]::NewGuid().ToString('N'))"
    $evidence = [ordered]@{
        schemaVersion = 1
        sessionId = "$($Manifest.sessionId)"
        observedAtUtc = "$($Observation.observedAtUtc)"
        coordinator = [ordered]@{
            status = "$($Observation.status)"
            processId = [int] $Observation.processId
            expectedStartTimeUtc = if ($null -eq $Observation.expectedStartTimeUtc) { $null } else { "$($Observation.expectedStartTimeUtc)" }
            actualStartTimeUtc = if ($null -eq $Observation.actualStartTimeUtc) { $null } else { "$($Observation.actualStartTimeUtc)" }
            failureReason = if ([string]::IsNullOrWhiteSpace("$($Observation.failureReason)")) {
                $null
            }
            else {
                Protect-NervFullStackDiagnosticText `
                    -Text "$($Observation.failureReason)" `
                    -SensitiveValues $SensitiveValues
            }
        }
        trigger = [ordered]@{
            leaseExpired = $LeaseExpired
            coordinatorMissing = $CoordinatorMissing
        }
    }

    try {
        $safeJson = Protect-NervFullStackDiagnosticText `
            -Text (($evidence | ConvertTo-Json -Depth 10) + "`n") `
            -SensitiveValues $SensitiveValues
        [System.IO.File]::WriteAllText($temporaryPath, $safeJson, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $evidencePath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }

    return $evidencePath
}

function Invoke-NervFullStackDiagnosticCollection {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string[]] $SensitiveValues = @(),
        [scriptblock] $PrepareManifestAction,
        [scriptblock] $CollectAction
    )

    if ($null -eq $PrepareManifestAction) {
        $PrepareManifestAction = {
            param($InputManifest)
            Update-NervFullStackManifest `
                -SessionId "$($InputManifest.sessionId)" `
                -AllowedStates @('Running', 'Collecting', 'Failed', 'Stopping', 'Stopped', 'CleanupFailed') `
                -ReturnUnchangedOnStateMismatch `
                -UpdateAction {
                    param($current)
                    if ([string]::Equals([string]$current.state, 'Running', [StringComparison]::OrdinalIgnoreCase)) {
                        return (Move-NervFullStackSessionState -Manifest $current -State Collecting)
                    }
                    return $current
                }
        }
    }
    if ($null -eq $CollectAction) {
        $CollectAction = {
            param($InputManifest, $InputSensitiveValues)
            Collect-NervFullStackDiagnostics -Manifest $InputManifest -SensitiveValues $InputSensitiveValues
        }
    }

    $latest = & $PrepareManifestAction $Manifest
    if ($null -eq $latest) { $latest = $Manifest }
    # Collection is deliberately unconditional after manifest preparation. A guardian or a
    # concurrent failure may already have advanced the session beyond Running; that state is
    # diagnostic evidence, not a reason to skip the only pre-stop log sweep.
    return (& $CollectAction $latest $SensitiveValues)
}

function Get-NervFullStackCollectTimeoutSeconds {
    if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS)) { return 120 }
    $seconds = 0
    if (-not [int]::TryParse($env:NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS, [ref] $seconds) -or $seconds -lt 1 -or $seconds -gt 600) {
        throw 'NERV_IIP_FULLSTACK_COLLECT_TIMEOUT_SECONDS must be an integer from 1 through 600.'
    }
    return $seconds
}

function Collect-NervFullStackDiagnostics {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string[]] $SensitiveValues = @(),
        [scriptblock] $LogAction,
        [int] $TimeoutSeconds = (Get-NervFullStackCollectTimeoutSeconds)
    )

    if ($null -eq $LogAction) {
        $LogAction = {
            param($ResourceName, $InputManifest, $BoundedTimeoutSeconds, $InputSensitiveValues)
            $result = Invoke-AspireOutput `
                -Arguments @('logs', $ResourceName, '--tail', '500', '--format', 'Json', '--apphost', "$($InputManifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory "$($InputManifest.worktreeRoot)" `
                -TimeoutSeconds $BoundedTimeoutSeconds `
                -Name "fullstack-$($InputManifest.sessionId)-collect-$ResourceName" `
                -Environment (Get-NervFullStackNonSeedEnvironment) `
                -SensitiveValues @($InputSensitiveValues)
            return "$($result.Stdout)"
        }
    }

    $artifactPath = [System.IO.Path]::GetFullPath("$($Manifest.artifactPath)")
    $logDirectory = Join-Path $artifactPath 'aspire-logs'
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $collectionErrors = [System.Collections.Generic.List[string]]::new()
    # Every service any managed scenario can fail inside must be collectable, otherwise a failing
    # run leaves exactly the log that would explain it missing (equipment-branch failures live in
    # business-industrial-telemetry / business-maintenance, auth failures live in iam).
    $resourceNames = @(
        'iam', 'gateway', 'business-gateway', 'console', 'business-console', 'screen',
        'business-master-data', 'business-product-engineering', 'business-inventory',
        'business-quality', 'business-mes', 'business-demand-planning', 'business-wms',
        'business-erp', 'business-scheduling',
        'business-industrial-telemetry', 'business-maintenance', 'business-approval',
        'postgres', 'redis', 'minio'
    )
    foreach ($resourceName in $resourceNames) {
        try {
            $raw = (& $LogAction $resourceName $Manifest $TimeoutSeconds $SensitiveValues) -join "`n"
            $safe = Protect-NervFullStackDiagnosticText -Text $raw -SensitiveValues $SensitiveValues
            [System.IO.File]::WriteAllText(
                (Join-Path $logDirectory "$resourceName.ndjson"),
                $safe,
                [System.Text.UTF8Encoding]::new($false)
            )
        }
        catch {
            $collectionErrors.Add((Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues $SensitiveValues))
        }
    }

    $existingCleanupErrors = if ($null -ne $Manifest.cleanup) { @($Manifest.cleanup.errors) } else { @() }
    $summary = [ordered]@{
        schemaVersion = 1
        sessionId = "$($Manifest.sessionId)"
        state = "$($Manifest.state)"
        collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        endpoints = $Manifest.endpoints
        cleanupErrors = @($existingCleanupErrors | ForEach-Object {
            Protect-NervFullStackDiagnosticText -Text "$_" -SensitiveValues $SensitiveValues
        })
        collectionErrors = @($collectionErrors)
    }
    $summaryJson = Protect-NervFullStackDiagnosticText `
        -Text ($summary | ConvertTo-Json -Depth 20) `
        -SensitiveValues $SensitiveValues
    [System.IO.File]::WriteAllText(
        (Join-Path $artifactPath 'summary.json'),
        $summaryJson,
        [System.Text.UTF8Encoding]::new($false)
    )
    return [pscustomobject]@{ Complete = $collectionErrors.Count -eq 0; Errors = @($collectionErrors) }
}

function Invoke-NervManagedFullStackRun {
    param(
        [Parameter(Mandatory)] [scriptblock] $StartAction,
        [Parameter(Mandatory)] [scriptblock] $ScenarioAction,
        [Parameter(Mandatory)] [scriptblock] $CollectAction,
        [Parameter(Mandatory)] [scriptblock] $StopAction,
        [scriptblock] $ResolveFailedManifestAction,
        [scriptblock] $FailureAction,
        [scriptblock] $CollectionFailureAction
    )

    $manifest = $null
    $startupState = [pscustomobject]@{ SessionCreated = $false }
    $scenarioFailure = $null
    $cleanupFailure = $null
    $collectionFailures = [System.Collections.Generic.List[string]]::new()
    try {
        $manifest = & $StartAction $startupState
        & $ScenarioAction $manifest | Out-Null
    }
    catch {
        $scenarioFailure = $_
        if ($null -eq $manifest -and $startupState.SessionCreated -and $null -ne $ResolveFailedManifestAction) {
            try { $manifest = & $ResolveFailedManifestAction } catch { }
        }
        if ($null -ne $manifest -and $null -ne $FailureAction) {
            try { & $FailureAction $manifest $scenarioFailure | Out-Null } catch { }
        }
    }
    finally {
        if ($null -ne $manifest) {
            try { & $CollectAction $manifest | Out-Null }
            catch {
                $collectionFailures.Add("$($_.Exception.Message)")
                if ($null -ne $CollectionFailureAction) {
                    try { & $CollectionFailureAction $manifest $_ | Out-Null } catch { }
                }
            }
            try {
                $stopResult = & $StopAction $manifest
                if ($null -eq $stopResult -or -not [bool] $stopResult.Complete) {
                    throw 'Managed full-stack stop did not report complete cleanup.'
                }
                if ($null -ne $stopResult.Manifest) { $manifest = $stopResult.Manifest }
            }
            catch { $cleanupFailure = $_ }
        }
    }
    if ($cleanupFailure) { throw $cleanupFailure }
    if ($scenarioFailure) { throw $scenarioFailure }
    return [pscustomobject]@{ Manifest = $manifest; CollectionFailures = @($collectionFailures) }
}

function Get-NervFullStackEnvironment {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    return @{
        NERV_IIP_EPHEMERAL = 'true'
        NERV_IIP_SESSION_ID = $SessionId
        Messaging__Provider = 'Redis'
        Persistence__Provider = 'PostgreSQL'
        ASPNETCORE_ENVIRONMENT = 'Development'
        DOTNET_ENVIRONMENT = 'Development'
        NERV_IIP_POSTGRES_VOLUME = "nerv-iip-postgres-18-$SessionId"
        NERV_IIP_REDIS_VOLUME = "nerv-iip-redis-$SessionId"
        NERV_IIP_MINIO_VOLUME = "nerv-iip-minio-$SessionId"
        NERV_IIP_VICTORIA_LOGS_VOLUME = "nerv-iip-victoria-logs-$SessionId"
    }
}

function Get-NervFullStackNonSeedEnvironment {
    return @{
        'NERV_IIP_FULLSTACK_WMS_WORKER_PASSWORD' = $null
        'Parameters__iam-seed-demo-worker-password' = $null
    }
}

function New-NervFullStackSecretValue {
    param(
        [ValidateRange(16, 256)]
        [int] $Bytes = 32
    )

    $buffer = [Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)
    try {
        return [Convert]::ToBase64String($buffer)
    }
    finally {
        [Array]::Clear($buffer, 0, $buffer.Length)
    }
}

function ConvertTo-NervBase64Url {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-NervFullStackSecretEnvironment {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId,

        [switch] $IncludeDemoWorkerPassword
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        $parameters = $rsa.ExportParameters($false)
        $kid = "$SessionId-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
        $jwks = [ordered]@{
            keys = @([ordered]@{
                kty = 'RSA'
                use = 'sig'
                kid = $kid
                alg = 'RS256'
                n = ConvertTo-NervBase64Url -Bytes $parameters.Modulus
                e = ConvertTo-NervBase64Url -Bytes $parameters.Exponent
            })
        } | ConvertTo-Json -Compress -Depth 5
        $adminPassword = New-NervFullStackSecretValue -Bytes 24
        $workerPassword = if ($IncludeDemoWorkerPassword) {
            New-NervFullStackSecretValue -Bytes 24
        }
        else {
            $null
        }
        $environment = @{
            'Parameters__iam-jwt-signing-key-id' = $kid
            'Parameters__iam-jwt-private-key-pem' = $rsa.ExportPkcs8PrivateKeyPem()
            'Parameters__iam-jwt-jwks-json' = $jwks
            'Parameters__iam-secrets-pepper' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__iam-enterprise-identity-mfa-code' = '654321'
            'Parameters__internal-service-bearer-token' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__template-asset-retirement-proof-issuer' = 'nerv-fullstack-template-asset-retirement'
            'Parameters__template-asset-retirement-proof-audience' = 'nerv-barcodelabel-template-asset-retirement'
            'Parameters__template-asset-retirement-proof-secret-base64' = New-NervFullStackSecretValue -Bytes 32
            'Parameters__redis-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__minio-root-user' = "nerv-$SessionId"
            'Parameters__minio-root-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__iam-seed-admin-password' = $adminPassword
            'Parameters__iam-seed-connector-host-secret' = New-NervFullStackSecretValue -Bytes 32
            'Parameters__connector-ingestion-token-signing-key' = New-NervFullStackSecretValue -Bytes 48
        }
        if ($IncludeDemoWorkerPassword) {
            $environment['Parameters__iam-seed-demo-worker-password'] = $workerPassword
        }

        return [pscustomobject]@{
            Environment = $environment
            AdminPassword = $adminPassword
            WorkerPassword = $workerPassword
        }
    }
    finally {
        $rsa.Dispose()
    }
}

function Get-NervDockerListedValues {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $Name,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name `
        -Environment $Environment

    return @($result.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-NervDockerInspectObjects {
    param(
        [Parameter(Mandatory)] [ValidateSet('container', 'network', 'volume')] [string] $Kind,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Identifiers,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $Name,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    if ($Identifiers.Count -eq 0) {
        return @()
    }

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments (@($Kind, 'inspect') + $Identifiers) `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name `
        -Environment $Environment
    return @($result.Stdout | ConvertFrom-Json -Depth 30)
}

function Get-NervFullStackContainerRecords {
    param(
        [Parameter(Mandatory)] [string] $OwnedSessionId,
        [string] $WorkingDirectory = (Get-Location).Path,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    $ids = @(Get-NervDockerListedValues `
        -Arguments @('container', 'ls', '-a', '--no-trunc', '--filter', "label=com.nerv-iip.session=$OwnedSessionId", '--format', '{{.ID}}') `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery" `
        -Environment $Environment)
    $objects = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $ids `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery-inspect" `
        -Environment $Environment)
    return @($objects | ForEach-Object {
        $containerName = "$($_.Name)".TrimStart('/')
        $resourceName = @('postgres', 'redis', 'minio', 'victoria-logs') |
            Where-Object { $containerName.StartsWith("$_-", [StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($resourceName)) { $resourceName = $containerName }
        [ordered]@{
            resourceName = $resourceName
            id = "$($_.Id)"
            name = $containerName
        }
    })
}

function Merge-NervSessionContainerIds {
    param(
        [object[]] $RecordedIds = @(),
        [object[]] $DiscoveredRecords = @()
    )

    return @(Get-NervStringsSorted -Values @(
        @($RecordedIds | ForEach-Object { "$_" }) +
            @($DiscoveredRecords | ForEach-Object { "$($_.id)" }) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -Comparer ([StringComparer]::Ordinal) -Unique)
}

function Get-NervContainerNetworkIds {
    param([object[]] $Containers = @())

    return @(Get-NervStringsSorted -Values @(
        foreach ($container in $Containers) {
            $networks = $container.NetworkSettings.Networks
            if ($null -eq $networks) { continue }
            foreach ($property in $networks.PSObject.Properties) {
                $networkId = "$($property.Value.NetworkID)"
                if (-not [string]::IsNullOrWhiteSpace($networkId)) { $networkId }
            }
        }
    ) -Comparer ([StringComparer]::Ordinal) -Unique)
}

function Get-NervRecordableDcpNetworkIds {
    param(
        [object[]] $Networks = @(),
        [Parameter(Mandatory)] [string[]] $OwnedContainerIds
    )

    return @(Get-NervStringsSorted -Values @(
        foreach ($network in $Networks) {
            $name = "$($network.Name)"
            if (-not $name.StartsWith('aspire-session-network-', [StringComparison]::Ordinal)) { continue }

            $labels = if ($null -ne $network.PSObject.Properties['Labels']) { $network.Labels } else { $null }
            if ($null -eq $labels) { continue }
            $creator = $labels.PSObject.Properties['com.microsoft.developer.usvc-dev.creatorProcessId']
            $persistent = $labels.PSObject.Properties['com.microsoft.developer.usvc-dev.persistent']
            if ($null -eq $creator -or [string]::IsNullOrWhiteSpace("$($creator.Value)")) { continue }
            if ($null -eq $persistent -or -not [string]::Equals([string]$persistent.Value, 'false', [StringComparison]::Ordinal)) { continue }

            $attachedIds = @(if ($null -ne $network.PSObject.Properties['Containers'] -and $null -ne $network.Containers) {
                $network.Containers.PSObject.Properties.Name
            })
            if ($attachedIds.Count -eq 0) { continue }
            if (@($attachedIds | Where-Object { -not [Collections.Generic.HashSet[string]]::new([string[]]@($OwnedContainerIds), [StringComparer]::Ordinal).Contains([string]$_) }).Count -gt 0) { continue }

            $id = "$($network.Id)"
            if (-not [string]::IsNullOrWhiteSpace($id)) { $id }
        }
    ) -Comparer ([StringComparer]::Ordinal) -Unique)
}

function Get-NervFullStackDcpNetworkIds {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [object[]] $ContainerRecords = @(),
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    $recordedContainerIds = @($ContainerRecords | ForEach-Object { "$($_.id)" })
    if ($recordedContainerIds.Count -eq 0) { return @() }

    $containerInspect = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $recordedContainerIds `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$SessionId-startup-container-inspect" `
        -Environment $Environment)
    $ownedContainers = @($containerInspect | Where-Object {
        Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $SessionId -RecordedIds $recordedContainerIds
    })
    $ownedContainerIds = @($ownedContainers | ForEach-Object { "$($_.Id)" })
    $candidateNetworkIds = @(Get-NervContainerNetworkIds -Containers $ownedContainers)
    if ($candidateNetworkIds.Count -eq 0) { return @() }

    $networkInspect = @(Get-NervDockerInspectObjects `
        -Kind network `
        -Identifiers $candidateNetworkIds `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$SessionId-startup-network-inspect" `
        -Environment $Environment)
    return @(Get-NervRecordableDcpNetworkIds -Networks $networkInspect -OwnedContainerIds $ownedContainerIds)
}

function Get-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = (Get-NervFullStackCleanupWorkingDirectory),
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    $sessionId = "$($Manifest.sessionId)"
    if ($sessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$sessionId'."
    }

    $recordedContainerIds = @($Manifest.runtime.containerIds | ForEach-Object { "$_" })
    $recordedNetworkIds = @($Manifest.runtime.networkIds | ForEach-Object { "$_" })
    $recordedVolumeNames = @($Manifest.runtime.volumeNames | ForEach-Object { "$_" })
    $unresolved = [System.Collections.Generic.List[string]]::new()
    $discoveredNetworkIds = @()

    try {
        $discoveredContainers = @(Get-NervFullStackContainerRecords -OwnedSessionId $sessionId -WorkingDirectory $WorkingDirectory -Environment $Environment)
        $candidateContainerIds = @(Merge-NervSessionContainerIds -RecordedIds $recordedContainerIds -DiscoveredRecords $discoveredContainers)
        $listedContainerIds = Get-NervDockerListedValues -Arguments @('container', 'ls', '-a', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-list" -Environment $Environment
        $presentContainerIds = @($candidateContainerIds | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@($listedContainerIds), [StringComparer]::Ordinal).Contains([string]$_) })
        $containerInspect = if ($presentContainerIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind container -Identifiers $presentContainerIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-inspect" -Environment $Environment)
        }
        else { @() }
        $containers = @($containerInspect | Where-Object {
            Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $candidateContainerIds
        })
        $discoveredNetworkIds = @(Get-NervContainerNetworkIds -Containers $containers)
        $ownedContainerIds = @($containers | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentContainerIds) {
            if (-not [Collections.Generic.HashSet[string]]::new([string[]]@($ownedContainerIds), [StringComparer]::Ordinal).Contains([string]$id)) { $unresolved.Add("container:$id") }
        }
    }
    catch {
        $containers = @()
        foreach ($id in $recordedContainerIds) { $unresolved.Add("container:$id") }
    }

    try {
        $candidateNetworkIds = @(Get-NervStringsSorted -Values @($recordedNetworkIds + $discoveredNetworkIds) -Comparer ([StringComparer]::Ordinal) -Unique)
        $listedNetworkIds = Get-NervDockerListedValues -Arguments @('network', 'ls', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-list" -Environment $Environment
        $presentNetworkIds = @($candidateNetworkIds | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@($listedNetworkIds), [StringComparer]::Ordinal).Contains([string]$_) })
        $networkInspect = if ($presentNetworkIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind network -Identifiers $presentNetworkIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-inspect" -Environment $Environment)
        }
        else { @() }
        $networks = @($networkInspect | Where-Object {
            Test-NervDockerNetworkOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $recordedNetworkIds
        })
        $ownedNetworkIds = @($networks | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentNetworkIds) {
            if (-not [Collections.Generic.HashSet[string]]::new([string[]]@($ownedNetworkIds), [StringComparer]::Ordinal).Contains([string]$id)) { $unresolved.Add("network:$id") }
        }
    }
    catch {
        $networks = @()
        foreach ($id in $recordedNetworkIds) { $unresolved.Add("network:$id") }
    }

    try {
        $listedVolumeNames = Get-NervDockerListedValues -Arguments @('volume', 'ls', '--format', '{{.Name}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-list" -Environment $Environment
        $presentVolumeNames = @($recordedVolumeNames | Where-Object { [Collections.Generic.HashSet[string]]::new([string[]]@($listedVolumeNames), [StringComparer]::Ordinal).Contains([string]$_) })
        $volumeInspect = if ($presentVolumeNames.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind volume -Identifiers $presentVolumeNames -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-inspect" -Environment $Environment)
        }
        else { @() }
        $volumes = @($volumeInspect | Where-Object {
            $name = "$($_.Name)"
            (Test-NervDockerRecordedNameOwnership -Name $name -SessionId $sessionId -RecordedNames $recordedVolumeNames) -and
                (Test-NervDockerOptionalSessionLabel -Labels $_.Labels -SessionId $sessionId)
        })
        $ownedVolumeNames = @($volumes | ForEach-Object { "$($_.Name)" })
        foreach ($name in $presentVolumeNames) {
            if (-not [Collections.Generic.HashSet[string]]::new([string[]]@($ownedVolumeNames), [StringComparer]::Ordinal).Contains([string]$name)) { $unresolved.Add("volume:$name") }
        }
    }
    catch {
        $volumes = @()
        foreach ($name in $recordedVolumeNames) { $unresolved.Add("volume:$name") }
    }

    return [pscustomobject]@{
        Containers = @($containers)
        Networks = @($networks)
        Volumes = @($volumes)
        Unresolved = @(Get-NervStringsSorted -Values @($unresolved) -Comparer ([StringComparer]::Ordinal) -Unique)
    }
}

function Remove-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = (Get-NervFullStackCleanupWorkingDirectory),
        [ValidateRange(1, 600)] [int] $TimeoutSeconds = 120,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    $sessionId = "$($Manifest.sessionId)"
    $resources = Get-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory $WorkingDirectory -Environment $Environment
    $unresolved = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $resources.Unresolved) { $unresolved.Add("$item") }

    $removals = @(
        [pscustomobject]@{ Kind = 'container'; Values = @($resources.Containers | ForEach-Object { "$($_.Id)" }); Arguments = @('container', 'rm', '-f') },
        [pscustomobject]@{ Kind = 'network'; Values = @($resources.Networks | ForEach-Object { "$($_.Id)" }); Arguments = @('network', 'rm') },
        [pscustomobject]@{ Kind = 'volume'; Values = @($resources.Volumes | ForEach-Object { "$($_.Name)" }); Arguments = @('volume', 'rm') }
    )
    foreach ($removal in $removals) {
        if ($removal.Values.Count -eq 0) { continue }
        try {
            Invoke-NativeCommandWithTimeout `
                -Command 'docker' `
                -Arguments ($removal.Arguments + $removal.Values) `
                -WorkingDirectory $WorkingDirectory `
                -TimeoutSeconds $TimeoutSeconds `
                -Name "fullstack-$sessionId-$($removal.Kind)-remove" `
                -Environment $Environment | Out-Null
        }
        catch {
            foreach ($value in $removal.Values) { $unresolved.Add("$($removal.Kind):$value") }
        }
    }

    return [pscustomobject]@{
        Complete = $unresolved.Count -eq 0
        Remaining = @(Get-NervStringsSorted -Values @($unresolved) -Comparer ([StringComparer]::Ordinal) -Unique)
    }
}

function Get-NervFullStackStatusSummary {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = (Get-NervFullStackCleanupWorkingDirectory),
        [scriptblock] $DockerResourcesAction,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    if ($null -eq $DockerResourcesAction) {
        $DockerResourcesAction = {
            param($InputManifest, $InputWorkingDirectory, $InputEnvironment)
            Get-NervSessionDockerResources -Manifest $InputManifest -WorkingDirectory $InputWorkingDirectory -Environment $InputEnvironment
        }
    }

    $resources = & $DockerResourcesAction $Manifest $WorkingDirectory $Environment
    if ($null -eq $resources) {
        throw "Full-stack status Docker ownership inspection returned no result for '$($Manifest.sessionId)'."
    }

    return [pscustomobject]@{
        ContainerCount = @($resources.Containers).Count
        RecordedContainerCount = @($Manifest.runtime.containerIds).Count
        UnresolvedCount = @($resources.Unresolved).Count
    }
}

function Invoke-NervDockerCleanupWithRetry {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [scriptblock] $RemoveAction,
        [ValidateRange(1, 10)] [int] $MaximumAttempts = 5,
        [scriptblock] $DelayAction
    )

    if ($null -eq $DelayAction) {
        $DelayAction = { param($Attempt) Start-Sleep -Seconds 3 }
    }

    $result = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $result = & $RemoveAction $Manifest
        if ($null -eq $result) { throw 'Docker cleanup action returned no result.' }
        $remaining = @($result.Remaining)
        if ([bool] $result.Complete -and $remaining.Count -eq 0) { return $result }
        if ($attempt -lt $MaximumAttempts) { & $DelayAction $attempt }
    }
    return $result
}

function Invoke-NervAspireStartWithRetry {
    param(
        [Parameter(Mandatory)] [scriptblock] $StartAction,
        [Parameter(Mandatory)] [scriptblock] $CleanupAction,
        [ValidateRange(1, 3)] [int] $MaximumAttempts = 2,
        [scriptblock] $DelayAction
    )

    if ($null -eq $DelayAction) {
        $DelayAction = { param($Attempt) Start-Sleep -Seconds 5 }
    }

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try { return (& $StartAction) }
        catch {
            $message = "$($_.Exception.Message)"
            $isTransientBuildResourceFailure = $message -match 'MSB4166|system resource|系统资源不足'
            if (-not $isTransientBuildResourceFailure -or $attempt -ge $MaximumAttempts) { throw }
            & $CleanupAction
            & $DelayAction $attempt
        }
    }
}

function Stop-NervWorktreeProcesses {
    param(
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [Parameter(Mandatory)] [string] $SessionId,
        [int[]] $ExcludedProcessIds = @(),
        [scriptblock] $ProcessQueryAction,
        [scriptblock] $StopAction,
        [scriptblock] $ProcessAliveAction
    )

    if ($null -eq $ProcessQueryAction) {
        $ProcessQueryAction = {
            if ($IsWindows) {
                return @(Get-CimInstance Win32_Process | Select-Object ProcessId, Name, CommandLine)
            }
            if ($IsLinux) {
                $records = [System.Collections.Generic.List[object]]::new()
                foreach ($entry in [System.IO.Directory]::EnumerateDirectories('/proc')) {
                    $processId = 0
                    if (-not [int]::TryParse([System.IO.Path]::GetFileName($entry), [ref] $processId)) { continue }
                    try {
                        $commandLinePath = Join-Path $entry 'cmdline'
                        $commandLineBytes = [System.IO.File]::ReadAllBytes($commandLinePath)
                        if ($commandLineBytes.Length -eq 0) { continue }
                        $commandLine = [System.Text.Encoding]::UTF8.GetString($commandLineBytes).Replace([char] 0, ' ').Trim()
                        if ([string]::IsNullOrWhiteSpace($commandLine)) { continue }
                        $name = [System.IO.File]::ReadAllText((Join-Path $entry 'comm')).Trim()
                        $ownedSessionId = $null
                        try {
                            $environmentBytes = [System.IO.File]::ReadAllBytes((Join-Path $entry 'environ'))
                            foreach ($environmentEntry in [System.Text.Encoding]::UTF8.GetString($environmentBytes).Split([char] 0)) {
                                if ($environmentEntry.StartsWith('NERV_IIP_SESSION_ID=', [StringComparison]::Ordinal)) {
                                    $ownedSessionId = $environmentEntry.Substring('NERV_IIP_SESSION_ID='.Length)
                                    break
                                }
                            }
                        }
                        catch {
                            # Command-line ownership remains available when a kernel policy hides environ.
                        }
                        $records.Add([pscustomobject]@{
                            ProcessId = $processId
                            Name = $name
                            CommandLine = $commandLine
                            SessionId = $ownedSessionId
                        })
                    }
                    catch {
                        # A process may exit while /proc is being inspected; that is already clean.
                    }
                }
                return @($records)
            }

            return @()
        }
    }
    if ($null -eq $StopAction) {
        $StopAction = {
            param($ProcessId, $Reason)
            Stop-ProcessTree -ProcessId $ProcessId -Reason $Reason | Out-Null
        }
    }
    if ($null -eq $ProcessAliveAction) {
        $ProcessAliveAction = {
            param($ProcessId)
            try {
                Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
                return $true
            }
            catch { return $false }
        }
    }

    $normalizedRoot = "$WorktreeRoot".Replace('\', '/')
    $isDriveRooted = $normalizedRoot -match '^[A-Za-z]:/'
    if (-not $isDriveRooted -and -not $normalizedRoot.StartsWith('/', [StringComparison]::Ordinal)) {
        $normalizedRoot = [System.IO.Path]::GetFullPath($WorktreeRoot).Replace('\', '/')
    }
    $normalizedRoot = $normalizedRoot.TrimEnd('/') + '/'
    $pathComparison = if ($IsWindows -or $isDriveRooted) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $excluded = @($ExcludedProcessIds | ForEach-Object { [int] $_ })
    $allowedProcessNames = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('dotnet', 'dotnet.exe', 'node', 'node.exe', 'aspire', 'aspire.exe', 'aspire-managed', 'dcp', 'dcp.exe'),
        [StringComparer]::Ordinal)
    $stopped = [System.Collections.Generic.List[int]]::new()
    foreach ($process in @(& $ProcessQueryAction)) {
        $processId = [int] $process.ProcessId
        if ($processId -le 0 -or $excluded -contains $processId) { continue }
        $sessionProperty = $process.PSObject.Properties['SessionId']
        $isSessionOwned = $null -ne $sessionProperty -and
            [string]::Equals([string]$sessionProperty.Value, $SessionId, [StringComparison]::Ordinal)
        $hasAllowedWorktreeName = $allowedProcessNames.Contains([string]("$($process.Name)".ToLowerInvariant()))
        $commandLine = "$($process.CommandLine)".Replace('\', '/')
        $isWorktreeOwned = $hasAllowedWorktreeName -and
            -not [string]::IsNullOrWhiteSpace($commandLine) -and
            $commandLine.Contains($normalizedRoot, $pathComparison)
        if (-not ($isSessionOwned -or $isWorktreeOwned)) { continue }
        & $StopAction $processId "Exact session process cleanup for $SessionId"
        $stopped.Add($processId)
    }

    $remaining = [System.Collections.Generic.List[int]]::new()
    foreach ($processId in @($stopped)) {
        $alive = $true
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            if (-not (& $ProcessAliveAction $processId)) { $alive = $false; break }
            Start-Sleep -Milliseconds 100
        }
        if ($alive) { $remaining.Add($processId) }
    }

    return [pscustomobject]@{
        Complete = $remaining.Count -eq 0
        StoppedProcessIds = @($stopped)
        RemainingProcessIds = @($remaining)
    }
}

function Invoke-NervFullStackSessionEnvironment {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [scriptblock] $ScriptBlock
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    Use-ScopedEnvironmentVariable `
        -Name 'NERV_IIP_SESSION_ID' `
        -Value $SessionId `
        -ScriptBlock $ScriptBlock
}

function Stop-NervFullStackSession {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [scriptblock] $AspireStopAction,
        [scriptblock] $ProcessStopAction,
        [scriptblock] $DockerRemoveAction,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { $Environment = Get-NervFullStackNonSeedEnvironment }

    if ($null -eq $AspireStopAction) {
        $AspireStopAction = {
            param($Manifest)
            if (-not (Test-NervFullStackAppHostAvailable -Manifest $Manifest)) { return }
            Invoke-AspireOutput `
                -Arguments @('stop', '--apphost', "$($Manifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory (Get-NervFullStackCleanupWorkingDirectory -StateRoot $StateRoot) `
                -TimeoutSeconds 150 `
                -Name "fullstack-$($Manifest.sessionId)-aspire-stop" `
                -AllowPartialOutput `
                -Environment (Get-NervFullStackNonSeedEnvironment) | Out-Null
        }
    }
    if ($null -eq $ProcessStopAction) {
        $ProcessStopAction = {
            param($Manifest)
            $guardianPid = if ($null -ne $Manifest.guardian) { $Manifest.guardian.pid } else { $null }
            $guardianStarted = if ($null -ne $Manifest.guardian) { $Manifest.guardian.processStartTimeUtc } else { $null }
            $identities = @(
                [pscustomobject]@{ Pid = $Manifest.aspire.appHostPid; Started = $Manifest.aspire.appHostProcessStartTimeUtc },
                [pscustomobject]@{ Pid = $Manifest.aspire.cliPid; Started = $Manifest.aspire.cliProcessStartTimeUtc },
                [pscustomobject]@{ Pid = $guardianPid; Started = $guardianStarted }
            )
            $callerGuardianPid = 0
            [void] [int]::TryParse($env:NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID, [ref] $callerGuardianPid)
            foreach ($identity in $identities) {
                if ($null -eq $identity.Pid -or [string]::IsNullOrWhiteSpace("$($identity.Started)")) { continue }
                $processId = [int] $identity.Pid
                if ($processId -eq $PID -or $processId -eq $callerGuardianPid) { continue }
                if (Test-NervProcessIdentity -ProcessId $processId -ProcessStartTimeUtc $identity.Started) {
                    Stop-ProcessTree -ProcessId $processId -Reason "Exact full-stack session stop for $($Manifest.sessionId)" | Out-Null
                }
            }
            $worktreeProcessResult = Stop-NervWorktreeProcesses `
                -WorktreeRoot "$($Manifest.worktreeRoot)" `
                -SessionId "$($Manifest.sessionId)" `
                -ExcludedProcessIds @($PID, $callerGuardianPid)
            if (-not $worktreeProcessResult.Complete) {
                throw "Owned worktree processes remain after cleanup: $($worktreeProcessResult.RemainingProcessIds -join ', ')."
            }
        }
    }
    if ($null -eq $DockerRemoveAction) {
        $DockerRemoveAction = {
            param($Manifest)
            Remove-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory (Get-NervFullStackCleanupWorkingDirectory -StateRoot $StateRoot) -Environment $Environment
        }
    }

    $manifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
    $wasStopped = [string]::Equals([string]$manifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase)
    if (-not $wasStopped) {
        $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
            $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
            if (-not [string]::Equals([string]$lockedManifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -and -not [string]::Equals([string]$lockedManifest.state, 'Stopping', [StringComparison]::OrdinalIgnoreCase)) {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopping
                Write-NervFullStackManifest -Manifest $lockedManifest -StateRoot $StateRoot
            }
            return $lockedManifest
        }
        $wasStopped = [string]::Equals([string]$manifest.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase)
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $cleanupFailures = [System.Collections.Generic.List[string]]::new()
    if (-not $wasStopped) {
        try {
            Invoke-NervFullStackSessionEnvironment `
                -SessionId "$($manifest.sessionId)" `
                -ScriptBlock { & $AspireStopAction $manifest }
        }
        catch {
            $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
            $cleanupFailures.Add('aspire:stop-failed')
        }
        try { & $ProcessStopAction $manifest } catch {
            $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
            $cleanupFailures.Add('process:stop-failed')
        }
    }

    try {
        $dockerResult = Invoke-NervDockerCleanupWithRetry -Manifest $manifest -RemoveAction $DockerRemoveAction
    }
    catch {
        $errors.Add((Protect-ScriptAutomationText -Text "$($_.Exception.Message)"))
        $dockerResult = [pscustomobject]@{ Complete = $false; Remaining = @('docker:inspection-failed') }
    }
    $remaining = @(Get-NervStringsSorted -Values @(
        @($cleanupFailures) + @($dockerResult.Remaining | ForEach-Object { "$_" })) -Comparer ([StringComparer]::Ordinal) -Unique)
    $complete = $errors.Count -eq 0 -and [bool] $dockerResult.Complete -and $remaining.Count -eq 0

    $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
        $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
        $lockedManifest.cleanup.remaining = @($remaining)
        $lockedManifest.cleanup.errors = @($lockedManifest.cleanup.errors) + @($errors)
        if ($complete) {
            $lockedManifest.cleanup.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            if ([string]::Equals([string]$lockedManifest.state, 'Stopping', [StringComparison]::OrdinalIgnoreCase)) {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopped
            }
        }
        elseif ([string]::Equals([string]$lockedManifest.state, 'Stopping', [StringComparison]::OrdinalIgnoreCase)) {
            $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State CleanupFailed
        }
        Write-NervFullStackManifest -Manifest $lockedManifest -StateRoot $StateRoot
        return $lockedManifest
    }

    return [pscustomobject]@{
        Complete = $complete
        Remaining = @($remaining)
        Errors = @($errors)
        Manifest = $manifest
    }
}
