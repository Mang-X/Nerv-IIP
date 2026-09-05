# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads a FullChain lane manifest and VSTest TRX files supplied by the caller
#     - Invokes a caller-supplied member action only after deadline admission succeeds
#   Writes:
#     - Caller-defined outputs through the admitted member action
#   Cleanup:
#     - The caller-supplied member action owns and restores its scoped resources
#   Requires:
#     - PowerShell 7

function Import-NervFullChainTestLaneManifest {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 20
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported FullChain lane manifest schemaVersion '$($manifest.schemaVersion)'." }
    $members = @($manifest.members)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $allowedKinds = [Collections.Generic.HashSet[string]]::new([string[]]@('fullstack', 'script', 'dotnet'), [StringComparer]::Ordinal)
    foreach ($member in $members) {
        $id = [string]$member.id
        if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw "FullChain lane member id '$id' must be unique and canonical." }
        if (-not [string]::Equals([string]$member.tier, 'core', [StringComparison]::Ordinal) -or -not [string]::Equals([string]$member.status, 'active', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must be active/core." }
        $project = [string]$member.project
        if ($project -cnotmatch '^backend/.+\.csproj$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "FullChain lane member '$id' project is missing or outside backend." }
        if ([string]::IsNullOrWhiteSpace([string]$member.filter) -or -not ([string]$member.filter).StartsWith('FullyQualifiedName=', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must declare one exact FullyQualifiedName filter." }
        $memberIdentities = @($member.expectedTestIdentities | ForEach-Object { [string]$_ })
        if ($memberIdentities.Count -ne 1 -or [string]::IsNullOrWhiteSpace($memberIdentities[0]) -or -not $identities.Add($memberIdentities[0])) { throw "FullChain lane member '$id' must freeze exactly one globally unique identity." }
        if (-not [string]::Equals([string]$member.filter, "FullyQualifiedName=$($memberIdentities[0])", [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' filter must equal its frozen identity." }
        $schemas = @($member.diagnosticSchemas | ForEach-Object { [string]$_ })
        $schemaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($schemas.Count -eq 0 -or @($schemas | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9_]{0,62}$' -or -not $schemaSet.Add($_) }).Count -gt 0) { throw "FullChain lane member '$id' must declare unique canonical diagnosticSchemas." }
        $kind = [string]$member.entrypoint.kind
        if (-not $allowedKinds.Contains($kind)) { throw "FullChain lane member '$id' has invalid entrypoint kind '$kind'." }
        if ([string]::Equals($kind, 'fullstack', [StringComparison]::Ordinal) -and [string]$member.entrypoint.scenario -cnotmatch '^man-[0-9]+$') { throw "FullChain lane member '$id' has an invalid fullstack scenario." }
        if ([string]::Equals($kind, 'script', [StringComparison]::Ordinal)) {
            $path = [string]$member.entrypoint.path
            if ($path -cnotmatch '^scripts/.+\.ps1$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path) -PathType Leaf)) { throw "FullChain lane member '$id' script entrypoint is missing." }
        }
        if (-not [bool]$member.dependencies.postgres) { throw "FullChain lane member '$id' must require PostgreSQL." }
    }
    if ($members.Count -eq 0) { throw 'FullChain lane manifest must contain at least one active/core member.' }
    return [pscustomobject]@{ schemaVersion = 1; members = $members }
}

function Test-NervFullChainDeadlineAdmission {
    param(
        [Parameter(Mandatory)] [int64] $GlobalDeadlineSeconds,
        [Parameter(Mandatory)] [int64] $ElapsedSeconds,
        [Parameter(Mandatory)] [int64] $EntrypointTimeoutSeconds,
        [Parameter(Mandatory)] [int64] $CleanupReserveSeconds,
        [Parameter(Mandatory)] [int64] $GuardReserveSeconds
    )

    $remainingSeconds = ([Numerics.BigInteger]$GlobalDeadlineSeconds) - [Numerics.BigInteger]$ElapsedSeconds
    $requiredSeconds = ([Numerics.BigInteger]$EntrypointTimeoutSeconds) +
        [Numerics.BigInteger]$CleanupReserveSeconds +
        [Numerics.BigInteger]$GuardReserveSeconds
    $allowed = $remainingSeconds -ge $requiredSeconds

    return [pscustomobject][ordered]@{
        Allowed = $allowed
        Reason = if ($allowed) { 'Allowed' } else { 'InsufficientRemainingBudget' }
        RemainingSeconds = $remainingSeconds
        RequiredSeconds = $requiredSeconds
    }
}

function Invoke-NervFullChainMemberAdmission {
    param(
        [Parameter(Mandatory)] [string] $MemberId,
        [Parameter(Mandatory)] [ValidateSet('fullstack', 'script', 'dotnet')] [string] $EntrypointKind,
        [Parameter(Mandatory)] [int64] $GlobalDeadlineSeconds,
        [Parameter(Mandatory)] [int64] $ElapsedSeconds,
        [Parameter(Mandatory)] [int64] $FullstackEntrypointTimeoutSeconds,
        [Parameter(Mandatory)] [int64] $ScriptEntrypointTimeoutSeconds,
        [Parameter(Mandatory)] [int64] $DotnetEntrypointTimeoutSeconds,
        [Parameter(Mandatory)] [int64] $CleanupReserveSeconds,
        [Parameter(Mandatory)] [int64] $GuardReserveSeconds,
        [Parameter(Mandatory)] [object] $MemberSummary,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    $entrypointTimeoutSeconds = if ([string]::Equals($EntrypointKind, 'fullstack', [StringComparison]::Ordinal)) { $FullstackEntrypointTimeoutSeconds }
        elseif ([string]::Equals($EntrypointKind, 'script', [StringComparison]::Ordinal)) { $ScriptEntrypointTimeoutSeconds }
        else { $DotnetEntrypointTimeoutSeconds }
    $admission = Test-NervFullChainDeadlineAdmission `
        -GlobalDeadlineSeconds $GlobalDeadlineSeconds `
        -ElapsedSeconds $ElapsedSeconds `
        -EntrypointTimeoutSeconds $EntrypointTimeoutSeconds `
        -CleanupReserveSeconds $CleanupReserveSeconds `
        -GuardReserveSeconds $GuardReserveSeconds
    $MemberSummary.deadlineAdmission.reason = [string]$admission.Reason
    $MemberSummary.deadlineAdmission.elapsedSeconds = $ElapsedSeconds
    $MemberSummary.deadlineAdmission.remainingSeconds = $admission.RemainingSeconds
    $MemberSummary.deadlineAdmission.requiredSeconds = $admission.RequiredSeconds
    if (-not $admission.Allowed) {
        $MemberSummary.outcome = 'failed'
        $MemberSummary.cleanup = 'passed'
        $MemberSummary.diagnosticEvidence = 'deadline-admission-denied'
        return $admission
    }

    & $Action $MemberId | Out-Null
    return $admission
}

function Get-NervFullChainDiscoveredTestIdentities {
    <#
        把 `dotnet test --list-tests` 的原始输出转成权威用例身份集合。

        #3135：本函数刻意**不**依赖 "The following Tests are available:" / "以下测试可用:" 这行表头 —
        VSTest 的表头随 CLI UI 语言变化（本机中文、CI 英文），拿它当锚点就是「本机绿 CI 红」的经典形状。
        这里改为按身份自身的形状识别：以被测程序集根命名空间开头、且至少还有「类型 + 方法」两段。
        身份形状同时排除了 MSBuild 的构建输出行（`Nerv.IIP.X -> /abs/path/X.dll`）：整行必须**完全**
        匹配「根命名空间 + 至少两段标识符」，路径里的 `/`、空格和 `->` 都落在字符集之外。刻意不额外加
        一条 ` -> ` 的特判——那条分支在这个正则下永远命中不到，是拿不出鉴别力证据的死代码。
        `[Theory]` 会按用例参数逐行列出（`...Method(x: 1)`），截断到第一个 `(` 后去重，得到方法级身份。
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $DiscoveryLines,
        [Parameter(Mandatory)] [string] $RootNamespace
    )

    if ($RootNamespace -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$') {
        throw "FullChain discovery root namespace '$RootNamespace' is not a canonical .NET namespace."
    }
    $identityPattern = '^' + [regex]::Escape($RootNamespace) + '(?:\.[A-Za-z_][A-Za-z0-9_]*){2,}$'
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rawLine in @($DiscoveryLines)) {
        $line = ([string]$rawLine).Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parenthesisIndex = $line.IndexOf('(', [StringComparison]::Ordinal)
        if ($parenthesisIndex -ge 0) { $line = $line.Substring(0, $parenthesisIndex).TrimEnd() }
        if ($line -cnotmatch $identityPattern) { continue }
        [void]$identities.Add($line)
    }
    $ordered = @($identities)
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    return @($ordered)
}

function Get-NervFullChainResidualTestIdentities {
    <#
        #3135：lane 的选取口径从「白名单精确 filter」翻转为「默认全跑 + 无逃生口」。
        residual = 该项目发现到的全部用例 − manifest 冻结的成员身份。新增测试类因此**自动被跑**，
        而不是像 #3135 之前那样掉出地图（被 heavyLanes[full-chain] 排除出四个 fast shard、又不在
        5 个成员名单里 = 谁都不跑）。刻意不提供排除注册表：空注册表拿不出鉴别力证据，而一个逃生口
        必然会被用来重新制造暗测试 —— 白名单选取本身就是本票要治的病因。

        Claimed 必须由调用方传入 manifest 的**全部** members，而不是 -MemberId 选中的子集：
        否则本地只跑一个成员时，另外 4 个成员会落进 residual 被无依赖重跑。这个错不会红，只会让人困惑。
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $DiscoveredIdentities,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $ClaimedIdentities
    )

    $claimed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($identity in @($ClaimedIdentities)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$identity)) { [void]$claimed.Add(([string]$identity).Trim()) }
    }
    $unclaimed = @(@($DiscoveredIdentities) | Where-Object { -not $claimed.Contains([string]$_) } | ForEach-Object { [string]$_ })
    $residual = @($unclaimed)
    [Array]::Sort($residual, [StringComparer]::Ordinal)
    return @($residual)
}

function Assert-NervFullChainDiscoveryClosure {
    <#
        #3135：manifest 冻结的每一条成员身份都必须真的被发现。名单指向一条不存在的用例时，
        成员侧的 discovery 断言会先红；这里再补一层集合口径的护栏，让「名单陈旧」和
        「新增用例未被跑」在同一处被表述为同一个不变量：发现集 = 成员集 ∪ residual 集。
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $DiscoveredIdentities,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $ClaimedIdentities,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [string[]] $ResidualIdentities
    )

    $discovered = [Collections.Generic.HashSet[string]]::new([string[]]@(@($DiscoveredIdentities) | ForEach-Object { [string]$_ }), [StringComparer]::Ordinal)
    $missingClaims = @(@($ClaimedIdentities) | Where-Object { -not $discovered.Contains([string]$_) } | ForEach-Object { [string]$_ })
    if ($missingClaims.Count -gt 0) {
        # 诊断文本也走序数排序：Sort-Object 的键比较是 culture collation，本仓的序数契约门禁
        # （scripts/tests/ordinal-comparison-layers.Tests.ps1）扫到即红。
        [Array]::Sort($missingClaims, [StringComparer]::Ordinal)
        throw "FullChain lane manifest freezes identities that discovery did not report: $($missingClaims -join ', ')."
    }
    $accounted = @(@($ClaimedIdentities) | ForEach-Object { [string]$_ }) + @(@($ResidualIdentities) | ForEach-Object { [string]$_ })
    $accountedSet = [Collections.Generic.HashSet[string]]::new([string[]]$accounted, [StringComparer]::Ordinal)
    $unaccounted = @(@($DiscoveredIdentities) | Where-Object { -not $accountedSet.Contains([string]$_) } | ForEach-Object { [string]$_ })
    if ($unaccounted.Count -gt 0) {
        [Array]::Sort($unaccounted, [StringComparer]::Ordinal)
        throw "FullChain lane discovered tests that no member and no residual run accounts for: $($unaccounted -join ', ')."
    }
}

function Get-NervFullChainTrxResult {
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "FullChain lane member must produce exactly one TRX file; observed $($trxFiles.Count)." }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']"))
    $definitionById = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
        if ($null -ne $method) { $definitionById[[string]$definition.id] = "$($method.className).$($method.name)" }
    }
    $observed = @($results | ForEach-Object { if ($definitionById.ContainsKey([string]$_.testId)) { $definitionById[[string]$_.testId] } else { [string]$_.testName } })
    $expected = @($ExpectedTestIdentities)
    [Array]::Sort($observed, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    $identitiesMatch = [string]::Equals(($observed -join "`n"), ($expected -join "`n"), [StringComparison]::Ordinal)
    $passed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Passed', [StringComparison]::Ordinal) }).Count
    $failed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Failed', [StringComparison]::Ordinal) }).Count
    $skipped = $results.Count - $passed - $failed
    $valid = $identitiesMatch -and $passed -eq $expected.Count -and $failed -eq 0 -and $skipped -eq 0
    $result = [pscustomobject]@{ total = $results.Count; passed = $passed; failed = $failed; skipped = $skipped; identities = @($observed); identitiesMatch = $identitiesMatch; valid = $valid }
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw 'FullChain lane TRX identities do not equal the frozen member identities.' }
    if (-not $AllowInvalid -and -not $valid) { throw "FullChain lane requires $($expected.Count) passed, 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped." }
    return $result
}

function Get-NervFullChainResidualTrxResult {
    <#
        #3135 residual 的 TRX 判定。刻意**不**复用 Get-NervFullChainTrxResult：那个函数服务于 5 个
        冻结成员的 1:1 身份契约（恰好 1 条、逐字相等），把它放松掉会削弱 lane 的核心不变量。

        residual 是方法级集合，而 `[Theory]` 在 TRX 里是逐用例的（`Method(value: 1)`、
        `Method(value: 2)`）。本机变异实测过：直接套用成员那套逐字比较会在新增一个 Theory 时
        误红（"TRX identities do not equal the frozen member identities"）。因此这里把 TRX 身份
        归一到方法级再比集合，同时要求**每一条用例**都通过——Theory 的任一参数化用例失败仍是红。
    #>
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ExpectedTestIdentities
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "FullChain residual coverage must produce exactly one TRX file; observed $($trxFiles.Count)." }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']"))
    $definitionById = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
        if ($null -ne $method) { $definitionById[[string]$definition.id] = "$($method.className).$($method.name)" }
    }
    $methodIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $failedIdentities = [Collections.Generic.List[string]]::new()
    $passed = 0
    $failed = 0
    foreach ($result in $results) {
        $raw = if ($definitionById.ContainsKey([string]$result.testId)) { $definitionById[[string]$result.testId] } else { [string]$result.testName }
        $parenthesisIndex = $raw.IndexOf('(', [StringComparison]::Ordinal)
        $methodIdentity = if ($parenthesisIndex -ge 0) { $raw.Substring(0, $parenthesisIndex).TrimEnd() } else { $raw }
        [void]$methodIdentities.Add($methodIdentity)
        $outcome = [string]$result.outcome
        if ([string]::Equals($outcome, 'Passed', [StringComparison]::Ordinal)) { $passed++ }
        elseif ([string]::Equals($outcome, 'Failed', [StringComparison]::Ordinal)) { $failed++; $failedIdentities.Add($raw) }
    }
    $skipped = $results.Count - $passed - $failed
    $observed = @($methodIdentities)
    $expected = @($ExpectedTestIdentities | ForEach-Object { [string]$_ })
    [Array]::Sort($observed, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    if (-not [string]::Equals(($observed -join "`n"), ($expected -join "`n"), [StringComparison]::Ordinal)) {
        $missing = @($expected | Where-Object { -not $methodIdentities.Contains([string]$_) })
        throw "FullChain residual coverage executed a different identity set than discovery reported. Missing: $($missing -join ', '); observed: $($observed -join ', ')."
    }
    if ($failed -ne 0 -or $skipped -ne 0) {
        throw "FullChain residual coverage requires 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped. Failed identities: $($failedIdentities -join ', ')."
    }
    return [pscustomobject]@{ total = $results.Count; methods = $observed.Count; passed = $passed; failed = $failed; skipped = $skipped; identities = @($observed) }
}

function Test-NervFullChainEvidenceProperty {
    param([AllowNull()] [object] $Object, [Parameter(Mandatory)] [string] $Name)
    if ($null -eq $Object) { return $false }
    return @($Object.PSObject.Properties | Where-Object { [string]::Equals([string]$_.Name, $Name, [StringComparison]::Ordinal) }).Count -eq 1
}

function Assert-NervFullChainZeroReadback {
    param([Parameter(Mandatory)] [object] $Object, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string] $MemberId)
    if (-not (Test-NervFullChainEvidenceProperty -Object $Object -Name $Name)) { throw "FullChain member '$MemberId' cleanup evidence is missing required '$Name' readback." }
    $value = $Object.PSObject.Properties[$Name].Value
    if ($value -isnot [byte] -and $value -isnot [int16] -and $value -isnot [int32] -and $value -isnot [int64]) { throw "FullChain member '$MemberId' cleanup evidence '$Name' readback must be an integer." }
    if ([int64]$value -ne 0) { throw "FullChain member '$MemberId' cleanup evidence '$Name' readback must be zero." }
}

function Assert-NervFullChainMemberEvidence {
    param(
        [Parameter(Mandatory)] [object] $Member,
        [Parameter(Mandatory)] [string] $MemberResultsDirectory,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $schemas = @($Member.diagnosticSchemas | ForEach-Object { [string]$_ })
    if ($schemas.Count -eq 0) { throw "FullChain member '$($Member.id)' has no diagnostic schema contract." }
    $kind = [string]$Member.entrypoint.kind
    if ([string]::Equals($kind, 'fullstack', [StringComparison]::Ordinal)) {
        $manifests = @(Get-ChildItem -LiteralPath (Join-Path $MemberResultsDirectory 'fullstack-state/fullstack-sessions') -Filter '*.json' -File -ErrorAction SilentlyContinue)
        if ($manifests.Count -ne 1) { throw "FullChain member '$($Member.id)' must produce exactly one FullStack cleanup manifest; observed $($manifests.Count)." }
        $evidence = Get-Content -LiteralPath $manifests[0].FullName -Raw | ConvertFrom-Json -Depth 30
        if (-not [string]::Equals([string]$evidence.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -or
            @($evidence.cleanup.remaining).Count -ne 0 -or @($evidence.cleanup.errors).Count -ne 0 -or
            [string]::IsNullOrWhiteSpace([string]$evidence.cleanup.completedAtUtc)) {
            throw "FullChain member '$($Member.id)' FullStack cleanup manifest is incomplete."
        }
        $artifactPath = [IO.Path]::GetFullPath([string]$evidence.artifactPath)
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts/fullstack')) + [IO.Path]::DirectorySeparatorChar
        if (-not $artifactPath.StartsWith($artifactRoot, [StringComparison]::Ordinal) -or -not (Test-Path -LiteralPath $artifactPath -PathType Container)) {
            throw "FullChain member '$($Member.id)' diagnostic artifact directory is missing or outside the governed root."
        }
        $summaryPath = Join-Path $artifactPath 'summary.json'
        if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) { throw "FullChain member '$($Member.id)' FullStack diagnostic summary is missing." }
        foreach ($schema in $schemas) {
            $resourceName = $schema.Replace('_', '-')
            $diagnosticPath = Join-Path $artifactPath "aspire-logs/business-$resourceName.ndjson"
            if (-not (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) { throw "FullChain member '$($Member.id)' diagnostic artifact for schema '$schema' is missing." }
        }
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'fullstack-artifacts-verified'; source = $manifests[0].FullName; diagnosticSchemas = $schemas }
    }

    if ([string]::Equals($kind, 'script', [StringComparison]::Ordinal)) {
        $evidencePath = Join-Path $MemberResultsDirectory 'entrypoint-evidence.json'
        if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "FullChain member '$($Member.id)' entrypoint cleanup evidence is missing." }
        $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -Depth 30
        if ([string]::Equals([string]$Member.id, 'erp-wms-delivery-completion', [StringComparison]::Ordinal)) {
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name 'cleanup')) { throw "FullChain member '$($Member.id)' cleanup evidence is missing required 'cleanup' object." }
            Assert-NervFullChainZeroReadback -Object $evidence.cleanup -Name 'managedProcessRemaining' -MemberId ([string]$Member.id)
            Assert-NervFullChainZeroReadback -Object $evidence.cleanup -Name 'exactDatabaseRemaining' -MemberId ([string]$Member.id)
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence.cleanup -Name 'errors') -or $evidence.cleanup.errors -isnot [array] -or @($evidence.cleanup.errors).Count -ne 0) { throw "FullChain member '$($Member.id)' cleanup evidence must contain an empty errors array." }
        }
        elseif ([string]::Equals([string]$Member.id, 'sales-order-demand-planning', [StringComparison]::Ordinal)) {
            foreach ($objectName in @('managedProcesses', 'disposableDatabase', 'composeServices')) {
                if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name $objectName)) { throw "FullChain member '$($Member.id)' cleanup evidence is missing required '$objectName' object." }
                Assert-NervFullChainZeroReadback -Object $evidence.PSObject.Properties[$objectName].Value -Name 'remaining' -MemberId ([string]$Member.id)
            }
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name 'cleanupFailures') -or $evidence.cleanupFailures -isnot [array] -or @($evidence.cleanupFailures).Count -ne 0) { throw "FullChain member '$($Member.id)' cleanup evidence must contain an empty cleanupFailures array." }
        }
        elseif ([string]::Equals([string]$Member.id, 'ncr-rework-cost-closure', [StringComparison]::Ordinal)) {
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence -Name 'cleanup')) { throw "FullChain member '$($Member.id)' cleanup evidence is missing required 'cleanup' object." }
            foreach ($name in @('managedProcessRemaining', 'exactDatabaseRemaining', 'ownedRedisKeyRemaining', 'ownedComposeServiceRemaining', 'foreignRedisSentinelRemaining')) {
                Assert-NervFullChainZeroReadback -Object $evidence.cleanup -Name $name -MemberId ([string]$Member.id)
            }
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence.cleanup -Name 'foreignRedisSentinelPreserved') -or
                $evidence.cleanup.foreignRedisSentinelPreserved -isnot [bool] -or
                -not [bool]$evidence.cleanup.foreignRedisSentinelPreserved) {
                throw "FullChain member '$($Member.id)' cleanup evidence must prove its foreign Redis sentinel was preserved before exact cleanup."
            }
            if (-not (Test-NervFullChainEvidenceProperty -Object $evidence.cleanup -Name 'errors') -or $evidence.cleanup.errors -isnot [array] -or @($evidence.cleanup.errors).Count -ne 0) { throw "FullChain member '$($Member.id)' cleanup evidence must contain an empty errors array." }
        }
        else { throw "FullChain script member '$($Member.id)' has no cleanup evidence validator." }
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'entrypoint-evidence-verified'; source = $evidencePath; diagnosticSchemas = $schemas }
    }

    if ([string]::Equals($kind, 'dotnet', [StringComparison]::Ordinal) -and -not [bool]$Member.dependencies.externalProcesses) {
        return [pscustomobject]@{ cleanup = 'passed'; diagnosticEvidence = 'trx-and-runner-output-verified'; source = 'runner-owned-dependencies'; diagnosticSchemas = $schemas }
    }
    throw "FullChain member '$($Member.id)' has no governed cleanup and diagnostic evidence contract."
}

function Assert-NervFullChainTestLaneSummary {
    param(
        [Parameter(Mandatory)] [string[]] $SelectedMemberIds,
        [Parameter(Mandatory)] [object[]] $MemberSummaries
    )

    if ($MemberSummaries.Count -ne $SelectedMemberIds.Count) { throw "FullChain lane selected $($SelectedMemberIds.Count) members but summarized $($MemberSummaries.Count)." }
    for ($index = 0; $index -lt $SelectedMemberIds.Count; $index++) {
        $id = $SelectedMemberIds[$index]
        $member = $MemberSummaries[$index]
        if (-not [string]::Equals([string]$member.memberId, $id, [StringComparison]::Ordinal)) { throw "FullChain lane member at index $index must be '$id' but was '$($member.memberId)'." }
        if (-not [string]::Equals([string]$member.outcome, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has outcome '$($member.outcome)'." }
        if (-not [string]::Equals([string]$member.cleanup, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has cleanup '$($member.cleanup)'." }
        if (-not [string]::Equals([string]$member.dependencyEvidence, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has dependency evidence '$($member.dependencyEvidence)'." }
        if (-not ([string]$member.diagnosticEvidence).EndsWith('-verified', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has unverified diagnostic evidence '$($member.diagnosticEvidence)'." }
        if ([int]$member.expected -ne 1 -or [int]$member.discovered -ne 1) { throw "FullChain lane member '$id' expected 1 test but discovered $($member.discovered)." }
        if ([int]$member.passed -ne 1 -or [int]$member.failed -ne 0 -or [int]$member.skipped -ne 0) { throw "FullChain lane member '$id' expected 1 passed, 0 failed and 0 skipped; observed $($member.passed) passed, $($member.failed) failed and $($member.skipped) skipped." }
    }
}
