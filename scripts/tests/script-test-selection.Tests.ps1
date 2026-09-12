# Script-Governance:
#   Category: check
#   SideEffects:
#     - Builds synthetic workflow and scripts/tests fixtures under the operating-system temp directory
#   Writes:
#     - An owned temporary fixture root only
#   Cleanup:
#     - Removes the owned temporary fixture root in finally
#   Requires:
#     - PowerShell 7
#     - Ruby with the yaml and json standard libraries

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptTestSelection.ps1')

function Assert-Selection([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-SelectionFailure([scriptblock] $Action) {
    try { & $Action | Out-Null }
    catch { return [string] $_.Exception.Message }
    return $null
}

function New-SelectionFixtureRoot {
    <#
        一个最小的「仓库形状」：.github/workflows 一个工作流 + scripts/tests 若干测试文件。
        所有 fail-closed 用例都在这上面做单点变异，而不是改真实仓库。
    #>
    param(
        [Parameter(Mandatory)] [string] $WorkflowContent,
        [Parameter(Mandatory)] [hashtable] $TestFiles
    )

    $root = Join-Path ([IO.Path]::GetTempPath()) "nerv-script-test-selection-$([guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory((Join-Path $root '.github/workflows')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'scripts/tests')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $root '.github/workflows/ci.yml'), $WorkflowContent, [Text.UTF8Encoding]::new($false))
    foreach ($key in $TestFiles.Keys) {
        [IO.File]::WriteAllText((Join-Path (Join-Path $root 'scripts/tests') ([string] $key)), [string] $TestFiles[$key], [Text.UTF8Encoding]::new($false))
    }

    return $root
}

# ─────────────────────────────────────────────────────────────────────────────
# 1. 扫描口径：一行式 / 多行 run 块 / 仅注释 三种形态的正反对照
#
# 这是本文件最承重的一组断言。#3300 的筛查面之所以会数错，正是因为按行 grep `run:.*Tests.ps1`
# 数不到写在 `run: |` 块里的引用（ci.yml:942 的 redis-cap-observation.Tests.ps1 就是这种）。
# 阳性对照：多行 `run: |` 块里的引用必须命中。少了它，一个只认一行式的实现会「看起来」正确，
# 并把多行选中的文件误判成零选中。
#
# 阴性对照分两格，鉴别力**天差地别**，不要混为一谈：
#
#   * run 体内的 **shell 注释**（`# ./scripts/tests/x.Tests.ps1`）—— **这一格才有鉴别力**。
#     它是 #3300 复审实测出的最便宜绕法 G1：一行注释就能把 x 记成已选中并从 runner 摘掉，
#     而且在 diff 里像一条无辜注释、可抵赖。把过滤去掉这一格立刻转红。
#   * **YAML 层注释**（step 之间那种）—— **这一格恒真、零鉴别力**，保留只为记录形态：
#     YAML 解析器根本不会把它交给任何 step 的 `run`，因此任何实现都不会命中它。
#     ⛔ 不要把它读成「注释绕法已被守住」—— 守住那件事的是上面那格。
# ─────────────────────────────────────────────────────────────────────────────
$shapeWorkflow = @'
name: Shape
on: [push]
jobs:
  inline-job:
    runs-on: ubuntu-latest
    steps:
      - name: Inline reference
        run: ./scripts/tests/inline.Tests.ps1
  block-job:
    runs-on: ubuntu-latest
    steps:
      - name: Block reference
        run: |
          . ./scripts/lib/ScriptAutomation.ps1
          ./scripts/tests/block.Tests.ps1
          echo done
  yaml-comment-job:
    runs-on: ubuntu-latest
    steps:
      # ./scripts/tests/commented.Tests.ps1 is deliberately only mentioned here.
      - name: Unrelated
        run: echo unrelated
  shell-comment-job:
    runs-on: ubuntu-latest
    steps:
      - name: Mentions a test only in a shell comment inside the run body
        run: |
          # ./scripts/tests/shell-commented.Tests.ps1 的逐字契约断言，故保持逐处加固。
          echo unrelated
'@
$shapeRoot = New-SelectionFixtureRoot -WorkflowContent $shapeWorkflow -TestFiles @{
    'inline.Tests.ps1' = '# inline'
    'block.Tests.ps1' = '# block'
    'commented.Tests.ps1' = '# commented'
    'shell-commented.Tests.ps1' = '# shell commented'
}
try {
    $shapeSelections = Get-NervScriptTestWorkflowSelections -RepositoryRoot $shapeRoot
    Assert-Selection ($shapeSelections.ContainsKey('inline.Tests.ps1')) 'A one-line run: reference must be detected.'
    Assert-Selection ($shapeSelections.ContainsKey('block.Tests.ps1')) 'A reference inside a multi-line run: block must be detected — this is the positive control for the multi-line scan face.'
    Assert-Selection (-not $shapeSelections.ContainsKey('commented.Tests.ps1')) 'A name that only appears in a YAML-level comment must not count as selection (zero-discrimination control: no implementation can reach it).'
    Assert-Selection (-not $shapeSelections.ContainsKey('shell-commented.Tests.ps1')) `
        'A name that only appears in a shell comment inside a run body must not count as selection — this is the G1 control and it does discriminate: drop the comment filter and it turns red.'

    $shapePlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    $shapeExpected = [Collections.Generic.HashSet[string]]::new([string[]]@('commented.Tests.ps1', 'shell-commented.Tests.ps1'), [StringComparer]::Ordinal)
    Assert-Selection ($shapeExpected.SetEquals([string[]] $shapePlan.RunnerSelected)) `
        'The discovery runner must select exactly the complement: both comment-only files and nothing else.'

    # 新增一个文件后，它必须自动落进 runner 集合 —— 这是「后来者不掉队」的直接证据，
    # 而不是「有人记得改名单」。
    [IO.File]::WriteAllText((Join-Path $shapeRoot 'scripts/tests/newcomer.Tests.ps1'), '# newcomer', [Text.UTF8Encoding]::new($false))
    $newcomerPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection (@($newcomerPlan.RunnerSelected | Where-Object { [string]::Equals($_, 'newcomer.Tests.ps1', [StringComparison]::Ordinal) }).Count -eq 1) `
        'A newly added scripts/tests file must land in the discovery runner set without any registration.'
    Assert-Selection ($newcomerPlan.All.Count -eq 5 -and $newcomerPlan.WorkflowSelected.Count -eq 2 -and $newcomerPlan.RunnerSelected.Count -eq 3) `
        'The plan must stay a total partition after a file is added.'
}
finally { Remove-Item -LiteralPath $shapeRoot -Recurse -Force -ErrorAction SilentlyContinue }

# ─────────────────────────────────────────────────────────────────────────────
# 1b. 子串不算引用（前瞻性加固，当前仓库零触发）
#
# 若 A 的文件名是 B 的子串，朴素子串判定下引用 B 就会把 A 也标成「已被工作流选中」并从 runner
# 摘掉——静默漏跑，正是本票要消除的形态。
#
# ⚠️ 分寸：**当前仓库里一对这样的名字都没有**（61 个文件 3660 个有序对，子串命中数 0，
# 下面那条断言就是这个读数）。这里用的是合成名字，不是实例。留这一格是因为命名碰撞只需有人
# 新增一个文件就会出现，而它的失效方向是静默漏跑。
#
# 两侧都要判：只判左侧挡得住「短名是长名的后缀/中缀」，挡不住「短名是长名的**前缀**」
# （`x.Tests.ps1` vs `x.Tests.ps1-extra.Tests.ps1`）。两个方向各一条用例。
# ─────────────────────────────────────────────────────────────────────────────
$substringWorkflow = @'
name: Substring
on: [push]
jobs:
  longer-only:
    runs-on: ubuntu-latest
    steps:
      - name: Longer names only
        run: |
          ./scripts/tests/full-chain-lane.Tests.ps1
          ./scripts/tests/lane.Tests.ps1-extra.Tests.ps1
'@
$substringRoot = New-SelectionFixtureRoot -WorkflowContent $substringWorkflow -TestFiles @{
    'full-chain-lane.Tests.ps1' = '# longer, shorter name is its suffix'
    'lane.Tests.ps1-extra.Tests.ps1' = '# longer, shorter name is its prefix'
    'lane.Tests.ps1' = '# shorter'
}
try {
    $substringSelections = Get-NervScriptTestWorkflowSelections -RepositoryRoot $substringRoot
    Assert-Selection ($substringSelections.ContainsKey('full-chain-lane.Tests.ps1')) 'The referenced longer file must be selected.'
    Assert-Selection ($substringSelections.ContainsKey('lane.Tests.ps1-extra.Tests.ps1')) 'The referenced prefix-shaped longer file must be selected.'
    Assert-Selection (-not $substringSelections.ContainsKey('lane.Tests.ps1')) 'A file whose name is only a substring of two referenced names must not count as selected.'

    Assert-Selection (Test-NervScriptTestReference -Text './scripts/tests/lane.Tests.ps1' -Name 'lane.Tests.ps1') 'A path-prefixed whole-name reference must match.'
    Assert-Selection (Test-NervScriptTestReference -Text 'lane.Tests.ps1' -Name 'lane.Tests.ps1') 'A reference at index 0 must match.'
    Assert-Selection (Test-NervScriptTestReference -Text "& (Join-Path `$r 'lane.Tests.ps1')" -Name 'lane.Tests.ps1') 'A quoted reference must match.'
    Assert-Selection (Test-NervScriptTestReference -Text 'lane.Tests.ps1:982 调用' -Name 'lane.Tests.ps1') 'A reference followed by a line number must match.'
    Assert-Selection (-not (Test-NervScriptTestReference -Text 'full-chain-lane.Tests.ps1' -Name 'lane.Tests.ps1')) 'A name preceded by a name character must not match (suffix shape).'
    Assert-Selection (-not (Test-NervScriptTestReference -Text 'lane.Tests.ps1-extra.Tests.ps1' -Name 'lane.Tests.ps1')) 'A name followed by a name character must not match (prefix shape) — the left-only check used to let this through.'
    Assert-Selection (Test-NervScriptTestReference -Text 'full-chain-lane.Tests.ps1 and ./lane.Tests.ps1' -Name 'lane.Tests.ps1') 'A later whole-name occurrence must still match after a rejected substring hit.'
}
finally { Remove-Item -LiteralPath $substringRoot -Recurse -Force -ErrorAction SilentlyContinue }

# 「当前仓库零触发」是一条读数，不是印象：把它当断言写下来，将来第一次出现同形状名字时这里会红，
# 提醒复审这条加固从「前瞻」变成了「在承重」。
$collisionPairs = 0
$allTestNames = @(Get-NervScriptTestFiles -RepositoryRoot $repoRoot)
foreach ($leftName in $allTestNames) {
    foreach ($rightName in $allTestNames) {
        if ([string]::Equals($leftName, $rightName, [StringComparison]::Ordinal)) { continue }
        if ($rightName.Contains($leftName, [StringComparison]::Ordinal)) { $collisionPairs++ }
    }
}
Assert-Selection ($collisionPairs -eq 0) "scripts/tests currently has $collisionPairs substring-colliding name pairs; the whole-name boundary has stopped being merely prophylactic and the narrative around it must be updated."


# ─────────────────────────────────────────────────────────────────────────────
# 2. 扫描面塌掉必须 throw，不得伪装成「没有遗漏」
# ─────────────────────────────────────────────────────────────────────────────
$emptyRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-script-test-selection-empty-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $emptyRoot 'scripts/tests')) | Out-Null
    Assert-Selection ($null -ne (Get-SelectionFailure { Get-NervScriptTestFiles -RepositoryRoot $emptyRoot })) 'An empty scripts/tests directory must fail closed.'
    Assert-Selection ($null -ne (Get-SelectionFailure { Get-NervScriptTestWorkflowSelections -RepositoryRoot $emptyRoot })) 'A missing workflow directory must fail closed.'

    [IO.Directory]::CreateDirectory((Join-Path $emptyRoot '.github/workflows')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $emptyRoot 'scripts/tests/only.Tests.ps1'), '# only', [Text.UTF8Encoding]::new($false))
    Assert-Selection ($null -ne (Get-SelectionFailure { Get-NervScriptTestWorkflowSelections -RepositoryRoot $emptyRoot })) 'A workflow directory with no workflow file must fail closed.'

    [IO.File]::WriteAllText((Join-Path $emptyRoot '.github/workflows/ci.yml'), "name: Empty`non: [push]`njobs:`n  noop:`n    runs-on: ubuntu-latest`n    steps:`n      - name: Noop`n        run: echo noop`n", [Text.UTF8Encoding]::new($false))
    Assert-Selection ($null -ne (Get-SelectionFailure { Get-NervScriptTestWorkflowSelections -RepositoryRoot $emptyRoot })) 'Zero derived workflow selections must fail closed rather than report a clean sheet.'
}
finally { Remove-Item -LiteralPath $emptyRoot -Recurse -Force -ErrorAction SilentlyContinue }

# ─────────────────────────────────────────────────────────────────────────────
# 3. 登记面自己烂掉必须被抓到
#
# 出界登记是这套机制里唯一的人工维护面，所以它的每一种腐烂形态都要有一条红。
# ─────────────────────────────────────────────────────────────────────────────
$registryWorkflow = @'
name: Registry
on: [push]
jobs:
  only-job:
    runs-on: ubuntu-latest
    steps:
      - name: Parent
        run: ./scripts/tests/parent.Tests.ps1
'@
$registryRoot = New-SelectionFixtureRoot -WorkflowContent $registryWorkflow -TestFiles @{
    'parent.Tests.ps1' = "& (Join-Path `$PSScriptRoot 'child.Tests.ps1')`n"
    'child.Tests.ps1' = '# child'
    'orphan.Tests.ps1' = "# Script-Governance:`n#   Category: check`n#   Requires:`n#     - PowerShell 7`n#     - a real widget daemon`n# orphan`n"
    'stranger.Tests.ps1' = '# stranger'
}
try {
    $goodRegistry = @(
        [pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Tracking = $null; Reason = 'parent runs it' },
        [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'needs a real dependency' }
    )
    $goodPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry $goodRegistry
    Assert-Selection ($goodPlan.RunnerSelected.Count -eq 1 -and [string]::Equals($goodPlan.RunnerSelected[0], 'stranger.Tests.ps1', [StringComparison]::Ordinal)) `
        'A well-formed registry must remove exactly its own entries from the runner set.'

    foreach ($rot in @(
            [pscustomobject]@{
                Name = 'missing-file'
                Registry = @([pscustomobject]@{ Name = 'deleted.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'stale' })
            },
            [pscustomobject]@{
                Name = 'empty-reason'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = '   ' })
            },
            [pscustomobject]@{
                Name = 'unknown-kind'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'someday'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'later' })
            },
            [pscustomobject]@{
                Name = 'duplicate'
                Registry = @(
                    [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'first' },
                    [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'second' })
            },
            [pscustomobject]@{
                Name = 'nested-without-parent'
                Registry = @([pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = ''; Tracking = $null; Reason = 'somebody runs it' })
            },
            [pscustomobject]@{
                Name = 'nested-parent-not-in-ci'
                Registry = @([pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'stranger.Tests.ps1'; Tracking = $null; Reason = 'claimed' })
            },
            [pscustomobject]@{
                Name = 'nested-parent-does-not-reference-child'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Tracking = $null; Reason = 'claimed' })
            },
            [pscustomobject]@{
                Name = 'excluded-with-parent'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = 'parent.Tests.ps1'; Tracking = $null; Requirement = 'a real widget daemon'; Reason = 'confused' })
            },
            [pscustomobject]@{
                Name = 'contradicts-workflow'
                Registry = @([pscustomobject]@{ Name = 'parent.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'PowerShell 7'; Reason = 'but CI runs it' })
            },
            # quarantine 的四种腐烂形态。前两条是裁决点名的 fail-closed 要求：票号为空、票号形态不对。
            # 后两条守住「三种 Kind 互不可替代」：quarantine 不许带 Parent、非 quarantine 不许带 Tracking
            # —— 于是「无票静默排除」既写不成 quarantine（缺票号）也写不成 excluded（带了票号就红）。
            [pscustomobject]@{
                Name = 'excluded-without-requirement'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = ''; Reason = 'needs a real dependency' })
            },
            [pscustomobject]@{
                Name = 'excluded-requirement-not-declared-by-the-file'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'a GPU'; Reason = 'needs a real dependency' })
            },
            [pscustomobject]@{
                Name = 'excluded-on-a-file-with-no-requires-block'
                Registry = @([pscustomobject]@{ Name = 'stranger.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = $null; Requirement = 'anything'; Reason = 'no header at all' })
            },
            [pscustomobject]@{
                Name = 'quarantine-carrying-requirement'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = '#3404'; Requirement = 'a real widget daemon'; Reason = 'red on linux #3404' })
            },
            [pscustomobject]@{
                Name = 'quarantine-without-tracking'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = ''; Reason = 'red on linux' })
            },
            [pscustomobject]@{
                Name = 'quarantine-with-malformed-tracking'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = '3404'; Reason = 'red on linux' })
            },
            [pscustomobject]@{
                Name = 'quarantine-with-zero-tracking'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = '#0'; Reason = 'red on linux' })
            },
            [pscustomobject]@{
                Name = 'quarantine-with-parent'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = 'parent.Tests.ps1'; Tracking = '#3404'; Reason = 'red on linux' })
            },
            [pscustomobject]@{
                Name = 'quarantine-pointing-at-missing-file'
                Registry = @([pscustomobject]@{ Name = 'deleted.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = '#3404'; Reason = 'red on linux' })
            },
            [pscustomobject]@{
                Name = 'excluded-carrying-tracking'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Tracking = '#3404'; Requirement = 'a real widget daemon'; Reason = 'needs a real dependency' })
            },
            [pscustomobject]@{
                Name = 'nested-carrying-tracking'
                Registry = @([pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Tracking = '#3404'; Reason = 'parent runs it' })
            }
        )) {
        $failure = Get-SelectionFailure { Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry $rot.Registry }
        Assert-Selection ($null -ne $failure) "Registry rot '$($rot.Name)' must fail closed."
    }

    # quarantine 的正路：带合法票号 ⇒ 从 runner 摘掉；**删掉这一行 ⇒ 立刻回到 runner**。
    # 后半句是解除成本的直接读数：解除只需删一条登记，不需要碰 runner 源码。
    $quarantineRegistry = @(
        [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'quarantine'; Parent = $null; Tracking = '#3404'; Reason = 'red on linux, tracked' })
    $quarantinePlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry $quarantineRegistry
    Assert-Selection (@($quarantinePlan.RunnerSelected | Where-Object { [string]::Equals($_, 'orphan.Tests.ps1', [StringComparison]::Ordinal) }).Count -eq 0) `
        'A well-formed quarantine entry must remove its file from the discovery runner set.'
    $liftedPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry @()
    Assert-Selection (@($liftedPlan.RunnerSelected | Where-Object { [string]::Equals($_, 'orphan.Tests.ps1', [StringComparison]::Ordinal) }).Count -eq 1) `
        'Deleting the quarantine entry must put the file straight back into the discovery runner set, with no other edit.'

    # 只在注释里引用子测试的父测试不算数：否则加一行注释就能把一个文件从 runner 摘掉。
    [IO.File]::WriteAllText((Join-Path $registryRoot 'scripts/tests/parent.Tests.ps1'), "# child.Tests.ps1`n", [Text.UTF8Encoding]::new($false))
    $commentOnlyFailure = Get-SelectionFailure {
        Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry @(
            [pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Tracking = $null; Reason = 'claimed' })
    }
    Assert-Selection ($null -ne $commentOnlyFailure) 'A nested claim backed only by a comment in the parent must fail closed.'
}
finally { Remove-Item -LiteralPath $registryRoot -Recurse -Force -ErrorAction SilentlyContinue }

# ─────────────────────────────────────────────────────────────────────────────
# 4. 真实仓库：分区必须是全覆盖且互不相交，且 runner 真的被 CI 调起来
# ─────────────────────────────────────────────────────────────────────────────
$plan = Get-NervScriptTestSelectionPlan -RepositoryRoot $repoRoot

$covered = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($name in $plan.WorkflowSelected) {
    Assert-Selection ($covered.Add($name)) "Script test '$name' appears in more than one selection bucket."
}
foreach ($entry in $plan.OutOfBand) {
    Assert-Selection ($covered.Add((Get-NervScriptTestRegistryField -Entry $entry -Name 'Name'))) "Script test '$($entry.Name)' appears in more than one selection bucket."
}
foreach ($name in $plan.RunnerSelected) {
    Assert-Selection ($covered.Add($name)) "Script test '$name' appears in more than one selection bucket."
}
Assert-Selection ($covered.SetEquals([string[]] $plan.All)) `
    'Workflow-selected, out-of-band and discovery-runner buckets must partition scripts/tests/*.Tests.ps1 exactly once each.'

# runner 集合为空意味着这一步变成空转，必须当成红而不是「都覆盖到了」。
Assert-Selection ($plan.RunnerSelected.Count -gt 0) 'The discovery runner must select at least one test; an empty selection would make its CI step a no-op.'

$workflowSource = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
Assert-Selection ($workflowSource.Contains('run: ./scripts/run-script-contract-tests.ps1', [StringComparison]::Ordinal)) `
    'CI must run the discovery runner; without that step the whole selection closure is dead code.'
Assert-Selection ($workflowSource.Contains('run: ./scripts/tests/script-test-selection.Tests.ps1', [StringComparison]::Ordinal)) `
    'CI must run this selection contract; a coverage gate that is itself unselected proves nothing.'

$runnerSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/run-script-contract-tests.ps1'))
Assert-Selection ($runnerSource.Contains('Get-NervScriptTestSelectionPlan', [StringComparison]::Ordinal)) `
    'The discovery runner must take its selection from the shared plan rather than from a list of its own.'
# 对 All 而不是只对 RunnerSelected 断言：runner 源码里一个测试文件名都不许出现。
# 这同时是「解除 quarantine 只需删一条登记」的直接证据——被摘掉的文件名也不在 runner 里，
# 所以把它放回选中集合不需要碰 runner 源码。
foreach ($name in $plan.All) {
    Assert-Selection (-not $runnerSource.Contains($name, [StringComparison]::Ordinal)) `
        "The discovery runner must not name '$name' literally; naming files is the defect #3300 exists to remove, and it would also make lifting a quarantine a runner edit."
}

$quarantined = @($plan.OutOfBand | Where-Object { [string]::Equals((Get-NervScriptTestRegistryField -Entry $_ -Name 'Kind'), 'quarantine', [StringComparison]::Ordinal) })
foreach ($entry in $quarantined) {
    $entryTracking = Get-NervScriptTestRegistryField -Entry $entry -Name 'Tracking'
    $entryReason = Get-NervScriptTestRegistryField -Entry $entry -Name 'Reason'
    Assert-Selection (-not [string]::IsNullOrWhiteSpace($entryTracking)) "Quarantined '$($entry.Name)' must carry a tracking issue."
    Assert-Selection ($entryReason.Contains($entryTracking, [StringComparison]::Ordinal)) `
        "Quarantined '$($entry.Name)' must state its lift condition against $entryTracking in the reason, so the registry is readable without cross-referencing code."
}

Write-Output "Script test selection contracts passed: $($plan.All.Count) files, $($plan.WorkflowSelected.Count) workflow-selected, $($plan.OutOfBand.Count) out of band ($($quarantined.Count) quarantined), $($plan.RunnerSelected.Count) discovery-runner selected."
