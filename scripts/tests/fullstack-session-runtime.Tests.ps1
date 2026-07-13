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

Write-Host 'Full-stack session runtime tests passed.'
