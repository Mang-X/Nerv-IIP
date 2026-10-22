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
默认执行现有发现式补集；ScriptPath 模式执行调用方给出的原命令，供 workflow 组织稳定 suite。
每个脚本仍是独立、有界的 PowerShell 子进程。累计全部失败后 exit 1，后续成功不能覆盖失败。
#>

[CmdletBinding()]
param(
    [ValidateRange(1, 2147483)] [int] $TimeoutSeconds = 300,
    [ValidateRange(1, 2147483647)] [int] $FailureOutputLineCount = 40,
    [string[]] $ScriptPath,
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/ScriptTestSelection.ps1')

if ($PSBoundParameters.ContainsKey('ScriptPath')) {
    if ($ScriptPath.Count -eq 0) { throw 'An explicit script suite must not be empty.' }
    $selected = @($ScriptPath)
    $selectionSummary = "explicit=$($selected.Count)"
}
else {
    $plan = Get-NervScriptTestSelectionPlan -RepositoryRoot $repoRoot
    $selected = @($plan.RunnerSelected | ForEach-Object { "scripts/tests/$_" })
    $selectionSummary = "discovered=$($plan.All.Count) workflow=$($plan.WorkflowSelected.Count) out-of-band=$($plan.OutOfBand.Count) complement=$($selected.Count)"
    foreach ($entry in $plan.OutOfBand) {
        Write-Host "  [$($entry.Kind)] $($entry.Name) — $($entry.Reason)"
    }
}
Write-Host "Script contract suite selection: $selectionSummary"
foreach ($path in $selected) { Write-Host "  selected $path" }

$failures = [Collections.Generic.List[string]]::new()
$passedCount = 0
$runRoot = New-ScriptAutomationLogDirectory -Name 'script-contract-tests'

foreach ($path in $selected) {
    $name = $path
    $testPath = Join-Path $repoRoot $path
    $logDirectory = Join-Path $runRoot ([IO.Path]::GetFileNameWithoutExtension($path))
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        # -NonInteractive matters: a test whose parameters cannot be bound would otherwise prompt and
        # look like a hang with no output at all instead of failing with its real reason.
        Invoke-NativeCommandWithTimeout `
            -Command 'pwsh' `
            -Arguments @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $testPath) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds $TimeoutSeconds `
            -Name "script-contract-$([IO.Path]::GetFileNameWithoutExtension($path))" `
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
Write-Host "Script contract suite: passed=$passedCount failed=$($failures.Count) $selectionSummary"
if ($failures.Count -gt 0) {
    Write-Host "Failed script contract tests: $($failures -join ', ')"
    exit 1
}
exit 0
