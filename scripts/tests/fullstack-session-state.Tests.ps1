$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Utf8TestFile([string] $Path, [string] $Content) {
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-state-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try {
    $discoveryRoot = Join-Path $testRoot 'manifest-discovery'
    $discoverySessionId = 'nerv-abcd-123456'
    $discoveryManifest = New-NervFullStackManifest `
        -SessionId $discoverySessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$discoverySessionId") `
        -StateRoot $discoveryRoot
    Write-NervFullStackManifest -Manifest $discoveryManifest -StateRoot $discoveryRoot

    $discoveryDirectory = Join-Path $discoveryRoot 'fullstack-sessions'
    Write-Utf8TestFile (Join-Path $discoveryDirectory 'nerv-bad.json') '{invalid-json'
    Write-Utf8TestFile (Join-Path $discoveryDirectory 'nerv-cafe-654321.json') '{"sessionId":"nerv-cafe-654321"}'
    Write-Utf8TestFile `
        (Join-Path $discoveryDirectory 'nerv-abcd-123456.guardian-stop.ack.json') `
        '{invalid-json'
    Write-Utf8TestFile `
        (Join-Path $discoveryDirectory 'nerv-dead-beef12.json') `
        '{"sessionId":"nerv-feed-abcdef","state":"Stopped"}'
    Write-Utf8TestFile (Join-Path $discoveryDirectory 'nerv-0001-000001.json') 'null'
    Write-Utf8TestFile `
        (Join-Path $discoveryDirectory 'nerv-0002-000002.json') `
        '[{"sessionId":"nerv-0002-000002","state":"Stopped"}]'
    Write-Utf8TestFile (Join-Path $discoveryDirectory 'nerv-0003-000003.json') '42'

    $discoveryOutput = @(Get-NervFullStackManifests -StateRoot $discoveryRoot 3>&1)
    $discoveryWarnings = @($discoveryOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
    $discoveredManifests = @($discoveryOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })
    Assert-True ($discoveredManifests.Count -eq 1) "Manifest discovery must return exactly the one valid manifest; removing any independent manifest predicate must make this assertion fail. Actual: $(@($discoveredManifests | ForEach-Object { [string]$_.sessionId }) -join ', ')."
    Assert-True ([string]::Equals([string]$discoveredManifests[0].sessionId, $discoverySessionId, [StringComparison]::Ordinal)) 'Manifest discovery must not lose a valid manifest.'
    Assert-True ($discoveryWarnings.Count -eq 7) "Manifest discovery must emit exactly one warning for each rejected candidate; actual count: $($discoveryWarnings.Count)."
    $invalidJsonWarnings = @($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-bad.json', [StringComparison]::Ordinal) })
    Assert-True ($invalidJsonWarnings.Count -eq 1) 'Invalid JSON must emit exactly one visible warning.'
    Assert-True ($invalidJsonWarnings[0].Message.Contains('invalid full-stack session manifest candidate', [StringComparison]::Ordinal)) 'A malformed manifest candidate must retain its invalid JSON warning.'
    Assert-True (@($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-cafe-654321.json', [StringComparison]::Ordinal) }).Count -eq 1) 'A canonical manifest without state must emit exactly one visible warning.'
    $invalidGuardianWarnings = @($discoveryWarnings | Where-Object { $_.Message.Contains('guardian-stop.ack.json', [StringComparison]::Ordinal) })
    Assert-True ($invalidGuardianWarnings.Count -eq 1) 'A malformed sidecar outside the manifest namespace must emit exactly one visible warning.'
    Assert-True ($invalidGuardianWarnings[0].Message.Contains('outside the manifest namespace', [StringComparison]::Ordinal)) 'A malformed sidecar must be classified by its file name before its JSON payload is parsed.'
    Assert-True (-not $invalidGuardianWarnings[0].Message.Contains('invalid full-stack session manifest candidate', [StringComparison]::Ordinal)) 'A malformed sidecar outside the manifest namespace must not emit an invalid JSON warning.'
    Assert-True (@($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-dead-beef12.json', [StringComparison]::Ordinal) }).Count -eq 1) 'A canonical file with a mismatched payload sessionId must emit exactly one visible warning.'
    Assert-True (@($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-0001-000001.json', [StringComparison]::Ordinal) }).Count -eq 1) 'A null JSON payload must emit exactly one visible warning.'
    Assert-True (@($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-0002-000002.json', [StringComparison]::Ordinal) }).Count -eq 1) 'An array JSON payload must emit exactly one visible warning.'
    Assert-True (@($discoveryWarnings | Where-Object { $_.Message.Contains('nerv-0003-000003.json', [StringComparison]::Ordinal) }).Count -eq 1) 'A scalar JSON payload must emit exactly one visible warning.'

    $guardianRoot = Join-Path $testRoot 'guardian-sidecar-discovery'
    $guardianDirectory = Join-Path $guardianRoot 'fullstack-sessions'
    [System.IO.Directory]::CreateDirectory($guardianDirectory) | Out-Null
    Write-Utf8TestFile `
        (Join-Path $guardianDirectory 'nerv-aaaa-aaaaaa.guardian-stop.ack.json') `
        '{"schemaVersion":1,"kind":"ack","sessionId":"nerv-aaaa-aaaaaa"}'
    $guardianOutput = @(Get-NervFullStackManifests -StateRoot $guardianRoot 3>&1)
    $guardianWarnings = @($guardianOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
    $guardianManifests = @($guardianOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })
    Assert-True ($guardianManifests.Count -eq 0) 'A real guardian acknowledgement payload must not be discovered as a manifest.'
    Assert-True ($guardianWarnings.Count -eq 1) 'A real guardian acknowledgement sidecar must emit exactly one warning.'
    Assert-True ($guardianWarnings[0].Message.Contains('outside the manifest namespace', [StringComparison]::Ordinal)) 'A real guardian acknowledgement must be classified by its sidecar file name before payload fields are inspected.'

    $uppercaseRoot = Join-Path $testRoot 'uppercase-discovery'
    $uppercaseSessionId = 'nerv-ABCD-ABCDEF'
    $uppercaseManifest = New-NervFullStackManifest `
        -SessionId $uppercaseSessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$uppercaseSessionId") `
        -StateRoot $uppercaseRoot
    Write-NervFullStackManifest -Manifest $uppercaseManifest -StateRoot $uppercaseRoot
    $uppercaseReloaded = Read-NervFullStackManifest -SessionId $uppercaseSessionId -StateRoot $uppercaseRoot
    $uppercaseDiscovered = @(Get-NervFullStackManifests -StateRoot $uppercaseRoot)
    Assert-True ([string]::Equals([string]$uppercaseReloaded.sessionId, $uppercaseSessionId, [StringComparison]::Ordinal)) 'Uppercase session IDs must round-trip through Write/Read.'
    Assert-True ($uppercaseDiscovered.Count -eq 1) 'Discovery must not lose a manifest accepted by New/Write/Read because its hex digits are uppercase.'
    Assert-True ([string]::Equals([string]$uppercaseDiscovered[0].sessionId, $uppercaseSessionId, [StringComparison]::Ordinal)) 'Discovery must preserve the uppercase session ID payload.'

    $caseMismatchRoot = Join-Path $testRoot 'case-mismatch-discovery'
    $caseMismatchDirectory = Join-Path $caseMismatchRoot 'fullstack-sessions'
    [System.IO.Directory]::CreateDirectory($caseMismatchDirectory) | Out-Null
    Write-Utf8TestFile `
        (Join-Path $caseMismatchDirectory 'nerv-abcd-abcdef.json') `
        '{"sessionId":"nerv-ABCD-ABCDEF","state":"Stopped"}'
    $caseMismatchOutput = @(Get-NervFullStackManifests -StateRoot $caseMismatchRoot 3>&1)
    $caseMismatchWarnings = @($caseMismatchOutput | Where-Object { $_ -is [Management.Automation.WarningRecord] })
    $caseMismatchManifests = @($caseMismatchOutput | Where-Object { $_ -isnot [Management.Automation.WarningRecord] })
    Assert-True ($caseMismatchManifests.Count -eq 0) 'Discovery must reject a payload sessionId whose casing differs from the actual file stem.'
    Assert-True ($caseMismatchWarnings.Count -eq 1) 'A file/payload casing mismatch must emit exactly one warning.'
    Assert-True ($caseMismatchWarnings[0].Message.Contains('sessionId does not match the file name', [StringComparison]::Ordinal)) 'A file/payload casing mismatch must be classified as an ordinal identity mismatch.'

    $sessionId = New-NervFullStackSessionId -WorktreeRoot $repoRoot
    Assert-True ($sessionId -match '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$') "Invalid session ID: $sessionId"

    $manifest = New-NervFullStackManifest `
        -SessionId $sessionId `
        -WorktreeRoot $repoRoot `
        -AppHostProject (Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj') `
        -ArtifactPath (Join-Path $repoRoot "artifacts/fullstack/$sessionId") `
        -StateRoot $testRoot `
        -LeaseMinutes 90

    Assert-True ($manifest.schemaVersion -eq 1) 'Manifest schema must be 1.'
    Assert-True ([string]::Equals([string]$manifest.state, 'Creating', [StringComparison]::OrdinalIgnoreCase)) 'New manifests must be Creating.'
    Assert-True ($null -eq $manifest.runtime.messagingProvider) 'A new manifest must not claim a messaging provider before startup records it.'
    Assert-True ($null -eq $manifest.runtime.persistenceProvider) 'A new manifest must not claim a persistence provider before startup records it.'
    Assert-True (-not ($manifest | ConvertTo-Json -Depth 20).Contains('connectionString', [StringComparison]::Ordinal)) 'Manifest must not contain connection strings.'

    Write-NervFullStackManifest -Manifest $manifest -StateRoot $testRoot
    Assert-True (-not (Test-NervFullStackSessionIdAvailable -SessionId $sessionId -StateRoot $testRoot)) 'An existing session ID must never be available for overwrite.'
    Assert-True (Test-NervFullStackSessionIdAvailable -SessionId 'nerv-abcd-654321' -StateRoot $testRoot) 'An unused valid session ID must be available.'
    $reloaded = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
    Assert-True ([string]::Equals([string]$reloaded.sessionId, $sessionId, [StringComparison]::Ordinal)) 'Atomic manifest round-trip failed.'

    Move-NervFullStackSessionState -Manifest $reloaded -State Running | Out-Null
    Assert-True ([string]::Equals([string]$reloaded.state, 'Running', [StringComparison]::OrdinalIgnoreCase)) 'Creating -> Running must be allowed.'
    $invalidFailed = $false
    try { Move-NervFullStackSessionState -Manifest $reloaded -State Creating } catch { $invalidFailed = $true }
    Assert-True $invalidFailed 'Running -> Creating must be rejected.'

    Write-NervFullStackManifest -Manifest $reloaded -StateRoot $testRoot
    $admission = Test-NervFullStackAdmission -StateRoot $testRoot -MaximumSessions 1 -ExcludeSessionId 'none'
    Assert-True (-not $admission.Allowed) 'A second active session must be denied at the configured ceiling.'
    $softHyphen = [string][char]0x00AD
    $softHyphenAdmission = Test-NervFullStackAdmission `
        -StateRoot $testRoot `
        -MaximumSessions 1 `
        -ExcludeSessionId "$sessionId$softHyphen"
    Assert-True (-not $softHyphenAdmission.Allowed) 'A session ID differing by U+00AD must not exclude the current active session from admission.'
    Assert-True ($softHyphenAdmission.ActiveCount -eq 1) 'A U+00AD-distinct exclusion must retain the current active session in the admission count.'
    Assert-True ([string]::Equals([string]$softHyphenAdmission.Reason, 'MaximumSessionsReached', [StringComparison]::Ordinal)) 'A U+00AD-distinct exclusion must retain the maximum-session blocker.'

    $reloaded.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    Assert-True (Test-NervFullStackSessionStale -Manifest $reloaded -Now ([DateTimeOffset]::UtcNow)) 'Expired lease must be stale.'

    $reloaded.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(30).ToString('O')
    $reloaded.coordinator = $null
    Assert-True (Test-NervFullStackSessionStale -Manifest $reloaded) 'A missing coordinator must be stale.'
    $currentProcess = Get-Process -Id $PID
    $reloaded.coordinator = [pscustomobject]@{
        pid = $PID
        processStartTimeUtc = $currentProcess.StartTime.ToUniversalTime().AddMinutes(-1).ToString('O')
    }
    Assert-True (Test-NervFullStackSessionStale -Manifest $reloaded) 'A reused PID with another start time must be stale.'
    $reloaded.coordinator.processStartTimeUtc = $currentProcess.StartTime.ToUniversalTime().ToString('O')
    Assert-True (-not (Test-NervFullStackSessionStale -Manifest $reloaded)) 'A live coordinator with a valid lease must not be stale.'
    Write-NervFullStackManifest -Manifest $reloaded -StateRoot $testRoot
    Assert-True (@(Get-NervStaleFullStackSessions -StateRoot $testRoot).Count -eq 0) 'A live session must never be selected for GC.'

    $reloaded.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    Write-NervFullStackManifest -Manifest $reloaded -StateRoot $testRoot
    $renewed = Renew-NervFullStackSessionLease -SessionId $sessionId -StateRoot $testRoot -LeaseMinutes 30
    Assert-True ([string]::Equals([string]$renewed.state, 'Running', [StringComparison]::OrdinalIgnoreCase)) 'Atomic renewal must keep a running session active.'
    Assert-True (@(Claim-NervStaleFullStackSessions -StateRoot $testRoot).Count -eq 0) 'GC must not claim a session renewed before its stale recheck.'

    $renewed.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    Write-NervFullStackManifest -Manifest $renewed -StateRoot $testRoot
    $claimed = @(Claim-NervStaleFullStackSessions -StateRoot $testRoot)
    Assert-True ($claimed.Count -eq 1 -and $claimed[0] -eq $sessionId) 'GC must atomically claim an actually stale session.'
    $renewAfterClaim = Renew-NervFullStackSessionLease -SessionId $sessionId -StateRoot $testRoot -LeaseMinutes 30
    Assert-True ([string]::Equals([string]$renewAfterClaim.state, 'Stopping', [StringComparison]::OrdinalIgnoreCase)) 'Lease renewal must never overwrite a GC or user stop claim.'
    $reclaimedStopping = @(Claim-NervStaleFullStackSessions -StateRoot $testRoot)
    Assert-True ($reclaimedStopping.Count -eq 1 -and $reclaimedStopping[0] -eq $sessionId) 'GC must reclaim a stale session already left in Stopping.'

    $claimedManifest = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
    $claimedManifest = Move-NervFullStackSessionState -Manifest $claimedManifest -State Stopped
    Write-NervFullStackManifest -Manifest $claimedManifest -StateRoot $testRoot
    $staleStartupWriteRejected = $false
    try {
        Update-NervFullStackManifest `
            -SessionId $sessionId `
            -StateRoot $testRoot `
            -AllowedStates @('Creating') `
            -UpdateAction { param($latest) $latest.runtime.processIds = @(999); $latest } | Out-Null
    }
    catch { $staleStartupWriteRejected = $true }
    Assert-True $staleStartupWriteRejected 'A stale startup writer must not overwrite a session already claimed and stopped.'
    $afterRejectedWrite = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
    Assert-True ([string]::Equals([string]$afterRejectedWrite.state, 'Stopped', [StringComparison]::OrdinalIgnoreCase) -and @($afterRejectedWrite.runtime.processIds).Count -eq 0) 'Rejected stale writes must leave the stopped manifest unchanged.'

    $reloaded = $afterRejectedWrite
    $reloaded.state = 'Stopped'
    Assert-True (-not (Test-NervFullStackSessionStale -Manifest $reloaded)) 'A stopped session must not be stale.'

    $script:lockCount = 0
    Invoke-WithNervFullStackSessionLock -StateRoot $testRoot -ScriptBlock { $script:lockCount++ }
    Assert-True ($script:lockCount -eq 1) 'Session lock must execute its body once.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Full-stack session state tests passed.'
