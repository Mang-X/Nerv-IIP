# Script-Governance:
#   Category: check
#   SideEffects:
#     - Provides shared script automation helpers
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed child process trees when requested
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

function Get-ScriptAutomationRepoRoot {
    $root = Resolve-Path (Join-Path $PSScriptRoot '../..')
    return $root.Path
}

function Protect-ScriptAutomationText {
    param(
        [AllowNull()]
        [string] $Text
    )

    if ($null -eq $Text) {
        return $null
    }

    $redacted = $Text
    $patterns = @(
        '(?i)(authorization\s*[:=]\s*bearer\s+)[^\s''"]+',
        '(?i)(password\s*=\s*)[^;\s]+',
        '(?i)(pwd\s*=\s*)[^;\s]+',
        '(?i)(token\s*[:=]\s*)[^\s''";]+',
        '(?i)(secret\s*[:=]\s*)[^\s''";]+',
        '(?i)(client_secret\s*[:=]\s*)[^\s''";]+',
        '(?i)(user-secrets\s+set\s+["'']?[^"''\s]+["'']?\s+)[^\s]+',
        '(?i)(Host=[^;]+;Port=[^;]+;Database=[^;]+;Username=[^;]+;Password=)[^;]+'
    )

    foreach ($pattern in $patterns) {
        $redacted = [regex]::Replace($redacted, $pattern, '$1<redacted>')
    }

    return $redacted
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
        [string] $Content
    )

    [System.IO.File]::WriteAllText($Path, (Protect-ScriptAutomationText $Content), [System.Text.UTF8Encoding]::new($false))
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

    return [pscustomobject]@{
        RequestedProcessId = $ProcessId
        Reason = $Reason
        StoppedProcessIds = @($stopped)
        MissingProcessIds = @($missing)
    }
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

        [int[]] $SensitiveArgumentIndexes = @()
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

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.EnableRaisingEvents = $true

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $stdoutTask = $null
    $stderrTask = $null
    $rootProcessId = $null

    try {
        $displayArguments = Protect-ScriptAutomationArguments -Arguments $Arguments -SensitiveArgumentIndexes $SensitiveArgumentIndexes
        Write-Diagnostic "Starting $Command $displayArguments (cwd=$WorkingDirectory, timeout=${TimeoutSeconds}s, logs=$resolvedLogDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        $rootProcessId = $process.Id
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $timedOut = $true
            Write-Diagnostic -Level 'ERROR' -Message "Command timed out: $Command (pid=$rootProcessId, timeout=${TimeoutSeconds}s, logs=$resolvedLogDirectory)"
            $cleanup = Stop-ProcessTree -ProcessId $rootProcessId -Reason "Timeout while running $Command"
            [void] $process.WaitForExit(5000)
            if ($stdoutTask) {
                Write-ScriptAutomationProcessLog -Path $stdoutPath -Content $stdoutTask.GetAwaiter().GetResult()
            }
            if ($stderrTask) {
                Write-ScriptAutomationProcessLog -Path $stderrPath -Content $stderrTask.GetAwaiter().GetResult()
            }
            throw "Command '$Command' timed out after $TimeoutSeconds seconds. Stopped PIDs: $($cleanup.StoppedProcessIds -join ', '). Logs: $resolvedLogDirectory"
        }

        $process.WaitForExit()
        Write-ScriptAutomationProcessLog -Path $stdoutPath -Content $stdoutTask.GetAwaiter().GetResult()
        Write-ScriptAutomationProcessLog -Path $stderrPath -Content $stderrTask.GetAwaiter().GetResult()

        $exitCode = $process.ExitCode
        $stopwatch.Stop()

        if ($exitCode -ne 0) {
            throw "Command '$Command' exited with $exitCode after $($stopwatch.Elapsed). Logs: $resolvedLogDirectory"
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
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for $Command" | Out-Null
        }

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

        [int[]] $SensitiveArgumentIndexes = @()
    )

    Invoke-NativeCommandWithTimeout -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name -SensitiveArgumentIndexes $SensitiveArgumentIndexes
}

function Invoke-NativeCommandOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 60,

        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
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

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        $displayArguments = Protect-ScriptAutomationText ($Arguments -join ' ')
        Write-Diagnostic "Reading command output for $Name`: $Command $displayArguments (cwd=$WorkingDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Timeout while reading output for $Command" | Out-Null
            throw "Command '$Command' timed out after $TimeoutSeconds seconds while reading output."
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        if ($process.ExitCode -ne 0) {
            throw "Command '$Command' exited with $($process.ExitCode). Output: $(Protect-ScriptAutomationText (($stdout, $stderr) -join [Environment]::NewLine))"
        }

        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Diagnostic -Level 'WARN' -Message "Stderr from ${Name}: $stderr"
        }

        return [pscustomobject]@{
            Command = $Command
            Arguments = $Arguments
            WorkingDirectory = $WorkingDirectory
            ExitCode = $process.ExitCode
            Stdout = $stdout
            Stderr = $stderr
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for output command $Command" | Out-Null
        }

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

        [string] $Name
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

        [string] $Name = 'aspire'
    )

    Invoke-NativeCommandOutput -Command (Get-AspireCliCommand) -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
}

function Invoke-AspireInteractive {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name = 'aspire'
    )

    Invoke-NativeCommandInteractive -Command (Get-AspireCliCommand) -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Name $Name
}

function Invoke-Pnpm {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'pnpm'
    )

    if ($IsWindows) {
        return Invoke-NativeCommandWithTimeout -Command 'cmd' -Arguments (@('/d', '/s', '/c', 'pnpm') + $Arguments) -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
    }

    Invoke-NativeCommandWithTimeout -Command 'pnpm' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
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

        [string] $Name = 'pwsh-script'
    )

    $fullArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Arguments
    Invoke-NativeCommandWithTimeout -Command 'pwsh' -Arguments $fullArguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
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
        [Parameter(Mandatory)] [string] $StderrPath
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
