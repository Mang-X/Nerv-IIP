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

# 发现规则及其覆盖边界见 docs/governance/script-automation.md。

$script:NervScriptTestDirectory = 'scripts/tests'
$script:NervScriptTestSuffix = '.Tests.ps1'
$script:NervScriptTestOutOfBandKinds = @('nested', 'excluded', 'quarantine')
$script:NervScriptTestTrackingPattern = '^#[1-9][0-9]*$'

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
    # 同时检查文件名两端，避免长文件名命中短文件名后把短名从发现式 runner 漏掉。
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

                # 忽略常见注释中的提及，让未实际点名的文件仍由发现式 runner 执行。
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
