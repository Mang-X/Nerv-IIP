# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates acceptance scenario manifest, selection, workflow budget, discovery, and artifact contracts with fixtures
#     - Runs a temporary fake dotnet executable through the real planner helper boundary
#   Writes:
#     - Temporary JSON and workflow fixtures plus planning artifacts under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrix.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$v1ManifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$plannerPath = Join-Path $repoRoot 'scripts/plan-acceptance-scenario-matrix.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
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

function Write-WorkflowFixture {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $StepTimeoutMinutes,
        [string] $StepRun = 'pwsh scripts/plan-acceptance-scenario-matrix.ps1',
        [string] $AdditionalJobs = ''
    )

    $path = Join-Path $fixtureRoot "$Name.yml"
    $content = @"
name: Acceptance planning fixture
on:
  workflow_dispatch:
jobs:
  acceptance-scenario-matrix-planning:
    runs-on: ubuntu-latest
    timeout-minutes: $($StepTimeoutMinutes + 5)
    steps:
      - name: Plan acceptance scenario matrix
        timeout-minutes: $StepTimeoutMinutes
        run: $StepRun
$AdditionalJobs
"@
    [IO.File]::WriteAllText($path, $content, [Text.UTF8Encoding]::new($false))
    return $path
}

function Assert-ThrowsContaining {
    param(
        [Parameter(Mandatory)] [scriptblock] $Action,
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [Parameter(Mandatory)] [string] $Context
    )

    $rejected = $false
    $observedMessage = '<no exception>'
    try { & $Action }
    catch {
        $observedMessage = $_.Exception.Message
        $rejected = $observedMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal)
    }
    Assert-Contract $rejected "$Context must fail with '$ExpectedMessage'; observed '$observedMessage'."
}

function Assert-PlanningArtifactRejected {
    param(
        [Parameter(Mandatory)] [object] $Artifact,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [object] $Selection,
        [Parameter(Mandatory)] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $ExpectedMessage,
        [Parameter(Mandatory)] [string] $Context
    )

    Assert-ThrowsContaining -ExpectedMessage $ExpectedMessage -Context $Context -Action {
        Assert-NervAcceptancePlanningArtifact `
            -Artifact $Artifact `
            -Manifest $Manifest `
            -Selection $Selection `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 `
            -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
            -ManifestDigest $ManifestDigest `
            -Event 'workflow_dispatch'
    }
}

function New-ListTestsOutput {
    param([AllowEmptyCollection()] [string[]] $Identities = @())

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('Build succeeded.')
    $lines.Add('The following Tests are available:')
    foreach ($identity in @($Identities)) { $lines.Add("    $identity") }
    return ($lines -join "`n")
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

    $pullRequestSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $manifest `
        -Event 'pull_request' `
        -ChangedPaths @('backend/services/Business/Erp/Application/Orders.cs') `
        -ImpactRulesSucceeded $true
    Assert-Contract ([string]::Equals([string]$pullRequestSelection.selectionMode, 'pull-request-impact', [StringComparison]::Ordinal)) 'Pull request selection must report impact mode.'
    Assert-Contract ([string]::Equals((@($pullRequestSelection.scenarios.id) -join '|'), 'erp-return-closure|sales-order-demand|wms-delivery-erp', [StringComparison]::Ordinal)) 'Pull request impact selection must use ordinal scenario order and include every exact matching active scenario.'

    $wrongCaseSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $manifest `
        -Event 'pull_request' `
        -ChangedPaths @('backend/services/Business/erp/Application/Orders.cs') `
        -ImpactRulesSucceeded $true
    Assert-Contract (@($wrongCaseSelection.scenarios).Count -eq 0) 'Pull request impact matching must be ordinal and reject wrong-case paths.'

    $unmatchedSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $manifest `
        -Event 'pull_request' `
        -ChangedPaths @('README.md') `
        -ImpactRulesSucceeded $true
    Assert-Contract (@($unmatchedSelection.scenarios).Count -eq 0) 'A successful pull request impact evaluation may select zero scenarios.'
    Assert-Contract ([string]::Equals((@($unmatchedSelection.reasons) -join '|'), 'no-impact', [StringComparison]::Ordinal)) 'A successful zero-scenario pull request selection must record the explicit no-impact reason.'

    foreach ($entrypointCase in @(
        @{ Path = 'scripts/verify-erp-sales-order-demand-planning.ps1'; ScenarioId = 'sales-order-demand' },
        @{ Path = 'scripts/verify-erp-wms-delivery-completion.ps1'; ScenarioId = 'wms-delivery-erp' }
    )) {
        $entrypointSelection = Select-NervAcceptanceScenarioMatrix `
            -Manifest $manifest `
            -Event 'pull_request' `
            -ChangedPaths @($entrypointCase.Path) `
            -ImpactRulesSucceeded $true
        Assert-Contract ([string]::Equals((@($entrypointSelection.scenarios.id) -join '|'), $entrypointCase.ScenarioId, [StringComparison]::Ordinal)) "Scenario entrypoint '$($entrypointCase.Path)' must select only '$($entrypointCase.ScenarioId)'."
    }

    foreach ($globalSelectionPath in @(
        'scripts/acceptance-scenario-matrix.json',
        'scripts/lib/AcceptanceScenarioMatrix.ps1',
        'scripts/plan-acceptance-scenario-matrix.ps1',
        'scripts/tests/acceptance-scenario-matrix.Tests.ps1',
        'scripts/full-chain-test-lane.json',
        'scripts/lib/FullChainTestLane.ps1',
        'scripts/run-full-chain-test-lane.ps1',
        'scripts/tests/full-chain-test-lane.Tests.ps1',
        'backend/tests/Nerv.IIP.Business.FullChain.Tests/Scenario.cs',
        'infra/aspire/Nerv.IIP.AppHost/Program.cs',
        '.github/workflows/ci.yml',
        'scripts/lib/CiImpactPlan.ps1'
    )) {
        $globalSelection = Select-NervAcceptanceScenarioMatrix `
            -Manifest $manifest `
            -Event 'pull_request' `
            -ChangedPaths @($globalSelectionPath) `
            -ImpactRulesSucceeded $true
        Assert-Contract (@($globalSelection.scenarios).Count -eq 5) "Global FullChain control '$globalSelectionPath' must conservatively select all active/core scenarios."
    }

    foreach ($invalidChangedPath in @(
        '../README.md',
        '/tmp/README.md',
        'C:/repo/README.md',
        'C:',
        'C:repo/README.md',
        'c:repo/README.md',
        'Z:foo',
        'docs/name:part.md',
        'backend//README.md',
        'backend\README.md',
        './README.md',
        'backend/./README.md',
        'backend/services/../README.md',
        'backend/'
    )) {
        $invalidPathSelection = Select-NervAcceptanceScenarioMatrix `
            -Manifest $manifest `
            -Event 'pull_request' `
            -ChangedPaths @($invalidChangedPath) `
            -ImpactRulesSucceeded $true
        Assert-Contract ([string]::Equals([string]$invalidPathSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal)) "Invalid changed path '$invalidChangedPath' must use conservative selection."
        Assert-Contract (@($invalidPathSelection.scenarios).Count -eq 5) "Invalid changed path '$invalidChangedPath' must select all active/core scenarios."
    }

    $missingEntrypointMappingManifest = Copy-JsonObject $manifest
    $missingEntrypointMappingManifest.scenarios[0].entrypoint.PSObject.Properties.Remove('path')
    $missingEntrypointMappingSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $missingEntrypointMappingManifest `
        -Event 'pull_request' `
        -ChangedPaths @('scripts/verify-erp-sales-order-demand-planning.ps1') `
        -ImpactRulesSucceeded $true
    Assert-Contract ([string]::Equals([string]$missingEntrypointMappingSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal) -and @($missingEntrypointMappingSelection.scenarios).Count -eq 5) 'A missing active script entrypoint mapping must conservatively select all active/core scenarios.'

    foreach ($entrypointRuleMutation in @(
        @{ Name = 'wrong-case-kind'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.kind = 'SCRIPT' } },
        @{ Name = 'unknown-kind'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.kind = 'container' } },
        @{ Name = 'missing-kind'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.PSObject.Properties.Remove('kind') } },
        @{ Name = 'non-string-kind'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.kind = 123 } },
        @{ Name = 'non-string-script-path'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.path = 123 } },
        @{ Name = 'non-script-entrypoint-path'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.path = 'backend/verify.ps1' } },
        @{ Name = 'wrong-script-extension'; ScenarioIndex = 0; Apply = { param($value) $value.entrypoint.path = 'scripts/verify.sh' } },
        @{ Name = 'fullstack-missing-scenario'; ScenarioIndex = 2; Apply = { param($value) $value.entrypoint.PSObject.Properties.Remove('scenario') } },
        @{ Name = 'fullstack-non-string-scenario'; ScenarioIndex = 2; Apply = { param($value) $value.entrypoint.scenario = 528 } },
        @{ Name = 'fullstack-invalid-scenario'; ScenarioIndex = 2; Apply = { param($value) $value.entrypoint.scenario = 'MAN-528' } },
        @{ Name = 'dotnet-extra-path'; ScenarioIndex = 4; Apply = { param($value) $value.entrypoint | Add-Member -NotePropertyName path -NotePropertyValue 'scripts/verify.ps1' } }
    )) {
        $malformedEntrypointManifest = Copy-JsonObject $manifest
        & $entrypointRuleMutation.Apply $malformedEntrypointManifest.scenarios[$entrypointRuleMutation.ScenarioIndex]
        $malformedEntrypointSelection = Select-NervAcceptanceScenarioMatrix `
            -Manifest $malformedEntrypointManifest `
            -Event 'pull_request' `
            -ChangedPaths @('README.md') `
            -ImpactRulesSucceeded $true
        Assert-Contract (
            [string]::Equals([string]$malformedEntrypointSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal) -and
            [string]::Equals((@($malformedEntrypointSelection.reasons) -join '|'), 'impact-rules-invalid', [StringComparison]::Ordinal) -and
            @($malformedEntrypointSelection.scenarios).Count -eq 5
        ) "Malformed active/core entrypoint '$($entrypointRuleMutation.Name)' must conservatively select all active/core scenarios with an impact-rules-invalid reason."
    }

    $wrongCaseEntrypointForEveryEventManifest = Copy-JsonObject $manifest
    $wrongCaseEntrypointForEveryEventManifest.scenarios[0].entrypoint.kind = 'SCRIPT'
    foreach ($entrypointEventCase in @(
        @{ Event = 'push'; Parameters = @{} },
        @{ Event = 'schedule'; Parameters = @{} },
        @{ Event = 'workflow_dispatch'; Parameters = @{ DispatchSelection = 'full' } }
    )) {
        $entrypointEventParameters = @{
            Manifest = $wrongCaseEntrypointForEveryEventManifest
            Event = $entrypointEventCase.Event
        }
        foreach ($parameter in $entrypointEventCase.Parameters.GetEnumerator()) {
            $entrypointEventParameters[$parameter.Key] = $parameter.Value
        }
        $malformedEntrypointEventSelection = Select-NervAcceptanceScenarioMatrix @entrypointEventParameters
        Assert-Contract (
            [string]::Equals([string]$malformedEntrypointEventSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal) -and
            [string]::Equals((@($malformedEntrypointEventSelection.reasons) -join '|'), 'impact-rules-invalid', [StringComparison]::Ordinal) -and
            @($malformedEntrypointEventSelection.scenarios).Count -eq 5
        ) "Malformed active/core entrypoint must conservatively select active/core scenarios for event '$($entrypointEventCase.Event)'."
    }

    foreach ($fullstackScenarioSuffixCase in @(
        @{ Name = 'trailing-lf'; Suffix = "`n" },
        @{ Name = 'trailing-crlf'; Suffix = "`r`n" },
        @{ Name = 'trailing-cr'; Suffix = "`r" },
        @{ Name = 'trailing-nul'; Suffix = [string][char]0 },
        @{ Name = 'trailing-unicode-line-separator'; Suffix = [string][char]0x2028 }
    )) {
        $malformedFullstackManifest = Copy-JsonObject $manifest
        $malformedFullstackManifest.scenarios[2].entrypoint.scenario = "man-528$($fullstackScenarioSuffixCase.Suffix)"
        foreach ($fullstackEventCase in @(
            @{ Event = 'pull_request'; Parameters = @{ ChangedPaths = @('README.md'); ImpactRulesSucceeded = $true } },
            @{ Event = 'push'; Parameters = @{} },
            @{ Event = 'schedule'; Parameters = @{} },
            @{ Event = 'workflow_dispatch'; Parameters = @{ DispatchSelection = 'full' } }
        )) {
            $fullstackEventParameters = @{
                Manifest = $malformedFullstackManifest
                Event = $fullstackEventCase.Event
            }
            foreach ($parameter in $fullstackEventCase.Parameters.GetEnumerator()) {
                $fullstackEventParameters[$parameter.Key] = $parameter.Value
            }
            $malformedFullstackSelection = Select-NervAcceptanceScenarioMatrix @fullstackEventParameters
            Assert-Contract (
                [string]::Equals([string]$malformedFullstackSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal) -and
                [string]::Equals((@($malformedFullstackSelection.reasons) -join '|'), 'impact-rules-invalid', [StringComparison]::Ordinal) -and
                @($malformedFullstackSelection.scenarios).Count -eq 5
            ) "Fullstack scenario mutation '$($fullstackScenarioSuffixCase.Name)' must conservatively select active/core scenarios for event '$($fullstackEventCase.Event)'."
        }
    }

    $malformedImpactRulesManifest = Copy-JsonObject $manifest
    $malformedImpactRulesManifest.scenarios[0].impact.paths = @()
    $malformedImpactRulesSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $malformedImpactRulesManifest `
        -Event 'pull_request' `
        -ChangedPaths @('backend/services/Business/Erp/Application/Orders.cs') `
        -ImpactRulesSucceeded $true
    Assert-Contract ([string]::Equals([string]$malformedImpactRulesSelection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal) -and @($malformedImpactRulesSelection.scenarios).Count -eq 5) 'Malformed active impact rules must conservatively select all active/core scenarios.'

    foreach ($conservativeCase in @(
        @{ Name = 'rules-failed'; Paths = @('README.md'); RulesSucceeded = $false },
        @{ Name = 'paths-missing'; Paths = @(); RulesSucceeded = $true }
    )) {
        $selection = Select-NervAcceptanceScenarioMatrix `
            -Manifest $manifest `
            -Event 'pull_request' `
            -ChangedPaths $conservativeCase.Paths `
            -ImpactRulesSucceeded $conservativeCase.RulesSucceeded
        Assert-Contract ([string]::Equals([string]$selection.selectionMode, 'conservative-active-core', [StringComparison]::Ordinal)) "Pull request $($conservativeCase.Name) must fail open to conservative active/core selection."
        Assert-Contract (@($selection.scenarios).Count -eq 5) "Pull request $($conservativeCase.Name) must select all five active/core scenarios."
    }

    $mainSelection = Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event 'push'
    Assert-Contract ([string]::Equals([string]$mainSelection.selectionMode, 'main-active-core', [StringComparison]::Ordinal) -and @($mainSelection.scenarios).Count -eq 5) 'Main push must select all active/core scenarios.'

    $nightlySelection = Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event 'schedule'
    Assert-Contract ([string]::Equals([string]$nightlySelection.selectionMode, 'nightly-active', [StringComparison]::Ordinal) -and @($nightlySelection.scenarios).Count -eq 5) 'Nightly must select every active scenario.'

    foreach ($wrongCaseEvent in @('PULL_REQUEST', 'PUSH', 'SCHEDULE', 'WORKFLOW_DISPATCH')) {
        Assert-ThrowsContaining -ExpectedMessage 'Planning event' -Context "Wrong-case selector event '$wrongCaseEvent'" -Action {
            Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event $wrongCaseEvent -ChangedPaths @('README.md') -DispatchSelection 'full' | Out-Null
        }
    }

    foreach ($dispatchAll in @('lane', 'full')) {
        $dispatchSelection = Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event 'workflow_dispatch' -DispatchSelection $dispatchAll
        Assert-Contract (@($dispatchSelection.scenarios).Count -eq 5) "workflow_dispatch '$dispatchAll' must select every active scenario."
    }
    $dispatchSingle = Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event 'workflow_dispatch' -DispatchSelection 'mes-produced-lot-inventory'
    Assert-Contract ([string]::Equals((@($dispatchSingle.scenarios.id) -join '|'), 'mes-produced-lot-inventory', [StringComparison]::Ordinal)) 'workflow_dispatch must support one named active scenario.'
    Assert-ThrowsContaining -ExpectedMessage 'is not active' -Context 'Blocked workflow_dispatch selection' -Action {
        Select-NervAcceptanceScenarioMatrix -Manifest $manifest -Event 'workflow_dispatch' -DispatchSelection 'equipment-unavailable-scheduling-mes' | Out-Null
    }
    $deferredManifest = Copy-JsonObject $manifest
    $deferredManifest.scenarios[5].status = 'deferred'
    Assert-ThrowsContaining -ExpectedMessage 'is not active' -Context 'Deferred workflow_dispatch selection' -Action {
        Select-NervAcceptanceScenarioMatrix -Manifest $deferredManifest -Event 'workflow_dispatch' -DispatchSelection 'equipment-unavailable-scheduling-mes' | Out-Null
    }

    $planningManifest = Copy-JsonObject $manifest
    $planningManifest.scenarios[1].testProjects[0].path = 'backend/tests/Nerv.IIP.Second.FullChain.Tests/Nerv.IIP.Second.FullChain.Tests.csproj'
    $planningSelection = Select-NervAcceptanceScenarioMatrix -Manifest $planningManifest -Event 'workflow_dispatch' -DispatchSelection 'full'
    $planningProjects = @(Get-NervAcceptancePlanningProjects -Scenarios $planningSelection.scenarios)
    Assert-Contract ($planningProjects.Count -eq 2) 'Planning must group selected scenarios into two ordinal-unique projects.'
    Assert-Contract ([string]::Equals([string]$planningProjects[0].path, 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj', [StringComparison]::Ordinal)) 'Planning projects must be ordinal-sorted.'
    Assert-Contract (@($planningProjects[0].expectedTestIdentities).Count -eq 4 -and @($planningProjects[1].expectedTestIdentities).Count -eq 1) 'Planning must aggregate each selected identity into exactly one project.'

    $planningWorkflowPath = Write-WorkflowFixture -Name 'planning-workflow' -StepTimeoutMinutes 60
    $directPlanningWorkflowPath = Write-WorkflowFixture `
        -Name 'planning-workflow-direct-pwsh-shell' `
        -StepTimeoutMinutes 60 `
        -StepRun './scripts/plan-acceptance-scenario-matrix.ps1 -Event push'
    $shortPlanningWorkflowPath = Write-WorkflowFixture -Name 'planning-workflow-short' -StepTimeoutMinutes 46
    $runDriftWorkflowPath = Write-WorkflowFixture `
        -Name 'planning-workflow-run-drift' `
        -StepTimeoutMinutes 60 `
        -StepRun 'pwsh scripts/not-the-acceptance-planner.ps1'
    $unrelatedNamedStep = @"
  unrelated-job:
    runs-on: ubuntu-latest
    timeout-minutes: 65
    steps:
      - name: Plan acceptance scenario matrix
        timeout-minutes: 60
        run: pwsh scripts/plan-acceptance-scenario-matrix.ps1
"@
    $unrelatedSameNameWorkflowPath = Write-WorkflowFixture `
        -Name 'planning-workflow-unrelated-same-name' `
        -StepTimeoutMinutes 60 `
        -StepRun 'echo not-the-planner' `
        -AdditionalJobs $unrelatedNamedStep
    $unrelatedFixtureJobs = Get-NervCiWorkflowBudgets -Path $unrelatedSameNameWorkflowPath
    Assert-Contract ([string]::Equals((@($unrelatedFixtureJobs.Name) -join '|'), 'acceptance-scenario-matrix-planning|unrelated-job', [StringComparison]::Ordinal)) 'The unrelated-step fixture must contain two distinct workflow jobs.'
    $workflowBudget = Get-NervAcceptancePlanningWorkflowBudget `
        -WorkflowPath $planningWorkflowPath `
        -JobName 'acceptance-scenario-matrix-planning' `
        -StepName 'Plan acceptance scenario matrix'
    Assert-Contract ($workflowBudget.stepTimeoutSeconds -eq 3600) 'Planning must derive the actual step timeout from the supplied workflow.'
    $directWorkflowBudget = Get-NervAcceptancePlanningWorkflowBudget `
        -WorkflowPath $directPlanningWorkflowPath `
        -JobName 'acceptance-scenario-matrix-planning' `
        -StepName 'Plan acceptance scenario matrix'
    Assert-Contract ($directWorkflowBudget.stepTimeoutSeconds -eq 3600) 'Planning must accept a direct governed planner invocation inside the workflow pwsh shell.'
    foreach ($workflowRunMutation in @(
        @{ Name = 'run-drift'; Path = $runDriftWorkflowPath },
        @{ Name = 'unrelated-same-name'; Path = $unrelatedSameNameWorkflowPath }
    )) {
        Assert-ThrowsContaining -ExpectedMessage 'must invoke scripts/plan-acceptance-scenario-matrix.ps1' -Context "Workflow $($workflowRunMutation.Name) mutation" -Action {
            Get-NervAcceptancePlanningWorkflowBudget `
                -WorkflowPath $workflowRunMutation.Path `
                -JobName 'acceptance-scenario-matrix-planning' `
                -StepName 'Plan acceptance scenario matrix' | Out-Null
        }
    }
    $requiredBudgetSeconds = Assert-NervAcceptancePlanningBudgetFits `
        -PlanningBudget $planningManifest.planningBudget `
        -UniqueProjectCount 2 `
        -StepTimeoutSeconds $workflowBudget.stepTimeoutSeconds
    Assert-Contract ($requiredBudgetSeconds -eq 2760) 'Planning budget must use the approved checked arithmetic formula.'
    $shortWorkflowBudget = Get-NervAcceptancePlanningWorkflowBudget `
        -WorkflowPath $shortPlanningWorkflowPath `
        -JobName 'acceptance-scenario-matrix-planning' `
        -StepName 'Plan acceptance scenario matrix'
    Assert-ThrowsContaining -ExpectedMessage 'must be strictly less than' -Context 'Shortened planning workflow budget' -Action {
        Assert-NervAcceptancePlanningBudgetFits -PlanningBudget $planningManifest.planningBudget -UniqueProjectCount 2 -StepTimeoutSeconds $shortWorkflowBudget.stepTimeoutSeconds | Out-Null
    }
    $currentWorkflowBudget = Get-NervAcceptancePlanningWorkflowBudget `
        -WorkflowPath $workflowPath `
        -JobName 'acceptance-scenario-matrix-planning' `
        -StepName 'Plan acceptance scenario matrix'
    Assert-Contract ($currentWorkflowBudget.stepTimeoutSeconds -eq 1800) 'The hosted planning workflow must expose its governed 30-minute production budget.'

    $projectExpected = @($planningProjects[0].expectedTestIdentities)
    $closedDiscovery = Assert-NervAcceptanceDiscoveryClosure `
        -ProjectPath ([string]$planningProjects[0].path) `
        -ExpectedTestIdentities $projectExpected `
        -DiscoveryOutput (New-ListTestsOutput -Identities $projectExpected)
    Assert-Contract ([string]::Equals((@($closedDiscovery) -join '|'), ($projectExpected -join '|'), [StringComparison]::Ordinal)) 'Discovery closure must return the exact ordinal identity set.'
    $tabIndentedDiscovery = Assert-NervAcceptanceDiscoveryClosure `
        -ProjectPath ([string]$planningProjects[0].path) `
        -ExpectedTestIdentities $projectExpected `
        -DiscoveryOutput ("The following Tests are available:`n" + (@($projectExpected | ForEach-Object { "`t$_" }) -join "`n"))
    Assert-Contract ([string]::Equals((@($tabIndentedDiscovery) -join '|'), ($projectExpected -join '|'), [StringComparison]::Ordinal)) 'Discovery closure must accept a continuous real list with tab-indented identities.'
    $buildLogIdentityLeak = ($projectExpected -join "`n") + "`nThe following Tests are available:`n"
    Assert-ThrowsContaining -ExpectedMessage 'identity set does not exactly equal' -Context 'Build-log identity leak with empty test list' -Action {
        Assert-NervAcceptanceDiscoveryClosure `
            -ProjectPath ([string]$planningProjects[0].path) `
            -ExpectedTestIdentities $projectExpected `
            -DiscoveryOutput $buildLogIdentityLeak | Out-Null
    }
    foreach ($listTerminatorMutation in @(
        @{ Name = 'blank-line'; Terminator = '' },
        @{ Name = 'build-summary'; Terminator = 'Build succeeded.' }
    )) {
        $postTerminatorIdentities = @($projectExpected | ForEach-Object { "    $_" }) -join "`n"
        $terminatedListOutput = "The following Tests are available:`n$($listTerminatorMutation.Terminator)`n$postTerminatorIdentities"
        Assert-ThrowsContaining -ExpectedMessage 'identity set does not exactly equal' -Context "Discovery $($listTerminatorMutation.Name) terminator mutation" -Action {
            Assert-NervAcceptanceDiscoveryClosure `
                -ProjectPath ([string]$planningProjects[0].path) `
                -ExpectedTestIdentities $projectExpected `
                -DiscoveryOutput $terminatedListOutput | Out-Null
        }
    }
    foreach ($discoveryMutation in @(
        @{ Name = 'zero'; Output = (New-ListTestsOutput); Message = 'identity set does not exactly equal' },
        @{ Name = 'missing'; Output = (New-ListTestsOutput -Identities @($projectExpected | Select-Object -Skip 1)); Message = 'identity set does not exactly equal' },
        @{ Name = 'extra'; Output = (New-ListTestsOutput -Identities @($projectExpected + 'Nerv.IIP.Extra.Tests.Unregistered_test')); Message = 'identity set does not exactly equal' },
        @{ Name = 'duplicate'; Output = (New-ListTestsOutput -Identities @($projectExpected + $projectExpected[0])); Message = 'duplicate discovered identity' }
    )) {
        Assert-ThrowsContaining -ExpectedMessage $discoveryMutation.Message -Context "Discovery $($discoveryMutation.Name) mutation" -Action {
            Assert-NervAcceptanceDiscoveryClosure `
                -ProjectPath ([string]$planningProjects[0].path) `
                -ExpectedTestIdentities $projectExpected `
                -DiscoveryOutput $discoveryMutation.Output | Out-Null
        }
    }

    $manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $manifestPath
    Assert-Contract ($manifestDigest -cmatch '^[0-9a-f]{64}$') 'Manifest digest must be a lowercase SHA-256 hex string.'

    $noImpactSelection = Select-NervAcceptanceScenarioMatrix `
        -Manifest $manifest `
        -Event 'pull_request' `
        -ChangedPaths @('README.md') `
        -ImpactRulesSucceeded $true
    Assert-Contract ([string]::Equals([string]$noImpactSelection.selectionMode, 'pull-request-impact', [StringComparison]::Ordinal)) 'A no-impact pull request selection must report pull-request-impact mode.'
    Assert-Contract ([string]::Equals((@($noImpactSelection.reasons) -join '|'), 'no-impact', [StringComparison]::Ordinal)) 'A no-impact pull request selection must record the no-impact reason.'
    Assert-Contract (@($noImpactSelection.scenarios).Count -eq 0) 'A no-impact pull request selection must select zero scenarios.'
    $noImpactProjects = @(Get-NervAcceptancePlanningProjects -Scenarios $noImpactSelection.scenarios)
    Assert-Contract ($noImpactProjects.Count -eq 0) 'A no-impact selection must produce zero planning projects.'
    $noImpactCalls = [Collections.Generic.List[object]]::new()
    $noImpactCommandAction = {
        param([string] $Operation, [string] $ProjectPath, [string[]] $Arguments, [int] $TimeoutSeconds)

        $noImpactCalls.Add([pscustomobject]@{ operation = $Operation; projectPath = $ProjectPath })
        return [pscustomobject]@{ Stdout = ''; Stderr = ''; ExitCode = 0 }
    }
    $noImpactArtifactPath = Join-Path $fixtureRoot 'artifacts/no-impact-planning.json'
    $noImpactArtifact = Invoke-NervAcceptanceScenarioMatrixPlanning `
        -Manifest $manifest `
        -Selection $noImpactSelection `
        -RepositoryRoot $repoRoot `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt 2 `
        -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
        -ManifestDigest $manifestDigest `
        -Event 'pull_request' `
        -WorkflowPath $planningWorkflowPath `
        -WorkflowJobName 'acceptance-scenario-matrix-planning' `
        -WorkflowStepName 'Plan acceptance scenario matrix' `
        -ArtifactPath $noImpactArtifactPath `
        -ProjectCommandAction $noImpactCommandAction
    Assert-Contract (@($noImpactCalls).Count -eq 0) 'No-impact planning must not invoke any project command.'
    Assert-Contract (@($noImpactCalls | Where-Object { [string]::Equals([string]$_.operation, 'restore', [StringComparison]::Ordinal) }).Count -eq 0) 'No-impact planning must not restore any project.'
    Assert-Contract (@($noImpactCalls | Where-Object { [string]::Equals([string]$_.operation, 'discovery', [StringComparison]::Ordinal) }).Count -eq 0) 'No-impact planning must not discover any project.'
    Assert-Contract (Test-Path -LiteralPath $noImpactArtifactPath -PathType Leaf) 'No-impact planning must write the declared zero-project artifact.'
    $persistedNoImpactArtifact = Get-Content -LiteralPath $noImpactArtifactPath -Raw | ConvertFrom-Json -Depth 50
    Assert-NervAcceptancePlanningArtifact `
        -Artifact $persistedNoImpactArtifact `
        -Manifest $manifest `
        -Selection $noImpactSelection `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt 2 `
        -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
        -ManifestDigest $manifestDigest `
        -Event 'pull_request' | Out-Null
    Assert-Contract ([string]::Equals((@($persistedNoImpactArtifact.selectionReasons) -join '|'), 'no-impact', [StringComparison]::Ordinal)) 'No-impact artifact selectionReasons must record no-impact.'
    Assert-Contract (@($persistedNoImpactArtifact.scenarios).Count -eq 0) 'No-impact artifact must contain an empty scenarios array.'
    Assert-Contract (@($persistedNoImpactArtifact.projects).Count -eq 0) 'No-impact artifact must contain an empty projects array.'

    $artifactPath = Join-Path $fixtureRoot 'artifacts/planning.json'
    $calls = [Collections.Generic.List[object]]::new()
    $projectCommandAction = {
        param([string] $Operation, [string] $ProjectPath, [string[]] $Arguments, [int] $TimeoutSeconds)

        $calls.Add([pscustomobject]@{ operation = $Operation; projectPath = $ProjectPath; arguments = @($Arguments); timeoutSeconds = $TimeoutSeconds })
        if ([string]::Equals($Operation, 'discovery', [StringComparison]::Ordinal)) {
            $project = @($planningProjects | Where-Object { [string]::Equals([string]$_.path, $ProjectPath, [StringComparison]::Ordinal) })[0]
            return [pscustomobject]@{ Stdout = (New-ListTestsOutput -Identities @($project.expectedTestIdentities)); Stderr = ''; ExitCode = 0 }
        }
        return [pscustomobject]@{ Stdout = ''; Stderr = ''; ExitCode = 0 }
    }

    $artifact = Invoke-NervAcceptanceScenarioMatrixPlanning `
        -Manifest $planningManifest `
        -Selection $planningSelection `
        -RepositoryRoot $repoRoot `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt 2 `
        -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
        -ManifestDigest $manifestDigest `
        -Event 'workflow_dispatch' `
        -WorkflowPath $planningWorkflowPath `
        -WorkflowJobName 'acceptance-scenario-matrix-planning' `
        -WorkflowStepName 'Plan acceptance scenario matrix' `
        -ArtifactPath $artifactPath `
        -ProjectCommandAction $projectCommandAction
    Assert-Contract (@($calls | Where-Object { [string]::Equals([string]$_.operation, 'restore', [StringComparison]::Ordinal) }).Count -eq 2) 'Planning must restore each unique project exactly once.'
    Assert-Contract (@($calls | Where-Object { [string]::Equals([string]$_.operation, 'discovery', [StringComparison]::Ordinal) }).Count -eq 2) 'Planning must discover each unique project exactly once.'
    foreach ($restoreCall in @($calls | Where-Object { [string]::Equals([string]$_.operation, 'restore', [StringComparison]::Ordinal) })) {
        Assert-Contract ([string]::Equals(($restoreCall.arguments -join '|'), "restore|$($restoreCall.projectPath)", [StringComparison]::Ordinal)) 'Restore must receive only the exact selected project path.'
    }
    foreach ($discoveryCall in @($calls | Where-Object { [string]::Equals([string]$_.operation, 'discovery', [StringComparison]::Ordinal) })) {
        $expectedProject = @($planningProjects | Where-Object { [string]::Equals([string]$_.path, [string]$discoveryCall.projectPath, [StringComparison]::Ordinal) })[0]
        $expectedFilter = (@($expectedProject.expectedTestIdentities | ForEach-Object { "FullyQualifiedName=$_" }) -join '|')
        Assert-Contract ([string]::Equals(($discoveryCall.arguments -join '|'), "test|$($discoveryCall.projectPath)|--configuration|Release|--no-restore|--list-tests|--filter|$expectedFilter", [StringComparison]::Ordinal)) 'Discovery must use Release --no-restore --list-tests and one exact selected-identity filter.'
    }
    Assert-Contract (Test-Path -LiteralPath $artifactPath -PathType Leaf) 'Successful planning must write the declared artifact.'
    $artifactBytes = [IO.File]::ReadAllBytes($artifactPath)
    Assert-Contract (-not ($artifactBytes.Length -ge 3 -and $artifactBytes[0] -eq 0xEF -and $artifactBytes[1] -eq 0xBB -and $artifactBytes[2] -eq 0xBF)) 'Planning artifact must be UTF-8 without BOM.'
    $persistedArtifact = Get-Content -LiteralPath $artifactPath -Raw | ConvertFrom-Json -Depth 50
    Assert-NervAcceptancePlanningArtifact `
        -Artifact $persistedArtifact `
        -Manifest $planningManifest `
        -Selection $planningSelection `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt 2 `
        -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
        -ManifestDigest $manifestDigest `
        -Event 'workflow_dispatch' | Out-Null
    Assert-Contract ([string]::Equals((@($persistedArtifact.projects.path) -join '|'), (@($planningProjects.path) -join '|'), [StringComparison]::Ordinal)) 'Artifact projects must retain stable ordinal ordering.'

    foreach ($artifactMutation in @(
        @{ Name = 'unknown-field'; Apply = { param($value) $value | Add-Member -NotePropertyName extra -NotePropertyValue $true }; Message = 'unknown field' },
        @{ Name = 'missing-field'; Apply = { param($value) $value.PSObject.Properties.Remove('runId') }; Message = 'missing required field' },
        @{ Name = 'run-native-number'; Apply = { param($value) $value.runId = [int64]123456789 }; Message = 'runId must be a JSON string' },
        @{ Name = 'repository-native-type'; Apply = { param($value) $value.repository = 123 }; Message = 'repository must be a JSON string' },
        @{ Name = 'sha-native-type'; Apply = { param($value) $value.testedSha = 123 }; Message = 'testedSha must be a JSON string' },
        @{ Name = 'attempt-native-string'; Apply = { param($value) $value.runAttempt = '2' }; Message = 'runAttempt must be a JSON integer' },
        @{ Name = 'manifest-path-native-type'; Apply = { param($value) $value.manifestPath = 123 }; Message = 'manifestPath must be a JSON string' },
        @{ Name = 'digest-native-type'; Apply = { param($value) $value.manifestDigest = 123 }; Message = 'manifestDigest must be a JSON string' },
        @{ Name = 'event-native-type'; Apply = { param($value) $value.event = 123 }; Message = 'event must be a JSON string' },
        @{ Name = 'selection-mode-native-type'; Apply = { param($value) $value.selectionMode = 123 }; Message = 'selectionMode must be a JSON string' },
        @{ Name = 'sha'; Apply = { param($value) $value.testedSha = '1123456789abcdef0123456789abcdef01234567' }; Message = 'testedSha does not match' },
        @{ Name = 'run'; Apply = { param($value) $value.runId = '987654321' }; Message = 'runId does not match' },
        @{ Name = 'attempt'; Apply = { param($value) $value.runAttempt = 3 }; Message = 'runAttempt does not match' },
        @{ Name = 'digest'; Apply = { param($value) $value.manifestDigest = ('f' * 64) }; Message = 'manifestDigest does not match' },
        @{ Name = 'scenario'; Apply = { param($value) $value.scenarios[0].id = 'drifted-scenario' }; Message = 'scenario set does not exactly equal' },
        @{ Name = 'project'; Apply = { param($value) $value.projects[0].path = 'backend/tests/Drifted/Drifted.csproj' }; Message = 'project set does not exactly equal' },
        @{ Name = 'identity'; Apply = { param($value) $value.projects[0].discoveredTestIdentities[0] = 'Nerv.IIP.Drifted.Tests.Drifted' }; Message = 'discovered identities do not exactly equal' },
        @{ Name = 'non-active'; Apply = { param($value) $value.scenarios[0].status = 'blocked' }; Message = 'must record only active scenarios' }
    )) {
        $mutatedArtifact = Copy-JsonObject $persistedArtifact
        & $artifactMutation.Apply $mutatedArtifact
        Assert-PlanningArtifactRejected `
            -Artifact $mutatedArtifact `
            -Manifest $planningManifest `
            -Selection $planningSelection `
            -ManifestDigest $manifestDigest `
            -ExpectedMessage $artifactMutation.Message `
            -Context "Artifact $($artifactMutation.Name) mutation"
    }

    foreach ($invalidSameValueMutation in @(
        @{ Name = 'repository'; Property = 'repository'; Value = 'invalid'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'repository-parent-segment'; Property = 'repository'; Value = '../repo'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'repository-current-segment'; Property = 'repository'; Value = './repo'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'repository-leading-hyphen'; Property = 'repository'; Value = '-owner/repo'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'repository-trailing-hyphen'; Property = 'repository'; Value = 'owner/repo-'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'repository-empty-segment'; Property = 'repository'; Value = 'owner//repo'; ExpectedName = 'Repository'; Message = 'Planning repository' },
        @{ Name = 'tested-sha'; Property = 'testedSha'; Value = 'invalid'; ExpectedName = 'TestedSha'; Message = 'Planning testedSha' },
        @{ Name = 'run-id'; Property = 'runId'; Value = '0'; ExpectedName = 'RunId'; Message = 'Planning runId' },
        @{ Name = 'run-attempt'; Property = 'runAttempt'; Value = 0; ExpectedName = 'RunAttempt'; Message = 'Planning runAttempt' },
        @{ Name = 'manifest-path'; Property = 'manifestPath'; Value = '../matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-terminal-parent-segment'; Property = 'manifestPath'; Value = 'a/..'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-current-segment'; Property = 'manifestPath'; Value = 'a/./b'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-drive-absolute'; Property = 'manifestPath'; Value = 'C:/repo/matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-drive-designator'; Property = 'manifestPath'; Value = 'C:'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-drive-relative-uppercase'; Property = 'manifestPath'; Value = 'C:repo/matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-drive-relative-lowercase'; Property = 'manifestPath'; Value = 'c:repo/matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-drive-relative-other'; Property = 'manifestPath'; Value = 'Z:foo'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-colon-segment'; Property = 'manifestPath'; Value = 'scripts/name:part.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-empty-segment'; Property = 'manifestPath'; Value = 'scripts//matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-backslash'; Property = 'manifestPath'; Value = 'scripts\matrix.json'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-path-trailing-slash'; Property = 'manifestPath'; Value = 'scripts/matrix.json/'; ExpectedName = 'ManifestPath'; Message = 'normalized and repository-relative' },
        @{ Name = 'manifest-digest'; Property = 'manifestDigest'; Value = 'invalid'; ExpectedName = 'ManifestDigest'; Message = 'Planning manifestDigest' },
        @{ Name = 'event'; Property = 'event'; Value = 'PUSH'; ExpectedName = 'Event'; Message = 'Planning event' }
    )) {
        $invalidSameArtifact = Copy-JsonObject $persistedArtifact
        $invalidSameArtifact.PSObject.Properties[$invalidSameValueMutation.Property].Value = $invalidSameValueMutation.Value
        $invalidSameArguments = @{
            Artifact = $invalidSameArtifact
            Manifest = $planningManifest
            Selection = $planningSelection
            Repository = 'Mang-X/Nerv-IIP'
            TestedSha = '0123456789abcdef0123456789abcdef01234567'
            RunId = '123456789'
            RunAttempt = 2
            ManifestPath = 'scripts/acceptance-scenario-matrix.json'
            ManifestDigest = $manifestDigest
            Event = 'workflow_dispatch'
        }
        $invalidSameArguments[$invalidSameValueMutation.ExpectedName] = $invalidSameValueMutation.Value
        Assert-ThrowsContaining -ExpectedMessage $invalidSameValueMutation.Message -Context "Artifact and expected $($invalidSameValueMutation.Name) invalid-same mutation" -Action {
            Assert-NervAcceptancePlanningArtifact @invalidSameArguments | Out-Null
        }
    }

    $invalidSelectionModeArtifact = Copy-JsonObject $persistedArtifact
    $invalidSelectionModeArtifact.selectionMode = 'invalid-mode'
    $invalidSelectionModeSelection = Copy-JsonObject $planningSelection
    $invalidSelectionModeSelection.selectionMode = 'invalid-mode'
    Assert-ThrowsContaining -ExpectedMessage 'selectionMode is invalid' -Context 'Artifact and selection invalid-same selectionMode mutation' -Action {
        Assert-NervAcceptancePlanningArtifact `
            -Artifact $invalidSelectionModeArtifact `
            -Manifest $planningManifest `
            -Selection $invalidSelectionModeSelection `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 `
            -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
            -ManifestDigest $manifestDigest `
            -Event 'workflow_dispatch' | Out-Null
    }

    $nonActiveSelection = Copy-JsonObject $planningSelection
    $nonActiveSelection.scenarios[0] = Copy-JsonObject $planningManifest.scenarios[5]
    $nonActiveArtifact = Copy-JsonObject $persistedArtifact
    $nonActiveArtifact.scenarios[0].id = [string]$nonActiveSelection.scenarios[0].id
    Assert-ThrowsContaining -ExpectedMessage 'selection must contain only active scenarios' -Context 'Non-active scenario selection' -Action {
        Assert-NervAcceptancePlanningArtifact `
            -Artifact $nonActiveArtifact `
            -Manifest $planningManifest `
            -Selection $nonActiveSelection `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 `
            -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
            -ManifestDigest $manifestDigest `
            -Event 'workflow_dispatch' | Out-Null
    }

    [IO.File]::WriteAllText($artifactPath, "stale-success`n", [Text.UTF8Encoding]::new($false))
    $failingProjectAction = {
        param([string] $Operation, [string] $ProjectPath, [string[]] $Arguments, [int] $TimeoutSeconds)
        if ([string]::Equals($Operation, 'discovery', [StringComparison]::Ordinal)) {
            return [pscustomobject]@{ Stdout = ''; Stderr = ''; ExitCode = 0 }
        }
        return [pscustomobject]@{ Stdout = ''; Stderr = ''; ExitCode = 0 }
    }
    Assert-ThrowsContaining -ExpectedMessage 'identity set does not exactly equal' -Context 'Failed planning discovery' -Action {
        Invoke-NervAcceptanceScenarioMatrixPlanning `
            -Manifest $planningManifest `
            -Selection $planningSelection `
            -RepositoryRoot $repoRoot `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 `
            -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
            -ManifestDigest $manifestDigest `
            -Event 'workflow_dispatch' `
            -WorkflowPath $planningWorkflowPath `
            -WorkflowJobName 'acceptance-scenario-matrix-planning' `
            -WorkflowStepName 'Plan acceptance scenario matrix' `
            -ArtifactPath $artifactPath `
            -ProjectCommandAction $failingProjectAction | Out-Null
    }
    Assert-Contract (-not (Test-Path -LiteralPath $artifactPath)) 'Failed planning must remove any stale or partial success artifact.'

    Assert-Contract (Test-Path -LiteralPath $plannerPath -PathType Leaf) 'The planning entrypoint must exist.'
    $fakeBin = Join-Path $fixtureRoot 'fake-bin'
    [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
    $fakeCommandLog = Join-Path $fixtureRoot 'fake-dotnet-commands.log'
    $fakeEnvironmentLog = Join-Path $fixtureRoot 'fake-dotnet-environment.log'
    $activeIdentities = @($manifest.scenarios | Where-Object {
        [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal)
    } | ForEach-Object { $_.testProjects.frozenTestIdentities })
    if ($IsWindows) {
        $fakeDotnetPath = Join-Path $fakeBin 'dotnet.cmd'
        $identityCommands = ((@('  echo The following Tests are available:') + @($activeIdentities | ForEach-Object { "  echo     $_" })) -join "`r`n")
        $fakeDotnetContent = @"
@echo off
>>"%NERV_ACCEPTANCE_FAKE_COMMAND_LOG%" echo %*
>>"%NERV_ACCEPTANCE_FAKE_ENVIRONMENT_LOG%" echo %MSBUILDDISABLENODEREUSE%^|%DOTNET_CLI_USE_MSBUILD_SERVER%
echo %* | findstr /C:"--list-tests" >nul
if not errorlevel 1 (
$identityCommands
)
exit /b 0
"@
        [IO.File]::WriteAllText($fakeDotnetPath, $fakeDotnetContent, [Text.ASCIIEncoding]::new())
    }
    else {
        $fakeDotnetPath = Join-Path $fakeBin 'dotnet'
        $identityCommands = ((@("    printf '%s\n' 'The following Tests are available:'") + @($activeIdentities | ForEach-Object { "    printf '%s\n' '    $_'" })) -join "`n")
        $fakeDotnetContent = @"
#!/bin/sh
printf '%s\n' "`$*" >> "`$NERV_ACCEPTANCE_FAKE_COMMAND_LOG"
printf '%s|%s\n' "`$MSBUILDDISABLENODEREUSE" "`$DOTNET_CLI_USE_MSBUILD_SERVER" >> "`$NERV_ACCEPTANCE_FAKE_ENVIRONMENT_LOG"
case " `$* " in
  *" --list-tests "*)
$identityCommands
    ;;
esac
exit 0
"@
        [IO.File]::WriteAllText($fakeDotnetPath, $fakeDotnetContent, [Text.UTF8Encoding]::new($false))
        [IO.File]::SetUnixFileMode(
            $fakeDotnetPath,
            [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute -bor
                [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupExecute -bor
                [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherExecute)
    }
    $entrySuccessArtifactPath = Join-Path $fixtureRoot 'entry-success/planning.json'
    $savedPath = [Environment]::GetEnvironmentVariable('PATH')
    $savedNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')
    $savedBuildServer = [Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER')
    $savedFakeCommandLog = [Environment]::GetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_COMMAND_LOG')
    $savedFakeEnvironmentLog = [Environment]::GetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_ENVIRONMENT_LOG')
    try {
        [Environment]::SetEnvironmentVariable('PATH', "$fakeBin$([IO.Path]::PathSeparator)$savedPath")
        [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', 'sentinel-node-reuse')
        [Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', 'sentinel-build-server')
        [Environment]::SetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_COMMAND_LOG', $fakeCommandLog)
        [Environment]::SetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_ENVIRONMENT_LOG', $fakeEnvironmentLog)
        & $plannerPath `
            -ManifestPath $manifestPath `
            -V1ManifestPath $v1ManifestPath `
            -WorkflowPath (Write-WorkflowFixture -Name 'entry-success-workflow' -StepTimeoutMinutes 30) `
            -WorkflowJobName 'acceptance-scenario-matrix-planning' `
            -WorkflowStepName 'Plan acceptance scenario matrix' `
            -ArtifactPath $entrySuccessArtifactPath `
            -Event 'workflow_dispatch' `
            -DispatchSelection 'full' `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 6>$null | Out-Null
        Assert-Contract ([string]::Equals([Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE'), 'sentinel-node-reuse', [StringComparison]::Ordinal)) 'Planner must restore the prior MSBuild node-reuse environment value.'
        Assert-Contract ([string]::Equals([Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER'), 'sentinel-build-server', [StringComparison]::Ordinal)) 'Planner must restore the prior dotnet build-server environment value.'

        $fakeCommands = [IO.File]::ReadAllLines($fakeCommandLog)
        Assert-Contract ($fakeCommands.Count -eq 2) 'The real planner entrypoint must execute exactly one restore and one discovery for the shared selected project.'
        Assert-Contract ($fakeCommands[0].StartsWith('restore backend/tests/Nerv.IIP.Business.FullChain.Tests/', [StringComparison]::Ordinal)) 'The planner entrypoint must execute restore first.'
        Assert-Contract ($fakeCommands[1].Contains('--configuration Release --no-restore --list-tests --filter', [StringComparison]::Ordinal)) 'The planner entrypoint must execute only governed Release discovery after restore.'
        $fakeEnvironment = [IO.File]::ReadAllLines($fakeEnvironmentLog)
        Assert-Contract ($fakeEnvironment.Count -eq 2 -and @($fakeEnvironment | Where-Object { -not [string]::Equals([string]$_, '1|0', [StringComparison]::Ordinal) }).Count -eq 0) 'Both dotnet commands must observe disabled MSBuild node reuse and persistent build server.'
        Assert-Contract (Test-Path -LiteralPath $entrySuccessArtifactPath -PathType Leaf) 'The planner entrypoint must write a success artifact only after fixture discovery closes.'

        [IO.File]::WriteAllText($fakeCommandLog, '', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($fakeEnvironmentLog, '', [Text.UTF8Encoding]::new($false))
        [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', 'failure-sentinel-node-reuse')
        [Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', 'failure-sentinel-build-server')
        $entryArtifactPath = Join-Path $fixtureRoot 'entry/planning.json'
        [IO.Directory]::CreateDirectory((Split-Path -Parent $entryArtifactPath)) | Out-Null
        [IO.File]::WriteAllText($entryArtifactPath, "stale-success`n", [Text.UTF8Encoding]::new($false))
        & $plannerPath `
            -ManifestPath $manifestPath `
            -V1ManifestPath $v1ManifestPath `
            -WorkflowJobName 'acceptance-scenario-matrix-planning' `
            -WorkflowStepName 'Plan acceptance scenario matrix' `
            -ArtifactPath $entryArtifactPath `
            -Event 'workflow_dispatch' `
            -DispatchSelection 'full' `
            -Repository 'Mang-X/Nerv-IIP' `
            -TestedSha '0123456789abcdef0123456789abcdef01234567' `
            -RunId '123456789' `
            -RunAttempt 2 6>$null | Out-Null
        Assert-Contract (Test-Path -LiteralPath $entryArtifactPath -PathType Leaf) 'The real workflow planning budget must allow the planner entrypoint to replace a stale artifact after discovery closes.'
        Assert-Contract ([IO.File]::ReadAllLines($fakeCommandLog).Count -eq 2) 'Planning against the real workflow must execute exactly one restore and one discovery.'
        Assert-Contract ([IO.File]::ReadAllLines($fakeEnvironmentLog).Count -eq 2) 'Planning against the real workflow must scope the planning environment to both child commands.'
        Assert-Contract ([string]::Equals([Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE'), 'failure-sentinel-node-reuse', [StringComparison]::Ordinal)) 'The real-workflow planner must restore the prior MSBuild node-reuse environment value.'
        Assert-Contract ([string]::Equals([Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER'), 'failure-sentinel-build-server', [StringComparison]::Ordinal)) 'The real-workflow planner must restore the prior dotnet build-server environment value.'
    }
    finally {
        [Environment]::SetEnvironmentVariable('PATH', $savedPath)
        [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', $savedNodeReuse)
        [Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', $savedBuildServer)
        [Environment]::SetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_COMMAND_LOG', $savedFakeCommandLog)
        [Environment]::SetEnvironmentVariable('NERV_ACCEPTANCE_FAKE_ENVIRONMENT_LOG', $savedFakeEnvironmentLog)
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Acceptance scenario matrix contract tests passed.'
