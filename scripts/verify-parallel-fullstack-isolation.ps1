# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Creates disposable linked worktrees and starts isolated full-stack sessions
#   Writes:
#     - Machine-state worktrees, manifests, logs, and retained acceptance artifacts
#   Cleanup:
#     - Stops every recorded session and removes only validated acceptance worktrees
#   Requires:
#     - PowerShell 7
#     - Git
#     - Docker
#     - Aspire CLI 13.4.x

[CmdletBinding()]
param(
    [ValidateRange(2, 3)] [int] $Sessions = 2,
    [switch] $NoBuild,
    [switch] $InjectFailure
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Assert-Acceptance([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Test-PathBelow([string] $Path, [string] $Parent) {
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return $resolvedPath.StartsWith($resolvedParent, $comparison)
}

function Stop-AcceptanceStartProcess([object] $Record) {
    if ($null -eq $Record.StartIdentity) { return }
    if (-not (Test-NervProcessIdentity -ProcessId $Record.StartIdentity.Pid -ProcessStartTimeUtc $Record.StartIdentity.ProcessStartTimeUtc)) { return }

    $process = Get-Process -Id $Record.StartIdentity.Pid -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    Stop-Process -Id $Record.StartIdentity.Pid -Force
    [void] $process.WaitForExit(10000)
}

function Wait-AcceptanceSessions([object[]] $Records, [int] $TimeoutSeconds = 900) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $pending = 0
        foreach ($record in $Records) {
            $manifestPath = Get-NervFullStackManifestPath -SessionId $record.SessionId
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                if ($null -ne $record.StartIdentity -and -not (Test-NervProcessIdentity -ProcessId $record.StartIdentity.Pid -ProcessStartTimeUtc $record.StartIdentity.ProcessStartTimeUtc)) {
                    $stderr = if (Test-Path -LiteralPath $record.StderrPath -PathType Leaf) { Get-Content -LiteralPath $record.StderrPath -Raw } else { '' }
                    $safeStderr = Protect-ScriptAutomationText -Text $stderr
                    if ($safeStderr.Length -gt 2000) { $safeStderr = $safeStderr.Substring($safeStderr.Length - 2000) }
                    throw "Acceptance start process for '$($record.SessionId)' exited before creating a manifest. $safeStderr"
                }
                $pending++
                continue
            }
            $manifest = Read-NervFullStackManifest -SessionId $record.SessionId
            if ("$($manifest.state)" -eq 'Failed' -or "$($manifest.state)" -eq 'CleanupFailed') {
                throw "Acceptance session '$($record.SessionId)' entered $($manifest.state)."
            }
            if ("$($manifest.state)" -ne 'Running') { $pending++ }
        }
        if ($pending -eq 0) { return }
        Start-Sleep -Seconds 2
    }
    throw "Timed out waiting for $($Records.Count) parallel full-stack sessions."
}

if ($IsWindows -and [string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_STATE_ROOT)) {
    $env:NERV_IIP_FULLSTACK_STATE_ROOT = 'C:\nfs'
}
$stateRoot = Get-NervFullStackStateRoot
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $stateRoot "fullstack-worktrees/$runId"
$worktreeParent = $runRoot
$archiveRoot = Join-Path $runRoot 'artifacts'
[System.IO.Directory]::CreateDirectory($worktreeParent) | Out-Null
[System.IO.Directory]::CreateDirectory($archiveRoot) | Out-Null
Assert-Acceptance (Test-PathBelow -Path $worktreeParent -Parent (Join-Path $stateRoot 'fullstack-worktrees')) 'Acceptance worktrees escaped the machine-state root.'

$records = [System.Collections.Generic.List[object]]::new()
$primaryFailure = $null
$cleanupFailures = [System.Collections.Generic.List[string]]::new()
$injectedFailureObserved = $false
try {
    for ($index = 1; $index -le $Sessions; $index++) {
        $worktreePath = Join-Path $worktreeParent "s$index"
        Assert-Acceptance (Test-PathBelow -Path $worktreePath -Parent $worktreeParent) "Unsafe worktree path '$worktreePath'."
        Invoke-NativeCommandWithTimeout `
            -Command 'git' `
            -Arguments @('worktree', 'add', '--detach', $worktreePath, 'HEAD') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 120 `
            -Name "parallel-fullstack-worktree-$index" | Out-Null
        $record = [pscustomobject]@{
            Index = $index
            WorktreePath = $worktreePath
            SessionId = New-NervFullStackSessionId -WorktreeRoot $worktreePath
            AdminPassword = $null
            StartIdentity = $null
            StderrPath = $null
            Manifest = $null
        }
        $records.Add($record)
        $env:CI = 'true'
        try {
            Invoke-PwshScript `
                -ScriptPath (Join-Path $worktreePath 'scripts/setup-worktree.ps1') `
                -WorkingDirectory $worktreePath `
                -TimeoutSeconds 900 `
                -Name "parallel-fullstack-setup-$index" | Out-Null
        }
        finally { Remove-Item Env:CI -ErrorAction SilentlyContinue }
        Assert-Acceptance (Test-Path -LiteralPath (Join-Path $worktreePath 'frontend/node_modules') -PathType Container) "Worktree $index frontend dependencies were not installed."

        $sessionId = $record.SessionId
        $adminPassword = New-NervFullStackSecretValue -Bytes 24
        $stdoutPath = Join-Path $runRoot "start-$index.stdout.log"
        $stderrPath = Join-Path $runRoot "start-$index.stderr.log"
        $startArguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $worktreePath 'scripts/fullstack-session.ps1'),
            'start', '-SessionId', $sessionId
        )
        if ($NoBuild) { $startArguments += '-NoBuild' }
        $env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $adminPassword
        try {
            $startIdentity = Start-DetachedManagedProcess `
                -Command (Get-Process -Id $PID).Path `
                -Arguments $startArguments `
                -WorkingDirectory $worktreePath `
                -StdoutPath $stdoutPath `
                -StderrPath $stderrPath
        }
        finally { Remove-Item Env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD -ErrorAction SilentlyContinue }
        $record.AdminPassword = $adminPassword
        $record.StartIdentity = $startIdentity
        $record.StderrPath = $stderrPath
    }

    Wait-AcceptanceSessions -Records @($records)
    foreach ($record in $records) { $record.Manifest = Read-NervFullStackManifest -SessionId $record.SessionId }

    if ($InjectFailure) {
        $injectedFailureObserved = $true
        throw 'Intentional parallel full-stack acceptance failure.'
    }

    foreach ($resourceName in @('gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
        $urls = @($records | ForEach-Object { Get-NervFullStackEndpointValue -Manifest $_.Manifest -ResourceName $resourceName })
        Assert-Acceptance (@($urls | Select-Object -Unique).Count -eq $Sessions) "Endpoint '$resourceName' was not isolated."
    }
    foreach ($volumeKey in @('NERV_IIP_POSTGRES_VOLUME', 'NERV_IIP_REDIS_VOLUME', 'NERV_IIP_MINIO_VOLUME', 'NERV_IIP_VICTORIA_LOGS_VOLUME')) {
        $names = @($records | ForEach-Object { (Get-NervFullStackEnvironment -SessionId $_.SessionId)[$volumeKey] })
        Assert-Acceptance (@($names | Select-Object -Unique).Count -eq $Sessions) "Volume contract '$volumeKey' was not isolated."
    }

    foreach ($record in $records) {
        $browserEnvironment = @{
            NERV_IIP_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $record.Manifest -ResourceName 'gateway'
            NERV_IIP_BUSINESS_GATEWAY_URL = Get-NervFullStackEndpointValue -Manifest $record.Manifest -ResourceName 'business-gateway'
            PLAYWRIGHT_BASE_URL = Get-NervFullStackEndpointValue -Manifest $record.Manifest -ResourceName 'business-console'
            NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $record.AdminPassword
        }
        try {
            foreach ($entry in $browserEnvironment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
            Invoke-Pnpm `
                -Arguments @('-C', 'frontend', '--filter', '@nerv-iip/business-console', 'exec', 'playwright', 'test', 'e2e/fullstack-proxy.spec.ts', '--project=desktop', '--reporter=line', '--output', (Join-Path "$($record.Manifest.artifactPath)" 'test-results')) `
                -WorkingDirectory $record.WorktreePath `
                -TimeoutSeconds 300 `
                -Name "parallel-fullstack-browser-$($record.Index)" | Out-Null
        }
        finally {
            foreach ($key in $browserEnvironment.Keys) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
            $record.AdminPassword = $null
        }
    }

    $firstPostgres = @(Get-NervFullStackContainerRecords -OwnedSessionId $records[0].SessionId | Where-Object { "$($_.resourceName)" -eq 'postgres' })
    $secondPostgres = @(Get-NervFullStackContainerRecords -OwnedSessionId $records[1].SessionId | Where-Object { "$($_.resourceName)" -eq 'postgres' })
    Assert-Acceptance ($firstPostgres.Count -eq 1 -and $secondPostgres.Count -eq 1) 'Each running session must own one canonical postgres container.'
    Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments @('exec', '--user', 'postgres', "$($firstPostgres[0].id)", 'psql', '-U', 'postgres', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', "create table nerv_fullstack_isolation_probe(value text); insert into nerv_fullstack_isolation_probe values ('session-one');") `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 30 `
        -Name 'parallel-fullstack-postgres-write' | Out-Null
    $isolationRead = Invoke-NativeCommandOutput `
        -Command 'docker' `
        -Arguments @('exec', '--user', 'postgres', "$($secondPostgres[0].id)", 'psql', '-U', 'postgres', '-d', 'postgres', '-Atc', "select to_regclass('public.nerv_fullstack_isolation_probe');") `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 30 `
        -Name 'parallel-fullstack-postgres-isolation-read'
    Assert-Acceptance ([string]::IsNullOrWhiteSpace("$($isolationRead.Stdout)")) 'PostgreSQL probe leaked into the second session.'

    Invoke-PwshScript `
        -ScriptPath (Join-Path $records[0].WorktreePath 'scripts/fullstack-session.ps1') `
        -Arguments @('stop', '-SessionId', $records[0].SessionId) `
        -WorkingDirectory $records[0].WorktreePath `
        -TimeoutSeconds 300 `
        -Name 'parallel-fullstack-first-stop' | Out-Null
    Invoke-WebRequest -Uri (Get-NervFullStackEndpointValue -Manifest $records[1].Manifest -ResourceName 'gateway') -TimeoutSec 30 -UseBasicParsing -SkipHttpErrorCheck | Out-Null
}
catch {
    if ($InjectFailure -and $_.Exception.Message -eq 'Intentional parallel full-stack acceptance failure.') {
        $injectedFailureObserved = $true
    }
    else { $primaryFailure = $_ }
}
finally {
    foreach ($record in $records) {
        try {
            $record.AdminPassword = $null
            Stop-AcceptanceStartProcess -Record $record
            $manifestPath = Get-NervFullStackManifestPath -SessionId $record.SessionId
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                Invoke-PwshScript `
                    -ScriptPath (Join-Path $record.WorktreePath 'scripts/fullstack-session.ps1') `
                    -Arguments @('stop', '-SessionId', $record.SessionId) `
                    -WorkingDirectory $record.WorktreePath `
                    -TimeoutSeconds 300 `
                    -Name "parallel-fullstack-final-stop-$($record.Index)" | Out-Null
                $manifest = Read-NervFullStackManifest -SessionId $record.SessionId
                Assert-Acceptance ("$($manifest.state)" -eq 'Stopped') "Session '$($record.SessionId)' did not stop."
                $archivePath = Join-Path $archiveRoot $record.SessionId
                if (Test-Path -LiteralPath "$($manifest.artifactPath)" -PathType Container) {
                    Copy-Item -LiteralPath "$($manifest.artifactPath)" -Destination $archivePath -Recurse -Force
                    $manifest.artifactPath = $archivePath
                    Write-NervFullStackManifest -Manifest $manifest
                }
            }
        }
        catch { $cleanupFailures.Add("$($record.SessionId): $($_.Exception.Message)") }
    }
    try {
        Invoke-PwshScript -ScriptPath (Join-Path $repoRoot 'scripts/fullstack-session.ps1') -Arguments @('gc') -WorkingDirectory $repoRoot -TimeoutSeconds 300 -Name 'parallel-fullstack-gc' | Out-Null
    }
    catch { $cleanupFailures.Add("gc: $($_.Exception.Message)") }
    foreach ($record in @($records | Sort-Object Index -Descending)) {
        try {
            Assert-Acceptance (Test-PathBelow -Path $record.WorktreePath -Parent $worktreeParent) "Refusing unsafe worktree removal '$($record.WorktreePath)'."
            Invoke-NativeCommandWithTimeout `
                -Command 'git' `
                -Arguments @('worktree', 'remove', '--force', $record.WorktreePath) `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 300 `
                -Name "parallel-fullstack-worktree-remove-$($record.Index)" | Out-Null
        }
        catch { $cleanupFailures.Add("worktree $($record.Index): $($_.Exception.Message)") }
    }
}

if ($cleanupFailures.Count -gt 0) {
    $primaryMessage = if ($primaryFailure) { Protect-ScriptAutomationText -Text "$($primaryFailure.Exception.Message)" } else { 'none' }
    if ($primaryMessage.Length -gt 4000) { $primaryMessage = $primaryMessage.Substring(0, 4000) }
    throw "Parallel acceptance failed. Primary failure: $primaryMessage Cleanup failures: $($cleanupFailures -join ' | ')"
}
if ($InjectFailure) {
    Assert-Acceptance $injectedFailureObserved 'Injected failure was not observed.'
    Write-Host 'Parallel full-stack injected-failure cleanup acceptance passed.'
    exit 0
}
if ($primaryFailure) { throw $primaryFailure }
Write-Host "Parallel full-stack isolation acceptance passed for $Sessions sessions. Artifacts: $archiveRoot"
