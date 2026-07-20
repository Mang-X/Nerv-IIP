# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts and inspects session-owned Aspire and Docker resources
#   Writes:
#     - Local full-stack session manifests and artifacts
#   Cleanup:
#     - Stop and GC are installed by the lifecycle task
#   Requires:
#     - PowerShell 7
#     - Aspire CLI 13.4.x
#     - Docker

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('run', 'start', 'url', 'status', 'logs', 'stop', 'list', 'gc', 'help')]
    [string] $Action = 'help',
    [Parameter(Position = 1)] [string] $Target,
    [ValidateSet('smoke')] [string] $Scenario = 'smoke',
    [string] $SessionId,
    [switch] $NoBuild,
    [int] $Tail = 120,
    [switch] $Follow
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

function Write-NervFullStackHelp {
    Write-Host @'
Nerv-IIP isolated full-stack sessions

Usage:
  .\nerv.ps1 fullstack run -Scenario smoke [-NoBuild]
  .\nerv.ps1 fullstack start [-SessionId nerv-abcd-123456] [-NoBuild]
  .\nerv.ps1 fullstack url <gateway|business-gateway|console|business-console|screen> [-SessionId ...]
  .\nerv.ps1 fullstack status [-SessionId ...]
  .\nerv.ps1 fullstack logs [resource] [-SessionId ...] [-Tail 120] [-Follow]
  .\nerv.ps1 fullstack stop [-SessionId ...]
  .\nerv.ps1 fullstack list
  .\nerv.ps1 fullstack gc
'@
}

function Resolve-NervFullStackSessionId {
    param([string] $RequestedSessionId)

    if (-not [string]::IsNullOrWhiteSpace($RequestedSessionId)) {
        [void] (Get-NervFullStackManifestPath -SessionId $RequestedSessionId)
        return $RequestedSessionId
    }

    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $matches = @(Get-NervFullStackManifests | Where-Object {
        "$($_.state)" -ne 'Stopped' -and
        [string]::Equals([System.IO.Path]::GetFullPath("$($_.worktreeRoot)"), $repoRoot, $comparison)
    })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one active full-stack session for '$repoRoot', found $($matches.Count); specify -SessionId."
    }
    return "$($matches[0].sessionId)"
}

function Get-NervFullStackGuardianIntervalSeconds {
    if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_GUARDIAN_INTERVAL_SECONDS)) { return 60 }
    $seconds = 0
    if (-not [int]::TryParse($env:NERV_IIP_FULLSTACK_GUARDIAN_INTERVAL_SECONDS, [ref] $seconds) -or $seconds -lt 1 -or $seconds -gt 3600) {
        throw 'NERV_IIP_FULLSTACK_GUARDIAN_INTERVAL_SECONDS must be an integer from 1 through 3600.'
    }
    return $seconds
}

function Start-NervFullStackGuardian {
    param(
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [ValidateSet('Automated', 'Interactive')] [string] $Mode,
        [int] $CoordinatorPid,
        [string] $CoordinatorStartTimeUtc
    )

    $guardianScript = Join-Path $repoRoot 'scripts/fullstack-guardian.ps1'
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $guardianScript,
        '-SessionId', "$($Manifest.sessionId)",
        '-Mode', $Mode,
        '-IntervalSeconds', "$(Get-NervFullStackGuardianIntervalSeconds)",
        '-StateRoot', (Get-NervFullStackStateRoot)
    )
    if ($Mode -eq 'Automated') {
        $arguments += @('-CoordinatorPid', "$CoordinatorPid", '-CoordinatorStartTimeUtc', $CoordinatorStartTimeUtc)
    }
    return (Start-DetachedManagedProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments $arguments `
        -WorkingDirectory $repoRoot `
        -StdoutPath (Join-Path "$($Manifest.artifactPath)" 'guardian.stdout.log') `
        -StderrPath (Join-Path "$($Manifest.artifactPath)" 'guardian.stderr.log'))
}

function Start-NervFullStackSession {
    param(
        [ValidateSet('Automated', 'Interactive')] [string] $GuardianMode = 'Interactive',
        [int] $CoordinatorPid,
        [string] $CoordinatorStartTimeUtc,
        [string] $SessionAdminPassword,
        [switch] $PassThru
    )

    $staleSessionIds = @(Claim-NervStaleFullStackSessions -TimeoutSeconds 300)
    foreach ($staleSessionId in $staleSessionIds) {
        [void] (Stop-NervFullStackSession -SessionId $staleSessionId)
    }

    $createdManifest = Invoke-WithNervFullStackSessionLock -TimeoutSeconds 300 -ScriptBlock {
        $admission = Test-NervFullStackAdmission -WorktreeRoot $repoRoot
        if (-not $admission.Allowed) {
            throw "Full-stack session admission denied: $($admission.Reason) ($($admission.ActiveCount)/$($admission.MaximumSessions))."
        }

        $newSessionId = if ([string]::IsNullOrWhiteSpace($SessionId)) {
            do {
                $candidateSessionId = New-NervFullStackSessionId -WorktreeRoot $repoRoot
            } while (-not (Test-NervFullStackSessionIdAvailable -SessionId $candidateSessionId))
            $candidateSessionId
        }
        else {
            [void] (Get-NervFullStackManifestPath -SessionId $SessionId)
            if (-not (Test-NervFullStackSessionIdAvailable -SessionId $SessionId)) {
                throw "Full-stack session ID '$SessionId' already exists and cannot be overwritten."
            }
            $SessionId
        }
        $appHostProject = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
        $artifactPath = Join-Path $repoRoot "artifacts/fullstack/$newSessionId"
        [System.IO.Directory]::CreateDirectory($artifactPath) | Out-Null
        $sessionProfile = Get-NervFullStackEnvironment -SessionId $newSessionId
        $manifest = New-NervFullStackManifest `
            -SessionId $newSessionId `
            -WorktreeRoot $repoRoot `
            -AppHostProject $appHostProject `
            -ArtifactPath $artifactPath `
            -MessagingProvider $sessionProfile.Messaging__Provider `
            -LeaseMinutes (Get-NervFullStackLeaseMinutes)
        Write-NervFullStackManifest -Manifest $manifest

        return $manifest
    }

    $manifest = $createdManifest
    $newSessionId = "$($manifest.sessionId)"
    $appHostProject = "$($manifest.appHostProject)"

    $sessionEnvironment = Get-NervFullStackEnvironment -SessionId $newSessionId
        $sessionEnvironment['ASPIRE_CLI_START_TIMEOUT'] = '300'
        $sessionEnvironment['MSBUILDDISABLENODEREUSE'] = '1'
        $sessionEnvironment['DOTNET_CLI_USE_MSBUILD_SERVER'] = '0'
        $manifest = Update-NervFullStackManifest -SessionId $newSessionId -AllowedStates @('Creating') -UpdateAction {
            param($latest)
            $latest.runtime.volumeNames = @(
                $sessionEnvironment.NERV_IIP_POSTGRES_VOLUME,
                $sessionEnvironment.NERV_IIP_REDIS_VOLUME,
                $sessionEnvironment.NERV_IIP_MINIO_VOLUME,
                $sessionEnvironment.NERV_IIP_VICTORIA_LOGS_VOLUME
            )
            return $latest
        }
        $secretSet = New-NervFullStackSecretEnvironment -SessionId $newSessionId
        $suppliedAdminPassword = if (-not [string]::IsNullOrWhiteSpace($SessionAdminPassword)) {
            $SessionAdminPassword
        }
        else {
            $env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD
        }
        $childEnvironmentKeys = @(
            @($sessionEnvironment.Keys) + @($secretSet.Environment.Keys) + @('NERV_IIP_FULLSTACK_ADMIN_PASSWORD') |
                Select-Object -Unique
        )
        $originalEnvironment = @{}
        foreach ($key in $childEnvironmentKeys) {
            $originalEnvironment[$key] = [pscustomobject]@{
                HadValue = Test-Path -LiteralPath "Env:$key"
                Value = [Environment]::GetEnvironmentVariable($key, 'Process')
            }
        }
        Remove-Item Env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($suppliedAdminPassword)) {
            $secretSet.Environment['Parameters__iam-seed-admin-password'] = $suppliedAdminPassword
        }
        try {
            foreach ($entry in $sessionEnvironment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
            foreach ($entry in $secretSet.Environment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
            $arguments = @('start', '--isolated', '--format', 'Json', '--apphost', $appHostProject, '--non-interactive', '--nologo')
            if ($NoBuild) { $arguments += '--no-build' }
            $startResult = Invoke-NervAspireStartWithRetry `
                -StartAction {
                    Invoke-AspireOutput `
                        -Arguments $arguments `
                        -WorkingDirectory $repoRoot `
                        -TimeoutSeconds 660 `
                        -Name "fullstack-$newSessionId-aspire-start"
                } `
                -CleanupAction {
                    try {
                        Invoke-AspireOutput `
                            -Arguments @('stop', '--apphost', $appHostProject, '--non-interactive', '--nologo') `
                            -WorkingDirectory $repoRoot `
                            -TimeoutSeconds 150 `
                            -Name "fullstack-$newSessionId-transient-start-stop" | Out-Null
                    }
                    catch { }
                }
            $startObject = Read-NervAspireJson -Text "$($startResult.Stdout)"
            $identity = Get-NervAspireStartIdentity -StartObject $startObject

            $appHostProcess = Get-Process -Id $identity.AppHostPid -ErrorAction Stop
            $cliProcess = Get-Process -Id $identity.CliPid -ErrorAction Stop
            $appHostStartedAt = $appHostProcess.StartTime.ToUniversalTime().ToString('O')
            $cliStartedAt = $cliProcess.StartTime.ToUniversalTime().ToString('O')
            $manifest = Update-NervFullStackManifest -SessionId $newSessionId -AllowedStates @('Creating') -UpdateAction {
                param($latest)
                $latest.aspire.appHostId = $identity.AppHostId
                $latest.aspire.dcpId = $null
                $latest.aspire.appHostPath = $identity.AppHostPath
                $latest.aspire.appHostPid = $identity.AppHostPid
                $latest.aspire.appHostProcessStartTimeUtc = $appHostStartedAt
                $latest.aspire.cliPid = $identity.CliPid
                $latest.aspire.cliProcessStartTimeUtc = $cliStartedAt
                $latest.aspire.logFile = $identity.LogFile
                $latest.coordinator.pid = $identity.AppHostPid
                $latest.coordinator.processStartTimeUtc = $appHostStartedAt
                $latest.runtime.processIds = @($identity.AppHostPid, $identity.CliPid)
                return $latest
            }

            foreach ($resourceName in @('iam', 'business-master-data', 'gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
                Wait-NervAspireResource `
                    -AppHostProject $appHostProject `
                    -ResourceName $resourceName `
                    -WorkingDirectory $repoRoot
            }
            $containerRecords = @(Get-NervFullStackContainerRecords -OwnedSessionId $newSessionId)
            $networkIds = @(Get-NervFullStackDcpNetworkIds `
                -SessionId $newSessionId `
                -ContainerRecords $containerRecords `
                -WorkingDirectory $repoRoot)
            $describe = Get-NervAspireDescribeObject -AppHostProject $appHostProject -WorkingDirectory $repoRoot
            $manifest = Update-NervFullStackManifest -SessionId $newSessionId -AllowedStates @('Creating') -UpdateAction {
                param($latest)
                $latest.runtime.containers = @($containerRecords)
                $latest.runtime.containerIds = @($containerRecords | ForEach-Object { "$($_.id)" })
                $latest.runtime.networkIds = @($networkIds)
                $latest = Save-NervFullStackEndpoints -Manifest $latest -DescribeObject $describe
                $latest = Move-NervFullStackSessionState -Manifest $latest -State Running
                $latest = Renew-NervFullStackLease -Manifest $latest -LeaseMinutes (Get-NervFullStackLeaseMinutes)
                return $latest
            }
            $manifest = Invoke-WithNervFullStackSessionLock -ScriptBlock {
                $latest = Read-NervFullStackManifest -SessionId $newSessionId
                if ("$($latest.state)" -ne 'Running') {
                    throw "Session '$newSessionId' is '$($latest.state)'; guardian registration requires Running."
                }
                $guardianIdentity = Start-NervFullStackGuardian `
                    -Manifest $latest `
                    -Mode $GuardianMode `
                    -CoordinatorPid $CoordinatorPid `
                    -CoordinatorStartTimeUtc $CoordinatorStartTimeUtc
                try {
                    $latest.guardian = [ordered]@{
                        pid = $guardianIdentity.Pid
                        processStartTimeUtc = $guardianIdentity.ProcessStartTimeUtc
                        mode = $GuardianMode
                    }
                    $latest.coordinator = if ($GuardianMode -eq 'Automated') {
                        [ordered]@{ pid = $CoordinatorPid; processStartTimeUtc = $CoordinatorStartTimeUtc }
                    }
                    else {
                        [ordered]@{ pid = $guardianIdentity.Pid; processStartTimeUtc = $guardianIdentity.ProcessStartTimeUtc }
                    }
                    Write-NervFullStackManifest -Manifest $latest
                    return $latest
                }
                catch {
                    Stop-ProcessTree -ProcessId $guardianIdentity.Pid -Reason "Failed guardian registration cleanup for $newSessionId" | Out-Null
                    if (Test-NervProcessIdentity -ProcessId $guardianIdentity.Pid -ProcessStartTimeUtc $guardianIdentity.ProcessStartTimeUtc) {
                        throw "Guardian registration failed and guardian process $($guardianIdentity.Pid) could not be stopped."
                    }
                    throw
                }
            }
        }
        catch {
            $safeError = Protect-ScriptAutomationText -Text "$($_.Exception.Message)"
            $manifest = Update-NervFullStackManifest `
                -SessionId $newSessionId `
                -AllowedStates @('Creating', 'Running', 'Collecting', 'Failed') `
                -ReturnUnchangedOnStateMismatch `
                -UpdateAction {
                    param($latest)
                    if ("$($latest.state)" -ne 'Failed') {
                        $latest = Move-NervFullStackSessionState -Manifest $latest -State Failed
                    }
                    $latest.failure = [ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); message = $safeError }
                    return $latest
                }
            try {
                $cleanupResult = Stop-NervFullStackSession -SessionId $newSessionId
                if (-not $cleanupResult.Complete) {
                    throw "Startup cleanup remains incomplete: $($cleanupResult.Remaining -join ', ')."
                }
            }
            catch {
                $cleanupError = Protect-ScriptAutomationText -Text "$($_.Exception.Message)"
                throw "$safeError Startup cleanup failed: $cleanupError"
            }
            throw $safeError
        }
        finally {
            foreach ($key in $childEnvironmentKeys) {
                if ($originalEnvironment[$key].HadValue) {
                    Set-Item -LiteralPath "Env:$key" -Value $originalEnvironment[$key].Value
                }
                else {
                    Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
                }
            }
            $secretSet.Environment.Clear()
            $suppliedAdminPassword = $null
            $secretSet = $null
        }
    $createdManifest = $manifest

    if ($PassThru) { return $createdManifest }

    Write-Output "$($createdManifest.sessionId)"
    $endpointEntries = if ($createdManifest.endpoints -is [System.Collections.IDictionary]) {
        @($createdManifest.endpoints.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{ Name = "$($_.Key)"; Value = "$($_.Value)" }
        })
    }
    else {
        @($createdManifest.endpoints.PSObject.Properties)
    }
    foreach ($entry in $endpointEntries) {
        Write-Output "$($entry.Name)=$($entry.Value)"
    }
}

try {
    switch ($Action) {
        'help' { Write-NervFullStackHelp }
        'start' { Start-NervFullStackSession }
        'list' {
            foreach ($manifest in Get-NervFullStackManifests) {
                Write-Output "$($manifest.sessionId) state=$($manifest.state) worktree=$($manifest.worktreeRoot) lease=$($manifest.leaseExpiresAtUtc)"
            }
        }
        'status' {
            $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId
            $manifest = Renew-NervFullStackSessionLease -SessionId $resolvedSessionId -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            Write-Output "$resolvedSessionId state=$($manifest.state) containers=$(@($manifest.runtime.containerIds).Count)"
        }
        'url' {
            if ([string]::IsNullOrWhiteSpace($Target)) { throw 'fullstack url requires a resource target.' }
            try { $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId }
            catch { throw "Cannot resolve URL target '$Target': $($_.Exception.Message)" }
            $manifest = Renew-NervFullStackSessionLease -SessionId $resolvedSessionId -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            $endpoint = $manifest.endpoints.PSObject.Properties[$Target]
            if ($null -eq $endpoint) { throw "Session '$resolvedSessionId' has no endpoint named '$Target'." }
            Write-Output "$($endpoint.Value)"
        }
        'logs' {
            $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId
            $manifest = Renew-NervFullStackSessionLease -SessionId $resolvedSessionId -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            $arguments = @('logs')
            if (-not [string]::IsNullOrWhiteSpace($Target)) { $arguments += $Target }
            $arguments += @('--tail', "$Tail", '--apphost', "$($manifest.appHostProject)", '--non-interactive', '--nologo')
            if ($Follow) { $arguments += '--follow' }
            Invoke-AspireInteractive -Arguments $arguments -WorkingDirectory "$($manifest.worktreeRoot)" -Name "fullstack-$resolvedSessionId-logs"
        }
        'run' {
            if ([string]::IsNullOrWhiteSpace($SessionId)) { $SessionId = New-NervFullStackSessionId -WorktreeRoot $repoRoot }
            $runProcess = Get-Process -Id $PID
            $sessionAdminPassword = New-NervFullStackSecretValue -Bytes 24
            try {
                $runResult = Invoke-NervManagedFullStackRun `
                    -StartAction {
                        Start-NervFullStackSession `
                            -GuardianMode Automated `
                            -CoordinatorPid $PID `
                            -CoordinatorStartTimeUtc $runProcess.StartTime.ToUniversalTime().ToString('O') `
                            -SessionAdminPassword $sessionAdminPassword `
                            -PassThru
                    } `
                    -ScenarioAction {
                        param($InputManifest)
                        switch ($Scenario) {
                            'smoke' {
                                Invoke-NervFullStackSmokeScenario `
                                    -Manifest $InputManifest `
                                    -SessionAdminPassword $sessionAdminPassword | Out-Null
                            }
                        }
                    } `
                    -ResolveFailedManifestAction {
                        Read-NervFullStackManifest -SessionId $SessionId
                    } `
                    -FailureAction {
                        param($InputManifest, $FailureRecord)
                        $failureMessage = Protect-NervFullStackDiagnosticText -Text "$($FailureRecord.Exception.Message)" -SensitiveValues @($sessionAdminPassword)
                        Update-NervFullStackManifest `
                            -SessionId "$($InputManifest.sessionId)" `
                            -AllowedStates @('Creating', 'Running', 'Collecting', 'Failed') `
                            -ReturnUnchangedOnStateMismatch `
                            -UpdateAction {
                                param($latest)
                                $latest.failure = [ordered]@{
                                    atUtc = [DateTimeOffset]::UtcNow.ToString('O')
                                    category = 'ScenarioOrStartup'
                                    message = $failureMessage
                                }
                                return $latest
                            } | Out-Null
                    } `
                    -CollectAction {
                        param($InputManifest)
                        $latest = Update-NervFullStackManifest `
                            -SessionId "$($InputManifest.sessionId)" `
                            -AllowedStates @('Running') `
                            -ReturnUnchangedOnStateMismatch `
                            -UpdateAction {
                                param($current)
                                return (Move-NervFullStackSessionState -Manifest $current -State Collecting)
                            }
                        if ("$($latest.state)" -eq 'Collecting') {
                            Collect-NervFullStackDiagnostics -Manifest $latest -SensitiveValues @($sessionAdminPassword) | Out-Null
                        }
                    } `
                    -CollectionFailureAction {
                        param($InputManifest, $FailureRecord)
                        $safeError = Protect-NervFullStackDiagnosticText -Text "$($FailureRecord.Exception.Message)" -SensitiveValues @($sessionAdminPassword)
                        Update-NervFullStackManifest `
                            -SessionId "$($InputManifest.sessionId)" `
                            -AllowedStates @('Collecting', 'Failed') `
                            -ReturnUnchangedOnStateMismatch `
                            -UpdateAction {
                                param($latest)
                                $latest.cleanup.errors = @($latest.cleanup.errors) + @($safeError)
                                return $latest
                            } | Out-Null
                    } `
                    -StopAction {
                        param($InputManifest)
                        Stop-NervFullStackSession -SessionId "$($InputManifest.sessionId)"
                    }
                $manifest = $runResult.Manifest
            }
            finally {
                $sessionAdminPassword = $null
            }
            Write-Output "$($manifest.sessionId) state=$($manifest.state) artifacts=$($manifest.artifactPath)"
        }
        'stop' {
            $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId
            $result = Stop-NervFullStackSession -SessionId $resolvedSessionId
            Write-Output "$resolvedSessionId state=$($result.Manifest.state) remaining=$($result.Remaining.Count)"
            if (-not $result.Complete) { throw "Session '$resolvedSessionId' cleanup remains incomplete: $($result.Remaining -join ', ')." }
        }
        'gc' {
            $staleSessionIds = @(Claim-NervStaleFullStackSessions)
            $failures = [System.Collections.Generic.List[string]]::new()
            foreach ($staleSessionId in $staleSessionIds) {
                try {
                    $result = Stop-NervFullStackSession -SessionId $staleSessionId
                    Write-Output "$staleSessionId state=$($result.Manifest.state) remaining=$($result.Remaining.Count)"
                    if (-not $result.Complete) { $failures.Add($staleSessionId) }
                }
                catch { $failures.Add($staleSessionId) }
            }
            if ($failures.Count -gt 0) { throw "GC could not fully clean sessions: $($failures -join ', ')." }
        }
    }
}
catch {
    Write-Error (Protect-ScriptAutomationText -Text "$($_.Exception.Message)")
    exit 1
}
