$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-state-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try {
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
    Assert-True ($manifest.state -eq 'Creating') 'New manifests must be Creating.'
    Assert-True (-not ($manifest | ConvertTo-Json -Depth 20).Contains('connectionString')) 'Manifest must not contain connection strings.'

    Write-NervFullStackManifest -Manifest $manifest -StateRoot $testRoot
    Assert-True (-not (Test-NervFullStackSessionIdAvailable -SessionId $sessionId -StateRoot $testRoot)) 'An existing session ID must never be available for overwrite.'
    Assert-True (Test-NervFullStackSessionIdAvailable -SessionId 'nerv-abcd-654321' -StateRoot $testRoot) 'An unused valid session ID must be available.'
    $reloaded = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
    Assert-True ($reloaded.sessionId -eq $sessionId) 'Atomic manifest round-trip failed.'

    Move-NervFullStackSessionState -Manifest $reloaded -State Running | Out-Null
    Assert-True ($reloaded.state -eq 'Running') 'Creating -> Running must be allowed.'
    $invalidFailed = $false
    try { Move-NervFullStackSessionState -Manifest $reloaded -State Creating } catch { $invalidFailed = $true }
    Assert-True $invalidFailed 'Running -> Creating must be rejected.'

    Write-NervFullStackManifest -Manifest $reloaded -StateRoot $testRoot
    $admission = Test-NervFullStackAdmission -StateRoot $testRoot -MaximumSessions 1 -ExcludeSessionId 'none'
    Assert-True (-not $admission.Allowed) 'A second active session must be denied at the configured ceiling.'

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
    Assert-True ($renewed.state -eq 'Running') 'Atomic renewal must keep a running session active.'
    Assert-True (@(Claim-NervStaleFullStackSessions -StateRoot $testRoot).Count -eq 0) 'GC must not claim a session renewed before its stale recheck.'

    $renewed.leaseExpiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('O')
    Write-NervFullStackManifest -Manifest $renewed -StateRoot $testRoot
    $claimed = @(Claim-NervStaleFullStackSessions -StateRoot $testRoot)
    Assert-True ($claimed.Count -eq 1 -and $claimed[0] -eq $sessionId) 'GC must atomically claim an actually stale session.'
    $renewAfterClaim = Renew-NervFullStackSessionLease -SessionId $sessionId -StateRoot $testRoot -LeaseMinutes 30
    Assert-True ($renewAfterClaim.state -eq 'Stopping') 'Lease renewal must never overwrite a GC or user stop claim.'

    $reloaded = Read-NervFullStackManifest -SessionId $sessionId -StateRoot $testRoot
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
