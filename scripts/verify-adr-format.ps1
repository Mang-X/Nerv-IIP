# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads ADR identities, their index, and current Markdown local link targets
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

[CmdletBinding()]
param(
    [string] $AdrRoot = (Join-Path $PSScriptRoot '../docs/adr'),
    [string] $MarkdownRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/OrdinalString.ps1')

# 默认仓库 suite 同时检查当前 Markdown；显式 AdrRoot 可隔离 ADR 夹具。
# MarkdownRoot 可用于隔离链接夹具，不改变默认 CI 的扫描范围。
if (-not $PSBoundParameters.ContainsKey('AdrRoot') -and [string]::IsNullOrEmpty($MarkdownRoot)) {
    $MarkdownRoot = Join-Path $PSScriptRoot '..'
}

# 只保护可解析身份、索引覆盖与本地链接。正文正确性由评审负责；
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

function Get-RepositoryMarkdownFiles {
    param([string] $Root)

    # 先剪枝再下降，避免本地依赖/构建产物的规模影响轻量检查。
    # -Force 包含 .github/.claude 等当前协作入口；不跟随目录符号链接。
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.PSIsContainer) {
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                if ($item.Name -match '^(?:\.git|node_modules|bin|obj|dist|\.vitepress|\.cache|fixtures)$') { continue }
                $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
                if ([string]::Equals($relative, 'artifacts', [StringComparison]::Ordinal)) { continue }
                $pending.Push($item.FullName)
            }
            elseif ([string]::Equals($item.Extension, '.md', [StringComparison]::OrdinalIgnoreCase)) {
                $item
            }
        }
    }
}

function Test-CurrentMarkdownLinks {
    param(
        [string] $Root,
        [AllowEmptyCollection()] [System.Collections.Generic.List[string]] $Findings
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        $Findings.Add("[DOC_ROOT] Markdown 根目录不存在：$Root")
        return
    }
    $Root = [IO.Path]::GetFullPath($Root)
    foreach ($entry in @('docs/README.md', 'docs/adr/README.md', 'docs/architecture/README.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $entry) -PathType Leaf)) {
            $Findings.Add("[DOC_ENTRY] 当前入口不存在：$entry")
        }
    }
    $checkedDocuments = 0
    foreach ($file in Get-RepositoryMarkdownFiles -Root $Root) {
        $path = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        # 冻结正文保留时点语义；这些目录中的 README/AGENTS 仍是当前协作入口。
        $isEntry = [string]::Equals($file.Name, 'README.md', [StringComparison]::Ordinal) -or
            [string]::Equals($file.Name, 'AGENTS.md', [StringComparison]::Ordinal)
        if (-not $isEntry -and $path -match '^docs/(?:adr|reports|superpowers|status/archive)/') { continue }
        $isSiteDocument = -not $isEntry -and (
            $path.StartsWith('frontend/apps/docs/', [StringComparison]::Ordinal) -or
            $path.StartsWith('frontend/apps/design-system/docs/', [StringComparison]::Ordinal))

        # 使用 PowerShell 自带的 Markdown parser，先渲染再取真实 href/src。
        # 引用式链接、图片、转义和代码示例无需再实现一套 Markdown 正则解析器。
        $html = [string](ConvertFrom-Markdown -InputObject ([IO.File]::ReadAllText($file.FullName))).Html
        $html = [regex]::Replace($html, '(?s)<!--.*?(?:-->|\z)', '')
        $linkPattern = '<(?<element>a|img)\b[^>]*?\b(?:href|src)\s*=\s*(?<quote>["''])(?<target>.*?)\k<quote>'
        foreach ($link in [regex]::Matches($html, $linkPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            $target = [Net.WebUtility]::HtmlDecode($link.Groups['target'].Value)
            # 外链、站点绝对路由和锚点不是仓库相对文件目标；不访问网络或锁定标题文案。
            if ($target -match '^(?:[A-Za-z][A-Za-z0-9+.-]*:|/|#)') { continue }
            $destination = [Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
            if ([string]::IsNullOrEmpty($destination)) { continue }
            # VitePress 的无扩展名 / .html 页面路由由各站点 build 解析。
            # 只豁免 a 的路由目标；显式文件与 img 仍检查，不跳过整篇活文档。
            if ($isSiteDocument -and [string]::Equals($link.Groups['element'].Value, 'a', [StringComparison]::OrdinalIgnoreCase)) {
                $extension = [IO.Path]::GetExtension($destination)
                if ([string]::IsNullOrEmpty($extension) -or
                    [string]::Equals($extension, '.html', [StringComparison]::OrdinalIgnoreCase)) { continue }
            }
            if (-not (Test-Path -LiteralPath (Join-Path $file.DirectoryName $destination))) {
                $Findings.Add("[DOC_LINK] $path -> $target")
            }
        }
        $checkedDocuments++
    }
    Write-Host "当前 Markdown 本地目标已检查（$checkedDocuments 个文件）；不验证外链、标题锚点、站点页面路由或内容语义。"
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
if (-not [string]::IsNullOrEmpty($MarkdownRoot)) {
    Test-CurrentMarkdownLinks -Root $MarkdownRoot -Findings $findings
}

if ($findings.Count -gt 0) {
    Write-Host '文档结构检查失败：'
    foreach ($finding in $findings) { Write-Host "  $finding" }
    exit 1
}
Write-Host "ADR 结构检查通过（$($adrFiles.Count) 条记录）；不代表决策内容或实现已验证。"
