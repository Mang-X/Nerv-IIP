# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads and structurally validates every ADR under docs/adr
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#
# 校验 docs/adr 的结构不变量。判据与理由见
# docs/governance/decisions/records.md；本脚本只强制该文档里已经
# 达成的部分，未达成的欠账在那里登记，不在此处误红。
#
# 编号唯一性与 H1/文件名编号一致性均已校验：0020 撞号（industrial-telemetry 与
# nvui-naming 各占一篇）已由独立 PR 把零入链的 industrial-telemetry 改为 0026，
# 该次改动同时补上这两项校验。

[CmdletBinding()]
param(
    [string] $AdrRoot = (Join-Path $PSScriptRoot '../docs/adr')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/OrdinalString.ps1')

# 同义异名：左侧一律不允许，必须写成右侧的规范值。理由是同一语义三套写法会让
# 门禁无法校验，也让读者以为它们不同。
$synonymSections = [ordered]@{
    '影响'         = '后果'
    '结果'         = '后果'
    '范围外事项'   = '范围之外'
    '范围外'       = '范围之外'
    '备选方案'     = '已考虑的替代方案'
    '替代方案'     = '已考虑的替代方案'
    '已考虑的方案' = '已考虑的替代方案'
}
$allowedStatuses = @('已接受', '已否决', '被取代')

# 生命周期禁用标题：决策记录只写裁决，不写提案期与进度期段落。判据、逐条理由和这张表的
# 权威副本见 docs/governance/decisions/records.md 的「生命周期禁用标题表」；
# 两处必须逐字对齐，由 scripts/tests/verify-adr-format-lifecycle.Tests.ps1 双向锁定。
# 前缀匹配而不是全等：`实施状态` 与 `实施状态声明`、`当前实现事实` 与
# `当前实现事实与目标状态` 是同一档欠账的两种写法，全等表每来一个变体就要补一行。
$lifecycleForbiddenPrefixes = @(
    '实施',
    '迁移计划',
    '验收标准',
    '验收条件',
    '当前实现',
    '下一步',
    '待办',
    '计划',
    '路线图',
    '进度',
    '票映射'
)
# 英文提案期标题按全等匹配：英文词在中文标题里做前缀会误伤（`Complete 提交时序` 之类的
# 领域小节是合法的），而这几条只会以整节标题的形式出现。
$lifecycleForbiddenExactHeadings = @(
    'Proposal',
    'Plan',
    'Migration plan',
    'Acceptance criteria',
    'Next steps',
    'TODO',
    'Roadmap',
    'Implementation status'
)
# 白名单：`## 实施说明` 是本仓库既定约定（27 篇里 15 篇在用），部分取代记录的编号项就住在
# 这里。它必须先于 `实施` 前缀判定，否则前缀禁令会把它连带禁掉。白名单只豁免前缀禁令，
# 不豁免日期戳禁令——`## 实施说明（2026-08-20 修订）` 仍然是按时间叠加的段落。
$lifecycleSectionAllowlist = @('实施说明')
# 日期戳标题：带日期的小节等于把决策记录写成变更日志，读者必须读完全文才知道哪条还有效。
# 只查标题不查正文——正文里的票号与日期是耐久指针，下探正文会误伤（见门禁小节）。
$dateStampedHeadingPattern = '\d{4}-\d{2}-\d{2}'

# `README.md` 是目录导航入口，不是决策记录。只排除这一确切文件名，其他 Markdown 文件仍然
# 必须经过原有的文件名和格式校验。
$adrFiles = @(Get-NervItemsSortedByString -Items @(
        Get-ChildItem -LiteralPath $AdrRoot -Filter '*.md' -File | Where-Object {
            -not [string]::Equals($_.Name, 'README.md', [StringComparison]::Ordinal)
        }
    ) -KeySelector { param($row) [string]$row.Name } -Comparer ([StringComparer]::Ordinal))
if ($adrFiles.Count -eq 0) { Write-Host "No ADR found under $AdrRoot"; exit 1 }

$findings = [System.Collections.Generic.List[string]]::new()

foreach ($file in $adrFiles) {
    $name = $file.Name
    if ($name -notmatch '^\d{4}-[a-z0-9]+(-[a-z0-9]+)*\.md$') {
        $findings.Add("${name}: 文件名必须是 NNNN-kebab-case.md")
    }

    $lines = @(Get-Content -LiteralPath $file.FullName)
    if ($lines.Count -eq 0) { $findings.Add("${name}: 文件为空"); continue }

    # 标题：全角冒号是规范值（25 篇统一后的口径）
    $titleMatch = [regex]::Match($lines[0], '^# ADR (\d{4})：\S')
    if (-not $titleMatch.Success) {
        $findings.Add("${name}: 首行必须是 '# ADR NNNN：<标题>'（全角冒号），实际为 '$($lines[0])'")
    }
    elseif ($name.Length -ge 4) {
        # H1 编号必须与文件名编号一致，否则改号时只改一处会留下矛盾记录
        $filePrefix = $name.Substring(0, 4)
        if (-not [string]::Equals($titleMatch.Groups[1].Value, $filePrefix, [StringComparison]::Ordinal)) {
            $findings.Add("${name}: H1 编号 $($titleMatch.Groups[1].Value) 与文件名编号 $filePrefix 不一致")
        }
    }

    $text = $lines -join "`n"

    $statusMatch = [regex]::Match($text, '(?m)^- 状态：(.+)$')
    if (-not $statusMatch.Success) {
        $findings.Add("${name}: 缺少 '- 状态：' 行")
    }
    else {
        $statusValue = $statusMatch.Groups[1].Value.Trim()
        $statusHead = ($statusValue -split '—', 2)[0].Trim()
        $statusAllowed = @($allowedStatuses | Where-Object { [string]::Equals($_, $statusHead, [StringComparison]::Ordinal) }).Count -gt 0
        if (-not $statusAllowed) {
            $findings.Add("${name}: 状态 '$statusValue' 不在允许集合 [$($allowedStatuses -join ' / ')] 内")
        }
    }

    if (-not [regex]::IsMatch($text, '(?m)^- 日期：\d{4}-\d{2}-\d{2}\s*$')) {
        $findings.Add("${name}: 缺少 '- 日期：YYYY-MM-DD' 行")
    }

    $sections = @([regex]::Matches($text, '(?m)^## (.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() })

    $hasBackground = @($sections | Where-Object { [string]::Equals($_, '背景', [StringComparison]::Ordinal) }).Count -gt 0
    if (-not $hasBackground) { $findings.Add("${name}: 缺少 '## 背景'") }
    # 备选方案必写：决策记录不写它打败了谁，会招来反复重议。原文确实没保留权衡时，
    # 小节内写明「本记录未保留当时的备选权衡」即可，但小节本身不能缺。
    $hasAlternatives = @($sections | Where-Object {
            [string]::Equals($_, '已考虑的替代方案', [StringComparison]::Ordinal)
        }).Count -gt 0
    if (-not $hasAlternatives) { $findings.Add("${name}: 缺少 '## 已考虑的替代方案'") }
    $hasDecision = @($sections | Where-Object {
            [string]::Equals($_, '决策', [StringComparison]::Ordinal) -or
            $_.StartsWith('决策 ', [StringComparison]::Ordinal)
        }).Count -gt 0
    if (-not $hasDecision) { $findings.Add("${name}: 缺少 '## 决策'（允许 '## 决策 N：...' 形式）") }

    foreach ($section in $sections) {
        if ($synonymSections.Contains($section)) {
            $findings.Add("${name}: 小节 '## $section' 是同义异名，必须写成 '## $($synonymSections[$section])'")
        }
    }

    # 生命周期禁令查 `##` 及以下的全部标题：欠账复发时未必落在顶级小节上。
    $headings = @([regex]::Matches($text, '(?m)^(#{2,})[ \t]+(.+?)[ \t]*$') | ForEach-Object {
            [pscustomobject]@{ Marker = $_.Groups[1].Value; Title = $_.Groups[2].Value }
        })
    foreach ($heading in $headings) {
        $title = $heading.Title
        $marker = $heading.Marker
        $allowlisted = @($lifecycleSectionAllowlist | Where-Object {
                [string]::Equals($_, $title, [StringComparison]::Ordinal)
            }).Count -gt 0
        if (-not $allowlisted) {
            $matchedPrefix = @($lifecycleForbiddenPrefixes | Where-Object {
                    $title.StartsWith($_, [StringComparison]::Ordinal)
                })
            if ($matchedPrefix.Count -gt 0) {
                $findings.Add("${name}: 标题 '$marker $title' 是提案期/进度期段落（禁用前缀 '$($matchedPrefix[0])'），已实施的决策记录不得出现；见 docs/governance/decisions/records.md 的生命周期禁用标题表")
            }
            else {
                $matchedExact = @($lifecycleForbiddenExactHeadings | Where-Object {
                        [string]::Equals($_, $title, [StringComparison]::OrdinalIgnoreCase)
                    })
                if ($matchedExact.Count -gt 0) {
                    $findings.Add("${name}: 标题 '$marker $title' 是提案期段落（禁用标题 '$($matchedExact[0])'），已实施的决策记录不得出现；见 docs/governance/decisions/records.md 的生命周期禁用标题表")
                }
            }
        }

        if ([regex]::IsMatch($title, $dateStampedHeadingPattern)) {
            $findings.Add("${name}: 标题 '$marker $title' 带日期戳，决策变更必须新开记录而不是按时间叠加段落；见 docs/governance/decisions/records.md")
        }
    }
}

# 编号唯一性：撞号会让「ADR NNNN」这种文字引用无法解析到唯一记录。
$numberOwners = [ordered]@{}
foreach ($file in $adrFiles) {
    if ($file.Name.Length -lt 4) { continue }
    $number = $file.Name.Substring(0, 4)
    if (-not $numberOwners.Contains($number)) {
        $numberOwners[$number] = [System.Collections.Generic.List[string]]::new()
    }
    $numberOwners[$number].Add($file.Name)
}
foreach ($number in $numberOwners.Keys) {
    $owners = @($numberOwners[$number])
    if ($owners.Count -gt 1) {
        $findings.Add("编号 $number 被 $($owners.Count) 篇占用，必须唯一：$($owners -join '、')")
    }
}

if ($findings.Count -gt 0) {
    Write-Host 'ADR format governance failed:'
    foreach ($finding in $findings) { Write-Host "  $finding" }
    exit 1
}

Write-Host "ADR format check passed ($($adrFiles.Count) records)."
