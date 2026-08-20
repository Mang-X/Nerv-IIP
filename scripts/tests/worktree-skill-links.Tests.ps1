# Script-Governance:
#   Category: check
#   SideEffects:
#     - Builds throwaway worktree fixtures under the operating-system temp directory
#   Writes:
#     - Temporary fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1')
. (Join-Path $repoRoot 'scripts/lib/WorktreeSkills.ps1')

function Get-LinkLayerSnapshot([string] $Root) {
    <#
        每个条目一行：相对路径 + 内容（或链接目标）。用于逐字节比较两次运行的产出。
    #>
    $linkDir = Join-Path $Root '.claude/skills'
    if (-not (Test-Path -LiteralPath $linkDir)) { return @() }
    $prefix = (Resolve-Path -LiteralPath $linkDir).Path
    $lines = foreach ($item in Get-ChildItem -LiteralPath $linkDir -Force -Recurse) {
        $relative = $item.FullName.Substring($prefix.Length)
        if ($item.LinkType) {
            "$relative|LINK:$(@($item.Target)[0])"
        }
        elseif ($item.PSIsContainer) {
            "$relative|DIR"
        }
        else {
            "$relative|$(Get-Content -LiteralPath $item.FullName -Raw)"
        }
    }
    return Get-NervStringsSorted -Values @($lines) -Comparer ([System.StringComparer]::Ordinal)
}

function New-Fixture([string[]] $PayloadNames, [string[]] $StrayFiles = @()) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-skill-links-" + [guid]::NewGuid().ToString('N'))
    $payloadRoot = Join-Path $root '.agents/skills'
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
    foreach ($name in $PayloadNames) {
        $dir = Join-Path $payloadRoot $name
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'SKILL.md') -Value "name: $name" -NoNewline
    }
    foreach ($file in $StrayFiles) {
        Set-Content -LiteralPath (Join-Path $payloadRoot $file) -Value 'stray' -NoNewline
    }
    return $root
}

function Get-LinkNames([string] $Root) {
    $linkDir = Join-Path $Root '.claude/skills'
    if (-not (Test-Path -LiteralPath $linkDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $linkDir -Force | ForEach-Object { $_.Name })
}

function Assert-SameNameSet([string[]] $Actual, [string[]] $Expected, [string] $Message) {
    $missing = @($Expected | Where-Object { $name = $_; -not (@($Actual | Where-Object { [string]::Equals($_, $name, [StringComparison]::Ordinal) }).Count -gt 0) })
    $extra = @($Actual | Where-Object { $name = $_; -not (@($Expected | Where-Object { [string]::Equals($_, $name, [StringComparison]::Ordinal) }).Count -gt 0) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "$Message`n  missing: $($missing -join ', ')`n  unexpected: $($extra -join ', ')"
    }
}

$fixtures = [System.Collections.Generic.List[string]]::new()
try {
    # 完备性契约：链接集合必须等于 payload 目录集合。这条比「建了几个链接」强，
    # 因为它同时挡住漏链（旧实现从另一棵树的 .claude/skills 派生，主树未播种时为空）
    # 与多链（把非技能条目发布给 agent）。
    $root = New-Fixture -PayloadNames @('alpha', 'beta', 'gamma') -StrayFiles @('README.txt')
    $fixtures.Add($root)
    New-NervSkillLinkLayer -RepoRoot $root
    Assert-SameNameSet -Actual (Get-LinkNames -Root $root) -Expected @('alpha', 'beta', 'gamma') `
        -Message 'The link layer must expose exactly the installed payload directories.'

    # 链接必须真的能读到 payload 内容，而不只是同名条目存在。
    foreach ($name in @('alpha', 'beta', 'gamma')) {
        $viaLink = Join-Path (Join-Path $root '.claude/skills') (Join-Path $name 'SKILL.md')
        if (-not (Test-Path -LiteralPath $viaLink)) {
            throw "Skill '$name' must be readable through the link layer."
        }
        $content = Get-Content -LiteralPath $viaLink -Raw
        if (-not [string]::Equals($content, "name: $name", [StringComparison]::Ordinal)) {
            throw "Skill '$name' resolved through the link layer to unexpected content: '$content'."
        }
    }

    # 链接目标必须是相对路径：worktree 会被整棵镜像复制，绝对目标会把副本指回源树。
    foreach ($name in @('alpha', 'beta', 'gamma')) {
        $item = Get-Item -LiteralPath (Join-Path (Join-Path $root '.claude/skills') $name)
        $target = $item.Target
        if ($null -eq $target) { continue }   # 无符号链接能力的平台退化为实体拷贝
        $targetText = @($target)[0]
        if ([System.IO.Path]::IsPathRooted($targetText)) {
            throw "Skill '$name' must be linked through a relative target, got '$targetText'."
        }
    }

    # 幂等：重跑不改变结果，且**有 payload 的**条目若已是实体目录也原样保留
    # （`npx skills add --copy` 会留下实体目录）。fixture 必须同时有 payload，
    # 否则循环根本遍历不到它，这条断言就走不到。
    $adoptedPayload = Join-Path (Join-Path $root '.agents/skills') 'delta'
    New-Item -ItemType Directory -Path $adoptedPayload -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $adoptedPayload 'SKILL.md') -Value 'name: delta' -NoNewline
    $adopted = Join-Path (Join-Path $root '.claude/skills') 'delta'
    New-Item -ItemType Directory -Path $adopted -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $adopted 'SKILL.md') -Value 'name: delta-adopted' -NoNewline

    # 幂等的真判据：快照必须**跨过**这次重建。取在重建之后就只能证明"损伤稳定"，
    # 证不了"没有损伤"——覆盖写、嵌套拷贝、链接目标漂移都会在这里显形。
    $before = @(Get-LinkLayerSnapshot -Root $root)
    New-NervSkillLinkLayer -RepoRoot $root
    $after = @(Get-LinkLayerSnapshot -Root $root)
    $beforeText = ($before -join "`n")
    $afterText = ($after -join "`n")
    if (-not [string]::Equals($beforeText, $afterText, [StringComparison]::Ordinal)) {
        throw "Rebuilding the link layer must be byte-identical.`n--- before ---`n$beforeText`n--- after ---`n$afterText"
    }

    Assert-SameNameSet -Actual (Get-LinkNames -Root $root) -Expected @('alpha', 'beta', 'gamma', 'delta') `
        -Message 'Rebuilding the link layer must leave pre-existing entries in place.'
    if ((Get-Item -LiteralPath $adopted).LinkType) {
        throw 'A pre-existing real directory must not be replaced by a link.'
    }
    $adoptedContent = Get-Content -LiteralPath (Join-Path $adopted 'SKILL.md') -Raw
    if (-not [string]::Equals($adoptedContent, 'name: delta-adopted', [StringComparison]::Ordinal)) {
        throw "A pre-existing entry must keep its own content, got '$adoptedContent'."
    }

    # 空 payload：不得凭空建出条目。
    $emptyRoot = New-Fixture -PayloadNames @()
    $fixtures.Add($emptyRoot)
    New-NervSkillLinkLayer -RepoRoot $emptyRoot
    if (@(Get-LinkNames -Root $emptyRoot).Count -ne 0) {
        throw 'An empty payload must produce no link entries.'
    }
    if (Test-NervSkillsPayloadPresent -RepoRoot $emptyRoot) {
        throw 'Test-NervSkillsPayloadPresent must report false for an empty payload directory.'
    }

    # 缺失 payload 目录：静默返回，不抛。
    $bareRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-skill-links-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $bareRoot -Force | Out-Null
    $fixtures.Add($bareRoot)
    New-NervSkillLinkLayer -RepoRoot $bareRoot
    if (Test-NervSkillsPayloadPresent -RepoRoot $bareRoot) {
        throw 'Test-NervSkillsPayloadPresent must report false when .agents/skills is absent.'
    }

    Write-Host 'Worktree skill link layer contract passed.'
}
finally {
    foreach ($fixture in $fixtures) {
        if (Test-Path -LiteralPath $fixture) {
            Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
