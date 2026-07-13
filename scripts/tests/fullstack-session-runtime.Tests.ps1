# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates full-stack Docker ownership predicates against synthetic inspect data
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$sessionId = 'nerv-abcd-123456'
$fixturePath = Join-Path $PSScriptRoot 'fixtures/fullstack/docker-resources.json'
$inspectObjects = @(Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json)
$recordedContainerIds = @('owned-container-id', 'unlabeled-container-id')
$startFixture = Join-Path $PSScriptRoot 'fixtures/fullstack/aspire-start.json'
$describeFixture = Join-Path $PSScriptRoot 'fixtures/fullstack/aspire-describe.json'

$start = Read-NervAspireJson -Text (Get-Content -LiteralPath $startFixture -Raw)
$describe = Read-NervAspireJson -Text (Get-Content -LiteralPath $describeFixture -Raw)
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

$environment = Get-NervFullStackEnvironment -SessionId $sessionId
Assert-True ($environment.NERV_IIP_EPHEMERAL -eq 'true') 'Ephemeral flag missing.'
Assert-True ($environment.NERV_IIP_SESSION_ID -eq $sessionId) 'Session ID missing.'
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
    PLAYWRIGHT_BASE_URL = $scenarioManifest.endpoints.'business-console'
    NERV_IIP_FULLSTACK_ADMIN_PASSWORD = 'process-only-password'
}
Assert-True ($script:browserEnvironment.Count -eq $expectedBrowserEnvironment.Count) 'Browser child environment contained unexpected keys.'
foreach ($key in $expectedBrowserEnvironment.Keys) {
    Assert-True ($script:browserEnvironment[$key] -ceq $expectedBrowserEnvironment[$key]) "Unexpected browser environment value for '$key'."
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
}
finally {
    Remove-Item -LiteralPath $stopStateRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Full-stack session runtime tests passed.'
