# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs scripts/verify-adr-format.ps1 against synthetic ADR fixtures and against the real docs/adr tree
#   Writes:
#     - Fixture ADR files under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#   Requires:
#     - PowerShell 7

# #1887：`verify-adr-format.ps1` 的生命周期禁令面。两件事各自被守住：
#
# 1. 双向对齐：docs/governance/decisions/records.md 的「生命周期禁用标题表」与脚本
#    里的三个列表逐字相等。文档多一行门禁不查、门禁多一条文档没写，都在这里转红——否则
#    「文档强度高于实现强度」会以两个方向复发。
# 2. 鉴别力：逐条禁用标题各插一次必红，`## 实施说明` 白名单不红，日期戳标题必红，现存的合法
#    标题（`## 当前流程指引`、`## Complete 提交时序`、`## 复评触发条件`）一条都不许被连带禁掉。
#    该禁令加进门禁那天对 27 篇 ADR 是零命中，所以「跑一遍 docs/adr 是绿的」什么都不证明；
#    鉴别力只能由下面这份变异矩阵给出。
#
# 门禁在子进程里跑：它以 exit 1 表达失败，同进程调用会把测试进程一起带走。

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$gatePath = Join-Path $repoRoot 'scripts/verify-adr-format.ps1'
$governanceDocPath = Join-Path $repoRoot 'docs/governance/decisions/records.md'
$realAdrRoot = Join-Path $repoRoot 'docs/adr'

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-GateListLiterals {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Ast,
        [Parameter(Mandatory)] [string] $VariableName
    )

    $assignment = $Ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]::Equals([string] $node.Left.VariablePath.UserPath, $VariableName, [StringComparison]::Ordinal)
        }, $true)
    Assert-Contract ($null -ne $assignment) "verify-adr-format.ps1 必须把生命周期禁令保留在名为 `$$VariableName 的列表里。"

    return @(
        $assignment.Right.FindAll({
                param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
            }, $true) | ForEach-Object { [string] $_.Value }
    )
}

# 序数比较：`-ne` 与 `-cne` 都是 culture-aware（#1507 裁决），只差一个可忽略字符的两份清单会被
# 判为相等，这条对齐契约就会漏掉那次放宽。
function Assert-SetEquals {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Actual,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )

    $actualJoined = (@($Actual) -join '|')
    $expectedJoined = (@($Expected) -join '|')
    Assert-Contract ([string]::Equals($actualJoined, $expectedJoined, [StringComparison]::Ordinal)) `
        "${Message} 脚本：[$actualJoined]；文档：[$expectedJoined]。"
}

$gateAst = [System.Management.Automation.Language.Parser]::ParseFile($gatePath, [ref]$null, [ref]$null)
$scriptPrefixes = Get-GateListLiterals -Ast $gateAst -VariableName 'lifecycleForbiddenPrefixes'
$scriptExact = Get-GateListLiterals -Ast $gateAst -VariableName 'lifecycleForbiddenExactHeadings'
$scriptAllowlist = Get-GateListLiterals -Ast $gateAst -VariableName 'lifecycleSectionAllowlist'

Assert-Contract ($scriptPrefixes.Count -gt 0) '禁用前缀列表为空时，整条生命周期禁令等于没上。'
Assert-Contract ($scriptExact.Count -gt 0) '英文全等禁令列表为空时，提案期标题会从英文那侧漏回来。'
Assert-Contract ($scriptAllowlist.Count -gt 0) '白名单为空时，`## 实施说明` 会被 `实施` 前缀连带禁掉。'

# --- 契约 1：文档表与脚本列表双向对齐 ---------------------------------------------------------

$governanceDoc = [IO.File]::ReadAllText($governanceDocPath)
$tableMatch = [regex]::Match($governanceDoc, '(?s)### 生命周期禁用标题表.*?\n\n(?<table>\| 禁用标题 \| 匹配方式 \|.*?)\n\n')
Assert-Contract ($tableMatch.Success) 'records.md 必须保留「### 生命周期禁用标题表」及其表格。'

$documentedPrefixes = [System.Collections.Generic.List[string]]::new()
$documentedExact = [System.Collections.Generic.List[string]]::new()
foreach ($row in ($tableMatch.Groups['table'].Value -split "`n")) {
    $rowMatch = [regex]::Match($row, '^\|\s*`(?<title>[^`]+)`\s*\|\s*(?<mode>前缀|全等)\s*\|\s*$')
    if (-not $rowMatch.Success) { continue }
    if ([string]::Equals($rowMatch.Groups['mode'].Value, '前缀', [StringComparison]::Ordinal)) {
        $documentedPrefixes.Add($rowMatch.Groups['title'].Value)
    }
    else {
        $documentedExact.Add($rowMatch.Groups['title'].Value)
    }
}

$allowlistMatch = [regex]::Match($governanceDoc, '\*\*白名单：`## (?<title>[^`]+)`\*\*')
Assert-Contract ($allowlistMatch.Success) 'records.md 必须成文写出白名单标题。'

Assert-SetEquals -Actual $scriptPrefixes -Expected @($documentedPrefixes) `
    -Message '生命周期禁用前缀在脚本与治理文档之间漂移了，两处必须同改。'
Assert-SetEquals -Actual $scriptExact -Expected @($documentedExact) `
    -Message '英文全等禁令在脚本与治理文档之间漂移了，两处必须同改。'
Assert-SetEquals -Actual $scriptAllowlist -Expected @($allowlistMatch.Groups['title'].Value) `
    -Message '白名单在脚本与治理文档之间漂移了，两处必须同改。'

# --- 契约 2：行为变异矩阵 ---------------------------------------------------------------------

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-adr-lifecycle-$([Guid]::NewGuid().ToString('N'))"

$baselineRecord = @'
# ADR 0001：夹具决策记录

- 状态：已接受
- 日期：2026-08-22

## 背景

夹具用的最小合规记录。

## 决策

保持最小。

## 已考虑的替代方案

原文只记录了该选择，未保留落选理由。

## 后果

无。
'@

function Invoke-Gate {
    param([Parameter(Mandatory)] [string] $AdrRoot)

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $gatePath -AdrRoot $AdrRoot 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = (@($output) -join "`n")
    }
}

# 变异用例按「一个变异一篇记录」造，再整批喂给门禁：门禁的每条 finding 都以文件名开头，
# 所以批量运行不丢归因，而逐条起 pwsh 会把这个 step 从秒级拖到分钟级。
# 红批与绿批分开：绿批断言 exit 0，任何一条被连带禁掉都会让整批转红。
$caseIndex = 0
function New-MutationCase {
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $Title,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ExpectedFindingFragments
    )

    $script:caseIndex++
    $number = '{0:d4}' -f $script:caseIndex
    $body = ($baselineRecord -replace '# ADR 0001：', "# ADR $number：") + "`n`n$Title`n`n插入的段落。`n"
    return [pscustomobject]@{
        FileName = "$number-$Label.md"
        Title    = $Title
        Content  = $body
        Expected = @($ExpectedFindingFragments)
    }
}

function New-CaseRoot {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Cases
    )

    $caseRoot = Join-Path $temporaryRoot $Name
    [IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    foreach ($case in $Cases) {
        [IO.File]::WriteAllText((Join-Path $caseRoot $case.FileName), $case.Content, [Text.UTF8Encoding]::new($false))
    }
    return $caseRoot
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    # 阴性对照：基线夹具必须绿。它绿不了，下面每一条「插入后转红」都无法归因到插入的那一行。
    $baselineRoot = Join-Path $temporaryRoot 'baseline'
    [IO.Directory]::CreateDirectory($baselineRoot) | Out-Null
    [IO.File]::WriteAllText((Join-Path $baselineRoot '0001-baseline.md'), $baselineRecord, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $baselineRoot 'README.md'), "# ADR 导航`n`n本页仅用于导航。", [Text.UTF8Encoding]::new($false))
    $baseline = Invoke-Gate -AdrRoot $baselineRoot
    Assert-Contract ($baseline.ExitCode -eq 0) "基线夹具必须通过门禁，实际 exit $($baseline.ExitCode)：`n$($baseline.Output)"

    # 回归：docs/adr/README.md 是导航入口而不是决策记录，只能排除这一确切文件名；其它
    # Markdown 仍必须经过文件名和结构校验，不能把扫描范围收窄成只认 NNNN-kebab-case。
    $malformedRoot = Join-Path $temporaryRoot 'malformed'
    [IO.Directory]::CreateDirectory($malformedRoot) | Out-Null
    [IO.File]::WriteAllText((Join-Path $malformedRoot 'README.md'), "# ADR 导航`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $malformedRoot 'not-an-adr.md'), $baselineRecord, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $malformedRoot '0002-malformed.md'), "# ADR 0002：不完整记录`n", [Text.UTF8Encoding]::new($false))
    $malformed = Invoke-Gate -AdrRoot $malformedRoot
    Assert-Contract ($malformed.ExitCode -eq 1) "非 README 的 Markdown 和不完整 ADR 必须继续失败，实际 exit $($malformed.ExitCode)：`n$($malformed.Output)"
    Assert-Contract ($malformed.Output.Contains('not-an-adr.md', [StringComparison]::Ordinal)) "非 README 的 Markdown 文件名必须继续进入 ADR 门禁：`n$($malformed.Output)"
    Assert-Contract ($malformed.Output.Contains('0002-malformed.md', [StringComparison]::Ordinal)) "不完整 ADR 必须继续进入 ADR 门禁：`n$($malformed.Output)"

    $redCases = [System.Collections.Generic.List[object]]::new()

    # 变异 1：逐条禁用前缀各插一次标题，必须红，且失败信息必须点名命中的那一条。
    # 每条前缀补一个真实变体（`实施状态声明`、`当前实现事实与目标状态` 这类同档不同写法），
    # 证明前缀匹配确实覆盖变体，而不是只挡住表里那个字面量。
    foreach ($prefix in $scriptPrefixes) {
        foreach ($suffix in @('', '声明', '与票映射')) {
            $title = "## $prefix$suffix"
            $redCases.Add((New-MutationCase -Label 'prefix' -Title $title -ExpectedFindingFragments @("'$title'", "禁用前缀 '$prefix'")))
        }
    }

    # 变异 2：英文全等禁令逐条各插一次，必须红；大小写不同也必须红（`## plan` 与 `## Plan`
    # 是同一件事，比较必须走 OrdinalIgnoreCase 那条路）。
    foreach ($heading in $scriptExact) {
        foreach ($spelling in @($heading, $heading.ToLowerInvariant(), $heading.ToUpperInvariant())) {
            $title = "## $spelling"
            $redCases.Add((New-MutationCase -Label 'exact' -Title $title -ExpectedFindingFragments @("'$title'", "禁用标题 '$heading'")))
        }
    }

    # 变异 3：日期戳标题必须红，`##` 与 `###` 两级都要红；白名单只豁免前缀禁令，
    # 带日期的 `## 实施说明（…）` 仍然是按时间叠加的段落。
    foreach ($title in @('## 2026-08-20 修订', '## 收口更正（2026-07-07）', '### 2026-05-17 增量', '## 实施说明（2026-08-20 修订）')) {
        $redCases.Add((New-MutationCase -Label 'date-stamped' -Title $title -ExpectedFindingFragments @("'$title'", '带日期戳')))
    }

    # 变异 4：禁令覆盖 `###` 及更深层级——欠账复发未必落在顶级小节上。
    foreach ($marker in @('###', '####')) {
        $title = "$marker 实施状态"
        $redCases.Add((New-MutationCase -Label 'deep-heading' -Title $title -ExpectedFindingFragments @("'$title'", "禁用前缀 '实施'")))
    }

    $redRoot = New-CaseRoot -Name 'red' -Cases @($redCases)
    $redResult = Invoke-Gate -AdrRoot $redRoot
    Assert-Contract ($redResult.ExitCode -eq 1) "全部禁用标题变异必须让门禁转红，实际 exit $($redResult.ExitCode)：`n$($redResult.Output)"
    foreach ($case in $redCases) {
        $findingLines = @(($redResult.Output -split "`n") | Where-Object { $_.Contains($case.FileName, [StringComparison]::Ordinal) })
        Assert-Contract ($findingLines.Count -ge 1) "变异 '$($case.Title)' 必须产生指名到文件 $($case.FileName) 的 finding：`n$($redResult.Output)"
        $line = ($findingLines -join "`n")
        foreach ($fragment in $case.Expected) {
            Assert-Contract ($line.Contains($fragment, [StringComparison]::Ordinal)) `
                "变异 '$($case.Title)' 的 finding 必须包含 '$fragment'，实际：`n$line"
        }
    }

    $greenCases = [System.Collections.Generic.List[object]]::new()

    # 阴性对照 1：白名单必须活着。`## 实施说明` 命中 `实施` 前缀，只有白名单先判定它才不红——
    # 删掉白名单这一条会让本用例转红，白名单因此不是死代码。
    foreach ($allowed in $scriptAllowlist) {
        $greenCases.Add((New-MutationCase -Label 'allowlist' -Title "## $allowed" -ExpectedFindingFragments @()))
    }

    # 阴性对照 2：英文全等项确实是「全等」而不是前缀——把它做成中文标题的前缀不许红，否则
    # `## Complete 提交时序` 这类合法领域小节会被连带禁掉。
    foreach ($heading in $scriptExact) {
        $greenCases.Add((New-MutationCase -Label 'exact-not-prefix' -Title "## $heading 提交时序" -ExpectedFindingFragments @()))
    }

    # 阴性对照 3：仓库现存的合法标题一条都不许被连带禁掉。这些标题都取自真实 ADR
    # （0017 的 `## 当前流程指引`、0023 的 `## Complete 提交时序`、0022 的 `## 复评触发条件`），
    # 逐节读全文后已判定为规范性裁决，不是进度叙述。
    $legitimateTitles = @(
        '## 实施说明',
        '## 当前流程指引',
        '## Complete 提交时序',
        '## Upload session 状态机',
        '## 复评触发条件',
        '## 重新评估的触发条件',
        '## 继承 ADR 0023 的约束',
        '## 范围与非范围',
        '## 参考数据落点',
        '## 决策 5：迁移路线与验收口径',
        '### 第 4 层：周边文档同步',
        '### 3.2 动效：值共享、引用名分场景、motion-v 统一封装'
    )
    foreach ($title in $legitimateTitles) {
        $greenCases.Add((New-MutationCase -Label 'legitimate' -Title $title -ExpectedFindingFragments @()))
    }

    $greenRoot = New-CaseRoot -Name 'green' -Cases @($greenCases)
    $greenResult = Invoke-Gate -AdrRoot $greenRoot
    Assert-Contract ($greenResult.ExitCode -eq 0) `
        "白名单与合法标题一条都不得被生命周期禁令连带禁掉，门禁却转红了：`n$($greenResult.Output)"

    # --- 契约 3：真实 ADR 树 -----------------------------------------------------------------
    # 零命中是这条禁令上线的前提（#1887 的实测口径），也是它此后的不变量：谁往 docs/adr 里
    # 写进度段落，这一步就红。
    $realTree = Invoke-Gate -AdrRoot $realAdrRoot
    Assert-Contract ($realTree.ExitCode -eq 0) "docs/adr 必须通过 ADR 格式门禁：`n$($realTree.Output)"
}
finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "ADR lifecycle section governance contract passed ($caseIndex mutation fixtures)."