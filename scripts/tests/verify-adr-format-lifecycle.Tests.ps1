# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the documentation gate against temporary fixtures and the repository
#   Writes:
#     - Fixtures under an owned operating-system temporary directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#   Requires:
#     - PowerShell 7

# 保留现有 CI 调用路径；不读取脚本 AST 或 Governance 的自然语言表格。
# 通用结构正反例不随文档目录、标题或每条链接新增独立 fixture。
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$gatePath = Join-Path $repoRoot 'scripts/verify-adr-format.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-adr-structure-$([Guid]::NewGuid().ToString('N'))"
$checked = 0

function Write-Fixture {
    param([string] $Root, [string] $Name, [string] $Content)
    $path = Join-Path $Root $Name
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
}

function Write-MarkdownEntries {
    param([string] $Root)
    foreach ($entry in @('docs/README.md', 'docs/adr/README.md', 'docs/architecture/README.md')) {
        Write-Fixture $Root $entry '# 任意导航标题'
    }
    Write-Fixture $Root 'docs/README.md' '[记录](adr/README.md) [架构](architecture/README.md)'
}

function New-FixtureRoot {
    param([string] $Name)
    $root = Join-Path $temporaryRoot $Name
    [IO.Directory]::CreateDirectory($root) | Out-Null
    # 编号、文件名和索引显示键分别给定；变异只改其中一个输入轴。
    Write-Fixture $root '0001-one.md' "# ADR 0001：第一条`n`n正文无需固定章节、状态或日期。"
    Write-Fixture $root '0002-two.md' "# ADR 0002: 第二条`n`n## Plan`n`n## 影响`n`n## 2026-09-11 修订`n"
    Write-Fixture $root '0003-three.md' @'
```markdown
# ADR 9999：代码示例不是记录身份
```
# ADR 0003：第三条

## 自由的小节名
'@
    Write-Fixture $root 'guide (draft).txt' '导航目标。'
    Write-Fixture $root 'README.md' @'
# 可自由改写的索引标题

| 记录 | 说明 |
| --- | --- |
| [0003 任意显示标题](./0003-three.md#任意锚点) | 其它列不限 |
| [ADR 0001 任意显示标题](0001-one.md) | 关系：[ADR 0002](0002-two.md) |
| [0002 任意显示标题](0002-two.md) | 普通说明 |

[本地参考](guide%20(draft).txt#片段)
[外部参考](https://example.invalid/not-checked)
`[示例](missing-inline.md)`

<!-- | [9998 注释不是索引](9998-comment.md) | -->
```markdown
| [9999 示例不是索引](9999-sample.md) |
[不是导航](missing-fenced.md)
```
'@
    return $root
}

function Assert-Gate {
    param(
        [string] $Name,
        [string] $Root,
        [int] $ExpectedExit,
        [string[]] $ExpectedFragments = @(),
        [string] $MarkdownRoot
    )
    $arguments = @('-NoProfile', '-File', $gatePath, '-AdrRoot', $Root)
    if (-not [string]::IsNullOrEmpty($MarkdownRoot)) { $arguments += @('-MarkdownRoot', $MarkdownRoot) }
    $output = & pwsh @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = @($output) -join "`n"
    if ($exitCode -ne $ExpectedExit) {
        throw "${Name}: 预期 exit $ExpectedExit，实际 $exitCode。`n$text"
    }
    foreach ($fragment in $ExpectedFragments) {
        if (-not $text.Contains($fragment, [StringComparison]::Ordinal)) {
            throw "${Name}: 缺少预期结构错误 '$fragment'。`n$text"
        }
    }
    $script:checked++
    Write-Host "[PASS] $Name"
}

try {
    $baseline = New-FixtureRoot 'baseline'
    Assert-Gate '正文、标题和索引顺序可调整；示例不作为导航' $baseline 0

    $identity = New-FixtureRoot 'identity'
    Write-Fixture $identity '0001-one.md' '# ADR 9999：只改变 H1 编号'
    Write-Fixture $identity '0002-two.md' '缺少记录身份。'
    Write-Fixture $identity '0003-duplicate.md' '# ADR 0003：撞号记录'
    Write-Fixture $identity 'not-an-adr.md' '# ADR 0004：错误文件名仍须进入检查'
    Assert-Gate '身份错误不能被其它结构错误掩盖' $identity 1 @(
        '[ADR_NUMBER_MISMATCH] 0001-one.md',
        '[ADR_H1] 0002-two.md',
        '[ADR_DUPLICATE_NUMBER]',
        '[ADR_FILENAME] not-an-adr.md'
    )

    $index = New-FixtureRoot 'index'
    Write-Fixture $index 'README.md' @'
# 索引负例

| 记录 | 修订关系 |
| --- | --- |
| [0002 第二条](0002-two.md) | 普通引用不能补齐首列：[ADR 0001](0001-one.md) |
| [0002 重复别名](./0002-two.md#其它标题) | |
| [9999 只改显示编号](0003-three.md) | |
| [0004 不存在的文件](0004-missing.md) | |
| [0005 远程不是本地记录](https://example.invalid/0005.md) | |

[当前导航断链](missing-guide.md)
'@
    Assert-Gate '索引覆盖、去重、独立显示键和本地目标' $index 1 @(
        '[ADR_INDEX_MISSING] 0001-one.md',
        '[ADR_INDEX_DUPLICATE] 0002-two.md',
        '[ADR_INDEX_NUMBER_MISMATCH] 0003-three.md',
        '[ADR_INDEX_TARGET] 0004-missing.md',
        '[ADR_INDEX_TARGET] https://example.invalid/0005.md',
        '[ADR_INDEX_LINK] missing-guide.md'
    )

    $missingIndex = New-FixtureRoot 'missing-index'
    Remove-Item -LiteralPath (Join-Path $missingIndex 'README.md')
    Assert-Gate '缺失索引不能静默跳过' $missingIndex 1 @('[ADR_INDEX]')

    $empty = Join-Path $temporaryRoot 'empty'
    [IO.Directory]::CreateDirectory($empty) | Out-Null
    Write-Fixture $empty 'README.md' '# 只有导航不能证明有 ADR'
    Assert-Gate '零记录不能成为绿色证据' $empty 1 @('[ADR_EMPTY]')

    $markdown = Join-Path $temporaryRoot 'markdown'
    Write-MarkdownEntries $markdown
    Write-Fixture $markdown 'notes (一).md' '# 任意正文'
    Write-Fixture $markdown 'picture.svg' '<svg />'
    Write-Fixture $markdown 'README.md' @'
# 任意首页

[引用式][guide]

[guide]: <notes%20(一).md> "说明"

![图片](picture.svg)
[目录](docs/)
[外链](https://example.invalid/not-checked)
[站点路由](/site-route)
`[代码示例](missing-inline.md)`
<!-- <a href="missing-comment.md">注释</a> -->
```markdown
[示例](missing-fenced.md)
```
'@
    Write-Fixture $markdown 'docs/reports/audit.md' '[冻结历史](retired.md)'
    Write-Fixture $markdown 'docs/superpowers/plans/history.md' '[冻结计划](retired.md)'
    Write-Fixture $markdown 'docs/status/archive/history.md' '[冻结快照](retired.md)'
    Write-Fixture $markdown 'tests/fixtures/input.md' '[机器夹具](fixture-only.md)'
    Write-Fixture $markdown 'node_modules/dependency/README.md' '[第三方文档](dependency-only.md)'
    Write-Fixture $markdown 'frontend/apps/docs/guide.md' '[站点语义](extensionless-route)'
    Write-Fixture $markdown 'frontend/apps/design-system/docs/component.md' '[站点语义](another-extensionless-route)'
    Write-Fixture $markdown '.claude/README.md' '[当前入口](../docs/README.md)'
    Assert-Gate '解析真实导航而非代码示例；冻结正文与站点消费者边界' $baseline 0 -MarkdownRoot $markdown

    Write-Fixture $markdown 'README.md' "[引用式][lost]`n`n[lost]: reference-lost.md`n`n![图](picture-lost.svg)"
    Write-Fixture $markdown 'docs/superpowers/AGENTS.md' '[当前指令](current-guide-lost.md)'
    Write-Fixture $markdown '.claude/README.md' '[当前入口](hidden-guide-lost.md)'
    Remove-Item -LiteralPath (Join-Path $markdown 'docs/architecture/README.md')
    Assert-Gate '引用式链接、图片、活跃指令、隐藏目录和入口缺失均失败关闭' $baseline 1 @(
        '[DOC_LINK] README.md -> reference-lost.md',
        '[DOC_LINK] README.md -> picture-lost.svg',
        '[DOC_LINK] docs/superpowers/AGENTS.md -> current-guide-lost.md',
        '[DOC_LINK] .claude/README.md -> hidden-guide-lost.md',
        '[DOC_ENTRY] 当前入口不存在：docs/architecture/README.md'
    ) -MarkdownRoot $markdown

    # 站点消费者拥有页面路由，不等于站点正文中的普通文件和图片均可免检。
    # 共用一份通用夹具；错误文件目标与合法路由共存，避免以误报冒充漏检修复。
    $siteMarkdown = Join-Path $temporaryRoot 'site-markdown'
    Write-MarkdownEntries $siteMarkdown
    foreach ($sitePath in @('frontend/apps/docs', 'frontend/apps/design-system/docs')) {
        Write-Fixture $siteMarkdown "$sitePath/target.md" '# 文件目标'
        Write-Fixture $siteMarkdown "$sitePath/asset.svg" '<svg />'
        Write-Fixture $siteMarkdown "$sitePath/page.md" @'
[源文件](target.md#任意标题)
![图片](asset.svg)
[页面路由](generated-route)
[HTML 页面路由](generated-route.html)
`[代码示例](missing-example.md)`
'@
    }
    Assert-Gate '站点文件目标有效；页面路由仍由站点构建负责' $baseline 0 -MarkdownRoot $siteMarkdown

    foreach ($sitePath in @('frontend/apps/docs', 'frontend/apps/design-system/docs')) {
        Write-Fixture $siteMarkdown "$sitePath/page.md" @'
[源文件][missing]

[missing]: missing-source.md#section

![图片](missing-image.svg)
![无扩展名图片](missing-image)
[页面路由](generated-route)
[HTML 页面路由](generated-route.html)
'@
    }
    Assert-Gate '站点正文不能隐藏显式文件、引用式链接或图片断链' $baseline 1 @(
        '[DOC_LINK] frontend/apps/docs/page.md -> missing-source.md#section',
        '[DOC_LINK] frontend/apps/docs/page.md -> missing-image.svg',
        '[DOC_LINK] frontend/apps/docs/page.md -> missing-image',
        '[DOC_LINK] frontend/apps/design-system/docs/page.md -> missing-source.md#section',
        '[DOC_LINK] frontend/apps/design-system/docs/page.md -> missing-image.svg',
        '[DOC_LINK] frontend/apps/design-system/docs/page.md -> missing-image'
    ) -MarkdownRoot $siteMarkdown

    foreach ($sitePath in @('frontend/apps/docs', 'frontend/apps/design-system/docs')) {
        Write-Fixture $siteMarkdown "$sitePath/page.md" '[源文件](target.md)'
    }
    Write-Fixture $siteMarkdown 'frontend/apps/docs/README.md' '[协作文件](missing-entry-target)'
    Write-Fixture $siteMarkdown 'frontend/apps/design-system/docs/AGENTS.md' '[协作文件](missing-entry-target)'
    Assert-Gate '站点 README 与 AGENTS 不借用页面路由豁免' $baseline 1 @(
        '[DOC_LINK] frontend/apps/docs/README.md -> missing-entry-target',
        '[DOC_LINK] frontend/apps/design-system/docs/AGENTS.md -> missing-entry-target'
    ) -MarkdownRoot $siteMarkdown

    $navigation = Join-Path $temporaryRoot 'navigation'
    Write-MarkdownEntries $navigation
    Write-Fixture $navigation 'docs/README.md' @'
# 标题与链接标签不受约束

[记录目录](./adr/)
[架构][architecture]

[architecture]: ./architecture/../architecture/README.md#free-heading
'@
    Assert-Gate '目录别名、规范化路径和引用式链接均可作为当前导航' $baseline 0 -MarkdownRoot $navigation
    Write-Fixture $navigation 'docs/README.md' @'
# 文件仍在，但没有当前导航

`[记录](adr/README.md)`
![不是导航](adr/README.md)
<a data-href="architecture/README.md">不是 href</a>
<!-- [架构](architecture/README.md) -->
'@
    Assert-Gate '代码、图片和 data 属性不能冒充两个当前入口的导航' $baseline 1 @(
        '[DOC_ENTRY_LINK] docs/README.md -> docs/adr/README.md',
        '[DOC_ENTRY_LINK] docs/README.md -> docs/architecture/README.md'
    ) -MarkdownRoot $navigation
    Write-Fixture $navigation 'docs/README.md' '[记录](adr/README.md)'
    Assert-Gate '一个当前入口不能代替另一个入口' $baseline 1 @(
        '[DOC_ENTRY_LINK] docs/README.md -> docs/architecture/README.md'
    ) -MarkdownRoot $navigation

    Write-MarkdownEntries $navigation
    Write-Fixture $navigation 'target.md' '# 当前文件'
    Write-Fixture $navigation 'image.svg' '<svg />'
    Write-Fixture $navigation 'README.md' @'
<a data-href="missing-metadata.md" title="href='missing-title.md' >" href="target.md">实际导航</a>
<a href=target.md>无引号导航</a>
<img data-src="missing-metadata.svg" title="src='missing-title.svg'" src="image.svg">
'@
    Assert-Gate 'HTML 只读取真实目标属性，不把 data 或 title 当作导航' $baseline 0 -MarkdownRoot $navigation
    Write-Fixture $navigation 'README.md' @'
<a data-href="target.md" href="actual-missing.md">不能以元数据掩盖断链</a>
<img data-src="image.svg" src=actual-missing.svg>
'@
    Assert-Gate '真实 href 与无引号 src 断链不能被已有 data 目标掩盖' $baseline 1 @(
        '[DOC_LINK] README.md -> actual-missing.md',
        '[DOC_LINK] README.md -> actual-missing.svg'
    ) -MarkdownRoot $navigation

    Assert-Gate '当前仓库 ADR、索引与活文档本地目标' (Join-Path $repoRoot 'docs/adr') 0 -MarkdownRoot $repoRoot
    Write-Host "文档结构回归通过（$checked 个批次）；不证明生产或业务链路已验证。"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
