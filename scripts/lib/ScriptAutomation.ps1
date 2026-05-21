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
        '(?i)(Host=[^;]+;Port=[^;]+;Database=[^;]+;Username=[^;]+;Password=)[^;]+'
    )

    foreach ($pattern in $patterns) {
        $redacted = [regex]::Replace($redacted, $pattern, '$1<redacted>')
    }

    return $redacted
}

function New-ScriptAutomationLogDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [string] $LogDirectory
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

    $content = Get-Content $Path -Raw
    Write-ScriptAutomationProcessLog -Path $Path -Content $content
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

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.EnableRaisingEvents = $true

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $stdoutTask = $null
    $stderrTask = $null
    $rootProcessId = $null

    try {
        $displayArguments = Protect-ScriptAutomationText ($Arguments -join ' ')
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

        [string] $Name = 'dotnet'
    )

    Invoke-NativeCommandWithTimeout -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -TimeoutSeconds $TimeoutSeconds -Name $Name
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

function Invoke-Pnpm {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [int] $TimeoutSeconds = 600,

        [string] $Name = 'pnpm'
    )

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

    $process = Start-Process -FilePath $Command -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -NoNewWindow -PassThru
    Write-Diagnostic "Started background process $Command (pid=$($process.Id), cwd=$WorkingDirectory, logs=$resolvedLogDirectory)"

    $stopBlock = {
        param(
            [string] $Reason = 'Managed background stop'
        )

        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason $Reason | Out-Null
        }

        Protect-ScriptAutomationLogFile -Path $stdoutPath
        Protect-ScriptAutomationLogFile -Path $stderrPath
        $process.Dispose()
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
