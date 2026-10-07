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
    'scripts/tests/acceptance-scenario-matrix.Tests.ps1'
)
$acceptanceCanonicalOwningPaths = @(
    'scripts/lib/AcceptanceCanonicalResult.ps1'
    'scripts/tests/acceptance-canonical-result.Tests.ps1'
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

function Get-NervCiDotNetSdkContractFindings {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $ExpectedJobNames,
        [Parameter(Mandatory)] [string] $ExpectedSdkVersion,
        [Parameter(Mandatory)] [string] $ExpectedInstallDirectory
    )

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $expectedJobs = [Collections.Generic.HashSet[string]]::new($ExpectedJobNames, [StringComparer]::Ordinal)
    $findings = [Collections.Generic.List[string]]::new()

    foreach ($jobName in $ExpectedJobNames) {
        $jobProperty = $parsedWorkflow.jobs.PSObject.Properties[$jobName]
        if ($null -eq $jobProperty) {
            $findings.Add("ci-dotnet-job-missing:$jobName")
            continue
        }

        $job = $jobProperty.Value
        $setupSteps = @($job.steps | Where-Object {
                $usesProperty = $_.PSObject.Properties['uses']
                $null -ne $usesProperty -and ([string]$usesProperty.Value).StartsWith('actions/setup-dotnet@', [StringComparison]::Ordinal)
            })
        if ($setupSteps.Count -ne 1) {
            $findings.Add("ci-setup-dotnet-count:${jobName}:$($setupSteps.Count)")
        }
        foreach ($setupStep in $setupSteps) {
            $actualVersion = [string]$setupStep.with.'dotnet-version'
            if (-not [string]::Equals($actualVersion, $ExpectedSdkVersion, [StringComparison]::Ordinal)) {
                $findings.Add("ci-dotnet-sdk-version:${jobName}:$actualVersion")
            }
            $actualInstallDirectory = ''
            $stepEnvProperty = $setupStep.PSObject.Properties['env']
            if ($null -ne $stepEnvProperty) {
                $installDirectoryProperty = $stepEnvProperty.Value.PSObject.Properties['DOTNET_INSTALL_DIR']
                if ($null -ne $installDirectoryProperty) { $actualInstallDirectory = [string]$installDirectoryProperty.Value }
            }
            if (-not [string]::Equals($actualInstallDirectory, $ExpectedInstallDirectory, [StringComparison]::Ordinal)) {
                $findings.Add("ci-dotnet-install-directory:${jobName}:$actualInstallDirectory")
            }
        }

        $jobEnvProperty = $job.PSObject.Properties['env']
        if ($null -ne $jobEnvProperty) {
            $installDirectoryProperty = $jobEnvProperty.Value.PSObject.Properties['DOTNET_INSTALL_DIR']
            if ($null -ne $installDirectoryProperty) { $findings.Add("ci-dotnet-install-directory-job-scope:$jobName") }
        }
    }

    foreach ($jobProperty in $parsedWorkflow.jobs.PSObject.Properties) {
        if ($expectedJobs.Contains([string]$jobProperty.Name)) { continue }
        $unexpectedJobEnvProperty = $jobProperty.Value.PSObject.Properties['env']
        if ($null -ne $unexpectedJobEnvProperty -and $null -ne $unexpectedJobEnvProperty.Value.PSObject.Properties['DOTNET_INSTALL_DIR']) {
            $findings.Add("ci-dotnet-install-directory-unexpected-job:$($jobProperty.Name)")
        }
        $unexpectedSetupSteps = @($jobProperty.Value.steps | Where-Object {
                $usesProperty = $_.PSObject.Properties['uses']
                $null -ne $usesProperty -and ([string]$usesProperty.Value).StartsWith('actions/setup-dotnet@', [StringComparison]::Ordinal)
            })
        if ($unexpectedSetupSteps.Count -gt 0) {
            $findings.Add("ci-setup-dotnet-unexpected-job:$($jobProperty.Name):$($unexpectedSetupSteps.Count)")
        }
    }

    return @($findings)
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
        'connector-host-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
        'openapi-client-drift' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
        'postgres-provider-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false') }}"
        'redis-cap-transport-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.redis_cap != 'false') }}"
        'script-governance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.scripts != 'false' || needs.impact-plan.outputs.backend != 'false' || needs.impact-plan.outputs.infra != 'false' || needs.impact-plan.outputs.docs != 'false') }}"
    }

    $impactPlan = $parsedWorkflow.jobs.PSObject.Properties['impact-plan'].Value
    foreach ($outputName in @('scripts', 'backend', 'connector_hosts', 'openapi_codegen', 'postgresql', 'redis_cap', 'full_chain', 'infra', 'docs')) {
        $outputProperty = $impactPlan.outputs.PSObject.Properties[$outputName]
        Assert-Contract ($null -ne $outputProperty) "Impact plan must declare routed output '$outputName'."
        $expectedOutput = '${{ steps.plan.outputs.' + $outputName + ' }}'
        Assert-Contract ([string]::Equals([string]$outputProperty.Value, $expectedOutput, [StringComparison]::Ordinal)) "Impact plan output '$outputName' must map directly to the plan step."
    }
    Assert-Contract ($null -eq $impactPlan.outputs.PSObject.Properties['erp_sales_order_demand']) 'Impact plan must not expose the retired erp_sales_order_demand output.'
    Assert-Contract ($null -eq $parsedWorkflow.jobs.PSObject.Properties['erp-sales-order-demand-acceptance']) 'CI must not define the retired ERP Sales Order Demand Acceptance job.'

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
            'connector-host-tests', 'openapi-client-drift',
            'postgres-provider-tests', 'redis-cap-transport-tests',
            'business-full-chain-acceptance', 'business-full-chain-acceptance-v1', 'script-governance', 'ci-summary'
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

function Assert-RedisCapActiveSelectionWorkflowContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $redisCapJobProperty = $parsedWorkflow.jobs.PSObject.Properties['redis-cap-transport-tests']
    Assert-Contract ($null -ne $redisCapJobProperty) 'CI must define the Redis/CAP transport job.'
    $runnerSteps = @($redisCapJobProperty.Value.steps | Where-Object {
            $runProperty = $_.PSObject.Properties['run']
            $null -ne $runProperty -and ([string]$runProperty.Value).Contains('./scripts/run-redis-cap-test-lane.ps1', [StringComparison]::Ordinal)
        })
    Assert-Contract ($runnerSteps.Count -eq 1) 'The Redis/CAP job must invoke its governed runner exactly once.'
    $runnerInvocation = [string]$runnerSteps[0].run
    Assert-Contract ($runnerInvocation.Contains('-AllActiveMembers', [StringComparison]::Ordinal)) 'Hosted Redis/CAP execution must select the manifest active set.'
    Assert-Contract (-not $runnerInvocation.Contains('-MemberId', [StringComparison]::Ordinal)) 'Hosted Redis/CAP execution must not maintain a member-id list.'
}

function Assert-BusinessConsoleBrowserValidationWorkflowContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $frontendValidation = $parsedWorkflow.jobs.'frontend-validation-shards'
    Assert-Contract ([int]$frontendValidation.'timeout-minutes' -eq 80) 'Frontend Validation must leave a 9-minute job margin above the 71-minute evidence-publishing step sum for implicit post steps.'
    $businessConsoleBrowserCondition = "matrix.name == '@nerv-iip/business-console'"
    $resolveBrowserSteps = @($frontendValidation.steps | Where-Object {
            [string]::Equals([string]$_.name, 'Resolve Business Console browser', [StringComparison]::Ordinal)
        })
    Assert-Contract ($resolveBrowserSteps.Count -eq 1) 'Frontend Validation must resolve the Business Console browser exactly once.'
    $resolveBrowserStep = $resolveBrowserSteps[0]
    Assert-Contract ([string]::Equals([string]$resolveBrowserStep.if, $businessConsoleBrowserCondition, [StringComparison]::Ordinal)) 'Business Console browser resolution must run only for its validation matrix item.'
    Assert-Contract ([int]$resolveBrowserStep.'timeout-minutes' -eq 3) 'Business Console browser resolution must keep a three-minute budget.'
    Assert-Contract ([string]::Equals([string]$resolveBrowserStep.shell, 'bash --noprofile --norc -euo pipefail {0}', [StringComparison]::Ordinal)) 'Business Console browser resolution must use the governed fail-fast Bash shell.'
    Assert-Contract (([string]$resolveBrowserStep.run).Contains('command -v google-chrome', [StringComparison]::Ordinal) -and
        ([string]$resolveBrowserStep.run).Contains('if [ -z "$browser_path" ]; then', [StringComparison]::Ordinal) -and
        ([string]$resolveBrowserStep.run).Contains('exit 1', [StringComparison]::Ordinal) -and
        ([string]$resolveBrowserStep.run).Contains('PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=', [StringComparison]::Ordinal) -and
        ([string]$resolveBrowserStep.run).Contains('$GITHUB_ENV', [StringComparison]::Ordinal)) 'Business Console browser resolution must fail closed and export the runner Chrome path.'

    $browserTestSteps = @($frontendValidation.steps | Where-Object {
            [string]::Equals([string]$_.name, 'Test Business Console browser invariants', [StringComparison]::Ordinal)
        })
    Assert-Contract ($browserTestSteps.Count -eq 1) 'Frontend Validation must execute the Business Console browser invariants exactly once.'
    $browserTestStep = $browserTestSteps[0]
    $browserTestIdProperty = $browserTestStep.PSObject.Properties['id']
    Assert-Contract ($null -ne $browserTestIdProperty -and [string]::Equals([string]$browserTestIdProperty.Value, 'business-console-browser-tests', [StringComparison]::Ordinal)) 'Business Console browser invariants must expose a stable outcome for diagnostic upload routing.'
    Assert-Contract ([string]::Equals([string]$browserTestStep.if, $businessConsoleBrowserCondition, [StringComparison]::Ordinal)) 'Business Console browser invariants must run only for their validation matrix item.'
    Assert-Contract ([int]$browserTestStep.'timeout-minutes' -eq 10) 'Business Console browser invariants must keep a ten-minute budget.'
    Assert-Contract ([string]::Equals([string]$browserTestStep.env.NERV_IIP_OUT_DIR, '${{ runner.temp }}/issue-2098-tooling-browser', [StringComparison]::Ordinal)) 'Business Console browser artifacts must use the runner temporary directory.'
    Assert-Contract (([string]$browserTestStep.run).Contains('pnpm -C frontend --filter @nerv-iip/business-console exec playwright test', [StringComparison]::Ordinal) -and
        ([string]$browserTestStep.run).Contains('e2e/issue1974-tooling-visual.spec.ts', [StringComparison]::Ordinal) -and
        ([string]$browserTestStep.run).Contains('e2e/issue3734-column-width.spec.ts', [StringComparison]::Ordinal) -and
        ([string]$browserTestStep.run).Contains('e2e/issue3735-layout.spec.ts', [StringComparison]::Ordinal) -and
        ([string]$browserTestStep.run).Contains('--project=desktop', [StringComparison]::Ordinal)) 'Business Console browser invariants must run the governed desktop tooling specifications.'

    $browserUploadSteps = @($frontendValidation.steps | Where-Object {
            [string]::Equals([string]$_.name, 'Upload Business Console browser diagnostics', [StringComparison]::Ordinal)
        })
    Assert-Contract ($browserUploadSteps.Count -eq 1) 'Frontend Validation must upload Business Console browser diagnostics exactly once.'
    $browserUploadStep = $browserUploadSteps[0]
    $browserUploadTimeoutProperty = $browserUploadStep.PSObject.Properties['timeout-minutes']
    Assert-Contract ($null -ne $browserUploadTimeoutProperty -and [int]$browserUploadTimeoutProperty.Value -eq 5) 'Business Console browser diagnostic upload must keep a five-minute budget.'
    Assert-Contract ([string]::Equals([string]$browserUploadStep.if, "failure() && steps.business-console-browser-tests.outcome == 'failure'", [StringComparison]::Ordinal)) 'Business Console browser diagnostics must upload only after its browser test fails.'
    Assert-Contract ([string]::Equals([string]$browserUploadStep.uses, 'actions/upload-artifact@v4', [StringComparison]::Ordinal)) 'Business Console browser diagnostics must use the governed artifact uploader.'
    Assert-Contract ([string]::Equals([string]$browserUploadStep.with.name, 'business-console-browser-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'Business Console browser diagnostics must have a run-attempt-specific artifact identity.'
    Assert-Contract ([string]::Equals([string]$browserUploadStep.with.path, '${{ runner.temp }}/issue-2098-tooling-browser', [StringComparison]::Ordinal)) 'Business Console browser diagnostics must upload the governed temporary directory.'
    Assert-Contract ([string]::Equals([string]$browserUploadStep.with.'if-no-files-found', 'error', [StringComparison]::Ordinal)) 'Business Console browser diagnostics must fail closed when no diagnostic files exist.'
    Assert-Contract ([int]$browserUploadStep.with.'retention-days' -eq 7) 'Business Console browser diagnostics must retain artifacts for seven days.'

    $resolveBrowserIndex = [Array]::IndexOf([object[]]$frontendValidation.steps, $resolveBrowserStep)
    $browserTestIndex = [Array]::IndexOf([object[]]$frontendValidation.steps, $browserTestStep)
    $browserUploadIndex = [Array]::IndexOf([object[]]$frontendValidation.steps, $browserUploadStep)
    Assert-Contract ($resolveBrowserIndex -gt 0 -and
        [string]::Equals([string]$frontendValidation.steps[$resolveBrowserIndex - 1].name, 'Build affected frontend app', [StringComparison]::Ordinal) -and
        $browserTestIndex -eq ($resolveBrowserIndex + 1) -and
        $browserUploadIndex -eq ($browserTestIndex + 1)) 'Business Console browser resolution, test, and diagnostic upload must follow the validated production build in order.'
}

function Assert-AcceptanceScenarioMatrixWorkflowContract {
    param([Parameter(Mandatory)] [string] $Path)

    $parsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $Path -WorkingDirectory $repoRoot
    $impactJob = $parsedWorkflow.jobs.'impact-plan'
    foreach ($outputContract in @(
            @{ Name = 'artifact-name'; Value = '${{ steps.impact-artifact-identity.outputs.artifact-name }}' },
            @{ Name = 'producer-run-attempt'; Value = '${{ steps.impact-artifact-identity.outputs.producer-run-attempt }}' }
        )) {
        $outputProperty = $impactJob.outputs.PSObject.Properties[$outputContract.Name]
        Assert-Contract ($null -ne $outputProperty -and [string]::Equals([string]$outputProperty.Value, [string]$outputContract.Value, [StringComparison]::Ordinal)) "Impact-plan producer output '$($outputContract.Name)' must come from its artifact identity step."
    }
    $impactIdentitySteps = @($impactJob.steps | Where-Object {
            $idProperty = $_.PSObject.Properties['id']
            $null -ne $idProperty -and [string]::Equals([string]$idProperty.Value, 'impact-artifact-identity', [StringComparison]::Ordinal)
        })
    Assert-Contract ($impactIdentitySteps.Count -eq 1 -and
        ([string]$impactIdentitySteps[0].run).Contains('artifact-name=ci-impact-plan-${{ github.run_id }}-${{ github.run_attempt }}', [StringComparison]::Ordinal) -and
        ([string]$impactIdentitySteps[0].run).Contains('producer-run-attempt=${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'Impact-plan must single-source its physical artifact name and producer attempt.'
    $impactUploads = @($impactJob.steps | Where-Object {
            $usesProperty = $_.PSObject.Properties['uses']
            $null -ne $usesProperty -and [string]::Equals([string]$usesProperty.Value, 'actions/upload-artifact@v4', [StringComparison]::Ordinal)
        })
    Assert-Contract ($impactUploads.Count -eq 1 -and [string]::Equals([string]$impactUploads[0].with.name, '${{ steps.impact-artifact-identity.outputs.artifact-name }}', [StringComparison]::Ordinal) -and $null -eq $impactUploads[0].with.PSObject.Properties['overwrite']) 'Impact-plan upload must use its immutable producer identity without overwrite.'

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

function Assert-FullChainProjectReferenceCoverage {
    # #3338：把「FullChain lane 的依赖边」从**手抄**改成**从 .csproj 派生**看守。
    #
    # 背景：CiImpactPlan.ps1 的路径分发是一串手写 if 分支，每条各自硬编码一组 flag，
    # 没有任何从项目引用关系派生的机制。于是 `backend/gateway/BusinessGateway/` 那条漏了
    # full_chain（#3330 / PR #3337 实例），而 Wms / Mes / Maintenance 三个被 FullChain 直接
    # 引用的业务服务同样没被 salesOrderDemand 那个集合覆盖。逐条补名单每轮必复发
    # （本仓同形状已栽三次：#3003 / #3135 / #3300）。
    #
    # 本契约不消灭名单，而是**让名单的完备性由一条不会过期的派生断言看守**：
    # 引用关系的唯一权威是 Nerv.IIP.Business.FullChain.Tests.csproj 的 ProjectReference，
    # 新增一条引用而忘了更新 CiImpactPlan 的集合/分支，这里立刻红。
    #
    # **本契约不保证什么（别读成完备）**：
    #
    # (a) 它只覆盖 .csproj 里的**编译期** ProjectReference。FullChain 的运行时依赖面比这更大
    #     （seed 路径、跨服务事件转换器/处理器等由 Test-FullChainSeedPath /
    #     Test-CrossServiceIntegrationEventPath 另行覆盖），那些不在本契约射程内。
    #
    # (b) **它只看守一个方向**：「csproj 里有这条引用 ⇒ CiImpactPlan 必须选中 full_chain」。
    #     **反向不看守** —— csproj 里删掉一条引用、而上面那个名单里还留着该服务，本契约**不会红**。
    #     这个方向是**刻意选的、也是安全的**：残留名单只会让 full_chain lane **过度选中**
    #     （多跑一次重 lane），不会让它**漏选**；而漏选才是 #3338 要修的那类缺陷
    #     （改了网关却不跑 FullChain，缺陷带着绿灯进 main）。⛔ 别把本契约读成双向完备。
    #
    # (c) ⚠️ **一个「绿得理由不对」的已知边界（登记，未在 #3338 修）**：下面业务服务那一面用的是
    #     **合成探针路径**。今天 5 个被引用服务全都在 CiImpactPlan 的 $knownBusinessServiceNames 里，
    #     所以没有假过。但**将来若 FullChain 引用了一个尚未登记进那份名单的业务服务**，探针路径会命中
    #     CiImpactPlan 的 `-not $knownBusinessServiceNameSet.Contains(...)` 分支走 Select-AllImpacts
    #     **全量点亮**，于是本契约照样通过 —— **CI 行为仍然正确**（全选是保守的），
    #     **但本契约那一刻是因为错误的理由变绿的**，它并没有证明「名单覆盖了该服务」。
    #     **可辨识特征（实测读数，#3338）**：已登记服务的探针点亮 7 个 flag、full_chain 的 reason 前缀是
    #     `changed:`；未登记服务点亮 20 个 flag、reason 前缀是 `unclassified-business-service:`。
    #     后来人若要收掉这一格，就从这个前缀入手。
    $projectPath = Join-Path $repoRoot 'backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj'
    Assert-Contract (Test-Path -LiteralPath $projectPath) 'FullChain test project must exist for the dependency-edge contract.'

    [xml] $projectXml = Get-Content -LiteralPath $projectPath -Raw
    $referenceRoot = Split-Path -Parent $projectPath
    $referencedPaths = [Collections.Generic.List[string]]::new()
    foreach ($node in $projectXml.SelectNodes('//ProjectReference')) {
        $include = [string]$node.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $referenceRoot ($include -replace '\\', [IO.Path]::DirectorySeparatorChar)))
        $relative = $resolved.Substring($repoRoot.Length).TrimStart([char]'/', [char]'\') -replace '\\', '/'
        [void]$referencedPaths.Add($relative)
    }

    # 正向判据：解析必须真的产出东西。没有这一条，解析一旦失败（改名/改结构）会让下面
    # 每一条 foreach 断言退化成「空集即真」而全绿——那是本仓成文教训里最典型的假绿形态。
    Assert-Contract ($referencedPaths.Count -gt 0) 'FullChain dependency-edge contract parsed zero ProjectReference entries; the contract would be vacuously true.'

    $businessServiceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $nonBusinessReferences = [Collections.Generic.List[string]]::new()
    foreach ($relative in $referencedPaths) {
        $match = [regex]::Match($relative, '^backend/services/Business/([^/]+)/')
        if ($match.Success) { [void]$businessServiceNames.Add($match.Groups[1].Value) }
        else { [void]$nonBusinessReferences.Add($relative) }
    }

    # 同上：两个分支各自也不许是空集。
    Assert-Contract ($businessServiceNames.Count -gt 0) 'FullChain dependency-edge contract resolved zero referenced business services.'
    Assert-Contract ($nonBusinessReferences.Count -gt 0) 'FullChain dependency-edge contract resolved zero non-business references.'

    # ① 业务服务这一面：被 FullChain 引用的每个服务，改它必须选中 full_chain。
    #    用该服务 .csproj 之外的真实路径不可得时，这里直接对服务名断言覆盖关系——
    #    判定发生在 CiImpactPlan 的集合里，因此断言集合包含关系比造夹具更直接、也无夹具选取偏差。
    foreach ($serviceName in $businessServiceNames) {
        $servicePath = "backend/services/Business/$serviceName/src/probe/FullChainDependencyEdgeProbe.cs"
        $plan = Get-NervCiImpactPlan -ChangedPaths @($servicePath)
        Assert-Contract ([bool]$plan.full_chain) "FullChain references business service '$serviceName', so changing it must select the full_chain lane (path: $servicePath)."
    }

    # ② 非业务服务这一面（网关 / common/*）：直接用被引用项目的 .csproj 路径作夹具。
    foreach ($relative in $nonBusinessReferences) {
        $plan = Get-NervCiImpactPlan -ChangedPaths @($relative)
        Assert-Contract ([bool]$plan.full_chain) "FullChain references '$relative', so changing it must select the full_chain lane."
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

function Assert-AcceptanceCanonicalOwningPathsRoute {
    $expectedSelectedFlags = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('scripts', 'backend', 'full_chain'),
        [StringComparer]::Ordinal)

    foreach ($owningPath in $acceptanceCanonicalOwningPaths) {
        $plan = Get-NervCiImpactPlan -ChangedPaths @($owningPath)
        foreach ($flag in @($plan.PSObject.Properties | Where-Object { $_.Value -is [bool] })) {
            Assert-ImpactFlag -Plan $plan -Name ([string]$flag.Name) -Expected $expectedSelectedFlags.Contains([string]$flag.Name)
        }
        Assert-Contract (@($plan.business_services).Count -eq 0) "Acceptance scenario matrix runtime owner '$owningPath' must not select business services."
    }
}

function Assert-AcceptanceCanonicalPathMutationsDoNotAliasOwners {
    foreach ($owningPath in $acceptanceCanonicalOwningPaths) {
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

Assert-ImpactCase -Name 'pure-docs' -Paths @('README.md', 'docs/architecture/overview/context-map.md') -Flags @{
    docs = $true; backend = $false; frontend = $false; scripts = $false; connector_hosts = $false; postgresql = $false; full_chain = $false
}

Assert-ImpactCase -Name 'script-governance-registry' -Paths @('docs/governance/script-automation.md') -Flags @{
    docs = $true; scripts = $true; backend = $false; frontend = $false
}

# #3145: the restore manifest lives under 'docs/', and the generic 'docs/' rule would route it to
# 'docs' alone. The only gate that reads it, scripts/verify-restore-lock-contract.ps1, runs in the
# 'Script Governance' job, whose `if` is `scripts != false || backend != false` — so without
# 'scripts' here, a PR that edits only the manifest skips the job entirely and the check runs on
# zero jobs for exactly the change it exists to catch. Measured before the routing rule was added:
# the plan for this path came back 'docs' only.
Assert-ImpactCase -Name 'restore-lock-manifest' -Paths @('docs/reference/api/business-gateway-surface-restore.manifest.json') -Flags @{
    docs = $true; scripts = $true; backend = $false; frontend = $false
}

# #3157: the same routing for the second manifest. Asserted separately from the case above because
# the rule used to be an exact string match on the BusinessGateway path — one case passing proves
# only that that one path is routed, which is precisely how a whitelist of one passes review.
Assert-ImpactCase -Name 'restore-lock-manifest-platform-gateway' -Paths @('docs/reference/api/platform-gateway-restore.manifest.json') -Flags @{
    docs = $true; scripts = $true; backend = $false; frontend = $false
}

# A manifest that does not exist yet must already route to 'scripts'. This is the assertion that
# distinguishes a derived rule from a widened whitelist: it fails if anyone replaces the pattern with
# an enumeration of the two real paths, and it is the only case here that cannot be satisfied by
# listing today's files.
Assert-ImpactCase -Name 'restore-lock-manifest-future' -Paths @('docs/reference/api/not-yet-created-restore.manifest.json') -Flags @{
    docs = $true; scripts = $true; backend = $false; frontend = $false
}

# The neighbouring negative: a Reference document under the same directory that is NOT a restore
# manifest must stay on 'docs' alone. Without it the pattern could be loosened to the whole
# directory and every case above would still pass.
Assert-ImpactCase -Name 'reference-api-non-manifest' -Paths @('docs/reference/api/contracts-and-codegen.md') -Flags @{
    docs = $true; scripts = $false; backend = $false; frontend = $false
}

# The exemption table reaches the same gate through the generic 'scripts/' rule. Asserted rather
# than assumed: it is the file that decides which forks stay silent, and if it ever moved out of
# 'scripts/' the gate would stop being scheduled on changes to it.
Assert-ImpactCase -Name 'restore-lock-exemption-table' -Paths @('scripts/restore-lock-drift-exemptions.json') -Flags @{
    scripts = $true; docs = $false; backend = $false; frontend = $false
}

Assert-ImpactCase -Name 'nested-readme-docs' -Paths @('backend/services/Business/Erp/README.md', 'connector-hosts/README.md') -Flags @{
    docs = $true; backend = $false; frontend = $false; connector_hosts = $false; postgresql = $false; full_chain = $false
}

Assert-ImpactCase -Name 'frontend-package-markdown' -Paths @('frontend/packages/scheduling/README.md') -Flags @{
    docs = $true; frontend = $true; frontend_packages = $true; backend = $false; postgresql = $false; full_chain = $false
}

Assert-ImpactCase -Name 'frontend-guidance-markdown' -Paths @('frontend/AGENTS.md') -Flags @{
    docs = $true; frontend = $true; frontend_apps = $false; frontend_packages = $false; backend = $false
}

Assert-ImpactCase -Name 'single-business-service' -Paths @('backend/services/Business/Erp/src/Orders.cs') -Flags @{
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend_packages = $true; connector_hosts = $false; postgresql = $true; redis_cap = $false; full_chain = $true
} -Services @('erp')

Assert-ImpactCase -Name 'product-engineering-service-name' -Paths @('backend/services/Business/ProductEngineering/src/Release.cs') -Flags @{
    backend = $true; business_gateway = $true; full_chain = $false
} -Services @('product-engineering')

foreach ($erpService in @('Erp', 'DemandPlanning', 'MasterData')) {
    Assert-ImpactCase -Name "sales-order-demand-full-chain-service-$erpService" -Paths @("backend/services/Business/$erpService/src/ObservedChange.cs") -Flags @{
        backend = $true; full_chain = $true
    }
}

Assert-ImpactCase -Name 'common-contract-expansion' -Paths @('backend/common/Contracts/IntegrationEvents.cs') -Flags @{
    backend = $true; backend_contracts = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true; connector_hosts = $true; postgresql = $true; redis_cap = $true; full_chain = $true
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
Assert-Contract ($backendCommonDirectories.Count -eq 12) 'The backend common-directory observation baseline must be revised when a shared directory is added or removed.'
foreach ($commonDirectory in $backendCommonDirectories) {
    $plan = Get-NervCiImpactPlan -ChangedPaths @("backend/common/$commonDirectory/ObservedChange.cs")
    Assert-ImpactFlag -Plan $plan -Name 'backend' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'business_gateway' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'connector_hosts' -Expected $true
    Assert-ImpactFlag -Plan $plan -Name 'full_chain' -Expected $true
    Assert-Contract (@($plan.business_services).Count -gt 10) "Shared backend directory '$commonDirectory' must conservatively expand to every known business service."
}

Assert-ImpactCase -Name 'business-gateway' -Paths @('backend/gateway/BusinessGateway/src/Facade.cs') -Flags @{
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true
}

foreach ($backendBuildInput in @('backend/Directory.Build.props', 'backend/Directory.Packages.props')) {
    Assert-ImpactCase -Name "openapi-backend-build-input-$([IO.Path]::GetFileName($backendBuildInput))" -Paths @($backendBuildInput) -Flags @{
        backend = $true; openapi_codegen = $true; connector_hosts = $true; full_chain = $true
    }
}

foreach ($salesOrderDemandFullChainInput in @(
        'scripts/verify-erp-sales-order-demand-planning.ps1',
        'backend/tests/Nerv.IIP.Business.FullChain.Tests/Scenario.cs',
        'infra/docker-compose.dev.yml'
    )) {
    Assert-ImpactCase -Name "sales-order-demand-full-chain-input-$([IO.Path]::GetFileName($salesOrderDemandFullChainInput))" -Paths @($salesOrderDemandFullChainInput) -Flags @{
        full_chain = $true
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
    postgresql = $false; redis_cap = $false; full_chain = $false
} -Services @()

Assert-ImpactCase -Name 'github-issue-templates' -Paths @(
    '.github/ISSUE_TEMPLATE/bug_report.yml',
    '.github/ISSUE_TEMPLATE/config.yml') -Flags @{
    docs = $true
    backend = $false; frontend = $false; scripts = $false; workflows = $false
    postgresql = $false; redis_cap = $false; full_chain = $false
} -Services @()

# Routing an agent-tooling path must not weaken workflow routing: '.github/workflows/**'
# still fails open even though a sibling '.github/' prefix is now classified.
Assert-ImpactCase -Name 'workflow-still-fails-open-beside-issue-templates' -Paths @('.github/workflows/nightly.yml') -Flags @{
    docs = $true; backend = $true; frontend = $true; scripts = $true; workflows = $true
    postgresql = $true; redis_cap = $true; full_chain = $true
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
    connector_hosts = $true; backend = $false; frontend = $false; full_chain = $false
}

Assert-ImpactCase -Name 'openapi-generation-script' -Paths @('scripts/export-gateway-openapi.ps1') -Flags @{
    scripts = $true; openapi_codegen = $true; business_gateway = $true; frontend = $true; frontend_packages = $true
}

Assert-PostgresLaneOwningPathsRoute
Assert-RedisCapLaneOwningPathsRoute
Assert-FullChainLaneOwningPathsRoute
Assert-FullChainProjectReferenceCoverage
Assert-AcceptanceScenarioMatrixOwningPathsRoute
Assert-AcceptanceCanonicalOwningPathsRoute
Assert-AcceptanceCanonicalPathMutationsDoNotAliasOwners

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

# ⚠️ #3338：下面这几条反例的夹具服务从 Mes 换成 Quality，**换的是夹具、不是期望值**。
# 原因：#3338 起 Mes / Wms / Maintenance / Erp / DemandPlanning 因**被 FullChain 直接 ProjectReference**
# 而无条件选中 full_chain，用 Mes 当夹具会让这几条反例的 full_chain 维度恒为 true、**失去鉴别力**
# （本仓判例：结构变更会静默抽掉上一票断言的前提，而断言还在跑、还在绿）。
# Quality 同样是已登记业务服务，但**不**被 FullChain 引用、也不在 sales-order-demand 集合里，
# 因此 full_chain 对它仍是干净的指示器，这几条反例要钉的「按目录段整段比对、不做前缀包含」
# 与「相邻 Application 子目录不扩面」原样成立。⛔ 别把夹具换回 Mes。
# NERV-1711 反例：同前缀但不同目录不得触发，钉住「按目录段整段比对」而不是前缀包含。
Assert-ImpactCase -Name 'integration-event-converters-prefix-collision' -Paths @('backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConvertersLegacy/LegacyShim.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('quality')

Assert-ImpactCase -Name 'seed-prefix-collision' -Paths @('backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/SeedlingCatalog/SeedlingCatalogQuery.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('quality')

# NERV-1711 反例：同一服务的相邻 Application 子目录仍然只是普通后端改动，
# 钉住新规则没有退化成「任何 backend/services 路径都跑重 lane」。
Assert-ImpactCase -Name 'sibling-application-directory-stays-narrow' -Paths @('backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/WorkOrderQuery.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('quality')

# NERV-1711 反例：测试工程里的同名目录不在 backend/services/ 之下，
# 钉住新规则带着服务前缀限定（处理器的 redis_cap 仍由既有 messaging 规则给出）。
Assert-ImpactCase -Name 'test-project-seed-directory-not-a-service' -Paths @('backend/tests/Nerv.IIP.Business.Mes.Tests/Application/Seed/SeedFixture.cs') -Flags @{
    backend = $true; redis_cap = $false; full_chain = $false
}

Assert-ImpactCase -Name 'test-project-integration-event-handlers-not-a-service' -Paths @('backend/tests/Nerv.IIP.Business.Mes.Tests/Application/IntegrationEventHandlers/HandlerTests.cs') -Flags @{
    backend = $true; redis_cap = $true; full_chain = $false
}

Assert-ImpactCase -Name 'capitalized-is-not-cap' -Paths @('backend/services/Business/Quality/src/CapitalizedUnitCost.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('quality')

Assert-ImpactCase -Name 'capacity-is-not-cap' -Paths @('backend/services/Business/Scheduling/src/FiniteCapacityScheduler.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('scheduling')

Assert-ImpactCase -Name 'full-chain-test' -Paths @('backend/tests/Nerv.IIP.Business.FullChain.Tests/Scenario.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
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
            'frontend_design_system', 'frontend_docs', 'postgresql', 'redis_cap', 'full_chain'
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

$acceptanceRuntimeMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-acceptance-canonical-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($acceptanceRuntimeMutationRoot) | Out-Null
    $runtimeMutationIndex = 0
    foreach ($owningPath in $acceptanceCanonicalOwningPaths) {
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
            Assert-Contract (-not [string]::Equals($weakenedImpactLibrary, $canonicalImpactLibrary, [StringComparison]::Ordinal)) "Acceptance canonical $($mutation.Name) mutation must change owning path '$owningPath'."
            $runtimeMutationIndex++
            $mutationPath = Join-Path $acceptanceRuntimeMutationRoot "$runtimeMutationIndex.CiImpactPlan.ps1"
            [IO.File]::WriteAllText($mutationPath, $weakenedImpactLibrary, [Text.UTF8Encoding]::new($false))
            . $mutationPath
            $mutationFailure = $null
            try { Assert-AcceptanceCanonicalOwningPathsRoute } catch { $mutationFailure = $_ }
            Assert-Contract ($null -ne $mutationFailure) "Acceptance canonical $($mutation.Name) mutation for owning path '$owningPath' must fail the behavioral contract."
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
    Assert-ImpactFlag -Plan $writtenPlan -Name 'full_chain' -Expected $true
    Assert-Contract ($null -eq $writtenPlan.PSObject.Properties['erp_sales_order_demand']) 'Serialized impact plan must omit the retired ERP acceptance signal.'
    $writtenOutputs = @(Get-Content -LiteralPath $fixtureOutputs)
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'backend=true') 'GitHub output must serialize selected booleans as lowercase true.'
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'redis_cap=false') 'GitHub output must serialize unselected booleans as lowercase false.'
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'full_chain=true') 'GitHub output must serialize the conservatively selected FullChain signal.'
    Assert-Contract (@($writtenOutputs | Where-Object { $_.StartsWith('erp_sales_order_demand=', [StringComparison]::Ordinal) }).Count -eq 0) 'GitHub output must omit the retired ERP acceptance signal.'
    Assert-Contract (@($writtenOutputs | Where-Object { $_.StartsWith('business_services=[', [StringComparison]::Ordinal) }).Count -eq 1) 'GitHub output must expose the stable business-services JSON array.'
    $writtenSummary = Get-Content -LiteralPath $fixtureSummary -Raw
    Assert-Contract ($writtenSummary.Contains('NERV-668 routes Backend Tests, Connector Host Tests, Script Governance, and OpenAPI/api-client Drift; NERV-688 routes PostgreSQL Provider Tests and Redis/CAP Transport Tests', [StringComparison]::Ordinal)) 'Actions Summary must identify every routed batch.'
    Assert-Contract (-not $writtenSummary.Contains('erp_sales_order_demand', [StringComparison]::Ordinal) -and -not $writtenSummary.Contains('ERP Sales Order Demand Acceptance', [StringComparison]::Ordinal)) 'Actions Summary must omit the retired ERP job and signal.'
    Assert-Contract ($writtenSummary.Contains('NERV-685 derives governed frontend workspace shards', [StringComparison]::Ordinal)) 'Actions Summary must identify the frontend workspace routing.'
    Assert-Contract ($writtenSummary.Contains('changed:backend/services/Business/Erp/src/Orders.cs', [StringComparison]::Ordinal)) 'Actions Summary must retain the selected signal reason.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

$workflow = [IO.File]::ReadAllText($workflowPath)
Assert-Contract ($workflow.Contains("  impact-plan:`n", [StringComparison]::Ordinal)) 'CI must define the impact-plan job.'
Assert-Contract ($workflow.Contains('uses: actions/upload-artifact@v4', [StringComparison]::Ordinal)) 'The impact-plan job must upload its audit artifact.'
Assert-RedisCapActiveSelectionWorkflowContract -Path $workflowPath

$redisCapMemberListMutation = $workflow.Replace('-AllActiveMembers', '-MemberId stale-hosted-member-redis-cap', [StringComparison]::Ordinal)
Assert-Contract (-not [string]::Equals($redisCapMemberListMutation, $workflow, [StringComparison]::Ordinal)) 'The Redis/CAP hosted-member mutation must alter the canonical workflow invocation.'
$redisCapMemberListMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-redis-cap-selection-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($redisCapMemberListMutationRoot) | Out-Null
    $redisCapMemberListMutationPath = Join-Path $redisCapMemberListMutationRoot 'member-list.yml'
    [IO.File]::WriteAllText($redisCapMemberListMutationPath, $redisCapMemberListMutation, [Text.UTF8Encoding]::new($false))
    $redisCapMemberListMutationFailure = $null
    try { Assert-RedisCapActiveSelectionWorkflowContract -Path $redisCapMemberListMutationPath } catch { $redisCapMemberListMutationFailure = $_ }
    Assert-Contract ($null -ne $redisCapMemberListMutationFailure) 'A hosted Redis/CAP -MemberId list must fail the workflow contract.'
}
finally {
    if (Test-Path -LiteralPath $redisCapMemberListMutationRoot) { Remove-Item -LiteralPath $redisCapMemberListMutationRoot -Recurse -Force }
}

$expectedDotNetJobNames = @(
    'backend-tests-business-gateway'
    'backend-tests-platform'
    'backend-tests-business-core-a'
    'backend-tests-business-core-b'
    'postgres-provider-tests'
    'redis-cap-transport-tests'
    'business-full-chain-acceptance-v1'
    'connector-host-tests'
    'openapi-client-drift'
    'script-governance'
)
$expectedDotNetSdkVersion = '10.0.302'
$expectedDotNetInstallDirectory = '${{ runner.temp }}/dotnet'
$dotNetSdkFindings = @(Get-NervCiDotNetSdkContractFindings -Path $workflowPath -ExpectedJobNames $expectedDotNetJobNames -ExpectedSdkVersion $expectedDotNetSdkVersion -ExpectedInstallDirectory $expectedDotNetInstallDirectory)
Assert-Contract ($dotNetSdkFindings.Count -eq 0) "Every managed CI job must contain exactly one step-scoped isolated setup-dotnet step for SDK $expectedDotNetSdkVersion. Findings=[$($dotNetSdkFindings -join ', ')]"

$dotNetMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-dotnet-sdk-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($dotNetMutationRoot) | Out-Null
    $setupStepPattern = '(?ms)^      - name: Setup \.NET\r?\n        timeout-minutes: 5\r?\n        uses: actions/setup-dotnet@v4\r?\n        env:\r?\n          DOTNET_INSTALL_DIR: \$\{\{ runner\.temp \}\}/dotnet\r?\n        with:\r?\n          dotnet-version: 10\.0\.302\r?\n\r?\n'
    $setupStepRegex = [regex]::new($setupStepPattern)
    Assert-Contract ($setupStepRegex.Matches($workflow).Count -eq $expectedDotNetJobNames.Count) 'The missing-step mutation must recognize every managed setup-dotnet step.'
    $missingSetupWorkflow = $setupStepRegex.Replace($workflow, '', 1)
    $missingSetupPath = Join-Path $dotNetMutationRoot 'missing-setup-dotnet.yml'
    [IO.File]::WriteAllText($missingSetupPath, $missingSetupWorkflow, [Text.UTF8Encoding]::new($false))
    $missingSetupFindings = @(Get-NervCiDotNetSdkContractFindings -Path $missingSetupPath -ExpectedJobNames $expectedDotNetJobNames -ExpectedSdkVersion $expectedDotNetSdkVersion -ExpectedInstallDirectory $expectedDotNetInstallDirectory)
    Assert-Contract ($missingSetupFindings.Count -eq 1 -and [string]::Equals([string]$missingSetupFindings[0], 'ci-setup-dotnet-count:backend-tests-business-gateway:0', [StringComparison]::Ordinal)) 'Deleting one managed setup-dotnet step must produce its exact missing-position finding.'
    $missingSetupParsedWorkflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $missingSetupPath -WorkingDirectory $repoRoot
    $recognizedSetupSteps = 0
    foreach ($jobProperty in $missingSetupParsedWorkflow.jobs.PSObject.Properties) {
        foreach ($step in @($jobProperty.Value.steps)) {
            $usesProperty = $step.PSObject.Properties['uses']
            if ($null -ne $usesProperty -and ([string]$usesProperty.Value).StartsWith('actions/setup-dotnet@', [StringComparison]::Ordinal)) { $recognizedSetupSteps++ }
        }
    }
    Write-Output "CI SDK missing-step mutation: RECOGNIZED=$recognizedSetupSteps FINDINGS=$($missingSetupFindings.Count) [$($missingSetupFindings -join ', ')]"

    $exactVersionPattern = 'dotnet-version:\s*' + [regex]::Escape($expectedDotNetSdkVersion)
    $exactVersionRegex = [regex]::new($exactVersionPattern)
    Assert-Contract ($exactVersionRegex.Matches($workflow).Count -eq $expectedDotNetJobNames.Count) 'The floating-version mutation must recognize every exact SDK selector.'
    $floatingSdkWorkflow = $exactVersionRegex.Replace($workflow, 'dotnet-version: 10.0.x', 1)
    $floatingSdkPath = Join-Path $dotNetMutationRoot 'floating-dotnet-sdk.yml'
    [IO.File]::WriteAllText($floatingSdkPath, $floatingSdkWorkflow, [Text.UTF8Encoding]::new($false))
    $floatingSdkFindings = @(Get-NervCiDotNetSdkContractFindings -Path $floatingSdkPath -ExpectedJobNames $expectedDotNetJobNames -ExpectedSdkVersion $expectedDotNetSdkVersion -ExpectedInstallDirectory $expectedDotNetInstallDirectory)
    Assert-Contract ($floatingSdkFindings.Count -eq 1 -and [string]::Equals([string]$floatingSdkFindings[0], 'ci-dotnet-sdk-version:backend-tests-business-gateway:10.0.x', [StringComparison]::Ordinal)) 'Changing one managed setup-dotnet step back to 10.0.x must produce its exact version finding.'
    Write-Output "CI SDK floating-version mutation: FINDINGS=$($floatingSdkFindings.Count) [$($floatingSdkFindings -join ', ')]"
}
finally {
    if (Test-Path -LiteralPath $dotNetMutationRoot) { Remove-Item -LiteralPath $dotNetMutationRoot -Recurse -Force }
}

Assert-ConditionalRoutingWorkflow -Path $workflowPath
Assert-BusinessConsoleBrowserValidationWorkflowContract -Path $workflowPath
Assert-AcceptanceScenarioMatrixWorkflowContract -Path $workflowPath

$workflowMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($workflowMutationRoot) | Out-Null
    # #4204：内部治理接线不是公开合同。改名、重排、合并 run 体与删除预算注释
    # 不改变 impact 选取与 artifact 身份；这些稳定行为仍须通过同一合同。
    $governanceStart = $workflow.IndexOf('  script-governance:', [StringComparison]::Ordinal)
    $governanceEnd = $workflow.IndexOf('  ci-summary:', $governanceStart, [StringComparison]::Ordinal)
    $governanceSource = $workflow.Substring($governanceStart, $governanceEnd - $governanceStart)
    $scenarioStep = @'
      - name: Test acceptance scenario matrix contract
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/acceptance-scenario-matrix.Tests.ps1

'@
    $canonicalStep = @'
      - name: Test acceptance canonical result contract
        timeout-minutes: 5
        shell: pwsh
        run: ./scripts/tests/acceptance-canonical-result.Tests.ps1

'@
    $combinedStep = @'
      - name: Acceptance fixtures
        timeout-minutes: 10
        shell: pwsh
        run: |
          ./scripts/tests/acceptance-canonical-result.Tests.ps1
          ./scripts/tests/acceptance-scenario-matrix.Tests.ps1

'@
    $rearrangedGovernance = $governanceSource.Replace($scenarioStep, '').Replace($canonicalStep, $combinedStep)
    $rearrangedGovernance = [regex]::Replace($rearrangedGovernance, '(?m)^      - name: [^\n]+', '      - name: Renamed governance entry')
    $rearrangedGovernance = [regex]::Replace($rearrangedGovernance, '(?m)^    #[^\n]*\n', '')
    Assert-Contract (-not [string]::Equals($rearrangedGovernance, $governanceSource, [StringComparison]::Ordinal)) 'The legal rearrangement control must change governance wiring.'
    $rearrangedWorkflowPath = Join-Path $workflowMutationRoot 'rearranged-governance.yml'
    [IO.File]::WriteAllText($rearrangedWorkflowPath, $workflow.Replace($governanceSource, $rearrangedGovernance), [Text.UTF8Encoding]::new($false))
    Assert-ConditionalRoutingWorkflow -Path $rearrangedWorkflowPath
    Assert-AcceptanceScenarioMatrixWorkflowContract -Path $rearrangedWorkflowPath
    Write-Output 'Governance wiring rearrangement control: PASS (renamed, merged, reversed fixture commands; no budget comments).'

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

    foreach ($browserMutation in @(
            @{
                Name = 'business-console-browser-diagnostics-not-failure-only'
                Original = "        if: failure() && steps.business-console-browser-tests.outcome == 'failure'"
                Replacement = '        if: always()'
            },
            @{
                Name = 'business-console-browser-diagnostics-missing'
                Original = '      - name: Upload Business Console browser diagnostics'
                Replacement = '      - name: Browser diagnostic upload removed by mutation'
            }
        )) {
        $mutated = $workflow.Replace($browserMutation.Original, $browserMutation.Replacement)
        Assert-Contract (-not [string]::Equals($mutated, $workflow, [StringComparison]::Ordinal)) "Business Console browser mutation '$($browserMutation.Name)' must match the canonical workflow."
        $mutationPath = Join-Path $workflowMutationRoot "$($browserMutation.Name).yml"
        [IO.File]::WriteAllText($mutationPath, $mutated, [Text.UTF8Encoding]::new($false))
        $failure = $null
        try { Assert-BusinessConsoleBrowserValidationWorkflowContract -Path $mutationPath } catch { $failure = $_ }
        Assert-Contract ($null -ne $failure) "Business Console browser mutation '$($browserMutation.Name)' must be rejected."
    }
}
finally {
    if (Test-Path -LiteralPath $workflowMutationRoot) { Remove-Item -LiteralPath $workflowMutationRoot -Recurse -Force }
}

Write-Output 'CI impact-plan contract tests passed.'
