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
      - name: Mentions tests only inside comments in the run body
        run: |
          # ./scripts/tests/shell-commented.Tests.ps1 的逐字契约断言，故保持逐处加固。
          echo ok  # ./scripts/tests/trailing-commented.Tests.ps1
          <# ./scripts/tests/block-commented.Tests.ps1 #>
          <#
          ./scripts/tests/multiline-block-commented.Tests.ps1
          #>
          echo unrelated
'@
$shapeRoot = New-SelectionFixtureRoot -WorkflowContent $shapeWorkflow -TestFiles @{
    'inline.Tests.ps1' = '# inline'
    'block.Tests.ps1' = '# block'
    'commented.Tests.ps1' = '# commented'
    'shell-commented.Tests.ps1' = '# whole-line shell comment'
    'trailing-commented.Tests.ps1' = '# trailing shell comment'
    'block-commented.Tests.ps1' = '# single-line pwsh block comment'
    'multiline-block-commented.Tests.ps1' = '# multi-line pwsh block comment'
}
try {
    $shapePlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection ([Array]::IndexOf([string[]] $shapePlan.WorkflowSelected, 'inline.Tests.ps1') -ge 0 -and
        [Array]::IndexOf([string[]] $shapePlan.WorkflowSelected, 'block.Tests.ps1') -ge 0) 'Both single-line and block run bodies must select their tests.'
    $shapeExpected = [Collections.Generic.HashSet[string]]::new([string[]]@(
            'commented.Tests.ps1',
            'shell-commented.Tests.ps1',
            'trailing-commented.Tests.ps1',
            'block-commented.Tests.ps1',
            'multiline-block-commented.Tests.ps1'), [StringComparer]::Ordinal)
    Assert-Selection ($shapeExpected.SetEquals([string[]] $shapePlan.RunnerSelected)) `
        'The discovery runner must select exactly the complement: all five comment-only files and nothing else.'

    # 合同执行 owner 可以在 workflow 与发现式补集之间移动，不能固定 step 名或排列。
    $mergedWorkflow = @'
name: Rearranged
on: [push]
jobs:
  governance:
    runs-on: ubuntu-latest
    steps:
      - name: Renamed and merged
        run: |
          ./scripts/tests/block.Tests.ps1
          ./scripts/tests/inline.Tests.ps1
'@
    $shapeWorkflowPath = Join-Path $shapeRoot '.github/workflows/ci.yml'
    [IO.File]::WriteAllText($shapeWorkflowPath, $mergedWorkflow, [Text.UTF8Encoding]::new($false))
    $mergedPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection ($shapeExpected.SetEquals([string[]] $mergedPlan.RunnerSelected)) 'Renaming, reversing and merging steps must preserve the execution complement.'
    [IO.File]::WriteAllText($shapeWorkflowPath, $mergedWorkflow.Replace('          ./scripts/tests/inline.Tests.ps1', ''), [Text.UTF8Encoding]::new($false))
    $movedPlan = Get-NervScriptTestSelectionPlan -RepositoryRoot $shapeRoot -Registry @()
    Assert-Selection ($movedPlan.RunnerSelected.Count -eq ($shapePlan.RunnerSelected.Count + 1) -and
        @($movedPlan.RunnerSelected | Where-Object { [string]::Equals($_, 'inline.Tests.ps1', [StringComparison]::Ordinal) }).Count -eq 1) 'Removing a direct workflow invocation must transfer that test to the discovery runner exactly once.'
    Write-Output 'Governance selection rearrangement control: PASS (renamed/merged/reversed; removed invocation rediscovered).'
}
finally { Remove-Item -LiteralPath $shapeRoot -Recurse -Force -ErrorAction SilentlyContinue }

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

Assert-Selection ([Array]::IndexOf([string[]]$plan.RunnerSelected, 'redis-cap-observation.Tests.ps1') -ge 0) `
    'Script Governance must discover the Redis/CAP observation fixtures so they run on script-only changes.'

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

$workflow = ConvertFrom-NervCiRequiredSummaryWorkflow -Path (Join-Path $repoRoot '.github/workflows/ci.yml') -WorkingDirectory $repoRoot
$discoveryCalls = @(foreach ($job in $workflow.jobs.PSObject.Properties.Value) {
    $steps = $job.PSObject.Properties['steps']
    if ($null -eq $steps) { continue }
    foreach ($step in @($steps.Value)) {
        $run = $step.PSObject.Properties['run']
        if ($null -eq $run) { continue }
        $commands = [Management.Automation.Language.Parser]::ParseInput([string] $run.Value, [ref]$null, [ref]$null)
        $commands.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
                [string]::Equals($node.GetCommandName(), './scripts/ci/run-script-contract-tests.ps1', [StringComparison]::Ordinal) -and
                @($node.CommandElements | Where-Object {
                    $_ -is [Management.Automation.Language.CommandParameterAst] -and
                        [string]::Equals($_.ParameterName, 'ScriptPath', [StringComparison]::OrdinalIgnoreCase)
                }).Count -eq 0
        }, $true)
    }
})
Assert-Selection ($discoveryCalls.Count -gt 0) 'CI must invoke automatic discovery. Explicit suites and comments cannot run its complement.'

$quarantined = @($plan.OutOfBand | Where-Object { [string]::Equals((Get-NervScriptTestRegistryField -Entry $_ -Name 'Kind'), 'quarantine', [StringComparison]::Ordinal) })
foreach ($entry in $quarantined) {
    $entryTracking = Get-NervScriptTestRegistryField -Entry $entry -Name 'Tracking'
    $entryReason = Get-NervScriptTestRegistryField -Entry $entry -Name 'Reason'
    Assert-Selection (-not [string]::IsNullOrWhiteSpace($entryTracking)) "Quarantined '$($entry.Name)' must carry a tracking issue."
    Assert-Selection ($entryReason.Contains($entryTracking, [StringComparison]::Ordinal)) `
        "Quarantined '$($entry.Name)' must state its lift condition against $entryTracking in the reason, so the registry is readable without cross-referencing code."
}

Write-Output "Script test selection contracts passed: $($plan.All.Count) files, $($plan.WorkflowSelected.Count) workflow-selected, $($plan.OutOfBand.Count) out of band ($($quarantined.Count) quarantined), $($plan.RunnerSelected.Count) discovery-runner selected."
