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
    <#
        #3285 扫描时点名、复核后**判定不改**，把理由写在这里，免得后来人以为这里已经安全：

        本参数是「`Mandatory [string[]]` 公开参数今天收着一段被切好的进程 stdout」这个形状的位点之一
        （`scripts/get-ci-impact-plan.ps1` 的 Diff 参数集把 `git diff --name-only` 的 stdout 切行后
        递进来）。同面直连一跳的另一处是 `Get-NervStringsSorted -Values`（带 `AllowEmptyString`，由类型免疫）；
        隔一跳的还有 `Get-NervDockerInspectObjects -Identifiers`（`FullStackSessionRuntime.ps1:1651`，
        `[AllowEmptyCollection()] [string[]]`、**没有** `AllowEmptyString` 因而不免疫，今天不炸只因
        生产者 `Get-NervDockerListedValues` 在 `FullStackSessionRuntime.ps1:1645` 内部就过滤掉了空白行）。若该调用点的 `IsNullOrWhiteSpace` 过滤被去掉，`git diff` 输出的尾随换行同样会产生空
        元素、同样在绑定处报 `because it is an empty string` ——**它不靠类型，靠调用方过滤**。

        不照 #3279/#3285 改成收原始 stdout 的原因是本参数的契约不是「某个进程的输出」：Paths 参数集
        直接从命令行收路径列表，测试也从 13 个调用点递字面路径数组。把它改成 `[string] $DiffOutput`
        等于把一个通用的「路径集合」入参钉死到 `git diff` 这一个生产者上，反而更窄更错。这里能收紧的
        只有「空元素非法」这一条，而 `Mandatory [string[]]` 本来就已经在表达它。
    #>
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
    $salesOrderDemandBusinessServiceNameSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('Erp', 'DemandPlanning', 'MasterData'),
        [StringComparer]::Ordinal)
    # #3338：被 Nerv.IIP.Business.FullChain.Tests 直接 ProjectReference 的业务服务。
    # 与上面那个集合**语义不同、不可合并**：上面是「sales-order-demand 这个场景需要谁」，
    # 这里是「FullChain 测试程序集在编译期就引用了谁」——引用了就意味着改它可能让 FullChain 变红。
    # ⚠️ 这仍是一份名单，但它的**完备性**由 scripts/tests/ci-impact-plan.Tests.ps1 的
    # Assert-FullChainProjectReferenceCoverage 从 .csproj 的 ProjectReference **派生**出来看守：
    # 新增一条 ProjectReference 而忘了更新本集合，那条契约立刻红。别手工往这里加而不跑那条契约。
    $fullChainReferencedBusinessServiceNameSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('Erp', 'DemandPlanning', 'Maintenance', 'Mes', 'Wms'),
        [StringComparer]::Ordinal)
    $acceptanceScenarioMatrixOwningPathSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            'scripts/acceptance-scenario-matrix.json'
            'scripts/lib/AcceptanceScenarioMatrix.ps1'
            'scripts/plan-acceptance-scenario-matrix.ps1'
            'scripts/tests/acceptance-scenario-matrix.Tests.ps1'
        ),
        [StringComparer]::Ordinal)
    $acceptanceScenarioMatrixRuntimeOwningPathSet = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            'scripts/lib/AcceptanceScenarioMatrixRuntime.ps1'
            'scripts/run-acceptance-scenario-matrix.ps1'
            'scripts/tests/acceptance-scenario-matrix-runtime.Tests.ps1'
            'scripts/lib/AcceptanceScenarioMatrixEquivalence.ps1'
            'scripts/verify-acceptance-scenario-matrix-equivalence.ps1'
            'scripts/tests/acceptance-scenario-matrix-equivalence.Tests.ps1'
        ),
        [StringComparer]::Ordinal)
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

    # 跨服务事件的实际收发点：转换器把领域事件译成集成事件（发信侧），
    # 处理器消费别的服务发来的集成事件（收信侧）。改这两处等于改跨服务契约的
    # 内容，必须同时验收 Redis/CAP 传输与 FullChain 场景，所以两个 lane 都选中。
    # 目录名带尾斜杠比对，避免 'IntegrationEventConvertersLegacy' 这类同前缀目录被误收。
    function Test-CrossServiceIntegrationEventPath {
        param([Parameter(Mandatory)] [string] $Path)

        return $Path.StartsWith('backend/services/', [StringComparison]::Ordinal) -and
            ($Path.Contains('/Application/IntegrationEventConverters/', [StringComparison]::Ordinal) -or
                $Path.Contains('/Application/IntegrationEventHandlers/', [StringComparison]::Ordinal))
    }

    # 世界观种子引擎是 FullChain lane 赖以运行的数据基础：种子造错数据，
    # 五条场景本身就跑在错数据上。所以改种子必须跑 FullChain；种子不涉及
    # CAP 传输面本身，不牵连 redis_cap。
    function Test-FullChainSeedPath {
        param([Parameter(Mandatory)] [string] $Path)

        return $Path.StartsWith('backend/services/', [StringComparison]::Ordinal) -and
            $Path.Contains('/Application/Seed/', [StringComparison]::Ordinal)
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

        if ($path.StartsWith('docs/governance/testing/', [StringComparison]::Ordinal)) {
            foreach ($flag in @('docs', 'scripts')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        if ([string]::Equals($path, 'docs/governance/script-automation.md', [StringComparison]::Ordinal)) {
            foreach ($flag in @('docs', 'scripts')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        # #3145: a restore manifest is the hash ledger that scripts/verify-restore-lock-contract.ps1
        # reads, and the checker runs in the 'Script Governance' job, whose `if` is
        # `scripts != false || backend != false`. Left to the generic 'docs/' rule below, a PR that
        # edits only a manifest routes to 'docs' alone, the job is skipped, and the one gate that
        # reads the file never runs on the change it exists to catch — the shape this repository has
        # already been caught by in #3003, #3135 and #3140. It routes to 'docs' as well because it is
        # still a Reference document. 'backend' is deliberately not selected: the checker reads files
        # only, and pulling in the 45-minute backend shards would buy nothing.
        #
        # #3157: this was a single hardcoded path when only BusinessGateway had a manifest, and the
        # comment above already named the #3003/#3135/#3140 shape while the rule underneath it was a
        # whitelist of one — so adding PlatformGateway's manifest would have routed to 'docs' alone
        # and reproduced the very defect the comment warns about. The rule is now DERIVED from the
        # same 'docs/reference/api/*-restore.manifest.json' pattern the checker discovers manifests
        # by, so the routing set and the checked set cannot drift apart: a manifest that the checker
        # picks up is a manifest this router selects 'scripts' for, by construction rather than by
        # someone remembering to edit two files. Widening the whitelist from one entry to two would
        # have left the same trap armed for the third manifest.
        if ($path.StartsWith('docs/reference/api/', [StringComparison]::Ordinal) -and
            $path.EndsWith('-restore.manifest.json', [StringComparison]::Ordinal)) {
            foreach ($flag in @('docs', 'scripts')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        # Agent-harness configuration only reaches local agent runtimes: the skill payload
        # directories these install into are gitignored, and no CI job reads them. They
        # route to 'docs' like the AGENTS.md guidance they sit beside. 'skills/' is the
        # project-owned skill source those lockfile entries point at, so it belongs to
        # the same class. 't3.json' is the
        # T3 Code project file and belongs to the same class: it only declares that
        # harness's worktree-create action, which shells out to
        # 'scripts/setup-worktree.ps1' exactly like the '.claude' SessionStart hook and
        # the '.codex' '[setup]' block already do. Runtime and build
        # inputs that happen to live in the repository root ('.gitattributes',
        # '.gitignore', '.node-version', 'aspire.config.json', 'dotnet-tools.json',
        # 'nerv.ps1') are deliberately absent here so they keep failing open.
        if ([string]::Equals($path, 'skills-lock.json', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 't3.json', [StringComparison]::Ordinal) -or
            $path.StartsWith('skills/', [StringComparison]::Ordinal) -or
            $path.StartsWith('.claude/', [StringComparison]::Ordinal) -or
            $path.StartsWith('.codex/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'docs' -Reason $reason
            continue
        }

        # Issue templates are collaboration metadata. The earlier rule already claimed
        # every other '.github/workflows/' path, so this cannot weaken workflow routing.
        if ($path.StartsWith('.github/ISSUE_TEMPLATE/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'docs' -Reason $reason
            continue
        }

        if ([string]::Equals($path, 'NuGet.config', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/ScriptAutomation.ps1', [StringComparison]::Ordinal)) {
            Select-AllImpacts -Reason "shared-control-input:$path"
            continue
        }

        if ([string]::Equals($path, 'scripts/backend-test-shards.json', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/test-evidence-policy.json', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/run-backend-test-shard.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/verify-backend-test-shards.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/verify-backend-real-postgres-tests.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/verify-business-full-chain-acceptance.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/verify-business-performance-baseline.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/OrdinalString.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/BackendTestShardSelectors.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/lib/BackendTestShardDiagnostics.ps1', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'scripts/tests/backend-test-shards.Tests.ps1', [StringComparison]::Ordinal)) {
            foreach ($flag in @('scripts', 'backend')) { Select-Impact -Name $flag -Reason $reason }
            continue
        }

        if ([string]::Equals($path, 'backend/Directory.Build.props', [StringComparison]::Ordinal) -or
            [string]::Equals($path, 'backend/Directory.Packages.props', [StringComparison]::Ordinal)) {
            foreach ($flag in @('backend', 'openapi_codegen', 'connector_hosts', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
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

        if ($path.StartsWith('backend/common/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'connector_hosts' -Reason $reason
            Select-Impact -Name 'full_chain' -Reason $reason
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
            if ($salesOrderDemandBusinessServiceNameSet.Contains($serviceName) -or
                $fullChainReferencedBusinessServiceNameSet.Contains($serviceName)) {
                Select-Impact -Name 'full_chain' -Reason $reason
            }
            if ((Test-MessagingImpactPath -Path $path) -or (Test-CrossServiceIntegrationEventPath -Path $path)) {
                foreach ($flag in @('redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            if (Test-FullChainSeedPath -Path $path) { Select-Impact -Name 'full_chain' -Reason $reason }
            continue
        }

        if ($path.StartsWith('backend/gateway/BusinessGateway/', [StringComparison]::Ordinal)) {
            # #3338：FullChain.Tests 直接 ProjectReference 了 BusinessGateway.Web，
            # MaintenancePublicHttpLifecycleAcceptanceTests 用 WebApplicationFactory<GatewayProgram>
            # 真发 HTTP 打网关位点 —— 这条依赖边此前漏了，改网关不触发 FullChain lane（#3330 / PR #3337 实例：
            # 改的是 AuthorizedBusinessProxyEndpoint 的执行序、影响所有代理端点，而那一轮该面零读数）。
            foreach ($flag in @('backend', 'business_gateway', 'openapi_codegen', 'frontend', 'frontend_packages', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
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
            # 平台侧服务（AppHub/Ops/Iam/Notification）走这条兜底分支，
            # 它们的转换器/处理器/种子与业务服务同样是跨服务行为源头。
            if (Test-CrossServiceIntegrationEventPath -Path $path) {
                foreach ($flag in @('redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            if (Test-FullChainSeedPath -Path $path) { Select-Impact -Name 'full_chain' -Reason $reason }
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
            if ($acceptanceScenarioMatrixRuntimeOwningPathSet.Contains($path)) {
                foreach ($flag in @('backend', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
                continue
            }
            if ($acceptanceScenarioMatrixOwningPathSet.Contains($path)) {
                foreach ($flag in @('backend', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
                continue
            }
            if ([string]::Equals($path, 'scripts/run-full-chain-test-lane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/lib/FullChainTestLane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/full-chain-test-lane.json', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/tests/full-chain-test-lane.Tests.ps1', [StringComparison]::Ordinal)) {
                foreach ($flag in @('backend', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
                continue
            }
            if ([string]::Equals($path, 'scripts/verify-erp-sales-order-demand-planning.ps1', [StringComparison]::Ordinal)) {
                foreach ($flag in @('backend', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ([string]::Equals($path, 'scripts/verify-erp-wms-delivery-completion.ps1', [StringComparison]::Ordinal)) {
                foreach ($flag in @('backend', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ([string]::Equals($path, 'scripts/export-gateway-openapi.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/verify-openapi-client-drift.ps1', [StringComparison]::Ordinal)) {
                foreach ($flag in @('openapi_codegen', 'business_gateway', 'frontend', 'frontend_packages')) { Select-Impact -Name $flag -Reason $reason }
            }
            if ([string]::Equals($path, 'scripts/run-postgres-test-lane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/lib/PostgresTestLane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/postgres-test-lane.json', [StringComparison]::Ordinal)) {
                Select-Impact -Name 'postgresql' -Reason $reason
            }
            if ([string]::Equals($path, 'scripts/run-redis-cap-test-lane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/lib/RedisCapTestLane.ps1', [StringComparison]::Ordinal) -or
                [string]::Equals($path, 'scripts/redis-cap-test-lane.json', [StringComparison]::Ordinal)) {
                Select-Impact -Name 'redis_cap' -Reason $reason
            }
            if ($path.Contains('full-chain', [StringComparison]::OrdinalIgnoreCase) -or $path.Contains('fullstack', [StringComparison]::OrdinalIgnoreCase)) {
                foreach ($flag in @('postgresql', 'redis_cap', 'full_chain')) { Select-Impact -Name $flag -Reason $reason }
            }
            continue
        }
        if ($path.StartsWith('infra/', [StringComparison]::Ordinal)) {
            Select-Impact -Name 'infra' -Reason $reason
            if ([string]::Equals($path, 'infra/docker-compose.dev.yml', [StringComparison]::Ordinal)) {
                Select-Impact -Name 'full_chain' -Reason $reason
            }
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
