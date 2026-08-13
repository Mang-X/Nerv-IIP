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
        'backend-test-shard-governance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-gateway' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-platform' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-core-a' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'backend-tests-business-core-b' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.backend != 'false') }}"
        'erp-sales-order-demand-acceptance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}"
        'connector-host-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.connector_hosts != 'false') }}"
        'openapi-client-drift' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.openapi_codegen != 'false') }}"
        'postgres-provider-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.postgresql != 'false') }}"
        'redis-cap-transport-tests' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.redis_cap != 'false') }}"
        'business-full-chain-acceptance' = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.full_chain != 'false') }}"
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
            'postgres-provider-tests', 'redis-cap-transport-tests', 'business-full-chain-acceptance', 'script-governance', 'ci-summary'
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
            'scripts/tests/full-chain-test-lane.Tests.ps1'
        )) {
        Assert-ImpactCase -Name "full-chain-lane-owner-$([IO.Path]::GetFileName($owningPath))" -Paths @($owningPath) -Flags @{
            scripts = $true; backend = $true; postgresql = $false; redis_cap = $false; full_chain = $true
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

$workflowMutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ci-impact-workflow-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($workflowMutationRoot) | Out-Null
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
                Original = "    needs: impact-plan`n    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}`n"
                Replacement = "    if: >-`n      `${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}`n"
            },
            @{
                Name = 'erp-drops-plan-failure-fail-open'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}"
                Replacement = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}"
            },
            @{
                Name = 'erp-drops-cancellation-guard'
                Original = "`${{ !cancelled() && (github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false') }}"
                Replacement = "`${{ github.event_name != 'pull_request' || needs.impact-plan.result != 'success' || needs.impact-plan.outputs.erp_sales_order_demand != 'false' }}"
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
