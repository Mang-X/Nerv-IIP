# Script-Governance:
#   Category: library
#   SideEffects:
#     - Classifies caller-provided repository-relative changed paths
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function ConvertTo-NervCiImpactServiceId {
    param([Parameter(Mandatory)] [string] $Name)

    return ([regex]::Replace($Name, '(?<=[a-z0-9])(?=[A-Z])', '-')).ToLowerInvariant()
}

function Get-NervCiImpactPlan {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ChangedPaths
    )

    if ($ChangedPaths.Count -eq 0) {
        throw 'At least one changed repository path is required.'
    }

    $knownBusinessServiceNames = @(
        'Approval',
        'BarcodeLabel',
        'DemandPlanning',
        'Erp',
        'IndustrialTelemetry',
        'Inventory',
        'Maintenance',
        'MasterData',
        'Mes',
        'ProductEngineering',
        'Quality',
        'Scheduling',
        'Wms'
    )
    $knownBusinessServiceNameSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($knownBusinessServiceName in $knownBusinessServiceNames) { [void]$knownBusinessServiceNameSet.Add($knownBusinessServiceName) }
    $knownBusinessServices = @($knownBusinessServiceNames | ForEach-Object { ConvertTo-NervCiImpactServiceId -Name $_ })
    $flags = [ordered]@{
        backend = $false
        frontend = $false
        scripts = $false
        docs = $false
        connector_hosts = $false
        workflows = $false
        infra = $false
        backend_contracts = $false
        backend_testing = $false
        backend_persistence = $false
        backend_messaging = $false
        business_gateway = $false
        openapi_codegen = $false
        frontend_apps = $false
        frontend_packages = $false
        frontend_design_system = $false
        frontend_docs = $false
        postgresql = $false
        redis_cap = $false
        full_chain = $false
    }
    $reasonLists = [ordered]@{}
    $serviceSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    function Select-Impact {
        param(
            [Parameter(Mandatory)] [string] $Name,
            [Parameter(Mandatory)] [string] $Reason
        )

        if (-not $flags.Contains($Name)) { throw "Unknown CI impact flag '$Name'." }
        $flags[$Name] = $true
        if (-not $reasonLists.Contains($Name)) {
            $reasonLists[$Name] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
        [void]$reasonLists[$Name].Add($Reason)
    }

    function Select-BusinessServices {
        param(
            [Parameter(Mandatory)] [string[]] $Services,
            [Parameter(Mandatory)] [string] $Reason
        )

        foreach ($service in $Services) { [void]$serviceSet.Add($service) }
        Select-Impact -Name 'backend' -Reason $Reason
        Select-Impact -Name 'business_gateway' -Reason $Reason
        Select-Impact -Name 'openapi_codegen' -Reason $Reason
        Select-Impact -Name 'frontend' -Reason $Reason
        Select-Impact -Name 'frontend_packages' -Reason $Reason
        Select-Impact -Name 'postgresql' -Reason $Reason
    }

    function Select-AllImpacts {
        param([Parameter(Mandatory)] [string] $Reason)

        foreach ($flagName in @($flags.Keys)) { Select-Impact -Name $flagName -Reason $Reason }
        foreach ($service in $knownBusinessServices) { [void]$serviceSet.Add($service) }
    }

    function Test-MessagingImpactPath {
        param([Parameter(Mandatory)] [string] $Path)

        return [regex]::IsMatch($Path, '(?:^|[/_.-])CAP(?:$|[/_.-])', [Text.RegularExpressions.RegexOptions]::IgnoreCase) -or
            [regex]::IsMatch($Path, '(?<=[a-z0-9])Cap(?=[A-Z0-9])', [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
            $Path.Contains('Redis', [StringComparison]::OrdinalIgnoreCase) -or
            $Path.Contains('Messaging', [StringComparison]::OrdinalIgnoreCase) -or
            $Path.Contains('/IntegrationEventHandlers/', [StringComparison]::Ordinal) -or
            [regex]::IsMatch($Path, 'IntegrationEventHandler\.cs$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }

    $normalizedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rawPath in $ChangedPaths) {
        if ([string]::IsNullOrWhiteSpace($rawPath)) { throw 'Changed repository paths cannot be empty.' }
        $path = $rawPath.Replace('\', '/')
        if ($path.StartsWith('/', [StringComparison]::Ordinal) -or
            $path -match '^[A-Za-z]:/' -or
            $path.Contains('../', [StringComparison]::Ordinal) -or
            $path.Contains('//', [StringComparison]::Ordinal) -or
            $path.StartsWith('./', [StringComparison]::Ordinal)) {
            throw "Changed path '$rawPath' must be a normalized repository-relative path."
        }
        [void]$normalizedSet.Add($path)
    }
    $normalizedPaths = @($normalizedSet)
    [Array]::Sort($normalizedPaths, [StringComparer]::Ordinal)

    foreach ($path in $normalizedPaths) {
        $reason = "changed:$path"
        $isRuleSelfChange = [string]::Equals($path, '.github/workflows/ci.yml', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/CiImpactPlan.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/get-ci-impact-plan.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/tests/ci-impact-plan.Tests.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/FrontendWorkspacePlan.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/get-frontend-workspace-plan.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/frontend-test-skip-allowlist.json', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/tests/frontend-workspace-plan.Tests.ps1', [StringComparison]::Ordinal)
        if ($isRuleSelfChange -or $path.StartsWith('.github/workflows/', [StringComparison]::Ordinal)) {
            Select-AllImpacts -Reason "rule-self-check:$path"
            Select-Impact -Name 'workflows' -Reason $reason
            continue
        }

        if ([string]::Equals($path, 'docs/architecture/script-automation-governance.md', [StringComparison]::Ordinal)) {
            foreach ($flag in @('docs', 'scripts')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        if ([string]::Equals($path, 'backend/Directory.Build.props', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'backend/Directory.Packages.props', [StringComparison]::Ordinal)) {
            foreach ($flag in @('backend', 'openapi_codegen')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        if ([string]::Equals($path, 'frontend/package.json', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'frontend/pnpm-lock.yaml', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'frontend/pnpm-workspace.yaml', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_packages', 'openapi_codegen')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        if ($path.StartsWith('frontend/', [StringComparison]::Ordinal) -and $path.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
            foreach ($flag in @('frontend', 'docs')) { Select-Impact -Name $flag -Reason $reason }
            if ($path.StartsWith('frontend/apps/', [StringComparison]::Ordinal)) { Select-Impact -Name 'frontend_apps' -Reason $reason }
            if ($path.StartsWith('frontend/packages/', [StringComparison]::Ordinal)) { Select-Impact -Name 'frontend_packages' -Reason $reason }
            if ($path.StartsWith('frontend/DESIGN/', [StringComparison]::Ordinal)) {
                foreach ($flag in @('frontend_design_system', 'frontend_docs')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ($path.StartsWith('frontend/apps/docs/', [StringComparison]::Ordinal)) { Select-Impact -Name 'frontend_docs' -Reason $reason }
            if ($path.StartsWith('frontend/apps/design-system/', [StringComparison]::Ordinal)) { Select-Impact -Name 'frontend_design_system' -Reason $reason }
            continue
        }

        if ($path.StartsWith('frontend/DESIGN/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_design_system', 'frontend_docs', 'docs')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('frontend/apps/docs/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_apps', 'frontend_docs', 'docs')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('frontend/apps/design-system/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_apps', 'frontend_design_system')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('docs/', [StringComparison]::Ordinal) -or $path.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
            Select-Impact -Name 'docs' -Reason $reason
            continue
        }

        if ($path.StartsWith('backend/common/Contracts/', [StringComparison]::Ordinal)) {
            Select-BusinessServices -Services $knownBusinessServices -Reason $reason
            foreach ($flag in @('backend_contracts', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/common/Testing/', [StringComparison]::Ordinal)) {
            Select-BusinessServices -Services $knownBusinessServices -Reason $reason
            foreach ($flag in @('backend_testing', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/common/Persistence/', [StringComparison]::Ordinal)) {
            Select-BusinessServices -Services $knownBusinessServices -Reason $reason
            foreach ($flag in @('backend_persistence', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/common/Messaging/', [StringComparison]::Ordinal)) {
            Select-BusinessServices -Services $knownBusinessServices -Reason $reason
            foreach ($flag in @('backend_messaging', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/common/', [StringComparison]::Ordinal)) {
            Select-BusinessServices -Services $knownBusinessServices -Reason $reason
            Select-Impact -Name 'full_chain' -Reason $reason
            if (Test-MessagingImpactPath -Path $path) { Select-Impact -Name 'redis_cap' -Reason $reason }
            continue
        }

        $businessServiceMatch = [regex]::Match($path, '^backend/services/Business/([^/]+)/')
        if ($businessServiceMatch.Success) {
            $serviceName = $businessServiceMatch.Groups[1].Value
            if (-not $knownBusinessServiceNameSet.Contains($serviceName)) {
                Select-AllImpacts -Reason "unclassified-business-service:$path"
                continue
            }
            Select-BusinessServices -Services @((ConvertTo-NervCiImpactServiceId -Name $serviceName)) -Reason $reason
            if (Test-MessagingImpactPath -Path $path) {
                foreach ($flag in @('redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            continue
        }

        if ($path.StartsWith('backend/gateway/BusinessGateway/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('backend', 'business_gateway', 'openapi_codegen', 'frontend', 'frontend_packages')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/gateway/PlatformGateway/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('backend', 'openapi_codegen', 'frontend', 'frontend_packages')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/tests/Nerv.IIP.Business.FullChain.Tests/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('backend', 'postgresql', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('backend/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'backend' -Reason $reason
            if ($path.Contains('Postgres', [StringComparison]::OrdinalIgnoreCase) -or $path.Contains('Persistence', [StringComparison]::OrdinalIgnoreCase)) {
                Select-Impact -Name 'postgresql' -Reason $reason
            }
            if (Test-MessagingImpactPath -Path $path) {
                Select-Impact -Name 'redis_cap' -Reason $reason
            }
            continue
        }

        if ($path.StartsWith('frontend/apps/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_apps')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }
        if ($path.StartsWith('frontend/packages/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('frontend', 'frontend_packages')) { Select-Impact -Name $flag -Reason $reason }
            if ($path.StartsWith('frontend/packages/api-client/', [StringComparison]::Ordinal)) {
                foreach ($flag in @('openapi_codegen', 'business_gateway')) { Select-Impact -Name $flag -Reason $reason }
            }
            continue
        }
        if ($path.StartsWith('frontend/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'frontend' -Reason $reason
            continue
        }

        if ($path.StartsWith('connector-hosts/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'connector_hosts' -Reason $reason
            continue
        }
        if ($path.StartsWith('scripts/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'scripts' -Reason $reason
            if ([string]::Equals($path, 'scripts/export-gateway-openapi.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/verify-openapi-client-drift.ps1', [StringComparison]::Ordinal)) {
                foreach ($flag in @('openapi_codegen', 'business_gateway', 'frontend', 'frontend_packages')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ($path.Contains('full-chain', [StringComparison]::OrdinalIgnoreCase) -or $path.Contains('fullstack', [StringComparison]::OrdinalIgnoreCase)) {
                foreach ($flag in @('postgresql', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            continue
        }
        if ($path.StartsWith('infra/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'infra' -Reason $reason
            if ($path.StartsWith('infra/aspire/', [StringComparison]::Ordinal)) {
                foreach ($flag in @('postgresql', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ($path.StartsWith('infra/postgres/', [StringComparison]::Ordinal)) { Select-Impact -Name 'postgresql' -Reason $reason }
            if ($path.Contains('redis', [StringComparison]::OrdinalIgnoreCase)) { Select-Impact -Name 'redis_cap' -Reason $reason }
            continue
        }

        Select-AllImpacts -Reason "unclassified-path:$path"
    }

    $services = @($serviceSet)
    [Array]::Sort($services, [StringComparer]::Ordinal)
    $reasons = [ordered]@{}
    foreach ($flagName in $reasonLists.Keys) {
        $values = @($reasonLists[$flagName])
        [Array]::Sort($values, [StringComparer]::Ordinal)
        $reasons[$flagName] = $values
    }

    $plan = [ordered]@{
        schema_version = 1
        changed_paths = $normalizedPaths
        business_services = $services
    }
    foreach ($entry in $flags.GetEnumerator()) { $plan[$entry.Key] = [bool]$entry.Value }
    $plan['reasons'] = [pscustomobject]$reasons
    return [pscustomobject]$plan
}
