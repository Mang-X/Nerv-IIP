# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads ADR identities and the navigation index under AdrRoot
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $AdrRoot = (Join-Path $PSScriptRoot '../docs/adr')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/OrdinalString.ps1')

# 只保护可解析身份、索引覆盖与索引的本地链接。正文正确性由评审负责；
# 不读取 Governance 的标题表，也不推断状态、日期或措辞的业务含义。
function Get-MarkdownLines {
    param([string] $Path)

    $text = [regex]::Replace([IO.File]::ReadAllText($Path), '(?s)<!--.*?(?:-->|\z)', '')
    $fence = ''
    foreach ($line in ($text -split '\r?\n')) {
        $match = [regex]::Match($line, '^[ \t]{0,3}(?<fence>`{3,}|~{3,})(?<tail>.*)$')
        if ($match.Success) {
            $token = $match.Groups['fence'].Value
            if ($fence.Length -eq 0) { $fence = $token; continue }
            if ($token[0] -eq $fence[0] -and $token.Length -ge $fence.Length -and
                [string]::IsNullOrWhiteSpace($match.Groups['tail'].Value)) {
                $fence = ''
            }
        }
        if ($fence.Length -eq 0 -and -not $match.Success) { $line }
    }
}

if (-not (Test-Path -LiteralPath $AdrRoot -PathType Container)) {
    Write-Host "[ADR_ROOT] ADR 目录不存在：$AdrRoot"
    exit 1
}
$AdrRoot = [IO.Path]::GetFullPath($AdrRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$adrFiles = @(Get-NervItemsSortedByString -Items @(
        Get-ChildItem -LiteralPath $AdrRoot -Filter '*.md' -File | Where-Object {
            -not [string]::Equals($_.Name, 'README.md', [StringComparison]::Ordinal)
        }
    ) -KeySelector { param($row) [string]$row.Name } -Comparer ([StringComparer]::Ordinal))
$findings = [System.Collections.Generic.List[string]]::new()
$records = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$numbers = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$indexed = [System.Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)

if ($adrFiles.Count -eq 0) { $findings.Add('[ADR_EMPTY] 目录内没有 ADR 记录。') }
foreach ($file in $adrFiles) {
    $name = $file.Name
    $identity = [regex]::Match($name, '^(?<number>[0-9]{4})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$')
    if (-not $identity.Success) {
        $findings.Add("[ADR_FILENAME] ${name}: 文件名必须是 NNNN-kebab-case.md")
        continue
    }
    $number = $identity.Groups['number'].Value
    $records.Add($name, $number)
    if ($numbers.ContainsKey($number)) {
        $findings.Add("[ADR_DUPLICATE_NUMBER] ${name}: 编号 $number 同时属于 $($numbers[$number])")
    }
    else { $numbers.Add($number, $name) }

    $text = @(Get-MarkdownLines -Path $file.FullName) -join "`n"
    $h1 = [regex]::Match($text, '(?m)^[ \t]{0,3}#[ \t]+(?<title>[^\r\n]+)')
    $titleIdentity = [regex]::Match($h1.Groups['title'].Value, '^ADR[ \t]+(?<number>[0-9]{4})(?=[:： \t]|$)')
    if (-not $h1.Success -or -not $titleIdentity.Success) {
        $findings.Add("[ADR_H1] ${name}: 首个 H1 必须包含 ADR 四位编号。")
    }
    elseif (-not [string]::Equals($titleIdentity.Groups['number'].Value, $number, [StringComparison]::Ordinal)) {
        $findings.Add("[ADR_NUMBER_MISMATCH] ${name}: H1 编号与文件名编号 $number 不一致。")
    }
}

$indexPath = Join-Path $AdrRoot 'README.md'
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    $findings.Add('[ADR_INDEX] 缺少 README.md 索引。')
}
else {
    # 索引成员只取表格首列；修订关系栏或正文中的普通交叉引用不能冒充索引项。
    # 标题、行顺序、列名、状态和总数都不是门禁合同。
    $linkPattern = '\[(?<label>[^\]]*)\]\(\s*(?<target><[^>]+>|(?:[^()\s]|\([^()\r\n]*\))+)(?:\s+(?:"[^"]*"|''[^'']*''))?\s*\)'
    foreach ($line in @(Get-MarkdownLines -Path $indexPath)) {
        $entry = [regex]::Match($line, '^[ \t]*\|[ \t]*' + $linkPattern + '[ \t]*\|')
        if ($entry.Success) {
            $target = $entry.Groups['target'].Value.Trim([char[]]'<>')
            $destination = [Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
            $path = ''
            if (-not [string]::IsNullOrEmpty($destination) -and $destination -notmatch '^(?:[A-Za-z][A-Za-z0-9+.-]*:|/)') {
                try { $path = [IO.Path]::GetFullPath((Join-Path $AdrRoot $destination)) }
                catch { $path = '' }
            }
            $name = [IO.Path]::GetFileName($path)
            if ([string]::IsNullOrEmpty($path) -or
                -not [string]::Equals([IO.Path]::GetDirectoryName($path), $AdrRoot, [StringComparison]::Ordinal) -or
                -not $records.ContainsKey($name)) {
                $findings.Add("[ADR_INDEX_TARGET] ${target}: 索引项必须指向同目录的 ADR 文件。")
            }
            else {
                if (-not $indexed.ContainsKey($name)) { $indexed.Add($name, 0) }
                $indexed[$name]++
                $label = $entry.Groups['label'].Value.TrimStart([char[]]'`* ')
                $key = [regex]::Match($label, '^(?:ADR[ \t]+)?(?<number>[0-9]{4})(?=[^0-9]|$)')
                if (-not $key.Success -or -not [string]::Equals($key.Groups['number'].Value, $records[$name], [StringComparison]::Ordinal)) {
                    $findings.Add("[ADR_INDEX_NUMBER_MISMATCH] ${name}: 索引显示编号与目标文件编号不一致。")
                }
            }
        }

        # 仅检查索引中 inline Markdown 链接的本地目标；不访问网络、不锁定标题锚点。
        # 去掉代码示例，避免把反引号中的链接字面量当作导航。
        $visible = [regex]::Replace($line, '(?<!`)(?<ticks>`+)(?!`).*?\k<ticks>(?!`)', '')
        foreach ($link in [regex]::Matches($visible, $linkPattern)) {
            $target = $link.Groups['target'].Value.Trim([char[]]'<>')
            if ($target -match '^(?:[A-Za-z][A-Za-z0-9+.-]*:|/|#)') { continue }
            $destination = [Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
            if ([string]::IsNullOrEmpty($destination)) { continue }
            if (-not (Test-Path -LiteralPath (Join-Path $AdrRoot $destination))) {
                $findings.Add("[ADR_INDEX_LINK] ${target}: README.md 的本地链接目标不存在。")
            }
        }
    }
}
foreach ($file in $adrFiles) {
    if (-not $records.ContainsKey($file.Name)) { continue }
    if (-not $indexed.ContainsKey($file.Name)) {
        $findings.Add("[ADR_INDEX_MISSING] $($file.Name): 未被索引表首列覆盖。")
    }
    elseif ($indexed[$file.Name] -gt 1) {
        $findings.Add("[ADR_INDEX_DUPLICATE] $($file.Name): 索引重复登记。")
    }
}

if ($findings.Count -gt 0) {
    Write-Host 'ADR 结构检查失败：'
    foreach ($finding in $findings) { Write-Host "  $finding" }
    exit 1
}
Write-Host "ADR 结构检查通过（$($adrFiles.Count) 条记录）；不代表决策内容或实现已验证。"
