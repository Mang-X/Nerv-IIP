# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the acceptance scenario manifest with temporary JSON fixtures
#   Writes:
#     - Temporary JSON fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrix.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$v1ManifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-acceptance-scenario-matrix-$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $libraryPath -PathType Leaf)) {
    throw "Acceptance scenario matrix library is missing at '$libraryPath'."
}
. $libraryPath

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Copy-ManifestObject {
    return (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 50)
}

function Write-ManifestFixture {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Manifest
    )

    $path = Join-Path $fixtureRoot "$Name.json"
    [IO.File]::WriteAllText($path, (($Manifest | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
    return $path
}

function Assert-ManifestRejected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $path = Write-ManifestFixture -Name $Name -Manifest $Manifest
    $rejected = $false
    try {
        Import-NervAcceptanceScenarioMatrixManifest `
            -ManifestPath $path `
            -V1ManifestPath $v1ManifestPath `
            -RepositoryRoot $repoRoot | Out-Null
    }
    catch {
        $rejected = $_.Exception.Message.Contains($ExpectedMessage, [StringComparison]::Ordinal)
    }
    Assert-Contract $rejected "Mutation '$Name' must be rejected with '$ExpectedMessage'."
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    $manifest = Import-NervAcceptanceScenarioMatrixManifest `
        -ManifestPath $manifestPath `
        -V1ManifestPath $v1ManifestPath `
        -RepositoryRoot $repoRoot

    $expectedIds = @(
        'sales-order-demand',
        'wms-delivery-erp',
        'mes-produced-lot-inventory',
        'telemetry-runtime-maintenance',
        'erp-return-closure',
        'equipment-unavailable-scheduling-mes'
    )
    Assert-Contract ([string]::Equals((@($manifest.scenarios.id) -join '|'), ($expectedIds -join '|'), [StringComparison]::Ordinal)) 'The manifest must freeze the six approved scenario ids in stable order.'
    Assert-Contract (@($manifest.scenarios | Select-Object -First 5 | Where-Object {
        -not [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
    }).Count -eq 0) 'The first five scenarios must be active/core.'
    $blocked = $manifest.scenarios[5]
    Assert-Contract (
        [string]::Equals([string]$blocked.status, 'blocked', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$blocked.tier, 'extended', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$blocked.ownerIssue, '#1240', [StringComparison]::Ordinal) -and
        -not [string]::IsNullOrWhiteSpace([string]$blocked.blockedReason) -and
        @($blocked.testProjects.frozenTestIdentities).Count -gt 0
    ) 'The future equipment-unavailable scenario must remain blocked/extended with #1240 ownership, a reason, and a canonical identity.'

    $missingScenario = Copy-ManifestObject
    $missingScenario.scenarios = @($missingScenario.scenarios | Select-Object -First 5)
    Assert-ManifestRejected -Name 'missing-scenario' -Manifest $missingScenario -ExpectedMessage 'exactly 6 scenarios'

    $duplicateId = Copy-ManifestObject
    $duplicateId.scenarios[1].id = [string]$duplicateId.scenarios[0].id
    Assert-ManifestRejected -Name 'duplicate-id' -Manifest $duplicateId -ExpectedMessage 'unique canonical id'

    $duplicateAlias = Copy-ManifestObject
    $duplicateAlias.scenarios[1].v1Alias = [string]$duplicateAlias.scenarios[0].v1Alias
    Assert-ManifestRejected -Name 'duplicate-alias' -Manifest $duplicateAlias -ExpectedMessage 'v1Alias must be ordinal-unique'

    $duplicateIdentity = Copy-ManifestObject
    $duplicateIdentity.scenarios[1].testProjects[0].frozenTestIdentities[0] = [string]$duplicateIdentity.scenarios[0].testProjects[0].frozenTestIdentities[0]
    Assert-ManifestRejected -Name 'duplicate-identity' -Manifest $duplicateIdentity -ExpectedMessage 'frozen identity must be ordinal-unique'

    $invalidStatus = Copy-ManifestObject
    $invalidStatus.scenarios[0].status = 'ready'
    Assert-ManifestRejected -Name 'invalid-status' -Manifest $invalidStatus -ExpectedMessage 'invalid status'

    $invalidTier = Copy-ManifestObject
    $invalidTier.scenarios[0].tier = 'required'
    Assert-ManifestRejected -Name 'invalid-tier' -Manifest $invalidTier -ExpectedMessage 'invalid tier'

    $blockedWithoutReason = Copy-ManifestObject
    $blockedWithoutReason.scenarios[5].blockedReason = '   '
    Assert-ManifestRejected -Name 'blocked-without-reason' -Manifest $blockedWithoutReason -ExpectedMessage 'blockedReason'

    foreach ($unknownMutation in @(
        @{ Name = 'unknown-top-level'; Apply = { param($value) $value | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-planning-budget'; Apply = { param($value) $value.planningBudget | Add-Member -NotePropertyName extra -NotePropertyValue 1 } },
        @{ Name = 'unknown-scenario'; Apply = { param($value) $value.scenarios[0] | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-entrypoint'; Apply = { param($value) $value.scenarios[0].entrypoint | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-test-project'; Apply = { param($value) $value.scenarios[0].testProjects[0] | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-dependencies'; Apply = { param($value) $value.scenarios[0].dependencies | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-impact'; Apply = { param($value) $value.scenarios[0].impact | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-run-policy'; Apply = { param($value) $value.scenarios[0].runPolicy | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-execution-budget'; Apply = { param($value) $value.scenarios[0].executionBudget | Add-Member -NotePropertyName extra -NotePropertyValue 1 } },
        @{ Name = 'unknown-diagnostic-protocol'; Apply = { param($value) $value.scenarios[0].diagnosticProtocol | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-evidence-protocol'; Apply = { param($value) $value.scenarios[0].evidenceProtocol | Add-Member -NotePropertyName extra -NotePropertyValue $true } },
        @{ Name = 'unknown-cleanup-protocol'; Apply = { param($value) $value.scenarios[0].cleanupProtocol | Add-Member -NotePropertyName extra -NotePropertyValue $true } }
    )) {
        $unknown = Copy-ManifestObject
        & $unknownMutation.Apply $unknown
        Assert-ManifestRejected -Name $unknownMutation.Name -Manifest $unknown -ExpectedMessage 'unknown field'
    }

    foreach ($budgetMutation in @(
        @{ Name = 'zero-planning-budget'; Apply = { param($value) $value.planningBudget.restorePerProjectSeconds = 0 } },
        @{ Name = 'overflow-planning-budget'; Apply = { param($value) $value.planningBudget.discoveryPerProjectSeconds = 901 } },
        @{ Name = 'fractional-planning-budget'; Apply = { param($value) $value.planningBudget.artifactWriteSeconds = 1.5 } },
        @{ Name = 'zero-execution-budget'; Apply = { param($value) $value.scenarios[0].executionBudget.cleanupSeconds = 0 } },
        @{ Name = 'overflow-execution-budget'; Apply = { param($value) $value.scenarios[0].executionBudget.executionTimeoutSeconds = 7201 } }
    )) {
        $budget = Copy-ManifestObject
        & $budgetMutation.Apply $budget
        Assert-ManifestRejected -Name $budgetMutation.Name -Manifest $budget -ExpectedMessage 'positive integer within schema limit'
    }

    $whitespaceString = Copy-ManifestObject
    $whitespaceString.scenarios[0].services[0] = '   '
    Assert-ManifestRejected -Name 'whitespace-string' -Manifest $whitespaceString -ExpectedMessage 'trimmed non-empty string'

    foreach ($driftMutation in @(
        @{ Name = 'alias-drift'; Apply = { param($value) $value.scenarios[0].v1Alias = 'sales-order-demand-planning-drifted' }; Message = 'v1 alias set must exactly match' },
        @{ Name = 'project-drift'; Apply = { param($value) $value.scenarios[0].testProjects[0].path = 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Drifted.csproj' }; Message = 'project must equal v1' },
        @{ Name = 'entrypoint-drift'; Apply = { param($value) $value.scenarios[0].entrypoint.path = 'scripts/verify-drifted.ps1' }; Message = 'entrypoint must equal v1' },
        @{ Name = 'identity-drift'; Apply = { param($value) $value.scenarios[0].testProjects[0].frozenTestIdentities[0] = 'Nerv.IIP.Drifted.Tests.Drifted' }; Message = 'identities must equal v1' },
        @{ Name = 'dependency-drift'; Apply = { param($value) $value.scenarios[0].dependencies.redis = $false }; Message = 'dependencies must equal v1' },
        @{ Name = 'diagnostic-drift'; Apply = { param($value) $value.scenarios[0].diagnosticProtocol.schemas[0] = 'drifted' }; Message = 'diagnostic schemas must equal v1' }
    )) {
        $drift = Copy-ManifestObject
        & $driftMutation.Apply $drift
        Assert-ManifestRejected -Name $driftMutation.Name -Manifest $drift -ExpectedMessage $driftMutation.Message
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Acceptance scenario matrix contract tests passed.'
