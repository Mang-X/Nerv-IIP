# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the CI required-summary verifier once against the repository workflow
#     - Runs the dot-sourced production contract against temporary workflow mutations
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
    $findings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $fixturePath -RepositoryRoot $repoRoot)
    Assert-Contract ($findings.Count -gt 0) "Mutation '$Name' must fail required-summary governance."
    $matchingDiagnostics = @($findings | Where-Object {
            [string]::Equals([string]$_, $ExpectedDiagnostic, [StringComparison]::Ordinal)
        })
    Assert-Contract ($matchingDiagnostics.Count -eq 1) "Mutation '$Name' returned the wrong diagnostic: $($findings -join '; ')"
}

function Assert-AcceptedMutation {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Original,
        [Parameter(Mandatory)] [string] $Replacement,
        [Parameter(Mandatory)] [string] $Workflow
    )

    $mutated = $Workflow.Replace($Original, $Replacement)
    Assert-Contract (-not [string]::Equals($mutated, $Workflow, [StringComparison]::Ordinal)) "Mutation '$Name' did not match the workflow."
    [IO.File]::WriteAllText($fixturePath, $mutated, [Text.UTF8Encoding]::new($false))
    $findings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $fixturePath -RepositoryRoot $repoRoot)
    Assert-Contract ($findings.Count -eq 0) "Accepted mutation '$Name' returned findings: $($findings -join '; ')"
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
    [string[]]$expectedAggregateNeeds = @(
        'impact-plan',
        'acceptance-scenario-matrix-planning',
        'business-full-chain-acceptance-v1',
        'acceptance-scenario-matrix-runtime',
        'acceptance-scenario-matrix-equivalence'
    )
    [Array]::Sort($aggregateNeeds, [StringComparer]::Ordinal)
    [Array]::Sort($expectedAggregateNeeds, [StringComparer]::Ordinal)
    Assert-Contract ([string]::Equals(($aggregateNeeds -join '|'), ($expectedAggregateNeeds -join '|'), [StringComparison]::Ordinal)) 'The stable FullChain aggregate must need exactly impact-plan, planning, v1, shadow runtime, and equivalence.'

    $aggregateSteps = @($aggregate.steps)
    Assert-Contract ($aggregateSteps.Count -eq 1) 'The stable FullChain aggregate must contain exactly one fail-fast assertion step.'
    $aggregateStep = $aggregateSteps[0]
    Assert-Contract ([int]$aggregateStep.'timeout-minutes' -gt 0 -and [int]$aggregateStep.'timeout-minutes' -lt [int]$aggregate.'timeout-minutes') 'The aggregate assertion step must have a positive timeout below the five-minute job budget.'
    Assert-Contract ([string]::Equals([string]$aggregateStep.shell, 'bash --noprofile --norc -euo pipefail {0}', [StringComparison]::Ordinal)) 'The stable FullChain aggregate must use the governed fail-fast Bash shell.'
    Assert-Contract ($null -eq $aggregateStep.PSObject.Properties['if'] -and $null -eq $aggregateStep.PSObject.Properties['continue-on-error']) 'The aggregate assertion step must run naturally without a skip or continue-on-error escape.'
    $aggregateRun = [string]$aggregateStep.run
    $expectedAggregateRun = @'
planning_result="${{ needs.acceptance-scenario-matrix-planning.result }}"
v1_result="${{ needs.business-full-chain-acceptance-v1.result }}"
sales_order_demand_selected="${{ needs.acceptance-scenario-matrix-planning.outputs.sales-order-demand-selected }}"
shadow_result="${{ needs.acceptance-scenario-matrix-runtime.result }}"
equivalence_result="${{ needs.acceptance-scenario-matrix-equivalence.result }}"

test "$planning_result" = "success"
test "$v1_result" = "success"
case "$sales_order_demand_selected" in
  true)
    test "$shadow_result" = "success"
    test "$equivalence_result" = "success"
    ;;
  false)
    test "$shadow_result" = "skipped"
    test "$equivalence_result" = "skipped"
    ;;
  *)
    echo "sales-order-demand-selected must be exactly 'true' or 'false'." >&2
    exit 1
    ;;
esac
'@
    Assert-Contract ([string]::Equals($aggregateRun.Replace("`r`n", "`n").TrimEnd(), $expectedAggregateRun.Replace("`r`n", "`n").TrimEnd(), [StringComparison]::Ordinal)) 'The stable FullChain aggregate must enforce the exact selected/unselected fail-closed result matrix.'
    Assert-Contract (-not $aggregateRun.Contains('test "$impact_result" = "success"', [StringComparison]::Ordinal)) 'The stable aggregate must allow the governed conservative path when impact-plan itself failed.'
    foreach ($forbiddenAggregateWork in @('collect-test-evidence.ps1', 'upload-artifact', 'run-full-chain-test-lane.ps1', 'run-acceptance-scenario-matrix.ps1')) {
        Assert-Contract (-not $aggregateRun.Contains($forbiddenAggregateWork, [StringComparison]::OrdinalIgnoreCase)) "The stable aggregate must not execute or publish '$forbiddenAggregateWork'."
    }

    Assert-Contract ($null -eq $parsedWorkflow.jobs.PSObject.Properties['erp-sales-order-demand-acceptance']) 'The retired ERP Sales Order Demand Acceptance job must remain absent.'

    $summary = $parsedWorkflow.jobs.'ci-summary'
    [string[]]$summaryNeeds = @($summary.needs | ForEach-Object { [string]$_ })
    Assert-Contract ([Array]::IndexOf($summaryNeeds, 'business-full-chain-acceptance') -ge 0 -and
        [Array]::IndexOf($summaryNeeds, 'erp-sales-order-demand-acceptance') -lt 0 -and
        [Array]::IndexOf($summaryNeeds, 'acceptance-scenario-matrix-runtime') -lt 0 -and
        [Array]::IndexOf($summaryNeeds, 'acceptance-scenario-matrix-equivalence') -lt 0 -and
        [Array]::IndexOf($summaryNeeds, 'business-full-chain-acceptance-v1') -lt 0) 'CI Summary must consume only the stable FullChain aggregate and no internal FullChain producers.'
    $summaryRun = [string]$summary.steps[0].run
    Assert-Contract (-not $summaryRun.Contains('erp_result=', [StringComparison]::Ordinal) -and
        -not $summaryRun.Contains('erp_selected=', [StringComparison]::Ordinal) -and
        -not $summaryRun.Contains('ERP Sales Order Demand Acceptance', [StringComparison]::Ordinal) -and
        -not $summaryRun.Contains('erp_sales_order_demand', [StringComparison]::Ordinal)) 'CI Summary must omit the retired ERP result, policy, audit row, and impact signal.'
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
    $directBaselineFindings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $workflowPath -RepositoryRoot $repoRoot)
    Assert-Contract ($directBaselineFindings.Count -eq 0) "The in-process production contract must agree with the production verifier baseline: $($directBaselineFindings -join '; ')"

    $crlfLibraryPath = Join-Path $fixtureRoot 'CiRequiredSummary-crlf.ps1'
    $librarySource = [IO.File]::ReadAllText($libraryPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
    [IO.File]::WriteAllText($crlfLibraryPath, $librarySource, [Text.UTF8Encoding]::new($false))
    . $crlfLibraryPath
    $crlfFindings = @(Get-NervCiRequiredSummaryFindings -WorkflowPath $workflowPath -RepositoryRoot $repoRoot)
    Assert-Contract ($crlfFindings.Count -eq 0) "Required-summary governance must be independent of the library checkout line endings: $($crlfFindings -join '; ')"

    $needsDiagnostic = 'CI Summary must need the impact plan, five current required jobs, OpenAPI Drift, PostgreSQL Provider Tests, Redis/CAP Transport Tests, and Business FullChain Acceptance exactly.'
    $policyDiagnostic = 'CI Summary must retain the governed fail-closed selected/skipped-by-design/skipped-by-policy contract and audit table.'
    $fullChainAggregateDiagnostic = 'Stable Business FullChain Acceptance must retain the exact planning, v1, shadow, equivalence, and selected/skipped result contract.'
    $fullChainEvidenceOwnerDiagnostic = "Only 'business-full-chain-acceptance-v1' may collect or publish formal full-chain MAN-661 evidence."

    Assert-AcceptedMutation -Name 'full-chain-v1-collector-single-quoted-lane' -Workflow $workflow `
        -Original '-Lane full-chain' -Replacement "-Lane 'full-chain'"

    Assert-AcceptedMutation -Name 'full-chain-v1-collector-double-quoted-lane' -Workflow $workflow `
        -Original '-Lane full-chain' -Replacement '-Lane "full-chain"'

    Assert-AcceptedMutation -Name 'full-chain-v1-evidence-upload-v5' -Workflow $workflow `
        -Original "      - name: Upload FullChain normalized evidence$([Environment]::NewLine)        timeout-minutes: 5$([Environment]::NewLine)        if: always()$([Environment]::NewLine)        uses: actions/upload-artifact@v4" `
        -Replacement "      - name: Upload FullChain normalized evidence$([Environment]::NewLine)        timeout-minutes: 5$([Environment]::NewLine)        if: always()$([Environment]::NewLine)        uses: actions/upload-artifact@v5"

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
                Name = 'full-chain-aggregate-selected-allows-shadow-skip'
                Original = '    test "$shadow_result" = "success"'
                Replacement = '    test "$shadow_result" = "skipped"'
            },
            @{
                Name = 'full-chain-aggregate-unselected-allows-shadow-success'
                Original = '    test "$shadow_result" = "skipped"'
                Replacement = '    test "$shadow_result" = "success"'
            },
            @{
                Name = 'full-chain-aggregate-invalid-selection-falls-through'
                Original = "    exit 1$([Environment]::NewLine)"
                Replacement = "    exit 0$([Environment]::NewLine)"
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

    foreach ($requiredAggregateNeed in @(
        'acceptance-scenario-matrix-planning',
        'business-full-chain-acceptance-v1',
        'acceptance-scenario-matrix-runtime',
        'acceptance-scenario-matrix-equivalence'
    )) {
        Invoke-Mutation -Name "full-chain-aggregate-production-drops-$requiredAggregateNeed" -Workflow $workflow `
            -Original "      - $requiredAggregateNeed$([Environment]::NewLine)" -Replacement '' `
            -ExpectedDiagnostic $fullChainAggregateDiagnostic
    }

    foreach ($selectedResultMutation in @(
        @{ Name = 'planning'; Original = '          test "$planning_result" = "success"'; Replacement = '          test "$planning_result" = "skipped"' },
        @{ Name = 'v1'; Original = '          test "$v1_result" = "success"'; Replacement = '          test "$v1_result" = "skipped"' },
        @{ Name = 'shadow'; Original = '              test "$shadow_result" = "success"'; Replacement = '              test "$shadow_result" = "skipped"' },
        @{ Name = 'equivalence'; Original = '              test "$equivalence_result" = "success"'; Replacement = '              test "$equivalence_result" = "skipped"' }
    )) {
        Invoke-Mutation -Name "full-chain-aggregate-production-selected-allows-$($selectedResultMutation.Name)-skip" -Workflow $workflow `
            -Original $selectedResultMutation.Original -Replacement $selectedResultMutation.Replacement `
            -ExpectedDiagnostic $fullChainAggregateDiagnostic
    }

    Invoke-Mutation -Name 'full-chain-shadow-collects-formal-evidence' -Workflow $workflow `
        -Original "            -TrackIdentifier 'shadow'$([Environment]::NewLine)" `
        -Replacement "            -TrackIdentifier 'shadow'$([Environment]::NewLine)          ./scripts/collect-test-evidence.ps1 -Lane full-chain$([Environment]::NewLine)" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-shadow-collects-formal-evidence-single-quoted-lane' -Workflow $workflow `
        -Original "            -TrackIdentifier 'shadow'$([Environment]::NewLine)" `
        -Replacement "            -TrackIdentifier 'shadow'$([Environment]::NewLine)          ./scripts/collect-test-evidence.ps1 -Lane 'full-chain'$([Environment]::NewLine)" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-shadow-collects-formal-evidence-double-quoted-lane' -Workflow $workflow `
        -Original "            -TrackIdentifier 'shadow'$([Environment]::NewLine)" `
        -Replacement "            -TrackIdentifier 'shadow'$([Environment]::NewLine)          ./scripts/collect-test-evidence.ps1 -Lane `"full-chain`"$([Environment]::NewLine)" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-shadow-publishes-formal-evidence-artifact' -Workflow $workflow `
        -Original 'name: acceptance-scenario-matrix-runtime-summary-${{ github.run_id }}-${{ github.run_attempt }}' `
        -Replacement 'name: test-evidence-full-chain-${{ github.run_id }}-${{ github.run_attempt }}' `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-shadow-publishes-contained-formal-evidence-artifact-with-v5' -Workflow $workflow `
        -Original "        uses: actions/upload-artifact@v4$([Environment]::NewLine)        with:$([Environment]::NewLine)          name: acceptance-scenario-matrix-runtime-summary-`${{ github.run_id }}-`${{ github.run_attempt }}" `
        -Replacement "        uses: actions/upload-artifact@v5$([Environment]::NewLine)        with:$([Environment]::NewLine)          name: shadow-`${{ github.run_id }}-test-evidence-full-chain-`${{ github.run_attempt }}" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-equivalence-collects-formal-evidence' -Workflow $workflow `
        -Original "            -ReportPath artifacts/acceptance-scenario-matrix/equivalence-report.json$([Environment]::NewLine)" `
        -Replacement "            -ReportPath artifacts/acceptance-scenario-matrix/equivalence-report.json$([Environment]::NewLine)          ./scripts/collect-test-evidence.ps1 -Lane full-chain$([Environment]::NewLine)" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-equivalence-publishes-formal-evidence-artifact' -Workflow $workflow `
        -Original 'name: acceptance-scenario-matrix-equivalence-${{ github.run_id }}-${{ github.run_attempt }}' `
        -Replacement 'name: test-evidence-full-chain-${{ github.run_id }}-${{ github.run_attempt }}' `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-stable-aggregate-collects-formal-evidence' -Workflow $workflow `
        -Original "          planning_result=`"`${{ needs.acceptance-scenario-matrix-planning.result }}`"$([Environment]::NewLine)" `
        -Replacement "          planning_result=`"`${{ needs.acceptance-scenario-matrix-planning.result }}`"$([Environment]::NewLine)          ./scripts/collect-test-evidence.ps1 -Lane full-chain$([Environment]::NewLine)" `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    $stableAggregateStepHeader = "    steps:$([Environment]::NewLine)      - name: Require FullChain planning, v1 authority, and selected shadow equivalence"
    $stableAggregateFormalArtifact = @"
    steps:
      - name: Publish forbidden formal evidence
        timeout-minutes: 1
        uses: actions/upload-artifact@v4
        with:
          name: test-evidence-full-chain-`${{ github.run_id }}-`${{ github.run_attempt }}
          path: artifacts/forbidden
      - name: Require FullChain planning, v1 authority, and selected shadow equivalence
"@.Replace("`r`n", [Environment]::NewLine).TrimEnd()
    Invoke-Mutation -Name 'full-chain-stable-aggregate-publishes-formal-evidence-artifact' -Workflow $workflow `
        -Original $stableAggregateStepHeader -Replacement $stableAggregateFormalArtifact `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-v1-drops-formal-evidence-collector' -Workflow $workflow `
        -Original ('          ./scripts/collect-test-evidence.ps1' + [Environment]::NewLine) -Replacement '' `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

    Invoke-Mutation -Name 'full-chain-v1-drops-formal-evidence-artifact' -Workflow $workflow `
        -Original 'name: test-evidence-full-chain-${{ github.run_id }}-${{ github.run_attempt }}' `
        -Replacement 'name: full-chain-normalized-${{ github.run_id }}-${{ github.run_attempt }}' `
        -ExpectedDiagnostic $fullChainEvidenceOwnerDiagnostic

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
