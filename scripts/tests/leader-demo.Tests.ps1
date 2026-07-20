# Script-Governance:
#   Category: check
#   SideEffects:
#     - Exercises leader-demo lifecycle functions with injected actions
#   Writes:
#     - Temporary machine-state fixtures
#   Cleanup:
#     - Removes temporary fixtures
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/leader-demo.ps1')

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Write-TestFullStackManifest {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $ManifestWorktreeRoot,
        [Parameter(Mandatory)] [string] $StateRoot
    )

    $manifest = New-NervFullStackManifest `
        -SessionId $SessionId `
        -WorktreeRoot $ManifestWorktreeRoot `
        -AppHostProject (Join-Path $ManifestWorktreeRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $ManifestWorktreeRoot "artifacts/fullstack/$SessionId")
    $manifest = Move-NervFullStackSessionState -Manifest $manifest -State Running
    Write-NervFullStackManifest -Manifest $manifest -StateRoot $StateRoot
}

function Write-TestReservedPointer {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [Parameter(Mandatory)] [string] $StateRoot,
        [Parameter(Mandatory)] [int] $OwnerPid,
        [Parameter(Mandatory)] [string] $OwnerProcessStartTimeUtc,
        [Parameter(Mandatory)] [string] $CreatedAtUtc
    )

    Write-NervLeaderDemoSessionPointer `
        -SessionId $SessionId `
        -WorktreeRoot $WorktreeRoot `
        -OwnershipState Reserved `
        -OwnerPid $OwnerPid `
        -OwnerProcessStartTimeUtc $OwnerProcessStartTimeUtc `
        -CreatedAtUtc $CreatedAtUtc `
        -StateRoot $StateRoot | Out-Null
}

function Write-TestLegacyReservedPointer {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [Parameter(Mandatory)] [string] $StateRoot,
        [Parameter(Mandatory)] [DateTimeOffset] $LastWriteTimeUtc
    )

    $path = Get-NervLeaderDemoSessionPointerPath -StateRoot $StateRoot
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    $pointer = [ordered]@{
        schemaVersion = 1
        sessionId = $SessionId
        worktreeRoot = [System.IO.Path]::GetFullPath($WorktreeRoot)
        ownershipState = 'Reserved'
        updatedAtUtc = $LastWriteTimeUtc.ToString('O')
    }
    [System.IO.File]::WriteAllText(
        $path,
        ($pointer | ConvertTo-Json -Depth 5),
        [System.Text.UTF8Encoding]::new($false)
    )
    [System.IO.File]::SetLastWriteTimeUtc($path, $LastWriteTimeUtc.UtcDateTime)
    return $path
}

$stateRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-leader-demo-$([guid]::NewGuid().ToString('N'))"
$originalPassword = $env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD
$hadOriginalPassword = Test-Path Env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD
$script:startCalls = [System.Collections.Generic.List[string]]::new()
$script:stopCalls = [System.Collections.Generic.List[string]]::new()
$script:seedCalls = [System.Collections.Generic.List[string]]::new()
$script:healthCalls = [System.Collections.Generic.List[string]]::new()

$startAction = {
    param($SessionId)
    Assert-True ($env:NERV_IIP_LEADER_DEMO -ceq 'true') 'Start must opt in to the AppHost leader-demo profile.'
    Assert-True ($env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD -ceq 'controlled-local-password') 'The demo password must reach full-stack only through scoped process environment.'
    Write-TestFullStackManifest -SessionId $SessionId -ManifestWorktreeRoot $repoRoot -StateRoot $stateRoot
    $script:startCalls.Add($SessionId)
}
$stopAction = { param($SessionId) $script:stopCalls.Add($SessionId) }
$seedAction = { param($SessionId) $script:seedCalls.Add($SessionId) }
$healthAction = { param($SessionId) $script:healthCalls.Add($SessionId) }

try {
    $env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD = 'controlled-local-password'

    $currentProcess = Get-Process -Id $PID -ErrorAction Stop
    $currentProcessStartTimeUtc = $currentProcess.StartTime.ToUniversalTime()
    Assert-True (
        (Get-NervProcessIdentityStatus -ProcessId $PID -ProcessStartTimeUtc $currentProcessStartTimeUtc) -ceq 'Active'
    ) 'The real process probe must identify the exact live process identity as Active.'
    Assert-True (
        (Get-NervProcessIdentityStatus -ProcessId $PID -ProcessStartTimeUtc $currentProcessStartTimeUtc.AddSeconds(-1)) -ceq 'Mismatched'
    ) 'The real process probe must identify PID reuse/start-time mismatch as Mismatched.'
    Assert-True (
        (Get-NervProcessIdentityStatus -ProcessId ([int]::MaxValue) -ProcessStartTimeUtc $currentProcessStartTimeUtc) -ceq 'Absent'
    ) 'The real process probe must identify a missing PID as Absent.'
    $script:throwingStartTimeProbe = [pscustomobject]@{}
    $script:throwingStartTimeProbe | Add-Member -MemberType ScriptProperty -Name StartTime -Value { throw 'simulated StartTime access failure' }
    Assert-True (
        (Get-NervProcessIdentityStatus `
            -ProcessId 4107 `
            -ProcessStartTimeUtc $currentProcessStartTimeUtc `
            -ProcessLookupAction { param($ProcessId) $script:throwingStartTimeProbe }) -ceq 'Unknown'
    ) 'A process StartTime inspection failure must be Unknown rather than a reclaimable mismatch.'

    $lockPath = Get-NervLeaderDemoLifecycleLockPath -StateRoot $stateRoot
    [IO.Directory]::CreateDirectory((Split-Path -Parent $lockPath)) | Out-Null
    $heldLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $concurrentLockFailed = $false
        try {
            Invoke-WithNervLeaderDemoLifecycleLock -StateRoot $stateRoot -TimeoutSeconds 1 -ScriptBlock { throw 'must not enter a concurrently owned lifecycle' }
        }
        catch { $concurrentLockFailed = $_.Exception.Message.Contains('leader-demo lifecycle lock') }
        Assert-True $concurrentLockFailed 'A concurrent leader-demo lifecycle must fail before entering the protected action.'
    }
    finally {
        $heldLock.Dispose()
    }

    $reservedStateRoot = Join-Path $stateRoot 'reserved'
    $reservedSessionId = 'nerv-dead-000010'
    $reservationCreatedAt = [DateTimeOffset]::Parse('2026-07-20T00:00:00Z')
    $reservationOwnerStartedAt = '2026-07-19T23:59:00.0000000Z'
    Write-TestReservedPointer `
        -SessionId $reservedSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $reservedStateRoot `
        -OwnerPid 4101 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.ToString('O')
    $script:reservedStartCalls = 0
    $reservedFailed = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $reservedStateRoot `
            -WorktreeRoot $repoRoot `
            -UtcNow $reservationCreatedAt.AddHours(1) `
            -ReservationTtlSeconds 30 `
            -OwnerProcessIdentityAction { param($OwnerPid, $OwnerStartedAt) 'Active' } `
            -StartSessionAction { param($SessionId) $script:reservedStartCalls++ } | Out-Null
    }
    catch { $reservedFailed = $_.Exception.Message.Contains($reservedSessionId) }
    Assert-True $reservedFailed 'A Reserved ownership pointer must block another start for the same leader-demo slot.'
    Assert-True ($script:reservedStartCalls -eq 0) 'A Reserved ownership pointer must fail before starting another full-stack session.'
    Assert-True ((Read-NervLeaderDemoSessionPointer -StateRoot $reservedStateRoot).sessionId -ceq $reservedSessionId) 'An active owner must retain its Reserved pointer even after the stale TTL.'

    $legacyRecentRoot = Join-Path $stateRoot 'legacy-recent'
    $legacyRecentSessionId = 'nerv-dead-000019'
    $legacyLastWrite = [DateTimeOffset]::Parse('2026-07-20T01:00:00Z')
    $legacyRecentPath = Write-TestLegacyReservedPointer `
        -SessionId $legacyRecentSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $legacyRecentRoot `
        -LastWriteTimeUtc $legacyLastWrite
    $legacyRecentRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action stop `
            -StateRoot $legacyRecentRoot `
            -WorktreeRoot $repoRoot `
            -UtcNow $legacyLastWrite.AddSeconds(29) `
            -ReservationTtlSeconds 30 `
            -OwnerProcessIdentityAction { throw 'legacy reservation must not probe an unknowable owner' } `
            -StopSessionAction { param($SessionId) throw 'recent legacy reservation must not stop a session' } | Out-Null
    }
    catch { $legacyRecentRejected = $_.Exception.Message.Contains($legacyRecentSessionId) }
    Assert-True $legacyRecentRejected 'A recent legacy Reserved pointer must remain owned until its bounded TTL expires.'
    Assert-True (Test-Path -LiteralPath $legacyRecentPath -PathType Leaf) 'A recent legacy Reserved pointer must not be cleared.'

    $legacyStaleRoot = Join-Path $stateRoot 'legacy-stale'
    $legacyStaleSessionId = 'nerv-dead-000020'
    $legacyStalePath = Write-TestLegacyReservedPointer `
        -SessionId $legacyStaleSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $legacyStaleRoot `
        -LastWriteTimeUtc $legacyLastWrite
    $legacyStaleResult = Invoke-NervLeaderDemoCommand `
        -Action stop `
        -StateRoot $legacyStaleRoot `
        -WorktreeRoot $repoRoot `
        -UtcNow $legacyLastWrite.AddSeconds(31) `
        -ReservationTtlSeconds 30 `
        -OwnerProcessIdentityAction { throw 'legacy reservation must not probe an unknowable owner' } `
        -StopSessionAction { param($SessionId) throw 'stale legacy reservation has no session to stop' }
    Assert-True ("$legacyStaleResult" -match 'state=ReservationReclaimed') 'A stale legacy Reserved pointer must be reclaimable using its file timestamp fallback.'
    Assert-True (-not (Test-Path -LiteralPath $legacyStalePath)) 'A stale legacy Reserved pointer must release the dead slot.'

    $legacyManifestRoot = Join-Path $stateRoot 'legacy-manifest'
    $legacyManifestSessionId = 'nerv-dead-000021'
    $legacyManifestPath = Write-TestLegacyReservedPointer `
        -SessionId $legacyManifestSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $legacyManifestRoot `
        -LastWriteTimeUtc $legacyLastWrite
    Write-TestFullStackManifest -SessionId $legacyManifestSessionId -ManifestWorktreeRoot $repoRoot -StateRoot $legacyManifestRoot
    $legacyManifestRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $legacyManifestRoot `
            -WorktreeRoot $repoRoot `
            -UtcNow $legacyLastWrite.AddHours(1) `
            -ReservationTtlSeconds 30 `
            -OwnerProcessIdentityAction { throw 'manifest-backed legacy reservation must not probe its owner' } `
            -StartSessionAction { param($SessionId) throw 'manifest-backed legacy reservation must not start' } | Out-Null
    }
    catch { $legacyManifestRejected = $_.Exception.Message.Contains($legacyManifestSessionId) }
    Assert-True $legacyManifestRejected 'An authoritative manifest must preserve a stale legacy Reserved pointer.'
    Assert-True (Test-Path -LiteralPath $legacyManifestPath -PathType Leaf) 'A manifest-backed legacy Reserved pointer must retain its cleanup route.'

    $legacyForeignRoot = Join-Path $stateRoot 'legacy-foreign'
    $legacyForeignSessionId = 'nerv-dead-000022'
    $legacyForeignWorktree = Join-Path $stateRoot 'legacy-foreign-worktree'
    $legacyForeignPath = Write-TestLegacyReservedPointer `
        -SessionId $legacyForeignSessionId `
        -WorktreeRoot $legacyForeignWorktree `
        -StateRoot $legacyForeignRoot `
        -LastWriteTimeUtc $legacyLastWrite
    $legacyForeignRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action stop `
            -StateRoot $legacyForeignRoot `
            -WorktreeRoot $repoRoot `
            -UtcNow $legacyLastWrite.AddHours(1) `
            -ReservationTtlSeconds 30 `
            -OwnerProcessIdentityAction { throw 'foreign legacy reservation must not probe its owner' } `
            -StopSessionAction { param($SessionId) throw 'foreign legacy reservation must not stop a session' } | Out-Null
    }
    catch { $legacyForeignRejected = $_.Exception.Message.Contains($legacyForeignWorktree) }
    Assert-True $legacyForeignRejected 'A stale legacy Reserved pointer must not cross its recorded worktree boundary.'
    Assert-True (Test-Path -LiteralPath $legacyForeignPath -PathType Leaf) 'A foreign legacy Reserved pointer must not be cleared.'

    $unknownRecentRoot = Join-Path $stateRoot 'unknown-recent'
    $unknownRecentSessionId = 'nerv-dead-000017'
    Write-TestReservedPointer `
        -SessionId $unknownRecentSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $unknownRecentRoot `
        -OwnerPid 4105 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.AddSeconds(1).ToString('O')
    $unknownRecentRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $unknownRecentRoot `
            -WorktreeRoot $repoRoot `
            -UtcNow $reservationCreatedAt.AddSeconds(30) `
            -ReservationTtlSeconds 30 `
            -OwnerProcessIdentityAction { param($OwnerPid, $OwnerStartedAt) 'Unknown' } `
            -StartSessionAction { param($SessionId) throw 'recent unknown reservation must not start' } | Out-Null
    }
    catch { $unknownRecentRejected = $_.Exception.Message.Contains($unknownRecentSessionId) }
    Assert-True $unknownRecentRejected 'An unknown but non-stale reservation must remain owned.'
    Assert-True ((Read-NervLeaderDemoSessionPointer -StateRoot $unknownRecentRoot).sessionId -ceq $unknownRecentSessionId) 'A non-stale unknown reservation must not be cleared.'

    $unknownStaleRoot = Join-Path $stateRoot 'unknown-stale'
    $unknownStaleSessionId = 'nerv-dead-000018'
    Write-TestReservedPointer `
        -SessionId $unknownStaleSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $unknownStaleRoot `
        -OwnerPid 4106 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.ToString('O')
    $unknownStaleResult = Invoke-NervLeaderDemoCommand `
        -Action stop `
        -StateRoot $unknownStaleRoot `
        -WorktreeRoot $repoRoot `
        -UtcNow $reservationCreatedAt.AddSeconds(31) `
        -ReservationTtlSeconds 30 `
        -OwnerProcessIdentityAction { param($OwnerPid, $OwnerStartedAt) 'Unknown' } `
        -StopSessionAction { param($SessionId) throw 'stale unknown reservation has no session to stop' }
    Assert-True ("$unknownStaleResult" -match 'state=ReservationReclaimed') 'An unknown reservation past the bounded TTL with no manifest must be reclaimed.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $unknownStaleRoot))) 'A stale unknown reservation must release the dead slot.'

    $deadOwnerStateRoot = Join-Path $stateRoot 'dead-owner'
    $deadOwnerSessionId = 'nerv-dead-000014'
    Write-TestReservedPointer `
        -SessionId $deadOwnerSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $deadOwnerStateRoot `
        -OwnerPid 4102 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.ToString('O')
    $script:deadOwnerCleanupCalls = 0
    $deadOwnerStopResult = Invoke-NervLeaderDemoCommand `
        -Action stop `
        -StateRoot $deadOwnerStateRoot `
        -WorktreeRoot $repoRoot `
        -OwnerProcessIdentityAction { param($OwnerPid, $OwnerStartedAt) 'Absent' } `
        -StopSessionAction { param($SessionId) $script:deadOwnerCleanupCalls++ }
    Assert-True ($script:deadOwnerCleanupCalls -eq 0) 'A dead-owner reservation without a manifest must be reclaimed without invoking session cleanup.'
    Assert-True ("$deadOwnerStopResult" -match 'state=ReservationReclaimed') 'Stop must report abandoned reservation reclaim.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $deadOwnerStateRoot))) 'Stop must remove a definitively abandoned reservation with no manifest.'

    $pidReuseStateRoot = Join-Path $stateRoot 'pid-reuse'
    $pidReuseSessionId = 'nerv-dead-000015'
    Write-TestReservedPointer `
        -SessionId $pidReuseSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $pidReuseStateRoot `
        -OwnerPid 4103 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.ToString('O')
    $script:pidReuseIdentityChecked = $false
    $pidReuseReplacementId = Invoke-NervLeaderDemoCommand `
        -Action reset `
        -StateRoot $pidReuseStateRoot `
        -WorktreeRoot $repoRoot `
        -OwnerProcessIdentityAction {
            param($OwnerPid, $OwnerStartedAt)
            $script:pidReuseIdentityChecked =
                $OwnerPid -eq 4103 -and
                [DateTimeOffset]::Parse("$OwnerStartedAt") -eq [DateTimeOffset]::Parse($reservationOwnerStartedAt)
            return 'Mismatched'
        } `
        -StartSessionAction { param($SessionId) } `
        -SeedAction { param($SessionId) } `
        -HealthCheckAction { param($SessionId) }
    Assert-True $script:pidReuseIdentityChecked 'PID-reuse recovery must compare the recorded process start time, not PID alone.'
    Assert-True ($pidReuseReplacementId -ne $pidReuseSessionId) 'Reset must reclaim a start-time-mismatched reservation and allocate a fresh session.'

    $manifestOwnedReservationRoot = Join-Path $stateRoot 'reserved-with-manifest'
    $manifestOwnedSessionId = 'nerv-dead-000016'
    Write-TestReservedPointer `
        -SessionId $manifestOwnedSessionId `
        -WorktreeRoot $repoRoot `
        -StateRoot $manifestOwnedReservationRoot `
        -OwnerPid 4104 `
        -OwnerProcessStartTimeUtc $reservationOwnerStartedAt `
        -CreatedAtUtc $reservationCreatedAt.ToString('O')
    Write-TestFullStackManifest -SessionId $manifestOwnedSessionId -ManifestWorktreeRoot $repoRoot -StateRoot $manifestOwnedReservationRoot
    $script:manifestOwnedReplacementStarts = 0
    $manifestOwnedRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $manifestOwnedReservationRoot `
            -WorktreeRoot $repoRoot `
            -OwnerProcessIdentityAction { param($OwnerPid, $OwnerStartedAt) 'Absent' } `
            -StartSessionAction { param($SessionId) $script:manifestOwnedReplacementStarts++ } | Out-Null
    }
    catch { $manifestOwnedRejected = $_.Exception.Message.Contains($manifestOwnedSessionId) }
    Assert-True $manifestOwnedRejected 'A Reserved pointer with an authoritative manifest must never be reclaimed by start.'
    Assert-True ($script:manifestOwnedReplacementStarts -eq 0) 'Manifest-backed ownership must block replacement startup.'
    Assert-True ((Read-NervLeaderDemoSessionPointer -StateRoot $manifestOwnedReservationRoot).sessionId -ceq $manifestOwnedSessionId) 'Manifest-backed ownership must retain its sole cleanup route.'

    $finalizationStateRoot = Join-Path $stateRoot 'finalization'
    $finalizationSessionIds = [System.Collections.Generic.List[string]]::new()
    $finalizationCleanupIds = [System.Collections.Generic.List[string]]::new()
    $pointerWriteAction = {
        param($SessionId, $WorktreeRoot, $InputStateRoot, $OwnershipState, $OwnerPid, $OwnerProcessStartTimeUtc, $CreatedAtUtc)
        if ($OwnershipState -eq 'Current') { throw 'simulated pointer finalization failure' }
        Write-NervLeaderDemoSessionPointer `
            -SessionId $SessionId `
            -WorktreeRoot $WorktreeRoot `
            -OwnershipState $OwnershipState `
            -OwnerPid $OwnerPid `
            -OwnerProcessStartTimeUtc $OwnerProcessStartTimeUtc `
            -CreatedAtUtc $CreatedAtUtc `
            -StateRoot $InputStateRoot | Out-Null
    }
    $finalizationFailed = $false
    $finalizationError = ''
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $finalizationStateRoot `
            -WorktreeRoot $repoRoot `
            -StartSessionAction { param($SessionId) $finalizationSessionIds.Add($SessionId) } `
            -StopSessionAction { param($SessionId) $finalizationCleanupIds.Add($SessionId) } `
            -WritePointerAction $pointerWriteAction | Out-Null
    }
    catch {
        $finalizationFailed = $true
        $finalizationError = $_.Exception.Message
    }
    Assert-True $finalizationFailed 'Pointer finalization failure must fail start.'
    Assert-True ($finalizationError.Contains('simulated pointer finalization failure')) 'Start must preserve the original pointer finalization error.'
    Assert-True ($finalizationError.Contains('exact-session cleanup completed')) 'Start must append exact cleanup diagnostics to the original error.'
    Assert-True ($finalizationSessionIds.Count -eq 1) 'The finalization failure fixture must start exactly one session.'
    Assert-True ($finalizationCleanupIds.Count -eq 1 -and $finalizationCleanupIds[0] -ceq $finalizationSessionIds[0]) 'Pointer finalization failure must clean only the exact started session.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $finalizationStateRoot))) 'Successful compensation must remove the exact Reserved pointer.'

    $partialStartupStateRoot = Join-Path $stateRoot 'partial-startup'
    $partialStartupIds = [System.Collections.Generic.List[string]]::new()
    $partialStartupCleanupIds = [System.Collections.Generic.List[string]]::new()
    $script:partialStartupOwnershipAtCleanup = $null
    $partialStartupError = ''
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $partialStartupStateRoot `
            -WorktreeRoot $repoRoot `
            -StartSessionAction {
                param($SessionId)
                $partialStartupIds.Add($SessionId)
                Write-TestFullStackManifest -SessionId $SessionId -ManifestWorktreeRoot $repoRoot -StateRoot $partialStartupStateRoot
                throw 'simulated partial startup failure'
            } `
            -StopSessionAction {
                param($SessionId)
                $script:partialStartupOwnershipAtCleanup = (Read-NervLeaderDemoSessionPointer -StateRoot $partialStartupStateRoot).ownershipState
                $partialStartupCleanupIds.Add($SessionId)
            } | Out-Null
    }
    catch { $partialStartupError = $_.Exception.Message }
    Assert-True ($partialStartupError.Contains('simulated partial startup failure')) 'Partial-startup compensation must preserve the original startup error.'
    Assert-True ($partialStartupError.Contains('exact-session cleanup completed')) 'Partial-startup compensation must report successful exact cleanup.'
    Assert-True ($partialStartupIds.Count -eq 1) 'The partial-startup fixture must reserve exactly one session.'
    Assert-True ($partialStartupCleanupIds.Count -eq 1 -and $partialStartupCleanupIds[0] -ceq $partialStartupIds[0]) 'A partial startup with an authoritative manifest must clean only the exact reserved session.'
    Assert-True ($script:partialStartupOwnershipAtCleanup -ceq 'Reserved') 'The Reserved pointer must remain available until exact cleanup is confirmed.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $partialStartupStateRoot))) 'Successful partial-startup cleanup must remove the exact Reserved pointer.'

    $failedCleanupStateRoot = Join-Path $stateRoot 'partial-cleanup-failed'
    $failedCleanupIds = [System.Collections.Generic.List[string]]::new()
    $failedCleanupError = ''
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $failedCleanupStateRoot `
            -WorktreeRoot $repoRoot `
            -StartSessionAction {
                param($SessionId)
                Write-TestFullStackManifest -SessionId $SessionId -ManifestWorktreeRoot $repoRoot -StateRoot $failedCleanupStateRoot
                throw 'simulated startup failure before cleanup failure'
            } `
            -StopSessionAction {
                param($SessionId)
                $failedCleanupIds.Add($SessionId)
                throw 'simulated partial-startup cleanup failure'
            } | Out-Null
    }
    catch { $failedCleanupError = $_.Exception.Message }
    Assert-True ($failedCleanupError.Contains('simulated startup failure before cleanup failure')) 'Failed compensation must preserve the original startup error.'
    Assert-True ($failedCleanupError.Contains('simulated partial-startup cleanup failure')) 'Failed compensation must append exact cleanup diagnostics.'
    Assert-True ($failedCleanupIds.Count -eq 1) 'Failed compensation must attempt exact cleanup once.'
    $failedOwnership = Read-NervLeaderDemoSessionPointer -StateRoot $failedCleanupStateRoot
    Assert-True ($failedOwnership.ownershipState -ceq 'Reserved') 'Failed compensation must retain actionable Reserved ownership.'
    Assert-True ($failedOwnership.sessionId -ceq $failedCleanupIds[0]) 'Failed compensation must retain the exact session route.'

    $noManifestStateRoot = Join-Path $stateRoot 'startup-no-manifest'
    $script:noManifestCleanupCalls = 0
    $noManifestError = ''
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $noManifestStateRoot `
            -WorktreeRoot $repoRoot `
            -StartSessionAction { param($SessionId) throw 'simulated failure before manifest creation' } `
            -StopSessionAction { param($SessionId) $script:noManifestCleanupCalls++ } | Out-Null
    }
    catch { $noManifestError = $_.Exception.Message }
    Assert-True ($noManifestError.Contains('simulated failure before manifest creation')) 'A pre-manifest failure must preserve the original startup error.'
    Assert-True ($script:noManifestCleanupCalls -eq 0) 'A pre-manifest failure must not invoke exact cleanup without authority.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $noManifestStateRoot))) 'A pre-manifest failure may release its Reserved pointer.'

    $foreignStateRoot = Join-Path $stateRoot 'foreign'
    $foreignSessionId = 'nerv-dead-000011'
    Write-NervLeaderDemoSessionPointer `
        -SessionId $foreignSessionId `
        -WorktreeRoot $repoRoot `
        -OwnershipState Current `
        -StateRoot $foreignStateRoot | Out-Null
    $foreignWorktree = Join-Path $stateRoot 'foreign-worktree'
    Write-TestFullStackManifest -SessionId $foreignSessionId -ManifestWorktreeRoot $foreignWorktree -StateRoot $foreignStateRoot
    $script:foreignStopCalls = 0
    $foreignRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action stop `
            -StateRoot $foreignStateRoot `
            -WorktreeRoot $repoRoot `
            -StopSessionAction { param($SessionId) $script:foreignStopCalls++ } | Out-Null
    }
    catch { $foreignRejected = $_.Exception.Message.Contains($foreignSessionId) -and $_.Exception.Message.Contains($foreignWorktree) }

    $mismatchStateRoot = Join-Path $stateRoot 'mismatch'
    $pointerSessionId = 'nerv-dead-000012'
    $manifestSessionId = 'nerv-dead-000013'
    Write-NervLeaderDemoSessionPointer `
        -SessionId $pointerSessionId `
        -WorktreeRoot $repoRoot `
        -OwnershipState Current `
        -StateRoot $mismatchStateRoot | Out-Null
    Write-TestFullStackManifest -SessionId $pointerSessionId -ManifestWorktreeRoot $repoRoot -StateRoot $mismatchStateRoot
    $mismatchManifestPath = Get-NervFullStackManifestPath -SessionId $pointerSessionId -StateRoot $mismatchStateRoot
    $mismatchManifest = Get-Content -LiteralPath $mismatchManifestPath -Raw | ConvertFrom-Json -Depth 30
    $mismatchManifest.sessionId = $manifestSessionId
    [IO.File]::WriteAllText($mismatchManifestPath, ($mismatchManifest | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    $script:mismatchStopCalls = 0
    $mismatchRejected = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action stop `
            -StateRoot $mismatchStateRoot `
            -WorktreeRoot $repoRoot `
            -StopSessionAction { param($SessionId) $script:mismatchStopCalls++ } | Out-Null
    }
    catch { $mismatchRejected = $_.Exception.Message.Contains($pointerSessionId) -and $_.Exception.Message.Contains($manifestSessionId) }

    Assert-True $foreignRejected 'Stop must reject a valid-format pointer whose authoritative manifest belongs to a foreign worktree.'
    Assert-True ($script:foreignStopCalls -eq 0) 'A foreign authoritative manifest must fail before exact-session cleanup.'
    Assert-True $mismatchRejected 'Stop must reject a manifest whose embedded session ID does not match the pointer session ID.'
    Assert-True ($script:mismatchStopCalls -eq 0) 'A mismatched authoritative manifest must fail before exact-session cleanup.'

    $firstSessionId = Invoke-NervLeaderDemoCommand `
        -Action start `
        -StateRoot $stateRoot `
        -WorktreeRoot $repoRoot `
        -StartSessionAction $startAction `
        -StopSessionAction $stopAction `
        -SeedAction $seedAction `
        -HealthCheckAction $healthAction
    Assert-True ($firstSessionId -match '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$') 'Start must allocate a validated full-stack session ID.'
    Assert-True ($script:startCalls.Count -eq 1 -and $script:startCalls[0] -ceq $firstSessionId) 'Start must operate on the allocated exact session ID.'
    Assert-True (-not (Test-Path Env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD)) 'The mapped full-stack password must be removed after start.'
    Assert-True (-not (Test-Path Env:NERV_IIP_LEADER_DEMO)) 'The AppHost demo flag must be removed after start.'

    $pointer = Read-NervLeaderDemoSessionPointer -StateRoot $stateRoot -ExpectedWorktreeRoot $repoRoot
    Assert-True ($pointer.sessionId -ceq $firstSessionId) 'Start must persist the current exact session pointer outside the worktree.'
    Assert-True ((Get-NervLeaderDemoSessionPointerPath -StateRoot $stateRoot).StartsWith([IO.Path]::GetFullPath($stateRoot), [StringComparison]::OrdinalIgnoreCase)) 'The pointer must live under the machine state root.'

    Invoke-NervLeaderDemoCommand -Action seed -StateRoot $stateRoot -WorktreeRoot $repoRoot -SeedAction $seedAction | Out-Null
    Invoke-NervLeaderDemoCommand -Action health-check -StateRoot $stateRoot -WorktreeRoot $repoRoot -HealthCheckAction $healthAction | Out-Null
    Assert-True ($script:seedCalls.Count -eq 1 -and $script:seedCalls[0] -ceq $firstSessionId) 'Seed must use the recorded exact session ID.'
    Assert-True ($script:healthCalls.Count -eq 1 -and $script:healthCalls[0] -ceq $firstSessionId) 'Health-check must use the recorded exact session ID.'

    $leaderDemoRuntimeText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1') -Raw
    Assert-True ($leaderDemoRuntimeText.Contains('Invoke-NervLeaderDemoVerification')) 'The default seed/health actions must use the governed verification and evidence implementation.'
    Assert-True ($leaderDemoRuntimeText.Contains("-Command 'seed'")) 'The default seed action must write seed verification evidence.'
    Assert-True ($leaderDemoRuntimeText.Contains("-Command 'health-check'")) 'The default health action must write bounded health evidence.'
    $leaderDemoEntryText = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/leader-demo.ps1') -Raw
    Assert-True ($leaderDemoEntryText.Contains('Get-NervLeaderDemoFailureExitCode')) 'The leader-demo entrypoint must extract a structured verification exit code.'
    Assert-True ($leaderDemoEntryText.Contains('exit $exitCode')) 'The leader-demo entrypoint must propagate the structured nonzero code after evidence.'

    $secondSessionId = Invoke-NervLeaderDemoCommand `
        -Action reset `
        -StateRoot $stateRoot `
        -WorktreeRoot $repoRoot `
        -StartSessionAction $startAction `
        -StopSessionAction $stopAction `
        -SeedAction $seedAction `
        -HealthCheckAction $healthAction
    Assert-True ($secondSessionId -ne $firstSessionId) 'Reset must allocate a fresh isolated session.'
    Assert-True ($script:stopCalls.Count -eq 1 -and $script:stopCalls[0] -ceq $firstSessionId) 'Reset must stop only the validated recorded session ID.'
    Assert-True ($script:seedCalls[-1] -ceq $secondSessionId) 'Reset must seed the fresh exact session.'
    Assert-True ($script:healthCalls[-1] -ceq $secondSessionId) 'Reset must health-check the fresh exact session.'
    Assert-True ((Read-NervLeaderDemoSessionPointer -StateRoot $stateRoot -ExpectedWorktreeRoot $repoRoot).sessionId -ceq $secondSessionId) 'Reset must replace the pointer only after a fresh start succeeds.'

    Invoke-NervLeaderDemoCommand -Action stop -StateRoot $stateRoot -WorktreeRoot $repoRoot -StopSessionAction $stopAction | Out-Null
    Assert-True ($script:stopCalls.Count -eq 2 -and $script:stopCalls[1] -ceq $secondSessionId) 'Stop must use only the recorded exact session ID.'
    Assert-True (-not (Test-Path -LiteralPath (Get-NervLeaderDemoSessionPointerPath -StateRoot $stateRoot))) 'Stop must remove the current pointer after exact cleanup succeeds.'

    $invalidPointerPath = Get-NervLeaderDemoSessionPointerPath -StateRoot $stateRoot
    [IO.Directory]::CreateDirectory((Split-Path -Parent $invalidPointerPath)) | Out-Null
    [IO.File]::WriteAllText($invalidPointerPath, '{"schemaVersion":1,"sessionId":"unsafe-session","worktreeRoot":"C:\\foreign"}', [Text.UTF8Encoding]::new($false))
    $invalidPointerFailed = $false
    try {
        Invoke-NervLeaderDemoCommand -Action stop -StateRoot $stateRoot -WorktreeRoot $repoRoot -StopSessionAction $stopAction | Out-Null
    }
    catch { $invalidPointerFailed = $_.Exception.Message.Contains('Invalid leader-demo session ID') }
    Assert-True $invalidPointerFailed 'An invalid pointer must fail before any cleanup action runs.'
    Assert-True ($script:stopCalls.Count -eq 2) 'An invalid pointer must never reach the stop action.'

    Remove-Item -LiteralPath $invalidPointerPath -Force
    Remove-Item Env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD
    $missingPasswordFailed = $false
    try {
        Invoke-NervLeaderDemoCommand -Action start -StateRoot $stateRoot -WorktreeRoot $repoRoot -StartSessionAction $startAction | Out-Null
    }
    catch { $missingPasswordFailed = $_.Exception.Message.Contains('NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD') }
    Assert-True $missingPasswordFailed 'Start must require the demo admin password from its sole controlled environment variable.'
    Assert-True ($script:startCalls.Count -eq 2) 'Missing credentials must fail before a new session starts.'
}
finally {
    if ($hadOriginalPassword) { $env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD = $originalPassword }
    else { Remove-Item Env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD -ErrorAction SilentlyContinue }
    Remove-Item Env:NERV_IIP_FULLSTACK_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:NERV_IIP_LEADER_DEMO -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stateRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Leader-demo lifecycle tests passed.'
