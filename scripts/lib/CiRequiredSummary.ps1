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
            $findings.Add('CI Summary must need the impact plan, five current required jobs, OpenAPI Drift, and PostgreSQL Provider Tests exactly.')
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
connector_result="${{ needs.connector-host-tests.result }}"
script_governance_result="${{ needs.script-governance.result }}"
openapi_result="${{ needs.openapi-client-drift.result }}"
postgres_result="${{ needs.postgres-provider-tests.result }}"
connector_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false' }}"
script_governance_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.scripts != 'false' || needs.impact-plan.outputs.backend != 'false' }}"
openapi_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false' }}"
postgres_selected="${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false' }}"

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

{
  echo "## CI lane decisions"
  echo
  echo "| Lane | Policy | Result |"
  echo "| --- | --- | --- |"
  echo "| Connector Host Tests | $connector_policy | $connector_result |"
  echo "| Script Governance | $script_governance_policy | $script_governance_result |"
  echo "| OpenAPI/api-client Drift | $openapi_policy | $openapi_result |"
  echo "| PostgreSQL Provider Tests | $postgres_policy | $postgres_result |"
} >> "$GITHUB_STEP_SUMMARY"

test "$impact_result" = "success"
test "${{ needs.backend-tests.result }}" = "success"
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
            $findings.Add('CI Summary must retain the governed fail-closed selected/skipped-by-design policy and audit table.')
        }
    }
    catch {
        $findings.Add("CI workflow must be valid structured YAML: $($_.Exception.Message)")
    }

    return @($findings)
}
