# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads kernel memory accounting files and, when asked, the kernel ring buffer through an injected command runner
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

<#
    内存维度取证（#1664 / #1877）。

    #1664 把 FullChain `man-440` 的 137 归因为 OOM-kill，但票面自己声明那是推断：诊断包里**没有
    任何内存维度证据**。这个库补的就是那一维——它只采证，不下结论。

    两条不可协商的性质：

    0. **见证者要选对**：cgroup 的 `memory.events.oom_kill` 只统计因本 cgroup 上限触发的击杀；
       hosted runner 的 slice 上 `memory.max` 是 `max`，真发生的是全局 OOM，那个计数会一直是 0。
       因此 `/proc/vmstat` 的 `oom_kill`（全局、world-readable、不依赖 `dmesg` 权限）才是关键的
       那一项，判读看前后差值。
    1. **best-effort**：任何一项读不到（非 Linux、无 cgroup、dmesg 无权限）都只记 `unavailable`
       和原因，绝不抛错。采证脚本把被测 lane 弄红，等于用一个新的假红换一个旧的真红。
    2. **可被合成事实驱动**：`ProcRoot`/`CgroupRoot`/`CommandRunner` 都是注入 seam，因此契约测试
       能用固定 fixture 断言精确解析结果，而不是断言"在这台机器上跑起来没炸"。
#>

Set-StrictMode -Version Latest

$scriptAutomationLibrary = Join-Path $PSScriptRoot 'ScriptAutomation.ps1'
if (Test-Path -LiteralPath $scriptAutomationLibrary -PathType Leaf) {
    . $scriptAutomationLibrary
}

# 内核 OOM 杀进程时写入 ring buffer 的三种行首/行内标记。三条都保留：不同内核版本与不同触发路径
# （全局 OOM、cgroup OOM、oom_reaper）用词不同，只匹配其中一条会漏掉另外两类。
$script:NervRuntimeMemoryOomMarkers = @(
    'Out of memory',
    'oom-kill',
    'Killed process'
)

function Get-NervRuntimeMemoryFileText {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            return [pscustomobject]@{ status = 'unavailable'; reason = "not present: $Path"; text = $null }
        }
        return [pscustomobject]@{ status = 'read'; reason = ''; text = [IO.File]::ReadAllText($Path) }
    }
    catch {
        return [pscustomobject]@{ status = 'unavailable'; reason = "read failed: $($_.Exception.Message)"; text = $null }
    }
}

function ConvertTo-NervRuntimeMemoryValue {
    <#
        cgroup v2 的 memory.max 可以是字面量 `max`（无上限）。把它悄悄转成 0 或者让转换抛错都会
        制造误导，因此非数字一律原样保留。
    #>
    param([AllowNull()] [string] $Text)

    $trimmed = ([string]$Text).Trim()
    if ([string]::IsNullOrEmpty($trimmed)) { return $null }
    $parsed = [int64] 0
    if ([int64]::TryParse($trimmed, [ref] $parsed)) { return $parsed }
    return $trimmed
}

function Get-NervRuntimeMeminfo {
    param([Parameter(Mandatory)] [string] $ProcRoot)

    $read = Get-NervRuntimeMemoryFileText -Path (Join-Path $ProcRoot 'meminfo')
    if (-not [string]::Equals([string]$read.status, 'read', [StringComparison]::Ordinal)) {
        return [ordered]@{ status = 'unavailable'; reason = [string]$read.reason }
    }

    $wanted = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('MemTotal', 'MemFree', 'MemAvailable', 'SwapTotal', 'SwapFree'),
        [StringComparer]::Ordinal)
    $values = [ordered]@{ status = 'read'; reason = '' }
    foreach ($line in ([string]$read.text -split "`r?`n")) {
        $separatorIndex = $line.IndexOf(':', [StringComparison]::Ordinal)
        if ($separatorIndex -lt 1) { continue }
        $key = $line.Substring(0, $separatorIndex).Trim()
        if (-not $wanted.Contains($key)) { continue }
        # 单位统一为 kB：/proc/meminfo 的这几项一律以 kB 计，行尾的 `kB` 是噪声不是信息。
        $rawValue = $line.Substring($separatorIndex + 1).Trim()
        $spaceIndex = $rawValue.IndexOf(' ', [StringComparison]::Ordinal)
        if ($spaceIndex -ge 0) { $rawValue = $rawValue.Substring(0, $spaceIndex) }
        $values["${key}Kb"] = ConvertTo-NervRuntimeMemoryValue -Text $rawValue
    }

    return $values
}

function Get-NervRuntimeVmstat {
    <#
        全局 OOM 击杀计数。

        这不是 cgroup 计数的重复项，而是补它的盲区：hosted runner 的 slice 上 `memory.max` 是
        `max`（无上限），所以真发生的是全局 OOM，而 `memory.events.oom_kill` 只统计「因本 cgroup
        上限而触发」的击杀——那种情况下它会一直是 0。`/proc/vmstat` 的 `oom_kill` 是内核全局计数，
        world-readable，不需要 `dmesg` 的权限（hosted runner 通常 `kernel.dmesg_restrict=1`，
        `dmesg` 会被直接拒绝）。因此它才是「那一次击杀确实发生过」的可得见证者。

        判读方式是**前后快照的差值**，不是绝对值：这个计数自开机起累加，只看某一次读数说明不了
        任何事。
    #>
    param([Parameter(Mandatory)] [string] $ProcRoot)

    $read = Get-NervRuntimeMemoryFileText -Path (Join-Path $ProcRoot 'vmstat')
    if (-not [string]::Equals([string]$read.status, 'read', [StringComparison]::Ordinal)) {
        return [ordered]@{ status = 'unavailable'; reason = [string]$read.reason }
    }

    $wanted = [Collections.Generic.HashSet[string]]::new([string[]]@('oom_kill'), [StringComparer]::Ordinal)
    $values = [ordered]@{ status = 'read'; reason = '' }
    foreach ($line in ([string]$read.text -split "`r?`n")) {
        $parts = @($line.Trim() -split '\s+' | Where-Object { -not [string]::IsNullOrEmpty($_) })
        if ($parts.Count -ne 2) { continue }
        $key = [string]$parts[0]
        if (-not $wanted.Contains($key)) { continue }
        $values[$key] = ConvertTo-NervRuntimeMemoryValue -Text ([string]$parts[1])
    }

    return $values
}

function Get-NervRuntimeCgroupPath {
    param([Parameter(Mandatory)] [string] $ProcRoot)

    $read = Get-NervRuntimeMemoryFileText -Path (Join-Path $ProcRoot 'self/cgroup')
    if (-not [string]::Equals([string]$read.status, 'read', [StringComparison]::Ordinal)) { return $null }
    foreach ($line in ([string]$read.text -split "`r?`n")) {
        # cgroup v2 的统一层级永远是 `0::<path>`；v1 的多控制器行不是本库要读的东西。
        if (-not $line.StartsWith('0::', [StringComparison]::Ordinal)) { continue }
        $relative = $line.Substring(3).Trim()
        if ([string]::IsNullOrEmpty($relative)) { return $null }
        return $relative.TrimStart('/')
    }
    return $null
}

function Get-NervRuntimeCgroupMemory {
    param(
        [Parameter(Mandatory)] [string] $ProcRoot,
        [Parameter(Mandatory)] [string] $CgroupRoot
    )

    $relative = Get-NervRuntimeCgroupPath -ProcRoot $ProcRoot
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrEmpty($relative)) { $candidates.Add((Join-Path $CgroupRoot $relative)) }
    # runner 进程未必待在自己的 slice 里；根层级是有意义的兜底，而不是猜测。
    $candidates.Add($CgroupRoot)

    foreach ($directory in $candidates) {
        $currentRead = Get-NervRuntimeMemoryFileText -Path (Join-Path $directory 'memory.current')
        if (-not [string]::Equals([string]$currentRead.status, 'read', [StringComparison]::Ordinal)) { continue }

        $values = [ordered]@{
            status = 'read'
            reason = ''
            scope = $directory
            currentBytes = ConvertTo-NervRuntimeMemoryValue -Text $currentRead.text
        }
        foreach ($file in @('memory.max', 'memory.peak', 'memory.high')) {
            $read = Get-NervRuntimeMemoryFileText -Path (Join-Path $directory $file)
            $key = $file.Replace('memory.', '')
            $values[$key] = if ([string]::Equals([string]$read.status, 'read', [StringComparison]::Ordinal)) {
                ConvertTo-NervRuntimeMemoryValue -Text $read.text
            }
            else { 'unavailable' }
        }

        # memory.events 的 oom / oom_kill 计数只在本 cgroup 设了上限时才会动；无上限的 slice 上
        # 它恒为 0，真正的见证者是 Get-NervRuntimeVmstat。两者都留：有上限的环境里前者更精确地
        # 指认「是这个 cgroup 被限死」，后者只说明「机器上发生过全局击杀」。
        $eventsRead = Get-NervRuntimeMemoryFileText -Path (Join-Path $directory 'memory.events')
        $events = [ordered]@{ status = 'unavailable'; reason = [string]$eventsRead.reason }
        if ([string]::Equals([string]$eventsRead.status, 'read', [StringComparison]::Ordinal)) {
            $events = [ordered]@{ status = 'read'; reason = '' }
            foreach ($line in ([string]$eventsRead.text -split "`r?`n")) {
                $parts = @($line.Trim() -split '\s+' | Where-Object { -not [string]::IsNullOrEmpty($_) })
                if ($parts.Count -ne 2) { continue }
                $events[[string]$parts[0]] = ConvertTo-NervRuntimeMemoryValue -Text ([string]$parts[1])
            }
        }
        $values['events'] = $events

        return $values
    }

    return [ordered]@{ status = 'unavailable'; reason = "no readable memory.current under $CgroupRoot" }
}

function Get-NervRuntimeMemorySnapshot {
    <#
        一次内存维度快照。永不抛错：读不到的部分记 unavailable 和原因。
    #>
    param(
        [Parameter(Mandatory)] [string] $Phase,
        [string] $ProcRoot = '/proc',
        [string] $CgroupRoot = '/sys/fs/cgroup'
    )

    $snapshot = [ordered]@{
        phase = $Phase
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        platform = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    }

    try {
        if (-not (Test-Path -LiteralPath $ProcRoot -PathType Container)) {
            $snapshot['meminfo'] = [ordered]@{ status = 'unavailable'; reason = "not present: $ProcRoot" }
            $snapshot['vmstat'] = [ordered]@{ status = 'unavailable'; reason = "not present: $ProcRoot" }
            $snapshot['cgroup'] = [ordered]@{ status = 'unavailable'; reason = "not present: $ProcRoot" }
            return $snapshot
        }
        $snapshot['meminfo'] = Get-NervRuntimeMeminfo -ProcRoot $ProcRoot
        $snapshot['vmstat'] = Get-NervRuntimeVmstat -ProcRoot $ProcRoot
        $snapshot['cgroup'] = Get-NervRuntimeCgroupMemory -ProcRoot $ProcRoot -CgroupRoot $CgroupRoot
    }
    catch {
        # 取证失败只是少一条证据，不是被测对象的失败。
        $snapshot['meminfo'] = [ordered]@{ status = 'unavailable'; reason = "snapshot failed: $($_.Exception.Message)" }
        $snapshot['vmstat'] = [ordered]@{ status = 'unavailable'; reason = "snapshot failed: $($_.Exception.Message)" }
        $snapshot['cgroup'] = [ordered]@{ status = 'unavailable'; reason = "snapshot failed: $($_.Exception.Message)" }
    }

    return $snapshot
}

function Get-NervRuntimeKernelOomEvidence {
    <#
        失败时才采：读内核 ring buffer 里的 OOM 行。hosted runner 上 `dmesg` 常常是无权限的，
        那种情况必须记成 unavailable 而不是失败。
    #>
    param(
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [int] $MaximumLines = 20,
        # 默认值直接写成 { … } 字面量，而不是在函数体里挑一个：库作用域的动态调用只认「本作用域可证明
        # 是 script block」的变量，`$runner = if (…) {…} else {…}` 那种形状证明不了。
        [scriptblock] $CommandRunner = {
            param($directory)
            Invoke-NativeCommandOutput `
                -Command 'dmesg' `
                -Arguments @('--kernel', '--notime') `
                -WorkingDirectory $directory `
                -TimeoutSeconds 20 `
                -Name 'full-chain-kernel-oom-evidence'
        }
    )

    try {
        $result = & $CommandRunner $WorkingDirectory
        $lines = @(([string]$result.Stdout -split "`r?`n") | Where-Object {
            $line = $_
            @($script:NervRuntimeMemoryOomMarkers | Where-Object { $line.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
        })
        $matched = @($lines | Select-Object -Last $MaximumLines | ForEach-Object { Protect-ScriptAutomationText ([string]$_).Trim() })
        return [ordered]@{
            status = 'read'
            reason = ''
            matchedLineCount = $lines.Count
            retainedLines = @($matched)
        }
    }
    catch {
        return [ordered]@{
            status = 'unavailable'
            reason = "kernel log unreadable: $($_.Exception.Message)"
            matchedLineCount = 0
            retainedLines = @()
        }
    }
}
