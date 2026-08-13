# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts scenario-owned PostgreSQL, Redis, Aspire and business service processes through governed entrypoints
#     - Creates and removes scenario-owned disposable PostgreSQL databases and Docker resources
#   Writes:
#     - FullChain TRX files and a machine-readable dependency summary under artifacts/**
#     - Existing governed scenario diagnostics under artifacts/acceptance/** and artifacts/fullstack/**
#   Cleanup:
#     - Delegates exact process, database and container cleanup to each governed scenario entrypoint
#     - Fails the lane when an entrypoint or its cleanup fails
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker with PostgreSQL 18 and Redis 8 images

param(
    [string[]] $MemberId = @(),
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'full-chain-test-lane.json'),
    [string] $DatabaseSuffix = "$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())_1",
    [string] $ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/test-evidence-raw/full-chain'),
    [string] $SummaryPath = (Join-Path $PSScriptRoot '../artifacts/full-chain-test-lane/summary.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/FullChainTestLane.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($DatabaseSuffix -cnotmatch '^[a-z0-9_]{1,32}_[1-9][0-9]*$') { throw 'DatabaseSuffix must be canonical and end with an explicit positive run attempt.' }
$manifest = Import-NervFullChainTestLaneManifest -ManifestPath $ManifestPath -RepositoryRoot $repoRoot
if ($MemberId.Count -eq 0) { $MemberId = @($manifest.members.id | ForEach-Object { [string]$_ }) }
$selectedIdSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$selectedMembers = @(
    foreach ($id in $MemberId) {
        if ([string]::IsNullOrWhiteSpace($id) -or -not $selectedIdSet.Add($id)) { throw "FullChain member ids must be non-empty and unique; observed '$id'." }
        $matches = @($manifest.members | Where-Object { [string]::Equals([string]$_.id, $id, [StringComparison]::Ordinal) })
        if ($matches.Count -ne 1) { throw "FullChain member '$id' must resolve exactly once." }
        $matches[0]
    }
)

$adminPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
$redis = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
if ([string]::IsNullOrWhiteSpace($adminPostgres)) { throw 'Set NERV_IIP_TEST_POSTGRES before FullChain lane discovery.' }
if ([string]::IsNullOrWhiteSpace($redis)) { throw 'Set NERV_IIP_TEST_REDIS before FullChain lane discovery.' }

$summary = [ordered]@{
    schemaVersion = 2
    lane = 'full-chain'
    selectedMemberIds = @($MemberId)
    expected = 0
    discovered = 0
    passed = 0
    failed = 0
    skipped = 0
    readiness = [ordered]@{ postgres = 'not-run'; redis = 'not-run' }
    postgresVersion = ''
    redisVersion = ''
    cleanup = 'not-run'
    members = @()
}
$memberSummaries = [Collections.Generic.List[object]]::new()
$firstFailure = $null
$composeFile = Join-Path $repoRoot 'infra/docker-compose.dev.yml'
$initialServices = @()
$ownedServices = [Collections.Generic.List[string]]::new()
$infrastructureCleanup = 'not-run'

function Wait-NervFullChainComposeProbe {
    param(
        [Parameter(Mandatory)] [ValidateSet('postgres', 'redis')] [string] $Dependency,
        [Parameter(Mandatory)] [string] $ComposeFile,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [int] $MaximumAttempts = 30
    )

    $lastFailure = $null
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            if ([string]::Equals($Dependency, 'postgres', [StringComparison]::Ordinal)) {
                return (Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $ComposeFile, 'exec', '-T', 'postgres', 'pg_isready', '-U', 'nerv', '-d', 'postgres', '-q') -WorkingDirectory $RepositoryRoot -Name 'full-chain-postgres-readiness')
            }
            $probe = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $ComposeFile, 'exec', '-T', 'redis', 'redis-cli', '--raw', 'PING') -WorkingDirectory $RepositoryRoot -Name 'full-chain-redis-readiness'
            if (-not [string]::Equals($probe.Stdout.Trim(), 'PONG', [StringComparison]::Ordinal)) { throw 'Redis readiness probe did not return PONG.' }
            return $probe
        }
        catch {
            $lastFailure = $_
            if ($attempt -lt $MaximumAttempts) { Start-Sleep -Seconds 2 }
        }
    }
    throw "FullChain $Dependency readiness did not pass after $MaximumAttempts attempts. $($lastFailure.Exception.Message)"
}

try {
    $runningBefore = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $repoRoot -Name 'full-chain-infrastructure-before'
    $initialServices = @($runningBefore.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $initialSet = [Collections.Generic.HashSet[string]]::new([string[]]$initialServices, [StringComparer]::OrdinalIgnoreCase)
    foreach ($service in @('postgres', 'redis')) { if (-not $initialSet.Contains($service)) { $ownedServices.Add($service) } }
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $repoRoot -TimeoutSeconds 300 -Name 'full-chain-infrastructure-up' | Out-Null
    Wait-NervFullChainComposeProbe -Dependency postgres -ComposeFile $composeFile -RepositoryRoot $repoRoot | Out-Null
    $postgresProbe = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'exec', '-T', 'postgres', 'psql', '-U', 'nerv', '-d', 'postgres', '-X', '-Atqc', "SELECT current_setting('server_version')") -WorkingDirectory $repoRoot -Name 'full-chain-postgres-version'
    $summary.readiness.postgres = 'passed'
    $summary.postgresVersion = $postgresProbe.Stdout.Trim()
    $redisProbe = Wait-NervFullChainComposeProbe -Dependency redis -ComposeFile $composeFile -RepositoryRoot $repoRoot
    if (-not [string]::Equals($redisProbe.Stdout.Trim(), 'PONG', [StringComparison]::Ordinal)) { throw 'FullChain Redis readiness probe did not return PONG.' }
    $redisVersion = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'exec', '-T', 'redis', 'redis-cli', '--raw', 'INFO', 'server') -WorkingDirectory $repoRoot -Name 'full-chain-redis-version'
    $redisVersionLine = @($redisVersion.Stdout -split "`r?`n" | Where-Object { $_.StartsWith('redis_version:', [StringComparison]::Ordinal) })
    if ($redisVersionLine.Count -ne 1) { throw 'FullChain Redis readiness probe did not report exactly one redis_version.' }
    $summary.readiness.redis = 'passed'
    $summary.redisVersion = $redisVersionLine[0].Substring('redis_version:'.Length).Trim()

foreach ($member in $selectedMembers) {
    $memberResultsDirectory = Join-Path $ResultsDirectory ([string]$member.id)
    [IO.Directory]::CreateDirectory($memberResultsDirectory) | Out-Null
    $resultFile = "full-chain-$($member.id).trx"
    $memberSummary = [ordered]@{
        memberId = [string]$member.id
        service = [string]$member.service
        expected = 1
        discovered = 0
        passed = 0
        failed = 0
        skipped = 0
        dependencyEvidence = 'not-run'
        diagnosticEvidence = 'available-on-failure'
        cleanup = 'not-run'
        outcome = 'not-run'
    }
    $memberFailure = $null
    try {
        $discovery = Invoke-DotNetOutput -Name "full-chain-$($member.id)-discovery" -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--list-tests', '--filter', [string]$member.filter)
        $expectedIdentity = [string]$member.expectedTestIdentities[0]
        $discovered = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { [string]::Equals([string]$_, $expectedIdentity, [StringComparison]::Ordinal) })
        $memberSummary.discovered = $discovered.Count
        if ($discovered.Count -ne 1) { throw "FullChain member '$($member.id)' discovery expected 1 frozen test but found $($discovered.Count)." }
        if ($summary.readiness.postgres -ne 'passed' -or ([bool]$member.dependencies.redis -and $summary.readiness.redis -ne 'passed')) { throw "FullChain member '$($member.id)' dependency readiness is incomplete." }
        $memberSummary.dependencyEvidence = 'passed'

        $savedResultsDirectory = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY')
        $savedResultFile = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE')
        $savedFullStackStateRoot = [Environment]::GetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT')
        $savedMessagingProvider = [Environment]::GetEnvironmentVariable('Messaging__Provider')
        $savedPersistenceProvider = [Environment]::GetEnvironmentVariable('Persistence__Provider')
        try {
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY', $memberResultsDirectory)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE', $resultFile)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT', (Join-Path $memberResultsDirectory 'fullstack-state'))
            [Environment]::SetEnvironmentVariable('Messaging__Provider', 'Redis')
            [Environment]::SetEnvironmentVariable('Persistence__Provider', 'PostgreSQL')
            switch ([string]$member.entrypoint.kind) {
                'fullstack' {
                    Invoke-PwshScript -ScriptPath (Join-Path $repoRoot 'nerv.ps1') -Arguments @('fullstack', 'run', '-Scenario', [string]$member.entrypoint.scenario) -WorkingDirectory $repoRoot -TimeoutSeconds 2400 -Name "full-chain-$($member.id)-entrypoint" | Out-Null
                }
                'script' {
                    Invoke-PwshScript -ScriptPath (Join-Path $repoRoot ([string]$member.entrypoint.path)) -WorkingDirectory $repoRoot -TimeoutSeconds 1800 -Name "full-chain-$($member.id)-entrypoint" | Out-Null
                }
                'dotnet' {
                    Invoke-DotNetOutput -Name "full-chain-$($member.id)-entrypoint" -WorkingDirectory $repoRoot -TimeoutSeconds 900 -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--no-restore', '--filter', [string]$member.filter, '--logger', "trx;LogFileName=$resultFile", '--results-directory', $memberResultsDirectory) | Out-Null
                }
                default { throw "Unsupported FullChain entrypoint kind '$($member.entrypoint.kind)'." }
            }
        }
        finally {
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY', $savedResultsDirectory)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE', $savedResultFile)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT', $savedFullStackStateRoot)
            [Environment]::SetEnvironmentVariable('Messaging__Provider', $savedMessagingProvider)
            [Environment]::SetEnvironmentVariable('Persistence__Provider', $savedPersistenceProvider)
        }

        $trx = Get-NervFullChainTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities)
        $memberSummary.passed = $trx.passed
        $memberSummary.failed = $trx.failed
        $memberSummary.skipped = $trx.skipped
        $memberSummary.cleanup = 'passed'
        $memberSummary.outcome = 'passed'
    }
    catch {
        $memberFailure = $_
        $memberSummary.outcome = 'failed'
        $memberSummary.cleanup = 'failed'
        try {
            $trx = Get-NervFullChainTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
            $memberSummary.passed = $trx.passed
            $memberSummary.failed = $trx.failed
            $memberSummary.skipped = $trx.skipped
        }
        catch { }
    }
    $memberSummaries.Add([pscustomobject]$memberSummary)
    if ($null -ne $memberFailure -and $null -eq $firstFailure) { $firstFailure = $memberFailure }
}
}
catch {
    if ($null -eq $firstFailure) { $firstFailure = $_ }
}
finally {
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    if ($ownedServices.Count -gt 0) {
        try {
            $composeProjectName = [Environment]::GetEnvironmentVariable('COMPOSE_PROJECT_NAME')
            if ($ownedServices.Count -eq 2 -and $composeProjectName -cmatch '^nerv_full_chain_[a-z0-9_]+$') {
                Invoke-DockerCompose -Arguments @('-f', $composeFile, 'down', '--volumes', '--remove-orphans') -WorkingDirectory $repoRoot -TimeoutSeconds 300 -Name 'full-chain-infrastructure-down' | Out-Null
            }
            else {
                Invoke-DockerCompose -Arguments (@('-f', $composeFile, 'stop') + @($ownedServices)) -WorkingDirectory $repoRoot -TimeoutSeconds 300 -Name 'full-chain-infrastructure-stop' | Out-Null
            }
        }
        catch { $cleanupFailures.Add($_.Exception.Message) }
        try {
            $runningAfter = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $repoRoot -Name 'full-chain-infrastructure-after'
            $after = [Collections.Generic.HashSet[string]]::new([string[]]@($runningAfter.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }), [StringComparer]::OrdinalIgnoreCase)
            foreach ($ownedService in $ownedServices) { if ($after.Contains($ownedService)) { $cleanupFailures.Add("Owned compose service '$ownedService' is still running.") } }
        }
        catch { $cleanupFailures.Add($_.Exception.Message) }
    }
    $infrastructureCleanup = if ($cleanupFailures.Count -eq 0) { 'passed' } else { 'failed' }
    if ($cleanupFailures.Count -gt 0 -and $null -eq $firstFailure) { $firstFailure = [InvalidOperationException]::new(($cleanupFailures -join ' ')) }
}

$summary.members = @($memberSummaries)
foreach ($memberSummary in $memberSummaries) {
    $summary.expected += [int]$memberSummary.expected
    $summary.discovered += [int]$memberSummary.discovered
    $summary.passed += [int]$memberSummary.passed
    $summary.failed += [int]$memberSummary.failed
    $summary.skipped += [int]$memberSummary.skipped
}
$summary.cleanup = if ($infrastructureCleanup -eq 'passed' -and @($memberSummaries | Where-Object { $_.cleanup -ne 'passed' }).Count -eq 0) { 'passed' } else { 'failed' }
try { Assert-NervFullChainTestLaneSummary -SelectedMemberIds @($MemberId) -MemberSummaries @($memberSummaries) }
catch { if ($null -eq $firstFailure) { $firstFailure = $_ } }
$summaryDirectory = Split-Path -Parent $SummaryPath
if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) { [IO.Directory]::CreateDirectory($summaryDirectory) | Out-Null }
[IO.File]::WriteAllText($SummaryPath, (($summary | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
if ($null -ne $firstFailure) { throw $firstFailure }
Write-Host "FullChain lane passed: expected=$($summary.expected) discovered=$($summary.discovered) passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) cleanup=$($summary.cleanup)."
