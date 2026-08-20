# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts short-lived probe processes that terminate themselves with a signal
#   Writes:
#     - Temporary probe scripts, mutated library copies and command logs under the operating-system temp directory
#     - Shared script-automation command logs under artifacts/script-logs/** for probes whose entry point takes no log directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#     - Leaves artifacts/script-logs/** to the repository's existing artifact hygiene
#   Requires:
#     - PowerShell 7

# #1664 / #1876 的契约面：受管入口必须把「进程被信号杀掉」和「进程自己判定失败」区分开，并且这
# 个结论要能跨进程边界继承——真实故障里内层是被 SIGKILL 的 dotnet test，外层只看见 pwsh 退出 1。
#
# 本文件的每一条正例都配了变异对照：把 Get-ScriptAutomationSignalExit 改成恒返回 $null 之后，直接
# 分类与继承两条断言都必须转红。没有这一段就无法知道断言到底有没有鉴别力。

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1'
. $libraryPath

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-signal-exit-$([Guid]::NewGuid().ToString('N'))"

# 探针脚本：被测进程用自身 PID 给自己发信号，退出码因此是确定的 128 + signal，不依赖任何外部工具。
function New-SignalProbeArguments {
    param([Parameter(Mandatory)] [int] $Signal)

    return @('-c', "kill -$Signal `$`$")
}

function Get-ThrownMessage {
    param([Parameter(Mandatory)] [scriptblock] $Action)

    try {
        & $Action | Out-Null
    }
    catch {
        return [string] $_.Exception.Message
    }

    throw 'The probe was expected to fail but succeeded.'
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    # --- 分类本身：与平台约定一致，且不臆造名字 ---------------------------------------------------

    if ([OperatingSystem]::IsWindows()) {
        # Windows 退出码是任意 32 位值；把 137 读成 SIGKILL 会凭空造出一个 OOM 结论。
        Assert-Contract ($null -eq (Get-ScriptAutomationSignalExit -ExitCode 137)) 'Windows exit codes must never be classified as signal terminations.'
    }
    else {
        $sigkill = Get-ScriptAutomationSignalExit -ExitCode 137
        Assert-Contract ($null -ne $sigkill) 'Exit code 137 must be classified as a signal termination on Unix.'
        Assert-Contract ($sigkill.Signal -eq 9) 'Exit code 137 must resolve to signal 9.'
        Assert-Contract ([string]::Equals([string]$sigkill.SignalName, 'SIGKILL', [StringComparison]::Ordinal)) 'Signal 9 must be named SIGKILL.'
        Assert-Contract ($sigkill.Hint.IndexOf('out-of-memory', [StringComparison]::Ordinal) -ge 0) 'The SIGKILL hint must name the out-of-memory suspicion that #1664 could not read off the failure message.'

        $sigterm = Get-ScriptAutomationSignalExit -ExitCode 143
        Assert-Contract ([string]::Equals([string]$sigterm.SignalName, 'SIGTERM', [StringComparison]::Ordinal)) 'Exit code 143 must resolve to SIGTERM.'
        Assert-Contract ($sigterm.Hint.IndexOf('out-of-memory', [StringComparison]::Ordinal) -lt 0) 'Only SIGKILL may carry the out-of-memory suspicion.'

        $sigsegv = Get-ScriptAutomationSignalExit -ExitCode 139
        Assert-Contract ([string]::Equals([string]$sigsegv.SignalName, 'SIGSEGV', [StringComparison]::Ordinal)) 'Exit code 139 must resolve to SIGSEGV.'

        # SIGBUS/SIGUSR1/SIGUSR2 的编号在 Linux 与 macOS 之间不一致，因此只允许泛化命名。
        foreach ($portabilityUnsafe in @(7, 10, 12)) {
            $classified = Get-ScriptAutomationSignalExit -ExitCode (128 + $portabilityUnsafe)
            Assert-Contract ([string]::Equals([string]$classified.SignalName, "SIG$portabilityUnsafe", [StringComparison]::Ordinal)) "Signal $portabilityUnsafe must stay generically named because its number differs between Linux and macOS."
        }

        # 边界：128 本身不是信号终止，超出信号域的普通退出码也不是。
        Assert-Contract ($null -eq (Get-ScriptAutomationSignalExit -ExitCode 128)) 'Exit code 128 carries no signal.'
        Assert-Contract ($null -eq (Get-ScriptAutomationSignalExit -ExitCode 1)) 'Ordinary failures must not be classified as signal terminations.'
        Assert-Contract ($null -eq (Get-ScriptAutomationSignalExit -ExitCode 193)) 'Exit codes beyond the signal range must not be classified as signal terminations.'
    }

    if ([OperatingSystem]::IsWindows()) {
        Write-Host 'Signal-exit process probes are skipped on Windows: the 128 + signal convention does not exist there.'
        return
    }

    # --- Invoke-NativeCommandWithTimeout：直接被信号杀死 ------------------------------------------

    $timeoutLogDirectory = Join-Path $temporaryRoot 'with-timeout'
    $withTimeoutMessage = Get-ThrownMessage {
        Invoke-NativeCommandWithTimeout `
            -Command '/bin/sh' `
            -Arguments (New-SignalProbeArguments -Signal 9) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 30 `
            -Name 'signal-exit-with-timeout-probe' `
            -LogDirectory $timeoutLogDirectory
    }
    # 既有消费者（scripts/verify-backend-test-determinism.ps1 与两处 Tests）用
    # `exited with (?<exitCode>\d+)` 解析这条消息，前缀必须原样保留。
    Assert-Contract ($withTimeoutMessage -match 'exited with (?<exitCode>\d+)') 'The timed native failure message must keep the exit-code prefix its parsers depend on.'
    Assert-Contract ([string]::Equals([string]$Matches['exitCode'], '137', [StringComparison]::Ordinal)) 'A SIGKILLed probe must report exit code 137.'
    Assert-Contract ($withTimeoutMessage.IndexOf('Terminated by signal SIGKILL (9)', [StringComparison]::Ordinal) -ge 0) 'The timed native failure message must name the signal instead of leaving 137 unexplained.'
    Assert-Contract ($withTimeoutMessage.IndexOf('Logs: ', [StringComparison]::Ordinal) -ge 0) 'The timed native failure message must keep pointing at its logs.'

    # --- Invoke-NativeCommandOutput：同一条分类作用于第二条非零退出路径 --------------------------

    $outputMessage = Get-ThrownMessage {
        Invoke-NativeCommandOutput `
            -Command '/bin/sh' `
            -Arguments (New-SignalProbeArguments -Signal 9) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 30 `
            -Name 'signal-exit-output-probe'
    }
    Assert-Contract ($outputMessage.IndexOf('exited with 137', [StringComparison]::Ordinal) -ge 0) 'The output-reading failure message must keep the exit-code prefix.'
    Assert-Contract ($outputMessage.IndexOf('Terminated by signal SIGKILL (9)', [StringComparison]::Ordinal) -ge 0) 'Invoke-NativeCommandOutput must classify signal terminations on its own non-zero path.'

    # --- 普通失败不得被误报成信号终止 ------------------------------------------------------------

    $ordinaryMessage = Get-ThrownMessage {
        Invoke-NativeCommandOutput `
            -Command '/bin/sh' `
            -Arguments @('-c', 'exit 3') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 30 `
            -Name 'signal-exit-ordinary-probe'
    }
    Assert-Contract ($ordinaryMessage.IndexOf('exited with 3', [StringComparison]::Ordinal) -ge 0) 'Ordinary failures must keep reporting their exit code.'
    Assert-Contract ($ordinaryMessage.IndexOf('Terminated by signal', [StringComparison]::Ordinal) -lt 0) 'Ordinary failures must not be dressed up as signal terminations.'

    # --- 跨进程继承：复刻 #1664 的真实层次 -------------------------------------------------------
    #
    # 内层 /bin/sh 被 SIGKILL（137）→ 中层 pwsh 脚本因此抛错并以 1 退出 → 外层只看得见 1。
    # 外层必须复现内层的信号结论，否则就退回到 #1664 里那条无法诊断的 `exited with 1`。

    $innerScriptPath = Join-Path $temporaryRoot 'inner-signal-probe.ps1'
    $innerScript = @"
`$ErrorActionPreference = 'Stop'
. '$libraryPath'
Invoke-NativeCommandWithTimeout ``
    -Command '/bin/sh' ``
    -Arguments @('-c', 'kill -9 `$`$') ``
    -WorkingDirectory '$repoRoot' ``
    -TimeoutSeconds 30 ``
    -Name 'signal-exit-inner-probe' ``
    -LogDirectory '$(Join-Path $temporaryRoot 'inner')' | Out-Null
"@
    [IO.File]::WriteAllText($innerScriptPath, $innerScript, [Text.UTF8Encoding]::new($false))

    $outerMessage = Get-ThrownMessage {
        Invoke-PwshScript `
            -ScriptPath $innerScriptPath `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name 'signal-exit-outer-probe'
    }
    Assert-Contract ($outerMessage.IndexOf("Command 'pwsh' exited with 1", [StringComparison]::Ordinal) -ge 0) 'The outer failure must still report the exit code the child actually produced.'
    Assert-Contract ($outerMessage.IndexOf('A governed child process was terminated by a signal:', [StringComparison]::Ordinal) -ge 0) 'The outer failure must inherit the inner signal diagnosis instead of only reporting exit 1.'
    Assert-Contract ($outerMessage.IndexOf('NERV-SIGNAL-EXIT', [StringComparison]::Ordinal) -ge 0) 'The inherited diagnosis must carry the machine-readable marker.'
    Assert-Contract ($outerMessage.IndexOf('signalName=SIGKILL', [StringComparison]::Ordinal) -ge 0) 'The inherited diagnosis must name the signal that killed the deepest process.'
    Assert-Contract ($outerMessage.IndexOf('exitCode=137', [StringComparison]::Ordinal) -ge 0) 'The inherited diagnosis must carry the inner exit code the outer level cannot see.'

    # --- 变异对照：拿掉分类之后两条断言必须转红 ---------------------------------------------------

    $mutatedLibraryPath = Join-Path $temporaryRoot 'ScriptAutomation.mutated.ps1'
    $libraryText = [IO.File]::ReadAllText($libraryPath)
    $mutationAnchor = @'
    if ([OperatingSystem]::IsWindows()) { return $null }
    if ($ExitCode -le 128 -or $ExitCode -gt 192) { return $null }
'@
    $anchorOccurrences = ([regex]::Matches($libraryText, [regex]::Escape($mutationAnchor))).Count
    Assert-Contract ($anchorOccurrences -eq 1) 'The mutation anchor must match exactly once; a moved guard silently turns this control into a no-op.'
    $mutatedText = $libraryText.Replace($mutationAnchor, ($mutationAnchor + "`n    return `$null"))
    [IO.File]::WriteAllText($mutatedLibraryPath, $mutatedText, [Text.UTF8Encoding]::new($false))

    $mutatedProbePath = Join-Path $temporaryRoot 'mutated-probe.ps1'
    $mutatedProbe = @"
`$ErrorActionPreference = 'Stop'
. '$mutatedLibraryPath'
try {
    Invoke-NativeCommandWithTimeout ``
        -Command '/bin/sh' ``
        -Arguments @('-c', 'kill -9 `$`$') ``
        -WorkingDirectory '$repoRoot' ``
        -TimeoutSeconds 30 ``
        -Name 'signal-exit-mutated-probe' ``
        -LogDirectory '$(Join-Path $temporaryRoot 'mutated')' | Out-Null
}
catch {
    Write-Host "MUTATED-MESSAGE: `$(`$_.Exception.Message)"
}
"@
    [IO.File]::WriteAllText($mutatedProbePath, $mutatedProbe, [Text.UTF8Encoding]::new($false))

    $mutatedRun = Invoke-NativeCommandOutput `
        -Command 'pwsh' `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $mutatedProbePath) `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 120 `
        -Name 'signal-exit-mutation-control'
    $mutatedOutput = [string] $mutatedRun.Stdout
    Assert-Contract ($mutatedOutput.IndexOf('MUTATED-MESSAGE: ', [StringComparison]::Ordinal) -ge 0) 'The mutation control must reach the failure it is measuring.'
    Assert-Contract ($mutatedOutput.IndexOf('exited with 137', [StringComparison]::Ordinal) -ge 0) 'The mutated library must still produce the same 137 the real one classifies; otherwise the control changed the input instead of the behaviour.'
    Assert-Contract ($mutatedOutput.IndexOf('Terminated by signal', [StringComparison]::Ordinal) -lt 0) 'Removing the classification must remove the direct signal diagnosis; if it survives, the assertions above are not measuring it.'
    Assert-Contract ($mutatedOutput.IndexOf('NERV-SIGNAL-EXIT', [StringComparison]::Ordinal) -lt 0) 'Removing the classification must also remove the marker the outer level inherits.'

    Write-Host 'Script automation signal-exit contract passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
