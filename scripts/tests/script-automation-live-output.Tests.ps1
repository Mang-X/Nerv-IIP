# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts owned native child probes
#   Writes:
#     - Temporary probe scripts and logs under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary root in finally
#     - Managed helpers stop the owned probe process trees
#   Requires:
#     - PowerShell 7

# Regression / PublicContract: #3165. Real pwsh processes; the A/ack/B handshake
# proves visibility before exit without depending on a lucky sleep duration.
param(
    [string] $LibraryPath = (Join-Path $PSScriptRoot '../lib/ScriptAutomation.ps1'),
    [switch] $SkipMutation
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $LibraryPath
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-live-output-$([Guid]::NewGuid().ToString('N'))"
$pwshPath = (Get-Process -Id $PID).Path
$script:observed = [Text.StringBuilder]::new()
$script:releasePath = Join-Path $probeRoot 'release'

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Host {
    param([object] $Object, [switch] $NoNewline)
    [void] $script:observed.Append([string] $Object)
    if (-not $NoNewline) { [void] $script:observed.AppendLine() }
    if ([string]::Equals([string] $Object, ('stage-A' + [Environment]::NewLine), [StringComparison]::Ordinal)) {
        [IO.File]::WriteAllText($script:releasePath, 'ack')
    }
}

try {
    [IO.Directory]::CreateDirectory($probeRoot) | Out-Null
    $childPath = Join-Path $probeRoot 'child.ps1'
    [IO.File]::WriteAllText($childPath, @'
param([string] $ReleasePath, [string] $Mode)
if ([string]::Equals($Mode, 'handshake', [StringComparison]::Ordinal)) {
    [Console]::Out.WriteLine('stage-A')
    [Console]::Out.Flush()
    while (-not [IO.File]::Exists($ReleasePath)) { Start-Sleep -Milliseconds 10 }
    [Console]::Out.WriteLine('stage-B')
}
elseif ([string]::Equals($Mode, 'dual', [StringComparison]::Ordinal)) {
    for ($i = 0; $i -lt 2000; $i++) {
        [Console]::Out.WriteLine("out-$i-" + ([char]::ConvertFromUtf32(128512) * 40))
        [Console]::Error.WriteLine("err-$i-" + ('y' * 80))
    }
}
elseif ([string]::Equals($Mode, 'timeout', [StringComparison]::Ordinal)) {
    if ($IsLinux -or $IsWindows) {
        $child = if ($IsLinux) {
            [Diagnostics.Process]::Start('/bin/sleep', '60')
        }
        else {
            Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') -PassThru
        }
        [IO.File]::WriteAllText($ReleasePath + '.pid', [string] $child.Id)
        [IO.File]::WriteAllText($ReleasePath + '.start', [string] $child.StartTime.ToUniversalTime().Ticks)
    }
    [Console]::Out.WriteLine('before-timeout')
    [Console]::Out.Flush()
    Start-Sleep -Seconds 60
}
elseif ([string]::Equals($Mode, 'sensitive', [StringComparison]::Ordinal)) {
    $text = "safe-start`nknown-first`nknown-last`n`"token`": `"canary-json`"`n-----BEGIN PRIVATE KEY-----`ncanary-pem`n-----END PRIVATE KEY-----`nsafe-end`n"
    foreach ($character in $text.ToCharArray()) {
        [Console]::Out.Write($character)
        [Console]::Error.Write($character)
    }
}
elseif ([string]::Equals($Mode, 'oversize', [StringComparison]::Ordinal)) {
    [Console]::Out.Write("-----BEGIN PRIVATE KEY-----`ncanary-oversize" + ('z' * 80000) + "`n-----END PRIVATE KEY-----`nafter-oversize`n")
}
elseif ([string]::Equals($Mode, 'nonzero', [StringComparison]::Ordinal)) {
    [Console]::Out.WriteLine('before-nonzero')
    exit 7
}
else {
    [Console]::Out.WriteLine('default-child-marker')
    [Console]::Error.WriteLine('default-error-marker')
}
'@)
    $result = Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, 'handshake') -Name 'handshake' -TimeoutSeconds 10 -LogDirectory (Join-Path $probeRoot 'handshake') -LiveOutput
    Assert-Contract ([IO.File]::Exists($script:releasePath)) 'A must be visible before the child can exit.'
    $console = $script:observed.ToString()
    Assert-Contract (([regex]::Matches($console, 'stage-A')).Count -eq 1) 'A must be mirrored exactly once.'
    Assert-Contract (([regex]::Matches($console, 'stage-B')).Count -eq 1) 'B must be mirrored exactly once.'
    Assert-Contract ([IO.File]::ReadAllText($result.StdoutPath).Contains(('stage-A' + [Environment]::NewLine + 'stage-B' + [Environment]::NewLine), [StringComparison]::Ordinal)) 'Final stdout must retain A and B.'

    [void] $script:observed.Clear()
    $result = Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, 'default') -Name 'default' -TimeoutSeconds 10 -LogDirectory (Join-Path $probeRoot 'default')
    Assert-Contract (-not $script:observed.ToString().Contains('default-child-marker', [StringComparison]::Ordinal)) 'Default calls must not stream child output.'
    Assert-Contract (-not $script:observed.ToString().Contains('default-error-marker', [StringComparison]::Ordinal)) 'Default calls must not stream stderr.'

    [void] $script:observed.Clear()
    $result = Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, 'dual') -Name 'dual' -TimeoutSeconds 15 -LogDirectory (Join-Path $probeRoot 'dual') -LiveOutput
    $console = $script:observed.ToString()
    foreach ($stream in @('out', 'err')) {
        $path = if ([string]::Equals($stream, 'out', [StringComparison]::Ordinal)) { $result.StdoutPath } else { $result.StderrPath }
        $log = [IO.File]::ReadAllText($path)
        $counter = if ([string]::Equals($stream, 'out', [StringComparison]::Ordinal)) { 'stdoutBytes' } else { 'stderrBytes' }
        $bytes = 0L
        foreach ($match in [regex]::Matches($console, "$counter=(\d+)")) { $bytes += [long] $match.Groups[1].Value }
        Assert-Contract ($bytes -eq [Text.Encoding]::UTF8.GetByteCount($log)) 'Heartbeat byte increments must sum to actual UTF-8 stream bytes, including surrogate pairs.'
        for ($i = 0; $i -lt 2000; $i++) {
            $marker = "$stream-$i-"
            Assert-Contract (([regex]::Matches($console, [regex]::Escape($marker))).Count -eq 1) 'Both pipes must be drained and mirrored without duplicates.'
            Assert-Contract ($log.Contains($marker, [StringComparison]::Ordinal)) 'Both final logs must be complete.'
        }
    }

    [void] $script:observed.Clear()
    $result = Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, 'sensitive') -Name 'sensitive' -TimeoutSeconds 10 -LogDirectory (Join-Path $probeRoot 'sensitive') -SensitiveValues @("known-first`nknown-last") -LiveOutput
    foreach ($text in @($script:observed.ToString(), [IO.File]::ReadAllText($result.StdoutPath), [IO.File]::ReadAllText($result.StderrPath))) {
        foreach ($marker in @('known-first', 'known-last', 'canary-json', 'canary-pem')) {
            Assert-Contract (-not $text.Contains($marker, [StringComparison]::Ordinal)) 'Real dual-stream secrets must not leak through the console or logs.'
        }
        Assert-Contract ($text.Contains('safe-end', [StringComparison]::Ordinal)) 'Redaction must retain safe complete records.'
    }

    [void] $script:observed.Clear()
    $result = Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, 'oversize') -Name 'oversize' -TimeoutSeconds 10 -LogDirectory (Join-Path $probeRoot 'oversize') -LiveOutput
    Assert-Contract ($script:observed.ToString().Contains('textSuppressed=record-limit', [StringComparison]::Ordinal)) 'Oversized live records must emit a content-free suppression diagnostic.'
    Assert-Contract (-not $script:observed.ToString().Contains('canary-oversize', [StringComparison]::Ordinal)) 'Oversized sensitive text must not leak live.'
    Assert-Contract ([IO.File]::ReadAllText($result.StdoutPath).Contains('after-oversize', [StringComparison]::Ordinal)) 'Live suppression must not truncate final capture.'

    foreach ($mode in @('nonzero', 'timeout')) {
        [void] $script:observed.Clear()
        $failure = ''
        try {
            Invoke-NativeCommandWithTimeout -Command $pwshPath -Arguments @('-NoProfile', '-File', $childPath, $script:releasePath, $mode) -Name $mode -TimeoutSeconds 3 -LogDirectory (Join-Path $probeRoot $mode) -LiveOutput | Out-Null
        }
        catch { $failure = $_.Exception.Message }
        $expectedFailure = if ([string]::Equals($mode, 'timeout', [StringComparison]::Ordinal)) { 'timed out after 3 seconds' } else { 'exited with 7' }
        Assert-Contract ($failure.Contains($expectedFailure, [StringComparison]::Ordinal)) "Live output must preserve the native failure verdict for $mode. Actual: $failure"
        Assert-Contract ($script:observed.ToString().Contains("before-$mode", [StringComparison]::Ordinal)) "Failure must retain the last complete live record for $mode."
        if ([string]::Equals($mode, 'timeout', [StringComparison]::Ordinal)) {
            $console = $script:observed.ToString()
            Assert-Contract ($console.Contains('aliveCount=0', [StringComparison]::Ordinal)) 'Timeout cleanup must report the final empty process tree.'
            $rootMatch = [regex]::Match($console, 'rootPid=(\d+)')
            Assert-Contract ($rootMatch.Success) 'The timeout heartbeat must identify its root process.'
            Assert-Contract ($null -eq (Get-Process -Id ([int] $rootMatch.Groups[1].Value) -ErrorAction SilentlyContinue)) 'Timeout must stop its root.'
            if ($IsLinux -or $IsWindows) {
                $childId = [int] [IO.File]::ReadAllText($script:releasePath + '.pid')
                Assert-Contract ($null -eq (Get-Process -Id $childId -ErrorAction SilentlyContinue)) 'Timeout must stop its owned descendant.'
            }
        }
    }

    # R1: a valid 61,153-character record must not be rejected just because its
    # closing newline arrives in the same capture batch as 5,000 short records.
    [void] $script:observed.Clear()
    $pipeName = 'nl-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)
    $server = [IO.Pipes.NamedPipeServerStream]::new($pipeName, [IO.Pipes.PipeDirection]::In, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous)
    $client = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::Out)
    $connected = $server.WaitForConnectionAsync()
    $client.Connect(5000)
    [void] $connected.GetAwaiter().GetResult()
    $writer = [IO.StreamWriter]::new($client, [Text.UTF8Encoding]::new($false))
    $capture = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new([IO.StreamReader]::new($server))
    $empty = [Nerv.IIP.ScriptAutomation.RedirectedStreamCapture]::new([IO.StreamReader]::new([IO.MemoryStream]::new([byte[]] @())))
    $pipeState = @{ stdout = @{ Cursor = 0; Pending = ''; Bytes = 0L }; stderr = @{ Cursor = 0; Pending = ''; Bytes = 0L } }
    try {
        $expected = ''
        foreach ($batch in @(('x' * 61152), ("`n" + ('ok' + "`n") * 5000), "later`n")) {
            $writer.Write($batch)
            $writer.Flush()
            $expected += $batch
            $readClock = [Diagnostics.Stopwatch]::StartNew()
            # Test-only synchronization: make the full batch available before the
            # production incremental consumer runs, so the old defect cannot evade
            # the assertion through a lucky reader scheduling interleaving.
            while ($capture.Snapshot().Length -lt $expected.Length -and $readClock.ElapsedMilliseconds -lt 5000) {
                Start-Sleep -Milliseconds 10
            }
            Assert-Contract ($capture.Snapshot().Length -eq $expected.Length) 'The complete batch must reach capture before incremental consumption.'
            while ($pipeState.stdout.Cursor -lt $expected.Length -and $readClock.ElapsedMilliseconds -lt 5000) {
                Write-ScriptAutomationLiveOutput -State $pipeState -StdoutCapture $capture -StderrCapture $empty
                Start-Sleep -Milliseconds 10
            }
            Assert-Contract ($pipeState.stdout.Cursor -eq $expected.Length) 'The real pipe must reach each explicit capture/consume boundary.'
        }
        $writer.Dispose()
        Assert-Contract ($capture.Completion.Wait(5000)) 'The real pipe must drain to EOF.'
        Write-ScriptAutomationLiveOutput -State $pipeState -StdoutCapture $capture -StderrCapture $empty -Final
        Assert-Contract ([string]::Equals($script:observed.ToString(), $expected, [StringComparison]::Ordinal)) 'Chunk grouping must not suppress bounded complete records.'
    }
    finally {
        $writer.Dispose()
        $capture.Dispose()
        $empty.Dispose()
        $client.Dispose()
        $server.Dispose()
    }

    # Every possible chunk split must preserve the same authority's whole-text result.
    $canary = "known-first`nknown-last"
    $samples = @(
        "before`n$canary`nafter`n",
        "before`nsafe-prefix-----BEGIN PRIVATE KEY-----`ncanary-pem`n-----END PRIVATE KEY-----`nafter`n",
        "before`n`"token`": `"canary-json-first`ncanary-json-last`"`nafter`n",
        "before`npassword=`ncanary-password`nafter`n",
        "before`nHost=h;Port=1;Database=d;Username=u;Password=canary-db-first`ncanary-db-last;`nafter`n"
    )
    foreach ($sample in $samples) {
        $expected = Protect-ScriptAutomationText -Text $sample -SensitiveValues @($canary)
        for ($split = 1; $split -lt $sample.Length; $split++) {
            $state = @{ Pending = '' }
            $actual = Protect-ScriptAutomationText -Text $sample.Substring(0, $split) -SensitiveValues @($canary) -IncrementalState $state
            Assert-Contract ($actual.Length -eq 0 -or $actual.EndsWith("`n", [StringComparison]::Ordinal)) 'Live text must commit complete records only.'
            $actual += Protect-ScriptAutomationText -Text $sample.Substring($split) -SensitiveValues @($canary) -IncrementalState $state
            $actual += Protect-ScriptAutomationText -Text '' -SensitiveValues @($canary) -IncrementalState $state -Final
            Assert-Contract ([string]::Equals($actual, $expected, [StringComparison]::Ordinal)) "Incremental redaction must match the authority at split $split."
        }
    }
    $state = @{ Pending = '' }
    $unclosed = "-----BEGIN PRIVATE KEY-----`ncanary-unclosed`n"
    $safe = Protect-ScriptAutomationText -Text $unclosed -IncrementalState $state
    $safe += Protect-ScriptAutomationText -Text '' -IncrementalState $state -Final
    Assert-Contract (-not $safe.Contains('canary-unclosed', [StringComparison]::Ordinal)) 'An unclosed live tail must be suppressed at EOF.'
    Assert-Contract ($state.Pending.Length -eq 0) 'Final suppression must release pending text.'

    $state = @{ Pending = '' }
    for ($index = 0; $index -lt 10; $index++) {
        $safe = Protect-ScriptAutomationText -Text ('z' * 16384) -IncrementalState $state
        Assert-Contract ($safe.Length -eq 0) 'An oversized incomplete record must never be mirrored.'
        Assert-Contract ($state.Pending.Length -le 65536) 'Live pending text must remain bounded.'
    }
    if (-not $SkipMutation) {
        $source = [IO.File]::ReadAllText($LibraryPath)
        $mutations = @(
            @{ Name = 'redaction-bypass'; Anchor = '$safe = Protect-ScriptAutomationText -Text $increment -SensitiveValues $SensitiveValues -IncrementalState $streamState'; Replacement = '$safe = $increment'; Failure = 'Real dual-stream secrets must not leak' },
            @{ Name = 'cursor-reset'; Anchor = 'cursor += count;'; Replacement = 'cursor = 0;'; Failure = 'A must be mirrored exactly once' },
            @{ Name = 'pending-bound'; Anchor = '$pendingText.Length -gt 65536'; Replacement = '$pendingText.Length -gt 1048576'; Failure = 'Live pending text must remain bounded' },
            @{ Name = 'chunk-group-limit'; Anchor = '$recordLimitReached = $false'; Replacement = '$recordLimitReached = $Text.Length -gt 65536'; Failure = 'Chunk grouping must not suppress bounded complete records' }
        )
        foreach ($mutation in $mutations) {
            Assert-Contract (([regex]::Matches($source, [regex]::Escape($mutation.Anchor))).Count -eq 1) 'Mutation must match exactly one production anchor.'
            $mutatedPath = Join-Path $probeRoot ($mutation.Name + '.ps1')
            [IO.File]::WriteAllText($mutatedPath, $source.Replace($mutation.Anchor, $mutation.Replacement))
            $mutationOutput = & $pwshPath -NoProfile -File $PSCommandPath -LibraryPath $mutatedPath -SkipMutation 2>&1
            Assert-Contract ($LASTEXITCODE -ne 0) "Mutation $($mutation.Name) must turn the behavioral contract red."
            Assert-Contract (($mutationOutput -join "`n").Contains($mutation.Failure, [StringComparison]::Ordinal)) "Mutation $($mutation.Name) must fail its intended behavioral assertion."
            Microsoft.PowerShell.Utility\Write-Host "Mutation $($mutation.Name): RED (exit=$LASTEXITCODE)."
        }
    }
    Microsoft.PowerShell.Utility\Write-Host 'Script automation live-output contract passed.'
}
finally {
    Remove-Item Function:Write-Host
    if (Test-Path -LiteralPath ($script:releasePath + '.start')) {
        $ownedId = [int] [IO.File]::ReadAllText($script:releasePath + '.pid')
        $ownedStart = [long] [IO.File]::ReadAllText($script:releasePath + '.start')
        $owned = Get-Process -Id $ownedId -ErrorAction SilentlyContinue
        if ($null -ne $owned) {
            if ($owned.StartTime.ToUniversalTime().Ticks -eq $ownedStart) {
                Stop-Process -Id $ownedId -Force
                [void] $owned.WaitForExit(5000)
            }
            $owned.Dispose()
        }
    }
    if (Test-Path -LiteralPath $probeRoot) { Remove-Item -LiteralPath $probeRoot -Recurse -Force }
}
