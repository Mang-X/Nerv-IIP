# Script-Governance:
#   Category: library
#   SideEffects:
#     - Parses a caller-provided GitHub Actions workflow through Ruby YAML
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

function ConvertFrom-NervCiRequiredSummaryWorkflow {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
    $result = Invoke-NativeCommandOutput -Command 'ruby' -Arguments @(
        '-ryaml',
        '-rjson',
        '-e', $rubyProgram,
        $Path
    ) -WorkingDirectory $WorkingDirectory -Name 'parse-ci-required-summary-workflow'

    return ($result.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-NervCiRequiredSummaryStringValue {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) { return '' }
    return [string] $property.Value
}

function Get-NervCiRequiredSummaryFindings {
    param(
        [Parameter(Mandatory)] [string] $WorkflowPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $findings = [Collections.Generic.List[string]]::new()
    $expectedNeeds = @(
        'impact-plan',
        'backend-tests',
        'postgres-provider-tests',
        'redis-cap-transport-tests',
        'business-full-chain-acceptance',
        'connector-host-tests',
        'frontend-unit-tests',
        'frontend',
        'openapi-client-drift',
        'script-governance'
    )

    try {
        $workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $WorkflowPath -WorkingDirectory $RepositoryRoot
        $jobs = $workflow.PSObject.Properties['jobs'].Value
        if ($null -eq $jobs) {
            $findings.Add('CI workflow must define jobs.')
            return @($findings)
        }

        $fullChainAggregateDiagnostic = 'Stable Business FullChain Acceptance must retain the exact planning, v1, shadow, equivalence, and selected/skipped result contract.'
        $fullChainAggregateValid = $true
        $fullChainAggregateProperty = $jobs.PSObject.Properties['business-full-chain-acceptance']
        if ($null -eq $fullChainAggregateProperty) {
            $fullChainAggregateValid = $false
        }
        else {
            $fullChainAggregate = $fullChainAggregateProperty.Value
            $expectedFullChainNeeds = @(
                'impact-plan',
                'acceptance-scenario-matrix-planning',
                'business-full-chain-acceptance-v1',
                'acceptance-scenario-matrix-runtime',
                'acceptance-scenario-matrix-equivalence'
            )
            $actualFullChainNeeds = @($fullChainAggregate.needs | ForEach-Object { [string] $_ })
            $expectedFullChainNeedSet = Get-NervStringSet -Values $expectedFullChainNeeds -Comparer ([StringComparer]::Ordinal)
            $actualFullChainNeedSet = Get-NervStringSet -Values $actualFullChainNeeds -Comparer ([StringComparer]::Ordinal)
            $fullChainAggregateValid = $actualFullChainNeeds.Count -eq $expectedFullChainNeeds.Count -and
                $actualFullChainNeedSet.Count -eq $expectedFullChainNeedSet.Count -and
                @($expectedFullChainNeeds | Where-Object { -not $actualFullChainNeedSet.Contains([string] $_) }).Count -eq 0

            $expectedFullChainCondition = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}"
            $fullChainAggregateValid = $fullChainAggregateValid -and
                [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainAggregate -PropertyName 'name'), 'Business FullChain Acceptance', [StringComparison]::Ordinal) -and
                [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainAggregate -PropertyName 'runs-on'), 'ubuntu-latest', [StringComparison]::Ordinal) -and
                [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainAggregate -PropertyName 'timeout-minutes'), '5', [StringComparison]::Ordinal) -and
                [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainAggregate -PropertyName 'if'), $expectedFullChainCondition, [StringComparison]::Ordinal) -and
                $null -eq $fullChainAggregate.PSObject.Properties['continue-on-error']

            $fullChainSteps = @($fullChainAggregate.steps)
            if ($fullChainSteps.Count -ne 1) {
                $fullChainAggregateValid = $false
            }
            else {
                $fullChainStep = $fullChainSteps[0]
                $fullChainAggregateValid = $fullChainAggregateValid -and
                    [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainStep -PropertyName 'name'), 'Require FullChain planning, v1 authority, and selected shadow equivalence', [StringComparison]::Ordinal) -and
                    [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainStep -PropertyName 'timeout-minutes'), '3', [StringComparison]::Ordinal) -and
                    [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $fullChainStep -PropertyName 'shell'), 'bash --noprofile --norc -euo pipefail {0}', [StringComparison]::Ordinal) -and
                    $null -eq $fullChainStep.PSObject.Properties['if'] -and
                    $null -eq $fullChainStep.PSObject.Properties['continue-on-error']

                $expectedFullChainRun = @'
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
                $actualFullChainRun = Get-NervCiRequiredSummaryStringValue -Object $fullChainStep -PropertyName 'run'
                $fullChainAggregateValid = $fullChainAggregateValid -and [string]::Equals(
                    $actualFullChainRun.Replace("`r`n", "`n").TrimEnd(),
                    $expectedFullChainRun.Replace("`r`n", "`n").TrimEnd(),
                    [StringComparison]::Ordinal)
            }
        }
        if (-not $fullChainAggregateValid) {
            $findings.Add($fullChainAggregateDiagnostic)
        }

        $formalEvidenceCollectors = [Collections.Generic.List[string]]::new()
        $formalEvidenceArtifacts = [Collections.Generic.List[string]]::new()
        $directFullChainCollectorPattern = '(?s)(?:^|\r?\n)\s*\./scripts/collect-test-evidence\.ps1(?:\s|$).*?-Lane\s+(?:full-chain|''full-chain''|"full-chain")(?:\s|$)'
        foreach ($jobProperty in $jobs.PSObject.Properties) {
            $jobName = [string] $jobProperty.Name
            foreach ($jobStep in @($jobProperty.Value.steps)) {
                $stepRun = Get-NervCiRequiredSummaryStringValue -Object $jobStep -PropertyName 'run'
                if ($stepRun -cmatch $directFullChainCollectorPattern) {
                    $formalEvidenceCollectors.Add($jobName)
                }

                $stepUses = Get-NervCiRequiredSummaryStringValue -Object $jobStep -PropertyName 'uses'
                $stepWith = $jobStep.PSObject.Properties['with']
                $artifactName = if ($null -ne $stepWith) {
                    Get-NervCiRequiredSummaryStringValue -Object $stepWith.Value -PropertyName 'name'
                }
                else { '' }
                if ($stepUses -cmatch '^actions/upload-artifact@v(?:4|5)$' -and
                    $artifactName.Contains('test-evidence-full-chain-', [StringComparison]::Ordinal)) {
                    $formalEvidenceArtifacts.Add($jobName)
                }
            }
        }

        $formalEvidenceOwnerValid = $false
        if ($formalEvidenceCollectors.Count -eq 1 -and $formalEvidenceArtifacts.Count -eq 1) {
            $formalEvidenceOwnerValid = [string]::Equals($formalEvidenceCollectors[0], 'business-full-chain-acceptance-v1', [StringComparison]::Ordinal) -and
                [string]::Equals($formalEvidenceArtifacts[0], 'business-full-chain-acceptance-v1', [StringComparison]::Ordinal)
        }
        if (-not $formalEvidenceOwnerValid) {
            $findings.Add("Only 'business-full-chain-acceptance-v1' may collect or publish formal full-chain MAN-661 evidence.")
        }

        $summaryProperty = $jobs.PSObject.Properties['ci-summary']
        if ($null -eq $summaryProperty) {
            $findings.Add("CI workflow is missing the stable 'ci-summary' job.")
            return @($findings)
        }

        $summary = $summaryProperty.Value
        $actualNeeds = @($summary.needs | ForEach-Object { [string] $_ })
        $expectedNeedSet = Get-NervStringSet -Values $expectedNeeds -Comparer ([StringComparer]::Ordinal)
        $actualNeedSet = Get-NervStringSet -Values $actualNeeds -Comparer ([StringComparer]::Ordinal)
        $missingNeeds = @($expectedNeeds | Where-Object { -not $actualNeedSet.Contains([string] $_) })
        $unexpectedNeeds = @($actualNeeds | Where-Object { -not $expectedNeedSet.Contains([string] $_) })
        $missingJobs = @($expectedNeeds | Where-Object { $null -eq $jobs.PSObject.Properties[$_] })
        if ($actualNeeds.Count -ne $expectedNeeds.Count -or $missingNeeds.Count -gt 0 -or $unexpectedNeeds.Count -gt 0 -or $missingJobs.Count -gt 0) {
            $findings.Add('CI Summary must need the impact plan, five current required jobs, OpenAPI Drift, PostgreSQL Provider Tests, Redis/CAP Transport Tests, and Business FullChain Acceptance exactly.')
        }

        $name = Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'name'
        $condition = Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'if'
        if (-not [string]::Equals($name, 'CI Summary', [StringComparison]::Ordinal) -or
            -not [string]::Equals($condition, 'always()', [StringComparison]::OrdinalIgnoreCase)) {
            $findings.Add("CI Summary must retain name 'CI Summary' and if: always().")
        }

        if (-not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'runs-on'), 'ubuntu-latest', [StringComparison]::Ordinal) -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $summary -PropertyName 'timeout-minutes'), '5', [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must run on ubuntu-latest with a five-minute job timeout.')
        }

        $steps = @($summary.steps)
        $hasContinueOnError = $null -ne $summary.PSObject.Properties['continue-on-error'] -or @(
            $steps | Where-Object { $null -ne $_.PSObject.Properties['continue-on-error'] }
        ).Count -gt 0
        if ($hasContinueOnError) {
            $findings.Add("CI Summary must not set 'continue-on-error' on the job or any step.")
        }

        if ($steps.Count -eq 1 -and $null -ne $steps[0].PSObject.Properties['if']) {
            $findings.Add('CI Summary assertion step must not have a condition.')
        }

        if ($steps.Count -ne 1 -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'name'), 'Require all CI lanes', [StringComparison]::Ordinal) -or
            -not [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'timeout-minutes'), '3', [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must contain one three-minute required-lane assertion step.')
        }

        if ($steps.Count -ne 1 -or
            -not [string]::Equals(
                (Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'shell'),
                'bash --noprofile --norc -euo pipefail {0}',
                [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary assertion step must use the governed fail-fast Bash shell.')
        }

        $run = if ($steps.Count -eq 1) { Get-NervCiRequiredSummaryStringValue -Object $steps[0] -PropertyName 'run' } else { '' }
        $expectedRun = @'
impact_result="${{ needs.impact-plan.result }}"
backend_result="${{ needs.backend-tests.result }}"
connector_result="${{ needs.connector-host-tests.result }}"
script_governance_result="${{ needs.script-governance.result }}"
openapi_result="${{ needs.openapi-client-drift.result }}"
postgres_result="${{ needs.postgres-provider-tests.result }}"
redis_cap_result="${{ needs.redis-cap-transport-tests.result }}"
full_chain_result="${{ needs.business-full-chain-acceptance.result }}"
backend_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false' }}"
connector_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false' }}"
script_governance_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.scripts != 'false' || needs.impact-plan.outputs.backend != 'false' }}"
openapi_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false' }}"
postgres_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false' }}"
redis_cap_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.redis_cap != 'false' }}"
full_chain_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false' }}"

if [[ "$backend_selected" = "true" ]]; then
  backend_policy="selected"
else
  backend_policy="skipped by design"
fi
if [[ "$connector_selected" = "true" ]]; then
  connector_policy="selected"
else
  connector_policy="skipped by design"
fi
if [[ "$script_governance_selected" = "true" ]]; then
  script_governance_policy="selected"
else
  script_governance_policy="skipped by design"
fi
if [[ "$openapi_selected" = "true" ]]; then
  openapi_policy="selected"
else
  openapi_policy="skipped by design"
fi
if [[ "$postgres_selected" = "true" ]]; then
  postgres_policy="selected"
else
  postgres_policy="skipped by policy"
fi
if [[ "$redis_cap_selected" = "true" ]]; then
  redis_cap_policy="selected"
else
  redis_cap_policy="skipped by policy"
fi
if [[ "$full_chain_selected" = "true" ]]; then
  full_chain_policy="selected"
else
  full_chain_policy="skipped by policy"
fi

{
  echo "## CI lane decisions"
  echo
  echo "| Lane | Policy | Result |"
  echo "| --- | --- | --- |"
  echo "| Backend Tests | $backend_policy | $backend_result |"
  echo "| Connector Host Tests | $connector_policy | $connector_result |"
  echo "| Script Governance | $script_governance_policy | $script_governance_result |"
  echo "| OpenAPI/api-client Drift | $openapi_policy | $openapi_result |"
  echo "| PostgreSQL Provider Tests | $postgres_policy | $postgres_result |"
  echo "| Redis/CAP Transport Tests | $redis_cap_policy | $redis_cap_result |"
  echo "| Business FullChain Acceptance | $full_chain_policy | $full_chain_result |"
} >> "$GITHUB_STEP_SUMMARY"

test "$impact_result" = "success"
test "$backend_result" = "success"
test "${{ needs.frontend-unit-tests.result }}" = "success"
test "${{ needs.frontend.result }}" = "success"
if [[ "$connector_selected" = "true" ]]; then
  test "$connector_result" = "success"
else
  test "$connector_result" = "skipped"
fi
if [[ "$postgres_selected" = "true" ]]; then
  test "$postgres_result" = "success"
else
  test "$postgres_result" = "skipped"
fi
if [[ "$redis_cap_selected" = "true" ]]; then
  test "$redis_cap_result" = "success"
else
  test "$redis_cap_result" = "skipped"
fi
if [[ "$full_chain_selected" = "true" ]]; then
  test "$full_chain_result" = "success"
else
  test "$full_chain_result" = "skipped"
fi
if [[ "$script_governance_selected" = "true" ]]; then
  test "$script_governance_result" = "success"
else
  test "$script_governance_result" = "skipped"
fi
if [[ "$openapi_selected" = "true" ]]; then
  test "$openapi_result" = "success"
else
  test "$openapi_result" = "skipped"
fi
'@
        $normalizedRun = $run.Replace("`r`n", "`n").TrimEnd()
        $normalizedExpectedRun = $expectedRun.Replace("`r`n", "`n").TrimEnd()
        if (-not [string]::Equals($normalizedRun, $normalizedExpectedRun, [StringComparison]::Ordinal)) {
            $findings.Add('CI Summary must retain the governed fail-closed selected/skipped-by-design/skipped-by-policy contract and audit table.')
        }
    }
    catch {
        $findings.Add("CI workflow must be valid structured YAML: $($_.Exception.Message)")
    }

    return @($findings)
}
