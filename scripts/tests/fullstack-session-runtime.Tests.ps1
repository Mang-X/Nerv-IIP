# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates full-stack Docker ownership predicates against synthetic inspect data
#     - Starts and stops one detached local PowerShell lifecycle probe
#   Writes:
#     - Temporary detached-process stdout and stderr logs
#   Cleanup:
#     - Stops the exact lifecycle-probe process and removes its temporary directory
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True ($null -ne (Get-Command Get-NervFullStackContainerRecords -ErrorAction SilentlyContinue)) 'Shared runtime must expose current session container discovery.'
Assert-True ($null -ne (Get-Command Merge-NervSessionContainerIds -ErrorAction SilentlyContinue)) 'Cleanup must merge recorded and label-discovered containers.'
Assert-True ($null -ne (Get-Command Get-NervRecordableDcpNetworkIds -ErrorAction SilentlyContinue)) 'Startup must identify manifest-recordable DCP session networks.'

$sessionId = 'nerv-abcd-123456'
$fixturePath = Join-Path $PSScriptRoot 'fixtures/fullstack/docker-resources.json'
$inspectObjects = @(Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json)
$recordedContainerIds = @('owned-container-id', 'unlabeled-container-id')
$mergedContainerIds = @(Merge-NervSessionContainerIds `
    -RecordedIds @('recorded') `
    -DiscoveredRecords @([pscustomobject]@{ id = 'discovered' }, [pscustomobject]@{ id = 'recorded' }))
Assert-True ($mergedContainerIds.Count -eq 2) 'Container cleanup candidates must include recorded and label-discovered IDs without duplicates.'
Assert-True ($mergedContainerIds -ccontains 'discovered') 'A partially started label-discovered container must be recoverable.'
$discoveredNetworkIds = @(Get-NervContainerNetworkIds -Containers @(
    [pscustomobject]@{ NetworkSettings = [pscustomobject]@{ Networks = [pscustomobject]@{ session = [pscustomobject]@{ NetworkID = 'network-from-owned-container' } } } }
))
Assert-True ($discoveredNetworkIds -ccontains 'network-from-owned-container') 'Networks attached to label-owned containers must be recoverable after partial startup.'
$recordableDcpNetwork = [pscustomobject]@{
    Id = 'dcp-session-network'
    Name = 'aspire-session-network-xgsryykh-Nerv.IIP'
    Labels = [pscustomobject]@{
        'com.microsoft.developer.usvc-dev.creatorProcessId' = '120080'
        'com.microsoft.developer.usvc-dev.persistent' = 'false'
    }
    Containers = [pscustomobject]@{
        'owned-container-id' = [pscustomobject]@{}
        'owned-container-id-2' = [pscustomobject]@{}
    }
}
$recordableNetworkIds = @(Get-NervRecordableDcpNetworkIds `
    -Networks @(
        $recordableDcpNetwork,
        [pscustomobject]@{ Id = 'shared'; Name = 'shared'; Labels = $null; Containers = [pscustomobject]@{ 'owned-container-id' = [pscustomobject]@{} } },
        [pscustomobject]@{ Id = 'foreign'; Name = 'aspire-session-network-foreign-Nerv.IIP'; Labels = $recordableDcpNetwork.Labels; Containers = [pscustomobject]@{ 'foreign-container-id' = [pscustomobject]@{} } }
    ) `
    -OwnedContainerIds @('owned-container-id', 'owned-container-id-2'))
Assert-True ($recordableNetworkIds.Count -eq 1) 'Only the ephemeral DCP network exclusively attached to owned containers may become manifest authority.'
Assert-True ($recordableNetworkIds[0] -ceq 'dcp-session-network') 'The exact DCP session network ID must be recorded.'
$startFixture = Join-Path $PSScriptRoot 'fixtures/fullstack/aspire-start.json'
$describeFixture = Join-Path $PSScriptRoot 'fixtures/fullstack/aspire-describe.json'
$parallelAcceptanceScript = Join-Path $repoRoot 'scripts/verify-parallel-fullstack-isolation.ps1'
Assert-True (Test-Path -LiteralPath $parallelAcceptanceScript -PathType Leaf) 'Parallel full-stack acceptance entrypoint is missing.'
$parallelAcceptanceText = Get-Content -LiteralPath $parallelAcceptanceScript -Raw
$fullStackSessionText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/fullstack-session.ps1') -Raw
$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
Assert-True ($appHostText.Contains('max_connections=300')) 'Ephemeral AppHost PostgreSQL must leave capacity for full-stack probes and service pools.'
Assert-True ($fullStackSessionText.Contains("ASPIRE_CLI_START_TIMEOUT'] = '300'")) 'Full-stack startup must extend the Aspire CLI handshake timeout.'
Assert-True ($fullStackSessionText.Contains("MSBUILDDISABLENODEREUSE'] = '1'")) 'Full-stack startup must prevent reusable MSBuild worker accumulation.'
Assert-True ($fullStackSessionText.Contains("DOTNET_CLI_USE_MSBUILD_SERVER'] = '0'")) 'Full-stack startup must disable the persistent .NET build server.'
Assert-True ($fullStackSessionText.Contains("'business-master-data'")) 'Full-stack startup must wait for the business service used by the browser smoke test.'
$volumeRegistrationIndex = $fullStackSessionText.IndexOf('.runtime.volumeNames = @(', [StringComparison]::Ordinal)
$aspireStartIndex = $fullStackSessionText.IndexOf('Invoke-NervAspireStartWithRetry', [StringComparison]::Ordinal)
Assert-True ($volumeRegistrationIndex -ge 0 -and $volumeRegistrationIndex -lt $aspireStartIndex) 'Deterministic session volume names must be persisted before Aspire can create resources.'
foreach ($requiredText in @(
    '# Script-Governance:',
    '[ValidateRange(2, 3)]',
    'Get-NervFullStackStateRoot',
    'fullstack-worktrees',
    'scripts/setup-worktree.ps1',
    'Invoke-PwshScript',
    'Stop-AcceptanceStartProcess',
    'Remove-AcceptanceWorktree',
    'Test-NervProcessIdentity',
    'Primary failure:',
    'Injected failure was not observed before the primary failure:',
    'PGPASSWORD="$POSTGRES_PASSWORD"',
    "-TimeoutSeconds 300 ``",
    'finally',
    'git',
    'worktree',
    'remove'
)) {
    Assert-True ($parallelAcceptanceText.Contains($requiredText)) "Parallel acceptance script is missing '$requiredText'."
}
$parseErrors = $null
[void] [System.Management.Automation.Language.Parser]::ParseFile($parallelAcceptanceScript, [ref] $null, [ref] $parseErrors)
Assert-True (@($parseErrors).Count -eq 0) 'Parallel acceptance script must parse successfully.'
$detachedStartIndex = $parallelAcceptanceText.IndexOf('Start-DetachedManagedProcess', [StringComparison]::Ordinal)
$manifestWaitIndex = $parallelAcceptanceText.IndexOf('Wait-AcceptanceSessions', [StringComparison]::Ordinal)
$identityCleanupIndex = $parallelAcceptanceText.IndexOf('function Stop-AcceptanceStartProcess', [StringComparison]::Ordinal)
Assert-True (
    $detachedStartIndex -ge 0 -and
    $manifestWaitIndex -ge 0 -and
    $identityCleanupIndex -ge 0
) 'Detached wrapper lifecycle structure must include start, manifest wait, and identity cleanup.'

$detachedProbeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-detached-lifecycle-$([guid]::NewGuid().ToString('N'))"
$detachedProbe = $null
try {
    [System.IO.Directory]::CreateDirectory($detachedProbeRoot) | Out-Null
    $detachedProbe = Start-DetachedManagedProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') `
        -WorkingDirectory $repoRoot `
        -StdoutPath (Join-Path $detachedProbeRoot 'stdout.log') `
        -StderrPath (Join-Path $detachedProbeRoot 'stderr.log')
    Assert-True (
        Test-NervProcessIdentity -ProcessId $detachedProbe.Pid -ProcessStartTimeUtc $detachedProbe.ProcessStartTimeUtc
    ) 'Detached wrapper identity must be observable before cleanup.'
    $detachedProcess = Get-Process -Id $detachedProbe.Pid -ErrorAction Stop
    Stop-Process -Id $detachedProbe.Pid -Force
    [void] $detachedProcess.WaitForExit(10000)
    $detachedProcess.Dispose()
    Assert-True (-not (Test-NervProcessIdentity -ProcessId $detachedProbe.Pid -ProcessStartTimeUtc $detachedProbe.ProcessStartTimeUtc)) 'Detached wrapper identity must disappear after exact cleanup.'
}
finally {
    if ($null -ne $detachedProbe -and (Test-NervProcessIdentity -ProcessId $detachedProbe.Pid -ProcessStartTimeUtc $detachedProbe.ProcessStartTimeUtc)) {
        Stop-Process -Id $detachedProbe.Pid -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $detachedProbeRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$start = Read-NervAspireJson -Text (Get-Content -LiteralPath $startFixture -Raw)
$describe = Read-NervAspireJson -Text (Get-Content -LiteralPath $describeFixture -Raw) -RequireResources
$identity = Get-NervAspireStartIdentity -StartObject $start
$endpoint = Get-NervAspireResourceEndpoint -DescribeObject $describe -ResourceName 'business-console' -EndpointName 'http'
Assert-True (-not [string]::IsNullOrWhiteSpace($identity.AppHostId)) 'AppHost ID was not parsed.'
Assert-True ($identity.AppHostPid -eq 4242) 'AppHost PID was not parsed.'
Assert-True ($endpoint -eq 'http://127.0.0.1:43125') "Unexpected endpoint '$endpoint'."
$emptyInspect = @(Get-NervDockerInspectObjects -Kind container -Identifiers @() -WorkingDirectory $repoRoot -Name 'empty-inspect-contract')
Assert-True ($emptyInspect.Count -eq 0) 'Empty recorded Docker resources must not invoke inspect or fail cleanup.'
$allDescribe = [pscustomobject]@{
    resources = @('gateway', 'business-gateway', 'console', 'business-console', 'screen') | ForEach-Object {
        [pscustomobject]@{
            displayName = $_
            urls = @([pscustomobject]@{ name = 'http'; url = "http://127.0.0.1/$($_)" })
        }
    }
}
$endpointManifest = [pscustomobject]@{ endpoints = [ordered]@{} }
$savedManifest = @(Save-NervFullStackEndpoints -Manifest $endpointManifest -DescribeObject $allDescribe)
Assert-True ($savedManifest.Count -eq 1) 'Endpoint discovery must return exactly one manifest object.'
Assert-True ($savedManifest[0].endpoints.'business-console' -eq 'http://127.0.0.1/business-console') 'All public endpoints must be saved.'

$missingPayloadFailed = $false
try { Read-NervAspireJson -Text 'Aspire emitted no machine payload.' | Out-Null } catch { $missingPayloadFailed = $true }
Assert-True $missingPayloadFailed 'Aspire JSON parsing must reject missing payloads.'
$multiplePayloadsFailed = $false
try { Read-NervAspireJson -Text '{"one":1} trailing {"two":2}' | Out-Null } catch { $multiplePayloadsFailed = $true }
Assert-True $multiplePayloadsFailed 'Aspire JSON parsing must reject multiple payloads.'
$missingResourcesFailed = $false
try { Read-NervAspireJson -Text '{"appHostPid":42}' -RequireResources | Out-Null } catch { $missingResourcesFailed = $true }
Assert-True $missingResourcesFailed 'Aspire describe JSON parsing must require a resources collection.'
$emptyResourcesFailed = $false
try { Read-NervAspireJson -Text '{"resources":[]}' -RequireResources | Out-Null } catch { $emptyResourcesFailed = $true }
Assert-True $emptyResourcesFailed 'Aspire describe JSON parsing must reject an empty resources collection.'
$describeDefinition = (Get-Command Get-NervAspireDescribeObject -ErrorAction Stop).Definition
Assert-True (-not $describeDefinition.Contains('-AllowPartialOutput')) 'Parse-critical Aspire describe must reject partial redirected output.'
Assert-True ($describeDefinition.Contains('-RequireResources')) 'Aspire describe must require a complete resource collection after parsing.'
$waitDefinition = (Get-Command Wait-NervAspireResource -ErrorAction Stop).Definition
Assert-True ($waitDefinition.Contains('-AllowPartialOutput')) 'Aspire wait may opt in because the native exit code is authoritative and output is discarded.'
$stopDefinition = (Get-Command Stop-NervFullStackSession -ErrorAction Stop).Definition
Assert-True ($stopDefinition.Contains('-AllowPartialOutput')) 'Exact full-stack Aspire stop must allow partial discarded output when exit code is authoritative.'
$startActionIndex = $fullStackSessionText.IndexOf('-StartAction {', [StringComparison]::Ordinal)
$cleanupActionIndex = $fullStackSessionText.IndexOf('-CleanupAction {', $startActionIndex, [StringComparison]::Ordinal)
$startParseIndex = $fullStackSessionText.IndexOf('$startObject = Read-NervAspireJson', $cleanupActionIndex, [StringComparison]::Ordinal)
Assert-True ($startActionIndex -ge 0 -and $cleanupActionIndex -gt $startActionIndex -and $startParseIndex -gt $cleanupActionIndex) 'Full-stack Aspire start/retry boundaries must remain explicit.'
$startActionText = $fullStackSessionText.Substring($startActionIndex, $cleanupActionIndex - $startActionIndex)
Assert-True (-not $startActionText.Contains('-AllowPartialOutput')) 'Parse-critical Aspire start must reject partial redirected output.'
$transientStopText = $fullStackSessionText.Substring($cleanupActionIndex, $startParseIndex - $cleanupActionIndex)
Assert-True ($transientStopText.Contains("@('stop'")) 'Transient Aspire start cleanup must invoke stop.'
Assert-True ($transientStopText.Contains('-AllowPartialOutput')) 'Transient Aspire start cleanup must allow partial discarded stop output.'

Assert-True `
    (Test-NervDockerResourceOwnership -InspectObject $inspectObjects[0] -SessionId $sessionId -RecordedIds $recordedContainerIds) `
    'A container with both the recorded ID and exact session label must be owned.'
Assert-True `
    (-not (Test-NervDockerResourceOwnership -InspectObject $inspectObjects[1] -SessionId $sessionId -RecordedIds $recordedContainerIds)) `
    'A container from another session must not be owned.'
Assert-True `
    (-not (Test-NervDockerResourceOwnership -InspectObject $inspectObjects[2] -SessionId $sessionId -RecordedIds $recordedContainerIds)) `
    'An unlabeled container must not be owned even when its ID is recorded.'
Assert-True `
    (-not (Test-NervDockerResourceOwnership -InspectObject $inspectObjects[0] -SessionId $sessionId -RecordedIds @('different-id'))) `
    'A matching label without the recorded ID must not prove ownership.'

$recordedVolume = "postgres-data-$sessionId"
Assert-True `
    (Test-NervDockerRecordedNameOwnership -Name $recordedVolume -SessionId $sessionId -RecordedNames @($recordedVolume)) `
    'An exact recorded volume name with the session suffix must be owned.'
Assert-True `
    (-not (Test-NervDockerRecordedNameOwnership -Name "random-$sessionId" -SessionId $sessionId -RecordedNames @($recordedVolume))) `
    'A session-suffixed but unrecorded volume must not be owned.'
Assert-True `
    (-not (Test-NervDockerRecordedNameOwnership -Name 'postgres-data-nerv-ffff-654321' -SessionId $sessionId -RecordedNames @('postgres-data-nerv-ffff-654321'))) `
    'A recorded name with another session suffix must not be owned.'
Assert-True `
    (-not (Test-NervDockerRecordedNameOwnership -Name 'postgres-data-unsafe-session' -SessionId 'unsafe-session' -RecordedNames @('postgres-data-unsafe-session'))) `
    'An invalid session ID must never prove name ownership.'
Assert-True `
    (Test-NervDockerOptionalSessionLabel -Labels $null -SessionId $sessionId) `
    'Aspire volumes without exposed labels must rely on exact recorded-name ownership.'
Assert-True `
    (Test-NervDockerOptionalSessionLabel -Labels ([pscustomobject]@{ 'com.nerv-iip.session' = $sessionId }) -SessionId $sessionId) `
    'An exposed volume session label must match the active session.'
Assert-True `
    (-not (Test-NervDockerOptionalSessionLabel -Labels ([pscustomobject]@{ 'com.nerv-iip.session' = 'nerv-ffff-654321' }) -SessionId $sessionId)) `
    'An exposed volume label from another session must be rejected.'
Assert-True `
    (-not (Test-NervDockerNetworkOwnership -InspectObject ([pscustomobject]@{ Id = 'shared-network'; Name = 'shared'; Labels = $null }) -SessionId $sessionId -RecordedIds @())) `
    'An unrecorded unlabeled network discovered from a container must not be owned.'
Assert-True `
    (Test-NervDockerNetworkOwnership -InspectObject ([pscustomobject]@{ Id = 'recorded-network'; Name = 'session-network'; Labels = $null }) -SessionId $sessionId -RecordedIds @('recorded-network')) `
    'An exact manifest-recorded network may be recovered when Docker exposes no label.'
Assert-True `
    (-not (Test-NervDockerNetworkOwnership -InspectObject ([pscustomobject]@{ Id = 'bridge-id'; Name = 'bridge'; Labels = $null }) -SessionId $sessionId -RecordedIds @('bridge-id'))) `
    'Docker predefined networks must never be removed by a session cleanup.'

$environment = Get-NervFullStackEnvironment -SessionId $sessionId
Assert-True ($environment.NERV_IIP_EPHEMERAL -eq 'true') 'Ephemeral flag missing.'
Assert-True ($environment.NERV_IIP_SESSION_ID -eq $sessionId) 'Session ID missing.'
Assert-True ($environment.Messaging__Provider -ceq 'Redis') 'Ephemeral full-stack sessions must force Redis messaging.'
foreach ($expected in @(
    "nerv-iip-postgres-18-$sessionId",
    "nerv-iip-redis-$sessionId",
    "nerv-iip-minio-$sessionId",
    "nerv-iip-victoria-logs-$sessionId"
)) {
    Assert-True ($environment.Values -ccontains $expected) "Missing ephemeral volume '$expected'."
}

$invalidEnvironmentFailed = $false
try { Get-NervFullStackEnvironment -SessionId 'unsafe-session' | Out-Null } catch { $invalidEnvironmentFailed = $true }
Assert-True $invalidEnvironmentFailed 'Invalid session IDs must be rejected by the AppHost environment contract.'

$profileManifest = New-NervFullStackManifest `
    -SessionId $sessionId `
    -WorktreeRoot $repoRoot `
    -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
    -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$sessionId") `
    -MessagingProvider $environment.Messaging__Provider
Assert-True ($profileManifest.messagingProvider -ceq 'Redis') 'The non-secret messaging provider must be recorded in the session manifest.'

$appHostText = Get-Content -LiteralPath (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs') -Raw
Assert-True ($appHostText.Contains('NERV_IIP_LEADER_DEMO')) 'AppHost must require an explicit leader-demo profile flag.'
Assert-True (
    ([regex]::Matches($appHostText, 'WithEnvironment\("LeaderDemo__Seed__Enabled", leaderDemoEnabled \? "true" : "false"\)')).Count -eq 6
) 'AppHost must explicitly pass the opt-in seed flag to all six leader-demo prerequisite services.'
foreach ($resourceVariable in @(
    'businessMasterData',
    'businessProductEngineering',
    'businessInventory',
    'businessQuality',
    'businessMes',
    'businessIndustrialTelemetry'
)) {
    $resourceStart = $appHostText.IndexOf("var $resourceVariable =", [StringComparison]::Ordinal)
    $resourceEnd = $appHostText.IndexOf(';', $resourceStart)
    Assert-True (
        $resourceStart -ge 0 -and
        $resourceEnd -gt $resourceStart -and
        $appHostText.Substring($resourceStart, $resourceEnd - $resourceStart).Contains('.WithEnvironment("LeaderDemo__Seed__Enabled", leaderDemoEnabled ? "true" : "false")')
    ) "AppHost must pass the leader-demo seed flag to '$resourceVariable'."
}
$notificationStart = $appHostText.IndexOf('var notification =', [StringComparison]::Ordinal)
$notificationEnd = $appHostText.IndexOf(';', $notificationStart)
Assert-True (-not $appHostText.Substring($notificationStart, $notificationEnd - $notificationStart).Contains('LeaderDemo__Seed__Enabled')) 'AppHost must not leak the business leader-demo seed flag to Notification.'

$secretEnvironment = New-NervFullStackSecretEnvironment -SessionId $sessionId
foreach ($requiredName in @(
    'Parameters__iam-jwt-signing-key-id',
    'Parameters__iam-jwt-private-key-pem',
    'Parameters__iam-jwt-jwks-json',
    'Parameters__iam-secrets-pepper',
    'Parameters__internal-service-bearer-token',
    'Parameters__redis-password',
    'Parameters__minio-root-user',
    'Parameters__minio-root-password',
    'Parameters__iam-seed-admin-password',
    'Parameters__iam-seed-connector-host-secret',
    'Parameters__connector-ingestion-token-signing-key'
)) {
    Assert-True $secretEnvironment.Environment.ContainsKey($requiredName) "Missing session secret '$requiredName'."
    Assert-True (-not [string]::IsNullOrWhiteSpace($secretEnvironment.Environment[$requiredName])) "Session secret '$requiredName' is empty."
}
Assert-True `
    ($secretEnvironment.AdminPassword -ceq $secretEnvironment.Environment['Parameters__iam-seed-admin-password']) `
    'The browser password must match the AppHost seed password.'
$jwks = $secretEnvironment.Environment['Parameters__iam-jwt-jwks-json'] | ConvertFrom-Json
Assert-True ($jwks.keys.Count -eq 1) 'A session JWKS must contain one signing key.'
Assert-True `
    ($jwks.keys[0].kid -ceq $secretEnvironment.Environment['Parameters__iam-jwt-signing-key-id']) `
    'The session JWKS key ID must match the private signing key ID.'
$secretEnvironment.Environment.Clear()
$secretEnvironment = $null

$scenarioManifest = [pscustomobject]@{
    sessionId = $sessionId
    appHostProject = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
    worktreeRoot = "$repoRoot"
    endpoints = [pscustomobject]@{
        gateway = 'http://127.0.0.1:41001'
        'business-gateway' = 'http://127.0.0.1:41002'
        console = 'http://127.0.0.1:41003'
        'business-console' = 'http://127.0.0.1:41004'
        screen = 'http://127.0.0.1:41005'
    }
}
$script:checkedUrls = [System.Collections.Generic.List[string]]::new()
$script:browserEnvironment = $null
$healthySnapshot = [pscustomobject]@{
    resources = @([pscustomobject]@{ displayName = 'gateway'; resourceType = 'Project.v0'; state = 'Running' })
}
$scenarioResult = Invoke-NervFullStackSmokeScenario `
    -Manifest $scenarioManifest `
    -SessionAdminPassword 'process-only-password' `
    -WaitAction { param($Name, $Manifest) } `
    -HttpCheckAction { param($Name, $Url) $script:checkedUrls.Add("$Name=$Url") } `
    -AspireSnapshotAction { param($Manifest) $healthySnapshot } `
    -BrowserAction { param($Environment, $Manifest) $script:browserEnvironment = $Environment }
Assert-True ($scenarioResult.ExitCode -eq 0) 'Healthy injected smoke must pass.'
Assert-True ($script:checkedUrls.Count -eq 5) 'Smoke must HTTP-check all five manifest endpoints.'
foreach ($name in @('gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
    Assert-True ($script:checkedUrls -ccontains "$name=$($scenarioManifest.endpoints.$name)") "Smoke did not use manifest endpoint '$name'."
}
$expectedBrowserEnvironment = @{
    NERV_IIP_GATEWAY_URL = $scenarioManifest.endpoints.gateway
    NERV_IIP_BUSINESS_GATEWAY_URL = $scenarioManifest.endpoints.'business-gateway'
    NERV_IIP_PLAYWRIGHT_BASE_URL = $scenarioManifest.endpoints.'business-console'
    NERV_IIP_FULLSTACK_ADMIN_PASSWORD = 'process-only-password'
}
Assert-True ($script:browserEnvironment.Count -eq $expectedBrowserEnvironment.Count) 'Browser child environment contained unexpected keys.'
foreach ($key in $expectedBrowserEnvironment.Keys) {
    Assert-True ($script:browserEnvironment[$key] -ceq $expectedBrowserEnvironment[$key]) "Unexpected browser environment value for '$key'."
}
$playwrightReportRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-playwright-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($playwrightReportRoot) | Out-Null
    $passedReport = Join-Path $playwrightReportRoot 'passed.json'
    [IO.File]::WriteAllText($passedReport, '{"stats":{"expected":1,"skipped":0,"unexpected":0,"flaky":0}}')
    Assert-NervPlaywrightJsonReport -ReportPath $passedReport | Out-Null
    $skippedReport = Join-Path $playwrightReportRoot 'skipped.json'
    [IO.File]::WriteAllText($skippedReport, '{"stats":{"expected":0,"skipped":1,"unexpected":0,"flaky":0}}')
    $skippedReportFailed = $false
    try { Assert-NervPlaywrightJsonReport -ReportPath $skippedReport | Out-Null } catch { $skippedReportFailed = $true }
    Assert-True $skippedReportFailed 'A skipped-only Playwright report must fail the full-stack browser gate.'
}
finally {
    Remove-Item -LiteralPath $playwrightReportRoot -Recurse -Force -ErrorAction SilentlyContinue
}
$finishedFailed = $false
try {
    Invoke-NervFullStackSmokeScenario `
        -Manifest $scenarioManifest `
        -SessionAdminPassword 'process-only-password' `
        -WaitAction { param($Name, $Manifest) } `
        -HttpCheckAction { param($Name, $Url) } `
        -AspireSnapshotAction { param($Manifest) [pscustomobject]@{ resources = @([pscustomobject]@{ displayName = 'console'; resourceType = 'Project.v0'; state = 'Finished' }) } } `
        -BrowserAction { param($Environment, $Manifest) } | Out-Null
}
catch { $finishedFailed = $true }
Assert-True $finishedFailed 'A Finished Aspire project must fail smoke.'

$leaderEvidenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-leader-evidence-$([guid]::NewGuid().ToString('N'))"
try {
    $requiredLeaderResources = @(
        'iam',
        'business-gateway',
        'business-erp',
        'business-demand-planning',
        'business-product-engineering',
        'business-scheduling',
        'business-mes',
        'business-quality',
        'business-wms',
        'business-inventory',
        'business-industrial-telemetry',
        'business-maintenance',
        'postgres',
        'redis',
        'console',
        'business-console',
        'screen'
    )
    $leaderManifest = [pscustomobject]@{
        sessionId = $sessionId
        state = 'Running'
        messagingProvider = 'Redis'
        appHostProject = $scenarioManifest.appHostProject
        worktreeRoot = "$repoRoot"
        artifactPath = Join-Path $repoRoot "artifacts/fullstack/$sessionId"
        endpoints = $scenarioManifest.endpoints
    }
    $healthyLeaderSnapshot = [pscustomobject]@{
        resources = @($requiredLeaderResources | ForEach-Object {
            [pscustomobject]@{
                displayName = $_
                resourceType = if ($_ -in @('postgres', 'redis')) { 'Container.v0' } else { 'Project.v0' }
                state = 'Running'
                urls = @()
            }
        })
    }
    $script:leaderWaitCalls = [System.Collections.Generic.List[string]]::new()
    $script:leaderHttpCalls = [System.Collections.Generic.List[string]]::new()
    $script:leaderFactCalls = [System.Collections.Generic.List[string]]::new()
    $script:leaderPrincipalCalls = [System.Collections.Generic.List[string]]::new()
    $script:leaderLoginPassword = $null
    $leaderSecretPassword = 'leader-password-that-must-not-leak'
    $leaderSecretToken = 'leader-token-that-must-not-leak'

    $atomicFailureRoot = Join-Path $leaderEvidenceRoot 'atomic-write-failure'
    $atomicWriteFailure = $null
    try {
        Write-NervLeaderDemoEvidence `
            -Evidence ([pscustomobject][ordered]@{ runId = $null; diagnostics = [ordered]@{ evidencePath = $null }; marker = 'complete-json-only' }) `
            -EvidenceRoot $atomicFailureRoot `
            -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:33:56Z')) `
            -WriteTempAction {
                param($TempPath, $Content)
                [System.IO.File]::WriteAllText($TempPath, '{"marker":"truncated"', [System.Text.UTF8Encoding]::new($false))
                $failure = [System.IO.IOException]::new('simulated interrupted evidence write')
                $failure.Data['ExitCode'] = 23
                throw $failure
            } | Out-Null
    }
    catch { $atomicWriteFailure = $_.Exception }
    Assert-True ($null -ne $atomicWriteFailure -and $atomicWriteFailure.Message -ceq 'simulated interrupted evidence write') 'Interrupted evidence writes must preserve the original write error.'
    Assert-True ([int] $atomicWriteFailure.Data['ExitCode'] -eq 23) 'Interrupted evidence writes must preserve structured exit semantics.'
    Assert-True (@(Get-ChildItem -LiteralPath $atomicFailureRoot -Filter evidence.json -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0) 'Interrupted evidence writes must not publish a truncated authoritative file.'
    Assert-True (@(Get-ChildItem -LiteralPath $atomicFailureRoot -Filter '*.tmp' -File -Recurse -ErrorAction SilentlyContinue).Count -eq 0) 'Interrupted evidence writes must clean the same-directory temporary file.'

    $leaderSuccess = Invoke-NervLeaderDemoVerification `
        -Manifest $leaderManifest `
        -Command health-check `
        -SessionAdminPassword $leaderSecretPassword `
        -EvidenceRoot $leaderEvidenceRoot `
        -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:34:56Z')) `
        -CommitAction { '0123456789abcdef0123456789abcdef01234567' } `
        -WaitAction { param($Name, $Manifest, $TimeoutSeconds) $script:leaderWaitCalls.Add($Name) } `
        -AspireSnapshotAction { param($Manifest) $healthyLeaderSnapshot } `
        -HttpCheckAction { param($Name, $Url) $script:leaderHttpCalls.Add("$Name=$Url") } `
        -LoginAction {
            param($GatewayUrl, $Password)
            $script:leaderLoginPassword = $Password
            [pscustomobject]@{ data = [pscustomobject]@{ accessToken = $leaderSecretToken } }
        } `
        -PrincipalAction {
            param($GatewayUrl, $Headers)
            Assert-True ($Headers.Authorization -ceq "Bearer $leaderSecretToken") 'Principal observation did not use the authenticated token.'
            $script:leaderPrincipalCalls.Add("$GatewayUrl/api/console/v1/auth/me")
            [pscustomobject]@{
                data = [pscustomobject]@{
                    principalId = 'user-leader'
                    principalType = 'user'
                    loginName = 'leader'
                    organizationId = 'org-001'
                    environmentId = 'env-dev'
                    permissionCodes = @('business.erp.sales-orders.read', 'business.mes.work-orders.read')
                }
            }
        } `
        -PublicFactQueryAction {
            param($FactName, $Url, $Headers)
            Assert-True ($Headers.Authorization -ceq "Bearer $leaderSecretToken") "Fact '$FactName' did not use the authenticated public Gateway token."
            $script:leaderFactCalls.Add("$FactName=$Url")
            switch ($FactName) {
                'SO-DEMO-001' {
                    [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ salesOrderNo = 'SO-DEMO-001'; status = 'Released' }) } }
                }
                'WO-DEMO-Q01' {
                    [pscustomobject]@{ data = [pscustomobject]@{ workOrderId = 'WO-DEMO-Q01'; status = 'released' } }
                }
                'DEV-CNC-DEMO' {
                    [pscustomobject]@{ data = [pscustomobject]@{ resources = @([pscustomobject]@{ resourceType = 'device-asset'; code = 'DEV-CNC-DEMO'; active = $true }) } }
                }
                'MWO-DEMO-001' {
                    [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ deviceAssetId = 'DEV-CNC-DEMO'; ruleCode = 'MWO-DEMO-001:temperature'; isEnabled = $true }) } }
                }
                default { throw "Unexpected fact '$FactName'." }
            }
        }

    Assert-True ($leaderSuccess.ExitCode -eq 0) 'A healthy Redis leader-demo verification must pass.'
    Assert-True ($script:leaderLoginPassword -ceq $leaderSecretPassword) 'The login action did not receive the process-scoped password.'
    Assert-True ($script:leaderWaitCalls.Count -eq $requiredLeaderResources.Count) 'Every required leader-demo resource must use the bounded Aspire wait action.'
    foreach ($resourceName in $requiredLeaderResources) {
        Assert-True ($script:leaderWaitCalls -ccontains $resourceName) "Leader-demo health did not wait for '$resourceName'."
    }
    foreach ($entrypoint in @('business-gateway', 'console', 'business-console', 'screen')) {
        Assert-True (@($script:leaderHttpCalls | Where-Object { $_.StartsWith("$entrypoint=", [StringComparison]::Ordinal) }).Count -eq 1) "Leader-demo health did not HTTP-check '$entrypoint'."
    }
    foreach ($factName in @('SO-DEMO-001', 'WO-DEMO-Q01', 'DEV-CNC-DEMO', 'MWO-DEMO-001')) {
        Assert-True (@($script:leaderFactCalls | Where-Object { $_.StartsWith("$factName=", [StringComparison]::Ordinal) }).Count -eq 1) "Leader-demo health did not query '$factName' through a public facade."
    }
    Assert-True (Test-Path -LiteralPath $leaderSuccess.EvidencePath -PathType Leaf) 'Successful leader-demo verification did not write evidence.'
    $leaderEvidenceText = Get-Content -LiteralPath $leaderSuccess.EvidencePath -Raw
    $leaderEvidence = $leaderEvidenceText | ConvertFrom-Json -Depth 50
    Assert-True ($leaderEvidence.commitSha -ceq '0123456789abcdef0123456789abcdef01234567') 'Evidence must record the current commit SHA.'
    Assert-True ($leaderEvidence.recordedAtUtc -ceq '2026-07-20T12:34:56.0000000+00:00') 'Evidence must record the injected UTC time.'
    Assert-True ($leaderEvidence.sessionId -ceq $sessionId -and $leaderEvidence.command -ceq 'health-check') 'Evidence must identify the exact session and command.'
    Assert-True ($leaderEvidence.result -ceq 'passed' -and $leaderEvidence.messagingProvider -ceq 'Redis') 'Evidence must record the successful Redis result.'
    Assert-True ($script:leaderPrincipalCalls.Count -eq 1 -and $script:leaderPrincipalCalls[0].EndsWith('/api/console/v1/auth/me', [StringComparison]::Ordinal)) 'Verification must observe the authenticated principal through public auth/me.'
    Assert-True ($leaderEvidence.access.roles.Count -eq 0) 'Evidence must not infer or fabricate roles absent from the public auth contract.'
    Assert-True (-not $leaderEvidence.access.rolesObserved) 'Evidence must state that roles were not observed.'
    Assert-True ($leaderEvidence.access.rolesObservation -ceq 'not-exposed-by-public-auth-contract') 'Evidence must explain why authenticated roles are unavailable.'
    Assert-True ($leaderEvidence.access.principal.loginName -ceq 'leader') 'Evidence must record the observed non-secret principal identity.'
    Assert-True ($leaderEvidence.access.principal.permissionCodes.Count -eq 2) 'Evidence must record observed permission codes without inferring roles.'
    Assert-True (-not [string]::IsNullOrWhiteSpace("$($leaderEvidence.access.urls.gateway)")) 'Evidence must record non-secret access URLs.'
    Assert-True ($leaderEvidence.resources.Count -eq $requiredLeaderResources.Count) 'Evidence must contain one row per required resource.'
    Assert-True (@($leaderEvidence.resources | Where-Object { $null -eq $_.elapsedMilliseconds -or [string]::IsNullOrWhiteSpace("$($_.state)") }).Count -eq 0) 'Every resource evidence row needs state and elapsed time.'
    Assert-True ($leaderEvidence.facts.Count -eq 4 -and @($leaderEvidence.facts | Where-Object { -not $_.found }).Count -eq 0) 'Evidence must record all four observed public facts.'
    Assert-True (@($leaderEvidence.facts | Where-Object { [string]::IsNullOrWhiteSpace("$($_.link)") -or [string]::IsNullOrWhiteSpace("$($_.status)") }).Count -eq 0) 'Every fact evidence row needs a result link and observed status.'
    Assert-True (-not [string]::IsNullOrWhiteSpace("$($leaderEvidence.diagnostics.fullStackArtifactPath)")) 'Evidence must link the full-stack diagnostics.'
    Assert-True ($leaderEvidence.cleanup.command -ceq '.\nerv.ps1 demo stop') 'Evidence must include the exact cleanup command.'
    Assert-True (@(Get-ChildItem -LiteralPath (Split-Path -Parent $leaderSuccess.EvidencePath) -Filter '*.tmp' -File).Count -eq 0) 'Successful evidence publication must leave only the valid authoritative JSON file.'
    foreach ($secretValue in @($leaderSecretPassword, $leaderSecretToken, "Bearer $leaderSecretToken")) {
        Assert-True (-not $leaderEvidenceText.Contains($secretValue)) "Leader-demo success evidence leaked '$secretValue'."
    }

    $nonRedisFailure = $null
    $nonRedisManifest = $leaderManifest.PSObject.Copy()
    $nonRedisManifest.messagingProvider = 'InMemory'
    try {
        Invoke-NervLeaderDemoVerification `
            -Manifest $nonRedisManifest `
            -Command seed `
            -SessionAdminPassword $leaderSecretPassword `
            -EvidenceRoot $leaderEvidenceRoot `
            -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:35:56Z')) `
            -CommitAction { '0123456789abcdef0123456789abcdef01234567' } `
            -WaitAction { param($Name, $Manifest, $TimeoutSeconds) } `
            -AspireSnapshotAction { param($Manifest) $healthyLeaderSnapshot } `
            -HttpCheckAction { param($Name, $Url) } `
            -LoginAction { param($GatewayUrl, $Password) throw 'login must not run for a non-Redis manifest' } `
            -PrincipalAction { param($GatewayUrl, $Headers) throw 'auth/me must not run for a non-Redis manifest' } `
            -PublicFactQueryAction { param($FactName, $Url, $Headers) throw 'facts must not run for a non-Redis manifest' } | Out-Null
    }
    catch { $nonRedisFailure = $_.Exception.Message }
    Assert-True ($nonRedisFailure.Contains('Redis')) 'A non-Redis leader-demo manifest must fail explicitly.'
    $nonRedisEvidencePath = Get-ChildItem -LiteralPath $leaderEvidenceRoot -Filter evidence.json -File -Recurse |
        Where-Object { $_.Directory.Name.StartsWith('20260720T123556000Z-', [StringComparison]::Ordinal) } |
        Select-Object -First 1 -ExpandProperty FullName
    $nonRedisEvidence = Get-Content -LiteralPath $nonRedisEvidencePath -Raw | ConvertFrom-Json -Depth 50
    Assert-True ($nonRedisEvidence.resources.Count -eq $requiredLeaderResources.Count) 'A non-Redis precondition failure must still emit all required resource rows.'
    Assert-True (@($nonRedisEvidence.resources | Where-Object {
        [string]::IsNullOrWhiteSpace("$($_.state)") -or
        $null -eq $_.elapsedMilliseconds -or
        [string]::IsNullOrWhiteSpace("$($_.hint)")
    }).Count -eq 0) 'Every precondition-skipped resource row needs state, elapsed time, and remediation hint.'

    $script:failureWaitCalls = [System.Collections.Generic.List[string]]::new()
    $missingResourceFailure = $null
    try {
        Invoke-NervLeaderDemoVerification `
            -Manifest $leaderManifest `
            -Command health-check `
            -SessionAdminPassword $leaderSecretPassword `
            -EvidenceRoot $leaderEvidenceRoot `
            -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:36:56Z')) `
            -CommitAction { '0123456789abcdef0123456789abcdef01234567' } `
            -WaitAction {
                param($Name, $Manifest, $TimeoutSeconds)
                $script:failureWaitCalls.Add($Name)
                if ($Name -ceq 'business-quality') { throw 'simulated quality outage password=failure-secret' }
            } `
            -AspireSnapshotAction {
                param($Manifest)
                [pscustomobject]@{ resources = @($healthyLeaderSnapshot.resources | Where-Object { $_.displayName -cne 'business-quality' }) }
            } `
            -HttpCheckAction { param($Name, $Url) } `
            -LoginAction { param($GatewayUrl, $Password) [pscustomobject]@{ data = [pscustomobject]@{ accessToken = $leaderSecretToken } } } `
            -PrincipalAction { param($GatewayUrl, $Headers) [pscustomobject]@{ data = [pscustomobject]@{ principalId = 'user-leader'; principalType = 'user'; loginName = 'leader'; organizationId = 'org-001'; environmentId = 'env-dev'; permissionCodes = @() } } } `
            -PublicFactQueryAction { param($FactName, $Url, $Headers) throw 'fact query should be skipped after an unhealthy resource gate' } | Out-Null
    }
    catch { $missingResourceFailure = $_.Exception.Message }
    Assert-True ($missingResourceFailure.Contains('business-quality')) 'An unhealthy resource failure must name the resource.'
    Assert-True ($script:failureWaitCalls.Count -eq $requiredLeaderResources.Count) 'A failed resource must not prevent bounded checks from naming every other required resource.'
    $failedEvidencePath = Get-ChildItem -LiteralPath $leaderEvidenceRoot -Filter evidence.json -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    $failedEvidenceText = Get-Content -LiteralPath $failedEvidencePath -Raw
    $failedEvidence = $failedEvidenceText | ConvertFrom-Json -Depth 50
    Assert-True ($failedEvidence.result -ceq 'failed') 'A failed leader-demo verification must still write failure evidence.'
    $qualityEvidence = @($failedEvidence.resources | Where-Object { $_.name -ceq 'business-quality' })
    Assert-True ($qualityEvidence.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace("$($qualityEvidence[0].hint)")) 'A failed resource needs one bounded remediation hint.'
    Assert-True (-not $failedEvidenceText.Contains('failure-secret')) 'Leader-demo failure evidence must redact sensitive error values.'

    $exitCodeFailure = $null
    $script:nativeExitPwshPath = (Get-Process -Id $PID).Path
    $nativeExitSecret = 'native-secret-that-must-not-leak'
    try {
        Invoke-NervLeaderDemoVerification `
            -Manifest $leaderManifest `
            -Command health-check `
            -SessionAdminPassword $leaderSecretPassword `
            -EvidenceRoot $leaderEvidenceRoot `
            -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:37:56Z')) `
            -CommitAction { '0123456789abcdef0123456789abcdef01234567' } `
            -WaitAction {
                param($Name, $Manifest, $TimeoutSeconds)
                if ($Name -ceq 'business-mes') {
                    Invoke-NativeCommandOutput `
                        -Command $script:nativeExitPwshPath `
                        -Arguments @('-NoProfile', '-NonInteractive', '-Command', "[Console]::Error.WriteLine('token=$nativeExitSecret'); exit 17") `
                        -WorkingDirectory $repoRoot `
                        -TimeoutSeconds 30 `
                        -Name 'leader-demo-native-exit-probe' | Out-Null
                }
            } `
            -AspireSnapshotAction { param($Manifest) $healthyLeaderSnapshot } `
            -HttpCheckAction { param($Name, $Url) } `
            -LoginAction { param($GatewayUrl, $Password) throw 'login must be skipped after exit 17' } `
            -PrincipalAction { param($GatewayUrl, $Headers) throw 'auth/me must be skipped after exit 17' } `
            -PublicFactQueryAction { param($FactName, $Url, $Headers) throw 'facts must be skipped after exit 17' } | Out-Null
    }
    catch { $exitCodeFailure = $_.Exception }
    Assert-True ($null -ne $exitCodeFailure) 'A real native exit 17 must fail verification.'
    Assert-True ([int] $exitCodeFailure.Data['ExitCode'] -eq 17) 'Verification must preserve the real native nonzero exit code after evidence.'
    Assert-True (-not $exitCodeFailure.Message.Contains($nativeExitSecret)) 'Native failure exceptions must not expose sensitive process output.'
    $exitEvidencePath = Get-ChildItem -LiteralPath $leaderEvidenceRoot -Filter evidence.json -File -Recurse |
        Where-Object { $_.Directory.Name.StartsWith('20260720T123756000Z-', [StringComparison]::Ordinal) } |
        Select-Object -First 1 -ExpandProperty FullName
    Assert-True (Test-Path -LiteralPath $exitEvidencePath -PathType Leaf) 'Exit 17 verification must write evidence before propagating the code.'
    $exitEvidence = Get-Content -LiteralPath $exitEvidencePath -Raw | ConvertFrom-Json -Depth 50
    Assert-True ($exitEvidence.result -ceq 'failed' -and $exitEvidence.exitCode -eq 17) 'Failure evidence must record the propagated exit code.'
    Assert-True (-not (Get-Content -LiteralPath $exitEvidencePath -Raw).Contains($nativeExitSecret)) 'Native failure evidence must not expose sensitive process output.'

    $factFailure = $null
    try {
        Invoke-NervLeaderDemoVerification `
            -Manifest $leaderManifest `
            -Command seed `
            -SessionAdminPassword $leaderSecretPassword `
            -EvidenceRoot $leaderEvidenceRoot `
            -UtcNow ([DateTimeOffset]::Parse('2026-07-20T12:38:56Z')) `
            -CommitAction { '0123456789abcdef0123456789abcdef01234567' } `
            -WaitAction { param($Name, $Manifest, $TimeoutSeconds) } `
            -AspireSnapshotAction { param($Manifest) $healthyLeaderSnapshot } `
            -HttpCheckAction { param($Name, $Url) } `
            -LoginAction { param($GatewayUrl, $Password) [pscustomobject]@{ data = [pscustomobject]@{ accessToken = $leaderSecretToken } } } `
            -PrincipalAction { param($GatewayUrl, $Headers) [pscustomobject]@{ data = [pscustomobject]@{ principalId = 'user-leader'; principalType = 'user'; loginName = 'leader'; organizationId = 'org-001'; environmentId = 'env-dev'; permissionCodes = @() } } } `
            -PublicFactQueryAction {
                param($FactName, $Url, $Headers)
                switch ($FactName) {
                    'SO-DEMO-001' { [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ salesOrderNo = 'SO-DEMO-001'; status = 'Released' }) } } }
                    'WO-DEMO-Q01' { [pscustomobject]@{ data = [pscustomobject]@{ workOrderId = 'WO-DEMO-Q01'; status = 'released' } } }
                    'DEV-CNC-DEMO' { [pscustomobject]@{ data = [pscustomobject]@{ resources = @() } } }
                    'MWO-DEMO-001' { [pscustomobject]@{ data = [pscustomobject]@{ items = @([pscustomobject]@{ deviceAssetId = 'DEV-CNC-DEMO'; ruleCode = 'MWO-DEMO-001:temperature'; isEnabled = $true }) } } }
                }
            } | Out-Null
    }
    catch { $factFailure = $_.Exception }
    Assert-True ($null -ne $factFailure) 'A missing required public fact must fail seed verification.'
    $factEvidencePath = Get-ChildItem -LiteralPath $leaderEvidenceRoot -Filter evidence.json -File -Recurse |
        Where-Object { $_.Directory.Name.StartsWith('20260720T123856000Z-', [StringComparison]::Ordinal) } |
        Select-Object -First 1 -ExpandProperty FullName
    $factFailureEvidence = Get-Content -LiteralPath $factEvidencePath -Raw | ConvertFrom-Json -Depth 50
    $deviceFactFailure = @($factFailureEvidence.facts | Where-Object { $_.key -ceq 'DEV-CNC-DEMO' })
    Assert-True ($deviceFactFailure.Count -eq 1 -and -not $deviceFactFailure[0].found) 'Failed fact evidence must retain the exact missing key.'
    Assert-True ($deviceFactFailure[0].hint.Contains($leaderManifest.artifactPath)) 'Failed fact remediation must include the diagnostic artifact path.'
    Assert-True ($deviceFactFailure[0].hint.Contains('.\nerv.ps1 demo reset')) 'Failed fact remediation must include a bounded recovery command.'
}
finally {
    Remove-Item -LiteralPath $leaderEvidenceRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$generatedDiagnosticSecret = New-NervFullStackSecretValue -Bytes 24
$unsafeDiagnostic = "$generatedDiagnosticSecret password=secret Authorization: Bearer token Host=localhost;Port=5432;Database=nerv;Username=postgres;Password=db-secret"
$safeDiagnostic = Protect-NervFullStackDiagnosticText -Text $unsafeDiagnostic -SensitiveValues @($generatedDiagnosticSecret)
foreach ($forbidden in @($generatedDiagnosticSecret, 'password=secret', 'Bearer token', 'db-secret')) {
    Assert-True (-not $safeDiagnostic.Contains($forbidden)) "Diagnostic redaction leaked '$forbidden'."
}
$diagnosticRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-diagnostics-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $diagnosticRoot 'traces')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $diagnosticRoot 'traces/preserved.txt'), 'preserve')
    $diagnosticManifest = [pscustomobject]@{
        sessionId = $sessionId
        state = 'Running'
        artifactPath = $diagnosticRoot
        appHostProject = $scenarioManifest.appHostProject
        worktreeRoot = "$repoRoot"
        endpoints = $scenarioManifest.endpoints
        cleanup = [pscustomobject]@{ errors = @() }
    }
    Collect-NervFullStackDiagnostics `
        -Manifest $diagnosticManifest `
        -SensitiveValues @($generatedDiagnosticSecret) `
        -LogAction { param($ResourceName, $Manifest, $TimeoutSeconds) $unsafeDiagnostic } | Out-Null
    $diagnosticText = (Get-ChildItem -LiteralPath $diagnosticRoot -File -Recurse | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
    foreach ($forbidden in @($generatedDiagnosticSecret, 'password=secret', 'Bearer token', 'db-secret')) {
        Assert-True (-not $diagnosticText.Contains($forbidden)) "Collected diagnostics leaked '$forbidden'."
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $diagnosticRoot 'summary.json')) 'Diagnostic summary was not written.'
    Assert-True (Test-Path -LiteralPath (Join-Path $diagnosticRoot 'traces/preserved.txt')) 'Existing trace artifacts must be preserved.'
}
finally {
    Remove-Item -LiteralPath $diagnosticRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$script:managedCollectCalls = 0
$script:managedStopCalls = 0
$managedScenarioFailure = $null
try {
    Invoke-NervManagedFullStackRun `
        -StartAction { [pscustomobject]@{ sessionId = 'nerv-dead-000002'; state = 'Running' } } `
        -ScenarioAction { param($Manifest) throw 'original scenario failure' } `
        -CollectAction { param($Manifest) $script:managedCollectCalls++ } `
        -StopAction { param($Manifest) $script:managedStopCalls++; [pscustomobject]@{ Complete = $true; Manifest = $Manifest } } | Out-Null
}
catch { $managedScenarioFailure = $_.Exception.Message }
Assert-True ($managedScenarioFailure -eq 'original scenario failure') 'Managed run must preserve the original scenario error.'
Assert-True ($script:managedCollectCalls -eq 1) 'Managed run must collect after scenario failure.'
Assert-True ($script:managedStopCalls -eq 1) 'Managed run must stop after scenario failure.'

$managedCleanupFailure = $null
try {
    Invoke-NervManagedFullStackRun `
        -StartAction { [pscustomobject]@{ sessionId = 'nerv-dead-000003'; state = 'Running' } } `
        -ScenarioAction { param($Manifest) throw 'scenario hidden by cleanup' } `
        -CollectAction { param($Manifest) } `
        -StopAction { param($Manifest) throw 'cleanup failure wins' } | Out-Null
}
catch { $managedCleanupFailure = $_.Exception.Message }
Assert-True ($managedCleanupFailure -eq 'cleanup failure wins') 'Cleanup failure must take precedence after cleanup was attempted.'

$script:dockerRetryCalls = 0
$dockerRetryResult = Invoke-NervDockerCleanupWithRetry `
    -Manifest ([pscustomobject]@{ sessionId = $sessionId }) `
    -RemoveAction {
        param($Manifest)
        $script:dockerRetryCalls++
        if ($script:dockerRetryCalls -lt 2) { return [pscustomobject]@{ Complete = $false; Remaining = @('container:stopping') } }
        return [pscustomobject]@{ Complete = $true; Remaining = @() }
    } `
    -DelayAction { param($Attempt) }
Assert-True $dockerRetryResult.Complete 'Docker cleanup retry must return the successful result.'
Assert-True ($script:dockerRetryCalls -eq 2) 'Docker cleanup must retry a transient incomplete result.'

$script:aspireRetryCalls = 0
$script:aspireRetryCleanupCalls = 0
$aspireRetryResult = Invoke-NervAspireStartWithRetry `
    -StartAction {
        $script:aspireRetryCalls++
        if ($script:aspireRetryCalls -eq 1) { throw 'MSB4166 system resource exhaustion' }
        return 'started'
    } `
    -CleanupAction { $script:aspireRetryCleanupCalls++ } `
    -DelayAction { param($Attempt) }
Assert-True ($aspireRetryResult -eq 'started') 'Aspire startup retry must return the successful result.'
Assert-True ($script:aspireRetryCalls -eq 2) 'Aspire startup must retry one transient MSBuild resource failure.'
Assert-True ($script:aspireRetryCleanupCalls -eq 1) 'Aspire startup retry must clean the failed attempt first.'

$missingWorktree = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-missing-worktree-$([guid]::NewGuid().ToString('N'))"
$cleanupStateRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-cleanup-state-$([guid]::NewGuid().ToString('N'))"
try {
    $cleanupWorkingDirectory = Get-NervFullStackCleanupWorkingDirectory -StateRoot $cleanupStateRoot
    Assert-True (Test-Path -LiteralPath $cleanupWorkingDirectory -PathType Container) 'Cleanup must use an existing state-root working directory.'
    Assert-True (-not (Test-NervFullStackAppHostAvailable -Manifest ([pscustomobject]@{ worktreeRoot = $missingWorktree; appHostProject = (Join-Path $missingWorktree 'AppHost.csproj') }))) 'Aspire stop must be skipped after its worktree and AppHost project were removed.'
}
finally {
    Remove-Item -LiteralPath $cleanupStateRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$script:guardianReads = 0
$script:guardianStops = 0
$guardianResult = Invoke-NervFullStackGuardian `
    -SessionId $sessionId `
    -Mode Automated `
    -CoordinatorPid 1 `
    -CoordinatorStartTimeUtc '2000-01-01T00:00:00Z' `
    -IntervalSeconds 1 `
    -MaximumObservationFailures 3 `
    -MaximumStopAttempts 3 `
    -ReadAction {
        $script:guardianReads++
        if ($script:guardianReads -eq 1) { throw 'transient manifest lock' }
        if ($script:guardianStops -eq 0) { return [pscustomobject]@{ state = 'Running'; leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O') } }
        return [pscustomobject]@{ state = 'Stopped'; leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O') }
    } `
    -CoordinatorAliveAction { $false } `
    -StopAction {
        $script:guardianStops++
        if ($script:guardianStops -eq 1) { throw 'transient stop failure' }
    } `
    -DelayAction { param($Seconds) }
Assert-True ($guardianResult.State -eq 'Stopped') 'Guardian must survive transient manifest and stop failures until cleanup reaches Stopped.'
Assert-True ($script:guardianReads -ge 3) 'Guardian must retry manifest observation after a transient read failure.'
Assert-True ($script:guardianStops -eq 2) 'Guardian must retry a failed stop operation.'

$script:worktreeStoppedPids = [System.Collections.Generic.List[int]]::new()
$worktreeProcessResult = Stop-NervWorktreeProcesses `
    -WorktreeRoot 'C:\nfs\fullstack-worktrees\abcd1234\s2' `
    -ExcludedProcessIds @(102) `
    -ProcessQueryAction {
        @(
            [pscustomobject]@{ ProcessId = 101; Name = 'dotnet.exe'; CommandLine = 'dotnet run --project C:\nfs\fullstack-worktrees\abcd1234\s2\backend\service.csproj' },
            [pscustomobject]@{ ProcessId = 102; Name = 'node.exe'; CommandLine = 'node C:\nfs\fullstack-worktrees\abcd1234\s2\frontend\vite.js' },
            [pscustomobject]@{ ProcessId = 103; Name = 'dotnet.exe'; CommandLine = 'dotnet run --project C:\other\service.csproj' },
            [pscustomobject]@{ ProcessId = 104; Name = 'pwsh.exe'; CommandLine = 'pwsh -File C:\nfs\fullstack-worktrees\abcd1234\s2\scripts\operator.ps1' }
        )
    } `
    -StopAction { param($ProcessId, $Reason) $script:worktreeStoppedPids.Add($ProcessId) }
Assert-True ($worktreeProcessResult.StoppedProcessIds.Count -eq 1) 'Worktree cleanup must select only exact owned process command lines.'
Assert-True ($script:worktreeStoppedPids[0] -eq 101) 'Worktree cleanup stopped the wrong process.'

$stopStateRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-stop-$([guid]::NewGuid().ToString('N'))"
try {
    $stopSessionId = 'nerv-dead-000001'
    $stopManifest = New-NervFullStackManifest `
        -SessionId $stopSessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$stopSessionId") `
        -StateRoot $stopStateRoot
    $stopManifest = Move-NervFullStackSessionState -Manifest $stopManifest -State Running
    Write-NervFullStackManifest -Manifest $stopManifest -StateRoot $stopStateRoot
    $script:aspireStopCalls = 0
    $script:processStopCalls = 0
    $script:dockerStopCalls = 0
    $aspireStop = { param($Manifest) $script:aspireStopCalls++ }
    $processStop = { param($Manifest) $script:processStopCalls++ }
    $dockerStop = {
        param($Manifest)
        $script:dockerStopCalls++
        [pscustomobject]@{ Complete = $true; Remaining = @() }
    }

    $firstStop = Stop-NervFullStackSession -SessionId $stopSessionId -StateRoot $stopStateRoot -AspireStopAction $aspireStop -ProcessStopAction $processStop -DockerRemoveAction $dockerStop
    $secondStop = Stop-NervFullStackSession -SessionId $stopSessionId -StateRoot $stopStateRoot -AspireStopAction $aspireStop -ProcessStopAction $processStop -DockerRemoveAction $dockerStop
    Assert-True $firstStop.Complete 'The first exact stop must complete.'
    Assert-True $secondStop.Complete 'A repeated exact stop must remain complete.'
    Assert-True ($script:aspireStopCalls -eq 1) 'A stopped session must not invoke Aspire stop twice.'
    Assert-True ($script:processStopCalls -eq 1) 'A stopped session must not stop recorded processes twice.'
    Assert-True ($script:dockerStopCalls -eq 2) 'Repeated stop must still verify exact recorded Docker resources.'
    $stoppedManifest = Read-NervFullStackManifest -SessionId $stopSessionId -StateRoot $stopStateRoot
    Assert-True ($stoppedManifest.state -eq 'Stopped') 'A complete stop must persist Stopped.'

    $failedStopSessionId = 'nerv-dead-000004'
    $failedStopManifest = New-NervFullStackManifest `
        -SessionId $failedStopSessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$failedStopSessionId") `
        -StateRoot $stopStateRoot
    $failedStopManifest = Move-NervFullStackSessionState -Manifest $failedStopManifest -State Running
    Write-NervFullStackManifest -Manifest $failedStopManifest -StateRoot $stopStateRoot
    $failedStop = Stop-NervFullStackSession `
        -SessionId $failedStopSessionId `
        -StateRoot $stopStateRoot `
        -AspireStopAction { param($Manifest) throw 'aspire stop failed' } `
        -ProcessStopAction { param($Manifest) throw 'process stop failed' } `
        -DockerRemoveAction { param($Manifest) [pscustomobject]@{ Complete = $true; Remaining = @() } }
    Assert-True (-not $failedStop.Complete) 'Aspire or process cleanup errors must prevent a successful stop result.'
    Assert-True ($failedStop.Manifest.state -eq 'CleanupFailed') 'Aspire or process cleanup errors must persist CleanupFailed.'
    Assert-True ($failedStop.Remaining -ccontains 'aspire:stop-failed') 'Aspire cleanup failure must be explicit.'
    Assert-True ($failedStop.Remaining -ccontains 'process:stop-failed') 'Process cleanup failure must be explicit.'
}
finally {
    Remove-Item -LiteralPath $stopStateRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Full-stack session runtime tests passed.'
