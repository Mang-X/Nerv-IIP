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
# docs/architecture/decision-record-governance.md；本脚本只强制该文档里已经
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

$adrFiles = @(Get-NervItemsSortedByString -Items @(Get-ChildItem -LiteralPath $AdrRoot -Filter '*.md' -File) -KeySelector { param($row) [string]$row.Name } -Comparer ([StringComparer]::Ordinal))
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
