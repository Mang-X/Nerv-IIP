# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the CI required-summary verifier against temporary workflow mutations
#   Writes:
#     - Temporary workflow fixtures under the operating-system temp directory
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')

$verifierPath = Join-Path $repoRoot 'scripts/verify-ci-required-summary.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$libraryPath = Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-required-summary-$([Guid]::NewGuid().ToString('N'))"
$fixturePath = Join-Path $fixtureRoot 'ci.yml'

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function Invoke-SummaryVerifier {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [string] $Path = $workflowPath
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-File', $verifierPath, '-WorkflowPath', $Path) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

function Invoke-Mutation {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Original,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Replacement,
        [Parameter(Mandatory)] [string] $ExpectedDiagnostic,
        [Parameter(Mandatory)] [string] $Workflow
    )

    $mutated = $Workflow.Replace($Original, $Replacement)
    Assert-Contract (-not [string]::Equals($mutated, $Workflow, [StringComparison]::Ordinal)) "Mutation '$Name' did not match the workflow."
    [IO.File]::WriteAllText($fixturePath, $mutated, [Text.UTF8Encoding]::new($false))
    $result = Invoke-SummaryVerifier -Name $Name -Path $fixturePath
    Assert-Contract (-not $result.Passed) "Mutation '$Name' must fail required-summary governance."
    Assert-Contract ($result.Message.Contains($ExpectedDiagnostic, [StringComparison]::Ordinal)) "Mutation '$Name' returned the wrong diagnostic: $($result.Message)"
}

function Assert-FullChainAggregateContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $aggregateProperties = @($parsedWorkflow.jobs.PSObject.Properties | Where-Object {
            [string]::Equals([string]$_.Name, 'business-full-chain-acceptance', [StringComparison]::Ordinal)
        })
    Assert-Contract ($aggregateProperties.Count -eq 1) 'CI must retain exactly one stable business-full-chain-acceptance aggregate.'
    $aggregate = $aggregateProperties[0].Value
    Assert-Contract ([string]::Equals([string]$aggregate.name, 'Business FullChain Acceptance', [StringComparison]::Ordinal)) 'The stable FullChain aggregate must retain its required Actions name.'
    Assert-Contract ([int]$aggregate.'timeout-minutes' -eq 5) 'The stable FullChain aggregate must use the governed five-minute job budget.'
    Assert-Contract ([string]::Equals([string]$aggregate.'runs-on', 'ubuntu-latest', [StringComparison]::Ordinal)) 'The stable FullChain aggregate must run on ubuntu-latest.'
    Assert-Contract ([string]::Equals([string]$aggregate.if, "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}", [StringComparison]::Ordinal)) 'The stable FullChain aggregate must run always when selected and remain skipped for an explicit successful-PR full_chain=false policy decision.'

    [string[]]$aggregateNeeds = @($aggregate.needs | ForEach-Object { [string]$_ })
    [string[]]$expectedAggregateNeeds = @('impact-plan', 'acceptance-scenario-matrix-planning', 'business-full-chain-acceptance-v1')
    [Array]::Sort($aggregateNeeds, [StringComparer]::Ordinal)
    [Array]::Sort($expectedAggregateNeeds, [StringComparer]::Ordinal)
    Assert-Contract ([string]::Equals(($aggregateNeeds -join '|'), ($expectedAggregateNeeds -join '|'), [StringComparison]::Ordinal)) 'The stable FullChain aggregate must need exactly impact-plan, planning, and the physical v1 worker.'

    $aggregateSteps = @($aggregate.steps)
    Assert-Contract ($aggregateSteps.Count -eq 1) 'The stable FullChain aggregate must contain exactly one fail-fast assertion step.'
    $aggregateStep = $aggregateSteps[0]
    Assert-Contract ([int]$aggregateStep.'timeout-minutes' -gt 0 -and [int]$aggregateStep.'timeout-minutes' -lt [int]$aggregate.'timeout-minutes') 'The aggregate assertion step must have a positive timeout below the five-minute job budget.'
    Assert-Contract ([string]::Equals([string]$aggregateStep.shell, 'bash --noprofile --norc -euo pipefail {0}', [StringComparison]::Ordinal)) 'The stable FullChain aggregate must use the governed fail-fast Bash shell.'
    Assert-Contract ($null -eq $aggregateStep.PSObject.Properties['if'] -and $null -eq $aggregateStep.PSObject.Properties['continue-on-error']) 'The aggregate assertion step must run naturally without a skip or continue-on-error escape.'
    $aggregateRun = [string]$aggregateStep.run
    foreach ($requiredAggregateAssertion in @(
            'planning_result="${{ needs.acceptance-scenario-matrix-planning.result }}"',
            'v1_result="${{ needs.business-full-chain-acceptance-v1.result }}"',
            'test "$planning_result" = "success"',
            'test "$v1_result" = "success"'
        )) {
        Assert-Contract ($aggregateRun.Contains($requiredAggregateAssertion, [StringComparison]::Ordinal)) "The stable FullChain aggregate is missing required assertion '$requiredAggregateAssertion'."
    }
    Assert-Contract (-not $aggregateRun.Contains('test "$impact_result" = "success"', [StringComparison]::Ordinal)) 'The stable aggregate must allow the governed conservative path when impact-plan itself failed.'
    foreach ($forbiddenAggregateWork in @('collect-test-evidence.ps1', 'upload-artifact', 'run-full-chain-test-lane.ps1', 'run-acceptance-scenario-matrix.ps1')) {
        Assert-Contract (-not $aggregateRun.Contains($forbiddenAggregateWork, [StringComparison]::OrdinalIgnoreCase)) "The stable aggregate must not execute or publish '$forbiddenAggregateWork'."
    }

    $legacyErpProperties = @($parsedWorkflow.jobs.PSObject.Properties | Where-Object {
            [string]::Equals([string]$_.Name, 'erp-sales-order-demand-acceptance', [StringComparison]::Ordinal)
        })
    Assert-Contract ($legacyErpProperties.Count -eq 1) 'The legacy ERP Sales Order Demand Acceptance job must not be deleted or renamed.'
    $legacyErp = $legacyErpProperties[0].Value
    Assert-Contract ([string]::Equals([string]$legacyErp.name, 'ERP Sales Order Demand Acceptance', [StringComparison]::Ordinal) -and [int]$legacyErp.'timeout-minutes' -eq 55) 'The legacy ERP job name and budget must remain unchanged in this layer.'
    $legacyErpUploads = @($legacyErp.steps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/upload-artifact@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($legacyErpUploads.Count -eq 1 -and
        [string]::Equals([string]$legacyErpUploads[0].with.'if-no-files-found', 'warn', [StringComparison]::Ordinal) -and
        [int]$legacyErpUploads[0].with.'retention-days' -eq 7) 'The legacy ERP artifact must retain its existing warn/7-day behavior until the later equivalence layer.'
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    Assert-Contract (Test-Path -LiteralPath $verifierPath -PathType Leaf) 'The CI required-summary verifier is missing.'

    $workflow = [IO.File]::ReadAllText($workflowPath)
    foreach ($fullChainSummaryFragment in @(
        '      - business-full-chain-acceptance',
        'full_chain_result="${{ needs.business-full-chain-acceptance.result }}"',
        'full_chain_selected="${{ github.event_name != ''pull_request'' || needs.impact-plan.result != ''success'' || needs.impact-plan.outputs.full_chain != ''false'' }}"',
        '            full_chain_policy="skipped by policy"',
        '            echo "| Business FullChain Acceptance | $full_chain_policy | $full_chain_result |"',
        '            test "$full_chain_result" = "success"',
        '            test "$full_chain_result" = "skipped"'
    )) {
        Assert-Contract ($workflow.Contains($fullChainSummaryFragment, [StringComparison]::Ordinal)) "CI Summary is missing FullChain contract fragment '$fullChainSummaryFragment'."
    }
    Assert-FullChainAggregateContract -Path $workflowPath
    $baseline = Invoke-SummaryVerifier -Name 'ci-required-summary-baseline'
    Assert-Contract $baseline.Passed "The repository workflow must satisfy required-summary governance: $($baseline.Message)"

    $crlfLibraryPath = Join-Path $fixtureRoot 'CiRequiredSummary-crlf.ps1'
    $librarySource = [IO.File]::ReadAllText($libraryPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
    [IO.File]::WriteAllText($crlfLibraryPath, $librarySource, [Text.UTF8Encoding]::new($false))
    . $crlfLibraryPath
    $crlfFindings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $workflowPath -RepositoryRoot $repoRoot)
    Assert-Contract ($crlfFindings.Count -eq 0) "Required-summary governance must be independent of the library checkout line endings: $($crlfFindings -join '; ')"

    $needsDiagnostic = 'CI Summary must need the impact plan, five current required jobs, ERP Acceptance, OpenAPI Drift, PostgreSQL Provider Tests, Redis/CAP Transport Tests, and Business FullChain Acceptance exactly.'
    $policyDiagnostic = 'CI Summary must retain the governed fail-closed selected/skipped-by-design/skipped-by-policy contract and audit table.'

    foreach ($aggregateMutation in @(
            @{
                Name = 'full-chain-aggregate-drops-v1-need'
                Original = "      - business-full-chain-acceptance-v1$([Environment]::NewLine)"
                Replacement = ''
            },
            @{
                Name = 'full-chain-aggregate-allows-v1-skip'
                Original = 'test "$v1_result" = "success"'
                Replacement = 'test "$v1_result" = "skipped"'
            },
            @{
                Name = 'full-chain-aggregate-treats-missing-signal-as-unselected'
                Original = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}"
                Replacement = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain == 'true') }}"
            }
        )) {
        $mutatedAggregateWorkflow = $workflow.Replace([string]$aggregateMutation.Original, [string]$aggregateMutation.Replacement)
        Assert-Contract (-not [string]::Equals($mutatedAggregateWorkflow, $workflow, [StringComparison]::Ordinal)) "FullChain aggregate mutation '$($aggregateMutation.Name)' must match the canonical workflow."
        [IO.File]::WriteAllText($fixturePath, $mutatedAggregateWorkflow, [Text.UTF8Encoding]::new($false))
        $aggregateMutationFailure = $null
        try { Assert-FullChainAggregateContract -Path $fixturePath } catch { $aggregateMutationFailure = $_ }
        Assert-Contract ($null -ne $aggregateMutationFailure) "FullChain aggregate mutation '$($aggregateMutation.Name)' must be rejected."
    }

    $needLine = '      - impact-plan'
    Invoke-Mutation -Name 'ci-summary-missing-need' -Workflow $workflow `
        -Original "$needLine$([Environment]::NewLine)" -Replacement '' `
        -ExpectedDiagnostic $needsDiagnostic

    Invoke-Mutation -Name 'ci-summary-not-always' -Workflow $workflow `
        -Original "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    timeout-minutes: 5$([Environment]::NewLine)    runs-on: ubuntu-latest$([Environment]::NewLine)    if: always()" `
        -Replacement "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    timeout-minutes: 5$([Environment]::NewLine)    runs-on: ubuntu-latest$([Environment]::NewLine)    if: success()" `
        -ExpectedDiagnostic "CI Summary must retain name 'CI Summary' and if: always()."

    Invoke-Mutation -Name 'ci-summary-missing-job' -Workflow $workflow `
        -Original "  connector-host-tests:$([Environment]::NewLine)" `
        -Replacement "  connector-host-tests-missing:$([Environment]::NewLine)" `
        -ExpectedDiagnostic $needsDiagnostic

    $selectedAssertion = '            test "$script_governance_result" = "success"'
    Invoke-Mutation -Name 'ci-summary-selected-lane-allows-skip' -Workflow $workflow `
        -Original $selectedAssertion -Replacement '            test "$script_governance_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    $skippedAssertion = '            test "$openapi_result" = "skipped"'
    Invoke-Mutation -Name 'ci-summary-unselected-lane-allows-success' -Workflow $workflow `
        -Original $skippedAssertion -Replacement '            test "$openapi_result" = "success"' `
        -ExpectedDiagnostic $policyDiagnostic

    $connectorSelectedAssertion = '            test "$connector_result" = "success"'
    Invoke-Mutation -Name 'ci-summary-selected-connector-allows-skip' -Workflow $workflow `
        -Original $connectorSelectedAssertion -Replacement '            test "$connector_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    $connectorSkippedAssertion = '            test "$connector_result" = "skipped"'
    Invoke-Mutation -Name 'ci-summary-unselected-connector-allows-success' -Workflow $workflow `
        -Original $connectorSkippedAssertion -Replacement '            test "$connector_result" = "success"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-erp-selected-allows-skip' -Workflow $workflow `
        -Original '            test "$erp_result" = "success"' -Replacement '            test "$erp_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-erp-unselected-allows-success' -Workflow $workflow `
        -Original '            test "$erp_result" = "skipped"' -Replacement '            test "$erp_result" = "success"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-erp-skipped-by-design-audit' -Workflow $workflow `
        -Original '            erp_policy="skipped by design"' -Replacement '            erp_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-backend-uses-wrong-signal' -Workflow $workflow `
        -Original "needs.impact-plan.outputs.backend != 'false'" `
        -Replacement "needs.impact-plan.outputs.frontend != 'false'" `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-backend-skipped-by-design-audit' -Workflow $workflow `
        -Original '            backend_policy="skipped by design"' -Replacement '            backend_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-drops-backend-audit-row' -Workflow $workflow `
        -Original ('            echo "| Backend Tests | $backend_policy | $backend_result |"' + [Environment]::NewLine) -Replacement '' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-postgres-selected-allows-skip' -Workflow $workflow `
        -Original '            test "$postgres_result" = "success"' -Replacement '            test "$postgres_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-skipped-by-policy-audit' -Workflow $workflow `
        -Original '            postgres_policy="skipped by policy"' -Replacement '            postgres_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-redis-cap-selected-allows-skip' -Workflow $workflow `
        -Original '            test "$redis_cap_result" = "success"' -Replacement '            test "$redis_cap_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-redis-cap-skipped-by-policy-audit' -Workflow $workflow `
        -Original '            redis_cap_policy="skipped by policy"' -Replacement '            redis_cap_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-full-chain-selected-allows-skip' -Workflow $workflow `
        -Original '            test "$full_chain_result" = "success"' -Replacement '            test "$full_chain_result" = "skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-full-chain-unselected-allows-success' -Workflow $workflow `
        -Original '            test "$full_chain_result" = "skipped"' -Replacement '            test "$full_chain_result" = "success"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-full-chain-skipped-by-policy-audit' -Workflow $workflow `
        -Original '            full_chain_policy="skipped by policy"' -Replacement '            full_chain_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    $impactAssertion = '          test "$impact_result" = "success"'
    Invoke-Mutation -Name 'ci-summary-ignores-impact-plan-failure' -Workflow $workflow `
        -Original $impactAssertion -Replacement '          echo "$impact_result"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-masked-failure' -Workflow $workflow `
        -Original $selectedAssertion -Replacement "$selectedAssertion || true" `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-skipped-by-design-audit' -Workflow $workflow `
        -Original '            script_governance_policy="skipped by design"' `
        -Replacement '            script_governance_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-hides-connector-skipped-by-design-audit' -Workflow $workflow `
        -Original '            connector_policy="skipped by design"' `
        -Replacement '            connector_policy="skipped"' `
        -ExpectedDiagnostic $policyDiagnostic

    Invoke-Mutation -Name 'ci-summary-step-continue-on-error' -Workflow $workflow `
        -Original "      - name: Require all CI lanes$([Environment]::NewLine)" `
        -Replacement "      - name: Require all CI lanes$([Environment]::NewLine)        continue-on-error: true$([Environment]::NewLine)" `
        -ExpectedDiagnostic "CI Summary must not set 'continue-on-error' on the job or any step."

    Invoke-Mutation -Name 'ci-summary-skipped-assertion-step' -Workflow $workflow `
        -Original "      - name: Require all CI lanes$([Environment]::NewLine)" `
        -Replacement "      - name: Require all CI lanes$([Environment]::NewLine)        if: false$([Environment]::NewLine)" `
        -ExpectedDiagnostic 'CI Summary assertion step must not have a condition.'

    Invoke-Mutation -Name 'ci-summary-non-fail-fast-shell' -Workflow $workflow `
        -Original '        shell: bash --noprofile --norc -euo pipefail {0}' `
        -Replacement '        shell: bash {0}' `
        -ExpectedDiagnostic 'CI Summary assertion step must use the governed fail-fast Bash shell.'

    Invoke-Mutation -Name 'ci-summary-job-continue-on-error' -Workflow $workflow `
        -Original "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)" `
        -Replacement "  ci-summary:$([Environment]::NewLine)    name: CI Summary$([Environment]::NewLine)    continue-on-error: true$([Environment]::NewLine)" `
        -ExpectedDiagnostic "CI Summary must not set 'continue-on-error' on the job or any step."

}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Output 'CI required-summary contract tests passed.'
