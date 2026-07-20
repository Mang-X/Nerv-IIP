Set-StrictMode -Version Latest

$script:NervFullStackSessionIdPattern = '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$'
$script:NervLeaderDemoOwnershipStates = @('Reserved', 'Current')
$script:NervFullStackStates = @('Creating', 'Running', 'Collecting', 'Failed', 'Stopping', 'Stopped', 'CleanupFailed')
$script:NervFullStackTransitions = @{
    Creating = @('Running', 'Failed', 'Stopping')
    Running = @('Collecting', 'Failed', 'Stopping')
    Collecting = @('Failed', 'Stopping')
    Failed = @('Stopping')
    Stopping = @('Stopped', 'CleanupFailed')
    CleanupFailed = @('Stopping')
    Stopped = @()
}

function Get-NervFullStackStateRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_STATE_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:NERV_IIP_FULLSTACK_STATE_ROOT)
    }

    if ($IsWindows) {
        return (Join-Path $env:LOCALAPPDATA 'Nerv-IIP')
    }

    $base = if (-not [string]::IsNullOrWhiteSpace($env:XDG_STATE_HOME)) {
        $env:XDG_STATE_HOME
    }
    else {
        Join-Path $HOME '.local/state'
    }
    return (Join-Path $base 'nerv-iip')
}

function New-NervFullStackSessionId {
    param(
        [Parameter(Mandatory)]
        [string] $WorktreeRoot
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($WorktreeRoot).ToLowerInvariant()
    $bytes = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($normalizedRoot)
    )
    $worktreeHash = [Convert]::ToHexString($bytes).ToLowerInvariant().Substring(0, 4)
    $random = [Convert]::ToHexString(
        [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(3)
    ).ToLowerInvariant()
    return "nerv-$worktreeHash-$random"
}

function Get-NervFullStackManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId,

        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid full-stack session ID '$SessionId'."
    }

    return (Join-Path ([System.IO.Path]::GetFullPath($StateRoot)) "fullstack-sessions/$SessionId.json")
}

function Test-NervFullStackSessionIdAvailable {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    $path = Get-NervFullStackManifestPath -SessionId $SessionId -StateRoot $StateRoot
    return -not (Test-Path -LiteralPath $path)
}

function New-NervFullStackManifest {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [Parameter(Mandatory)] [string] $AppHostProject,
        [Parameter(Mandatory)] [string] $ArtifactPath,
        [ValidateSet('InMemory', 'RabbitMQ', 'Redis')] [string] $MessagingProvider = 'Redis',
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [ValidateRange(1, 1440)] [int] $LeaseMinutes = 90
    )

    [void] (Get-NervFullStackManifestPath -SessionId $SessionId -StateRoot $StateRoot)
    $now = [DateTimeOffset]::UtcNow
    $process = Get-Process -Id $PID

    return [ordered]@{
        schemaVersion = 1
        sessionId = $SessionId
        state = 'Creating'
        mode = 'ephemeral'
        messagingProvider = $MessagingProvider
        createdAtUtc = $now.ToString('O')
        updatedAtUtc = $now.ToString('O')
        leaseExpiresAtUtc = $now.AddMinutes($LeaseMinutes).ToString('O')
        worktreeRoot = [System.IO.Path]::GetFullPath($WorktreeRoot)
        appHostProject = [System.IO.Path]::GetFullPath($AppHostProject)
        coordinator = [ordered]@{
            pid = $PID
            processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
        }
        guardian = $null
        aspire = [ordered]@{
            appHostId = $null
            dcpId = $null
            appHostPath = $null
            appHostPid = $null
            appHostProcessStartTimeUtc = $null
            cliPid = $null
            cliProcessStartTimeUtc = $null
            logFile = $null
        }
        runtime = [ordered]@{
            processIds = @()
            containers = @()
            containerIds = @()
            networkIds = @()
            volumeNames = @()
        }
        endpoints = [ordered]@{}
        artifactPath = [System.IO.Path]::GetFullPath($ArtifactPath)
        transitions = @([ordered]@{ state = 'Creating'; atUtc = $now.ToString('O') })
        cleanup = [ordered]@{ completedAtUtc = $null; remaining = @(); errors = @() }
        failure = $null
    }
}

function Get-NervLeaderDemoSessionPointerPath {
    param([string] $StateRoot = (Get-NervFullStackStateRoot))

    return (Join-Path ([System.IO.Path]::GetFullPath($StateRoot)) 'leader-demo/current.json')
}

function Get-NervLeaderDemoLifecycleLockPath {
    param([string] $StateRoot = (Get-NervFullStackStateRoot))

    return (Join-Path ([System.IO.Path]::GetFullPath($StateRoot)) 'leader-demo/.lifecycle.lock')
}

function Invoke-WithNervLeaderDemoLifecycleLock {
    param(
        [Parameter(Mandatory)] [scriptblock] $ScriptBlock,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [ValidateRange(1, 300)] [int] $TimeoutSeconds = 30
    )

    $lockPath = Get-NervLeaderDemoLifecycleLockPath -StateRoot $StateRoot
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $lockPath)) | Out-Null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $stream = $null

    while ($null -eq $stream) {
        try {
            $stream = [System.IO.FileStream]::new(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        }
        catch [System.IO.IOException] {
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                throw "Timed out waiting for the leader-demo lifecycle lock at '$lockPath'."
            }
            Start-Sleep -Milliseconds 100
        }
    }

    try {
        return (& $ScriptBlock)
    }
    finally {
        $stream.Dispose()
    }
}

function Write-NervLeaderDemoSessionPointer {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $WorktreeRoot,
        [ValidateSet('Reserved', 'Current')] [string] $OwnershipState = 'Current',
        [Nullable[int]] $OwnerPid,
        [string] $OwnerProcessStartTimeUtc,
        [string] $CreatedAtUtc,
        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    if ($SessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid leader-demo session ID '$SessionId'."
    }
    $now = [DateTimeOffset]::UtcNow
    $createdAt = if ([string]::IsNullOrWhiteSpace($CreatedAtUtc)) {
        $now
    }
    else {
        try { [DateTimeOffset]::Parse($CreatedAtUtc) }
        catch { throw "Invalid leader-demo reservation creation time '$CreatedAtUtc'." }
    }
    $ownerStartedAt = $null
    if ($OwnershipState -eq 'Reserved') {
        if ($null -eq $OwnerPid -or [int] $OwnerPid -lt 1) {
            throw 'Reserved leader-demo ownership requires a positive owner PID.'
        }
        if ([string]::IsNullOrWhiteSpace($OwnerProcessStartTimeUtc)) {
            throw 'Reserved leader-demo ownership requires an owner process start time.'
        }
        try { $ownerStartedAt = [DateTimeOffset]::Parse($OwnerProcessStartTimeUtc) }
        catch { throw "Invalid leader-demo owner process start time '$OwnerProcessStartTimeUtc'." }
    }

    $path = Get-NervLeaderDemoSessionPointerPath -StateRoot $StateRoot
    $directory = Split-Path -Parent $path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = "$path.tmp-$([guid]::NewGuid().ToString('N'))"
    $pointer = [ordered]@{
        schemaVersion = 1
        sessionId = $SessionId
        worktreeRoot = [System.IO.Path]::GetFullPath($WorktreeRoot)
        ownershipState = $OwnershipState
        ownerPid = if ($OwnershipState -eq 'Reserved') { [int] $OwnerPid } else { $null }
        ownerProcessStartTimeUtc = if ($null -ne $ownerStartedAt) { $ownerStartedAt.ToString('O') } else { $null }
        createdAtUtc = $createdAt.ToString('O')
        updatedAtUtc = $now.ToString('O')
    }

    try {
        $json = $pointer | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }

    return [pscustomobject] $pointer
}

function Read-NervLeaderDemoSessionPointer {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [string] $ExpectedWorktreeRoot
    )

    $path = Get-NervLeaderDemoSessionPointerPath -StateRoot $StateRoot
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No current leader-demo session is recorded at '$path'."
    }

    try {
        $pointer = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 5
    }
    catch {
        throw "Leader-demo session pointer at '$path' is not valid JSON."
    }

    if ($pointer.schemaVersion -ne 1) {
        throw "Leader-demo session pointer at '$path' has unsupported schema version '$($pointer.schemaVersion)'."
    }
    $sessionId = "$($pointer.sessionId)"
    if ($sessionId -notmatch $script:NervFullStackSessionIdPattern) {
        throw "Invalid leader-demo session ID '$sessionId' in '$path'."
    }
    if ([string]::IsNullOrWhiteSpace("$($pointer.worktreeRoot)")) {
        throw "Leader-demo session pointer at '$path' has no worktree root."
    }
    if ($script:NervLeaderDemoOwnershipStates -cnotcontains "$($pointer.ownershipState)") {
        throw "Leader-demo session pointer at '$path' has invalid ownership state '$($pointer.ownershipState)'."
    }
    if ("$($pointer.ownershipState)" -eq 'Reserved') {
        try { [void] [DateTimeOffset]::Parse("$($pointer.createdAtUtc)") }
        catch { throw "Leader-demo session pointer at '$path' has invalid creation time '$($pointer.createdAtUtc)'." }
        $ownerPid = 0
        if (-not [int]::TryParse("$($pointer.ownerPid)", [ref] $ownerPid) -or $ownerPid -lt 1) {
            throw "Leader-demo Reserved pointer at '$path' has invalid owner PID '$($pointer.ownerPid)'."
        }
        try { [void] [DateTimeOffset]::Parse("$($pointer.ownerProcessStartTimeUtc)") }
        catch { throw "Leader-demo Reserved pointer at '$path' has invalid owner process start time '$($pointer.ownerProcessStartTimeUtc)'." }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedWorktreeRoot)) {
        $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        $actualRoot = [System.IO.Path]::GetFullPath("$($pointer.worktreeRoot)")
        $expectedRoot = [System.IO.Path]::GetFullPath($ExpectedWorktreeRoot)
        if (-not [string]::Equals($actualRoot, $expectedRoot, $comparison)) {
            throw "Leader-demo session '$sessionId' belongs to worktree '$actualRoot', not '$expectedRoot'."
        }
    }

    return $pointer
}

function Remove-NervLeaderDemoSessionPointer {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [string] $ExpectedSessionId
    )

    $path = Get-NervLeaderDemoSessionPointerPath -StateRoot $StateRoot
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSessionId) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
        $pointer = Read-NervLeaderDemoSessionPointer -StateRoot $StateRoot
        if ("$($pointer.sessionId)" -cne $ExpectedSessionId) {
            throw "Leader-demo pointer belongs to '$($pointer.sessionId)', not expected session '$ExpectedSessionId'."
        }
    }
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}

function Invoke-WithNervFullStackSessionLock {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock,

        [string] $StateRoot = (Get-NervFullStackStateRoot),

        [ValidateRange(1, 300)]
        [int] $TimeoutSeconds = 30
    )

    $sessionDirectory = Join-Path ([System.IO.Path]::GetFullPath($StateRoot)) 'fullstack-sessions'
    [System.IO.Directory]::CreateDirectory($sessionDirectory) | Out-Null
    $lockPath = Join-Path $sessionDirectory '.sessions.lock'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $stream = $null

    while ($null -eq $stream) {
        try {
            $stream = [System.IO.FileStream]::new(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        }
        catch [System.IO.IOException] {
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                throw "Timed out waiting for the full-stack session lock at '$lockPath'."
            }
            Start-Sleep -Milliseconds 100
        }
    }

    try {
        return (& $ScriptBlock)
    }
    finally {
        $stream.Dispose()
    }
}

function Write-NervFullStackManifest {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,

        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    $path = Get-NervFullStackManifestPath -SessionId "$($Manifest.sessionId)" -StateRoot $StateRoot
    $directory = Split-Path -Parent $path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = "$path.tmp-$([guid]::NewGuid().ToString('N'))"

    try {
        $json = $Manifest | ConvertTo-Json -Depth 30
        [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Read-NervFullStackManifest {
    param(
        [Parameter(Mandatory)]
        [string] $SessionId,

        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    $path = Get-NervFullStackManifestPath -SessionId $SessionId -StateRoot $StateRoot
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Full-stack session manifest '$SessionId' was not found at '$path'."
    }

    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 30)
}

function Get-NervFullStackManifests {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot)
    )

    $directory = Join-Path ([System.IO.Path]::GetFullPath($StateRoot)) 'fullstack-sessions'
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $directory -Filter 'nerv-*.json' -File |
            Sort-Object Name |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -Depth 30 }
    )
}

function Move-NervFullStackSessionState {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,

        [Parameter(Mandatory)]
        [ValidateSet('Creating', 'Running', 'Collecting', 'Failed', 'Stopping', 'Stopped', 'CleanupFailed')]
        [string] $State
    )

    $current = "$($Manifest.state)"
    if ($script:NervFullStackStates -notcontains $current) {
        throw "Manifest has unknown full-stack state '$current'."
    }
    if ($current -eq $State) {
        return $Manifest
    }
    if ($script:NervFullStackTransitions[$current] -notcontains $State) {
        throw "Invalid full-stack session transition '$current -> $State'."
    }

    $now = [DateTimeOffset]::UtcNow.ToString('O')
    $Manifest.state = $State
    $Manifest.updatedAtUtc = $now
    $Manifest.transitions = @($Manifest.transitions) + @([ordered]@{ state = $State; atUtc = $now })
    return $Manifest
}

function Renew-NervFullStackLease {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,

        [ValidateRange(1, 1440)]
        [int] $LeaseMinutes = 90
    )

    $now = [DateTimeOffset]::UtcNow
    $Manifest.updatedAtUtc = $now.ToString('O')
    $Manifest.leaseExpiresAtUtc = $now.AddMinutes($LeaseMinutes).ToString('O')
    return $Manifest
}

function Renew-NervFullStackSessionLease {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [ValidateRange(1, 1440)] [int] $LeaseMinutes = 90
    )

    return Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
        $manifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
        if ("$($manifest.state)" -eq 'Running') {
            $manifest = Renew-NervFullStackLease -Manifest $manifest -LeaseMinutes $LeaseMinutes
            Write-NervFullStackManifest -Manifest $manifest -StateRoot $StateRoot
        }
        return $manifest
    }
}

function Update-NervFullStackManifest {
    param(
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $AllowedStates,
        [Parameter(Mandatory)] [scriptblock] $UpdateAction,
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [switch] $ReturnUnchangedOnStateMismatch
    )

    return Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -ScriptBlock {
        $manifest = Read-NervFullStackManifest -SessionId $SessionId -StateRoot $StateRoot
        if ($AllowedStates -cnotcontains "$($manifest.state)") {
            if ($ReturnUnchangedOnStateMismatch) { return $manifest }
            throw "Session '$SessionId' is '$($manifest.state)'; expected one of: $($AllowedStates -join ', ')."
        }
        $updated = & $UpdateAction $manifest
        if ($null -eq $updated) { $updated = $manifest }
        Write-NervFullStackManifest -Manifest $updated -StateRoot $StateRoot
        return $updated
    }
}

function Test-NervProcessIdentity {
    param(
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [object] $ProcessStartTimeUtc
    )

    try {
        $expected = if ($ProcessStartTimeUtc -is [DateTime]) {
            ([DateTime] $ProcessStartTimeUtc).ToUniversalTime()
        }
        elseif ($ProcessStartTimeUtc -is [DateTimeOffset]) {
            ([DateTimeOffset] $ProcessStartTimeUtc).UtcDateTime
        }
        else {
            [DateTimeOffset]::Parse("$ProcessStartTimeUtc").UtcDateTime
        }
        $actual = (Get-Process -Id $ProcessId -ErrorAction Stop).StartTime.ToUniversalTime()
        return [Math]::Abs(($actual - $expected).TotalMilliseconds) -lt 1
    }
    catch {
        return $false
    }
}

function Get-NervProcessIdentityStatus {
    param(
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [object] $ProcessStartTimeUtc
    )

    try {
        Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        return 'Absent'
    }
    catch {
        return 'Unknown'
    }

    if (Test-NervProcessIdentity -ProcessId $ProcessId -ProcessStartTimeUtc $ProcessStartTimeUtc) {
        return 'Active'
    }
    return 'Mismatched'
}

function Test-NervFullStackSessionStale {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,

        [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow
    )

    if ("$($Manifest.state)" -eq 'Stopped') {
        return $false
    }

    $leaseExpiry = if ($Manifest.leaseExpiresAtUtc -is [DateTime]) {
        [DateTimeOffset] ([DateTime] $Manifest.leaseExpiresAtUtc)
    }
    else {
        [DateTimeOffset]::Parse("$($Manifest.leaseExpiresAtUtc)")
    }
    if ($leaseExpiry -le $Now) {
        return $true
    }

    if ($null -ne $Manifest.coordinator -and $Manifest.coordinator.pid) {
        return -not (Test-NervProcessIdentity `
            -ProcessId ([int] $Manifest.coordinator.pid) `
            -ProcessStartTimeUtc $Manifest.coordinator.processStartTimeUtc)
    }

    return $true
}

function Get-NervStaleFullStackSessions {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow
    )

    return @(Get-NervFullStackManifests -StateRoot $StateRoot | Where-Object {
        Test-NervFullStackSessionStale -Manifest $_ -Now $Now
    })
}

function Claim-NervStaleFullStackSessions {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow,
        [ValidateRange(1, 300)] [int] $TimeoutSeconds = 30
    )

    return @(Invoke-WithNervFullStackSessionLock -StateRoot $StateRoot -TimeoutSeconds $TimeoutSeconds -ScriptBlock {
        $claimed = [System.Collections.Generic.List[string]]::new()
        foreach ($manifest in @(Get-NervFullStackManifests -StateRoot $StateRoot)) {
            if (-not (Test-NervFullStackSessionStale -Manifest $manifest -Now $Now)) {
                continue
            }
            if ("$($manifest.state)" -eq 'Stopping') {
                $claimed.Add("$($manifest.sessionId)")
                continue
            }
            $manifest = Move-NervFullStackSessionState -Manifest $manifest -State Stopping
            Write-NervFullStackManifest -Manifest $manifest -StateRoot $StateRoot
            $claimed.Add("$($manifest.sessionId)")
        }
        return @($claimed)
    })
}

function Get-NervFullStackLeaseMinutes {
    if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_LEASE_MINUTES)) {
        return 90
    }
    $minutes = 0
    if (-not [int]::TryParse($env:NERV_IIP_FULLSTACK_LEASE_MINUTES, [ref] $minutes) -or $minutes -lt 1 -or $minutes -gt 1440) {
        throw 'NERV_IIP_FULLSTACK_LEASE_MINUTES must be an integer from 1 through 1440.'
    }
    return $minutes
}

function Test-NervFullStackAdmission {
    param(
        [string] $StateRoot = (Get-NervFullStackStateRoot),
        [Nullable[int]] $MaximumSessions,
        [string] $ExcludeSessionId,
        [string] $WorktreeRoot
    )

    $maximum = if ($null -ne $MaximumSessions) {
        [int] $MaximumSessions
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:NERV_IIP_FULLSTACK_MAX_SESSIONS)) {
        $parsed = 0
        if (-not [int]::TryParse($env:NERV_IIP_FULLSTACK_MAX_SESSIONS, [ref] $parsed) -or $parsed -lt 1) {
            throw "NERV_IIP_FULLSTACK_MAX_SESSIONS must be a positive integer."
        }
        $parsed
    }
    else {
        3
    }
    if ($maximum -lt 1) { throw 'MaximumSessions must be at least 1.' }

    $active = @(Get-NervFullStackManifests -StateRoot $StateRoot | Where-Object {
        "$($_.state)" -ne 'Stopped' -and "$($_.sessionId)" -ne $ExcludeSessionId
    })

    if (-not [string]::IsNullOrWhiteSpace($WorktreeRoot)) {
        $normalized = [System.IO.Path]::GetFullPath($WorktreeRoot)
        $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        $duplicate = @($active | Where-Object {
            [string]::Equals([System.IO.Path]::GetFullPath("$($_.worktreeRoot)"), $normalized, $comparison)
        })
        if ($duplicate.Count -gt 0) {
            return [pscustomobject]@{
                Allowed = $false
                Reason = 'WorktreeAlreadyActive'
                ActiveCount = $active.Count
                MaximumSessions = $maximum
            }
        }
    }

    return [pscustomobject]@{
        Allowed = $active.Count -lt $maximum
        Reason = if ($active.Count -lt $maximum) { 'Allowed' } else { 'MaximumSessionsReached' }
        ActiveCount = $active.Count
        MaximumSessions = $maximum
    }
}
