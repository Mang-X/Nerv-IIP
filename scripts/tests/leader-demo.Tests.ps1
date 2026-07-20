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
    Write-NervLeaderDemoSessionPointer `
        -SessionId $reservedSessionId `
        -WorktreeRoot $repoRoot `
        -OwnershipState Reserved `
        -StateRoot $reservedStateRoot | Out-Null
    $script:reservedStartCalls = 0
    $reservedFailed = $false
    try {
        Invoke-NervLeaderDemoCommand `
            -Action start `
            -StateRoot $reservedStateRoot `
            -WorktreeRoot $repoRoot `
            -StartSessionAction { param($SessionId) $script:reservedStartCalls++ } | Out-Null
    }
    catch { $reservedFailed = $_.Exception.Message.Contains($reservedSessionId) }
    Assert-True $reservedFailed 'A Reserved ownership pointer must block another start for the same leader-demo slot.'
    Assert-True ($script:reservedStartCalls -eq 0) 'A Reserved ownership pointer must fail before starting another full-stack session.'

    $finalizationStateRoot = Join-Path $stateRoot 'finalization'
    $finalizationSessionIds = [System.Collections.Generic.List[string]]::new()
    $finalizationCleanupIds = [System.Collections.Generic.List[string]]::new()
    $pointerWriteAction = {
        param($SessionId, $WorktreeRoot, $InputStateRoot, $OwnershipState)
        if ($OwnershipState -eq 'Current') { throw 'simulated pointer finalization failure' }
        Write-NervLeaderDemoSessionPointer `
            -SessionId $SessionId `
            -WorktreeRoot $WorktreeRoot `
            -OwnershipState $OwnershipState `
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
