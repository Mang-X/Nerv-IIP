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
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')

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
        'openapi-client-drift' = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
        'script-governance' = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.scripts != 'false' || needs.impact-plan.outputs.backend != 'false') }}"
    }

    $impactPlan = $parsedWorkflow.jobs.PSObject.Properties['impact-plan'].Value
    foreach ($outputName in @('scripts', 'backend', 'openapi_codegen')) {
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

    foreach ($jobProperty in @($parsedWorkflow.jobs.PSObject.Properties | Where-Object {
                -not [string]::Equals($_.Name, 'impact-plan', [StringComparison]::Ordinal) -and
                -not [string]::Equals($_.Name, 'openapi-client-drift', [StringComparison]::Ordinal) -and
                -not [string]::Equals($_.Name, 'script-governance', [StringComparison]::Ordinal) -and
                -not [string]::Equals($_.Name, 'ci-summary', [StringComparison]::Ordinal)
            })) {
        $job = $jobProperty.Value
        $needsProperty = $job.PSObject.Properties['needs']
        [string[]]$needs = @()
        if ($null -ne $needsProperty) { $needs = @($needsProperty.Value | ForEach-Object { [string]$_ }) }
        Assert-Contract ($needs.Count -eq 0 -or -not (Test-OrdinalMember -Values $needs -Expected 'impact-plan')) "Unrouted job '$($jobProperty.Name)' must not depend on impact-plan."
        $conditionProperty = $job.PSObject.Properties['if']
        $condition = if ($null -eq $conditionProperty) { '' } else { [string]$conditionProperty.Value }
        Assert-Contract (-not $condition.Contains('impact-plan', [StringComparison]::Ordinal)) "Unrouted job '$($jobProperty.Name)' must not consume impact-plan outputs."
    }
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

Assert-Contract (Test-Path -LiteralPath $libraryPath -PathType Leaf) 'The CI impact-plan library is missing.'
. $libraryPath

Assert-ImpactCase -Name 'pure-docs' -Paths @('README.md', 'docs/architecture/context-map.md') -Flags @{
    docs = $true; backend = $false; frontend = $false; scripts = $false; connector_hosts = $false; postgresql = $false; full_chain = $false
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
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend_packages = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('erp')

Assert-ImpactCase -Name 'product-engineering-service-name' -Paths @('backend/services/Business/ProductEngineering/src/Release.cs') -Flags @{
    backend = $true; business_gateway = $true
} -Services @('product-engineering')

Assert-ImpactCase -Name 'common-contract-expansion' -Paths @('backend/common/Contracts/IntegrationEvents.cs') -Flags @{
    backend = $true; backend_contracts = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true; postgresql = $true; redis_cap = $true; full_chain = $true
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
    Assert-ImpactFlag -Plan $plan -Name 'full_chain' -Expected $true
    Assert-Contract (@($plan.business_services).Count -gt 10) "Shared backend directory '$commonDirectory' must conservatively expand to every known business service."
}

Assert-ImpactCase -Name 'business-gateway' -Paths @('backend/gateway/BusinessGateway/src/Facade.cs') -Flags @{
    backend = $true; business_gateway = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true
}

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

Assert-ImpactCase -Name 'frontend-design-system' -Paths @('frontend/DESIGN/components/button.md') -Flags @{
    frontend = $true; frontend_design_system = $true; frontend_docs = $true; docs = $true
}

Assert-ImpactCase -Name 'connector-hosts' -Paths @('connector-hosts/src/Host.cs') -Flags @{
    connector_hosts = $true; backend = $false; frontend = $false
}

Assert-ImpactCase -Name 'openapi-generation-script' -Paths @('scripts/export-gateway-openapi.ps1') -Flags @{
    scripts = $true; openapi_codegen = $true; business_gateway = $true; frontend = $true; frontend_packages = $true
}

Assert-ImpactCase -Name 'platform-gateway-openapi' -Paths @('backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/OpenApi/GatewayOperationIdConvention.cs') -Flags @{
    backend = $true; openapi_codegen = $true; frontend = $true; frontend_packages = $true
}

Assert-ImpactCase -Name 'service-cap-integration' -Paths @('backend/services/Business/Mes/src/MesCapServiceCollectionExtensions.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
} -Services @('mes')

Assert-ImpactCase -Name 'integration-event-handler' -Paths @('backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/WorkOrderCostCapitalizedIntegrationEventHandler.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $true; full_chain = $true
} -Services @('mes')

Assert-ImpactCase -Name 'capitalized-is-not-cap' -Paths @('backend/services/Business/Mes/src/CapitalizedUnitCost.cs') -Flags @{
    backend = $true; postgresql = $true; redis_cap = $false; full_chain = $false
} -Services @('mes')

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
    $writtenOutputs = @(Get-Content -LiteralPath $fixtureOutputs)
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'backend=true') 'GitHub output must serialize selected booleans as lowercase true.'
    Assert-Contract (Test-OrdinalMember -Values $writtenOutputs -Expected 'redis_cap=false') 'GitHub output must serialize unselected booleans as lowercase false.'
    Assert-Contract (@($writtenOutputs | Where-Object { $_.StartsWith('business_services=[', [StringComparison]::Ordinal) }).Count -eq 1) 'GitHub output must expose the stable business-services JSON array.'
    $writtenSummary = Get-Content -LiteralPath $fixtureSummary -Raw
    Assert-Contract ($writtenSummary.Contains('first routed batch', [StringComparison]::Ordinal)) 'Actions Summary must identify the first routed batch.'
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

$workflowMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($workflowMutationRoot) | Out-Null
    foreach ($mutation in @(
            @{
                Name = 'unrouted-job-consumes-plan'
                Original = "  frontend:`n    name: Frontend Typecheck and Build"
                Replacement = "  frontend:`n    name: Frontend Typecheck and Build`n    needs: impact-plan"
            },
            @{
                Name = 'openapi-drops-plan-failure-fail-open'
                Original = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
                Replacement = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
            },
            @{
                Name = 'openapi-drops-always'
                Original = "`${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
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
                Original = "  openapi-client-drift:`n    name: OpenAPI/api-client Drift`n"
                Replacement = "  openapi-client-drift:`n    name: OpenAPI/api-client Drift`n"
                SecondaryOriginal = "    needs: impact-plan`n    if: >-`n      `${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}`n"
                SecondaryReplacement = "    if: >-`n      `${{ always() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}`n"
            }
        )) {
        $mutated = $workflow.Replace($mutation.Original, $mutation.Replacement)
        if ($mutation.ContainsKey('SecondaryOriginal')) {
            $mutated = $mutated.Replace([string]$mutation.SecondaryOriginal, [string]$mutation.SecondaryReplacement)
        }
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
