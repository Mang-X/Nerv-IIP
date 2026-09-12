# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the ADR gate against temporary fixtures and the repository ADR directory
#   Writes:
#     - Fixtures under an owned operating-system temporary directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#   Requires:
#     - PowerShell 7

# 保留现有 CI 调用路径；不再读取脚本 AST 或 Governance 的自然语言表格。
# 每个红批检查具体失败原因；绿批证明修改正文和导航措辞不改变结构结论。
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$gatePath = Join-Path $repoRoot 'scripts/verify-adr-format.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-adr-structure-$([Guid]::NewGuid().ToString('N'))"
$checked = 0

function Write-Fixture {
    param([string] $Root, [string] $Name, [string] $Content)
    [IO.File]::WriteAllText((Join-Path $Root $Name), $Content, [Text.UTF8Encoding]::new($false))
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
        [string[]] $ExpectedFragments = @()
    )
    $output = & pwsh -NoProfile -File $gatePath -AdrRoot $Root 2>&1
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

    Assert-Gate '当前仓库 ADR 与索引' (Join-Path $repoRoot 'docs/adr') 0
    Write-Host "ADR 结构回归通过（$checked 个批次）；不证明生产或业务链路已验证。"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
