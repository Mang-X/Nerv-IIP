# Script-Governance:
#   Category: check
#   SideEffects:
#     - Parses scripts/run-full-chain-test-lane.ps1 and drives the memory-evidence library against synthetic kernel files
#   Writes:
#     - Temporary /proc and cgroup fixtures plus a mutated library copy under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#   Requires:
#     - PowerShell 7

# #1664 / #1877 的契约面。两件事各自被守住：
#
# 1. 解析正确：用合成的 /proc 与 cgroup fixture 断言精确读数，而不是断言"在这台机器上跑起来没炸"
#    ——后者在 macOS 上永远是 unavailable，等于什么都没测。
# 2. best-effort 名副其实：读不到只记 unavailable，绝不抛错。采证脚本把被测 lane 弄红，等于用一个
#    新的假红换一个旧的真红。
#
# 采集点落在哪里用 AST 断言，不用字面量匹配：after 快照必须在 finally 里（entrypoint 被杀那一刻的
# 读数正是本票要的证据），内核 OOM 取证必须在 catch 里。变异对照在文件末尾。

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/RuntimeMemoryEvidence.ps1'
$runnerPath = Join-Path $repoRoot 'scripts/run-full-chain-test-lane.ps1'
. $libraryPath

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-memory-evidence-$([Guid]::NewGuid().ToString('N'))"

function New-FixtureFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Content
    )

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Test-NervAstNodeInsideRegion {
    param(
        [Parameter(Mandatory)] [System.Management.Automation.Language.Ast] $Node,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Regions
    )

    foreach ($region in $Regions) {
        if ($null -eq $region) { continue }
        if ($region.Extent.StartOffset -le $Node.Extent.StartOffset -and
            $Node.Extent.EndOffset -le $region.Extent.EndOffset) {
            return $true
        }
    }

    return $false
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    # --- 合成 /proc + cgroup fixture ---------------------------------------------------------------

    $procRoot = Join-Path $temporaryRoot 'proc'
    $cgroupRoot = Join-Path $temporaryRoot 'cgroup'
    New-FixtureFile -Path (Join-Path $procRoot 'meminfo') -Content @'
MemTotal:       16384000 kB
MemFree:          131072 kB
MemAvailable:    2097152 kB
Buffers:           65536 kB
SwapTotal:       4194304 kB
SwapFree:        4194304 kB
'@
    New-FixtureFile -Path (Join-Path $procRoot 'vmstat') -Content @'
nr_free_pages 1558134
pgmajfault 4211
oom_kill 7
pgpgin 992
'@
    New-FixtureFile -Path (Join-Path $procRoot 'self/cgroup') -Content "0::/actions_job`n"
    $jobCgroup = Join-Path $cgroupRoot 'actions_job'
    New-FixtureFile -Path (Join-Path $jobCgroup 'memory.current') -Content "6900000000`n"
    New-FixtureFile -Path (Join-Path $jobCgroup 'memory.peak') -Content "7100000000`n"
    New-FixtureFile -Path (Join-Path $jobCgroup 'memory.max') -Content "max`n"
    New-FixtureFile -Path (Join-Path $jobCgroup 'memory.events') -Content @'
low 0
high 0
max 12
oom 3
oom_kill 1
'@

    $snapshot = Get-NervRuntimeMemorySnapshot -Phase 'before-entrypoint' -ProcRoot $procRoot -CgroupRoot $cgroupRoot
    Assert-Contract ([string]::Equals([string]$snapshot.phase, 'before-entrypoint', [StringComparison]::Ordinal)) 'A snapshot must record the phase it was taken in.'
    Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$snapshot.capturedAtUtc)) 'A snapshot must record when it was taken.'

    Assert-Contract ([string]::Equals([string]$snapshot.meminfo.status, 'read', [StringComparison]::Ordinal)) 'The meminfo fixture must be read.'
    Assert-Contract ($snapshot.meminfo.MemTotalKb -eq 16384000) 'MemTotal must be parsed in kB without its unit suffix.'
    Assert-Contract ($snapshot.meminfo.MemAvailableKb -eq 2097152) 'MemAvailable is the headroom figure #1664 lacked; it must be parsed.'
    Assert-Contract ($snapshot.meminfo.SwapFreeKb -eq 4194304) 'Swap figures must be parsed.'
    # 键名放进变量再查：`.Contains('字面量')` 会被序数门禁判为歧义方法调用。
    $undeclaredMeminfoKey = 'BuffersKb'
    Assert-Contract (-not $snapshot.meminfo.Contains($undeclaredMeminfoKey)) 'Only the declared meminfo keys may be retained; an open-ended dump makes the summary unreviewable.'

    # hosted runner 的 slice 上 memory.max 是 max，cgroup 的 oom_kill 因此恒为 0；全局击杀只有
    # /proc/vmstat 见证得到，而它不需要 dmesg 权限。缺了这一项，真发生 OOM 时会拿不到直接证据。
    Assert-Contract ([string]::Equals([string]$snapshot.vmstat.status, 'read', [StringComparison]::Ordinal)) 'The vmstat fixture must be read.'
    Assert-Contract ($snapshot.vmstat.oom_kill -eq 7) 'The global oom_kill counter must be parsed; it is the only permission-free witness of an unlimited-cgroup OOM.'
    $undeclaredVmstatKey = 'pgmajfault'
    Assert-Contract (-not $snapshot.vmstat.Contains($undeclaredVmstatKey)) 'Only the declared vmstat key may be retained.'

    Assert-Contract ([string]::Equals([string]$snapshot.cgroup.status, 'read', [StringComparison]::Ordinal)) 'The cgroup fixture must be read.'
    Assert-Contract ($snapshot.cgroup.currentBytes -eq 6900000000) 'memory.current must be parsed as a number.'
    Assert-Contract ($snapshot.cgroup.peak -eq 7100000000) 'memory.peak must be parsed as a number.'
    # 把 `max` 悄悄转成 0 会读成"上限为零"，比缺这条证据更糟。
    Assert-Contract ([string]::Equals([string]$snapshot.cgroup.max, 'max', [StringComparison]::Ordinal)) 'A literal cgroup limit of max must be preserved verbatim.'
    Assert-Contract ([string]::Equals([string]$snapshot.cgroup.high, 'unavailable', [StringComparison]::Ordinal)) 'A missing cgroup file must be reported as unavailable rather than omitted.'
    Assert-Contract ([string]::Equals([string]$snapshot.cgroup.events.status, 'read', [StringComparison]::Ordinal)) 'memory.events must be read.'
    Assert-Contract ($snapshot.cgroup.events.oom -eq 3 -and $snapshot.cgroup.events.oom_kill -eq 1) 'The oom and oom_kill counters are the whole point of this evidence; they must be parsed.'

    # 兜底：进程自己的 slice 里没有 memory.current 时退到根层级，而不是直接放弃。
    $rootOnlyCgroup = Join-Path $temporaryRoot 'cgroup-root-only'
    New-FixtureFile -Path (Join-Path $rootOnlyCgroup 'memory.current') -Content "512`n"
    $fallback = Get-NervRuntimeMemorySnapshot -Phase 'fallback' -ProcRoot $procRoot -CgroupRoot $rootOnlyCgroup
    Assert-Contract ($fallback.cgroup.currentBytes -eq 512) 'The root cgroup must be used when the process slice has no readable memory.current.'

    # --- best-effort：读不到只记原因，绝不抛错 -----------------------------------------------------

    $absent = Get-NervRuntimeMemorySnapshot -Phase 'absent' -ProcRoot (Join-Path $temporaryRoot 'no-such-proc') -CgroupRoot (Join-Path $temporaryRoot 'no-such-cgroup')
    Assert-Contract ([string]::Equals([string]$absent.meminfo.status, 'unavailable', [StringComparison]::Ordinal)) 'A missing /proc must yield unavailable meminfo.'
    Assert-Contract ([string]::Equals([string]$absent.vmstat.status, 'unavailable', [StringComparison]::Ordinal)) 'A missing /proc must yield unavailable vmstat.'
    Assert-Contract ([string]::Equals([string]$absent.cgroup.status, 'unavailable', [StringComparison]::Ordinal)) 'A missing cgroup root must yield unavailable cgroup evidence.'
    Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$absent.meminfo.reason)) 'An unavailable reading must say why.'

    $emptyProc = Join-Path $temporaryRoot 'empty-proc'
    [IO.Directory]::CreateDirectory($emptyProc) | Out-Null
    $partial = Get-NervRuntimeMemorySnapshot -Phase 'partial' -ProcRoot $emptyProc -CgroupRoot $cgroupRoot
    Assert-Contract ([string]::Equals([string]$partial.meminfo.status, 'unavailable', [StringComparison]::Ordinal)) 'A present but empty /proc must still degrade cleanly.'

    # --- 内核 OOM 取证 ----------------------------------------------------------------------------

    $kernelLog = @'
[  912.001] usb 1-1: new high-speed USB device
[  913.100] Out of memory: Killed process 4242 (dotnet) total-vm:9000000kB
[  913.101] oom-kill:constraint=CONSTRAINT_NONE,nodemask=(null)
[  913.102] Killed process 4243 (testhost) total-vm:100000kB
[  914.000] something unrelated
'@
    $oomEvidence = Get-NervRuntimeKernelOomEvidence -WorkingDirectory $repoRoot -CommandRunner {
        param($directory)
        return [pscustomobject]@{ Stdout = $kernelLog }
    }.GetNewClosure()
    Assert-Contract ([string]::Equals([string]$oomEvidence.status, 'read', [StringComparison]::Ordinal)) 'A readable kernel log must be reported as read.'
    Assert-Contract ($oomEvidence.matchedLineCount -eq 3) 'All three OOM markers must match: different kernels and trigger paths word it differently.'
    Assert-Contract (@($oomEvidence.retainedLines | Where-Object { $_.IndexOf('unrelated', [StringComparison]::Ordinal) -ge 0 }).Count -eq 0) 'Unrelated kernel lines must not be retained.'

    $capped = Get-NervRuntimeKernelOomEvidence -WorkingDirectory $repoRoot -MaximumLines 2 -CommandRunner {
        param($directory)
        return [pscustomobject]@{ Stdout = $kernelLog }
    }.GetNewClosure()
    Assert-Contract ($capped.matchedLineCount -eq 3 -and @($capped.retainedLines).Count -eq 2) 'The retained-line cap must bound the artifact without hiding how many lines actually matched.'

    $denied = Get-NervRuntimeKernelOomEvidence -WorkingDirectory $repoRoot -CommandRunner {
        param($directory)
        throw 'dmesg: read kernel buffer failed: Operation not permitted'
    }
    Assert-Contract ([string]::Equals([string]$denied.status, 'unavailable', [StringComparison]::Ordinal)) 'An unreadable kernel buffer must degrade to unavailable; hosted runners routinely deny dmesg.'
    Assert-Contract ($denied.reason.IndexOf('Operation not permitted', [StringComparison]::Ordinal) -ge 0) 'The unavailable reason must carry the underlying failure.'
    Assert-Contract (@($denied.retainedLines).Count -eq 0) 'An unavailable reading must not fabricate lines.'

    # --- 采集点：用 AST 断言，而不是字面量 --------------------------------------------------------

    $parseErrors = $null
    $runnerAst = [System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref] $null, [ref] $parseErrors)
    Assert-Contract (@($parseErrors).Count -eq 0) 'The FullChain runner must parse cleanly.'

    $tryStatements = @($runnerAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.TryStatementAst] }, $true))
    $finallyBlocks = @($tryStatements | ForEach-Object { $_.Finally } | Where-Object { $null -ne $_ })
    $catchBlocks = @($tryStatements | ForEach-Object { $_.CatchClauses } | Where-Object { $null -ne $_ })

    $assignments = @($runnerAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
    function Get-RunnerAssignment {
        param([Parameter(Mandatory)] [string] $LeftFragment)

        $matched = @($assignments | Where-Object { $_.Left.Extent.Text.IndexOf($LeftFragment, [StringComparison]::Ordinal) -ge 0 })
        Assert-Contract ($matched.Count -eq 1) "The FullChain runner must assign '$LeftFragment' exactly once; found $($matched.Count)."
        return $matched[0]
    }

    $beforeAssignment = Get-RunnerAssignment -LeftFragment 'memory.beforeEntrypoint'
    $afterAssignment = Get-RunnerAssignment -LeftFragment 'memory.afterEntrypoint'
    $oomAssignment = Get-RunnerAssignment -LeftFragment 'memory.kernelOomEvidence'

    Assert-Contract (-not (Test-NervAstNodeInsideRegion -Node $beforeAssignment -Regions $finallyBlocks)) 'The before-entrypoint snapshot must be taken on the way in, not in a finally.'
    # entrypoint 被 SIGKILL 时不会走成功路径；after 快照落在 finally 之外就正好错过 #1664 那一刻。
    Assert-Contract (Test-NervAstNodeInsideRegion -Node $afterAssignment -Regions $finallyBlocks) 'The after-entrypoint snapshot must live in a finally so a killed entrypoint still leaves a reading.'
    Assert-Contract (Test-NervAstNodeInsideRegion -Node $oomAssignment -Regions $catchBlocks) 'Kernel OOM evidence must be collected on the member failure path.'
    Assert-Contract ($beforeAssignment.Extent.StartOffset -lt $afterAssignment.Extent.StartOffset) 'The before snapshot must precede the after snapshot.'

    $runnerText = [IO.File]::ReadAllText($runnerPath)
    Assert-Contract ($runnerText.IndexOf("lib/RuntimeMemoryEvidence.ps1", [StringComparison]::Ordinal) -ge 0) 'The FullChain runner must dot-source the memory evidence library.'
    # #3135 把 residual 覆盖段加进同一份 summary，schemaVersion 因此由 3 升到 4；本断言的语义是
    # 「往 dependency summary 加字段必须升版本」，锚点随之前移，不是放松。
    Assert-Contract ($runnerText.IndexOf('schemaVersion = 4', [StringComparison]::Ordinal) -ge 0) 'Adding memory evidence to the dependency summary must bump its schema version.'

    # --- 变异对照 ---------------------------------------------------------------------------------

    $libraryText = [IO.File]::ReadAllText($libraryPath)

    function Invoke-MutationControl {
        param(
            [Parameter(Mandatory)] [string] $Label,
            [Parameter(Mandatory)] [string[]] $Anchors,
            [Parameter(Mandatory)] [string[]] $Replacements,
            [Parameter(Mandatory)] [string] $Probe
        )

        Assert-Contract ($Anchors.Count -eq $Replacements.Count) "Mutation '$Label' must pair every anchor with a replacement."
        $mutatedText = $libraryText
        for ($index = 0; $index -lt $Anchors.Count; $index++) {
            $occurrences = ([regex]::Matches($mutatedText, [regex]::Escape($Anchors[$index]))).Count
            Assert-Contract ($occurrences -eq 1) "Mutation anchor $index of '$Label' must match exactly once; a moved anchor silently turns this control into a no-op."
            $mutatedText = $mutatedText.Replace($Anchors[$index], $Replacements[$index])
        }

        $mutatedLibrary = Join-Path $temporaryRoot "RuntimeMemoryEvidence.$Label.ps1"
        [IO.File]::WriteAllText($mutatedLibrary, $mutatedText, [Text.UTF8Encoding]::new($false))
        $probePath = Join-Path $temporaryRoot "mutation-probe-$Label.ps1"
        [IO.File]::WriteAllText($probePath, ". '$mutatedLibrary'`n$Probe", [Text.UTF8Encoding]::new($false))

        $run = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $probePath) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name "memory-evidence-mutation-$Label"
        return [string] $run.Stdout
    }

    # 变异 1：解析时忽略 MemAvailable——上面的 golden vector 必须因此转红。
    $mutatedParse = Invoke-MutationControl `
        -Label 'meminfo' `
        -Anchors @("'MemTotal', 'MemFree', 'MemAvailable', 'SwapTotal', 'SwapFree'") `
        -Replacements @("'MemTotal', 'MemFree', 'SwapTotal', 'SwapFree'") `
        -Probe @"
`$snapshot = Get-NervRuntimeMemorySnapshot -Phase 'mutation' -ProcRoot '$procRoot' -CgroupRoot '$cgroupRoot'
`$key = 'MemAvailableKb'
if (`$snapshot.meminfo.Contains(`$key)) { Write-Host 'MUTATION-SURVIVED' } else { Write-Host 'MUTATION-KILLED' }
"@
    Assert-Contract ($mutatedParse.IndexOf('MUTATION-KILLED', [StringComparison]::Ordinal) -ge 0) 'Dropping MemAvailable must change the parsed result; otherwise the golden vector above measures nothing.'

    # 变异 2：让"读不到"变成抛错。best-effort 由两处共同兑现——就地降级的那一支，和兜住意外的
    # 那张网——所以两处都要拆掉，否则剩下的一处会把变异吞掉，控制组读成假绿。
    $mutatedBestEffort = Invoke-MutationControl `
        -Label 'besteffort' `
        -Anchors @(
            '            $snapshot[''meminfo''] = [ordered]@{ status = ''unavailable''; reason = "not present: $ProcRoot" }',
            '        # 取证失败只是少一条证据，不是被测对象的失败。'
        ) `
        -Replacements @(
            '            throw "not present: $ProcRoot"',
            '        throw'
        ) `
        -Probe @"
try {
    `$snapshot = Get-NervRuntimeMemorySnapshot -Phase 'mutation' -ProcRoot '$(Join-Path $temporaryRoot 'no-such-proc')' -CgroupRoot '$cgroupRoot'
    if ([string]::Equals([string]`$snapshot.meminfo.status, 'unavailable', [StringComparison]::Ordinal)) { Write-Host 'MUTATION-SURVIVED' } else { Write-Host 'MUTATION-KILLED' }
}
catch { Write-Host 'MUTATION-KILLED' }
"@
    Assert-Contract ($mutatedBestEffort.IndexOf('MUTATION-KILLED', [StringComparison]::Ordinal) -ge 0) 'Removing the unavailable path must change the observed behaviour; otherwise the best-effort assertions measure nothing.'

    # 变异 3：不解析全局 oom_kill——这正是我在 hosted runner 真实读数里发现的那个盲区，
    # 拿掉它整套证据就退回"只能看余量、看不见击杀"。
    $mutatedVmstat = Invoke-MutationControl `
        -Label 'vmstat' `
        -Anchors @("[string[]]@('oom_kill')") `
        -Replacements @("[string[]]@()") `
        -Probe @"
`$snapshot = Get-NervRuntimeMemorySnapshot -Phase 'mutation' -ProcRoot '$procRoot' -CgroupRoot '$cgroupRoot'
`$key = 'oom_kill'
if (`$snapshot.vmstat.Contains(`$key)) { Write-Host 'MUTATION-SURVIVED' } else { Write-Host 'MUTATION-KILLED' }
"@
    Assert-Contract ($mutatedVmstat.IndexOf('MUTATION-KILLED', [StringComparison]::Ordinal) -ge 0) 'Dropping the global oom_kill counter must change the parsed result.'

    Write-Host 'FullChain memory evidence contract passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
