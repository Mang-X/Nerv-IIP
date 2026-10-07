# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the real FullChain manifest shape and frozen scenario closure with fixtures
#   Writes:
#     - Temporary manifest fixtures under the operating-system temp directory
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
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [AllowNull()] [object] $V1Manifest
    )

    $path = Write-ManifestFixture -Name $Name -Manifest $Manifest
    $fixtureV1ManifestPath = $v1ManifestPath
    if ($null -ne $V1Manifest) {
        $fixtureV1ManifestPath = Write-ManifestFixture -Name "$Name-v1" -Manifest $V1Manifest
    }
    $rejected = $false
    try {
        Import-NervAcceptanceScenarioMatrixManifest `
            -ManifestPath $path `
            -V1ManifestPath $fixtureV1ManifestPath `
            -RepositoryRoot $repoRoot | Out-Null
    }
    catch {
        $rejected = $_.Exception.Message.Contains($ExpectedMessage, [StringComparison]::Ordinal)
    }
    Assert-Contract $rejected "Mutation '$Name' must be rejected with '$ExpectedMessage'."
}

function Copy-JsonObject {
    param([Parameter(Mandatory)] [object] $Value)

    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50)
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    $manifest = Import-NervAcceptanceScenarioMatrixManifest `
        -ManifestPath $manifestPath `
        -V1ManifestPath $v1ManifestPath `
        -RepositoryRoot $repoRoot
    $activeCoreScenarioCount = @($manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
    }).Count
    $activeScenarioCount = @($manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal)
    }).Count

    $expandedMatrix = Copy-ManifestObject
    $expandedScenario = ($expandedMatrix.scenarios[0] | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50)
    $expandedScenario.id = 'additional-governed-scenario'
    $expandedScenario.v1Alias = 'additional-governed-member'
    $expandedScenario.testProjects[0].frozenTestIdentities = @('Nerv.IIP.Business.FullChain.Tests.AdditionalGovernedTests.Additional_governed_scenario')
    $expandedMatrix.scenarios += $expandedScenario
    $expandedV1 = Get-Content -LiteralPath $v1ManifestPath -Raw | ConvertFrom-Json -Depth 30
    $expandedMember = ($expandedV1.members | Where-Object { [string]::Equals([string]$_.id, 'sales-order-demand-planning', [StringComparison]::Ordinal) } | ConvertTo-Json -Depth 30 | ConvertFrom-Json -Depth 30)
    $expandedMember.id = 'additional-governed-member'
    $expandedMember.filter = 'FullyQualifiedName=Nerv.IIP.Business.FullChain.Tests.AdditionalGovernedTests.Additional_governed_scenario'
    $expandedMember.expectedTestIdentities = @('Nerv.IIP.Business.FullChain.Tests.AdditionalGovernedTests.Additional_governed_scenario')
    $expandedV1.members += $expandedMember
    $expandedMatrixPath = Write-ManifestFixture -Name 'expanded-authority-set' -Manifest $expandedMatrix
    $expandedV1Path = Write-ManifestFixture -Name 'expanded-authority-set-v1' -Manifest $expandedV1
    $expanded = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $expandedMatrixPath -V1ManifestPath $expandedV1Path -RepositoryRoot $repoRoot
    Assert-Contract (@($expanded.scenarios | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal) }).Count -eq 6) 'The matrix and FullChain manifest must admit a sixth active/core identity without a hard-coded member count.'

    $missingImpactRoot = Copy-ManifestObject
    $missingImpactRoot.scenarios[0].impact.paths[0] = 'backend/services/Business/DoesNotExist/**'
    Assert-ManifestRejected -Name 'missing-impact-static-root' -Manifest $missingImpactRoot -ExpectedMessage 'impact path static root must exist with exact casing'

    $wrongCaseImpactRoot = Copy-ManifestObject
    $wrongCaseImpactRoot.scenarios[0].impact.paths[0] = 'backend/services/Business/erp/**'
    Assert-ManifestRejected -Name 'wrong-case-impact-static-root' -Manifest $wrongCaseImpactRoot -ExpectedMessage 'impact path static root must exist with exact casing'

    $nonBooleanV1Dependency = Get-Content -LiteralPath $v1ManifestPath -Raw | ConvertFrom-Json -Depth 30
    $nonBooleanV1Dependency.members[0].dependencies.postgres = 'false'
    Assert-ManifestRejected -Name 'non-boolean-v1-dependency' -Manifest (Copy-ManifestObject) -V1Manifest $nonBooleanV1Dependency -ExpectedMessage 'v1 dependency must be a JSON boolean'

    $diagnosticCaptureDisabled = Copy-ManifestObject
    $diagnosticCaptureDisabled.scenarios[0].diagnosticProtocol.captureBeforeCleanup = $false
    Assert-ManifestRejected -Name 'diagnostic-capture-disabled' -Manifest $diagnosticCaptureDisabled -ExpectedMessage 'diagnosticProtocol.captureBeforeCleanup must be true'

    $diagnosticRedactionDisabled = Copy-ManifestObject
    $diagnosticRedactionDisabled.scenarios[0].diagnosticProtocol.redactSecrets = $false
    Assert-ManifestRejected -Name 'diagnostic-redaction-disabled' -Manifest $diagnosticRedactionDisabled -ExpectedMessage 'diagnosticProtocol.redactSecrets must be true'

    $placeholderProhibitedActions = Copy-ManifestObject
    $placeholderProhibitedActions.scenarios[0].cleanupProtocol.prohibitedActions = @('placeholder')
    Assert-ManifestRejected -Name 'placeholder-prohibited-actions' -Manifest $placeholderProhibitedActions -ExpectedMessage "cleanupProtocol.prohibitedActions must contain 'broad-process-kill'"

    foreach ($prohibitedAction in @('broad-process-kill', 'unknown-database-delete', 'docker-prune', 'redis-flushall')) {
        $missingProhibitedAction = Copy-ManifestObject
        $missingProhibitedAction.scenarios[0].cleanupProtocol.prohibitedActions = @(
            $missingProhibitedAction.scenarios[0].cleanupProtocol.prohibitedActions |
                Where-Object { -not [string]::Equals([string]$_, $prohibitedAction, [StringComparison]::Ordinal) }
        )
        Assert-ManifestRejected `
            -Name "missing-prohibited-action-$prohibitedAction" `
            -Manifest $missingProhibitedAction `
            -ExpectedMessage "cleanupProtocol.prohibitedActions must contain '$prohibitedAction'"
    }

    $nonCanonicalBlockedIdentity = Copy-ManifestObject
    $nonCanonicalBlockedIdentity.scenarios[5].testProjects[0].frozenTestIdentities[0] = 'x'
    Assert-ManifestRejected -Name 'non-canonical-blocked-identity' -Manifest $nonCanonicalBlockedIdentity -ExpectedMessage 'frozen identity must be a canonical FullyQualifiedName'

    $missingScenario = Copy-ManifestObject
    $missingScenario.scenarios = @($missingScenario.scenarios | Select-Object -Skip 1)
    Assert-ManifestRejected -Name 'missing-active-scenario' -Manifest $missingScenario -ExpectedMessage 'must exactly match'

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

    Write-Output 'Acceptance scenario matrix manifest contracts passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
