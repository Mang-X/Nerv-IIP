# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs every discovered scripts/tests contract test as a bounded child PowerShell process
#   Writes:
#     - artifacts/script-logs/** through ScriptAutomation
#   Cleanup:
#     - Stops managed child process trees through ScriptAutomation when a test exceeds its budget
#   Requires:
#     - PowerShell 7
#     - Ruby with the yaml and json standard libraries

<#
#3300 的发现式 runner。

它不持有任何测试名单：要跑什么由 scripts/lib/ScriptTestSelection.ps1 从
`glob(scripts/tests/*.Tests.ps1) - 工作流点名 - 显式出界登记` 算出来。因此新增一个契约测试
的默认归宿是「被这一步跑到」，而不是「静默不跑」。

失败即 exit 1，并把每条失败测试的 stdout/stderr 末尾打出来。本脚本按 CI 约定独占一个 run step：
`exit` 版的退出码只有在独占 step 时才不会被后续命令吞掉。
#>

[CmdletBinding()]
param(
    # 单个测试的上限，不是整步的上限。必须**明显小于** ci.yml 给本步的 step 预算（当前 15m），
    # 否则一个挂死的测试会把预算吃光、由 GitHub 的 step timeout 收场，而 GitHub 杀步时不会打印
    # 本 runner 的 FAIL 诊断与该测试的 stdout/stderr 尾巴 —— 最需要诊断的那次反而什么都拿不到。
    # 300s 的来源：CI 上单测试实测最大 41.1s（erp-sales-order-demand-planning-verify-script），
    # 取其约 7 倍；一次挂死后仍剩约 10m，足够跑完其余测试并由本 runner 自己报红。
    [ValidateRange(1, 2147483)] [int] $TimeoutSeconds = 300,
    [int] $FailureOutputLineCount = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/ScriptTestSelection.ps1')

$plan = Get-NervScriptTestSelectionPlan -RepositoryRoot $repoRoot

Write-Host "Script contract test selection (#3300 complement selection):"
Write-Host "  discovered under scripts/tests: $($plan.All.Count)"
Write-Host "  selected by a workflow run body: $($plan.WorkflowSelected.Count)"
Write-Host "  registered out of band: $($plan.OutOfBand.Count)"
foreach ($entry in $plan.OutOfBand) {
    $entryTracking = Get-NervScriptTestRegistryField -Entry $entry -Name 'Tracking'
    $tracking = if ([string]::IsNullOrWhiteSpace($entryTracking)) { '' } else { " (tracking $entryTracking)" }
    Write-Host "    [$($entry.Kind)]$tracking $($entry.Name) — $($entry.Reason)"
}
$quarantined = @($plan.OutOfBand | Where-Object { [string]::Equals((Get-NervScriptTestRegistryField -Entry $_ -Name 'Kind'), 'quarantine', [StringComparison]::Ordinal) })
Write-Host "  of which quarantined against a tracking issue: $($quarantined.Count)"
foreach ($entry in $quarantined) {
    Write-Host "    quarantined $(Get-NervScriptTestRegistryField -Entry $entry -Name 'Name') tracking $(Get-NervScriptTestRegistryField -Entry $entry -Name 'Tracking')"
}
Write-Host "  selected by this discovery runner: $($plan.RunnerSelected.Count)"

if ($plan.RunnerSelected.Count -eq 0) {
    Write-Host 'The discovery runner selected no test. Every scripts/tests contract test is either named by a workflow step or registered out of band.'
    exit 0
}

$failures = [Collections.Generic.List[string]]::new()
$passedCount = 0
$runRoot = New-ScriptAutomationLogDirectory -Name 'script-contract-tests'

foreach ($name in $plan.RunnerSelected) {
    $scriptPath = Join-Path (Join-Path $repoRoot 'scripts/tests') $name
    $logDirectory = Join-Path $runRoot ([IO.Path]::GetFileNameWithoutExtension($name))
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        # -NonInteractive matters: a test whose parameters cannot be bound would otherwise prompt and
        # look like a hang with no output at all instead of failing with its real reason.
        Invoke-NativeCommandWithTimeout `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds $TimeoutSeconds `
            -Name "script-contract-$([IO.Path]::GetFileNameWithoutExtension($name))" `
            -LogDirectory $logDirectory | Out-Null
        $stopwatch.Stop()
        $passedCount++
        Write-Host ("  PASS {0,8:N1}s  {1}" -f $stopwatch.Elapsed.TotalSeconds, $name)
    }
    catch {
        $stopwatch.Stop()
        $failures.Add($name)
        Write-Host ("  FAIL {0,8:N1}s  {1}" -f $stopwatch.Elapsed.TotalSeconds, $name)
        Write-Host "    $($_.Exception.Message)"
        foreach ($stream in @('stdout', 'stderr')) {
            $streamPath = Join-Path $logDirectory "$stream.log"
            if (-not (Test-Path -LiteralPath $streamPath -PathType Leaf)) { continue }
            $tail = @(Get-Content -LiteralPath $streamPath -Tail $FailureOutputLineCount)
            if ($tail.Count -eq 0) { continue }
            Write-Host "    --- $name $stream (last $($tail.Count) lines) ---"
            foreach ($line in $tail) { Write-Host "    $line" }
        }
    }
}

Write-Host ''
Write-Host "Script contract test discovery runner: passed=$passedCount skipped=$($plan.OutOfBand.Count) total=$($plan.All.Count) failed=$($failures.Count) (skipped = $(@($plan.OutOfBand).Count - $quarantined.Count) nested/excluded + $($quarantined.Count) quarantined)"

if ($failures.Count -gt 0) {
    Write-Host "Failed script contract tests: $($failures -join ', ')"
    exit 1
}

Write-Host "Discovered script contract tests passed: $passedCount of $($plan.All.Count) files, $($plan.WorkflowSelected.Count) run by named workflow steps, $($plan.OutOfBand.Count) registered out of band ($($quarantined.Count) quarantined against a tracking issue)."
exit 0
