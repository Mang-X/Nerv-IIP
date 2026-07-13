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

Write-Host 'Full-stack session runtime tests passed.'
