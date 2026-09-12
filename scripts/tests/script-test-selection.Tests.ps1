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
# 阳性对照（多行块必须命中）与阴性对照（注释里的名字必须不命中）都在这里钉死：少了阳性对照，
# 一个只认一行式的实现会「看起来」正确并把多行选中的文件误判成零选中；少了阴性对照，注释里
# 提一嘴就能把一个文件从 runner 里摘出去。
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
  comment-job:
    runs-on: ubuntu-latest
    steps:
      # ./scripts/tests/commented.Tests.ps1 is deliberately only mentioned here.
      - name: Unrelated
        run: echo unrelated
'@
$shapeRoot = New-SelectionFixtureRoot -WorkflowContent $shapeWorkflow -TestFiles @{
    'inline.Tests.ps1' = '# inline'
    'block.Tests.ps1' = '# block'
    'commented.Tests.ps1' = '# commented'
}
try {
    $shapeSelections = Get-NervScriptTestWorkflowSelections -RepositoryRoot $shapeRoot
    Assert-Selection ($shapeSelections.ContainsKey('inline.Tests.ps1')) 'A one-line run: reference must be detected.'
    Assert-Selection ($shapeSelections.ContainsKey('block.Tests.ps1')) 'A reference inside a multi-line run: block must be detected — this is the positive control for the multi-line scan face.'
    Assert-Selection (-not $shapeSelections.ContainsKey('commented.Tests.ps1')) 'A name that only appears in a workflow comment must not count as selection.'

    $shapePlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection ($shapePlan.RunnerSelected.Count -eq 1 -and [string]::Equals($shapePlan.RunnerSelected[0], 'commented.Tests.ps1', [StringComparison]::Ordinal)) `
        'The discovery runner must select exactly the complement: the commented-only file and nothing else.'

    # 新增一个文件后，它必须自动落进 runner 集合 —— 这是「后来者不掉队」的直接证据，
    # 而不是「有人记得改名单」。
    [IO.File]::WriteAllText((Join-Path $shapeRoot 'scripts/tests/newcomer.Tests.ps1'), '# newcomer', [Text.UTF8Encoding]::new($false))
    $newcomerPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection (@($newcomerPlan.RunnerSelected | Where-Object { [string]::Equals($_, 'newcomer.Tests.ps1', [StringComparison]::Ordinal) }).Count -eq 1) `
        'A newly added scripts/tests file must land in the discovery runner set without any registration.'
    Assert-Selection ($newcomerPlan.All.Count -eq 4 -and $newcomerPlan.WorkflowSelected.Count -eq 2 -and $newcomerPlan.RunnerSelected.Count -eq 2) `
        'The plan must stay a total partition after a file is added.'
}
finally { Remove-Item -LiteralPath $shapeRoot -Recurse -Force -ErrorAction SilentlyContinue }

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
    'orphan.Tests.ps1' = '# orphan'
    'stranger.Tests.ps1' = '# stranger'
}
try {
    $goodRegistry = @(
        [pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Reason = 'parent runs it' },
        [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = 'needs a real dependency' }
    )
    $goodPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry $goodRegistry
    Assert-Selection ($goodPlan.RunnerSelected.Count -eq 1 -and [string]::Equals($goodPlan.RunnerSelected[0], 'stranger.Tests.ps1', [StringComparison]::Ordinal)) `
        'A well-formed registry must remove exactly its own entries from the runner set.'

    foreach ($rot in @(
            [pscustomobject]@{
                Name = 'missing-file'
                Registry = @([pscustomobject]@{ Name = 'deleted.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = 'stale' })
            },
            [pscustomobject]@{
                Name = 'empty-reason'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = '   ' })
            },
            [pscustomobject]@{
                Name = 'unknown-kind'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'someday'; Parent = $null; Reason = 'later' })
            },
            [pscustomobject]@{
                Name = 'duplicate'
                Registry = @(
                    [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = 'first' },
                    [pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = 'second' })
            },
            [pscustomobject]@{
                Name = 'nested-without-parent'
                Registry = @([pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = ''; Reason = 'somebody runs it' })
            },
            [pscustomobject]@{
                Name = 'nested-parent-not-in-ci'
                Registry = @([pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'stranger.Tests.ps1'; Reason = 'claimed' })
            },
            [pscustomobject]@{
                Name = 'nested-parent-does-not-reference-child'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Reason = 'claimed' })
            },
            [pscustomobject]@{
                Name = 'excluded-with-parent'
                Registry = @([pscustomobject]@{ Name = 'orphan.Tests.ps1'; Kind = 'excluded'; Parent = 'parent.Tests.ps1'; Reason = 'confused' })
            },
            [pscustomobject]@{
                Name = 'contradicts-workflow'
                Registry = @([pscustomobject]@{ Name = 'parent.Tests.ps1'; Kind = 'excluded'; Parent = $null; Reason = 'but CI runs it' })
            }
        )) {
        $failure = Get-SelectionFailure { Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry $rot.Registry }
        Assert-Selection ($null -ne $failure) "Registry rot '$($rot.Name)' must fail closed."
    }

    # 只在注释里引用子测试的父测试不算数：否则加一行注释就能把一个文件从 runner 摘掉。
    [IO.File]::WriteAllText((Join-Path $registryRoot 'scripts/tests/parent.Tests.ps1'), "# child.Tests.ps1`n", [Text.UTF8Encoding]::new($false))
    $commentOnlyFailure = Get-SelectionFailure {
        Get-NervScriptTestSelectionPlan -RepositoryRoot $registryRoot -Registry @(
            [pscustomobject]@{ Name = 'child.Tests.ps1'; Kind = 'nested'; Parent = 'parent.Tests.ps1'; Reason = 'claimed' })
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
    Assert-Selection ($covered.Add([string] $entry.Name)) "Script test '$($entry.Name)' appears in more than one selection bucket."
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
foreach ($name in $plan.RunnerSelected) {
    Assert-Selection (-not $runnerSource.Contains($name, [StringComparison]::Ordinal)) `
        "The discovery runner must not name '$name' literally; naming files is the defect #3300 exists to remove."
}

Write-Output "Script test selection contracts passed: $($plan.All.Count) files, $($plan.WorkflowSelected.Count) workflow-selected, $($plan.OutOfBand.Count) out of band, $($plan.RunnerSelected.Count) discovery-runner selected."
