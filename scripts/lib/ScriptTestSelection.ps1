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
$script:NervScriptTestOutOfBandKinds = @('nested', 'excluded')

# 唯一的人工维护面。每条都由 Assert-NervScriptTestOutOfBandRegistry 自证，不是自由文本备注。
#
#   nested   ：该测试由另一个**已在 CI 上被选中**的测试作为子进程或子脚本执行，独立再跑一遍只是重复。
#              必须给出 Parent，且 Parent 的源码必须真的引用它。
#   excluded ：该测试在发现式 runner 的执行环境里根本跑不起来（需要真实外部依赖或必填参数）。
#              必须给出 Reason；它**不代表**该测试无需覆盖，只代表覆盖它属于另一条 lane。
$script:NervScriptTestOutOfBandRegistry = @(
    [pscustomobject]@{
        Name = 'postgres-test-database-consumers.Tests.ps1'
        Kind = 'nested'
        Parent = 'postgres-test-lane.Tests.ps1'
        Reason = 'postgres-test-lane.Tests.ps1:982 直接 `&` 调用它，父测试已在 Script Governance 上被点名执行。'
    },
    [pscustomobject]@{
        Name = 'redis-test-namespace-cleanup.Tests.ps1'
        Kind = 'nested'
        Parent = 'redis-cap-test-lane.Tests.ps1'
        Reason = 'redis-cap-test-lane.Tests.ps1:402 直接 `&` 调用它，父测试已在 Script Governance 上被点名执行。'
    },
    [pscustomobject]@{
        Name = 'script-automation-live-output.Tests.ps1'
        Kind = 'nested'
        Parent = 'script-automation-signal-exit.Tests.ps1'
        Reason = 'script-automation-signal-exit.Tests.ps1:60 以子进程方式跑它并对非零退出 throw（#3168 的 6726b1467）。'
    },
    [pscustomobject]@{
        Name = 'redis-cap-observation-runtime.Tests.ps1'
        Kind = 'excluded'
        Parent = $null
        Reason = '它有必填参数 -CountersPath，且自述 Requires 为 Linux、prlimit、timeout、redis-cli 与 dotnet-counters；这些只在 Redis/CAP lane 的服务容器里成立，不属于 Script Governance 的无依赖执行面。'
    }
)

function Get-NervScriptTestOutOfBandRegistry {
    [CmdletBinding()]
    param()

    return @($script:NervScriptTestOutOfBandRegistry)
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

function Test-NervScriptTestReference {
    <#
        文本里是否**以整名形式**出现该测试文件名。

        朴素的 `$text.Contains($name)` 在这里是 fail-open 的：`test-lane.Tests.ps1` 是
        `full-chain-test-lane.Tests.ps1` 的子串，于是引用后者会把前者也标成「已被选中」并从
        发现式 runner 里摘掉——正好是本票要消除的那种静默漏跑。因此要求命中位置左侧不是
        文件名字符（字母、数字、`_`、`.`、`-`）；右侧不需要判定，因为名字以 `.Tests.ps1` 结尾，
        而更长的名字只可能在左侧多出字符。
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
        if ($matchIndex -eq 0) { return $true }
        $previous = $Text[$matchIndex - 1]
        if (-not ([char]::IsLetterOrDigit($previous) -or $previous -eq '_' -or $previous -eq '.' -or $previous -eq '-')) { return $true }
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

                $run = [string] $runProperty.Value
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
        $name = [string] $entry.Name
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Out-of-band script test registry has an entry without a name.'
        }
        if (-not $seen.Add($name)) {
            throw "Out-of-band script test registry lists '$name' more than once."
        }
        if (-not $known.Contains($name)) {
            throw "Out-of-band script test registry lists '$name', which is not a file under $script:NervScriptTestDirectory; the registration face has rotted."
        }

        $kind = [string] $entry.Kind
        if (@($script:NervScriptTestOutOfBandKinds | Where-Object { [string]::Equals($_, $kind, [StringComparison]::Ordinal) }).Count -ne 1) {
            throw "Out-of-band script test registry entry '$name' declares unknown kind '$kind'; allowed kinds are $($script:NervScriptTestOutOfBandKinds -join ', ')."
        }

        if ([string]::IsNullOrWhiteSpace([string] $entry.Reason)) {
            throw "Out-of-band script test registry entry '$name' has no reason; an unexplained exemption is indistinguishable from an oversight."
        }

        if ($WorkflowSelections.ContainsKey($name)) {
            throw "Out-of-band script test registry entry '$name' is also selected by a workflow run body; the registry contradicts the workflow."
        }

        if ([string]::Equals($kind, 'nested', [StringComparison]::Ordinal)) {
            $parent = [string] $entry.Parent
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
        elseif ($null -ne $entry.Parent) {
            throw "Out-of-band script test registry entry '$name' has kind '$kind' but still names a parent; only nested entries have one."
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

    $outOfBandNames = [Collections.Generic.HashSet[string]]::new([string[]]@($Registry | ForEach-Object { [string] $_.Name }), [StringComparer]::Ordinal)
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
