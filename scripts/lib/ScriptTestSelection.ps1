# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads GitHub Actions workflow files and scripts/tests sources
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7
#     - Ruby with the yaml and json standard libraries

Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'OrdinalString.ps1')
. (Join-Path $PSScriptRoot 'CiRequiredSummary.ps1')

<#
#3300 scripts/tests 的选取闭合。

问题不是「某几个文件漏登记」，而是**选取方式本身**：ci.yml 逐条手写 `run:` 点名，名单记录的是
写名单那一刻的世界，后来新增的文件默认零 job 选中，并且没有任何门禁会因此变红。按同一姿势把
遗漏的名字补进 ci.yml 只会把今天的名单补齐，下一个新文件照样掉队（#3003、#3135、#3185 是同形）。

这里换成**补集选取**：

    RunnerSelected = glob(scripts/tests/*.Tests.ps1) - WorkflowSelected - OutOfBand

`WorkflowSelected` 不是手写名单，而是从 `.github/workflows/**` 的 `run:` 体里**推导**出来的；
`OutOfBand` 是唯一需要人工维护的面，且每一条都必须自证（目标文件存在、理由非空、种类在闭集内、
nested 条目的父测试确实在 CI 上被选中且其源码确实引用了该子测试）。

由此得到的失效方向是 fail-closed：

  * 新增一个 `scripts/tests/*.Tests.ps1` ⇒ 它既不在 `WorkflowSelected` 也不在 `OutOfBand`
    ⇒ 自动落进 `RunnerSelected` ⇒ 被 scripts/run-script-contract-tests.ps1 执行。
    「忘了登记」的后果是**被跑**，不是被静默跳过。
  * 想让某个文件不跑，必须写进 `OutOfBand` 并给出理由；写错（文件不存在、理由为空、种类不认识、
    nested 的父测试其实不在 CI 上）⇒ 本库直接 throw ⇒ 契约测试红。登记面自己烂掉也会被抓到。
  * 工作流目录读不到、工作流文件为零、测试文件为零、推导出的 `WorkflowSelected` 为空
    ⇒ throw。扫描面塌掉时不会伪装成「没有遗漏」。

注意 `WorkflowSelected` 的推导口径必须是**多行的**：ci.yml 里既有一行式 `run: ./scripts/tests/x.Tests.ps1`，
也有写在 `run: |` 块里的引用（例如 redis-cap-observation.Tests.ps1）。因此这里走 YAML 解析后按
step 的 `run` 整体文本匹配，而不是按行 grep。scripts/tests/script-test-selection.Tests.ps1 用
合成工作流对这三种形态（一行式、多行块、仅出现在注释里）做了正反对照。
#>

$script:NervScriptTestDirectory = 'scripts/tests'
$script:NervScriptTestSuffix = '.Tests.ps1'
$script:NervScriptTestOutOfBandKinds = @('nested', 'excluded', 'quarantine')
$script:NervScriptTestTrackingPattern = '^#[1-9][0-9]*$'

# 唯一的人工维护面。每条都由 Assert-NervScriptTestOutOfBandRegistry 自证，不是自由文本备注。
# 三种 Kind 互不可替代，字段要求各不相同，因此填错种类一定会撞上另一种的必填/禁填而转红：
#
#   nested     ：该测试由另一个**已在 CI 上被选中**的测试作为子进程或子脚本执行，独立再跑一遍只是重复。
#                必须给 Parent（且 Parent 源码真的引用它）、禁止给 Tracking。
#   excluded   ：该测试在发现式 runner 的执行环境里根本跑不起来（需要真实外部依赖或必填参数）。
#                这是**永久性**的执行面判断，不指向任何待办，因此禁止给 Tracking、禁止给 Parent。
#                必须给 Requirement，且该字符串必须出现在目标文件**自己的** `Requires:` header 里。
#                ⚠️ 这只是把散文压成引用，**不是**一道能挡住滥用的门：真实成本见下方「覆盖边界」。
#                它不代表该测试无需覆盖，只代表覆盖它属于另一条 lane。
#   quarantine ：该测试**应当跑、也确实被选中过**，但当前在本执行面上红，且修它不属于当前这张票。
#                必须给 Tracking（`#<issue>`）、禁止给 Parent。这是一条**有期限的**登记：
#                票销账时删掉这一行，该文件立刻回到发现式 runner 的选中集合，**不需要改 runner 源码**
#                （scripts/tests/script-test-selection.Tests.ps1 对这两点各有一条断言）。
#
# ⚠️ Tracking 的覆盖边界（声明多少就只断言多少）：本库**只校验票号形态与目标文件存在**。
#    「该 issue 是否真的存在」「是否已经关闭」**不在覆盖面内** —— 那需要联网查 GitHub，而
#    script-governance job 既没有 token 也不应为一条注释性字段引入网络依赖与非确定性。
#    因此一条指向已关闭 issue 的 quarantine 不会被本门禁抓到；防住它的是 #3404/#3405 这类票
#    自身的验收条目（「解除本文件的 quarantine 登记」写在票里），不是这里。
#
# ⚠️ `excluded` 的真实成本（#3300 复审实测，逐条读数写在这里，不要靠印象）：
#
#    1. Requirement 是**子串**匹配（`$_.Contains($requirement, Ordinal)`）：`'o'`、`'7'`、`'Power'`
#       都能通过。它约束的是「引用出自目标文件的 Requires 段」，不是「这条依赖真的不满足」。
#    2. **61 个测试里 59 个在自己的 `Requires:` 里声明了 `PowerShell 7`**（另 2 个没有 Requires 段）。
#       ⇒ 对其中任意一个写 `excluded` + `Requirement = 'PowerShell 7'` 都是绿的，实测已确认。
#    3. `Requires:` 段内容**本身没有任何门禁**：往目标文件 header 加一行
#       `#     - a mythical daemon` 再据此 excluded 同样绿，`check-script-governance.ps1` 也是 EXIT=0。
#
#    ⇒ 一条 `excluded` 可以静默摘掉任意测试。本库把它压成了一条**具名、带理由、在 diff 里显眼、
#      且引用可被逐字反驳**的数据表条目 —— 到此为止。判断「该依赖是不是真的不满足」属于人工复审。
#
# 本库声明的覆盖面：种类闭集、字段必填/禁填矩阵、目标存在性、父子引用真实性、票号**形态**、
# Requirement 的**出处**。不声明「豁免理由为真」，也不声明「无票排除不可拼写」。

# ⚠️ workflow-mention 面的覆盖边界（#3300 复审逐格实测，关掉的与放行的都逐字列在这里）。
#
# 【已关】run 体内的 `#` 注释与 pwsh 块注释，四种形态：
#    整行 `#`、**行尾 `#`**、`<# … #>` 单行、`<# … #>` 多行。
#    实现见 Get-NervScriptTestWorkflowSelections：先去块注释、再逐行截掉 `#` 之后的部分。
#    ⚠️ 量词要准：**不是**「run 体内的注释都关掉了」，是「`#` 系注释的上述四种形态」。
#    关它们的理由是**可抵赖性**最高：整行注释在 diff 里是新增一行，行尾注释只是**修改一行**，
#    而 `<# … #>` 在 `shell: pwsh` 的 step 里本就是合法写法 —— 三者都能伪装成无辜注释。
#    过滤对当前仓库**行为中性**：加过滤前后 ALL=61 WF=31 OOB=6 RUN=24，55 行成员清单逐字相同。
#    失败方向安全：截断只会**减少**命中 ⇒ 文件落回 runner ⇒ 被跑。
#
# 【放行，逐条声明】
#    N1 **嵌套**块注释 `<# 外 <# 内 #> 名字 #>`：非贪婪匹配停在第一个 `#>`，`名字 #>` 作为正文残留
#       ⇒ 会被记成选中。实测确认。不关的理由是它要求一个带嵌套计数的扫描器，而正确处理嵌套还得
#       同时处理 here-string 与引号内的 `<#` —— 那是 #3176 / PR #3214 判定永不收敛的那条路
#       （护栏自身 655→1139 行后被裁定移除）。可抵赖性也低：嵌套块注释在 diff 里不像无辜写法。
#    N2 **不可判定的非执行提及**：`if: false` 的 step 点名；在 PR 上从不触发的工作流
#       （如 nightly-business-performance.yml，`on:` 只有 schedule 与 workflow_dispatch）里点名；
#       `: ./x`、引号字符串里的名字、here-doc 体内的名字、`false && ./x` 之类永不执行的语句。
#       要判它们就得同时实现 GitHub 表达式求值、事件触发模型和一个 shell 语义分析器 —— 同上，
#       是永不收敛的那条路。这些写法与已关的注释形态的关键区别是**在 diff 里一眼就是错的**。
#    N3 **自指缴械面**：改 runner 自己的 ci.yml step（加 `if: false`）、在 runner 里插 `exit 0`。
#       任何护栏都能被改护栏本身缴械，追它没有终点。
#    N4 **伪造父测试**：新建一个被 CI 点名的父测试，再在其源码里加一行对目标的引用。
#       要挡住就得证明父测试真的执行了子测试，成本远超收益。
$script:NervScriptTestOutOfBandRegistry = @(
    [pscustomobject]@{
        Name = 'postgres-test-database-consumers.Tests.ps1'
        Kind = 'nested'
        Parent = 'postgres-test-lane.Tests.ps1'
        Tracking = $null
        Reason = 'postgres-test-lane.Tests.ps1:982 直接 `&` 调用它，父测试已在 Script Governance 上被点名执行。'
    },
    [pscustomobject]@{
        Name = 'redis-test-namespace-cleanup.Tests.ps1'
        Kind = 'nested'
        Parent = 'redis-cap-test-lane.Tests.ps1'
        Tracking = $null
        Reason = 'redis-cap-test-lane.Tests.ps1:402 直接 `&` 调用它，父测试已在 Script Governance 上被点名执行。'
    },
    [pscustomobject]@{
        Name = 'script-automation-live-output.Tests.ps1'
        Kind = 'nested'
        Parent = 'script-automation-signal-exit.Tests.ps1'
        Tracking = $null
        Reason = 'script-automation-signal-exit.Tests.ps1:60 以子进程方式跑它并对非零退出 throw（#3168 的 6726b1467）。'
    },
    [pscustomobject]@{
        Name = 'redis-cap-observation-runtime.Tests.ps1'
        Kind = 'excluded'
        Parent = $null
        Tracking = $null
        Requirement = 'dotnet-counters'
        Reason = '它有必填参数 -CountersPath，且自述 Requires 为 Linux、prlimit、timeout、redis-cli 与 dotnet-counters；这些只在 Redis/CAP lane 的服务容器里成立，不属于 Script Governance 的无依赖执行面。'
    },
    [pscustomobject]@{
        Name = 'fullstack-process-runtime.Tests.ps1'
        Kind = 'quarantine'
        Parent = $null
        Tracking = '#3404'
        Reason = 'Linux 上既有缺陷，#3300 的发现式 runner 首次把它接上 CI 时才被看见，非本 PR 引入（该文件与 scripts/lib/FullStackProcessRuntime.ps1 最后改动于 2026-08-22/23，此前零 job 选中）：真实子进程 fixture 上 Get-NervFullStackProcessIdentityState 把本次测试自己启动的 root 判成 Mismatched 而非 Active。macOS 本机绿、Linux CI 红。解除条件：#3404 修复后删掉本条登记。'
    },
    [pscustomobject]@{
        Name = 'fullstack-session-state-v2.Tests.ps1'
        Kind = 'quarantine'
        Parent = $null
        Tracking = '#3405'
        Reason = 'Linux 上既有缺陷，#3300 的发现式 runner 首次把它接上 CI 时才被看见，非本 PR 引入（该文件最后改动于 2026-08-22，此前零 job 选中）：canonical legacy manifest 在 Linux 上没有被分类为 v0。macOS 本机绿、Linux CI 红。解除条件：#3405 修复后删掉本条登记。'
    }
)

function Get-NervScriptTestOutOfBandRegistry {
    <#
        ⚠️ `,` 前缀不是可有可无的：`return @()` 经函数输出会退化成 $null，于是当登记表被清空时
        （#3404/#3405 销账后删到一条不剩就会发生）调用方收到的不是空集合而是 $null，
        `[AllowEmptyCollection()]` 形同虚设，报出来的是 PowerShell 的绑定错误
        「Cannot bind argument to parameter 'Registry' because it is null」——
        那是一条与本库无关的诊断，会让人以为登记机制坏了。用数组包装保住空集合语义。

        ⚠️ 次生影响，写探针时会踩：`,@(...)` 输出的是**一层包装**，所以
        `@(Get-NervScriptTestOutOfBandRegistry).Count` 得到 **1** 而不是条目数。
        直接赋值（`$r = Get-NervScriptTestOutOfBandRegistry`）与管道（`| ForEach-Object`）
        都仍然按条目展开，本库唯一的生产调用点是直接赋值，因此仓库内零影响。
        探针里要数条目就别再套一层 `@()`。
    #>
    [CmdletBinding()]
    param()

    return ,@($script:NervScriptTestOutOfBandRegistry)
}

function Get-NervScriptTestFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $directory = Join-Path $RepositoryRoot $script:NervScriptTestDirectory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Script test directory '$directory' does not exist; the selection plan cannot certify coverage."
    }

    # 先枚举全部 *.ps1 再按 ordinal 后缀过滤，而不是交给 -Filter：provider filter 的大小写语义随平台变化，
    # 而「哪些文件算契约测试」必须在 macOS 与 Linux 上是同一个集合。子目录（fixtures 等）不在此面内，
    # 与 `scripts/tests/*.Tests.ps1` 这一 glob 逐字对应。
    $names = @(Get-ChildItem -LiteralPath $directory -File |
            Where-Object { $_.Name.EndsWith($script:NervScriptTestSuffix, [StringComparison]::Ordinal) } |
            ForEach-Object { $_.Name })
    if ($names.Count -eq 0) {
        throw "Script test directory '$directory' matched no '*$script:NervScriptTestSuffix' file; the enumeration face collapsed."
    }

    return @(Get-NervStringsSorted -Values ([string[]] $names) -Comparer ([StringComparer]::Ordinal))
}

function Test-NervScriptTestNameCharacter {
    <#
        ⚠️ `[char]::IsLetterOrDigit` 对 CJK 返回 True，所以 `x.Tests.ps1的`（名字后**紧贴**汉字、
        中间无空格）会被判成「右侧不是边界」⇒ 不命中 ⇒ 该文件落回发现式 runner ⇒ **被跑**。
        方向是安全的（漏判成「没被点名」而不是「已被点名」）。真实 ci.yml 里的中文注释都在名字
        与汉字之间留了空格，实测仍然命中，因此这条不影响当前选中集合。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [char] $Value)

    return ([char]::IsLetterOrDigit($Value) -or $Value -eq '_' -or $Value -eq '.' -or $Value -eq '-')
}

function Test-NervScriptTestReference {
    <#
        文本里是否**以整名形式**出现该测试文件名。

        朴素的 `$text.Contains($name)` 在这里是 fail-open 的：若 A 的文件名是 B 的子串，
        引用 B 就会把 A 也标成「已被选中」并从发现式 runner 里摘掉 —— 正好是本票要消除的
        那种静默漏跑。因此**左右两侧都要判定**：命中位置的前一个字符与后一个字符都不能是
        文件名字符（字母、数字、`_`、`.`、`-`）。

        ⚠️ 不是只判左侧。这里曾写过「右侧不需要判定，因为名字以 `.Tests.ps1` 结尾，更长的名字
        只可能在左侧多出字符」——**那条命题是假的**，#3300 复审用反例推翻：
        `pnpm-invocation.Tests.ps1` 是 `pnpm-invocation.Tests.ps1-extra.Tests.ps1` 的**前缀**，
        点名后者会连带把前者摘出 runner。准确说法：只判左侧能挡住「短名是长名的后缀或中缀」，
        挡不住「短名是长名的前缀」。

        ⚠️ 这是**前瞻性加固，当前仓库零触发**：61 个文件的 3660 个有序对里子串命中数为 0。
        留它是因为命名碰撞只需有人新增一个文件就会出现，而它的失效方向是静默漏跑。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [string] $Name
    )

    $searchIndex = 0
    while ($searchIndex -le ($Text.Length - $Name.Length)) {
        $matchIndex = $Text.IndexOf($Name, $searchIndex, [StringComparison]::Ordinal)
        if ($matchIndex -lt 0) { return $false }
        $afterIndex = $matchIndex + $Name.Length
        $leftIsBoundary = ($matchIndex -eq 0) -or (-not (Test-NervScriptTestNameCharacter -Value $Text[$matchIndex - 1]))
        $rightIsBoundary = ($afterIndex -ge $Text.Length) -or (-not (Test-NervScriptTestNameCharacter -Value $Text[$afterIndex]))
        if ($leftIsBoundary -and $rightIsBoundary) { return $true }
        $searchIndex = $matchIndex + 1
    }

    return $false
}

function Get-NervScriptTestWorkflowSelections {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $workflowDirectory = Join-Path $RepositoryRoot '.github/workflows'
    if (-not (Test-Path -LiteralPath $workflowDirectory -PathType Container)) {
        throw "Workflow directory '$workflowDirectory' does not exist; script test selection cannot be derived."
    }

    $workflowFiles = @(Get-ChildItem -LiteralPath $workflowDirectory -File | Where-Object {
            $extension = [IO.Path]::GetExtension($_.Name)
            [string]::Equals($extension, '.yml', [StringComparison]::Ordinal) -or [string]::Equals($extension, '.yaml', [StringComparison]::Ordinal)
        })
    $workflowFiles = @(Get-NervItemsSortedByString -Items $workflowFiles -KeySelector { $args[0].Name } -Comparer ([StringComparer]::Ordinal))
    if ($workflowFiles.Count -eq 0) {
        throw "Workflow directory '$workflowDirectory' contains no workflow file; script test selection cannot be derived."
    }

    $names = Get-NervScriptTestFiles -RepositoryRoot $RepositoryRoot
    # Dictionary + ContainsKey 而不是 OrderedDictionary + Contains：后者的 .Contains() 与
    # String.Contains() 同名，ordinal 门禁（scripts/lib/OrdinalComparisonContract.ps1）无法区分，
    # 会把每个 `.Contains('literal')` 调用点都判成漏 [StringComparison] 的字符串比较。
    $selections = [Collections.Generic.Dictionary[string, Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)

    foreach ($workflowFile in $workflowFiles) {
        $workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $workflowFile.FullName -WorkingDirectory $RepositoryRoot
        $jobsProperty = $workflow.PSObject.Properties['jobs']
        if ($null -eq $jobsProperty -or $null -eq $jobsProperty.Value) { continue }

        foreach ($jobProperty in $jobsProperty.Value.PSObject.Properties) {
            $stepsProperty = $jobProperty.Value.PSObject.Properties['steps']
            if ($null -eq $stepsProperty -or $null -eq $stepsProperty.Value) { continue }

            $stepIndex = 0
            foreach ($step in @($stepsProperty.Value)) {
                $stepIndex++
                $runProperty = $step.PSObject.Properties['run']
                if ($null -eq $runProperty -or $null -eq $runProperty.Value) { continue }

                # 只看 run 体里的**非注释行**。
                #
                # G1（#3300 复审实测）：`run: |` 块里加一行 shell 注释 `# ./scripts/tests/x.Tests.ps1`
                # 就能把 x 记成「已被工作流选中」并从发现式 runner 摘掉 —— 一行、且在 diff 里看起来
                # 像一条无辜注释，是所有绕法里最便宜、最可抵赖的一条。ci.yml 今天就有四处这种写法
                # （1431/1902/2006/2069 都在 run 体里用中文注释提到 full-chain-test-lane.Tests.ps1；
                # 该文件同时在 2351 真被跑，所以今天无害 —— 但那是巧合不是设计）。
                #
                # 过滤后与过滤前在当前仓库逐项等价（ALL=61 WF=31 OOB=6 RUN=24），
                # 即这一条只关掉绕法、不改变今天的选中集合。
                $runBody = [regex]::Replace([string] $runProperty.Value, '(?s)<#.*?#>', '')
                $run = @($runBody -split "`n" | ForEach-Object { $_ -replace '(^|\s)#.*$', '' }) -join "`n"
                foreach ($name in $names) {
                    if (-not (Test-NervScriptTestReference -Text $run -Name $name)) { continue }
                    if (-not $selections.ContainsKey($name)) { $selections[$name] = [Collections.Generic.List[string]]::new() }
                    $stepNameProperty = $step.PSObject.Properties['name']
                    $stepName = if ($null -eq $stepNameProperty) { '(unnamed)' } else { [string] $stepNameProperty.Value }
                    $selections[$name].Add("$($workflowFile.Name):$($jobProperty.Name) step #$stepIndex ($stepName)")
                }
            }
        }
    }

    if ($selections.Count -eq 0) {
        throw 'No script test is referenced by any workflow run body; the workflow scan face collapsed and must not be read as "nothing is selected".'
    }

    return $selections
}

function Get-NervScriptTestRegistryField {
    <#
        登记条目的字段读取一律走这里。

        Set-StrictMode 下直接写 `$entry.Tracking` 在缺字段时抛 PropertyNotFound，而登记条目按 Kind
        只填自己需要的字段；把「字段缺失」和「字段为空」都归一成空字符串，判定矩阵才能只讲
        必填/禁填，不必先讲 PowerShell 的属性存在性。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Entry,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Entry.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return '' }
    return [string] $property.Value
}

function Get-NervScriptTestDeclaredRequirements {
    <#
        读目标脚本 Script-Governance header 里 `Requires:` 段下的条目行。

        只认 header 注释块内 `#   Requires:` 之后、缩进更深的 `#     - ` 行；遇到同级或更浅的
        header 键（`#   <Key>:`）即结束。文件没有该段时返回空集合，由调用方 fail closed。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }

    $requirements = [Collections.Generic.List[string]]::new()
    $inRequires = $false
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if (-not $line.StartsWith('#', [StringComparison]::Ordinal)) {
            if ($inRequires) { break }
            continue
        }
        if ($line.StartsWith('#   Requires:', [StringComparison]::Ordinal)) { $inRequires = $true; continue }
        if (-not $inRequires) { continue }
        if ($line.StartsWith('#     - ', [StringComparison]::Ordinal)) {
            $requirements.Add($line.Substring('#     - '.Length).Trim())
            continue
        }
        break
    }

    return @($requirements)
}

function Assert-NervScriptTestOutOfBandRegistry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Registry,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Names,
        [Parameter(Mandatory)] [object] $WorkflowSelections
    )

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $known = [Collections.Generic.HashSet[string]]::new([string[]] $Names, [StringComparer]::Ordinal)

    foreach ($entry in $Registry) {
        $name = Get-NervScriptTestRegistryField -Entry $entry -Name 'Name'
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Out-of-band script test registry has an entry without a name.'
        }
        if (-not $seen.Add($name)) {
            throw "Out-of-band script test registry lists '$name' more than once."
        }
        if (-not $known.Contains($name)) {
            throw "Out-of-band script test registry lists '$name', which is not a file under $script:NervScriptTestDirectory; the registration face has rotted."
        }

        $kind = Get-NervScriptTestRegistryField -Entry $entry -Name 'Kind'
        if (@($script:NervScriptTestOutOfBandKinds | Where-Object { [string]::Equals($_, $kind, [StringComparison]::Ordinal) }).Count -ne 1) {
            throw "Out-of-band script test registry entry '$name' declares unknown kind '$kind'; allowed kinds are $($script:NervScriptTestOutOfBandKinds -join ', ')."
        }

        if ([string]::IsNullOrWhiteSpace((Get-NervScriptTestRegistryField -Entry $entry -Name 'Reason'))) {
            throw "Out-of-band script test registry entry '$name' has no reason; an unexplained exemption is indistinguishable from an oversight."
        }

        if ($WorkflowSelections.ContainsKey($name)) {
            throw "Out-of-band script test registry entry '$name' is also selected by a workflow run body; the registry contradicts the workflow."
        }

        if ([string]::Equals($kind, 'nested', [StringComparison]::Ordinal)) {
            $parent = Get-NervScriptTestRegistryField -Entry $entry -Name 'Parent'
            if ([string]::IsNullOrWhiteSpace($parent)) {
                throw "Out-of-band script test registry entry '$name' is nested but names no parent."
            }
            if (-not $WorkflowSelections.ContainsKey($parent)) {
                throw "Out-of-band script test registry entry '$name' claims parent '$parent', but no workflow run body selects that parent; the nested claim buys nothing."
            }
            $parentPath = Join-Path (Join-Path $RepositoryRoot $script:NervScriptTestDirectory) $parent
            if (-not (Test-Path -LiteralPath $parentPath -PathType Leaf)) {
                throw "Out-of-band script test registry entry '$name' claims parent '$parent', which does not exist."
            }
            $parentSource = [IO.File]::ReadAllText($parentPath)
            $invokingLines = @([IO.File]::ReadAllLines($parentPath) | Where-Object {
                    (-not $_.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) -and (Test-NervScriptTestReference -Text $_ -Name $name)
                })
            if ($parentSource.Length -eq 0 -or $invokingLines.Count -eq 0) {
                throw "Out-of-band script test registry entry '$name' claims parent '$parent', but that parent has no non-comment reference to it."
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace((Get-NervScriptTestRegistryField -Entry $entry -Name 'Parent'))) {
            throw "Out-of-band script test registry entry '$name' has kind '$kind' but still names a parent; only nested entries have one."
        }

        # Tracking 的必填/禁填按 Kind 分叉：把一条 quarantine 改写成 excluded 时若忘了删 Tracking
        # 会转红，把一条 excluded 写成 quarantine 时缺 Tracking 也会转红。
        #
        # ⛔ 这**不等于**「无票静默排除没有可用的拼写」—— 那句话一度写在这里，是假的。
        #    重新写一条干净的 `excluded`（不带 Tracking、Requirement 取目标文件确实声明过的依赖）
        #    照样能把任意测试静默摘掉。成本见 Get-NervScriptTestDeclaredRequirements 上方的说明。
        #    这一条矩阵关掉的只是「改写时忘了删 Tracking」这一支。
        $requirement = Get-NervScriptTestRegistryField -Entry $entry -Name 'Requirement'
        if ([string]::Equals($kind, 'excluded', [StringComparison]::Ordinal)) {
            if ([string]::IsNullOrWhiteSpace($requirement)) {
                throw "Out-of-band script test registry entry '$name' is excluded but names no requirement; 'it does not run here' has to point at something."
            }
            $targetPath = Join-Path (Join-Path $RepositoryRoot $script:NervScriptTestDirectory) $name
            $declaredRequirements = @(Get-NervScriptTestDeclaredRequirements -Path $targetPath)
            if ($declaredRequirements.Count -eq 0) {
                throw "Out-of-band script test registry entry '$name' is excluded, but that file declares no Script-Governance 'Requires:' block to justify it."
            }
            if (@($declaredRequirements | Where-Object { $_.Contains($requirement, [StringComparison]::Ordinal) }).Count -eq 0) {
                throw "Out-of-band script test registry entry '$name' is excluded on requirement '$requirement', which does not appear in that file's own 'Requires:' block ($($declaredRequirements -join ' | '))."
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($requirement)) {
            throw "Out-of-band script test registry entry '$name' has kind '$kind' but names requirement '$requirement'; only excluded entries are justified by an unmet dependency."
        }

        $tracking = Get-NervScriptTestRegistryField -Entry $entry -Name 'Tracking'
        if ([string]::Equals($kind, 'quarantine', [StringComparison]::Ordinal)) {
            if ([string]::IsNullOrWhiteSpace($tracking)) {
                throw "Out-of-band script test registry entry '$name' is quarantined but names no tracking issue; an untracked quarantine is just a silent exclusion."
            }
            if ($tracking -notmatch $script:NervScriptTestTrackingPattern) {
                throw "Out-of-band script test registry entry '$name' declares tracking issue '$tracking', which is not of the form '#<issue number>'."
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($tracking)) {
            throw "Out-of-band script test registry entry '$name' has kind '$kind' but names tracking issue '$tracking'; only quarantine entries are time-boxed against a ticket."
        }
    }
}

function Get-NervScriptTestSelectionPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [AllowEmptyCollection()] [object[]] $Registry
    )

    if (-not $PSBoundParameters.ContainsKey('Registry')) { $Registry = Get-NervScriptTestOutOfBandRegistry }

    $names = Get-NervScriptTestFiles -RepositoryRoot $RepositoryRoot
    $workflowSelections = Get-NervScriptTestWorkflowSelections -RepositoryRoot $RepositoryRoot
    Assert-NervScriptTestOutOfBandRegistry -RepositoryRoot $RepositoryRoot -Registry $Registry -Names $names -WorkflowSelections $workflowSelections

    $outOfBandNames = [Collections.Generic.HashSet[string]]::new([string[]]@($Registry | ForEach-Object { Get-NervScriptTestRegistryField -Entry $_ -Name 'Name' }), [StringComparer]::Ordinal)
    $runnerSelected = @($names | Where-Object { (-not $workflowSelections.ContainsKey($_)) -and (-not $outOfBandNames.Contains($_)) })

    return [pscustomobject]@{
        RepositoryRoot = $RepositoryRoot
        All = @($names)
        WorkflowSelected = @(Get-NervStringsSorted -Values ([string[]] @($workflowSelections.Keys)) -Comparer ([StringComparer]::Ordinal))
        WorkflowSelectionSites = $workflowSelections
        OutOfBand = @($Registry)
        RunnerSelected = @($runnerSelected)
    }
}
