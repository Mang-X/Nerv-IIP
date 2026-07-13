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

function Get-NervFullStackContainerRecords {
    param([Parameter(Mandatory)] [string] $OwnedSessionId)

    $ids = @(Get-NervDockerListedValues `
        -Arguments @('container', 'ls', '-a', '--no-trunc', '--filter', "label=com.nerv-iip.session=$OwnedSessionId", '--format', '{{.ID}}') `
        -WorkingDirectory $repoRoot `
        -Name "fullstack-$OwnedSessionId-container-discovery")
    $objects = @(Get-NervDockerInspectObjects `
        -Kind container `
        -Identifiers $ids `
        -WorkingDirectory $repoRoot `
        -Name "fullstack-$OwnedSessionId-container-discovery-inspect")
    return @($objects | ForEach-Object {
        $containerName = "$($_.Name)".TrimStart('/')
        $resourceName = @('postgres', 'redis', 'minio', 'victoria-logs') |
            Where-Object { $containerName.StartsWith("$_-", [StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($resourceName)) { $resourceName = $containerName }
        [ordered]@{
            resourceName = $resourceName
            id = "$($_.Id)"
            name = $containerName
        }
    })
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

    $staleSessionIds = @(Invoke-WithNervFullStackSessionLock -ScriptBlock {
        return @(Get-NervStaleFullStackSessions | ForEach-Object { "$($_.sessionId)" })
    })
    foreach ($staleSessionId in $staleSessionIds) {
        [void] (Stop-NervFullStackSession -SessionId $staleSessionId)
    }

    $createdManifest = Invoke-WithNervFullStackSessionLock -TimeoutSeconds 300 -ScriptBlock {
        $admission = Test-NervFullStackAdmission -WorktreeRoot $repoRoot
        if (-not $admission.Allowed) {
            throw "Full-stack session admission denied: $($admission.Reason) ($($admission.ActiveCount)/$($admission.MaximumSessions))."
        }

        $newSessionId = if ([string]::IsNullOrWhiteSpace($SessionId)) {
            New-NervFullStackSessionId -WorktreeRoot $repoRoot
        }
        else {
            [void] (Get-NervFullStackManifestPath -SessionId $SessionId)
            $SessionId
        }
        $appHostProject = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
        $artifactPath = Join-Path $repoRoot "artifacts/fullstack/$newSessionId"
        [System.IO.Directory]::CreateDirectory($artifactPath) | Out-Null
        $manifest = New-NervFullStackManifest `
            -SessionId $newSessionId `
            -WorktreeRoot $repoRoot `
            -AppHostProject $appHostProject `
            -ArtifactPath $artifactPath `
            -LeaseMinutes (Get-NervFullStackLeaseMinutes)
        Write-NervFullStackManifest -Manifest $manifest

        $sessionEnvironment = Get-NervFullStackEnvironment -SessionId $newSessionId
        $secretSet = New-NervFullStackSecretEnvironment -SessionId $newSessionId
        $suppliedAdminPassword = if (-not [string]::IsNullOrWhiteSpace($SessionAdminPassword)) {
            $SessionAdminPassword
        }
        else {
            $env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD
        }
        Remove-Item Env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD -ErrorAction SilentlyContinue
        if (-not [string]::IsNullOrWhiteSpace($suppliedAdminPassword)) {
            $secretSet.Environment['Parameters__iam-seed-admin-password'] = $suppliedAdminPassword
        }
        $childEnvironmentKeys = @($sessionEnvironment.Keys) + @($secretSet.Environment.Keys)

        try {
            foreach ($entry in $sessionEnvironment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
            foreach ($entry in $secretSet.Environment.GetEnumerator()) { Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value }
            $arguments = @('start', '--isolated', '--format', 'Json', '--apphost', $appHostProject, '--non-interactive', '--nologo')
            if ($NoBuild) { $arguments += '--no-build' }
            $startResult = Invoke-AspireOutput `
                -Arguments $arguments `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 660 `
                -Name "fullstack-$newSessionId-aspire-start"
            $startObject = Read-NervAspireJson -Text "$($startResult.Stdout)"
            $identity = Get-NervAspireStartIdentity -StartObject $startObject

            $manifest.aspire.appHostId = $identity.AppHostId
            $manifest.aspire.dcpId = $null
            $appHostProcess = Get-Process -Id $identity.AppHostPid -ErrorAction Stop
            $cliProcess = Get-Process -Id $identity.CliPid -ErrorAction Stop
            $manifest.aspire.appHostPath = $identity.AppHostPath
            $manifest.aspire.appHostPid = $identity.AppHostPid
            $manifest.aspire.appHostProcessStartTimeUtc = $appHostProcess.StartTime.ToUniversalTime().ToString('O')
            $manifest.aspire.cliPid = $identity.CliPid
            $manifest.aspire.cliProcessStartTimeUtc = $cliProcess.StartTime.ToUniversalTime().ToString('O')
            $manifest.aspire.logFile = $identity.LogFile
            $manifest.coordinator.pid = $identity.AppHostPid
            $manifest.coordinator.processStartTimeUtc = $manifest.aspire.appHostProcessStartTimeUtc
            $manifest.runtime.processIds = @($identity.AppHostPid, $identity.CliPid)
            $manifest.runtime.volumeNames = @(
                $sessionEnvironment.NERV_IIP_POSTGRES_VOLUME,
                $sessionEnvironment.NERV_IIP_REDIS_VOLUME,
                $sessionEnvironment.NERV_IIP_MINIO_VOLUME,
                $sessionEnvironment.NERV_IIP_VICTORIA_LOGS_VOLUME
            )
            Write-NervFullStackManifest -Manifest $manifest

            foreach ($resourceName in @('gateway', 'business-gateway', 'console', 'business-console', 'screen')) {
                Wait-NervAspireResource `
                    -AppHostProject $appHostProject `
                    -ResourceName $resourceName `
                    -WorkingDirectory $repoRoot
            }
            $manifest.runtime.containers = @(Get-NervFullStackContainerRecords -OwnedSessionId $newSessionId)
            $manifest.runtime.containerIds = @($manifest.runtime.containers | ForEach-Object { "$($_.id)" })
            $describe = Get-NervAspireDescribeObject -AppHostProject $appHostProject -WorkingDirectory $repoRoot
            $manifest = Save-NervFullStackEndpoints -Manifest $manifest -DescribeObject $describe
            $manifest = Move-NervFullStackSessionState -Manifest $manifest -State Running
            $manifest = Renew-NervFullStackLease -Manifest $manifest -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            Write-NervFullStackManifest -Manifest $manifest
            $guardianIdentity = Start-NervFullStackGuardian `
                -Manifest $manifest `
                -Mode $GuardianMode `
                -CoordinatorPid $CoordinatorPid `
                -CoordinatorStartTimeUtc $CoordinatorStartTimeUtc
            $manifest.guardian = [ordered]@{
                pid = $guardianIdentity.Pid
                processStartTimeUtc = $guardianIdentity.ProcessStartTimeUtc
                mode = $GuardianMode
            }
            $manifest.coordinator = if ($GuardianMode -eq 'Automated') {
                [ordered]@{ pid = $CoordinatorPid; processStartTimeUtc = $CoordinatorStartTimeUtc }
            }
            else {
                [ordered]@{ pid = $guardianIdentity.Pid; processStartTimeUtc = $guardianIdentity.ProcessStartTimeUtc }
            }
            Write-NervFullStackManifest -Manifest $manifest
        }
        catch {
            if ("$($manifest.state)" -eq 'Creating') {
                $manifest = Move-NervFullStackSessionState -Manifest $manifest -State Failed
            }
            $safeError = Protect-ScriptAutomationText -Text "$($_.Exception.Message)"
            $manifest.failure = [ordered]@{ atUtc = [DateTimeOffset]::UtcNow.ToString('O'); message = $safeError }
            Write-NervFullStackManifest -Manifest $manifest
            try {
                Invoke-AspireOutput `
                    -Arguments @('stop', '--apphost', $appHostProject, '--non-interactive', '--nologo') `
                    -WorkingDirectory $repoRoot `
                    -TimeoutSeconds 150 `
                    -Name "fullstack-$newSessionId-start-failure-stop" | Out-Null
            }
            catch { }
            throw $safeError
        }
        finally {
            foreach ($key in $childEnvironmentKeys) { Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue }
            $secretSet.Environment.Clear()
            $suppliedAdminPassword = $null
            $secretSet = $null
        }
        return $manifest
    }

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
            $manifest = Read-NervFullStackManifest -SessionId $resolvedSessionId
            $manifest = Renew-NervFullStackLease -Manifest $manifest -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            Write-NervFullStackManifest -Manifest $manifest
            Write-Output "$resolvedSessionId state=$($manifest.state) containers=$(@($manifest.runtime.containerIds).Count)"
        }
        'url' {
            if ([string]::IsNullOrWhiteSpace($Target)) { throw 'fullstack url requires a resource target.' }
            try { $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId }
            catch { throw "Cannot resolve URL target '$Target': $($_.Exception.Message)" }
            $manifest = Read-NervFullStackManifest -SessionId $resolvedSessionId
            $endpoint = $manifest.endpoints.PSObject.Properties[$Target]
            if ($null -eq $endpoint) { throw "Session '$resolvedSessionId' has no endpoint named '$Target'." }
            $manifest = Renew-NervFullStackLease -Manifest $manifest -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            Write-NervFullStackManifest -Manifest $manifest
            Write-Output "$($endpoint.Value)"
        }
        'logs' {
            $resolvedSessionId = Resolve-NervFullStackSessionId -RequestedSessionId $SessionId
            $manifest = Read-NervFullStackManifest -SessionId $resolvedSessionId
            $manifest = Renew-NervFullStackLease -Manifest $manifest -LeaseMinutes (Get-NervFullStackLeaseMinutes)
            Write-NervFullStackManifest -Manifest $manifest
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
                        $latest = Read-NervFullStackManifest -SessionId "$($InputManifest.sessionId)"
                        $latest.failure = [ordered]@{
                            atUtc = [DateTimeOffset]::UtcNow.ToString('O')
                            category = 'ScenarioOrStartup'
                            message = Protect-NervFullStackDiagnosticText -Text "$($FailureRecord.Exception.Message)" -SensitiveValues @($sessionAdminPassword)
                        }
                        Write-NervFullStackManifest -Manifest $latest
                    } `
                    -CollectAction {
                        param($InputManifest)
                        $latest = Read-NervFullStackManifest -SessionId "$($InputManifest.sessionId)"
                        if ("$($latest.state)" -eq 'Running') {
                            $latest = Move-NervFullStackSessionState -Manifest $latest -State Collecting
                            Write-NervFullStackManifest -Manifest $latest
                        }
                        Collect-NervFullStackDiagnostics -Manifest $latest -SensitiveValues @($sessionAdminPassword) | Out-Null
                    } `
                    -CollectionFailureAction {
                        param($InputManifest, $FailureRecord)
                        $latest = Read-NervFullStackManifest -SessionId "$($InputManifest.sessionId)"
                        $safeError = Protect-NervFullStackDiagnosticText -Text "$($FailureRecord.Exception.Message)" -SensitiveValues @($sessionAdminPassword)
                        $latest.cleanup.errors = @($latest.cleanup.errors) + @($safeError)
                        Write-NervFullStackManifest -Manifest $latest
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
            $staleSessionIds = @(Invoke-WithNervFullStackSessionLock -ScriptBlock {
                return @(Get-NervStaleFullStackSessions | ForEach-Object { "$($_.sessionId)" })
            })
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
