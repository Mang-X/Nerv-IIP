Set-StrictMode -Version Latest

$runtimeLibraryRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $runtimeLibraryRoot 'scripts/lib/FullStackSessionState.ps1')

function Test-NervDockerResourceOwnership {
    param(
        [Parameter(Mandatory)] [object] $InspectObject,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $RecordedIds
    )

    $id = "$($InspectObject.Id)"
    if ($RecordedIds -cnotcontains $id) {
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
    return $null -ne $sessionLabel -and "$($sessionLabel.Value)" -ceq $SessionId
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

    return ($RecordedNames -ccontains $Name) -and
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
    return $null -eq $sessionLabel -or "$($sessionLabel.Value)" -ceq $SessionId
}

function Test-NervDockerNetworkOwnership {
    param(
        [Parameter(Mandatory)] [object] $InspectObject,
        [Parameter(Mandatory)] [string] $SessionId,
        [string[]] $RecordedIds = @()
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) { return $false }
    if (@('bridge', 'host', 'none') -ccontains "$($InspectObject.Name)") { return $false }

    $id = "$($InspectObject.Id)"
    $labels = if ($null -ne $InspectObject.PSObject.Properties['Labels']) { $InspectObject.Labels } else { $null }
    $sessionLabel = if ($null -ne $labels) { $labels.PSObject.Properties['com.nerv-iip.session'] } else { $null }
    if ($null -ne $sessionLabel) { return "$($sessionLabel.Value)" -ceq $SessionId }
    return $RecordedIds -ccontains $id
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

    $matches = @($DescribeObject.resources | Where-Object { "$($_.displayName)" -ceq $ResourceName })
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
    $matches = @($resource.urls | Where-Object { "$($_.name)" -ceq $EndpointName })
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
        -AllowPartialOutput
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
        -AllowPartialOutput | Out-Null
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
        "$($_.resourceType)" -like 'Project*' -and "$($_.state)" -eq 'Finished'
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
    Invoke-WithScopedEnvironment -Variables $browserEnvironment -ScriptBlock {
        Invoke-Pnpm `
            -Arguments @(
                '-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test',
                'e2e/fullstack-proxy.spec.ts', '--project=desktop', '--reporter=json',
                '--output', (Join-Path "$($Manifest.artifactPath)" 'test-results')
            ) `
            -WorkingDirectory "$($Manifest.worktreeRoot)" `
            -TimeoutSeconds 300 `
            -Name "fullstack-$($Manifest.sessionId)-playwright" | Out-Null
    }
    return Assert-NervPlaywrightJsonReport -ReportPath $reportPath
}

function Assert-NervLeaderDemoMainChainEvidence {
    param([Parameter(Mandatory)] [string] $EvidencePath)

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        throw "Leader-demo evidence was not created at '$EvidencePath'."
    }
    $evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json -Depth 100
    $requiredNodes = @(
        'sales-order-demand-source',
        'demand-source-mrp-suggestion',
        'mrp-suggestion-mes-work-order',
        'mes-work-order-schedule-plan',
        'schedule-release-mes-execution',
        'mes-task-production-report',
        'production-report-quality',
        'report-finished-goods-receipt',
        'finished-goods-receipt-inventory-posting',
        'inventory-produced-lot-fulfillment-lookup',
        'sales-order-delivery-order',
        'delivery-order-wms-outbound',
        'wms-completed-erp-delivery-status',
        'wms-completed-account-receivable',
        'account-receivable-voucher'
    )
    if ("$($evidence.runtimeProfileSource)" -cne 'session-manifest') { throw 'Leader-demo evidence runtime profile must come from the managed session manifest.' }
    if ("$($evidence.transport)" -cne 'redis-cross-process') { throw 'Leader-demo evidence must declare redis-cross-process transport.' }
    if ("$($evidence.persistence)" -cne 'postgresql') { throw 'Leader-demo evidence must declare PostgreSQL persistence.' }
    if ([string]::IsNullOrWhiteSpace("$($evidence.salesOrderNo)") -or "$($evidence.salesOrderNo)" -notlike 'SO-MAN524-*') {
        throw 'Leader-demo evidence must identify one run-scoped SO-MAN524-* sales order.'
    }
    $entries = @($evidence.entries)
    if ($entries.Count -ne $requiredNodes.Count) {
        throw "Leader-demo evidence must contain exactly $($requiredNodes.Count) entries; found $($entries.Count)."
    }
    foreach ($node in $requiredNodes) {
        $nodeEntries = @($entries | Where-Object { "$($_.node)" -ceq $node })
        if ($nodeEntries.Count -ne 1) { throw "Leader-demo evidence must contain exactly one '$node' entry; found $($nodeEntries.Count)." }
    }
    foreach ($entry in $entries) {
        if (@('runtime-confirmed', 'gap', 'not-verified') -cnotcontains "$($entry.conclusion)") {
            throw "Leader-demo evidence node '$($entry.node)' has invalid conclusion '$($entry.conclusion)'."
        }
        if ([string]::IsNullOrWhiteSpace("$($entry.stableKey)") -or [string]::IsNullOrWhiteSpace("$($entry.demoWording)")) {
            throw "Leader-demo evidence node '$($entry.node)' is missing its stable key or demo wording."
        }
    }
    $notVerifiedEntries = @($entries | Where-Object { "$($_.conclusion)" -ceq 'not-verified' })
    if ($notVerifiedEntries.Count -gt 0) {
        throw "Leader-demo evidence cannot pass with not-verified nodes: $($notVerifiedEntries.node -join ', ')."
    }
    $unexpectedGaps = @($entries | Where-Object {
        "$($_.conclusion)" -ceq 'gap' -and -not (
            "$($_.node)" -ceq 'inventory-produced-lot-fulfillment-lookup' -and
            "$($_.responsibilityIssue)" -match '(?<!\d)#972(?!\d)'
        )
    })
    if ($unexpectedGaps.Count -gt 0) {
        throw "Leader-demo evidence contains gaps outside the accepted #972 baseline: $($unexpectedGaps.node -join ', ')."
    }
    if (@($entries | Where-Object { "$($_.conclusion)" -ceq 'runtime-confirmed' }).Count -eq 0) {
        throw 'Leader-demo evidence must contain at least one runtime-confirmed node.'
    }
    $raw = Get-Content -LiteralPath $EvidencePath -Raw
    foreach ($forbiddenPattern in @('(?i)authorization', '(?i)bearer\s+', '(?i)password', '(?i)access[_-]?token', '(?i)refresh[_-]?token')) {
        if ($raw -match $forbiddenPattern) { throw "Leader-demo evidence contains forbidden secret-shaped text matching '$forbiddenPattern'." }
    }
    return $evidence
}

function Invoke-NervLeaderDemoMainChainBrowserCheck {
    param(
        [Parameter(Mandatory)] [hashtable] $Environment,
        [Parameter(Mandatory)] [object] $Manifest
    )

    [void] [System.IO.Directory]::CreateDirectory("$($Manifest.artifactPath)")
    $reportPath = Join-Path "$($Manifest.artifactPath)" 'playwright-leader-demo-main-chain.json'
    $evidencePath = Join-Path "$($Manifest.artifactPath)" 'leader-demo-main-chain-evidence.json'
    Remove-Item -LiteralPath $reportPath, $evidencePath -Force -ErrorAction SilentlyContinue
    $browserEnvironment = @{}
    foreach ($entry in $Environment.GetEnumerator()) { $browserEnvironment[$entry.Key] = "$($entry.Value)" }
    $browserEnvironment.PLAYWRIGHT_JSON_OUTPUT_FILE = $reportPath
    $browserEnvironment.NERV_IIP_MAIN_CHAIN_EVIDENCE_PATH = $evidencePath
    Invoke-WithScopedEnvironment -Variables $browserEnvironment -ScriptBlock {
        Invoke-Pnpm `
            -Arguments @(
                '-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test',
                'e2e/leader-demo-main-chain.spec.ts', '--project=desktop', '--reporter=json',
                '--output', (Join-Path "$($Manifest.artifactPath)" 'test-results')
            ) `
            -WorkingDirectory "$($Manifest.worktreeRoot)" `
            -TimeoutSeconds 1200 `
            -Name "fullstack-$($Manifest.sessionId)-leader-demo-main-chain" | Out-Null
    }
    Assert-NervPlaywrightJsonReport -ReportPath $reportPath | Out-Null
    return Assert-NervLeaderDemoMainChainEvidence -EvidencePath $evidencePath
}

function Invoke-NervLeaderDemoMainChainScenario {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $SessionAdminPassword,
        [scriptblock] $WaitAction,
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
    if ($null -eq $AspireSnapshotAction) {
        $AspireSnapshotAction = {
            param($InputManifest)
            Get-NervAspireDescribeObject -AppHostProject "$($InputManifest.appHostProject)" -WorkingDirectory "$($InputManifest.worktreeRoot)"
        }
    }
    if ($null -eq $BrowserAction) {
        $BrowserAction = {
            param($Environment, $InputManifest)
            Invoke-NervLeaderDemoMainChainBrowserCheck -Environment $Environment -Manifest $InputManifest | Out-Null
        }
    }

    if ("$($Manifest.runtime.messagingProvider)" -cne 'Redis') {
        throw "Leader-demo main-chain requires a Redis session profile; manifest recorded '$($Manifest.runtime.messagingProvider)'."
    }
    if ("$($Manifest.runtime.persistenceProvider)" -cne 'PostgreSQL') {
        throw "Leader-demo main-chain requires a PostgreSQL session profile; manifest recorded '$($Manifest.runtime.persistenceProvider)'."
    }

    $resourceNames = @(
        'postgres', 'redis', 'iam', 'gateway', 'business-gateway', 'business-console',
        'business-master-data', 'business-product-engineering', 'business-inventory',
        'business-quality', 'business-mes', 'business-demand-planning', 'business-wms',
        'business-erp', 'business-scheduling'
    )
    foreach ($resourceName in $resourceNames) { & $WaitAction $resourceName $Manifest | Out-Null }

    $snapshot = & $AspireSnapshotAction $Manifest
    $finishedProjects = @($snapshot.resources | Where-Object {
        "$($_.resourceType)" -like 'Project*' -and "$($_.state)" -eq 'Finished' -and
        $resourceNames -ccontains "$($_.displayName)"
    })
    if ($finishedProjects.Count -gt 0) {
        throw "Aspire project resources finished unexpectedly: $($finishedProjects.displayName -join ', ')."
    }

    $childEnvironment = @{
        NERV_IIP_PLAYWRIGHT_BASE_URL = Get-NervFullStackEndpointValue -Manifest $Manifest -ResourceName 'business-console'
        NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $SessionAdminPassword
        NERV_IIP_MAIN_CHAIN_RUNTIME_PROFILE_SOURCE = 'session-manifest'
        NERV_IIP_MAIN_CHAIN_TRANSPORT = 'redis-cross-process'
        NERV_IIP_MAIN_CHAIN_PERSISTENCE = 'postgresql'
    }
    & $BrowserAction $childEnvironment $Manifest | Out-Null
    return [pscustomobject]@{ ExitCode = 0; ChildEnvironment = $childEnvironment; CheckedResources = $resourceNames }
}

function Invoke-NervFullStackGuardian {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [ValidateSet('Automated', 'Interactive')] [string] $Mode,
        [int] $CoordinatorPid,
        [string] $CoordinatorStartTimeUtc,
        [ValidateRange(1, 3600)] [int] $IntervalSeconds = 60,
        [ValidateRange(1, 10)] [int] $MaximumObservationFailures = 3,
        [ValidateRange(1, 10)] [int] $MaximumStopAttempts = 3,
        [scriptblock] $ReadAction,
        [scriptblock] $CoordinatorAliveAction,
        [scriptblock] $StopAction,
        [scriptblock] $DelayAction
    )

    if ($null -eq $ReadAction) { $ReadAction = { Read-NervFullStackManifest -SessionId $SessionId } }
    if ($null -eq $CoordinatorAliveAction) {
        $CoordinatorAliveAction = { Test-NervProcessIdentity -ProcessId $CoordinatorPid -ProcessStartTimeUtc $CoordinatorStartTimeUtc }
    }
    if ($null -eq $StopAction) {
        $StopAction = {
            Invoke-WithScopedEnvironment -Variables @{ NERV_IIP_FULLSTACK_CALLER_GUARDIAN_PID = "$PID" } -ScriptBlock {
                Invoke-PwshScript `
                    -ScriptPath (Join-Path $runtimeLibraryRoot 'scripts/fullstack-session.ps1') `
                    -Arguments @('stop', '-SessionId', $SessionId) `
                    -WorkingDirectory $runtimeLibraryRoot `
                    -TimeoutSeconds 300 `
                    -Name "fullstack-$SessionId-guardian-stop" | Out-Null
            }
        }
    }
    if ($null -eq $DelayAction) { $DelayAction = { param($Seconds) Start-Sleep -Seconds $Seconds } }

    $observationFailures = 0
    while ($true) {
        try {
            $manifest = & $ReadAction
            $observationFailures = 0
        }
        catch {
            $observationFailures++
            Write-Warning "Guardian manifest observation $observationFailures/$MaximumObservationFailures failed for '$SessionId': $($_.Exception.Message)"
            if ($observationFailures -ge $MaximumObservationFailures) { throw }
            & $DelayAction $IntervalSeconds
            continue
        }

        if ("$($manifest.state)" -eq 'Stopped') { return [pscustomobject]@{ State = 'Stopped' } }
        $leaseExpired = [DateTimeOffset] $manifest.leaseExpiresAtUtc -le [DateTimeOffset]::UtcNow
        $coordinatorMissing = $Mode -eq 'Automated' -and
            ($CoordinatorPid -le 0 -or [string]::IsNullOrWhiteSpace($CoordinatorStartTimeUtc) -or -not (& $CoordinatorAliveAction))
        if (-not ($leaseExpired -or $coordinatorMissing)) {
            & $DelayAction $IntervalSeconds
            continue
        }

        $lastStopError = 'cleanup did not reach Stopped'
        for ($attempt = 1; $attempt -le $MaximumStopAttempts; $attempt++) {
            try {
                & $StopAction
                $afterStop = & $ReadAction
                if ("$($afterStop.state)" -eq 'Stopped') { return [pscustomobject]@{ State = 'Stopped' } }
                $lastStopError = "cleanup returned state '$($afterStop.state)'"
            }
            catch { $lastStopError = $_.Exception.Message }
            Write-Warning "Guardian stop attempt $attempt/$MaximumStopAttempts failed for '$SessionId': $lastStopError"
            if ($attempt -lt $MaximumStopAttempts) { & $DelayAction $IntervalSeconds }
        }
        throw "Guardian could not stop session '$SessionId' after $MaximumStopAttempts attempts: $lastStopError"
    }
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
            param($ResourceName, $InputManifest, $BoundedTimeoutSeconds)
            $result = Invoke-AspireOutput `
                -Arguments @('logs', $ResourceName, '--tail', '500', '--format', 'Json', '--apphost', "$($InputManifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory "$($InputManifest.worktreeRoot)" `
                -TimeoutSeconds $BoundedTimeoutSeconds `
                -Name "fullstack-$($InputManifest.sessionId)-collect-$ResourceName"
            return "$($result.Stdout)"
        }
    }

    $artifactPath = [System.IO.Path]::GetFullPath("$($Manifest.artifactPath)")
    $logDirectory = Join-Path $artifactPath 'aspire-logs'
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $collectionErrors = [System.Collections.Generic.List[string]]::new()
    $resourceNames = @(
        'gateway', 'business-gateway', 'console', 'business-console', 'screen',
        'business-master-data', 'business-product-engineering', 'business-inventory',
        'business-quality', 'business-mes', 'business-demand-planning', 'business-wms',
        'business-erp', 'business-scheduling',
        'postgres', 'redis', 'minio'
    )
    foreach ($resourceName in $resourceNames) {
        try {
            $raw = (& $LogAction $resourceName $Manifest $TimeoutSeconds) -join "`n"
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
    $scenarioFailure = $null
    $cleanupFailure = $null
    $collectionFailures = [System.Collections.Generic.List[string]]::new()
    try {
        $manifest = & $StartAction
        & $ScenarioAction $manifest | Out-Null
    }
    catch {
        $scenarioFailure = $_
        if ($null -eq $manifest -and $null -ne $ResolveFailedManifestAction) {
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
        [string] $SessionId
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
        $environment = @{
            'Parameters__iam-jwt-signing-key-id' = $kid
            'Parameters__iam-jwt-private-key-pem' = $rsa.ExportPkcs8PrivateKeyPem()
            'Parameters__iam-jwt-jwks-json' = $jwks
            'Parameters__iam-secrets-pepper' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__internal-service-bearer-token' = New-NervFullStackSecretValue -Bytes 48
            'Parameters__redis-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__minio-root-user' = "nerv-$SessionId"
            'Parameters__minio-root-password' = New-NervFullStackSecretValue -Bytes 24
            'Parameters__iam-seed-admin-password' = $adminPassword
            'Parameters__iam-seed-connector-host-secret' = New-NervFullStackSecretValue -Bytes 32
            'Parameters__connector-ingestion-token-signing-key' = New-NervFullStackSecretValue -Bytes 48
        }

        return [pscustomobject]@{
            Environment = $environment
            AdminPassword = $adminPassword
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
        [Parameter(Mandatory)] [string] $Name
    )

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name

    return @($result.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-NervDockerInspectObjects {
    param(
        [Parameter(Mandatory)] [ValidateSet('container', 'network', 'volume')] [string] $Kind,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Identifiers,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Identifiers.Count -eq 0) {
        return @()
    }

    $result = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments (@($Kind, 'inspect') + $Identifiers) `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds 30 `
        -Name $Name
    return @($result.Stdout | ConvertFrom-Json -Depth 30)
}

function Get-NervFullStackContainerRecords {
    param(
        [Parameter(Mandatory)] [string] $OwnedSessionId,
        [string] $WorkingDirectory = (Get-Location).Path
    )

    $ids = @(Get-NervDockerListedValues `
        -Arguments @('container', 'ls', '-a', '--no-trunc', '--filter', "label=com.nerv-iip.session=$OwnedSessionId", '--format', '{{.ID}}') `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery")
    $objects = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $ids `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$OwnedSessionId-container-discovery-inspect")
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

    return @(
        @($RecordedIds | ForEach-Object { "$_" }) +
            @($DiscoveredRecords | ForEach-Object { "$($_.id)" }) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
}

function Get-NervContainerNetworkIds {
    param([object[]] $Containers = @())

    return @(
        foreach ($container in $Containers) {
            $networks = $container.NetworkSettings.Networks
            if ($null -eq $networks) { continue }
            foreach ($property in $networks.PSObject.Properties) {
                $networkId = "$($property.Value.NetworkID)"
                if (-not [string]::IsNullOrWhiteSpace($networkId)) { $networkId }
            }
        }
    ) | Select-Object -Unique
}

function Get-NervRecordableDcpNetworkIds {
    param(
        [object[]] $Networks = @(),
        [Parameter(Mandatory)] [string[]] $OwnedContainerIds
    )

    return @(
        foreach ($network in $Networks) {
            $name = "$($network.Name)"
            if (-not $name.StartsWith('aspire-session-network-', [StringComparison]::Ordinal)) { continue }

            $labels = if ($null -ne $network.PSObject.Properties['Labels']) { $network.Labels } else { $null }
            if ($null -eq $labels) { continue }
            $creator = $labels.PSObject.Properties['com.microsoft.developer.usvc-dev.creatorProcessId']
            $persistent = $labels.PSObject.Properties['com.microsoft.developer.usvc-dev.persistent']
            if ($null -eq $creator -or [string]::IsNullOrWhiteSpace("$($creator.Value)")) { continue }
            if ($null -eq $persistent -or "$($persistent.Value)" -cne 'false') { continue }

            $attachedIds = @(if ($null -ne $network.PSObject.Properties['Containers'] -and $null -ne $network.Containers) {
                $network.Containers.PSObject.Properties.Name
            })
            if ($attachedIds.Count -eq 0) { continue }
            if (@($attachedIds | Where-Object { $OwnedContainerIds -cnotcontains $_ }).Count -gt 0) { continue }

            $id = "$($network.Id)"
            if (-not [string]::IsNullOrWhiteSpace($id)) { $id }
        }
    ) | Select-Object -Unique
}

function Get-NervFullStackDcpNetworkIds {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [object[]] $ContainerRecords = @(),
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $recordedContainerIds = @($ContainerRecords | ForEach-Object { "$($_.id)" })
    if ($recordedContainerIds.Count -eq 0) { return @() }

    $containerInspect = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $recordedContainerIds `
        -WorkingDirectory $WorkingDirectory `
        -Name "fullstack-$SessionId-startup-container-inspect")
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
        -Name "fullstack-$SessionId-startup-network-inspect")
    return @(Get-NervRecordableDcpNetworkIds -Networks $networkInspect -OwnedContainerIds $ownedContainerIds)
}

function Get-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = (Get-NervFullStackCleanupWorkingDirectory)
    )

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
        $discoveredContainers = @(Get-NervFullStackContainerRecords -OwnedSessionId $sessionId -WorkingDirectory $WorkingDirectory)
        $candidateContainerIds = @(Merge-NervSessionContainerIds -RecordedIds $recordedContainerIds -DiscoveredRecords $discoveredContainers)
        $listedContainerIds = Get-NervDockerListedValues -Arguments @('container', 'ls', '-a', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-list"
        $presentContainerIds = @($candidateContainerIds | Where-Object { $listedContainerIds -ccontains $_ })
        $containerInspect = if ($presentContainerIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind container -Identifiers $presentContainerIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-container-inspect")
        }
        else { @() }
        $containers = @($containerInspect | Where-Object {
            Test-NervDockerResourceOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $candidateContainerIds
        })
        $discoveredNetworkIds = @(Get-NervContainerNetworkIds -Containers $containers)
        $ownedContainerIds = @($containers | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentContainerIds) {
            if ($ownedContainerIds -cnotcontains $id) { $unresolved.Add("container:$id") }
        }
    }
    catch {
        $containers = @()
        foreach ($id in $recordedContainerIds) { $unresolved.Add("container:$id") }
    }

    try {
        $candidateNetworkIds = @($recordedNetworkIds + $discoveredNetworkIds | Select-Object -Unique)
        $listedNetworkIds = Get-NervDockerListedValues -Arguments @('network', 'ls', '--no-trunc', '--format', '{{.ID}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-list"
        $presentNetworkIds = @($candidateNetworkIds | Where-Object { $listedNetworkIds -ccontains $_ })
        $networkInspect = if ($presentNetworkIds.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind network -Identifiers $presentNetworkIds -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-network-inspect")
        }
        else { @() }
        $networks = @($networkInspect | Where-Object {
            Test-NervDockerNetworkOwnership -InspectObject $_ -SessionId $sessionId -RecordedIds $recordedNetworkIds
        })
        $ownedNetworkIds = @($networks | ForEach-Object { "$($_.Id)" })
        foreach ($id in $presentNetworkIds) {
            if ($ownedNetworkIds -cnotcontains $id) { $unresolved.Add("network:$id") }
        }
    }
    catch {
        $networks = @()
        foreach ($id in $recordedNetworkIds) { $unresolved.Add("network:$id") }
    }

    try {
        $listedVolumeNames = Get-NervDockerListedValues -Arguments @('volume', 'ls', '--format', '{{.Name}}') -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-list"
        $presentVolumeNames = @($recordedVolumeNames | Where-Object { $listedVolumeNames -ccontains $_ })
        $volumeInspect = if ($presentVolumeNames.Count -gt 0) {
            @(Get-NervDockerInspectObjects -Kind volume -Identifiers $presentVolumeNames -WorkingDirectory $WorkingDirectory -Name "fullstack-$sessionId-volume-inspect")
        }
        else { @() }
        $volumes = @($volumeInspect | Where-Object {
            $name = "$($_.Name)"
            (Test-NervDockerRecordedNameOwnership -Name $name -SessionId $sessionId -RecordedNames $recordedVolumeNames) -and
                (Test-NervDockerOptionalSessionLabel -Labels $_.Labels -SessionId $sessionId)
        })
        $ownedVolumeNames = @($volumes | ForEach-Object { "$($_.Name)" })
        foreach ($name in $presentVolumeNames) {
            if ($ownedVolumeNames -cnotcontains $name) { $unresolved.Add("volume:$name") }
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
        Unresolved = @($unresolved | Select-Object -Unique)
    }
}

function Remove-NervSessionDockerResources {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [string] $WorkingDirectory = (Get-NervFullStackCleanupWorkingDirectory),
        [ValidateRange(1, 600)] [int] $TimeoutSeconds = 120
    )

    $sessionId = "$($Manifest.sessionId)"
    $resources = Get-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory $WorkingDirectory
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
                -Name "fullstack-$sessionId-$($removal.Kind)-remove" | Out-Null
        }
        catch {
            foreach ($value in $removal.Values) { $unresolved.Add("$($removal.Kind):$value") }
        }
    }

    return [pscustomobject]@{
        Complete = $unresolved.Count -eq 0
        Remaining = @($unresolved | Select-Object -Unique)
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
        [int[]] $ExcludedProcessIds = @(),
        [scriptblock] $ProcessQueryAction,
        [scriptblock] $StopAction
    )

    if ($null -eq $ProcessQueryAction) {
        $ProcessQueryAction = {
            if (-not $IsWindows) { return @() }
            return @(Get-CimInstance Win32_Process | Select-Object ProcessId, Name, CommandLine)
        }
    }
    if ($null -eq $StopAction) {
        $StopAction = {
            param($ProcessId, $Reason)
            Stop-ProcessTree -ProcessId $ProcessId -Reason $Reason | Out-Null
        }
    }

    $normalizedRoot = [System.IO.Path]::GetFullPath($WorktreeRoot).Replace('/', '\').TrimEnd('\') + '\'
    $excluded = @($ExcludedProcessIds | ForEach-Object { [int] $_ })
    $allowedProcessNames = @('dotnet', 'dotnet.exe', 'node', 'node.exe', 'aspire', 'aspire.exe', 'dcp', 'dcp.exe')
    $stopped = [System.Collections.Generic.List[int]]::new()
    foreach ($process in @(& $ProcessQueryAction)) {
        $processId = [int] $process.ProcessId
        if ($processId -le 0 -or $excluded -contains $processId) { continue }
        if ($allowedProcessNames -cnotcontains "$($process.Name)".ToLowerInvariant()) { continue }
        $commandLine = "$($process.CommandLine)".Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($commandLine) -or -not $commandLine.Contains($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
        & $StopAction $processId "Exact worktree process cleanup for $normalizedRoot"
        $stopped.Add($processId)
    }

    return [pscustomobject]@{ StoppedProcessIds = @($stopped) }
}

function Stop-NervFullStackSession {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [scriptblock] $AspireStopAction,
        [scriptblock] $ProcessStopAction,
        [scriptblock] $DockerRemoveAction
    )

    if ($null -eq $AspireStopAction) {
        $AspireStopAction = {
            param($Manifest)
            if (-not (Test-NervFullStackAppHostAvailable -Manifest $Manifest)) { return }
            Invoke-AspireOutput `
                -Arguments @('stop', '--apphost', "$($Manifest.appHostProject)", '--non-interactive', '--nologo') `
                -WorkingDirectory (Get-NervFullStackCleanupWorkingDirectory -StateRoot $StateRoot) `
                -TimeoutSeconds 150 `
                -Name "fullstack-$($Manifest.sessionId)-aspire-stop" `
                -AllowPartialOutput | Out-Null
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
            Stop-NervWorktreeProcesses `
                -WorktreeRoot "$($Manifest.worktreeRoot)" `
                -ExcludedProcessIds @($PID, $callerGuardianPid) | Out-Null
        }
    }
    if ($null -eq $DockerRemoveAction) {
        $DockerRemoveAction = {
            param($Manifest)
            Remove-NervSessionDockerResources -Manifest $Manifest -WorkingDirectory (Get-NervFullStackCleanupWorkingDirectory -StateRoot $StateRoot)
        }
    }

    $manifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
    $wasStopped = "$($manifest.state)" -eq 'Stopped'
    if (-not $wasStopped) {
        $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
            $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
            if ("$($lockedManifest.state)" -ne 'Stopped' -and "$($lockedManifest.state)" -ne 'Stopping') {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopping
                Write-NervFullStackManifest -Manifest $lockedManifest -StateRoot $StateRoot
            }
            return $lockedManifest
        }
        $wasStopped = "$($manifest.state)" -eq 'Stopped'
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $cleanupFailures = [System.Collections.Generic.List[string]]::new()
    if (-not $wasStopped) {
        try { & $AspireStopAction $manifest } catch {
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
    $remaining = @(
        @($cleanupFailures) + @($dockerResult.Remaining | ForEach-Object { "$_" }) |
            Select-Object -Unique
    )
    $complete = $errors.Count -eq 0 -and [bool] $dockerResult.Complete -and $remaining.Count -eq 0

    $manifest = Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
        $lockedManifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
        $lockedManifest.cleanup.remaining = @($remaining)
        $lockedManifest.cleanup.errors = @($lockedManifest.cleanup.errors) + @($errors)
        if ($complete) {
            $lockedManifest.cleanup.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            if ("$($lockedManifest.state)" -eq 'Stopping') {
                $lockedManifest = Move-NervFullStackSessionState -Manifest $lockedManifest -State Stopped
            }
        }
        elseif ("$($lockedManifest.state)" -eq 'Stopping') {
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

function Get-NervLeaderDemoAdminPassword {
    $password = [Environment]::GetEnvironmentVariable('NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD', 'Process')
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD must be set in the current process.'
    }
    return $password
}

function Get-NervObjectPropertyValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-NervLeaderDemoResponseData {
    param([AllowNull()] [object] $Response)

    $data = Get-NervObjectPropertyValue -InputObject $Response -Name 'data'
    if ($null -ne $data) { return $data }
    return $Response
}

function Get-NervLeaderDemoFailureExitCode {
    param(
        [AllowNull()] [object] $Failure,
        [ValidateRange(1, 2147483647)] [int] $Default = 1
    )

    $candidate = if ($null -ne $Failure -and $null -ne $Failure.PSObject.Properties['Exception']) {
        $Failure.Exception
    }
    else { $Failure }
    for ($depth = 0; $depth -lt 5 -and $null -ne $candidate; $depth++) {
        foreach ($propertyName in @('ExitCode', 'NativeExitCode')) {
            $value = Get-NervObjectPropertyValue -InputObject $candidate -Name $propertyName
            $parsed = 0
            if ($null -ne $value -and [int]::TryParse("$value", [ref] $parsed) -and $parsed -gt 0) {
                return $parsed
            }
            if ($null -ne $candidate.Data -and $candidate.Data.Contains($propertyName)) {
                $parsed = 0
                if ([int]::TryParse("$($candidate.Data[$propertyName])", [ref] $parsed) -and $parsed -gt 0) {
                    return $parsed
                }
            }
        }
        $candidate = Get-NervObjectPropertyValue -InputObject $candidate -Name 'InnerException'
    }
    return $Default
}

function Resolve-NervLeaderDemoFailureExitCode {
    param(
        [ValidateRange(0, 2147483647)] [int] $CurrentExitCode,
        [AllowNull()] [object] $Failure
    )

    if ($CurrentExitCode -gt 0) { return $CurrentExitCode }
    return (Get-NervLeaderDemoFailureExitCode -Failure $Failure)
}

function Get-NervLeaderDemoRequiredResources {
    return @(
        [ordered]@{ name = 'iam'; label = 'IAM' },
        [ordered]@{ name = 'business-gateway'; label = 'BusinessGateway' },
        [ordered]@{ name = 'business-erp'; label = 'ERP' },
        [ordered]@{ name = 'business-demand-planning'; label = 'DemandPlanning' },
        [ordered]@{ name = 'business-product-engineering'; label = 'ProductEngineering' },
        [ordered]@{ name = 'business-scheduling'; label = 'Scheduling' },
        [ordered]@{ name = 'business-mes'; label = 'MES' },
        [ordered]@{ name = 'business-quality'; label = 'Quality' },
        [ordered]@{ name = 'business-wms'; label = 'WMS' },
        [ordered]@{ name = 'business-inventory'; label = 'Inventory' },
        [ordered]@{ name = 'business-industrial-telemetry'; label = 'IndustrialTelemetry' },
        [ordered]@{ name = 'business-maintenance'; label = 'Maintenance' },
        [ordered]@{ name = 'postgres'; label = 'PostgreSQL' },
        [ordered]@{ name = 'redis'; label = 'Redis' },
        [ordered]@{ name = 'console'; label = 'console' },
        [ordered]@{ name = 'business-console'; label = 'business-console' },
        [ordered]@{ name = 'screen'; label = 'screen' }
    )
}

function Write-NervLeaderDemoEvidence {
    param(
        [Parameter(Mandatory)] [object] $Evidence,
        [Parameter(Mandatory)] [string] $EvidenceRoot,
        [Parameter(Mandatory)] [DateTimeOffset] $UtcNow,
        [string[]] $SensitiveValues = @(),
        [scriptblock] $WriteTempAction,
        [scriptblock] $PromoteAction
    )

    $randomSuffix = [Convert]::ToHexString(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(3)
    ).ToLowerInvariant()
    $runId = "$($UtcNow.UtcDateTime.ToString('yyyyMMddTHHmmssfffZ'))-$randomSuffix"
    $artifactDirectory = Join-Path ([System.IO.Path]::GetFullPath($EvidenceRoot)) $runId
    [void] [System.IO.Directory]::CreateDirectory($artifactDirectory)
    $evidencePath = Join-Path $artifactDirectory 'evidence.json'
    $tempPath = Join-Path $artifactDirectory ".evidence.$([Guid]::NewGuid().ToString('N')).tmp"
    $Evidence.runId = $runId
    $Evidence.diagnostics.evidencePath = $evidencePath
    $json = $Evidence | ConvertTo-Json -Depth 50
    $safeJson = $json
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $safeJson = $safeJson.Replace($sensitiveValue, '<redacted>')
        }
    }

    if ($null -eq $WriteTempAction) {
        $WriteTempAction = {
            param($Path, $Content)
            $encoding = [System.Text.UTF8Encoding]::new($false)
            $bytes = $encoding.GetBytes($Content)
            $stream = [System.IO.FileStream]::new(
                $Path,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None
            )
            try {
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush($true)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    if ($null -eq $PromoteAction) {
        $PromoteAction = {
            param($SourcePath, $DestinationPath)
            [System.IO.File]::Move($SourcePath, $DestinationPath)
        }
    }

    $published = $false
    try {
        & $WriteTempAction $tempPath $safeJson
        & $PromoteAction $tempPath $evidencePath
        $published = $true
    }
    finally {
        if (-not $published -and [System.IO.File]::Exists($tempPath)) {
            try {
                [System.IO.File]::Delete($tempPath)
            }
            catch {
                Write-Diagnostic -Level 'WARN' -Message "Failed to clean temporary leader-demo evidence file '$tempPath': $($_.Exception.Message)"
            }
        }
    }
    return $evidencePath
}

function Invoke-NervHttpSuccessCheck {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Url,
        [scriptblock] $RequestAction
    )

    if ($null -eq $RequestAction) {
        $RequestAction = {
            param($RequestUrl)
            Invoke-WebRequest -Uri $RequestUrl -Method Get -TimeoutSec 30 -UseBasicParsing
        }
    }

    try {
        $response = & $RequestAction $Url
    }
    catch {
        $errorResponse = Get-NervObjectPropertyValue -InputObject $_.Exception -Name 'Response'
        $errorStatusValue = Get-NervObjectPropertyValue -InputObject $errorResponse -Name 'StatusCode'
        $errorStatus = if ($null -ne $errorStatusValue) { [int] $errorStatusValue } else { 0 }
        if ($errorStatus -gt 0) {
            throw "HTTP check failed for '$Name' at '$Url' with status ${errorStatus}: $($_.Exception.Message)"
        }
        throw "HTTP check failed for '$Name' at '$Url': $($_.Exception.Message)"
    }

    $statusValue = Get-NervObjectPropertyValue -InputObject $response -Name 'StatusCode'
    if ($null -eq $statusValue) {
        throw "HTTP check failed for '$Name' at '$Url': the response did not expose a status code."
    }
    $statusCode = [int] $statusValue
    if ($statusCode -lt 200 -or $statusCode -ge 300) {
        throw "HTTP check failed for '$Name' at '$Url' with status $statusCode."
    }
}

function Invoke-NervLeaderDemoVerification {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [ValidateSet('seed', 'health-check')] [string] $Command,
        [Parameter(Mandatory)] [string] $SessionAdminPassword,
        [string] $EvidenceRoot,
        [DateTimeOffset] $UtcNow = [DateTimeOffset]::UtcNow,
        [ValidateRange(1, 300)] [int] $ResourceTimeoutSeconds = 60,
        [scriptblock] $CommitAction,
        [scriptblock] $WaitAction,
        [scriptblock] $AspireSnapshotAction,
        [scriptblock] $HttpCheckAction,
        [scriptblock] $LoginAction,
        [scriptblock] $PrincipalAction,
        [scriptblock] $PublicFactQueryAction
    )

    $sessionId = "$(Get-NervObjectPropertyValue -InputObject $Manifest -Name 'sessionId')"
    $worktreeRoot = "$(Get-NervObjectPropertyValue -InputObject $Manifest -Name 'worktreeRoot')"
    if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
        $EvidenceRoot = Join-Path $worktreeRoot 'artifacts/leader-demo'
    }
    if ($null -eq $CommitAction) {
        $CommitAction = {
            $result = Invoke-NativeCommandOutput `
                -Command 'git' `
                -Arguments @('rev-parse', 'HEAD') `
                -WorkingDirectory $worktreeRoot `
                -TimeoutSeconds 30 `
                -Name "leader-demo-$sessionId-commit"
            return "$($result.Stdout)".Trim()
        }
    }
    if ($null -eq $WaitAction) {
        $WaitAction = {
            param($Name, $InputManifest, $TimeoutSeconds)
            Wait-NervAspireResource `
                -AppHostProject "$(Get-NervObjectPropertyValue -InputObject $InputManifest -Name 'appHostProject')" `
                -ResourceName $Name `
                -WorkingDirectory "$(Get-NervObjectPropertyValue -InputObject $InputManifest -Name 'worktreeRoot')" `
                -TimeoutSeconds $TimeoutSeconds
        }
    }
    if ($null -eq $AspireSnapshotAction) {
        $AspireSnapshotAction = {
            param($InputManifest)
            Get-NervAspireDescribeObject `
                -AppHostProject "$(Get-NervObjectPropertyValue -InputObject $InputManifest -Name 'appHostProject')" `
                -WorkingDirectory "$(Get-NervObjectPropertyValue -InputObject $InputManifest -Name 'worktreeRoot')"
        }
    }
    if ($null -eq $HttpCheckAction) {
        $HttpCheckAction = {
            param($Name, $Url)
            Invoke-NervHttpSuccessCheck -Name $Name -Url $Url
        }
    }
    if ($null -eq $LoginAction) {
        $LoginAction = {
            param($GatewayUrl, $Password)
            Invoke-RestMethod `
                -Method Post `
                -Uri "$($GatewayUrl.TrimEnd('/'))/api/console/v1/auth/login" `
                -Body (@{ loginName = 'admin'; password = $Password } | ConvertTo-Json -Compress) `
                -ContentType 'application/json' `
                -TimeoutSec 30
        }
    }
    if ($null -eq $PublicFactQueryAction) {
        $PublicFactQueryAction = {
            param($FactName, $Url, $Headers)
            Invoke-RestMethod -Method Get -Uri $Url -Headers $Headers -TimeoutSec 30
        }
    }
    if ($null -eq $PrincipalAction) {
        $PrincipalAction = {
            param($GatewayUrl, $Headers)
            Invoke-RestMethod `
                -Method Get `
                -Uri "$($GatewayUrl.TrimEnd('/'))/api/console/v1/auth/me" `
                -Headers $Headers `
                -TimeoutSec 30
        }
    }

    $requiredResources = @(Get-NervLeaderDemoRequiredResources)
    $resourceEvidence = [System.Collections.Generic.List[object]]::new()
    $factEvidence = [System.Collections.Generic.List[object]]::new()
    $failures = [System.Collections.Generic.List[object]]::new()
    $sensitiveValues = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrEmpty($SessionAdminPassword)) { $sensitiveValues.Add($SessionAdminPassword) }
    $waitFailures = @{}
    $elapsedByResource = @{}
    $snapshot = $null
    $accessToken = $null
    $propagatedExitCode = 0
    $observedPrincipal = [ordered]@{
        principalId = $null
        principalType = $null
        loginName = $null
        organizationId = $null
        environmentId = $null
        permissionCodes = @()
    }

    $endpoints = Get-NervObjectPropertyValue -InputObject $Manifest -Name 'endpoints'
    $accessUrls = [ordered]@{
        gateway = Get-NervObjectPropertyValue -InputObject $endpoints -Name 'gateway'
        businessGateway = Get-NervObjectPropertyValue -InputObject $endpoints -Name 'business-gateway'
        console = Get-NervObjectPropertyValue -InputObject $endpoints -Name 'console'
        businessConsole = Get-NervObjectPropertyValue -InputObject $endpoints -Name 'business-console'
        screen = Get-NervObjectPropertyValue -InputObject $endpoints -Name 'screen'
    }
    $fullStackArtifactPath = "$(Get-NervObjectPropertyValue -InputObject $Manifest -Name 'artifactPath')"
    $messagingProvider = "$(Get-NervObjectPropertyValue -InputObject $Manifest -Name 'messagingProvider')"
    $state = "$(Get-NervObjectPropertyValue -InputObject $Manifest -Name 'state')"
    $publicEndpointByResource = @{
        'business-gateway' = $accessUrls.businessGateway
        'console' = $accessUrls.console
        'business-console' = $accessUrls.businessConsole
        'screen' = $accessUrls.screen
    }

    if ($state -cne 'Running') {
        $failures.Add([ordered]@{ name = 'session'; message = "Session '$sessionId' is '$state', expected Running."; hint = 'Run .\nerv.ps1 demo reset and retry.' })
    }
    if ($messagingProvider -cne 'Redis') {
        $failures.Add([ordered]@{ name = 'messaging'; message = "Leader-demo session '$sessionId' must use Redis messaging; manifest reports '$messagingProvider'."; hint = 'Run .\nerv.ps1 demo reset to recreate the isolated Redis profile.' })
    }
    foreach ($urlName in @('gateway', 'businessGateway', 'console', 'businessConsole', 'screen')) {
        if ([string]::IsNullOrWhiteSpace("$($accessUrls[$urlName])")) {
            $failures.Add([ordered]@{ name = $urlName; message = "Manifest '$sessionId' has no '$urlName' access URL."; hint = 'Inspect the full-stack startup diagnostics and run .\nerv.ps1 demo reset.' })
        }
    }

    if ($failures.Count -eq 0) {
        foreach ($resource in $requiredResources) {
            $resourceName = "$($resource.name)"
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                & $WaitAction $resourceName $Manifest $ResourceTimeoutSeconds | Out-Null
            }
            catch {
                $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
                $waitFailures[$resourceName] = Protect-NervFullStackDiagnosticText `
                    -Text "$($_.Exception.Message)" `
                    -SensitiveValues @($sensitiveValues)
            }
            finally {
                $stopwatch.Stop()
                $elapsedByResource[$resourceName] = $stopwatch.ElapsedMilliseconds
            }
        }

        try {
            $snapshot = & $AspireSnapshotAction $Manifest
        }
        catch {
            $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
            $safeMessage = Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues @($sensitiveValues)
            $failures.Add([ordered]@{ name = 'aspire-describe'; message = $safeMessage; hint = 'Inspect the full-stack diagnostics and verify the exact AppHost session is still running.' })
        }

        $snapshotResources = if ($null -ne $snapshot) {
            @(Get-NervObjectPropertyValue -InputObject $snapshot -Name 'resources')
        }
        else { @() }
        foreach ($resource in $requiredResources) {
            $resourceName = "$($resource.name)"
            $matches = @($snapshotResources | Where-Object { "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'displayName')" -ceq $resourceName })
            $snapshotState = if ($matches.Count -eq 1) { "$(Get-NervObjectPropertyValue -InputObject $matches[0] -Name 'state')" } elseif ($matches.Count -eq 0) { 'Missing' } else { 'Ambiguous' }
            $waitFailure = if ($waitFailures.ContainsKey($resourceName)) { "$($waitFailures[$resourceName])" } else { $null }
            $stateFailed = $snapshotState -in @('Missing', 'Ambiguous', 'Failed', 'Finished', 'Exited')
            $hint = $null
            if (-not [string]::IsNullOrWhiteSpace($waitFailure)) {
                $hint = "Aspire wait failed: $waitFailure Inspect '$resourceName' logs under '$fullStackArtifactPath' and retry within the bounded gate."
            }
            elseif ($stateFailed) {
                $hint = "Aspire describe reported '$snapshotState'. Inspect '$resourceName' logs under '$fullStackArtifactPath' and run .\nerv.ps1 demo reset if startup cannot recover."
            }

            $publicEndpoint = if ($publicEndpointByResource.ContainsKey($resourceName)) { $publicEndpointByResource[$resourceName] } else { $null }
            $resourceEvidence.Add([ordered]@{
                name = $resourceName
                label = "$($resource.label)"
                state = $snapshotState
                elapsedMilliseconds = [long] $elapsedByResource[$resourceName]
                endpoint = $publicEndpoint
                hint = $hint
            })
            if (-not [string]::IsNullOrWhiteSpace($waitFailure) -or $stateFailed) {
                $message = if (-not [string]::IsNullOrWhiteSpace($waitFailure)) { $waitFailure } else { "Aspire resource state is '$snapshotState'." }
                $failures.Add([ordered]@{ name = $resourceName; message = $message; hint = $hint })
            }
        }

        foreach ($httpTarget in @(
            [ordered]@{ name = 'business-gateway'; url = "$($accessUrls.businessGateway)".TrimEnd('/') + '/health' },
            [ordered]@{ name = 'console'; url = $accessUrls.console },
            [ordered]@{ name = 'business-console'; url = $accessUrls.businessConsole },
            [ordered]@{ name = 'screen'; url = $accessUrls.screen }
        )) {
            try {
                & $HttpCheckAction "$($httpTarget.name)" "$($httpTarget.url)" | Out-Null
            }
            catch {
                $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
                $safeMessage = Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues @($sensitiveValues)
                $hint = "Check '$($httpTarget.name)' startup logs under '$fullStackArtifactPath' and retry the health gate."
                $failures.Add([ordered]@{ name = "$($httpTarget.name)"; message = $safeMessage; hint = $hint })
                $resourceRow = @($resourceEvidence | Where-Object { $_.name -ceq "$($httpTarget.name)" }) | Select-Object -First 1
                if ($null -ne $resourceRow) { $resourceRow.hint = $hint }
            }
        }
    }

    if ($resourceEvidence.Count -eq 0) {
        $preconditionNames = @($failures | ForEach-Object { "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'name')" } | Select-Object -Unique)
        $preconditionSummary = if ($preconditionNames.Count -gt 0) { $preconditionNames -join ', ' } else { 'unavailable precondition' }
        foreach ($resource in $requiredResources) {
            $resourceName = "$($resource.name)"
            $resourceEvidence.Add([ordered]@{
                name = $resourceName
                label = "$($resource.label)"
                state = 'Skipped'
                elapsedMilliseconds = 0
                endpoint = if ($publicEndpointByResource.ContainsKey($resourceName)) { $publicEndpointByResource[$resourceName] } else { $null }
                hint = "Health query skipped because $preconditionSummary failed. Inspect '$fullStackArtifactPath' and run .\nerv.ps1 demo reset before retrying."
            })
        }
    }

    $organizationId = 'org-001'
    $environmentId = 'env-dev'
    $businessGatewayUrl = "$($accessUrls.businessGateway)".TrimEnd('/')
    $encodedOrganization = [Uri]::EscapeDataString($organizationId)
    $encodedEnvironment = [Uri]::EscapeDataString($environmentId)
    $factDefinitions = @(
        [ordered]@{
            key = 'SO-DEMO-001'
            link = "$businessGatewayUrl/api/business-console/v1/erp/sales/sales-orders?organizationId=$encodedOrganization&environmentId=$encodedEnvironment&keyword=SO-DEMO-001&skip=0&take=10"
            expectedStatus = 'released'
        },
        [ordered]@{
            key = 'WO-DEMO-Q01'
            link = "$businessGatewayUrl/api/business-console/v1/mes/work-orders/WO-DEMO-Q01?organizationId=$encodedOrganization&environmentId=$encodedEnvironment"
            expectedStatus = 'released'
        },
        [ordered]@{
            key = 'DEV-CNC-DEMO'
            link = "$businessGatewayUrl/api/business-console/v1/master-data/device-assets?organizationId=$encodedOrganization&environmentId=$encodedEnvironment&keyword=DEV-CNC-DEMO&skip=0&take=10"
            expectedStatus = 'active'
        },
        [ordered]@{
            key = 'MWO-DEMO-001'
            link = "$businessGatewayUrl/api/business-console/v1/telemetry/alarm-rules?organizationId=$encodedOrganization&environmentId=$encodedEnvironment&deviceAssetId=DEV-CNC-DEMO&isEnabled=true&skip=0&take=100"
            expectedStatus = 'enabled'
        }
    )

    if ($failures.Count -eq 0) {
        try {
            $loginResponse = & $LoginAction "$($accessUrls.gateway)" $SessionAdminPassword
            $loginData = Get-NervLeaderDemoResponseData -Response $loginResponse
            $accessToken = "$(Get-NervObjectPropertyValue -InputObject $loginData -Name 'accessToken')"
            if ([string]::IsNullOrWhiteSpace($accessToken)) { throw 'Platform Gateway login returned no access token.' }
            $sensitiveValues.Add($accessToken)
            $headers = @{ Authorization = "Bearer $accessToken" }

            $principalResponse = & $PrincipalAction "$($accessUrls.gateway)" $headers
            $principalData = Get-NervLeaderDemoResponseData -Response $principalResponse
            $principalId = "$(Get-NervObjectPropertyValue -InputObject $principalData -Name 'principalId')"
            if ([string]::IsNullOrWhiteSpace($principalId)) { throw 'Platform Gateway auth/me returned no principal ID.' }
            $observedPrincipal = [ordered]@{
                principalId = $principalId
                principalType = "$(Get-NervObjectPropertyValue -InputObject $principalData -Name 'principalType')"
                loginName = "$(Get-NervObjectPropertyValue -InputObject $principalData -Name 'loginName')"
                organizationId = "$(Get-NervObjectPropertyValue -InputObject $principalData -Name 'organizationId')"
                environmentId = "$(Get-NervObjectPropertyValue -InputObject $principalData -Name 'environmentId')"
                permissionCodes = @(Get-NervObjectPropertyValue -InputObject $principalData -Name 'permissionCodes')
            }
            foreach ($fact in $factDefinitions) {
                $found = $false
                $observedStatus = 'not-found'
                $hint = $null
                try {
                    $response = & $PublicFactQueryAction "$($fact.key)" "$($fact.link)" $headers
                    $data = Get-NervLeaderDemoResponseData -Response $response
                    switch ("$($fact.key)") {
                        'SO-DEMO-001' {
                            $items = @(Get-NervObjectPropertyValue -InputObject $data -Name 'items')
                            $match = @($items | Where-Object { "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'salesOrderNo')" -ceq 'SO-DEMO-001' }) | Select-Object -First 1
                            $found = $null -ne $match
                            if ($found) { $observedStatus = "$(Get-NervObjectPropertyValue -InputObject $match -Name 'status')" }
                        }
                        'WO-DEMO-Q01' {
                            $found = "$(Get-NervObjectPropertyValue -InputObject $data -Name 'workOrderId')" -ceq 'WO-DEMO-Q01'
                            if ($found) { $observedStatus = "$(Get-NervObjectPropertyValue -InputObject $data -Name 'status')" }
                        }
                        'DEV-CNC-DEMO' {
                            $items = @(Get-NervObjectPropertyValue -InputObject $data -Name 'resources')
                            $match = @($items | Where-Object {
                                "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'resourceType')" -ceq 'device-asset' -and
                                "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'code')" -ceq 'DEV-CNC-DEMO'
                            }) | Select-Object -First 1
                            $found = $null -ne $match
                            if ($found) {
                                $observedStatus = if ([bool] (Get-NervObjectPropertyValue -InputObject $match -Name 'active')) { 'active' } else { 'disabled' }
                            }
                        }
                        'MWO-DEMO-001' {
                            $items = @(Get-NervObjectPropertyValue -InputObject $data -Name 'items')
                            $match = @($items | Where-Object {
                                "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'deviceAssetId')" -ceq 'DEV-CNC-DEMO' -and
                                "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'ruleCode')".StartsWith('MWO-DEMO-001', [StringComparison]::Ordinal)
                            }) | Select-Object -First 1
                            $found = $null -ne $match
                            if ($found) {
                                $observedStatus = if ([bool] (Get-NervObjectPropertyValue -InputObject $match -Name 'isEnabled')) { 'enabled' } else { 'disabled' }
                            }
                        }
                    }
                    if (-not $found) {
                        $hint = "Reserved public fact '$($fact.key)' was not found. Inspect service seed/startup diagnostics under '$fullStackArtifactPath', run .\nerv.ps1 demo reset, then retry the bounded $Command command."
                    }
                    elseif (-not [string]::Equals($observedStatus, "$($fact.expectedStatus)", [StringComparison]::OrdinalIgnoreCase)) {
                        $hint = "Reserved public fact '$($fact.key)' has status '$observedStatus', expected '$($fact.expectedStatus)'. Inspect '$fullStackArtifactPath', run .\nerv.ps1 demo reset, and retry; do not overwrite tenant facts."
                    }
                }
                catch {
                    $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
                    $observedStatus = 'query-failed'
                    $safeQueryMessage = Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues @($sensitiveValues)
                    $hint = "Public query failed: $safeQueryMessage Inspect diagnostics under '$fullStackArtifactPath', run .\nerv.ps1 demo reset, then retry the bounded $Command command."
                }

                $factEvidence.Add([ordered]@{
                    key = "$($fact.key)"
                    found = $found
                    status = $observedStatus
                    link = "$($fact.link)"
                    hint = $hint
                })
                if (-not $found -or -not [string]::Equals($observedStatus, "$($fact.expectedStatus)", [StringComparison]::OrdinalIgnoreCase)) {
                    $failures.Add([ordered]@{ name = "$($fact.key)"; message = "Public fact verification returned '$observedStatus'."; hint = $hint })
                }
            }
        }
        catch {
            $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
            $safeMessage = Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues @($sensitiveValues)
            $failures.Add([ordered]@{ name = 'authentication'; message = $safeMessage; hint = "Check IAM and PlatformGateway logs under '$fullStackArtifactPath'; then rerun the bounded command." })
        }
    }

    foreach ($fact in $factDefinitions) {
        if (@($factEvidence | Where-Object { $_.key -ceq "$($fact.key)" }).Count -eq 0) {
            $factEvidence.Add([ordered]@{
                key = "$($fact.key)"
                found = $false
                status = 'not-verified'
                link = "$($fact.link)"
                hint = "Prerequisite health or authentication failed. Inspect '$fullStackArtifactPath', run .\nerv.ps1 demo reset, then retry the bounded $Command command."
            })
        }
    }

    $commitSha = 'unknown'
    try {
        $commitSha = "$(& $CommitAction)".Trim()
        if ([string]::IsNullOrWhiteSpace($commitSha)) { throw 'Commit action returned an empty SHA.' }
    }
    catch {
        $propagatedExitCode = Resolve-NervLeaderDemoFailureExitCode -CurrentExitCode $propagatedExitCode -Failure $_
        $safeMessage = Protect-NervFullStackDiagnosticText -Text "$($_.Exception.Message)" -SensitiveValues @($sensitiveValues)
        $failures.Add([ordered]@{ name = 'commit'; message = $safeMessage; hint = 'Verify the worktree Git metadata is readable.' })
    }

    $safeFailures = @($failures | ForEach-Object {
        [ordered]@{
            name = "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'name')"
            message = Protect-NervFullStackDiagnosticText -Text "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'message')" -SensitiveValues @($sensitiveValues)
            hint = Protect-NervFullStackDiagnosticText -Text "$(Get-NervObjectPropertyValue -InputObject $_ -Name 'hint')" -SensitiveValues @($sensitiveValues)
        }
    })
    $evidence = [pscustomobject][ordered]@{
        schemaVersion = 1
        runId = $null
        recordedAtUtc = $UtcNow.ToUniversalTime().ToString('O')
        commitSha = $commitSha
        sessionId = $sessionId
        worktreeRoot = $worktreeRoot
        command = $Command
        result = if ($failures.Count -eq 0) { 'passed' } else { 'failed' }
        exitCode = if ($failures.Count -eq 0) { 0 } elseif ($propagatedExitCode -gt 0) { $propagatedExitCode } else { 1 }
        messagingProvider = $messagingProvider
        scope = [ordered]@{ organizationId = $organizationId; environmentId = $environmentId }
        access = [ordered]@{
            urls = $accessUrls
            roles = @()
            rolesObserved = $false
            rolesObservation = 'not-exposed-by-public-auth-contract'
            principal = $observedPrincipal
        }
        resources = @($resourceEvidence)
        facts = @($factEvidence)
        failures = $safeFailures
        diagnostics = [ordered]@{
            fullStackArtifactPath = $fullStackArtifactPath
            evidencePath = $null
        }
        cleanup = [ordered]@{ command = '.\nerv.ps1 demo stop'; sessionId = $sessionId }
    }
    $evidencePath = Write-NervLeaderDemoEvidence `
        -Evidence $evidence `
        -EvidenceRoot $EvidenceRoot `
        -UtcNow $UtcNow `
        -SensitiveValues @($sensitiveValues)

    if ($failures.Count -gt 0) {
        $failureSummary = @($safeFailures | ForEach-Object { "$($_.name): $($_.message)" }) -join '; '
        $verificationFailure = [InvalidOperationException]::new("Leader-demo $Command failed: $failureSummary; evidence=$evidencePath")
        $verificationFailure.Data['ExitCode'] = [int] $evidence.exitCode
        $verificationFailure.Data['EvidencePath'] = $evidencePath
        throw $verificationFailure
    }

    return [pscustomobject]@{
        ExitCode = 0
        EvidencePath = $evidencePath
        Evidence = $evidence
    }
}

function Invoke-NervLeaderDemoCredentialScope {
    param(
        [Parameter(Mandatory)] [string] $AdminPassword,
        [Parameter(Mandatory)] [string] $StateRoot,
        [Parameter(Mandatory)] [scriptblock] $ScriptBlock
    )

    Invoke-WithScopedEnvironment `
        -Variables @{
            NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $AdminPassword
            NERV_IIP_LEADER_DEMO = 'true'
            NERV_IIP_FULLSTACK_STATE_ROOT = [System.IO.Path]::GetFullPath($StateRoot)
        } `
        -ScriptBlock $ScriptBlock
}

function Resolve-NervLeaderDemoOwnedSession {
    param(
        [Parameter(Mandatory)] [string] $StateRoot,
        [Parameter(Mandatory)] [string] $ExpectedWorktreeRoot
    )

    $pointer = Read-NervLeaderDemoSessionPointer -StateRoot $StateRoot
    $pointerSessionId = "$($pointer.sessionId)"
    $manifest = Read-NervFullStackManifest -SessionId $pointerSessionId -StateRoot $StateRoot
    $manifestSessionId = "$($manifest.sessionId)"
    if ($manifestSessionId -cne $pointerSessionId) {
        throw "Leader-demo pointer session '$pointerSessionId' does not match authoritative manifest session '$manifestSessionId'."
    }
    if ([string]::IsNullOrWhiteSpace("$($manifest.worktreeRoot)")) {
        throw "Authoritative full-stack manifest '$pointerSessionId' has no worktree root."
    }

    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $actualWorktreeRoot = [System.IO.Path]::GetFullPath("$($manifest.worktreeRoot)")
    $expectedRoot = [System.IO.Path]::GetFullPath($ExpectedWorktreeRoot)
    if (-not [string]::Equals($actualWorktreeRoot, $expectedRoot, $comparison)) {
        throw "Leader-demo session '$pointerSessionId' authoritative manifest belongs to worktree '$actualWorktreeRoot', not '$expectedRoot'."
    }

    return [pscustomobject]@{
        SessionId = $pointerSessionId
        Pointer = $pointer
        Manifest = $manifest
    }
}

function Invoke-NervLeaderDemoCommand {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('start', 'reset', 'seed', 'health-check', 'stop')]
        [string] $Action,

        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [string] $WorktreeRoot = $runtimeLibraryRoot,
        [switch] $NoBuild,
        [scriptblock] $StartSessionAction,
        [scriptblock] $StopSessionAction,
        [scriptblock] $SeedAction,
        [scriptblock] $HealthCheckAction,
        [scriptblock] $WritePointerAction,
        [scriptblock] $OwnerProcessIdentityAction,
        [DateTimeOffset] $UtcNow = [DateTimeOffset]::UtcNow,
        [ValidateRange(30, 3600)] [int] $ReservationTtlSeconds = 300,
        [ValidateRange(1, 300)] [int] $LifecycleLockTimeoutSeconds = 30
    )

    $resolvedStateRoot = [System.IO.Path]::GetFullPath($StateRoot)
    $resolvedWorktreeRoot = [System.IO.Path]::GetFullPath($WorktreeRoot)
    $fullStackScript = Join-Path $resolvedWorktreeRoot 'scripts/fullstack-session.ps1'

    if ($null -eq $StartSessionAction) {
        $StartSessionAction = {
            param($ExactSessionId)
            $arguments = @('start', '-SessionId', $ExactSessionId)
            if ($NoBuild) { $arguments += '-NoBuild' }
            Invoke-PwshScript `
                -ScriptPath $fullStackScript `
                -Arguments $arguments `
                -WorkingDirectory $resolvedWorktreeRoot `
                -TimeoutSeconds 900 `
                -Name "leader-demo-$ExactSessionId-start" | Out-Null
        }
    }
    if ($null -eq $StopSessionAction) {
        $StopSessionAction = {
            param($ExactSessionId)
            Invoke-PwshScript `
                -ScriptPath $fullStackScript `
                -Arguments @('stop', '-SessionId', $ExactSessionId) `
                -WorkingDirectory $resolvedWorktreeRoot `
                -TimeoutSeconds 300 `
                -Name "leader-demo-$ExactSessionId-stop" | Out-Null
        }
    }
    if ($null -eq $SeedAction) {
        $SeedAction = {
            param($ExactSessionId)
            $manifest = Read-NervFullStackManifest -SessionId $ExactSessionId -StateRoot $resolvedStateRoot
            Invoke-NervLeaderDemoVerification `
                -Manifest $manifest `
                -Command 'seed' `
                -SessionAdminPassword (Get-NervLeaderDemoAdminPassword)
        }
    }
    if ($null -eq $HealthCheckAction) {
        $HealthCheckAction = {
            param($ExactSessionId)
            $manifest = Read-NervFullStackManifest -SessionId $ExactSessionId -StateRoot $resolvedStateRoot
            Invoke-NervLeaderDemoVerification `
                -Manifest $manifest `
                -Command 'health-check' `
                -SessionAdminPassword (Get-NervLeaderDemoAdminPassword)
        }
    }
    if ($null -eq $WritePointerAction) {
        $WritePointerAction = {
            param($ExactSessionId, $ExactWorktreeRoot, $ExactStateRoot, $OwnershipState, $OwnerPid, $OwnerProcessStartTimeUtc, $CreatedAtUtc)
            Write-NervLeaderDemoSessionPointer `
                -SessionId $ExactSessionId `
                -WorktreeRoot $ExactWorktreeRoot `
                -OwnershipState $OwnershipState `
                -OwnerPid $OwnerPid `
                -OwnerProcessStartTimeUtc $OwnerProcessStartTimeUtc `
                -CreatedAtUtc $CreatedAtUtc `
                -StateRoot $ExactStateRoot | Out-Null
        }
    }
    if ($null -eq $OwnerProcessIdentityAction) {
        $OwnerProcessIdentityAction = {
            param($OwnerPid, $OwnerProcessStartTimeUtc)
            Get-NervProcessIdentityStatus -ProcessId $OwnerPid -ProcessStartTimeUtc $OwnerProcessStartTimeUtc
        }
    }

    $testReservationReclaimable = {
        param($Pointer)

        if ("$($Pointer.ownershipState)" -cne 'Reserved') { return $false }
        $reservedSessionId = "$($Pointer.sessionId)"
        $manifestPath = Get-NervFullStackManifestPath -SessionId $reservedSessionId -StateRoot $resolvedStateRoot
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) { return $false }

        $identityStatus = if (
            $null -eq $Pointer.ownerPid -and
            [string]::IsNullOrWhiteSpace("$($Pointer.ownerProcessStartTimeUtc)")
        ) {
            'Unknown'
        }
        else {
            "$(& $OwnerProcessIdentityAction ([int] $Pointer.ownerPid) "$($Pointer.ownerProcessStartTimeUtc)")"
        }
        if (@('Active', 'Absent', 'Mismatched', 'Unknown') -cnotcontains $identityStatus) {
            throw "Leader-demo reservation '$reservedSessionId' owner identity returned invalid status '$identityStatus'."
        }
        if ($identityStatus -eq 'Active') { return $false }
        if ($identityStatus -in @('Absent', 'Mismatched')) { return $true }

        $createdAt = [DateTimeOffset]::Parse("$($Pointer.createdAtUtc)")
        return ($UtcNow - $createdAt).TotalSeconds -ge $ReservationTtlSeconds
    }

    $resolveLifecycleTarget = {
        $pointer = Read-NervLeaderDemoSessionPointer `
            -StateRoot $resolvedStateRoot `
            -ExpectedWorktreeRoot $resolvedWorktreeRoot
        $pointerSessionId = "$($pointer.sessionId)"
        if ("$($pointer.ownershipState)" -eq 'Reserved') {
            $manifestPath = Get-NervFullStackManifestPath -SessionId $pointerSessionId -StateRoot $resolvedStateRoot
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                if (& $testReservationReclaimable $pointer) {
                    Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId $pointerSessionId
                    return [pscustomobject]@{ Reclaimed = $true; SessionId = $pointerSessionId; OwnedSession = $null }
                }
                throw "Leader-demo session '$pointerSessionId' has an active or non-stale Reserved ownership and no authoritative manifest."
            }
        }
        $owned = Resolve-NervLeaderDemoOwnedSession -StateRoot $resolvedStateRoot -ExpectedWorktreeRoot $resolvedWorktreeRoot
        return [pscustomobject]@{ Reclaimed = $false; SessionId = "$($owned.SessionId)"; OwnedSession = $owned }
    }

    $compensateExactSession = {
        param($ExactSessionId)

        $cleanupDiagnostics = [System.Collections.Generic.List[string]]::new()
        $cleanupComplete = $false
        try {
            & $StopSessionAction $ExactSessionId | Out-Null
            $cleanupComplete = $true
            $cleanupDiagnostics.Add('exact-session cleanup completed')
        }
        catch {
            $cleanupDiagnostics.Add("exact-session cleanup failed: $($_.Exception.Message)")
        }
        if ($cleanupComplete) {
            try {
                Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId $ExactSessionId
                $cleanupDiagnostics.Add('Reserved ownership removed')
            }
            catch {
                $cleanupDiagnostics.Add("Reserved ownership removal failed: $($_.Exception.Message)")
            }
        }
        return @($cleanupDiagnostics)
    }

    $startSession = {
        $password = Get-NervLeaderDemoAdminPassword
        $pointerPath = Get-NervLeaderDemoSessionPointerPath -StateRoot $resolvedStateRoot
        if (Test-Path -LiteralPath $pointerPath -PathType Leaf) {
            $current = Read-NervLeaderDemoSessionPointer `
                -StateRoot $resolvedStateRoot `
                -ExpectedWorktreeRoot $resolvedWorktreeRoot
            if (& $testReservationReclaimable $current) {
                Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId "$($current.sessionId)"
            }
            else {
                throw "Leader-demo session '$($current.sessionId)' is already recorded with ownership '$($current.ownershipState)'; use demo reset or demo stop."
            }
        }

        do {
            $newSessionId = New-NervFullStackSessionId -WorktreeRoot $resolvedWorktreeRoot
        } while (-not (Test-NervFullStackSessionIdAvailable -SessionId $newSessionId -StateRoot $resolvedStateRoot))

        $ownerProcess = Get-Process -Id $PID -ErrorAction Stop
        $ownerProcessStartTimeUtc = $ownerProcess.StartTime.ToUniversalTime().ToString('O')
        & $WritePointerAction `
            $newSessionId `
            $resolvedWorktreeRoot `
            $resolvedStateRoot `
            'Reserved' `
            $PID `
            $ownerProcessStartTimeUtc `
            $UtcNow.ToString('O') | Out-Null
        try {
            Invoke-NervLeaderDemoCredentialScope -AdminPassword $password -StateRoot $resolvedStateRoot -ScriptBlock {
                & $StartSessionAction $newSessionId | Out-Null
            }
        }
        catch {
            $startupFailure = $_
            $startupError = "$($_.Exception.Message)"
            $manifestPath = Get-NervFullStackManifestPath -SessionId $newSessionId -StateRoot $resolvedStateRoot
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                try {
                    Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId $newSessionId
                }
                catch {
                    throw "Leader-demo session startup failed for '$newSessionId': $startupError; Reserved ownership removal failed: $($_.Exception.Message)."
                }
                throw $startupFailure
            }

            try {
                Resolve-NervLeaderDemoOwnedSession `
                    -StateRoot $resolvedStateRoot `
                    -ExpectedWorktreeRoot $resolvedWorktreeRoot | Out-Null
            }
            catch {
                throw "Leader-demo session startup failed for '$newSessionId': $startupError; exact-session cleanup skipped because manifest authority validation failed: $($_.Exception.Message)."
            }

            $cleanupDiagnostics = @(& $compensateExactSession $newSessionId)
            throw "Leader-demo session startup failed for '$newSessionId': $startupError; $($cleanupDiagnostics -join '; ')."
        }

        try {
            & $WritePointerAction $newSessionId $resolvedWorktreeRoot $resolvedStateRoot 'Current' $null $null $UtcNow.ToString('O') | Out-Null
        }
        catch {
            $finalizationError = "$($_.Exception.Message)"
            $cleanupDiagnostics = @(& $compensateExactSession $newSessionId)
            throw "Leader-demo pointer finalization failed for '$newSessionId': $finalizationError; $($cleanupDiagnostics -join '; ')."
        }
        return $newSessionId
    }

    return Invoke-WithNervLeaderDemoLifecycleLock `
        -StateRoot $resolvedStateRoot `
        -TimeoutSeconds $LifecycleLockTimeoutSeconds `
        -ScriptBlock {
            switch ($Action) {
                'start' {
                    return (& $startSession)
                }
                'reset' {
                    $password = Get-NervLeaderDemoAdminPassword
                    $pointerPath = Get-NervLeaderDemoSessionPointerPath -StateRoot $resolvedStateRoot
                    if (Test-Path -LiteralPath $pointerPath -PathType Leaf) {
                        $target = & $resolveLifecycleTarget
                        if (-not $target.Reclaimed) {
                            $oldSessionId = "$($target.SessionId)"
                            & $StopSessionAction $oldSessionId | Out-Null
                            Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId $oldSessionId
                        }
                    }

                    $newSessionId = & $startSession
                    Invoke-NervLeaderDemoCredentialScope -AdminPassword $password -StateRoot $resolvedStateRoot -ScriptBlock {
                        & $SeedAction $newSessionId | Out-Null
                        & $HealthCheckAction $newSessionId | Out-Null
                    }
                    return $newSessionId
                }
                'seed' {
                    $password = Get-NervLeaderDemoAdminPassword
                    $current = Resolve-NervLeaderDemoOwnedSession -StateRoot $resolvedStateRoot -ExpectedWorktreeRoot $resolvedWorktreeRoot
                    $exactSessionId = "$($current.SessionId)"
                    Invoke-NervLeaderDemoCredentialScope -AdminPassword $password -StateRoot $resolvedStateRoot -ScriptBlock {
                        & $SeedAction $exactSessionId
                    }
                }
                'health-check' {
                    $password = Get-NervLeaderDemoAdminPassword
                    $current = Resolve-NervLeaderDemoOwnedSession -StateRoot $resolvedStateRoot -ExpectedWorktreeRoot $resolvedWorktreeRoot
                    $exactSessionId = "$($current.SessionId)"
                    Invoke-NervLeaderDemoCredentialScope -AdminPassword $password -StateRoot $resolvedStateRoot -ScriptBlock {
                        & $HealthCheckAction $exactSessionId
                    }
                }
                'stop' {
                    $target = & $resolveLifecycleTarget
                    $exactSessionId = "$($target.SessionId)"
                    if ($target.Reclaimed) {
                        Write-Output "$exactSessionId state=ReservationReclaimed"
                        return
                    }
                    & $StopSessionAction $exactSessionId | Out-Null
                    Remove-NervLeaderDemoSessionPointer -StateRoot $resolvedStateRoot -ExpectedSessionId $exactSessionId
                    Write-Output "$exactSessionId state=Stopped"
                }
            }
        }
}
