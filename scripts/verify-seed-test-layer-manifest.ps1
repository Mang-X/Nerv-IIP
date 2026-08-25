#!/usr/bin/env pwsh
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads the seed-test layer manifest, WMS test source, and backend shard manifest
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'seed-test-layer-manifest.json'),
    [string] $ShardManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json'),
    [string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

$errors = New-Object System.Collections.Generic.List[string]

function Add-ManifestError([string] $Message) {
    [void] $errors.Add($Message)
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        Add-ManifestError "$Context 缺少非空字段 '$Name'。"
        return ''
    }

    return [string] $property.Value
}

function Get-MethodBody {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $MethodName,
        [Parameter(Mandatory)] [string] $Context
    )

    $methodPattern = '(?m)^    public\s+(?:async\s+)?Task(?:<[^>]+>)?\s+' +
        [regex]::Escape($MethodName) + '\s*\('
    $methodMatch = [regex]::Match($Source, $methodPattern)
    if (-not $methodMatch.Success) {
        Add-ManifestError "$Context 未找到测试方法 '$MethodName'。"
        return ''
    }

    $tail = $Source.Substring($methodMatch.Index)
    $nextMethod = [regex]::Match(
        $tail.Substring($methodMatch.Length),
        '(?m)^    (?:\[Fact|\[Theory|private\s+static|public\s+async|public\s+void)')
    if ($nextMethod.Success) {
        return $tail.Substring(0, $methodMatch.Length + $nextMethod.Index)
    }

    return $tail
}

function Assert-Trait {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $MethodName,
        [Parameter(Mandatory)] [string] $TraitName,
        [Parameter(Mandatory)] [string] $TraitValue,
        [Parameter(Mandatory)] [string] $Context
    )

    $methodPattern = '(?m)^    public\s+(?:async\s+)?Task(?:<[^>]+>)?\s+' +
        [regex]::Escape($MethodName) + '\s*\('
    $methodMatch = [regex]::Match($Source, $methodPattern)
    if (-not $methodMatch.Success) { return }

    $prefix = $Source.Substring([Math]::Max(0, $methodMatch.Index - 1000), [Math]::Min(1000, $methodMatch.Index))
    $traitPattern = '\[Trait\("' + [regex]::Escape($TraitName) + '",\s*"' +
        [regex]::Escape($TraitValue) + '"\)\]'
    if (-not [regex]::IsMatch($prefix, $traitPattern)) {
        Add-ManifestError "$Context 的方法 '$MethodName' 未声明 trait '$TraitName=$TraitValue'。"
    }
}

$expectedContractsByIdentity = [System.Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
$expectedContractsByIdentity['wms-world-history-warehouse-ops-pr-boundary'] = @(
    'idempotency',
    'reference-integrity',
    'terminal-state',
    'number-segments',
    'current-queue-shape')
$expectedContractsByIdentity['wms-world-history-warehouse-ops-pr-fail-closed'] = @('fail-closed')

$contractMarkers = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$contractMarkers['idempotency'] = 'AssertWarehouseOpsSeedIsIdempotentAsync'
$contractMarkers['reference-integrity'] = 'AssertReferencesRemainCompleteAsync'
$contractMarkers['terminal-state'] = 'AssertNumberSegmentsAndStatusDistributionAsync'
$contractMarkers['number-segments'] = 'AssertNumberSegmentsAndStatusDistributionAsync'
$contractMarkers['current-queue-shape'] = 'AssertCurrentQueueShapeAsync'
$contractMarkers['fail-closed'] = 'Assert.ThrowsAsync<WorldHistoryWarehouseOpsConsistencyException>'

function Assert-ExactContractSet {
    param(
        [Parameter(Mandatory)] [object] $Entry,
        [Parameter(Mandatory)] [string] $Context
    )

    $expected = $null
    if (-not $expectedContractsByIdentity.TryGetValue([string] $Entry.id, [ref] $expected)) {
        Add-ManifestError "$Context identity 未登记合同闭合规则。"
        return
    }

    $actualSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($contract in @($Entry.contracts)) {
        if (-not $actualSet.Add([string] $contract)) {
            Add-ManifestError "$Context contracts 不得重复登记 '$contract'。"
        }
    }
    $expectedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($contract in $expected) { [void] $expectedSet.Add($contract) }

    if ($actualSet.Count -ne $expectedSet.Count) {
        Add-ManifestError "$Context contracts 集合与 WMS 清单②预期不一致。"
        return
    }
    foreach ($contract in $expectedSet) {
        if (-not $actualSet.Contains($contract)) {
            Add-ManifestError "$Context 缺少合同 '$contract'。"
        }
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Add-ManifestError "找不到 seed-test manifest '$ManifestPath'。"
}
if (-not (Test-Path -LiteralPath $ShardManifestPath -PathType Leaf)) {
    Add-ManifestError "找不到 backend shard manifest '$ShardManifestPath'。"
}

if ($errors.Count -eq 0) {
    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 30
        $shards = Get-Content -LiteralPath $ShardManifestPath -Raw | ConvertFrom-Json -Depth 30
    }
    catch {
        Add-ManifestError "manifest JSON 解析失败：$($_.Exception.Message)"
    }
}

if ($errors.Count -eq 0) {
    if ([int] $manifest.schemaVersion -ne 1) { Add-ManifestError 'seed-test manifest schemaVersion 必须为 1。' }
    if (-not [string]::Equals([string] $manifest.issue, '#1244', [StringComparison]::Ordinal)) { Add-ManifestError 'seed-test manifest issue 必须为 #1244。' }
    if (-not [string]::Equals([string] $manifest.linear, 'NERV-677', [StringComparison]::Ordinal)) { Add-ManifestError 'seed-test manifest linear 必须为 NERV-677。' }

    $entries = @($manifest.entries)
    if ($entries.Count -ne 2) { Add-ManifestError "WMS 第一批必须登记 2 个独立 evidence identity，实际 $($entries.Count) 个。" }
    $entryIds = @($entries | ForEach-Object { [string] $_.id })
    $entryIdSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entryId in $entryIds) {
        if (-not $entryIdSet.Add($entryId)) { Add-ManifestError "seed-test manifest identity id '$entryId' 必须唯一。" }
    }

    foreach ($entry in $entries) {
        $context = "identity '$($entry.id)'"
        $sourcePath = Get-RequiredString -Object $entry -Name 'sourcePath' -Context $context
        $projectPath = Get-RequiredString -Object $entry -Name 'projectPath' -Context $context
        $testIdentity = Get-RequiredString -Object $entry -Name 'testIdentity' -Context $context
        $testPattern = Get-RequiredString -Object $entry -Name 'testPattern' -Context $context
        $provider = Get-RequiredString -Object $entry -Name 'provider' -Context $context
        $requiredLane = Get-RequiredString -Object $entry -Name 'requiredLane' -Context $context
        $executionShard = Get-RequiredString -Object $entry -Name 'executionShard' -Context $context

        $sourceFullPath = Join-Path $repoRoot $sourcePath
        if (-not (Test-Path -LiteralPath $sourceFullPath -PathType Leaf)) {
            Add-ManifestError "$context sourcePath '$sourcePath' 不存在。"
            continue
        }

        $source = Get-Content -LiteralPath $sourceFullPath -Raw
        $methodName = ($testIdentity -split '\.')[-1]
        $methodBody = Get-MethodBody -Source $source -MethodName $methodName -Context $context

        Assert-ExactContractSet -Entry $entry -Context $context

        try {
            $displayIdentity = if ([int] $entry.expectedRuntimeTestCount -gt 1) {
                "$testIdentity(year: 2026, month: 1, day: 5)"
            }
            else {
                $testIdentity
            }
            if (-not [regex]::IsMatch($displayIdentity, $testPattern)) {
                Add-ManifestError "$context testPattern 不匹配 testIdentity。"
            }
        }
        catch {
            Add-ManifestError "$context testPattern 不是有效正则：$($_.Exception.Message)"
        }

        foreach ($traitName in @($entry.traits.PSObject.Properties.Name)) {
            Assert-Trait -Source $source -MethodName $methodName -TraitName $traitName `
                -TraitValue ([string] $entry.traits.$traitName) -Context $context
        }
        foreach ($layer in @($entry.layers)) {
            if ($null -eq $entry.traits.PSObject.Properties[[string] $layer]) {
                Add-ManifestError "$context layer '$layer' 缺少同名 trait 映射。"
            }
        }

        if ([string]::Equals($provider, 'EF Core InMemory', [StringComparison]::Ordinal) -and
            -not $source.Contains('.UseInMemoryDatabase(', [StringComparison]::Ordinal)) {
            Add-ManifestError "$context provider 声明 EF Core InMemory，但 source 未使用 UseInMemoryDatabase。"
        }

        $shard = @($shards.fastShards | Where-Object {
                [string]::Equals([string] $_.id, $executionShard, [StringComparison]::Ordinal)
            })
        if ($shard.Count -ne 1) {
            Add-ManifestError "$context executionShard '$executionShard' 未在 backend-test-shards.json 中唯一登记。"
        }
        else {
            $projectMatches = @($shard[0].projects | Where-Object {
                    [string]::Equals([string] $_, $projectPath, [StringComparison]::Ordinal)
                })
            if ($projectMatches.Count -ne 1) {
                Add-ManifestError "$context projectPath '$projectPath' 不属于 executionShard '$executionShard'。"
            }
            if (-not ([string] $shard[0].evidenceLane).StartsWith($requiredLane, [StringComparison]::Ordinal)) {
                Add-ManifestError "$context requiredLane '$requiredLane' 与实际 evidenceLane '$($shard[0].evidenceLane)' 不一致。"
            }
        }

        $expectedCount = [int] $entry.expectedRuntimeTestCount
        $boundaries = @($entry.asOfDateBoundaries | ForEach-Object { [string] $_ })
        if ($expectedCount -ne $boundaries.Count) {
            Add-ManifestError "$context expectedRuntimeTestCount 必须等于 asOfDateBoundaries 数量。"
        }

        foreach ($date in $boundaries) {
            try {
                $dateParts = ([DateOnly]::Parse($date)).ToString('yyyy,M,d')
                $datePattern = '(?:\{\s*|new DateOnly\(\s*)' +
                    $dateParts.Replace(',', '\s*,\s*') + '\s*(?:\}|\))'
                if (-not [regex]::IsMatch($source, $datePattern)) {
                    Add-ManifestError "$context 的日期边界 '$date' 未在 source 中登记。"
                }
            }
            catch {
                Add-ManifestError "$context 的日期边界 '$date' 不是有效日期。"
            }
        }

        $hasSetupCountPerDate = $null -ne $entry.PSObject.Properties['setupCountPerDate']
        if ($hasSetupCountPerDate -and $expectedCount -gt 1) {
            if ([int] $entry.setupCountPerDate -ne 1) { Add-ManifestError "$context setupCountPerDate 必须为 1。" }
            if (([regex]::Matches($methodBody, 'CreateDbContext\(')).Count -ne 1) {
                Add-ManifestError "$context 每个日期必须只有一次 CreateDbContext setup。"
            }
            if (([regex]::Matches($methodBody, 'SeedDocumentChainAsync\(')).Count -ne 1) {
                Add-ManifestError "$context 每个日期必须只有一次上游 SeedDocumentChainAsync setup。"
            }
            foreach ($marker in @(
                'AssertCurrentQueueShapeAsync',
                'AssertNumberSegmentsAndStatusDistributionAsync',
                'AssertReferencesRemainCompleteAsync',
                'AssertWarehouseOpsSeedIsIdempotentAsync')) {
                if (-not $methodBody.Contains($marker, [StringComparison]::Ordinal)) {
                    Add-ManifestError "$context 缺少合同 helper '$marker'。"
                }
            }
        }

        $hasSetupCount = $null -ne $entry.PSObject.Properties['setupCount']
        if ($hasSetupCount) {
            if ([int] $entry.setupCount -ne 1) {
                Add-ManifestError "$context setupCount 必须为 1。"
            }
            if (([regex]::Matches($methodBody, 'CreateDbContext\(')).Count -ne 1) {
                Add-ManifestError "$context 必须只有一次 CreateDbContext setup。"
            }
            if (([regex]::Matches($methodBody, 'SeedDocumentChainAsync\(')).Count -ne 1) {
                Add-ManifestError "$context 必须只有一次上游 SeedDocumentChainAsync setup。"
            }
        }

        foreach ($contract in @($entry.contracts)) {
            $marker = $null
            if (-not $contractMarkers.TryGetValue([string] $contract, [ref] $marker)) {
                Add-ManifestError "$context 登记了未知合同 '$contract'。"
                continue
            }
            if (-not $methodBody.Contains($marker, [StringComparison]::Ordinal)) {
                Add-ManifestError "$context 合同 '$contract' 缺少可定位断言 '$marker'。"
            }
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Seed test layer manifest verified: $(@($manifest.entries).Count) identities."
exit 0
