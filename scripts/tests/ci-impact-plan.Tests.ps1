# Script-Governance:
#   Category: check
#   SideEffects:
#     - Loads the CI impact-plan library and inspects the CI workflow contract
#   Writes:
#     - Temporary impact-plan artifacts under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/CiImpactPlan.ps1'
$entrypointPath = Join-Path $repoRoot 'scripts/get-ci-impact-plan.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$acceptanceScenarioMatrixOwningPaths = @(
    'scripts/acceptance-scenario-matrix.json'
    'scripts/lib/AcceptanceScenarioMatrix.ps1'
    'scripts/plan-acceptance-scenario-matrix.ps1'
    'scripts/tests/acceptance-scenario-matrix.Tests.ps1'
)
$acceptanceScenarioMatrixRuntimeOwningPaths = @(
    'scripts/lib/AcceptanceScenarioMatrixRuntime.ps1'
    'scripts/run-acceptance-scenario-matrix.ps1'
    'scripts/tests/acceptance-scenario-matrix-runtime.Tests.ps1'
    'scripts/lib/AcceptanceScenarioMatrixEquivalence.ps1'
    'scripts/verify-acceptance-scenario-matrix-equivalence.ps1'
    'scripts/tests/acceptance-scenario-matrix-equivalence.Tests.ps1'
)
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')
. (Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrix.ps1')

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function Assert-ImpactFlag {
    param(
        [Parameter(Mandatory)] [object] $Plan,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [bool] $Expected
    )

    $property = $Plan.PSObject.Properties[$Name]
    Assert-Contract ($null -ne $property) "Impact plan is missing flag '$Name'."
    Assert-Contract ([bool]$property.Value -eq $Expected) "Impact flag '$Name' expected '$Expected' but was '$($property.Value)'."
}

function Test-OrdinalMember {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Values,
        [Parameter(Mandatory)] [string] $Expected
    )

    return @($Values | Where-Object { [string]::Equals($_, $Expected, [StringComparison]::Ordinal) }).Count -gt 0
}

function Assert-ConditionalRoutingWorkflow {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $routingPolicies = [ordered]@{
        'backend-test-shard-governance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-gateway' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-platform' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-core-a' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-core-b' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'erp-sales-order-demand-acceptance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}"
        'connector-host-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
        'openapi-client-drift' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
        'postgres-provider-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false') }}"
        'redis-cap-transport-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.redis_cap != 'false') }}"
        'script-governance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.scripts != 'false' || needs.impact-plan.outputs.backend != 'false') }}"
    }

    $impactPlan = $parsedWorkflow.jobs.PSObject.Properties['impact-plan'].Value
    foreach ($outputName in @('scripts', 'backend', 'connector_hosts', 'openapi_codegen', 'postgresql', 'redis_cap', 'full_chain', 'erp_sales_order_demand')) {
        $outputProperty = $impactPlan.outputs.PSObject.Properties[$outputName]
        Assert-Contract ($null -ne $outputProperty) "Impact plan must declare routed output '$outputName'."
        $expectedOutput = '${{ steps.plan.outputs.' + $outputName + ' }}'
        Assert-Contract ([string]::Equals([string]$outputProperty.Value, $expectedOutput, [StringComparison]::Ordinal)) "Impact plan output '$outputName' must map directly to the plan step."
    }

    foreach ($jobName in $routingPolicies.Keys) {
        $jobProperty = $parsedWorkflow.jobs.PSObject.Properties[$jobName]
        Assert-Contract ($null -ne $jobProperty) "CI must define routed job '$jobName'."
        $job = $jobProperty.Value
        $needsProperty = $job.PSObject.Properties['needs']
        Assert-Contract ($null -ne $needsProperty) "Routed job '$jobName' must declare impact-plan as a dependency."
        $needs = @($needsProperty.Value | ForEach-Object { [string]$_ })
        Assert-Contract ($needs.Count -eq 1 -and [string]::Equals($needs[0], 'impact-plan', [StringComparison]::Ordinal)) "Routed job '$jobName' must need exactly impact-plan."
        Assert-Contract ([string]::Equals([string]$job.if, [string]$routingPolicies[$jobName], [StringComparison]::Ordinal)) "Routed job '$jobName' must use the governed fail-open PR policy."
    }

    $frontendConsumers = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('frontend-unit-test-shards', 'frontend-unit-tests', 'frontend-check', 'frontend-validation-shards', 'frontend'),
        [StringComparer]::Ordinal)
    $allowedConsumers = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            'backend-test-shard-governance', 'backend-tests-business-gateway', 'backend-tests-platform',
            'backend-tests-business-core-a', 'backend-tests-business-core-b', 'backend-tests',
            'erp-sales-order-demand-acceptance', 'connector-host-tests', 'openapi-client-drift',
            'postgres-provider-tests', 'redis-cap-transport-tests', 'acceptance-scenario-matrix-planning',
            'business-full-chain-acceptance', 'script-governance', 'ci-summary'
        ),
        [StringComparer]::Ordinal)
    foreach ($frontendConsumer in $frontendConsumers) { [void]$allowedConsumers.Add($frontendConsumer) }

    foreach ($jobProperty in @($parsedWorkflow.jobs.PSObject.Properties | Where-Object { -not [string]::Equals($_.Name, 'impact-plan', [StringComparison]::Ordinal) })) {
        $job = $jobProperty.Value
        $needsProperty = $job.PSObject.Properties['needs']
        [string[]]$needs = @()
        if ($null -ne $needsProperty) { $needs = @($needsProperty.Value | ForEach-Object { [string]$_ }) }
        $consumesImpact = Test-OrdinalMember -Values $needs -Expected 'impact-plan'
        $conditionProperty = $job.PSObject.Properties['if']
        $condition = if ($null -eq $conditionProperty) { '' } else { [string]$conditionProperty.Value }
        if ($allowedConsumers.Contains([string]$jobProperty.Name)) {
            Assert-Contract $consumesImpact "Governed job '$($jobProperty.Name)' must consume impact-plan."
        }
        else {
            Assert-Contract (-not $consumesImpact) "Unrouted job '$($jobProperty.Name)' must not depend on impact-plan."
            Assert-Contract (-not $condition.Contains('impact-plan', [StringComparison]::Ordinal)) "Unrouted job '$($jobProperty.Name)' must not consume impact-plan outputs."
        }
    }
}

function Assert-AcceptanceScenarioMatrixWorkflowContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $scriptGovernanceProperty = $parsedWorkflow.jobs.PSObject.Properties['script-governance']
    Assert-Contract ($null -ne $scriptGovernanceProperty) 'CI must retain the script-governance job.'
    $scriptGovernanceSteps = @($scriptGovernanceProperty.Value.steps)
    $scriptGovernanceStepTimeouts = @($scriptGovernanceSteps | ForEach-Object { [int]$_.'timeout-minutes' })
    $scriptGovernanceStepBudgetMinutes = ($scriptGovernanceStepTimeouts | Measure-Object -Sum).Sum
    $fiveMinuteStepCount = @($scriptGovernanceStepTimeouts | Where-Object { $_ -eq 5 }).Count
    $workflowSource = [IO.File]::ReadAllText($Path)
    $expectedBudgetHeadline = "step 预算合计 $($scriptGovernanceStepBudgetMinutes)m（$($scriptGovernanceSteps.Count) 个 step：3m checkout"
    $expectedBudgetContinuation = "+ $fiveMinuteStepCount × 5m；"
    $contractSteps = @($scriptGovernanceSteps | Where-Object {
            [string]::Equals([string]$_.name, 'Test acceptance scenario matrix contract', [StringComparison]::Ordinal)
        })
    Assert-Contract ($contractSteps.Count -eq 1) 'Script Governance must contain exactly one independent acceptance scenario matrix contract step.'
    $contractStep = $contractSteps[0]
    Assert-Contract ([string]::Equals([string]$contractStep.shell, 'pwsh', [StringComparison]::Ordinal)) 'The acceptance scenario matrix contract step must use the pwsh shell.'
    Assert-Contract ([string]::Equals([string]$contractStep.run, './scripts/tests/acceptance-scenario-matrix.Tests.ps1', [StringComparison]::Ordinal)) 'The acceptance scenario matrix contract step must run only the pure fixture contract.'
    Assert-Contract ([int]$contractStep.'timeout-minutes' -eq 5) 'The acceptance scenario matrix contract step must have a 5-minute budget.'
    Assert-Contract ($null -eq $contractStep.PSObject.Properties['if']) 'The acceptance scenario matrix contract step must not have its own condition.'

    $runtimeContractSteps = @($scriptGovernanceSteps | Where-Object {
            [string]::Equals([string]$_.name, 'Test acceptance scenario matrix runtime contract', [StringComparison]::Ordinal)
        })
    Assert-Contract ($runtimeContractSteps.Count -eq 1) 'Script Governance must contain exactly one independent acceptance scenario matrix runtime contract step.'
    $runtimeContractStep = $runtimeContractSteps[0]
    Assert-Contract ([string]::Equals([string]$runtimeContractStep.shell, 'pwsh', [StringComparison]::Ordinal)) 'The acceptance scenario matrix runtime contract step must use the pwsh shell.'
    Assert-Contract ([string]::Equals([string]$runtimeContractStep.run, './scripts/tests/acceptance-scenario-matrix-runtime.Tests.ps1', [StringComparison]::Ordinal)) 'The acceptance scenario matrix runtime contract step must run only the pure runtime fixture contract.'
    Assert-Contract ([int]$runtimeContractStep.'timeout-minutes' -eq 5) 'The acceptance scenario matrix runtime contract step must have a 5-minute budget.'
    Assert-Contract ($null -eq $runtimeContractStep.PSObject.Properties['if']) 'The acceptance scenario matrix runtime contract step must not have its own condition.'

    $equivalenceContractSteps = @($scriptGovernanceSteps | Where-Object {
            [string]::Equals([string]$_.name, 'Test acceptance scenario matrix equivalence contract', [StringComparison]::Ordinal)
        })
    Assert-Contract ($equivalenceContractSteps.Count -eq 1) 'Script Governance must contain exactly one independent acceptance scenario matrix equivalence contract step.'
    $equivalenceContractStep = $equivalenceContractSteps[0]
    Assert-Contract ([string]::Equals([string]$equivalenceContractStep.shell, 'pwsh', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$equivalenceContractStep.run, './scripts/tests/acceptance-scenario-matrix-equivalence.Tests.ps1', [StringComparison]::Ordinal) -and
        [int]$equivalenceContractStep.'timeout-minutes' -eq 5 -and
        $null -eq $equivalenceContractStep.PSObject.Properties['if']) 'The equivalence fixture contract must run as one unconditional five-minute pwsh step.'

    Assert-Contract ($scriptGovernanceStepTimeouts.Count -eq $scriptGovernanceSteps.Count -and $scriptGovernanceStepTimeouts[0] -eq 3 -and $fiveMinuteStepCount -eq ($scriptGovernanceSteps.Count - 1)) 'Script Governance budget comment contract expects one three-minute checkout and all remaining steps to have five-minute timeouts.'
    Assert-Contract ($workflowSource.Contains($expectedBudgetHeadline, [StringComparison]::Ordinal) -and $workflowSource.Contains($expectedBudgetContinuation, [StringComparison]::Ordinal)) "Script Governance budget comment must match its actual $($scriptGovernanceSteps.Count)-step/$($scriptGovernanceStepBudgetMinutes)m structure."
    Assert-Contract (-not $workflowSource.Contains('实际为 103m', [StringComparison]::Ordinal)) 'Script Governance budget comment must not retain the obsolete 103m historical sentence.'

    $planningJobProperties = @($parsedWorkflow.jobs.PSObject.Properties | Where-Object {
            [string]::Equals([string]$_.Name, 'acceptance-scenario-matrix-planning', [StringComparison]::Ordinal)
        })
    Assert-Contract ($planningJobProperties.Count -eq 1) 'CI must define exactly one acceptance-scenario-matrix-planning job.'
    $planningJob = $planningJobProperties[0].Value
    Assert-Contract ([string]::Equals([string]$planningJob.name, 'Business FullChain Acceptance / Planning', [StringComparison]::Ordinal)) 'The planning job must retain its physical Actions name.'
    Assert-Contract ([string]::Equals([string]$planningJob.'runs-on', 'ubuntu-latest', [StringComparison]::Ordinal)) 'The planning job must run on ubuntu-latest.'
    $planningNeeds = @($planningJob.needs | ForEach-Object { [string]$_ })
    Assert-Contract ($planningNeeds.Count -eq 1 -and [string]::Equals($planningNeeds[0], 'impact-plan', [StringComparison]::Ordinal)) 'The planning job must need exactly impact-plan.'
    $fullChainSelectionPolicy = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}"
    Assert-Contract ([string]::Equals([string]$planningJob.if, $fullChainSelectionPolicy, [StringComparison]::Ordinal)) 'The planning job must preserve conservative FullChain selection and skip only explicit full_chain=false on a successful PR impact plan.'
    $expectedPlanningOutputs = [ordered]@{
        'sales-order-demand-selected' = '${{ steps.plan.outputs.sales-order-demand-selected }}'
        'tested-sha' = '${{ steps.plan.outputs.tested-sha }}'
        'manifest-digest' = '${{ steps.plan.outputs.manifest-digest }}'
        'artifact-digest' = '${{ steps.plan.outputs.artifact-digest }}'
    }
    foreach ($outputName in $expectedPlanningOutputs.Keys) {
        $outputProperty = $planningJob.outputs.PSObject.Properties[$outputName]
        Assert-Contract ($null -ne $outputProperty -and [string]::Equals([string]$outputProperty.Value, [string]$expectedPlanningOutputs[$outputName], [StringComparison]::Ordinal)) "Planning output '$outputName' must come from the generated planning artifact step."
    }

    $planningSteps = @($planningJob.steps)
    Assert-Contract ($planningSteps.Count -eq 5) 'The planning job must contain only checkout, .NET setup, conditional impact-plan download, planning, and planning-artifact upload.'
    Assert-Contract (@($planningSteps | Where-Object { $null -eq $_.PSObject.Properties['timeout-minutes'] -or [int]$_.'timeout-minutes' -le 0 }).Count -eq 0) 'Every planning job step must have a positive explicit timeout.'
    $planningStepBudget = (@($planningSteps | ForEach-Object { [int]$_.'timeout-minutes' }) | Measure-Object -Sum).Sum
    Assert-Contract ([int]$planningJob.'timeout-minutes' -gt $planningStepBudget) 'The planning job timeout must strictly exceed the sum of explicit step budgets so action post steps retain margin.'

    $checkoutSteps = @($planningSteps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/checkout@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($checkoutSteps.Count -eq 1) 'The planning job must checkout the tested repository exactly once.'
    $dotnetSetupSteps = @($planningSteps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/setup-dotnet@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($dotnetSetupSteps.Count -eq 1 -and [string]::Equals([string]$dotnetSetupSteps[0].with.'dotnet-version', '10.0.x', [StringComparison]::Ordinal)) 'The planning job must setup the governed .NET 10 SDK exactly once.'
    $impactDownloadSteps = @($planningSteps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/download-artifact@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($impactDownloadSteps.Count -eq 1) 'The planning job must conditionally download the CI impact-plan artifact exactly once.'
    $impactDownloadStep = $impactDownloadSteps[0]
    Assert-Contract ([string]::Equals([string]$impactDownloadStep.if, "`${{ github.event_name == 'pull_request' && needs.impact-plan.result == 'success' }}", [StringComparison]::Ordinal)) 'The planning job must download impact-plan only for a PR whose impact plan succeeded.'
    Assert-Contract ([string]::Equals([string]$impactDownloadStep.with.name, 'ci-impact-plan-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'The planning job must download the current run/attempt impact-plan artifact.'
    Assert-Contract ([string]::Equals([string]$impactDownloadStep.with.path, 'artifacts/ci-impact-plan', [StringComparison]::Ordinal)) 'The planning job must download impact-plan into its governed repository artifact path.'

    $planningRunSteps = @($planningSteps | Where-Object { [string]::Equals([string]$_.name, 'Plan acceptance scenario matrix', [StringComparison]::Ordinal) })
    Assert-Contract ($planningRunSteps.Count -eq 1) 'The planning job must contain exactly one Plan acceptance scenario matrix step.'
    $planningRunStep = $planningRunSteps[0]
    Assert-Contract (([int]$planningRunStep.'timeout-minutes' * 60) -gt 1500) 'The planning step timeout must strictly contain the current 1500-second one-project worst case.'
    Assert-Contract ([string]::Equals([string]$planningRunStep.shell, 'pwsh', [StringComparison]::Ordinal)) 'The planning step must use pwsh.'
    Assert-Contract (([string]$planningRunStep.run).Contains('scripts/plan-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)) 'The planning step must invoke scripts/plan-acceptance-scenario-matrix.ps1.'
    $planningManifest = Import-NervAcceptanceScenarioMatrixManifest `
        -ManifestPath (Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json') `
        -V1ManifestPath (Join-Path $repoRoot 'scripts/full-chain-test-lane.json') `
        -RepositoryRoot $repoRoot
    $planningProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($scenario in @($planningManifest.scenarios | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) })) {
        foreach ($testProject in @($scenario.testProjects)) { [void]$planningProjects.Add([string]$testProject.path) }
    }
    Assert-Contract ($planningProjects.Count -eq 1) 'The current active/core planning selection must still aggregate to one unique test project.'
    $planningWorkflowBudget = Get-NervAcceptancePlanningWorkflowBudget `
        -WorkflowPath $Path `
        -JobName 'acceptance-scenario-matrix-planning' `
        -StepName 'Plan acceptance scenario matrix'
    [void](Assert-NervAcceptancePlanningBudgetFits `
        -PlanningBudget $planningManifest.planningBudget `
        -UniqueProjectCount $planningProjects.Count `
        -StepTimeoutSeconds $planningWorkflowBudget.stepTimeoutSeconds)

    $planningUploads = @($planningSteps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and
            [string]::Equals([string]$usesProperty.Value, 'actions/upload-artifact@v4', [StringComparison]::Ordinal) -and
            [string]::Equals([string]$_.with.name, 'acceptance-scenario-matrix-plan-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal)
        })
    Assert-Contract ($planningUploads.Count -eq 1) 'The planning job must upload exactly one current run/attempt planning artifact.'
    Assert-Contract ([string]::Equals([string]$planningUploads[0].with.'if-no-files-found', 'error', [StringComparison]::Ordinal) -and [int]$planningUploads[0].with.'retention-days' -eq 14) 'The planning artifact must fail closed when absent and retain for 14 days.'

    $planningRunSurface = (@($planningSteps | ForEach-Object {
                $runProperty = $_.PSObject.Properties['run']
                if ($null -ne $runProperty) { [string]$runProperty.Value }
            }) -join "`n")
    foreach ($forbiddenPlanningCommand in @('docker', 'psql', 'redis-cli', 'aspire', 'nerv.ps1', 'run-full-chain-test-lane.ps1', 'run-acceptance-scenario-matrix.ps1')) {
        Assert-Contract (-not $planningRunSurface.Contains($forbiddenPlanningCommand, [StringComparison]::OrdinalIgnoreCase)) "The pure planning job must not invoke '$forbiddenPlanningCommand'."
    }

    $runtimeJobProperty = $parsedWorkflow.jobs.PSObject.Properties['acceptance-scenario-matrix-runtime']
    Assert-Contract ($null -ne $runtimeJobProperty) 'CI must define the hosted acceptance-scenario-matrix-runtime job.'
    $runtimeJob = $runtimeJobProperty.Value
    Assert-Contract ([string]::Equals([string]$runtimeJob.name, 'Business FullChain Acceptance / sales-order-demand', [StringComparison]::Ordinal)) 'The hosted runtime must expose the exact sales-order-demand Actions name.'
    Assert-Contract ([string]::Equals([string]$runtimeJob.needs, 'acceptance-scenario-matrix-planning', [StringComparison]::Ordinal)) 'The hosted runtime must need planning.'
    Assert-Contract ([string]::Equals([string]$runtimeJob.if, "`${{ !cancelled() && needs.acceptance-scenario-matrix-planning.result == 'success' && needs.acceptance-scenario-matrix-planning.outputs.sales-order-demand-selected == 'true' }}", [StringComparison]::Ordinal)) 'The hosted runtime must run only for a successful plan that selected sales-order-demand.'
    $runtimeSteps = @($runtimeJob.steps)
    Assert-Contract (@($runtimeSteps | Where-Object { $null -eq $_.PSObject.Properties['timeout-minutes'] -or [int]$_.'timeout-minutes' -le 0 }).Count -eq 0) 'Every hosted runtime step must have a positive explicit timeout.'
    $runtimeImageSteps = @($runtimeSteps | Where-Object { [string]::Equals([string]$_.name, 'Prepare shadow dependency images', [StringComparison]::Ordinal) })
    Assert-Contract ($runtimeImageSteps.Count -eq 1) 'The hosted shadow runtime must prepare its PostgreSQL and Redis images exactly once before the governed runner.'
    $runtimeImageStep = $runtimeImageSteps[0]
    Assert-Contract ([int]$runtimeImageStep.'timeout-minutes' -eq 10 -and
        [string]::Equals([string]$runtimeImageStep.shell, 'bash --noprofile --norc -euo pipefail {0}', [StringComparison]::Ordinal)) 'The hosted shadow image preparation must use one fail-fast ten-minute bash step.'
    $runtimeImageMatches = @([regex]::Matches([string]$runtimeImageStep.run, '(?:docker image inspect|docker pull) (?<image>[a-z]+:[^\s;]+)') | ForEach-Object { [string]$_.Groups['image'].Value })
    Assert-Contract ([string]::Equals(($runtimeImageMatches -join '|'), 'postgres:18|postgres:18|redis:8|redis:8', [StringComparison]::Ordinal)) 'The hosted shadow image preparation must inspect/pull exactly postgres:18 and redis:8.'
    foreach ($boundedRetryFragment in @('timeout --kill-after=10 75 docker pull postgres:18', 'timeout --kill-after=10 75 docker pull redis:8', 'if [ "${docker_attempt}" -ge 3 ]', 'sleep 15')) {
        Assert-Contract (([string]$runtimeImageStep.run).Contains($boundedRetryFragment, [StringComparison]::Ordinal)) "The hosted shadow image preparation is missing bounded retry fragment '$boundedRetryFragment'."
    }
    $runtimeStepBudget = (@($runtimeSteps | ForEach-Object { [int]$_.'timeout-minutes' }) | Measure-Object -Sum).Sum
    Assert-Contract ($runtimeStepBudget -eq 99 -and [int]$runtimeJob.'timeout-minutes' -eq 110 -and ([int]$runtimeJob.'timeout-minutes' - $runtimeStepBudget) -eq 11) 'The hosted runtime must retain the complete 99-minute explicit budget inside a 110-minute job with 11 minutes for action setup/post overhead.'
    $runtimeRunStep = @($runtimeSteps | Where-Object { [string]::Equals([string]$_.name, 'Run acceptance scenario matrix', [StringComparison]::Ordinal) })
    Assert-Contract ($runtimeRunStep.Count -eq 1 -and ([int]$runtimeRunStep[0].'timeout-minutes' * 60) -gt 2220) 'The hosted runtime step timeout must strictly exceed the governed 2220-second scenario budget.'
    Assert-Contract ([Array]::IndexOf($runtimeSteps, $runtimeImageStep) -lt [Array]::IndexOf($runtimeSteps, $runtimeRunStep[0])) 'The hosted shadow dependency images must be prepared before the governed runtime starts.'
    $runtimeRun = [string]$runtimeRunStep[0].run
    Assert-Contract ($runtimeRun.Contains("`$artifactPath = [IO.Path]::GetFullPath('artifacts/acceptance-scenario-matrix/planning.json')", [StringComparison]::Ordinal) -and
        $runtimeRun.Contains('-ArtifactPath $artifactPath', [StringComparison]::Ordinal)) 'The hosted shadow adapter must pass one canonical absolute planning artifact path to the raw runtime boundary.'
    foreach ($requiredRuntimeArgument in @(
            '-ArtifactPath $artifactPath',
            '-ExpectedArtifactDigest $artifactDigest',
            '-ExpectedManifestDigest $manifestDigest',
            "-Repository '`${{ github.repository }}'",
            "-TestedSha '`${{ needs.acceptance-scenario-matrix-planning.outputs.tested-sha }}'",
            "-RunId '`${{ github.run_id }}'",
            "-RunAttempt '`${{ github.run_attempt }}'",
            "-Event '`${{ github.event_name }}'",
            '-SummaryPath artifacts/acceptance-scenario-matrix/shadow/runtime-summary.json',
            '-CanonicalResultPath artifacts/acceptance-scenario-matrix/shadow/sales-order-demand-result.json',
            "-TrackIdentifier 'shadow'"
        )) {
        Assert-Contract ($runtimeRun.Contains($requiredRuntimeArgument, [StringComparison]::Ordinal)) "Hosted runtime invocation is missing '$requiredRuntimeArgument'."
    }
    $runtimeUploads = @($runtimeSteps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/upload-artifact@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($runtimeUploads.Count -ge 4 -and @($runtimeUploads | Where-Object { -not [string]::Equals([string]$_.with.'if-no-files-found', 'error', [StringComparison]::Ordinal) -or [int]$_.with.'retention-days' -ne 14 }).Count -eq 0) 'Hosted runtime summary, canonical, business/cleanup, and failure diagnostics uploads must fail closed and retain 14 days.'

    $equivalenceJobProperty = $parsedWorkflow.jobs.PSObject.Properties['acceptance-scenario-matrix-equivalence']
    Assert-Contract ($null -ne $equivalenceJobProperty) 'CI must define the internal three-track equivalence job.'
    $equivalenceJob = $equivalenceJobProperty.Value
    $equivalenceNeeds = @($equivalenceJob.needs | ForEach-Object { [string]$_ })
    Assert-Contract ([string]::Equals(($equivalenceNeeds -join '|'), 'acceptance-scenario-matrix-planning|business-full-chain-acceptance-v1|acceptance-scenario-matrix-runtime|erp-sales-order-demand-acceptance', [StringComparison]::Ordinal)) 'Three-track equivalence must inspect planning, v1, shadow, and legacy ERP prerequisites.'
    Assert-Contract ([string]::Equals([string]$equivalenceJob.if, "`${{ !cancelled() && needs.acceptance-scenario-matrix-planning.result == 'success' && needs.acceptance-scenario-matrix-planning.outputs.sales-order-demand-selected == 'true' }}", [StringComparison]::Ordinal)) 'Three-track equivalence must still run to inspect prerequisite failures without treating them as green.'
    $equivalenceSurface = (@($equivalenceJob.steps | ForEach-Object {
                foreach ($propertyName in @('run', 'with')) {
                    $property = $_.PSObject.Properties[$propertyName]
                    if ($null -ne $property) {
                        if ([string]::Equals($propertyName, 'run', [StringComparison]::Ordinal)) { [string]$property.Value }
                        else { [string]$property.Value.name; [string]$property.Value.path }
                    }
                }
            }) -join "`n")
    foreach ($requiredEquivalenceFragment in @('verify-acceptance-scenario-matrix-equivalence.ps1', 'acceptance-scenario-matrix-result-v1-${{ github.run_id }}-${{ github.run_attempt }}', 'acceptance-scenario-matrix-result-shadow-${{ github.run_id }}-${{ github.run_attempt }}', 'erp-sales-order-demand-${{ github.run_id }}-${{ github.run_attempt }}')) {
        Assert-Contract ($equivalenceSurface.Contains($requiredEquivalenceFragment, [StringComparison]::Ordinal)) "Three-track equivalence workflow is missing '$requiredEquivalenceFragment'."
    }

    $plannerInvocations = @(
        foreach ($jobProperty in $parsedWorkflow.jobs.PSObject.Properties) {
            foreach ($step in @($jobProperty.Value.steps)) {
                $runProperty = $step.PSObject.Properties['run']
                if ($null -ne $runProperty -and ([string]$runProperty.Value).Contains('scripts/plan-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)) {
                    [pscustomobject]@{ Job = $jobProperty.Name; Step = [string]$step.name }
                }
            }
        }
    )
    Assert-Contract ($plannerInvocations.Count -eq 1 -and
        [string]::Equals([string]$plannerInvocations[0].Job, 'acceptance-scenario-matrix-planning', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$plannerInvocations[0].Step, 'Plan acceptance scenario matrix', [StringComparison]::Ordinal)) 'Only the physical planning job may execute the acceptance scenario matrix planner.'

    $runtimeInvocations = @(
        foreach ($jobProperty in $parsedWorkflow.jobs.PSObject.Properties) {
            foreach ($step in @($jobProperty.Value.steps)) {
                $runProperty = $step.PSObject.Properties['run']
                if ($null -ne $runProperty -and ([string]$runProperty.Value).Contains('scripts/run-acceptance-scenario-matrix.ps1', [StringComparison]::Ordinal)) {
                    [pscustomobject]@{ Job = $jobProperty.Name; Step = [string]$step.name }
                }
            }
        }
    )
    Assert-Contract ($runtimeInvocations.Count -eq 1 -and
        [string]::Equals([string]$runtimeInvocations[0].Job, 'acceptance-scenario-matrix-runtime', [StringComparison]::Ordinal) -and
        [string]::Equals([string]$runtimeInvocations[0].Step, 'Run acceptance scenario matrix', [StringComparison]::Ordinal)) 'Only the hosted shadow job may execute the acceptance scenario matrix runtime runner.'
}

function Assert-ImpactCase {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Paths,
        [Parameter(Mandatory)] [hashtable] $Flags,
        [string[]] $Services = @()
    )

    $plan = Get-NervCiImpactPlan -ChangedPaths $Paths
    Assert-Contract ([int]$plan.schema_version -eq 1) "Case '$Name' must use impact-plan schema version 1."
    $expectedPathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Paths) { [void]$expectedPathSet.Add(($path -replace '\\', '/')) }
    $expectedPaths = @($expectedPathSet)
    [Array]::Sort($expectedPaths, [StringComparer]::Ordinal)
    Assert-Contract ([string]::Equals((@($plan.changed_paths) -join '|'), ($expectedPaths -join '|'), [StringComparison]::Ordinal)) "Case '$Name' must normalize, deduplicate, and ordinally sort changed paths."
    foreach ($flag in $Flags.GetEnumerator()) {
        Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Key) -Expected ([bool]$flag.Value)
    }
    if ($PSBoundParameters.ContainsKey('Services')) {
        $expectedServices = @($Services)
        [Array]::Sort($expectedServices, [StringComparer]::Ordinal)
        Assert-Contract ([string]::Equals((@($plan.business_services) -join '|'), ($expectedServices -join '|'), [StringComparison]::Ordinal)) "Case '$Name' returned the wrong stable business service set: $(@($plan.business_services) -join ', ')."
    }
    foreach ($selectedFlag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] -and $_.Value })) {
        Assert-Contract ($null -ne $plan.reasons.PSObject.Properties[$selectedFlag.Name]) "Case '$Name' selected '$($selectedFlag.Name)' without an audit reason."
        Assert-Contract (@($plan.reasons.PSObject.Properties[$selectedFlag.Name].Value).Count -gt 0) "Case '$Name' selected '$($selectedFlag.Name)' with an empty audit reason."
    }
}

function Assert-PostgresLaneOwningPathsRoute {
    foreach ($owningPath in @(
            'scripts/run-postgres-test-lane.ps1',
            'scripts/lib/PostgresTestLane.ps1',
            'scripts/postgres-test-lane.json'
        )) {
        Assert-ImpactCase -Name "postgres-lane-owner-$([IO.Path]::GetFileName($owningPath))" -Paths @($owningPath) -Flags @{
            scripts = $true; postgresql = $true; redis_cap = $false; full_chain = $false
        }
    }
}

function Assert-BackendFastLaneControlInputsRoute {
    foreach ($backendFastLaneControlInput in @(
            'scripts/backend-test-shards.json',
            'scripts/test-evidence-policy.json',
            'scripts/run-backend-test-shard.ps1',
            'scripts/verify-backend-test-shards.ps1',
            'scripts/verify-backend-real-postgres-tests.ps1',
            'scripts/verify-business-full-chain-acceptance.ps1',
            'scripts/verify-business-performance-baseline.ps1',
            'scripts/lib/OrdinalString.ps1',
            'scripts/lib/BackendTestShardSelectors.ps1',
            'scripts/lib/BackendTestShardDiagnostics.ps1',
            'scripts/tests/backend-test-shards.Tests.ps1'
        )) {
        Assert-ImpactCase -Name "backend-fast-lane-control-$([IO.Path]::GetFileName($backendFastLaneControlInput))" -Paths @($backendFastLaneControlInput) -Flags @{
            scripts = $true; backend = $true
        }
    }
}

function Assert-RedisCapLaneOwningPathsRoute {
    foreach ($owningPath in @(
            'scripts/run-redis-cap-test-lane.ps1',
            'scripts/lib/RedisCapTestLane.ps1',
            'scripts/redis-cap-test-lane.json'
        )) {
        Assert-ImpactCase -Name "redis-cap-lane-owner-$([IO.Path]::GetFileName($owningPath))" -Paths @($owningPath) -Flags @{
            scripts = $true; postgresql = $false; redis_cap = $true; full_chain = $false
        }
    }
}

function Assert-FullChainLaneOwningPathsRoute {
    foreach ($owningPath in @(
            'scripts/run-full-chain-test-lane.ps1',
            'scripts/lib/FullChainTestLane.ps1',
            'scripts/full-chain-test-lane.json',
            'scripts/tests/full-chain-test-lane.Tests.ps1',
            'scripts/verify-erp-sales-order-demand-planning.ps1',
            'scripts/verify-erp-wms-delivery-completion.ps1'
        )) {
        Assert-ImpactCase -Name "full-chain-lane-owner-$([IO.Path]::GetFileName($owningPath))" -Paths @($owningPath) -Flags @{
            scripts = $true; backend = $true; postgresql = $false; redis_cap = $false; full_chain = $true
        }
    }
}

function Assert-AcceptanceScenarioMatrixOwningPathsRoute {
    $expectedSelectedFlags = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('scripts', 'backend', 'full_chain'),
        [StringComparer]::Ordinal)

    foreach ($owningPath in $acceptanceScenarioMatrixOwningPaths) {
        $plan = Get-NervCiImpactPlan -ChangedPaths @($owningPath)
        foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
            Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected $expectedSelectedFlags.Contains([string]$flag.Name)
        }
        Assert-Contract (@($plan.business_services).Count -eq 0) "Acceptance scenario matrix owner '$owningPath' must not select business services."
    }
}

function Assert-AcceptanceScenarioMatrixRuntimeOwningPathsRoute {
    $expectedSelectedFlags = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('scripts', 'backend', 'full_chain', 'erp_sales_order_demand'),
        [StringComparer]::Ordinal)

    foreach ($owningPath in $acceptanceScenarioMatrixRuntimeOwningPaths) {
        $plan = Get-NervCiImpactPlan -ChangedPaths @($owningPath)
        foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
            Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected $expectedSelectedFlags.Contains([string]$flag.Name)
        }
        Assert-Contract (@($plan.business_services).Count -eq 0) "Acceptance scenario matrix runtime owner '$owningPath' must not select business services."
    }
}

function Assert-AcceptanceScenarioMatrixRuntimePathMutationsDoNotAliasOwners {
    foreach ($owningPath in $acceptanceScenarioMatrixRuntimeOwningPaths) {
        $leafIndex = $owningPath.LastIndexOf('/', [StringComparison]::Ordinal) + 1
        $firstLeafCharacter = [string]$owningPath[$leafIndex]
        $wrongCaseCharacter = if ([char]::IsUpper($firstLeafCharacter[0])) { $firstLeafCharacter.ToLowerInvariant() } else { $firstLeafCharacter.ToUpperInvariant() }
        $wrongCasePath = $owningPath.Substring(0, $leafIndex) + $wrongCaseCharacter + $owningPath.Substring($leafIndex + 1)
        foreach ($mutatedPath in @(
                $wrongCasePath,
                $owningPath.Replace('scripts/', 'scripts/./'),
                $owningPath.Replace('.ps1', '.legacy.ps1')
            )) {
            $plan = Get-NervCiImpactPlan -ChangedPaths @($mutatedPath)
            foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
                Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected ([string]::Equals([string]$flag.Name, 'scripts', [StringComparison]::Ordinal))
            }
            Assert-Contract (@($plan.business_services).Count -eq 0) "Acceptance scenario matrix runtime path mutation '$mutatedPath' must not select business services."
        }
    }
}

Assert-Contract (Test-Path -LiteralPath $libraryPath -PathType Leaf) 'The CI impact-plan library is missing.'
. $libraryPath

Assert-ImpactCase -Name 'pure-docs' -Paths @('README.md', 'docs/architecture/context-map.md') -Flags @{
    docs = $true; backend = $false; frontend = $false; scripts = $false; connector_hosts = $false; postgresql = $false; full_chain = $false; erp_sales_order_demand = $false
}

Assert-ImpactCase -Name 'script-governance-registry' -Paths @('docs/architecture/script-automation-governance.md') -Flags @{
    docs = $true; scripts = $true; backend = $false; frontend = $false
}

Assert-ImpactCase -Name 'nested-readme-docs' -Paths @('backend/services/Business/Erp/README.md', 'connector-hosts/README.md') -Flags @{
    docs = $true; backend = $false; frontend = $false; connector_hosts = $false; postgresql = $false; full_chain = $false; erp_sales_order_demand = $false
}

Assert-ImpactCase -Name 'frontend-package-markdown' -Paths @('frontend/packages/scheduling/README.md') -Flags @{
    docs = $true; frontend = $true; frontend_packages = $true; backend = $false; postgresql = $false; full_chain = $false
}

Assert-ImpactCase -Name 'frontend-guidance-markdown' -Paths @('frontend/AGENTS.md') -Flags @{
    docs = $true; frontend = $true; frontend_apps = $false; frontend_packages = $false; backend = $false
}

Assert-ImpactCase -Name 'single-business-service' -Paths @('backend/services/Business/Erp/src/Orders.cs') -Flags @{
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend_packages = $true; connector_hosts = $false; postgresql = $true; redis_cap = $false; full_chain = $false; erp_sales_order_demand = $true
} -Services @('erp')

Assert-ImpactCase -Name 'product-engineering-service-name' -Paths @('backend/services/Business/ProductEngineering/src/Release.cs') -Flags @{
    backend = $true; business_gateway = $true; erp_sales_order_demand = $false
} -Services @('product-engineering')

foreach ($erpService in @('Erp', 'DemandPlanning', 'MasterData')) {
    Assert-ImpactCase -Name "erp-acceptance-service-$erpService" -Paths @("backend/services/Business/$erpService/src/ObservedChange.cs") -Flags @{
        backend = $true; erp_sales_order_demand = $true
    }
}

Assert-ImpactCase -Name 'common-contract-expansion' -Paths @('backend/common/Contracts/IntegrationEvents.cs') -Flags @{
    backend = $true; backend_contracts = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true; connector_hosts = $true; postgresql = $true; redis_cap = $true; full_chain = $true; erp_sales_order_demand = $true
}

foreach ($sharedCase in @(
        @{ Name = 'testing'; Path = 'backend/common/Testing/PostgresFixture.cs'; Flag = 'backend_testing'; Redis = $false },
        @{ Name = 'persistence'; Path = 'backend/common/Persistence/UnitOfWork.cs'; Flag = 'backend_persistence'; Redis = $false },
        @{ Name = 'messaging'; Path = 'backend/common/Messaging/CapPublisher.cs'; Flag = 'backend_messaging'; Redis = $true }
    )) {
    $flags = @{ backend = $true; business_gateway = $true; postgresql = $true; full_chain = $true; redis_cap = [bool]$sharedCase.Redis }
    $flags[[string]$sharedCase.Flag] = $true
    Assert-ImpactCase -Name "shared-$($sharedCase.Name)" -Paths @([string]$sharedCase.Path) -Flags $flags
}

$backendCommonDirectories = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'backend/common') -Directory | ForEach-Object { $_.Name })
Assert-Contract ($backendCommonDirectories.Count -eq 11) 'The backend common-directory observation baseline must be revised when a shared directory is added or removed.'
foreach ($commonDirectory in $backendCommonDirectories) {
    $plan = Get-NervCiImpactPlan -ChangedPaths @("backend/common/$commonDirectory/ObservedChange.cs")
    Assert-ImpactFlag -Plan $plan -Name 'backend' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'business_gateway' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'connector_hosts' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'full_chain' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'erp_sales_order_demand' -Expected $true
    Assert-Contract (@($plan.business_services).Count -gt 10) "Shared backend directory '$commonDirectory' must conservatively expand to every known business service."
}

Assert-ImpactCase -Name 'business-gateway' -Paths @('backend/gateway/BusinessGateway/src/Facade.cs') -Flags @{
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true
}

foreach ($backendBuildInput in @('backend/Directory.Build.props', 'backend/Directory.Packages.props')) {
    Assert-ImpactCase -Name "openapi-backend-build-input-$([IO.Path]::GetFileName($backendBuildInput))" -Paths @($backendBuildInput) -Flags @{
        backend = $true; openapi_codegen = $true; connector_hosts = $true; erp_sales_order_demand = $true
    }
}

foreach ($erpAcceptanceInput in @(
        'scripts/verify-erp-sales-order-demand-planning.ps1',
        'backend/tests/Nerv.IIP.Business.FullChain.Tests/Scenario.cs',
        'infra/docker-compose.dev.yml'
    )) {
    Assert-ImpactCase -Name "erp-acceptance-input-$([IO.Path]::GetFileName($erpAcceptanceInput))" -Paths @($erpAcceptanceInput) -Flags @{
        erp_sales_order_demand = $true
    }
}

foreach ($sharedControlInput in @('NuGet.config', 'scripts/lib/ScriptAutomation.ps1')) {
    $plan = Get-NervCiImpactPlan -ChangedPaths @($sharedControlInput)
    foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
        Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected $true
    }
    Assert-Contract ([string]::Equals(
            (@($plan.business_services) -join '|'),
            'approval|barcode-label|demand-planning|erp|industrial-telemetry|inventory|maintenance|master-data|mes|product-engineering|quality|scheduling|wms',
            [StringComparison]::Ordinal)) "Shared control input '$sharedControlInput' must conservatively select every known business service."
}

Assert-ImpactCase -Name 'project-skill-source' -Paths @('skills/nerv-pr-review/agents/openai.yaml') -Flags @{
    docs = $true; backend = $false; frontend = $false; scripts = $false; workflows = $false
}

Assert-ImpactCase -Name 'agent-harness-configuration' -Paths @(
    'skills-lock.json',
    't3.json',
    '.claude/settings.json',
    '.claude/launch.json',
    '.codex/config.toml',
    '.codex/environments/environment.toml') -Flags @{
    docs = $true
    backend = $false; frontend = $false; scripts = $false; workflows = $false; infra = $false
    connector_hosts = $false; business_gateway = $false; openapi_codegen = $false
    postgresql = $false; redis_cap = $false; full_chain = $false; erp_sales_order_demand = $false
} -Services @()

Assert-ImpactCase -Name 'github-issue-templates' -Paths @(
    '.github/ISSUE_TEMPLATE/bug_report.yml',
    '.github/ISSUE_TEMPLATE/config.yml') -Flags @{
    docs = $true
    backend = $false; frontend = $false; scripts = $false; workflows = $false
    postgresql = $false; redis_cap = $false; full_chain = $false; erp_sales_order_demand = $false
} -Services @()

# Routing an agent-tooling path must not weaken workflow routing: '.github/workflows/**'
# still fails open even though a sibling '.github/' prefix is now classified.
Assert-ImpactCase -Name 'workflow-still-fails-open-beside-issue-templates' -Paths @('.github/workflows/nightly.yml') -Flags @{
    docs = $true; backend = $true; frontend = $true; scripts = $true; workflows = $true
    postgresql = $true; redis_cap = $true; full_chain = $true; erp_sales_order_demand = $true
}

# Root-level runtime and build inputs must keep failing open. Anything moved out of this
# contract silently narrows CI coverage, so the erosion has to break this test first.
$conservativeRootInputs = @(
    '.gitattributes',
    '.gitignore',
    '.node-version',
    'aspire.config.json',
    'dotnet-tools.json',
    'nerv.ps1'
)
foreach ($conservativeRootInput in $conservativeRootInputs) {
    $plan = Get-NervCiImpactPlan -ChangedPaths @($conservativeRootInput)
    foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
        Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected $true
    }
}

# Completeness: the unclassified fail-open set is enumerated from the tracked path space,
# not from whichever path a reviewer happened to name. A new tracked path that no rule
# claims reds this test, forcing an explicit classify-or-declare decision.
$trackedPaths = @(& git -C $repoRoot ls-files)
Assert-Contract ($trackedPaths.Count -gt 0) 'Tracked path enumeration returned nothing; the completeness contract cannot be evaluated.'
$trackedPlan = Get-NervCiImpactPlan -ChangedPaths $trackedPaths
$unclassifiedReasons = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($reasonProperty in $trackedPlan.reasons.PSObject.Properties) {
    foreach ($reasonValue in @($reasonProperty.Value)) {
        if ([string]$reasonValue -like 'unclassified-path:*') {
            [void]$unclassifiedReasons.Add(([string]$reasonValue).Substring('unclassified-path:'.Length))
        }
    }
}
$observedUnclassified = @($unclassifiedReasons)
[Array]::Sort($observedUnclassified, [StringComparer]::Ordinal)
$expectedUnclassified = @($conservativeRootInputs)
[Array]::Sort($expectedUnclassified, [StringComparer]::Ordinal)
Assert-Contract ([string]::Equals(
        ($observedUnclassified -join '|'),
        ($expectedUnclassified -join '|'),
        [StringComparison]::Ordinal)) "Tracked paths falling through to unclassified fail-open must equal the declared conservative set. Observed: $($observedUnclassified -join ', ')."

Assert-BackendFastLaneControlInputsRoute

Assert-ImpactCase -Name 'frontend-app' -Paths @('frontend/apps/screen/src/App.vue') -Flags @{
    frontend = $true; frontend_apps = $true; backend = $false; scripts = $false
}

Assert-ImpactCase -Name 'frontend-docs-app' -Paths @('frontend/apps/docs/src/Guide.vue') -Flags @{
    frontend = $true; frontend_apps = $true; frontend_docs = $true; docs = $true
}

Assert-ImpactCase -Name 'frontend-docs-markdown' -Paths @('frontend/apps/docs/docs/index.md') -Flags @{
    frontend = $true; frontend_apps = $true; frontend_docs = $true; docs = $true
}

Assert-ImpactCase -Name 'frontend-design-system-app' -Paths @('frontend/apps/design-system/src/App.vue') -Flags @{
    frontend = $true; frontend_apps = $true; frontend_design_system = $true
}

Assert-ImpactCase -Name 'frontend-design-system-markdown' -Paths @('frontend/apps/design-system/docs/index.md') -Flags @{
    frontend = $true; frontend_apps = $true; frontend_design_system = $true
}

Assert-ImpactCase -Name 'frontend-api-client' -Paths @('frontend/packages/api-client/src/generated.ts') -Flags @{
    frontend = $true; frontend_packages = $true; openapi_codegen = $true; business_gateway = $true
}

foreach ($frontendBuildInput in @('frontend/package.json', 'frontend/pnpm-lock.yaml', 'frontend/pnpm-workspace.yaml')) {
    Assert-ImpactCase -Name "openapi-frontend-build-input-$([IO.Path]::GetFileName($frontendBuildInput))" -Paths @($frontendBuildInput) -Flags @{
        frontend = $true; frontend_packages = $true; openapi_codegen = $true
    }
}

Assert-ImpactCase -Name 'frontend-design-system' -Paths @('frontend/DESIGN/components/button.md') -Flags @{
    frontend = $true; frontend_design_system = $true; frontend_docs = $true; docs = $true
}

Assert-ImpactCase -Name 'connector-hosts' -Paths @('connector-hosts/src/Host.cs') -Flags @{
    connector_hosts = $true; backend = $false; frontend = $false; erp_sales_order_demand = $false
}

Assert-ImpactCase -Name 'openapi-generation-script' -Paths @('scripts/export-gateway-openapi.ps1') -Flags @{
    scripts = $true; openapi_codegen = $true; business_gateway = $true; frontend = $true; frontend_packages = $true
}

Assert-PostgresLaneOwningPathsRoute
Assert-RedisCapLaneOwningPathsRoute
Assert-FullChainLaneOwningPathsRoute
Assert-AcceptanceScenarioMatrixOwningPathsRoute
Assert-AcceptanceScenarioMatrixRuntimeOwningPathsRoute
Assert-AcceptanceScenarioMatrixRuntimePathMutationsDoNotAliasOwners

Assert-ImpactCase -Name 'platform-gateway-openapi' -Paths @('backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/OpenApi/GatewayOperationIdConvention.cs') -Flags @{
    backend = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true
}

Assert-ImpactCase -Name 'service-cap-integration' -Paths @('backend/services/Business/Mes/src/MesCapServiceCollectionExtensions.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
} -Services @('mes')

Assert-ImpactCase -Name 'integration-event-handler' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/WorkOrderCostCapitalizedIntegrationEventHandler.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
} -Services @('mes')

# NERV-1711 正例：集成事件转换器是跨服务契约的发信侧，业务服务与平台侧服务都必须
# 同时选中 redis_cap 与 full_chain。
Assert-ImpactCase -Name 'integration-event-converter-business' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
} -Services @('mes')

Assert-ImpactCase -Name 'integration-event-converter-platform-service' -Paths @('backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/AuditRecordedIntegrationEventConverter.cs') -Flags @{
    backend = $true; redis_cap = $true; full_chain = $true
}

# NERV-1711 正例：平台侧服务的集成事件处理器是收信侧，先前只选中 redis_cap，
# 现在必须同样跑 FullChain。
Assert-ImpactCase -Name 'integration-event-handler-platform-service' -Paths @('backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/AlertRaisedIntegrationEventHandler.cs') -Flags @{
    backend = $true; redis_cap = $true; full_chain = $true
}

# NERV-1711 正例：世界观种子是 FullChain lane 的数据基础，必须选中 full_chain；
# 种子本身不改 CAP 传输面，不得连带选中 redis_cap。
Assert-ImpactCase -Name 'world-history-seed-business' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/WorldHistorySeedService.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $true
} -Services @('mes')

Assert-ImpactCase -Name 'world-history-seed-platform-service' -Paths @('backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs') -Flags @{
    backend = $true; redis_cap = $false; full_chain = $true
}

# NERV-1711 反例：同前缀但不同目录不得触发，钉住「按目录段整段比对」而不是前缀包含。
Assert-ImpactCase -Name 'integration-event-converters-prefix-collision' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConvertersLegacy/LegacyShim.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('mes')

Assert-ImpactCase -Name 'seed-prefix-collision' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/SeedlingCatalog/SeedlingCatalogQuery.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('mes')

# NERV-1711 反例：同一服务的相邻 Application 子目录仍然只是普通后端改动，
# 钉住新规则没有退化成「任何 backend/services 路径都跑重 lane」。
Assert-ImpactCase -Name 'sibling-application-directory-stays-narrow' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/WorkOrderQuery.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('mes')

# NERV-1711 反例：测试工程里的同名目录不在 backend/services/ 之下，
# 钉住新规则带着服务前缀限定（处理器的 redis_cap 仍由既有 messaging 规则给出）。
Assert-ImpactCase -Name 'test-project-seed-directory-not-a-service' -Paths @('backend/tests/Nerv.IIP.Business.Mes.Tests/Application/Seed/SeedFixture.cs') -Flags @{
    backend = $true; redis_cap = $false; full_chain = $false
}

Assert-ImpactCase -Name 'test-project-integration-event-handlers-not-a-service' -Paths @('backend/tests/Nerv.IIP.Business.Mes.Tests/Application/IntegrationEventHandlers/HandlerTests.cs') -Flags @{
    backend = $true; redis_cap = $true; full_chain = $false
}

Assert-ImpactCase -Name 'capitalized-is-not-cap' -Paths @('backend/services/Business/Mes/src/CapitalizedUnitCost.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('mes')

Assert-ImpactCase -Name 'capacity-is-not-cap' -Paths @('backend/services/Business/Scheduling/src/FiniteCapacityScheduler.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('scheduling')

Assert-ImpactCase -Name 'full-chain-test' -Paths @('backend/tests/Nerv.IIP.Business.FullChain.Tests/Scenario.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true; erp_sales_order_demand = $true
}

Assert-ImpactCase -Name 'postgres-infra' -Paths @('infra/postgres/init.sql') -Flags @{
    infra = $true; postgresql = $true; redis_cap = $false
}

Assert-ImpactCase -Name 'aspire-topology' -Paths @('infra/aspire/Nerv.IIP.AppHost/Program.cs') -Flags @{
    infra = $true; postgresql = $true; redis_cap = $true; full_chain = $true
}

foreach ($fullSelectionPath in @('.github/workflows/ci.yml', 'scripts/lib/CiImpactPlan.ps1', 'scripts/tests/ci-impact-plan.Tests.ps1')) {
    $plan = Get-NervCiImpactPlan -ChangedPaths @($fullSelectionPath)
    foreach ($requiredFlag in @(
            'backend', 'frontend', 'scripts', 'docs', 'connector_hosts', 'workflows', 'infra',
            'backend_contracts', 'backend_testing', 'backend_persistence', 'backend_messaging',
            'business_gateway', 'openapi_codegen', 'frontend_apps', 'frontend_packages',
            'frontend_design_system', 'frontend_docs', 'postgresql', 'redis_cap', 'full_chain',
            'erp_sales_order_demand'
        )) {
        Assert-ImpactFlag -Plan $plan -Name $requiredFlag -Expected $true
    }
    Assert-Contract (@($plan.business_services).Count -gt 10) "Rule self-change '$fullSelectionPath' must select all known business services."
}

foreach ($invalidPaths in @(
        @(),
        @('../outside.txt'),
        @('/absolute/path.txt'),
        @('backend//common/Contracts.cs')
    )) {
    $failure = $null
    try { Get-NervCiImpactPlan -ChangedPaths $invalidPaths | Out-Null } catch { $failure = $_ }
    Assert-Contract ($null -ne $failure) "Invalid changed path set '$($invalidPaths -join ',')' must fail closed."
}

$ordinalStringRoutingClause = "            [string]::Equals(`$path, 'scripts/lib/OrdinalString.ps1', [StringComparison]::Ordinal) -or`n"
$canonicalImpactLibrary = [IO.File]::ReadAllText($libraryPath)
$weakenedImpactLibrary = $canonicalImpactLibrary.Replace($ordinalStringRoutingClause, '')
Assert-Contract (-not [string]::Equals($weakenedImpactLibrary, $canonicalImpactLibrary, [StringComparison]::Ordinal)) 'OrdinalString control-input mutation must remove the canonical routing clause.'
$ordinalMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-ordinal-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($ordinalMutationRoot) | Out-Null
    $ordinalMutationPath = Join-Path $ordinalMutationRoot 'CiImpactPlan.ps1'
    [IO.File]::WriteAllText($ordinalMutationPath, $weakenedImpactLibrary, [Text.UTF8Encoding]::new($false))
    . $ordinalMutationPath
    $ordinalMutationFailure = $null
    try { Assert-BackendFastLaneControlInputsRoute } catch { $ordinalMutationFailure = $_ }
    Assert-Contract ($null -ne $ordinalMutationFailure) 'Removing the OrdinalString control-input routing clause must fail the behavioral contract.'
}
finally {
    . $libraryPath
    if (Test-Path -LiteralPath $ordinalMutationRoot) { Remove-Item -LiteralPath $ordinalMutationRoot -Recurse -Force }
}

$postgresRoutingBlock = @'
            if ([string]::Equals($path, 'scripts/run-postgres-test-lane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/lib/PostgresTestLane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/postgres-test-lane.json', [StringComparison]::Ordinal)) {
                Select-Impact -Name 'postgresql' -Reason $reason
            }
'@
$weakenedImpactLibrary = $canonicalImpactLibrary.Replace($postgresRoutingBlock, '')
Assert-Contract (-not [string]::Equals($weakenedImpactLibrary, $canonicalImpactLibrary, [StringComparison]::Ordinal)) 'PostgreSQL owning-path mutation must remove the canonical routing branch.'
$impactMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-library-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($impactMutationRoot) | Out-Null
    $impactMutationPath = Join-Path $impactMutationRoot 'CiImpactPlan.ps1'
    [IO.File]::WriteAllText($impactMutationPath, $weakenedImpactLibrary, [Text.UTF8Encoding]::new($false))
    . $impactMutationPath
    $mutationFailure = $null
    try { Assert-PostgresLaneOwningPathsRoute } catch { $mutationFailure = $_ }
    Assert-Contract ($null -ne $mutationFailure) 'Removing the PostgreSQL owning-path routing branch must fail the behavioral contract.'
}
finally {
    . $libraryPath
    if (Test-Path -LiteralPath $impactMutationRoot) { Remove-Item -LiteralPath $impactMutationRoot -Recurse -Force }
}

$acceptanceScenarioMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-acceptance-scenario-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($acceptanceScenarioMutationRoot) | Out-Null
    foreach ($owningPath in $acceptanceScenarioMatrixOwningPaths) {
        $routingEntry = "            '$owningPath'`n"
        $weakenedImpactLibrary = $canonicalImpactLibrary.Replace($routingEntry, '')
        Assert-Contract (-not [string]::Equals($weakenedImpactLibrary, $canonicalImpactLibrary, [StringComparison]::Ordinal)) "Acceptance scenario matrix mutation must remove owning path '$owningPath'."
        $mutationPath = Join-Path $acceptanceScenarioMutationRoot "$([IO.Path]::GetFileName($owningPath)).CiImpactPlan.ps1"
        [IO.File]::WriteAllText($mutationPath, $weakenedImpactLibrary, [Text.UTF8Encoding]::new($false))
        . $mutationPath
        $mutationFailure = $null
        try { Assert-AcceptanceScenarioMatrixOwningPathsRoute } catch { $mutationFailure = $_ }
        Assert-Contract ($null -ne $mutationFailure) "Removing acceptance scenario matrix owning path '$owningPath' must fail the behavioral contract."
    }
}
finally {
    . $libraryPath
    if (Test-Path -LiteralPath $acceptanceScenarioMutationRoot) { Remove-Item -LiteralPath $acceptanceScenarioMutationRoot -Recurse -Force }
}

$acceptanceRuntimeMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-acceptance-runtime-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($acceptanceRuntimeMutationRoot) | Out-Null
    $runtimeMutationIndex = 0
    foreach ($owningPath in $acceptanceScenarioMatrixRuntimeOwningPaths) {
        $leafIndex = $owningPath.LastIndexOf('/', [StringComparison]::Ordinal) + 1
        $firstLeafCharacter = [string]$owningPath[$leafIndex]
        $wrongCaseCharacter = if ([char]::IsUpper($firstLeafCharacter[0])) { $firstLeafCharacter.ToLowerInvariant() } else { $firstLeafCharacter.ToUpperInvariant() }
        $wrongCasePath = $owningPath.Substring(0, $leafIndex) + $wrongCaseCharacter + $owningPath.Substring($leafIndex + 1)
        foreach ($mutation in @(
                @{ Name = 'deleted'; Replacement = '' },
                @{ Name = 'wrong-case'; Replacement = "            '$wrongCasePath'`n" },
                @{ Name = 'alias'; Replacement = "            '$($owningPath.Replace('scripts/', 'scripts/./'))'`n" },
                @{ Name = 'path-drift'; Replacement = "            '$($owningPath.Replace('.ps1', '.legacy.ps1'))'`n" }
            )) {
            $routingEntry = "            '$owningPath'`n"
            $weakenedImpactLibrary = $canonicalImpactLibrary.Replace($routingEntry, [string]$mutation.Replacement)
            Assert-Contract (-not [string]::Equals($weakenedImpactLibrary, $canonicalImpactLibrary, [StringComparison]::Ordinal)) "Acceptance runtime $($mutation.Name) mutation must change owning path '$owningPath'."
            $runtimeMutationIndex++
            $mutationPath = Join-Path $acceptanceRuntimeMutationRoot "$runtimeMutationIndex.CiImpactPlan.ps1"
            [IO.File]::WriteAllText($mutationPath, $weakenedImpactLibrary, [Text.UTF8Encoding]::new($false))
            . $mutationPath
            $mutationFailure = $null
            try { Assert-AcceptanceScenarioMatrixRuntimeOwningPathsRoute } catch { $mutationFailure = $_ }
            Assert-Contract ($null -ne $mutationFailure) "Acceptance runtime $($mutation.Name) mutation for owning path '$owningPath' must fail the behavioral contract."
        }
    }
}
finally {
    . $libraryPath
    if (Test-Path -LiteralPath $acceptanceRuntimeMutationRoot) { Remove-Item -LiteralPath $acceptanceRuntimeMutationRoot -Recurse -Force }
}

Assert-Contract (Test-Path -LiteralPath $entrypointPath -PathType Leaf) 'The governed CI impact-plan entrypoint is missing.'
$entrypointSource = [IO.File]::ReadAllText($entrypointPath)
Assert-Contract ($entrypointSource.Contains("@('diff', '--name-only', '--no-renames', '--diff-filter=ACMRD'", [StringComparison]::Ordinal)) 'Git diff must disable rename collapsing so both the deleted source path and added destination path are observed.'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-plan-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $fixtureJson = Join-Path $fixtureRoot 'impact-plan.json'
    $fixtureOutputs = Join-Path $fixtureRoot 'github-output.txt'
    $fixtureSummary = Join-Path $fixtureRoot 'step-summary.md'
    & $entrypointPath `
        -ChangedPaths @('backend/services/Business/Erp/src/Orders.cs') `
        -OutputPath $fixtureJson `
        -GitHubOutputPath $fixtureOutputs `
        -StepSummaryPath $fixtureSummary | Out-Null
    $writtenPlan = Get-Content -LiteralPath $fixtureJson -Raw | ConvertFrom-Json -Depth 20
    Assert-ImpactFlag -Plan $writtenPlan -Name 'backend' -Expected $true
    Assert-ImpactFlag -Plan $writtenPlan -Name 'erp_sales_order_demand' -Expected $true
    $writtenOutputs = @(Get-Content -LiteralPath $fixtureOutputs)
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'backend=true') 'GitHub output must serialize selected booleans as lowercase true.'
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'redis_cap=false') 'GitHub output must serialize unselected booleans as lowercase false.'
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'erp_sales_order_demand=true') 'GitHub output must serialize the selected ERP acceptance signal.'
    Assert-Contract (@($writtenOutputs | Where-Object { $_.StartsWith('business_services=[', [StringComparison]::Ordinal) }).Count -eq 1) 'GitHub output must expose the stable business-services JSON array.'
    $writtenSummary = Get-Content -LiteralPath $fixtureSummary -Raw
    Assert-Contract ($writtenSummary.Contains('NERV-668 routes Backend Tests, ERP Sales Order Demand Acceptance, Connector Host Tests, Script Governance, and OpenAPI/api-client Drift; NERV-688 routes PostgreSQL Provider Tests and Redis/CAP Transport Tests', [StringComparison]::Ordinal)) 'Actions Summary must identify every routed batch.'
    Assert-Contract ($writtenSummary.Contains('NERV-685 derives governed frontend workspace shards', [StringComparison]::Ordinal)) 'Actions Summary must identify the frontend workspace routing.'
    Assert-Contract ($writtenSummary.Contains('changed:backend/services/Business/Erp/src/Orders.cs', [StringComparison]::Ordinal)) 'Actions Summary must retain the selected signal reason.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

$workflow = [IO.File]::ReadAllText($workflowPath)
Assert-Contract ($workflow.Contains("  impact-plan:`n", [StringComparison]::Ordinal)) 'CI must define the impact-plan job.'
Assert-Contract ($workflow.Contains('run: ./scripts/tests/ci-impact-plan.Tests.ps1', [StringComparison]::Ordinal)) 'Script Governance must run the CI impact-plan contract tests.'
Assert-Contract ($workflow.Contains('uses: actions/upload-artifact@v4', [StringComparison]::Ordinal)) 'The impact-plan job must upload its audit artifact.'
Assert-ConditionalRoutingWorkflow -Path $workflowPath
Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowPath

$workflowMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($workflowMutationRoot) | Out-Null
    foreach ($planningMutation in @(
            @{
                Name = 'planning-job-missing'
                Original = "  acceptance-scenario-matrix-planning:`n"
                Replacement = "  acceptance-scenario-matrix-planning-missing:`n"
            },
            @{
                Name = 'planning-command-drift'
                Original = 'scripts/plan-acceptance-scenario-matrix.ps1'
                Replacement = 'scripts/plan-acceptance-scenario-matrix-drift.ps1'
            },
            @{
                Name = 'planning-policy-treats-missing-signal-as-unselected'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain == 'true') }}"
            }
        )) {
        $mutatedPlanningWorkflow = $workflow.Replace([string]$planningMutation.Original, [string]$planningMutation.Replacement)
        Assert-Contract (-not [string]::Equals($mutatedPlanningWorkflow, $workflow, [StringComparison]::Ordinal)) "Planning workflow mutation '$($planningMutation.Name)' must match the canonical workflow."
        $planningMutationPath = Join-Path $workflowMutationRoot "$($planningMutation.Name).yml"
        [IO.File]::WriteAllText($planningMutationPath, $mutatedPlanningWorkflow, [Text.UTF8Encoding]::new($false))
        $planningMutationFailure = $null
        try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $planningMutationPath } catch { $planningMutationFailure = $_ }
        Assert-Contract ($null -ne $planningMutationFailure) "Planning workflow mutation '$($planningMutation.Name)' must be rejected."
    }

    $workflowWithoutShadowImages = [regex]::Replace(
        $workflow,
        '(?ms)^      - name: Prepare shadow dependency images\n.*?(?=^      - name: Run acceptance scenario matrix\n)',
        '')
    Assert-Contract (-not [string]::Equals($workflowWithoutShadowImages, $workflow, [StringComparison]::Ordinal)) 'Shadow image preparation deletion mutation must remove the complete governed step.'
    $workflowWithoutShadowImagesPath = Join-Path $workflowMutationRoot 'shadow-runtime-drops-dependency-images.yml'
    [IO.File]::WriteAllText($workflowWithoutShadowImagesPath, $workflowWithoutShadowImages, [Text.UTF8Encoding]::new($false))
    $shadowImagesDeletionFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithoutShadowImagesPath } catch { $shadowImagesDeletionFailure = $_ }
    Assert-Contract ($null -ne $shadowImagesDeletionFailure) 'Deleting the hosted shadow dependency image step must fail the workflow contract.'

    $workflowWithRelativeRuntimeArtifact = $workflow.Replace(
        "`$artifactPath = [IO.Path]::GetFullPath('artifacts/acceptance-scenario-matrix/planning.json')",
        "`$artifactPath = 'artifacts/acceptance-scenario-matrix/planning.json'",
        [StringComparison]::Ordinal)
    Assert-Contract (-not [string]::Equals($workflowWithRelativeRuntimeArtifact, $workflow, [StringComparison]::Ordinal)) 'Shadow runtime relative artifact mutation must alter the canonical workflow adapter.'
    $workflowWithRelativeRuntimeArtifactPath = Join-Path $workflowMutationRoot 'shadow-runtime-relative-planning-artifact.yml'
    [IO.File]::WriteAllText($workflowWithRelativeRuntimeArtifactPath, $workflowWithRelativeRuntimeArtifact, [Text.UTF8Encoding]::new($false))
    $relativeRuntimeArtifactFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithRelativeRuntimeArtifactPath } catch { $relativeRuntimeArtifactFailure = $_ }
    Assert-Contract ($null -ne $relativeRuntimeArtifactFailure) 'Passing a relative planning artifact path from the hosted shadow adapter must fail the workflow contract.'

    foreach ($imageMutation in @(
            @{ Name = 'wrong-postgres-image'; Original = 'postgres:18'; Replacement = 'postgres:17' },
            @{ Name = 'wrong-redis-image'; Original = 'redis:8'; Replacement = 'redis:7' }
        )) {
        $workflowWithWrongImage = $workflow.Replace([string]$imageMutation.Original, [string]$imageMutation.Replacement)
        Assert-Contract (-not [string]::Equals($workflowWithWrongImage, $workflow, [StringComparison]::Ordinal)) "Shadow image mutation '$($imageMutation.Name)' must alter the workflow."
        $workflowWithWrongImagePath = Join-Path $workflowMutationRoot "$($imageMutation.Name).yml"
        [IO.File]::WriteAllText($workflowWithWrongImagePath, $workflowWithWrongImage, [Text.UTF8Encoding]::new($false))
        $wrongImageFailure = $null
        try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithWrongImagePath } catch { $wrongImageFailure = $_ }
        Assert-Contract ($null -ne $wrongImageFailure) "Shadow image mutation '$($imageMutation.Name)' must fail the workflow contract."
    }

    $acceptanceScenarioContractStep = @'
      - name: Test acceptance scenario matrix contract
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/acceptance-scenario-matrix.Tests.ps1

'@
    $workflowWithoutAcceptanceScenarioContract = $workflow.Replace($acceptanceScenarioContractStep, '')
    Assert-Contract (-not [string]::Equals($workflowWithoutAcceptanceScenarioContract, $workflow, [StringComparison]::Ordinal)) 'Acceptance scenario matrix workflow mutation must remove the canonical contract step.'
    $workflowWithoutAcceptanceScenarioContractPath = Join-Path $workflowMutationRoot 'script-governance-drops-acceptance-scenario-contract.yml'
    [IO.File]::WriteAllText($workflowWithoutAcceptanceScenarioContractPath, $workflowWithoutAcceptanceScenarioContract, [Text.UTF8Encoding]::new($false))
    $workflowContractFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithoutAcceptanceScenarioContractPath } catch { $workflowContractFailure = $_ }
    Assert-Contract ($null -ne $workflowContractFailure) 'Removing the acceptance scenario matrix Script Governance step must fail the workflow contract.'

    $acceptanceRuntimeContractStep = @'
      - name: Test acceptance scenario matrix runtime contract
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/acceptance-scenario-matrix-runtime.Tests.ps1

'@
    $workflowWithoutAcceptanceRuntimeContract = $workflow.Replace($acceptanceRuntimeContractStep, '').Replace(
        'step 预算合计 143m（29 个 step：3m checkout',
        'step 预算合计 138m（28 个 step：3m checkout').Replace(
        '+ 28 × 5m；',
        '+ 27 × 5m；')
    Assert-Contract (-not [string]::Equals($workflowWithoutAcceptanceRuntimeContract, $workflow, [StringComparison]::Ordinal)) 'Acceptance runtime workflow mutation must remove the canonical pure fixture contract step.'
    Assert-Contract ($workflowWithoutAcceptanceRuntimeContract.Contains('step 预算合计 138m（28 个 step：3m checkout', [StringComparison]::Ordinal) -and $workflowWithoutAcceptanceRuntimeContract.Contains('+ 27 × 5m；', [StringComparison]::Ordinal)) 'Acceptance runtime workflow mutation must keep its budget comment truthful at 28 steps and 138m.'
    $workflowWithoutAcceptanceRuntimeContractPath = Join-Path $workflowMutationRoot 'script-governance-drops-acceptance-runtime-contract.yml'
    [IO.File]::WriteAllText($workflowWithoutAcceptanceRuntimeContractPath, $workflowWithoutAcceptanceRuntimeContract, [Text.UTF8Encoding]::new($false))
    $runtimeWorkflowContractFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithoutAcceptanceRuntimeContractPath } catch { $runtimeWorkflowContractFailure = $_ }
    $expectedRuntimeStepDiagnostic = 'Script Governance must contain exactly one independent acceptance scenario matrix runtime contract step.'
    $observedRuntimeStepDiagnostic = if ($null -eq $runtimeWorkflowContractFailure) { '<none>' } else { [string]$runtimeWorkflowContractFailure.Exception.Message }
    Assert-Contract ([string]::Equals($observedRuntimeStepDiagnostic, $expectedRuntimeStepDiagnostic, [StringComparison]::Ordinal)) "Removing the acceptance runtime Script Governance fixture step must fail with the exact runtime-step uniqueness diagnostic. Observed: $observedRuntimeStepDiagnostic"

    $acceptanceEquivalenceContractStep = @'
      - name: Test acceptance scenario matrix equivalence contract
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/acceptance-scenario-matrix-equivalence.Tests.ps1

'@
    $workflowWithoutAcceptanceEquivalenceContract = $workflow.Replace($acceptanceEquivalenceContractStep, '').Replace(
        'step 预算合计 143m（29 个 step：3m checkout',
        'step 预算合计 138m（28 个 step：3m checkout').Replace(
        '+ 28 × 5m；',
        '+ 27 × 5m；')
    $workflowWithoutAcceptanceEquivalenceContractPath = Join-Path $workflowMutationRoot 'script-governance-drops-acceptance-equivalence-contract.yml'
    [IO.File]::WriteAllText($workflowWithoutAcceptanceEquivalenceContractPath, $workflowWithoutAcceptanceEquivalenceContract, [Text.UTF8Encoding]::new($false))
    $equivalenceWorkflowContractFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithoutAcceptanceEquivalenceContractPath } catch { $equivalenceWorkflowContractFailure = $_ }
    Assert-Contract ($null -ne $equivalenceWorkflowContractFailure) 'Removing the equivalence Script Governance fixture step must fail the workflow contract.'

    $workflowWithIncorrectBudgetComment = $workflow.Replace(
        'step 预算合计 143m（29 个 step：3m checkout',
        'step 预算合计 138m（28 个 step：3m checkout').Replace(
        '+ 28 × 5m；',
        '+ 27 × 5m；')
    Assert-Contract (-not [string]::Equals($workflowWithIncorrectBudgetComment, $workflow, [StringComparison]::Ordinal)) 'Script Governance budget-comment mutation must alter the canonical 29-step/143m comment.'
    $workflowWithIncorrectBudgetCommentPath = Join-Path $workflowMutationRoot 'script-governance-uses-incorrect-budget-comment.yml'
    [IO.File]::WriteAllText($workflowWithIncorrectBudgetCommentPath, $workflowWithIncorrectBudgetComment, [Text.UTF8Encoding]::new($false))
    $budgetCommentContractFailure = $null
    try { Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowWithIncorrectBudgetCommentPath } catch { $budgetCommentContractFailure = $_ }
    $expectedBudgetCommentDiagnostic = 'Script Governance budget comment must match its actual 29-step/143m structure.'
    $observedBudgetCommentDiagnostic = if ($null -eq $budgetCommentContractFailure) { '<none>' } else { [string]$budgetCommentContractFailure.Exception.Message }
    Assert-Contract ([string]::Equals($observedBudgetCommentDiagnostic, $expectedBudgetCommentDiagnostic, [StringComparison]::Ordinal)) "An incorrect Script Governance budget comment must fail with the exact budget diagnostic. Observed: $observedBudgetCommentDiagnostic"

    foreach ($mutation in @(
            @{
                Name = 'backend-execution-jobs-drop-impact-dependency'
                Original = "    needs: impact-plan`n    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}`n"
                Replacement = "    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}`n"
            },
            @{
                Name = 'backend-shard-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.backend != 'false') }}"
            },
            @{
                Name = 'backend-shard-drops-cancellation-guard'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
                Replacement = "`${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false' }}"
            },
            @{
                Name = 'backend-shard-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.backend != 'false'"
                Replacement = "needs.impact-plan.outputs.postgresql != 'false'"
            },
            @{
                Name = 'backend-shard-treats-missing-output-as-unselected'
                Original = "needs.impact-plan.outputs.backend != 'false'"
                Replacement = "needs.impact-plan.outputs.backend == 'true'"
            },
            @{
                Name = 'erp-drops-impact-dependency'
                Original = "    needs: impact-plan`n    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}`n"
                Replacement = "    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}`n"
            },
            @{
                Name = 'erp-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}"
            },
            @{
                Name = 'erp-drops-cancellation-guard'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false') }}"
                Replacement = "`${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' || needs.impact-plan.outputs.full_chain != 'false' }}"
            },
            @{
                Name = 'erp-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.erp_sales_order_demand != 'false'"
                Replacement = "needs.impact-plan.outputs.full_chain != 'false'"
            },
            @{
                Name = 'erp-treats-missing-output-as-unselected'
                Original = "needs.impact-plan.outputs.erp_sales_order_demand != 'false'"
                Replacement = "needs.impact-plan.outputs.erp_sales_order_demand == 'true'"
            },
            @{
                Name = 'connector-drops-impact-dependency'
                Original = "    needs: impact-plan`n    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}`n"
                Replacement = "    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}`n"
            },
            @{
                Name = 'connector-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
            },
            @{
                Name = 'connector-drops-cancellation-guard'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
                Replacement = "`${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false' }}"
            },
            @{
                Name = 'connector-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.connector_hosts != 'false'"
                Replacement = "needs.impact-plan.outputs.backend != 'false'"
            },
            @{
                Name = 'connector-treats-missing-output-as-unselected'
                Original = "needs.impact-plan.outputs.connector_hosts != 'false'"
                Replacement = "needs.impact-plan.outputs.connector_hosts == 'true'"
            },
            @{
                Name = 'openapi-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
            },
            @{
                Name = 'openapi-drops-cancellation-guard'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
                Replacement = "`${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false' }}"
            },
            @{
                Name = 'openapi-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.openapi_codegen != 'false'"
                Replacement = "needs.impact-plan.outputs.frontend != 'false'"
            },
            @{
                Name = 'script-governance-drops-backend-coverage'
                Original = " || needs.impact-plan.outputs.backend != 'false'"
                Replacement = ''
            },
            @{
                Name = 'postgres-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.postgresql != 'false') }}"
            },
            @{
                Name = 'postgres-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.postgresql != 'false'"
                Replacement = "needs.impact-plan.outputs.redis_cap != 'false'"
            },
            @{
                Name = 'redis-cap-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.redis_cap != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.redis_cap != 'false') }}"
            },
            @{
                Name = 'redis-cap-uses-wrong-signal'
                Original = "needs.impact-plan.outputs.redis_cap != 'false'"
                Replacement = "needs.impact-plan.outputs.full_chain != 'false'"
            },
            @{
                Name = 'openapi-treats-missing-output-as-unselected'
                Original = "needs.impact-plan.outputs.openapi_codegen != 'false'"
                Replacement = "needs.impact-plan.outputs.openapi_codegen == 'true'"
            },
            @{
                Name = 'impact-plan-drops-routed-output'
                Original = "      openapi_codegen: `${{ steps.plan.outputs.openapi_codegen }}`n"
                Replacement = ''
            },
            @{
                Name = 'openapi-drops-impact-dependency'
                Original = "    needs: impact-plan`n    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}`n"
                Replacement = "    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}`n"
            }
        )) {
        $mutated = $workflow.Replace($mutation.Original, $mutation.Replacement)
        Assert-Contract (-not [string]::Equals($mutated, $workflow, [StringComparison]::Ordinal)) "Conditional-routing mutation '$($mutation.Name)' must match the canonical workflow."
        $mutationPath = Join-Path $workflowMutationRoot "$($mutation.Name).yml"
        [IO.File]::WriteAllText($mutationPath, $mutated, [Text.UTF8Encoding]::new($false))
        $failure = $null
        try { Assert-ConditionalRoutingWorkflow -Path $mutationPath } catch { $failure = $_ }
        Assert-Contract ($null -ne $failure) "Conditional-routing mutation '$($mutation.Name)' must be rejected."
    }
}
finally {
    if (Test-Path -LiteralPath $workflowMutationRoot) { Remove-Item -LiteralPath $workflowMutationRoot -Recurse -Force }
}

Write-Output 'CI impact-plan contract tests passed.'
