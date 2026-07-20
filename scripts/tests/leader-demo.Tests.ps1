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
    $script:startCalls.Add($SessionId)
}
$stopAction = { param($SessionId) $script:stopCalls.Add($SessionId) }
$seedAction = { param($SessionId) $script:seedCalls.Add($SessionId) }
$healthAction = { param($SessionId) $script:healthCalls.Add($SessionId) }

try {
    $env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD = 'controlled-local-password'

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
