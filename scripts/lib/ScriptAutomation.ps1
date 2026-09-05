# Script-Governance:
#   Category: library
#   SideEffects:
#     - Provides shared script automation helpers
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed child process trees when requested
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

$ordinalStringLibrary = Join-Path $PSScriptRoot 'OrdinalString.ps1'
if (Test-Path -LiteralPath $ordinalStringLibrary -PathType Leaf) {
    . $ordinalStringLibrary
}

$script:ScriptAutomationStreamDrainTimeoutMilliseconds = 5000

if (-not ('Nerv.IIP.ScriptAutomation.RedirectedStreamCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nerv.IIP.ScriptAutomation
{
    public sealed class RedirectedStreamCapture : IDisposable
    {
        private readonly StreamReader _reader;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly object _bufferLock = new object();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private int _stopRequested;

        public RedirectedStreamCapture(StreamReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Completion = CaptureAsync();
        }

        public Task Completion { get; }

        public string Snapshot()
        {
            lock (_bufferLock)
            {
                return _buffer.ToString();
            }
        }

        public string ReadIncrement(ref int cursor, int maximumCharacters)
        {
            lock (_bufferLock)
            {
                int count = Math.Min(maximumCharacters, _buffer.Length - cursor);
                // Keep UTF-16 surrogate pairs together for accurate UTF-8 byte counts.
                if (count > 0 && char.IsHighSurrogate(_buffer[cursor + count - 1]))
                {
                    if (cursor + count < _buffer.Length) count++;
                    else if (!Completion.IsCompleted) count--;
                }
                string increment = _buffer.ToString(cursor, count);
                cursor += count;
                return increment;
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            _reader.Dispose();
        }

        public void Dispose()
        {
            Stop();
            _cancellation.Dispose();
        }

        private async Task CaptureAsync()
        {
            var chunk = new char[4096];
            try
            {
                while (true)
                {
                    var read = await _reader.ReadAsync(chunk.AsMemory(), _cancellation.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    lock (_bufferLock)
                    {
                        _buffer.Append(chunk, 0, read);
                    }
                }
            }
            catch (OperationCanceledException) when (Volatile.Read(ref _stopRequested) != 0)
            {
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _stopRequested) != 0)
            {
            }
            catch (IOException) when (Volatile.Read(ref _stopRequested) != 0)
            {
            }
        }
    }
}
'@
}

function Get-ScriptAutomationRepoRoot {
    $root = Resolve-Path (Join-Path $PSScriptRoot '../..')
    return $root.Path
}

function Protect-ScriptAutomationText {
    param(
        [AllowNull()]
        [string] $Text,

        [string[]] $SensitiveValues = @(),

        [hashtable] $IncrementalState,

        [switch] $Final
    )

    if ($null -eq $Text) {
        return $null
    }

    $rules = @(
        @('(?is)-----BEGIN [^-\r\n]+-----.*?-----END [^-\r\n]+-----', '<redacted-pem>'),
        @('(?i)(https?://)[^/@\s]+@', '$1<redacted>@'),
        @('(?i)(["''](?:authorization|password|pwd|token|secret|client_secret|customerName|phone|email|address)["'']\s*:\s*["''])[^"'']*(["''])', '$1<redacted>$2'),
        @('(?i)(authorization\s*[:=]\s*bearer\s+)[^\s''"]+', '$1<redacted>'),
        @('(?i)(password\s*=\s*)[^;\s]+', '$1<redacted>'),
        @('(?i)(pwd\s*=\s*)[^;\s]+', '$1<redacted>'),
        @('(?i)(token\s*[:=]\s*)[^\s''";]+', '$1<redacted>'),
        @('(?i)(secret\s*[:=]\s*)[^\s''";]+', '$1<redacted>'),
        @('(?i)(client_secret\s*[:=]\s*)[^\s''";]+', '$1<redacted>'),
        @('(?i)((?:customerName|phone|email|address)\s*=\s*)[^;\s,}]+', '$1<redacted>'),
        @('(?i)(user-secrets\s+set\s+["'']?[^"''\s]+["'']?\s+)[^\s]+', '$1<redacted>'),
        @('(?i)(Host=[^;]+;Port=[^;]+;Database=[^;]+;Username=[^;]+;Password=)[^;]+', '$1<redacted>')
    )

    if ($null -ne $IncrementalState) {
        # Live output commits complete lines only. Multiline constructs stay in this
        # authority until their closing delimiter arrives; capture remains independent.
        if ($IncrementalState.ContainsKey('SuppressionReason')) { return '' }
        $Text = [string] $IncrementalState.Pending + $Text
        if ($Final -or $Text.Length -gt 65536) {
            $IncrementalState.Pending = ''
            if ($Text.Length -gt 0) {
                $IncrementalState.SuppressionReason = if ($Final) { 'incomplete-record' } else { 'record-limit' }
            }
            return ''
        }
        $boundary = $Text.LastIndexOf("`n", [StringComparison]::Ordinal) + 1
        if ($boundary -gt 0) {
            $openStructures = @(
                '(?is)-----BEGIN [^-\r\n]+-----(?:(?!-----END [^-\r\n]+-----).)*$',
                '(?is)["''](?:authorization|password|pwd|token|secret|client_secret|customerName|phone|email|address)["'']\s*(?::\s*(?:["''][^"'']*)?)?$',
                '(?is)(?:authorization|password|pwd|token|secret|client_secret|customerName|phone|email|address)\s*(?:[:=]\s*(?:bearer\s*)?)?$',
                '(?is)user-secrets\s+(?:set\s*(?:["'']?[^"''\s]+["'']?\s*)?)?$',
                '(?is)Host=[^;]*(?:;Port=[^;]*(?:;Database=[^;]*(?:;Username=[^;]*(?:;Password=[^;]*)?)?)?)?$'
            )
            foreach ($pattern in $openStructures) {
                $match = [regex]::Match($Text, $pattern)
                if ($match.Success) { $boundary = [Math]::Min($boundary, $match.Index) }
            }
            # Use the same complete-match grammar as final log redaction when a
            # match crosses the last complete line or an earlier retained boundary.
            do {
                $previousBoundary = $boundary
                foreach ($rule in $rules) {
                    foreach ($match in [regex]::Matches($Text, $rule[0])) {
                        if ($match.Index -lt $boundary -and $match.Index + $match.Length -gt $boundary) {
                            $boundary = $match.Index
                        }
                    }
                }
                foreach ($value in $SensitiveValues) {
                    if ([string]::IsNullOrEmpty($value)) { continue }
                    # A known secret may span any number of newlines or chunks.
                    $start = [Math]::Max(0, $boundary - $value.Length + 1)
                    for ($index = $start; $index -lt $boundary; $index++) {
                        $count = [Math]::Min($value.Length, $Text.Length - $index)
                        if ([string]::CompareOrdinal($Text, $index, $value, 0, $count) -eq 0 -and $index + $value.Length -gt $boundary) {
                            $boundary = $index
                            break
                        }
                    }
                }
                if ($boundary -gt 0) {
                    $boundary = $Text.LastIndexOf("`n", $boundary - 1, [StringComparison]::Ordinal) + 1
                }
            } while ($boundary -lt $previousBoundary)
        }
        $IncrementalState.Pending = $Text.Substring($boundary)
        $Text = $Text.Substring(0, $boundary)
    }

    $redacted = $Text
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $redacted = $redacted.Replace($sensitiveValue, '<redacted>')
        }
    }
    foreach ($rule in $rules) {
        $redacted = [regex]::Replace($redacted, $rule[0], $rule[1])
    }

    return $redacted
}

function Set-ScriptAutomationProcessEnvironment {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.ProcessStartInfo] $StartInfo,
        [System.Collections.IDictionary] $Environment
    )

    if ($null -eq $Environment) { return }
    foreach ($entry in $Environment.GetEnumerator()) {
        $environmentName = "$($entry.Key)"
        if ($null -eq $entry.Value) {
            [void] $StartInfo.Environment.Remove($environmentName)
        }
        else {
            $StartInfo.Environment[$environmentName] = "$($entry.Value)"
        }
    }
}

function Protect-ScriptAutomationArguments {
    param(
        [string[]] $Arguments = @(),

        [int[]] $SensitiveArgumentIndexes = @()
    )

    $sensitiveIndexes = @{}
    foreach ($index in $SensitiveArgumentIndexes) {
        $sensitiveIndexes[[int] $index] = $true
    }

    $displayArguments = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        if ($sensitiveIndexes.ContainsKey($index)) {
            $displayArguments.Add('<redacted>')
            continue
        }

        $displayArguments.Add($Arguments[$index])
    }

    return Protect-ScriptAutomationText ($displayArguments -join ' ')
}

function New-ScriptAutomationLogDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [string] $LogDirectory,

        [int[]] $SensitiveArgumentIndexes = @()
    )

    if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
        $safeName = [regex]::Replace($Name, '[^A-Za-z0-9_.-]+', '-').Trim('-')
        if ([string]::IsNullOrWhiteSpace($safeName)) {
            $safeName = 'command'
        }

        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
        $LogDirectory = Join-Path (Get-ScriptAutomationRepoRoot) "artifacts/script-logs/$safeName/$timestamp"
    }

    New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
    return (Resolve-Path $LogDirectory).Path
}

function Write-Diagnostic {
    param(
        [Parameter(Mandatory)]
        [string] $Message,

        [string] $Level = 'INFO'
    )

    $timestamp = Get-Date -Format o
    Write-Host "[$timestamp][$Level] $(Protect-ScriptAutomationText $Message)"
}

function Write-ScriptAutomationProcessLog {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [AllowNull()]
        [string] $Content,

        [switch] $PartialOutput,

        [string[]] $UnfinishedStreams = @(),

        [string[]] $SensitiveValues = @()
    )

    $logContent = [string] $Content
    if ($PartialOutput) {
        if ($logContent.Length -gt 0 -and -not $logContent.EndsWith("`n", [StringComparison]::Ordinal)) {
            $logContent += [Environment]::NewLine
        }
        $safeStreams = Protect-ScriptAutomationText (@($UnfinishedStreams) -join ', ') -SensitiveValues $SensitiveValues
        $logContent += "[NERV-IIP PARTIAL OUTPUT: bounded redirected stream capture ended before EOF; unfinished streams: $safeStreams]$([Environment]::NewLine)"
    }

    [System.IO.File]::WriteAllText($Path, (Protect-ScriptAutomationText $logContent -SensitiveValues $SensitiveValues), [System.Text.UTF8Encoding]::new($false))
}

function Protect-ScriptAutomationLogFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $fullPath = (Resolve-Path $Path).Path
    $tempPath = "$fullPath.redacted-$([System.Guid]::NewGuid().ToString('N')).tmp"
    $reader = $null
    $writer = $null
    $replaced = $false

    try {
        $reader = [System.IO.StreamReader]::new($fullPath, [System.Text.UTF8Encoding]::new($false), $true)
        $writer = [System.IO.StreamWriter]::new($tempPath, $false, [System.Text.UTF8Encoding]::new($false))

        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            $writer.WriteLine((Protect-ScriptAutomationText $line))
        }

        $reader.Dispose()
        $reader = $null
        $writer.Dispose()
        $writer = $null

        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force
        $replaced = $true
    }
    finally {
        if ($reader) {
            $reader.Dispose()
        }
        if ($writer) {
            $writer.Dispose()
        }
        if (-not $replaced) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-ScriptAutomationProcessTreeIds {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId
    )

    $ids = New-Object System.Collections.Generic.List[int]

    if ($IsWindows) {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            foreach ($childId in Get-ScriptAutomationProcessTreeIds -ProcessId ([int] $child.ProcessId)) {
                if (-not $ids.Contains($childId)) {
                    $ids.Add($childId)
                }
            }
        }
    }
    elseif ($IsLinux) {
        $children = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in [System.IO.Directory]::EnumerateDirectories('/proc')) {
            $candidateProcessId = 0
            if (-not [int]::TryParse([System.IO.Path]::GetFileName($entry), [ref] $candidateProcessId)) { continue }
            try {
                $stat = [System.IO.File]::ReadAllText((Join-Path $entry 'stat'))
                $commandEnd = $stat.LastIndexOf([string] ')', [StringComparison]::Ordinal)
                if ($commandEnd -lt 0 -or ($commandEnd + 2) -ge $stat.Length) { continue }
                $fieldsAfterCommand = @($stat.Substring($commandEnd + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
                if ($fieldsAfterCommand.Count -lt 2) { continue }
                $parentProcessId = 0
                if (-not [int]::TryParse($fieldsAfterCommand[1], [ref] $parentProcessId)) { continue }
                if ($parentProcessId -eq $ProcessId) {
                    $children.Add([pscustomobject]@{
                        ProcessId = $candidateProcessId
                        ParentProcessId = $parentProcessId
                    })
                }
            }
            catch {
                # A process may exit while /proc is being inspected.
            }
        }
        foreach ($child in @($children)) {
            foreach ($childId in Get-ScriptAutomationProcessTreeIds -ProcessId ([int] $child.ProcessId)) {
                if (-not $ids.Contains($childId)) {
                    $ids.Add($childId)
                }
            }
        }
    }

    if (-not $ids.Contains($ProcessId)) {
        $ids.Add($ProcessId)
    }

    return $ids
}

function Stop-ProcessTree {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId,

        [string] $Reason = 'Managed script cleanup'
    )

    $ids = @(Get-ScriptAutomationProcessTreeIds -ProcessId $ProcessId)
    [array]::Reverse($ids)
    $stopped = New-Object System.Collections.Generic.List[int]
    $missing = New-Object System.Collections.Generic.List[int]

    foreach ($id in $ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            $missing.Add($id)
            continue
        }

        try {
            Stop-Process -Id $id -Force -ErrorAction Stop
            $stopped.Add($id)
        }
        catch {
            Write-Diagnostic -Level 'WARN' -Message "Failed to stop process $id for ${Reason}: $($_.Exception.Message)"
        }
    }

    $remaining = [System.Collections.Generic.List[int]]::new()
    foreach ($id in $ids) {
        $alive = $true
        for ($attempt = 0; $attempt -lt 50; $attempt++) {
            if ($null -eq (Get-Process -Id $id -ErrorAction SilentlyContinue)) {
                $alive = $false
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if ($alive) { $remaining.Add($id) }
    }
    if ($remaining.Count -ne 0) {
        throw "Exact managed process tree cleanup left PID(s) $($remaining -join ', ') for ${Reason}."
    }

    return [pscustomobject]@{
        RequestedProcessId = $ProcessId
        Reason = $Reason
        StoppedProcessIds = @($stopped)
        MissingProcessIds = @($missing)
    }
}

function Complete-ScriptAutomationRedirectedStreamDrain {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [System.Threading.Tasks.Task] $StdoutTask,
        [Parameter(Mandatory)] [System.Threading.Tasks.Task] $StderrTask,
        [Parameter(Mandatory)] [string] $Name,
        [string] $LogDirectory,
        [object] $StdoutCapture,
        [object] $StderrCapture,

        [string[]] $SensitiveValues = @()
    )

    $drainStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $releaseBudgetMilliseconds = [Math]::Min(
        250,
        [Math]::Max(0, [int] ($script:ScriptAutomationStreamDrainTimeoutMilliseconds / 4))
    )
    $readerCloseAfterMilliseconds = $script:ScriptAutomationStreamDrainTimeoutMilliseconds - $releaseBudgetMilliseconds
    while (
        (-not $StdoutTask.IsCompleted -or -not $StderrTask.IsCompleted) -and
        $drainStopwatch.ElapsedMilliseconds -lt $readerCloseAfterMilliseconds
    ) {
        Start-Sleep -Milliseconds 25
    }

    $unfinished = [System.Collections.Generic.List[string]]::new()
    if (-not $StdoutTask.IsCompleted) {
        $unfinished.Add('stdout')
        try {
            if ($null -ne $StdoutCapture) { $StdoutCapture.Stop() }
            else { $Process.StandardOutput.Dispose() }
        }
        catch { }
    }
    if (-not $StderrTask.IsCompleted) {
        $unfinished.Add('stderr')
        try {
            if ($null -ne $StderrCapture) { $StderrCapture.Stop() }
            else { $Process.StandardError.Dispose() }
        }
        catch { }
    }

    while (
        (-not $StdoutTask.IsCompleted -or -not $StderrTask.IsCompleted) -and
        $drainStopwatch.ElapsedMilliseconds -lt $script:ScriptAutomationStreamDrainTimeoutMilliseconds
    ) {
        Start-Sleep -Milliseconds 10
    }

    $resolvedLogDirectory = $LogDirectory
    if ($unfinished.Count -gt 0) {
        $resolvedLogDirectory = New-ScriptAutomationLogDirectory `
            -Name "$Name-stream-drain" `
            -LogDirectory $resolvedLogDirectory
        Write-Diagnostic `
            -Level 'WARN' `
            -Message "Redirected stream drain did not complete within the $($script:ScriptAutomationStreamDrainTimeoutMilliseconds)ms bound for '$Name'; closed $($unfinished -join ', ') reader(s). Logs: $resolvedLogDirectory"
    }

    $drainErrors = [System.Collections.Generic.List[string]]::new()
    foreach ($stream in @(
        [pscustomobject]@{ Name = 'stdout'; Task = $StdoutTask },
        [pscustomobject]@{ Name = 'stderr'; Task = $StderrTask }
    )) {
        if ($unfinished.Contains($stream.Name)) { continue }
        if ($stream.Task.Status -eq [System.Threading.Tasks.TaskStatus]::Canceled) {
            $drainErrors.Add("$($stream.Name) drain was canceled")
        }
        elseif ($stream.Task.Status -eq [System.Threading.Tasks.TaskStatus]::Faulted) {
            $drainFailure = $stream.Task.Exception.GetBaseException()
            $safeDrainMessage = Protect-ScriptAutomationText "$($drainFailure.Message)" -SensitiveValues $SensitiveValues
            $drainErrors.Add("$($stream.Name) drain failed: $safeDrainMessage")
        }
    }

    $stdout = if ($null -ne $StdoutCapture) {
        [string] $StdoutCapture.Snapshot()
    }
    elseif ($StdoutTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) {
        # RanToCompletion is terminal; result access cannot block.
        [string] $StdoutTask.GetAwaiter().GetResult()
    }
    else { '' }
    $stderr = if ($null -ne $StderrCapture) {
        [string] $StderrCapture.Snapshot()
    }
    elseif ($StderrTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) {
        # RanToCompletion is terminal; result access cannot block.
        [string] $StderrTask.GetAwaiter().GetResult()
    }
    else { '' }

    return [pscustomobject]@{
        Stdout = $stdout
        Stderr = $stderr
        TimedOut = $unfinished.Count -gt 0
        UnfinishedStreams = @($unfinished)
        DrainErrors = @($drainErrors)
        LogDirectory = $resolvedLogDirectory
    }
}

function Write-ScriptAutomationStreamDrainDiagnostics {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $Drain,

        [string[]] $SensitiveValues = @()
    )

    foreach ($diagnostic in @($Drain.DrainErrors)) {
        Write-Diagnostic -Level 'WARN' -Message (Protect-ScriptAutomationText "Redirected stream diagnostic for '$Name': $diagnostic" -SensitiveValues $SensitiveValues)
    }
}

# 信号退出码分类（#1664 / #1876）。
#
# 受管入口此前把「进程被内核杀掉」和「进程自己判定失败」报成同一句话。两次 hosted runner 上的
# FullChain 失败里，真正死掉的是一个被 SIGKILL 的 `dotnet test`（内层 137 = 128 + 9），而 lane
# 只报 `Command 'pwsh' exited with 1`；不下载诊断包就会把它读成场景断言失败或抖动。下面三个函数
# 的存在就是为了取消那种读法：信号死亡在它经过的每一层都自报家门。
#
# POSIX 只保证下列信号的编号在各实现之间一致。SIGBUS、SIGUSR1、SIGUSR2 等在 Linux 与 macOS 上
# 编号不同，因此刻意不进表——报一个泛化的 `SIG<n>` 比报一个错的名字有用，后者会把排查引向另一
# 种故障。
$script:NervScriptAutomationPortableSignalNames = @{
    1 = 'SIGHUP'
    2 = 'SIGINT'
    3 = 'SIGQUIT'
    4 = 'SIGILL'
    5 = 'SIGTRAP'
    6 = 'SIGABRT'
    8 = 'SIGFPE'
    9 = 'SIGKILL'
    11 = 'SIGSEGV'
    13 = 'SIGPIPE'
    14 = 'SIGALRM'
    15 = 'SIGTERM'
}
# 跨进程边界的继承靠这个稳定标记，而不是去解析下一层的自然语言失败信息。
$script:NervScriptAutomationSignalExitMarker = 'NERV-SIGNAL-EXIT'

function Get-ScriptAutomationSignalExit {
    <#
        Classifies a child process exit code as a signal termination, or returns $null when the code
        carries no such meaning.
    #>
    param([Parameter(Mandatory)] [int] $ExitCode)

    # Windows 退出码是任意 32 位值，137 在那里就只是 137；只有 Unix 侧的 128 + signal 约定成立。
    if ([OperatingSystem]::IsWindows()) { return $null }
    if ($ExitCode -le 128 -or $ExitCode -gt 192) { return $null }

    $signal = $ExitCode - 128
    $signalName = if ($script:NervScriptAutomationPortableSignalNames.ContainsKey($signal)) {
        [string] $script:NervScriptAutomationPortableSignalNames[$signal]
    }
    else {
        "SIG$signal"
    }
    $hint = if ($signal -eq 9) {
        'the process was force-killed and never got to report a result; suspect an out-of-memory kill or an external kill'
    }
    elseif ($signal -eq 6 -or $signal -eq 11) {
        'the process crashed; look for a runtime fault rather than a failed assertion'
    }
    else {
        'the process was terminated by a signal rather than exiting on its own'
    }

    return [pscustomobject]@{
        ExitCode = $ExitCode
        Signal = $signal
        SignalName = $signalName
        Hint = $hint
    }
}

function Format-ScriptAutomationSignalExit {
    <#
        Renders the single-line marker a parent process can recognise in a child's captured output.
        The hint is last so the leading fields stay parseable.
    #>
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [object] $SignalExit
    )

    return "$script:NervScriptAutomationSignalExitMarker exitCode=$($SignalExit.ExitCode) signal=$($SignalExit.Signal) signalName=$($SignalExit.SignalName) name=$Name command=$Command hint=$($SignalExit.Hint)"
}

function Get-ScriptAutomationInheritedSignalExit {
    <#
        Recovers a signal termination that happened one or more levels down, from the captured output
        of a child that itself exited with an ordinary non-zero code.
    #>
    param(
        [AllowNull()] [string] $Stdout,
        [AllowNull()] [string] $Stderr
    )

    foreach ($stream in @([string] $Stdout, [string] $Stderr)) {
        if ([string]::IsNullOrEmpty($stream)) { continue }
        foreach ($line in ($stream -split "`r?`n")) {
            $markerIndex = $line.IndexOf($script:NervScriptAutomationSignalExitMarker, [StringComparison]::Ordinal)
            if ($markerIndex -ge 0) { return $line.Substring($markerIndex).Trim() }
        }
    }

    return $null
}

function Add-ScriptAutomationSignalExitDiagnosis {
    <#
        Appends the signal diagnosis to a failure message, and emits the marker so the next level up
        can inherit it. Callers keep the literal `exited with <code>` prefix intact: three governed
        scripts parse it with `exited with (?<exitCode>\d+)`.
    #>
    param(
        [Parameter(Mandatory)] [string] $FailureMessage,
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $ExitCode,
        [AllowNull()] [string] $Stdout,
        [AllowNull()] [string] $Stderr
    )

    $signalExit = Get-ScriptAutomationSignalExit -ExitCode $ExitCode
    if ($null -ne $signalExit) {
        Write-Diagnostic -Level 'ERROR' -Message (Format-ScriptAutomationSignalExit -Command $Command -Name $Name -SignalExit $signalExit)
        return "$FailureMessage Terminated by signal $($signalExit.SignalName) ($($signalExit.Signal)): $($signalExit.Hint)."
    }

    $inherited = Get-ScriptAutomationInheritedSignalExit -Stdout $Stdout -Stderr $Stderr
    if ($null -ne $inherited) {
        return "$FailureMessage A governed child process was terminated by a signal: $inherited."
    }

    return $FailureMessage
}

function Write-ScriptAutomationLiveOutput {
    param(
        [Parameter(Mandatory)] [hashtable] $State,
        [Parameter(Mandatory)] [object] $StdoutCapture,
        [Parameter(Mandatory)] [object] $StderrCapture,
        [string[]] $SensitiveValues = @(),
        [switch] $Final
    )

    foreach ($stream in @('stdout', 'stderr')) {
        $capture = if ([string]::Equals($stream, 'stdout', [StringComparison]::Ordinal)) { $StdoutCapture } else { $StderrCapture }
        $streamState = $State[$stream]
        do {
            $cursor = [int] $streamState.Cursor
            $increment = $capture.ReadIncrement([ref] $cursor, 16384)
            $streamState.Cursor = $cursor
            $streamState.Bytes += [Text.Encoding]::UTF8.GetByteCount($increment)
            $safe = Protect-ScriptAutomationText -Text $increment -SensitiveValues $SensitiveValues -IncrementalState $streamState
            if ($safe.Length -gt 0) { Write-Host -NoNewline $safe }
        } while ($Final -and $increment.Length -ge 16384)
        if ($Final) {
            $safe = Protect-ScriptAutomationText -Text '' -SensitiveValues $SensitiveValues -IncrementalState $streamState -Final
            if ($safe.Length -gt 0) { Write-Host -NoNewline $safe }
        }
        if ($streamState.ContainsKey('SuppressionReason') -and -not $streamState.ContainsKey('SuppressionReported')) {
            Write-Host "[live] stream=$stream textSuppressed=$($streamState.SuppressionReason)"
            $streamState.SuppressionReported = $true
        }
    }
}

function Write-ScriptAutomationLiveHeartbeat {
    param(
        [hashtable] $State,
        [string] $Name,
        [int] $ProcessId,
        [long] $ElapsedMilliseconds,
        [string[]] $SensitiveValues = @()
    )

    $alive = @(Get-ScriptAutomationProcessTreeIds -ProcessId $ProcessId | Where-Object { $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) })
    $safeName = Protect-ScriptAutomationText -Text $Name -SensitiveValues $SensitiveValues
    Write-Host "[live] name=$safeName rootPid=$ProcessId elapsedMs=$ElapsedMilliseconds stdoutBytes=$($State.stdout.Bytes) stderrBytes=$($State.stderr.Bytes) aliveCount=$($alive.Count) alivePids=$($alive -join ',')"
    $State.stdout.Bytes = 0L
    $State.stderr.Bytes = 0L
}

function Invoke-NativeCommandWithTimeout {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name,

        [string] $LogDirectory,

        [int[]] $SensitiveArgumentIndexes = @(),

        [scriptblock] $StreamReadTaskAction,

        [System.Collections.IDictionary] $Environment,

        [string[]] $SensitiveValues = @(),

        [switch] $LiveOutput
    )

    if ($LiveOutput -and $null -ne $StreamReadTaskAction) {
        throw 'LiveOutput requires the managed redirected stream capture.'
    }

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
    }
    $resolvedLogDirectory = New-ScriptAutomationLogDirectory -Name $Name -LogDirectory $LogDirectory
    $stdoutPath = Join-Path $resolvedLogDirectory 'stdout.log'
    $stderrPath = Join-Path $resolvedLogDirectory 'stderr.log'

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Command
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    Set-ScriptAutomationProcessEnvironment -StartInfo $startInfo -Environment $Environment

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.EnableRaisingEvents = $true

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $stdoutTask = $null
    $stderrTask = $null
    $stdoutCapture = $null
    $stderrCapture = $null
    $rootProcessId = $null
    $liveState = @{
        stdout = @{ Cursor = 0; Pending = ''; Bytes = 0L }
        stderr = @{ Cursor = 0; Pending = ''; Bytes = 0L }
    }

    try {
        $displayArguments = Protect-ScriptAutomationArguments -Arguments $Arguments -SensitiveArgumentIndexes $SensitiveArgumentIndexes
        Write-Diagnostic "Starting $Command $displayArguments (cwd=$WorkingDirectory, timeout=${TimeoutSeconds}s, logs=$resolvedLogDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        $rootProcessId = $process.Id
        if ($null -eq $StreamReadTaskAction) {
            $stdoutCapture = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new($process.StandardOutput)
            $stderrCapture = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new($process.StandardError)
            $stdoutTask = $stdoutCapture.Completion
            $stderrTask = $stderrCapture.Completion
        }
        else {
            $stdoutTask = & $StreamReadTaskAction $process.StandardOutput 'stdout'
            $stderrTask = & $StreamReadTaskAction $process.StandardError 'stderr'
        }

        if ($LiveOutput) {
            $waitClock = [Diagnostics.Stopwatch]::StartNew()
            $nextHeartbeat = 0L
            do {
                Write-ScriptAutomationLiveOutput -State $liveState -StdoutCapture $stdoutCapture -StderrCapture $stderrCapture -SensitiveValues $SensitiveValues
                if ($waitClock.ElapsedMilliseconds -ge $nextHeartbeat) {
                    Write-ScriptAutomationLiveHeartbeat -State $liveState -Name $Name -ProcessId $rootProcessId -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds -SensitiveValues $SensitiveValues
                    $nextHeartbeat = $waitClock.ElapsedMilliseconds + 5000
                }
                $remaining = [Math]::Max(0, $TimeoutSeconds * 1000L - $waitClock.ElapsedMilliseconds)
                $exited = $process.WaitForExit([int] [Math]::Min(250, $remaining))
            } while (-not $exited -and $waitClock.ElapsedMilliseconds -lt $TimeoutSeconds * 1000L)
        }
        else {
            $exited = $process.WaitForExit($TimeoutSeconds * 1000)
        }
        if (-not $exited) {
            $timedOut = $true
            if ($LiveOutput) {
                Write-ScriptAutomationLiveOutput -State $liveState -StdoutCapture $stdoutCapture -StderrCapture $stderrCapture -SensitiveValues $SensitiveValues
                Write-ScriptAutomationLiveHeartbeat -State $liveState -Name $Name -ProcessId $rootProcessId -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds -SensitiveValues $SensitiveValues
            }
            Write-Diagnostic -Level 'ERROR' -Message "Command timed out: $Command (pid=$rootProcessId, timeout=${TimeoutSeconds}s, logs=$resolvedLogDirectory)"
            $cleanup = Stop-ProcessTree -ProcessId $rootProcessId -Reason "Timeout while running $Command"
            $drain = Complete-ScriptAutomationRedirectedStreamDrain `
                -Process $process `
                -StdoutTask $stdoutTask `
                -StderrTask $stderrTask `
                -Name $Name `
                -LogDirectory $resolvedLogDirectory `
                -StdoutCapture $stdoutCapture `
                -StderrCapture $stderrCapture `
                -SensitiveValues $SensitiveValues
            Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
            if ($LiveOutput) {
                Write-ScriptAutomationLiveOutput -State $liveState -StdoutCapture $stdoutCapture -StderrCapture $stderrCapture -SensitiveValues $SensitiveValues -Final
                Write-ScriptAutomationLiveHeartbeat -State $liveState -Name $Name -ProcessId $rootProcessId -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds -SensitiveValues $SensitiveValues
            }
            Write-ScriptAutomationProcessLog -Path $stdoutPath -Content $drain.Stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            Write-ScriptAutomationProcessLog -Path $stderrPath -Content $drain.Stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            throw "Command '$Command' timed out after $TimeoutSeconds seconds. Stopped PIDs: $($cleanup.StoppedProcessIds -join ', '). Logs: $resolvedLogDirectory"
        }

        $exitCode = $process.ExitCode
        $drain = Complete-ScriptAutomationRedirectedStreamDrain `
            -Process $process `
            -StdoutTask $stdoutTask `
            -StderrTask $stderrTask `
            -Name $Name `
            -LogDirectory $resolvedLogDirectory `
            -StdoutCapture $stdoutCapture `
            -StderrCapture $stderrCapture `
            -SensitiveValues $SensitiveValues
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        if ($LiveOutput) {
            Write-ScriptAutomationLiveOutput -State $liveState -StdoutCapture $stdoutCapture -StderrCapture $stderrCapture -SensitiveValues $SensitiveValues -Final
            Write-ScriptAutomationLiveHeartbeat -State $liveState -Name $Name -ProcessId $rootProcessId -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds -SensitiveValues $SensitiveValues
        }
        Write-ScriptAutomationProcessLog -Path $stdoutPath -Content $drain.Stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
        Write-ScriptAutomationProcessLog -Path $stderrPath -Content $drain.Stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues

        $stopwatch.Stop()

        if ($exitCode -ne 0) {
            $failureMessage = Add-ScriptAutomationSignalExitDiagnosis `
                -FailureMessage "Command '$Command' exited with $exitCode after $($stopwatch.Elapsed)." `
                -Command $Command `
                -Name $Name `
                -ExitCode $exitCode `
                -Stdout $drain.Stdout `
                -Stderr $drain.Stderr
            throw "$failureMessage Logs: $resolvedLogDirectory"
        }
        if (@($drain.DrainErrors).Count -gt 0) {
            throw "Command '$Command' redirected stream drain failed: $($drain.DrainErrors -join '; '). Logs: $resolvedLogDirectory"
        }

        Write-Diagnostic "Command completed: $Command (pid=$rootProcessId, durationMs=$($stopwatch.ElapsedMilliseconds), logs=$resolvedLogDirectory)"

        return [pscustomobject]@{
            Command = $Command
            Arguments = $Arguments
            WorkingDirectory = $WorkingDirectory
            ExitCode = $exitCode
            TimedOut = $timedOut
            Duration = $stopwatch.Elapsed
            ProcessId = $rootProcessId
            LogDirectory = $resolvedLogDirectory
            StdoutPath = $stdoutPath
            StderrPath = $stderrPath
            PartialOutput = [bool] $drain.TimedOut
            UnfinishedStreams = @($drain.UnfinishedStreams)
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for $Command" | Out-Null
        }

        if ($null -ne $stdoutCapture) { $stdoutCapture.Dispose() }
        if ($null -ne $stderrCapture) { $stderrCapture.Dispose() }
        $process.Dispose()
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'dotnet',

        [int[]] $SensitiveArgumentIndexes = @(),

        [string[]] $SensitiveValues = @()
    )

    Invoke-NativeCommandWithTimeout -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name -SensitiveArgumentIndexes $SensitiveArgumentIndexes -SensitiveValues $SensitiveValues
}

function Invoke-NativeCommandOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 60,

        [string] $Name,

        [string] $LogDirectory,

        [switch] $PersistOutput,

        [switch] $AllowPartialOutput,

        [scriptblock] $StreamReadTaskAction,

        [System.Collections.IDictionary] $Environment,

        [string[]] $SensitiveValues = @(),

        [ValidateRange(1, [int]::MaxValue)]
        [int] $TimeoutMilliseconds
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
    }
    $usesMillisecondBudget = $PSBoundParameters.ContainsKey('TimeoutMilliseconds')
    if ($usesMillisecondBudget -and $PSBoundParameters.ContainsKey('TimeoutSeconds')) {
        throw [ArgumentException]::new('TimeoutSeconds and TimeoutMilliseconds are mutually exclusive.')
    }
    $effectiveTimeoutMilliseconds = if ($usesMillisecondBudget) {
        $TimeoutMilliseconds
    }
    else {
        $TimeoutSeconds * 1000
    }
    $timeoutDescription = if ($usesMillisecondBudget) {
        "$TimeoutMilliseconds milliseconds"
    }
    else {
        "$TimeoutSeconds seconds"
    }
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Command
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    Set-ScriptAutomationProcessEnvironment -StartInfo $startInfo -Environment $Environment

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stdoutCapture = $null
    $stderrCapture = $null

    try {
        $displayArguments = Protect-ScriptAutomationText ($Arguments -join ' ')
        Write-Diagnostic "Reading command output for $Name`: $Command $displayArguments (cwd=$WorkingDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        if ($null -eq $StreamReadTaskAction) {
            $stdoutCapture = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new($process.StandardOutput)
            $stderrCapture = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new($process.StandardError)
            $stdoutTask = $stdoutCapture.Completion
            $stderrTask = $stderrCapture.Completion
        }
        else {
            $stdoutTask = & $StreamReadTaskAction $process.StandardOutput 'stdout'
            $stderrTask = & $StreamReadTaskAction $process.StandardError 'stderr'
        }

        if (-not $process.WaitForExit($effectiveTimeoutMilliseconds)) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Timeout while reading output for $Command" | Out-Null
            $timeoutLogDirectory = New-ScriptAutomationLogDirectory -Name $Name -LogDirectory $LogDirectory
            $drain = Complete-ScriptAutomationRedirectedStreamDrain `
                -Process $process `
                -StdoutTask $stdoutTask `
                -StderrTask $stderrTask `
                -Name $Name `
                -LogDirectory $timeoutLogDirectory `
                -StdoutCapture $stdoutCapture `
                -StderrCapture $stderrCapture `
                -SensitiveValues $SensitiveValues
            Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stdout.log') -Content $drain.Stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            Write-ScriptAutomationProcessLog -Path (Join-Path $drain.LogDirectory 'stderr.log') -Content $drain.Stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            $failure = [TimeoutException]::new("Command '$Command' timed out after $timeoutDescription while reading output. Logs: $($drain.LogDirectory)")
            $failure.Data['Stdout'] = Protect-ScriptAutomationText $drain.Stdout -SensitiveValues $SensitiveValues
            $failure.Data['Stderr'] = Protect-ScriptAutomationText $drain.Stderr -SensitiveValues $SensitiveValues
            $failure.Data['LogDirectory'] = "$($drain.LogDirectory)"
            $failure.Data['PartialOutput'] = [bool] $drain.TimedOut
            throw $failure
        }

        $exitCode = $process.ExitCode
        $drain = Complete-ScriptAutomationRedirectedStreamDrain `
            -Process $process `
            -StdoutTask $stdoutTask `
            -StderrTask $stderrTask `
            -Name $Name `
            -LogDirectory $LogDirectory `
            -StdoutCapture $stdoutCapture `
            -StderrCapture $stderrCapture `
            -SensitiveValues $SensitiveValues
        Write-ScriptAutomationStreamDrainDiagnostics -Name $Name -Drain $drain -SensitiveValues $SensitiveValues
        $stdout = $drain.Stdout
        $stderr = $drain.Stderr
        if ($PersistOutput -or $drain.TimedOut) {
            $resolvedOutputLogDirectory = if ([string]::IsNullOrWhiteSpace([string]$drain.LogDirectory)) {
                New-ScriptAutomationLogDirectory -Name $Name -LogDirectory $LogDirectory
            }
            else {
                [string]$drain.LogDirectory
            }
            Write-ScriptAutomationProcessLog -Path (Join-Path $resolvedOutputLogDirectory 'stdout.log') -Content $stdout -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
            Write-ScriptAutomationProcessLog -Path (Join-Path $resolvedOutputLogDirectory 'stderr.log') -Content $stderr -PartialOutput:$drain.TimedOut -UnfinishedStreams $drain.UnfinishedStreams -SensitiveValues $SensitiveValues
        }

        if ($exitCode -ne 0) {
            $safeOutput = Protect-ScriptAutomationText (($stdout, $stderr) -join [Environment]::NewLine) -SensitiveValues $SensitiveValues
            $failureMessage = Add-ScriptAutomationSignalExitDiagnosis `
                -FailureMessage "Command '$Command' exited with $exitCode." `
                -Command $Command `
                -Name $Name `
                -ExitCode $exitCode `
                -Stdout $stdout `
                -Stderr $stderr
            $logSuffix = if ($PersistOutput) { " Logs: $resolvedOutputLogDirectory" } else { '' }
            $failure = [InvalidOperationException]::new("$failureMessage Output: $safeOutput$logSuffix")
            $failure.Data['ExitCode'] = [int] $exitCode
            if ($PersistOutput) {
                $failure.Data['LogDirectory'] = $resolvedOutputLogDirectory
            }
            throw $failure
        }
        if (@($drain.DrainErrors).Count -gt 0) {
            throw [InvalidOperationException]::new(
                "Command '$Command' redirected stream drain failed: $($drain.DrainErrors -join '; ')."
            )
        }
        if ($drain.TimedOut -and -not $AllowPartialOutput) {
            $failure = [InvalidOperationException]::new(
                "Command '$Command' exited successfully but redirected output was incomplete. Unfinished streams: $($drain.UnfinishedStreams -join ', '). Logs: $($drain.LogDirectory)"
            )
            $failure.Data['PartialOutput'] = $true
            $failure.Data['UnfinishedStreams'] = [string[]] @($drain.UnfinishedStreams)
            $failure.Data['LogDirectory'] = "$($drain.LogDirectory)"
            throw $failure
        }

        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Diagnostic -Level 'WARN' -Message (Protect-ScriptAutomationText "Stderr from ${Name}: $stderr" -SensitiveValues $SensitiveValues)
        }

        return [pscustomobject]@{
            Command = $Command
            Arguments = $Arguments
            WorkingDirectory = $WorkingDirectory
            ExitCode = $exitCode
            Stdout = $stdout
            Stderr = $stderr
            LogDirectory = if ($PersistOutput) { $resolvedOutputLogDirectory } else { $null }
            StdoutPath = if ($PersistOutput) { Join-Path $resolvedOutputLogDirectory 'stdout.log' } else { $null }
            StderrPath = if ($PersistOutput) { Join-Path $resolvedOutputLogDirectory 'stderr.log' } else { $null }
            PartialOutput = [bool] $drain.TimedOut
            UnfinishedStreams = @($drain.UnfinishedStreams)
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for output command $Command" | Out-Null
        }

        if ($null -ne $stdoutCapture) { $stdoutCapture.Dispose() }
        if ($null -ne $stderrCapture) { $stderrCapture.Dispose() }
        $process.Dispose()
    }
}

function Invoke-DotNetOutput {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 60,

        [string] $Name = 'dotnet'
    )

    Invoke-NativeCommandOutput -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
}

function Invoke-NativeCommandInteractive {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name,

        [System.Collections.IDictionary] $Environment
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Command
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    Set-ScriptAutomationProcessEnvironment -StartInfo $startInfo -Environment $Environment

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $rootProcessId = $null

    try {
        $displayArguments = Protect-ScriptAutomationText ($Arguments -join ' ')
        Write-Diagnostic "Starting interactive $Name`: $Command $displayArguments (cwd=$WorkingDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        $rootProcessId = $process.Id
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $stopwatch.Stop()

        if ($exitCode -ne 0) {
            Write-Diagnostic -Level 'WARN' -Message "Interactive command exited non-zero: $Name (command=$Command, exitCode=$exitCode, pid=$rootProcessId, durationMs=$($stopwatch.ElapsedMilliseconds))"
        }
        else {
            Write-Diagnostic "Interactive command completed: $Name (command=$Command, pid=$rootProcessId, durationMs=$($stopwatch.ElapsedMilliseconds))"
        }

        return [pscustomobject]@{
            Command = $Command
            Arguments = $Arguments
            WorkingDirectory = $WorkingDirectory
            ExitCode = $exitCode
            Duration = $stopwatch.Elapsed
            ProcessId = $rootProcessId
        }
    }
    finally {
        if ($process -and $rootProcessId -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for interactive $Command" | Out-Null
        }

        $process.Dispose()
    }
}

function Invoke-DotNetInteractive {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name = 'dotnet'
    )

    Invoke-NativeCommandInteractive -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Name $Name
}

function Get-AspireCliCommand {
    $command = Get-Command 'aspire' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    if ($IsWindows) {
        $localAspire = Join-Path $env:USERPROFILE '.aspire/bin/aspire.exe'
        if (Test-Path -LiteralPath $localAspire -PathType Leaf) {
            return $localAspire
        }
    }
    else {
        $localAspire = Join-Path $HOME '.aspire/bin/aspire'
        if (Test-Path -LiteralPath $localAspire -PathType Leaf) {
            return $localAspire
        }
    }

    throw 'Aspire CLI is required. Install it from https://aspire.dev or add it to PATH.'
}

function Invoke-Aspire {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'aspire'
    )

    Invoke-NativeCommandWithTimeout -Command (Get-AspireCliCommand) -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
}

function Invoke-AspireOutput {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 60,

        [string] $Name = 'aspire',

        [switch] $AllowPartialOutput,

        [System.Collections.IDictionary] $Environment,

        [string[]] $SensitiveValues = @()
    )

    Invoke-NativeCommandOutput `
        -Command (Get-AspireCliCommand) `
        -Arguments $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds $TimeoutSeconds `
        -Name $Name `
        -AllowPartialOutput:$AllowPartialOutput `
        -Environment $Environment `
        -SensitiveValues $SensitiveValues
}

function Invoke-AspireInteractive {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name = 'aspire',

        [System.Collections.IDictionary] $Environment
    )

    Invoke-NativeCommandInteractive -Command (Get-AspireCliCommand) -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Name $Name -Environment $Environment
}

function Resolve-PnpmDirArgument {
    param(
        [Parameter(Mandatory)]
        [string] $BaseDirectory,

        [Parameter(Mandatory)]
        [string] $Target
    )

    if ([System.IO.Path]::IsPathRooted($Target)) {
        return [System.IO.Path]::GetFullPath($Target)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BaseDirectory $Target))
}

function Resolve-PnpmInvocation {
    <#
    .SYNOPSIS
        规约 pnpm 调用的进程工作目录，根除 corepack 版本解析坑。
    .DESCRIPTION
        corepack 按“进程 cwd 就近 package.json 的 packageManager 字段”决定 pnpm 版本；
        仓库根目录没有 package.json，从根目录（或其他无 package.json 的目录）调用会解析
        到最新 pnpm，并因与 frontend/ 锁定版本不一致直接失败（pnpm -C 切目录发生在
        corepack 解析之后，救不回来）。本函数集中处理两件事：
        1. 参数中出现 -C/--dir <path> 时，把进程 cwd 对齐到该目标目录并剔除该参数对
           （行为等价：pnpm -C 的语义就是“切到该目录再执行”）。-C 大小写敏感（小写
           -c 是下游命令常见参数，不消费）；多次出现时各自基于原始 cwd 解析、末者胜；
           遇到 -- 分隔符后停止扫描，其后参数原样透传给下游命令。
        2. 未显式传 WorkingDirectory 时，默认以 <repoRoot>/frontend 为 cwd。
    #>
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $WorkingDirectory = Join-Path (Get-ScriptAutomationRepoRoot) 'frontend'
    }

    $baseDirectory = $WorkingDirectory
    $normalizedArguments = [System.Collections.Generic.List[string]]::new()
    $index = 0
    while ($index -lt $Arguments.Count) {
        $argument = $Arguments[$index]
        if ([string]::Equals($argument, '--', [StringComparison]::OrdinalIgnoreCase)) {
            # -- 之后的参数属于下游命令（pnpm run/exec 透传），原样保留、停止扫描。
            while ($index -lt $Arguments.Count) {
                $normalizedArguments.Add($Arguments[$index])
                $index += 1
            }
            break
        }
        # -C 必须大小写敏感：小写 -c 是下游命令（如 playwright test -c）常见参数。
        if (([string]::Equals($argument, '-C', [StringComparison]::Ordinal) -or
                [string]::Equals($argument, '--dir', [StringComparison]::OrdinalIgnoreCase)) -and
            ($index + 1) -lt $Arguments.Count) {
            $WorkingDirectory = Resolve-PnpmDirArgument -BaseDirectory $baseDirectory -Target $Arguments[$index + 1]
            $index += 2
            continue
        }
        if ($argument -like '--dir=*') {
            $WorkingDirectory = Resolve-PnpmDirArgument -BaseDirectory $baseDirectory -Target $argument.Substring('--dir='.Length)
            $index += 1
            continue
        }
        $normalizedArguments.Add($argument)
        $index += 1
    }

    return [pscustomobject]@{
        Arguments        = [string[]] $normalizedArguments.ToArray()
        WorkingDirectory = $WorkingDirectory
    }
}

function Invoke-Pnpm {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'pnpm',

        [System.Collections.IDictionary] $Environment,

        [string[]] $SensitiveValues = @()
    )

    $invocation = Resolve-PnpmInvocation -Arguments $Arguments -WorkingDirectory $WorkingDirectory

    if ($IsWindows) {
        return Invoke-NativeCommandWithTimeout -Command 'cmd' -Arguments (@('/d', '/s', '/c', 'pnpm') + $invocation.Arguments) -WorkingDirectory $invocation.WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name -Environment $Environment -SensitiveValues $SensitiveValues
    }

    Invoke-NativeCommandWithTimeout -Command 'pnpm' -Arguments $invocation.Arguments -WorkingDirectory $invocation.WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name -Environment $Environment -SensitiveValues $SensitiveValues
}

function Invoke-DockerCompose {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'docker-compose'
    )

    Invoke-NativeCommandWithTimeout -Command 'docker' -Arguments (@('compose') + $Arguments) -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
}

function Invoke-PwshScript {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'pwsh-script',

        [System.Collections.IDictionary] $Environment,

        [string[]] $SensitiveValues = @()
    )

    $fullArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Arguments
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments $fullArguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name -Environment $Environment -SensitiveValues $SensitiveValues
}

function ConvertTo-ScriptAutomationProcessArgument {
    param([AllowEmptyString()] [string] $Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = [System.Text.StringBuilder]::new()
    [void] $builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }
        if ($character -eq '"') {
            [void] $builder.Append(('\' * (($backslashes * 2) + 1)))
            [void] $builder.Append('"')
            $backslashes = 0
            continue
        }
        if ($backslashes -gt 0) {
            [void] $builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        [void] $builder.Append($character)
    }
    if ($backslashes -gt 0) {
        [void] $builder.Append(('\' * ($backslashes * 2)))
    }
    [void] $builder.Append('"')
    return $builder.ToString()
}

function Start-DetachedManagedProcess {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = (Get-Location).Path,
        [Parameter(Mandatory)] [string] $StdoutPath,
        [Parameter(Mandatory)] [string] $StderrPath,

        [System.Collections.IDictionary] $Environment
    )

    $resolvedWorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
    $resolvedStdoutPath = [System.IO.Path]::GetFullPath($StdoutPath)
    $resolvedStderrPath = [System.IO.Path]::GetFullPath($StderrPath)
    if ([string]::Equals($resolvedStdoutPath, $resolvedStderrPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Detached stdout and stderr paths must be different.'
    }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedStdoutPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedStderrPath)) | Out-Null

    $quotedArguments = @($Arguments | ForEach-Object { ConvertTo-ScriptAutomationProcessArgument -Value "$_" })
    $startParameters = @{
        FilePath = $Command
        ArgumentList = $quotedArguments
        WorkingDirectory = $resolvedWorkingDirectory
        RedirectStandardOutput = $resolvedStdoutPath
        RedirectStandardError = $resolvedStderrPath
        PassThru = $true
    }
    if ($null -ne $Environment) {
        $startEnvironment = @{}
        foreach ($entry in $Environment.GetEnumerator()) {
            $startEnvironment["$($entry.Key)"] = if ($null -eq $entry.Value) { '' } else { "$($entry.Value)" }
        }
        $startParameters['Environment'] = $startEnvironment
    }
    if ($IsWindows) { $startParameters['WindowStyle'] = 'Hidden' }
    $process = Start-Process @startParameters
    try {
        return [pscustomobject]@{
            Pid = $process.Id
            ProcessStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
        }
    }
    finally {
        $process.Dispose()
    }
}

function Start-ManagedBackgroundProcess {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name,

        [string] $LogDirectory
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
    }

    $resolvedLogDirectory = New-ScriptAutomationLogDirectory -Name $Name -LogDirectory $LogDirectory
    $stdoutPath = Join-Path $resolvedLogDirectory 'stdout.log'
    $stderrPath = Join-Path $resolvedLogDirectory 'stderr.log'

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Command
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $stdoutStream = [System.IO.FileStream]::new($stdoutPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    $stderrStream = [System.IO.FileStream]::new($stderrPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $state = @{ Disposed = $false }
    $copyCancellation = [System.Threading.CancellationTokenSource]::new()

    if (-not $process.Start()) {
        $stdoutStream.Dispose()
        $stderrStream.Dispose()
        $copyCancellation.Dispose()
        $process.Dispose()
        throw "Failed to start background process '$Command'."
    }

    $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutStream, $copyCancellation.Token)
    $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrStream, $copyCancellation.Token)
    $stopProcessTree = ${function:Stop-ProcessTree}
    $protectLogFile = ${function:Protect-ScriptAutomationLogFile}
    $writeDiagnostic = ${function:Write-Diagnostic}

    Write-Diagnostic "Started background process $Command (pid=$($process.Id), cwd=$WorkingDirectory, logs=$resolvedLogDirectory)"

    $stopBlock = {
        param(
            [string] $Reason = 'Managed background stop'
        )

        if ($state.Disposed) {
            return
        }

        try {
            if ($process -and -not $process.HasExited) {
                & $stopProcessTree -ProcessId $process.Id -Reason $Reason | Out-Null
            }

            if ($process) {
                [void] $process.WaitForExit(1000)
                if (-not $process.HasExited) {
                    & $writeDiagnostic -Level 'WARN' -Message "Background process did not exit promptly after stop request: $Command (pid=$($process.Id))"
                }
            }
        }
        finally {
            $state.Disposed = $true

            $copyTasks = @(
                [pscustomobject]@{ Name = 'stdout'; Task = $stdoutTask },
                [pscustomobject]@{ Name = 'stderr'; Task = $stderrTask }
            )
            $copyTimedOut = $false

            foreach ($copyTask in $copyTasks) {
                if (-not $copyTask.Task) {
                    continue
                }

                $copyCompleted = $false
                try {
                    $copyCompleted = $copyTask.Task.Wait(1000)
                }
                catch {
                    $copyCompleted = $true
                }

                if (-not $copyCompleted) {
                    $copyTimedOut = $true
                    & $writeDiagnostic -Level 'WARN' -Message "Timed out while collecting background $($copyTask.Name) log for $Command."
                }
            }

            if ($copyTimedOut) {
                $copyCancellation.Cancel()
            }

            foreach ($copyTask in $copyTasks) {
                if (-not $copyTask.Task) {
                    continue
                }

                if (-not $copyTask.Task.IsCompleted) {
                    try {
                        [void] $copyTask.Task.Wait(1000)
                    }
                    catch {
                    }
                }

                if (-not $copyTask.Task.IsCompleted) {
                    throw "Background $($copyTask.Name) log copy did not complete after cancellation for $Command; refusing to dispose its stream while copy is still active."
                }

                if ($copyTask.Task.IsCanceled) {
                    continue
                }

                try {
                    [void] $copyTask.Task.GetAwaiter().GetResult()
                }
                catch {
                    if (-not $copyTimedOut) {
                        throw
                    }

                    & $writeDiagnostic -Level 'WARN' -Message "Background $($copyTask.Name) log copy ended after cancellation for $Command`: $($_.Exception.Message)"
                }
            }

            $stdoutStream.Dispose()
            $stderrStream.Dispose()
            $copyCancellation.Dispose()
            & $protectLogFile -Path $stdoutPath
            & $protectLogFile -Path $stderrPath
            $process.Dispose()
        }
    }.GetNewClosure()

    return [pscustomobject]@{
        Process = $process
        ProcessId = $process.Id
        Command = $Command
        Arguments = $Arguments
        WorkingDirectory = $WorkingDirectory
        LogDirectory = $resolvedLogDirectory
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        Stop = $stopBlock
    }
}

function Use-ScopedEnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [AllowNull()]
        [string] $Value,

        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock
    )

    $hadValue = Test-Path "Env:$Name"
    $oldValue = [Environment]::GetEnvironmentVariable($Name, 'Process')

    try {
        if ($null -eq $Value) {
            Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$Name" $Value
        }

        & $ScriptBlock
    }
    finally {
        if ($hadValue) {
            Set-Item "Env:$Name" $oldValue
        }
        else {
            Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-WithScopedEnvironment {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Variables,

        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock
    )

    $originals = @{}

    foreach ($key in $Variables.Keys) {
        $originals[$key] = [pscustomobject]@{
            HadValue = Test-Path "Env:$key"
            Value = [Environment]::GetEnvironmentVariable($key, 'Process')
        }
    }

    try {
        foreach ($key in $Variables.Keys) {
            if ($null -eq $Variables[$key]) {
                Remove-Item "Env:$key" -ErrorAction SilentlyContinue
            }
            else {
                Set-Item "Env:$key" $Variables[$key]
            }
        }

        & $ScriptBlock
    }
    finally {
        foreach ($key in $originals.Keys) {
            if ($originals[$key].HadValue) {
                Set-Item "Env:$key" $originals[$key].Value
            }
            else {
                Remove-Item "Env:$key" -ErrorAction SilentlyContinue
            }
        }
    }
}

function New-ExclusiveInvocationClaim {
    <#
    .SYNOPSIS
        Atomically claims a single-owner invocation ID.
    .DESCRIPTION
        Uses FileMode.CreateNew so that exactly one caller can own a claim path. Concurrent callers
        racing on the same ID lose deterministically at the file system level rather than through a
        check-then-write window, so existing evidence can never be replaced by a rerun.
    #>
    param(
        [Parameter(Mandatory)]
        [string] $ClaimPath,

        [Parameter(Mandatory)]
        [string] $InvocationId
    )

    $claimStream = $null
    try {
        $claimStream = [System.IO.FileStream]::new(
            $ClaimPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::Read)
        $claimBytes = [System.Text.UTF8Encoding]::new($false).GetBytes("$InvocationId$([Environment]::NewLine)")
        $claimStream.Write($claimBytes, 0, $claimBytes.Length)
        $claimStream.Flush($true)
    }
    catch [System.IO.IOException] {
        if (Test-Path -LiteralPath $ClaimPath -PathType Leaf) {
            throw "Evidence invocation '$InvocationId' is already claimed at $ClaimPath. Use a new invocation ID; reruns never replace prior evidence."
        }
        throw
    }
    finally {
        if ($null -ne $claimStream) {
            $claimStream.Dispose()
        }
    }

    return $ClaimPath
}

function Assert-FacadeTypesGenExport {
    <#
    .SYNOPSIS
        Facade-coverage (MAN-475 / #841) assertion for an `exposed` endpoint: the
        generated request/response type is queryable in the api-client `types.gen.ts`
        AND the operation is re-exported from the stable barrel. Use this in the
        focused verify script of any issue that declares an endpoint `exposed`, so a
        silently-dropped facade type or barrel export fails the focused gate — not
        only the full contract test. See docs/architecture/facade-coverage-matrix.md.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]] $TypeName,

        [Parameter(Mandatory)]
        [string[]] $ExportName,

        [ValidateSet('business-console', 'console')]
        [string] $Surface = 'business-console',

        [string] $RepoRoot
    )

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Get-ScriptAutomationRepoRoot
    }

    $apiClientSrc = Join-Path $RepoRoot 'frontend/packages/api-client/src'
    $typesPath = Join-Path $apiClientSrc "generated/$Surface/types.gen.ts"
    $barrelPath = Join-Path $apiClientSrc "$Surface.ts"

    foreach ($path in @($typesPath, $barrelPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Assert-FacadeTypesGenExport: expected api-client file not found: $path"
        }
    }

    $typesContent = Get-Content -LiteralPath $typesPath -Raw
    $barrelContent = Get-Content -LiteralPath $barrelPath -Raw

    $missing = New-Object System.Collections.Generic.List[string]

    foreach ($type in $TypeName) {
        # Word-boundary match so a substring of a longer identifier does not pass.
        if ($typesContent -notmatch "\b$([regex]::Escape($type))\b") {
            $missing.Add("type '$type' not found in generated/$Surface/types.gen.ts")
        }
    }

    foreach ($export in $ExportName) {
        if ($barrelContent -notmatch "\b$([regex]::Escape($export))\b") {
            $missing.Add("export '$export' not re-exported from stable barrel $Surface.ts")
        }
    }

    if ($missing.Count -gt 0) {
        throw ("Facade-coverage export assertion failed (docs/architecture/facade-coverage-matrix.md):`n  - " + ($missing -join "`n  - "))
    }

    Write-Diagnostic "Facade-coverage export assertion passed: $($TypeName.Count) type(s) in $Surface types.gen.ts, $($ExportName.Count) export(s) in $Surface.ts."
}
