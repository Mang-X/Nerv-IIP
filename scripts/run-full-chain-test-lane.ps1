# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts scenario-owned PostgreSQL, Redis, Aspire and business service processes through governed entrypoints
#     - Creates and removes scenario-owned disposable PostgreSQL databases and Docker resources
#   Writes:
#     - FullChain TRX files and a machine-readable dependency summary under artifacts/**
#     - One caller-selected sales-order-demand canonical result when provenance is supplied
#     - Best-effort memory-dimension evidence inside that same dependency summary
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
    [string] $ScenarioMatrixPath = (Join-Path $PSScriptRoot 'acceptance-scenario-matrix.json'),
    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml'),
    [string] $ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/test-evidence-raw/full-chain'),
    [string] $SummaryPath = (Join-Path $PSScriptRoot '../artifacts/full-chain-test-lane/summary.json'),
    [string] $CanonicalResultPath,
    [string] $TrackIdentifier,
    [string] $Repository,
    [string] $RunId,
    [int] $RunAttempt,
    [string] $TestedSha,
    [string] $ManifestDigest,
    [string] $ScenarioId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/FullChainTestLane.ps1')
. (Join-Path $PSScriptRoot 'lib/CiWorkflowBudgets.ps1')
. (Join-Path $PSScriptRoot 'lib/RuntimeMemoryEvidence.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrixRuntime.ps1')
. (Join-Path $PSScriptRoot 'lib/AcceptanceScenarioMatrix.ps1')

$laneStopwatch = [Diagnostics.Stopwatch]::StartNew()

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifest = Import-NervFullChainTestLaneManifest -ManifestPath $ManifestPath -RepositoryRoot $repoRoot
[void](Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $ScenarioMatrixPath -V1ManifestPath $ManifestPath -RepositoryRoot $repoRoot)
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
$projectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($member in $selectedMembers) { $projectSet.Add([string]$member.project) | Out-Null }
if ($projectSet.Count -ne 1) { throw 'Selected FullChain members must share exactly one test project for governed discovery.' }
$fullChainProject = @($projectSet)[0]

$canonicalResultEnabled = -not [string]::IsNullOrWhiteSpace($CanonicalResultPath)
$canonicalResultFullPath = $null
if ($canonicalResultEnabled) {
    $salesMembers = @($selectedMembers | Where-Object { [string]::Equals([string]$_.id, 'sales-order-demand-planning', [StringComparison]::Ordinal) })
    if ($salesMembers.Count -ne 1) { throw 'FullChain canonical output requires the sales-order-demand-planning member to be selected exactly once.' }
    $canonicalResultFullPath = Resolve-NervAcceptanceCanonicalOutputPath -Path $CanonicalResultPath -RepositoryRoot $repoRoot -Context 'FullChain v1 canonical result'
    if (-not (Test-NervAcceptanceRepositoryIdentifier -Repository $Repository)) { throw 'FullChain canonical repository must be a canonical owner/name identifier.' }
    if ($RunId -cnotmatch '^[1-9][0-9]*$') { throw 'FullChain canonical runId must be a positive decimal identifier.' }
    if ($RunAttempt -le 0) { throw 'FullChain canonical runAttempt must be positive.' }
    if ($TestedSha -cnotmatch '^[0-9a-f]{40}$') { throw 'FullChain canonical testedSha must be a lowercase 40-character Git SHA.' }
    if ($ManifestDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'FullChain canonical manifestDigest must be a lowercase SHA-256 digest.' }
    if (-not [string]::Equals($ScenarioId, 'sales-order-demand', [StringComparison]::Ordinal)) { throw "FullChain canonical scenarioId must be 'sales-order-demand'." }
    if ($TrackIdentifier -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') { throw 'FullChain canonical track identifier must be canonical.' }
    if (Test-Path -LiteralPath $canonicalResultFullPath -PathType Leaf) { Remove-Item -LiteralPath $canonicalResultFullPath -Force }
}

$workflowJobs = Get-NervCiWorkflowBudgets -Path $WorkflowPath
$fullChainWorkflowJobs = @($workflowJobs | Where-Object {
    [string]::Equals([string]$_.Name, 'business-full-chain-acceptance-v1', [StringComparison]::Ordinal)
})
if ($fullChainWorkflowJobs.Count -ne 1) {
    throw "Workflow '$WorkflowPath' must define exactly one business-full-chain-acceptance-v1 job."
}
$fullChainRunSteps = @($fullChainWorkflowJobs[0].Steps | Where-Object {
    [string]::Equals([string]$_.Name, 'Run governed FullChain scenarios', [StringComparison]::Ordinal)
})
if ($fullChainRunSteps.Count -ne 1 -or [int]$fullChainRunSteps[0].TimeoutMinutes -le 0) {
    throw "Workflow '$WorkflowPath' must define exactly one timed Run governed FullChain scenarios step."
}
$runStepTimeoutSeconds = [int]$fullChainRunSteps[0].TimeoutMinutes * 60
$infrastructureTimeoutSeconds = 300
$readinessTimeoutSeconds = 30 * 2
$restoreTimeoutSeconds = 600
$discoveryTimeoutSeconds = 600
$fullstackEntrypointTimeoutSeconds = 1200
$scriptEntrypointTimeoutSeconds = 900
$dotnetEntrypointTimeoutSeconds = 600
$cleanupTimeoutSeconds = 300
$timeoutGuardSeconds = 300

$adminPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
$redis = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
if ([string]::IsNullOrWhiteSpace($adminPostgres)) { throw 'Set NERV_IIP_TEST_POSTGRES before FullChain lane discovery.' }
if ([string]::IsNullOrWhiteSpace($redis)) { throw 'Set NERV_IIP_TEST_REDIS before FullChain lane discovery.' }

$summary = [ordered]@{
    schemaVersion = 3
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
$memberSummaryById = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($member in $selectedMembers) {
    $memberSummary = [pscustomobject][ordered]@{
        memberId = [string]$member.id
        service = [string]$member.service
        expected = 1
        discovered = 0
        passed = 0
        failed = 0
        skipped = 0
        dependencyEvidence = 'not-run'
        diagnosticEvidence = 'not-run'
        cleanup = 'not-run'
        outcome = 'not-run'
        deadlineAdmission = [ordered]@{
            reason = 'not-evaluated'
            elapsedSeconds = 0
            remainingSeconds = 0
            requiredSeconds = 0
        }
        # #1664 / #1877：内存维度证据。快照刻意贴着 entrypoint 前后取，取在 lane 首尾会把冷启动
        # 峰值平均掉，正好看不见 137 发生的那一刻。
        memory = [ordered]@{
            beforeEntrypoint = 'not-run'
            afterEntrypoint = 'not-run'
            kernelOomEvidence = 'not-collected'
        }
    }
    $memberSummaries.Add($memberSummary)
    $memberSummaryById.Add([string]$member.id, $memberSummary)
}
$firstFailure = $null
$composeFile = Join-Path $repoRoot 'infra/docker-compose.dev.yml'
$initialServices = @()
$ownedServices = [Collections.Generic.List[string]]::new()
$infrastructureCleanup = 'not-run'
$discoveryLines = @()
$savedMsbuildNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')
$savedDotnetBuildServer = [Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER')
[Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', '1')
[Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', '0')

function Write-NervFullChainSummarySnapshot {
    $summary.members = @($memberSummaries)
    $summary.expected = 0
    $summary.discovered = 0
    $summary.passed = 0
    $summary.failed = 0
    $summary.skipped = 0
    foreach ($memberSummary in $memberSummaries) {
        $summary.expected += [int]$memberSummary.expected
        $summary.discovered += [int]$memberSummary.discovered
        $summary.passed += [int]$memberSummary.passed
        $summary.failed += [int]$memberSummary.failed
        $summary.skipped += [int]$memberSummary.skipped
    }
    $summaryDirectory = Split-Path -Parent $SummaryPath
    if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) { [IO.Directory]::CreateDirectory($summaryDirectory) | Out-Null }
    [IO.File]::WriteAllText($SummaryPath, (($summary | ConvertTo-Json -Depth 12) + "`n"), [Text.UTF8Encoding]::new($false))
}

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

Write-NervFullChainSummarySnapshot
$laneAction = {
try {
    $runningBefore = Invoke-NativeCommandOutput -Command 'docker' -Arguments @('compose', '-f', $composeFile, 'ps', '--services', '--status', 'running') -WorkingDirectory $repoRoot -Name 'full-chain-infrastructure-before'
    $initialServices = @($runningBefore.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $initialSet = [Collections.Generic.HashSet[string]]::new([string[]]$initialServices, [StringComparer]::OrdinalIgnoreCase)
    foreach ($service in @('postgres', 'redis')) { if (-not $initialSet.Contains($service)) { $ownedServices.Add($service) } }
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', '--pull', 'never', 'postgres', 'redis') -WorkingDirectory $repoRoot -TimeoutSeconds $infrastructureTimeoutSeconds -Name 'full-chain-infrastructure-up' | Out-Null
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

    Write-NervFullChainSummarySnapshot
    Invoke-DotNetOutput -Name 'full-chain-project-restore' -WorkingDirectory $repoRoot -TimeoutSeconds $restoreTimeoutSeconds -Arguments @('restore', $fullChainProject) | Out-Null
    $discovery = Invoke-DotNetOutput -Name 'full-chain-project-discovery' -WorkingDirectory $repoRoot -TimeoutSeconds $discoveryTimeoutSeconds -Arguments @('test', $fullChainProject, '--configuration', 'Release', '--no-restore', '--list-tests')
    $discoveryLines = @($discovery.Stdout -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($member in $selectedMembers) {
        $memberSummary = $memberSummaryById[[string]$member.id]
        $expectedIdentity = [string]$member.expectedTestIdentities[0]
        $discovered = @($discoveryLines | Where-Object { [string]::Equals([string]$_, $expectedIdentity, [StringComparison]::Ordinal) })
        $memberSummary.discovered = $discovered.Count
        if ($discovered.Count -ne 1) { throw "FullChain member '$($member.id)' discovery expected 1 frozen test but found $($discovered.Count)." }
    }
    Write-NervFullChainSummarySnapshot

foreach ($member in $selectedMembers) {
    $memberResultsDirectory = Join-Path $ResultsDirectory ([string]$member.id)
    $resultFile = "full-chain-$($member.id).trx"
    $memberSummary = $memberSummaryById[[string]$member.id]
    Write-NervFullChainSummarySnapshot
    $memberFailure = $null
    $entrypointKind = [string]$member.entrypoint.kind
    $memberAction = {
        param($admittedMemberId)

        if (-not [string]::Equals([string]$summary.readiness.postgres, 'passed', [StringComparison]::Ordinal) -or
            ([bool]$member.dependencies.redis -and -not [string]::Equals([string]$summary.readiness.redis, 'passed', [StringComparison]::Ordinal))) { throw "FullChain member '$admittedMemberId' dependency readiness is incomplete." }
        $memberSummary.dependencyEvidence = 'passed'
        [IO.Directory]::CreateDirectory($memberResultsDirectory) | Out-Null

        $savedResultsDirectory = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY')
        $savedResultFile = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE')
        $savedFullStackStateRoot = [Environment]::GetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT')
        $savedMessagingProvider = [Environment]::GetEnvironmentVariable('Messaging__Provider')
        $savedPersistenceProvider = [Environment]::GetEnvironmentVariable('Persistence__Provider')
        $savedEntrypointEvidencePath = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH')
        $savedFullChainConfiguration = [Environment]::GetEnvironmentVariable('NERV_IIP_FULL_CHAIN_CONFIGURATION')
        $entrypointEvidencePath = Join-Path $memberResultsDirectory 'entrypoint-evidence.json'
        try {
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY', $memberResultsDirectory)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE', $resultFile)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT', (Join-Path $memberResultsDirectory 'fullstack-state'))
            [Environment]::SetEnvironmentVariable('Messaging__Provider', 'Redis')
            [Environment]::SetEnvironmentVariable('Persistence__Provider', 'PostgreSQL')
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH', $entrypointEvidencePath)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_CONFIGURATION', 'Release')
            $memberSummary.memory.beforeEntrypoint = Get-NervRuntimeMemorySnapshot -Phase 'before-entrypoint'
            Write-NervFullChainSummarySnapshot
            if ([string]::Equals($entrypointKind, 'fullstack', [StringComparison]::Ordinal)) {
                Invoke-PwshScript -ScriptPath (Join-Path $repoRoot 'nerv.ps1') -Arguments @('fullstack', 'run', '-Scenario', [string]$member.entrypoint.scenario) -WorkingDirectory $repoRoot -TimeoutSeconds $fullstackEntrypointTimeoutSeconds -Name "full-chain-$admittedMemberId-entrypoint" | Out-Null
            }
            elseif ([string]::Equals($entrypointKind, 'script', [StringComparison]::Ordinal)) {
                $scriptArguments = @()
                if ($canonicalResultEnabled -and [string]::Equals($admittedMemberId, 'sales-order-demand-planning', [StringComparison]::Ordinal)) {
                    $scriptArguments = @(
                        '-CanonicalResultPath', $canonicalResultFullPath,
                        '-TrackIdentifier', $TrackIdentifier,
                        '-Repository', $Repository,
                        '-RunId', $RunId,
                        '-RunAttempt', [string]$RunAttempt,
                        '-TestedSha', $TestedSha,
                        '-ManifestDigest', $ManifestDigest,
                        '-ScenarioId', $ScenarioId
                    )
                }
                Invoke-PwshScript -ScriptPath (Join-Path $repoRoot ([string]$member.entrypoint.path)) -Arguments $scriptArguments -WorkingDirectory $repoRoot -TimeoutSeconds $scriptEntrypointTimeoutSeconds -Name "full-chain-$admittedMemberId-entrypoint" | Out-Null
                if ($canonicalResultEnabled -and [string]::Equals($admittedMemberId, 'sales-order-demand-planning', [StringComparison]::Ordinal) -and
                    -not (Test-Path -LiteralPath $canonicalResultFullPath -PathType Leaf)) {
                    throw 'FullChain sales-order-demand member did not produce its canonical result.'
                }
            }
            elseif ([string]::Equals($entrypointKind, 'dotnet', [StringComparison]::Ordinal)) {
                Invoke-DotNetOutput -Name "full-chain-$admittedMemberId-entrypoint" -WorkingDirectory $repoRoot -TimeoutSeconds $dotnetEntrypointTimeoutSeconds -Arguments @('test', [string]$member.project, '--configuration', 'Release', '--no-restore', '--no-build', '--filter', [string]$member.filter, '--logger', "trx;LogFileName=$resultFile", '--results-directory', $memberResultsDirectory) | Out-Null
            }
        }
        finally {
            # finally 而不是成功路径：entrypoint 被杀掉那一刻的内存读数正是本票要的那条证据。
            $memberSummary.memory.afterEntrypoint = Get-NervRuntimeMemorySnapshot -Phase 'after-entrypoint'
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULTS_DIRECTORY', $savedResultsDirectory)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_RESULT_FILE', $savedResultFile)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULLSTACK_STATE_ROOT', $savedFullStackStateRoot)
            [Environment]::SetEnvironmentVariable('Messaging__Provider', $savedMessagingProvider)
            [Environment]::SetEnvironmentVariable('Persistence__Provider', $savedPersistenceProvider)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_ENTRYPOINT_EVIDENCE_PATH', $savedEntrypointEvidencePath)
            [Environment]::SetEnvironmentVariable('NERV_IIP_FULL_CHAIN_CONFIGURATION', $savedFullChainConfiguration)
        }
    }
    try {
        $elapsedSeconds = [int64][Math]::Ceiling($laneStopwatch.Elapsed.TotalSeconds)
        $admission = Invoke-NervFullChainMemberAdmission `
            -MemberId ([string]$member.id) `
            -EntrypointKind $entrypointKind `
            -GlobalDeadlineSeconds $runStepTimeoutSeconds `
            -ElapsedSeconds $elapsedSeconds `
            -FullstackEntrypointTimeoutSeconds $fullstackEntrypointTimeoutSeconds `
            -ScriptEntrypointTimeoutSeconds $scriptEntrypointTimeoutSeconds `
            -DotnetEntrypointTimeoutSeconds $dotnetEntrypointTimeoutSeconds `
            -CleanupReserveSeconds $cleanupTimeoutSeconds `
            -GuardReserveSeconds $timeoutGuardSeconds `
            -MemberSummary $memberSummary `
            -Action $memberAction
        if (-not $admission.Allowed) {
            $memberFailure = [InvalidOperationException]::new("FullChain member '$($member.id)' deadline admission denied: reason=$($admission.Reason) elapsed=$elapsedSeconds remaining=$($admission.RemainingSeconds) required=$($admission.RequiredSeconds).")
            if ($null -eq $firstFailure) { $firstFailure = $memberFailure }
            Write-NervFullChainSummarySnapshot
            continue
        }

        $trx = Get-NervFullChainTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities)
        $memberSummary.passed = $trx.passed
        $memberSummary.failed = $trx.failed
        $memberSummary.skipped = $trx.skipped
        $entrypointEvidence = Assert-NervFullChainMemberEvidence -Member $member -MemberResultsDirectory $memberResultsDirectory -RepositoryRoot $repoRoot
        $memberSummary.diagnosticEvidence = $entrypointEvidence.diagnosticEvidence
        $memberSummary.cleanup = $entrypointEvidence.cleanup
        $memberSummary.outcome = 'passed'
    }
    catch {
        $memberFailure = $_
        $memberSummary.outcome = 'failed'
        $memberSummary.cleanup = 'failed'
        $memberSummary.memory.kernelOomEvidence = Get-NervRuntimeKernelOomEvidence -WorkingDirectory $repoRoot
        try {
            $trx = Get-NervFullChainTrxResult -ResultsDirectory $memberResultsDirectory -ExpectedTestIdentities @($member.expectedTestIdentities) -AllowInvalid
            $memberSummary.passed = $trx.passed
            $memberSummary.failed = $trx.failed
            $memberSummary.skipped = $trx.skipped
        }
        catch { }
    }
    Write-NervFullChainSummarySnapshot
    if ($null -ne $memberFailure -and $null -eq $firstFailure) { $firstFailure = $memberFailure }
}
}
catch {
    if ($null -eq $firstFailure) { $firstFailure = $_ }
}
}
$laneFinalizeAction = {
    $cleanupFailures = [Collections.Generic.List[string]]::new()
    if ($ownedServices.Count -gt 0) {
        try {
            $composeProjectName = [Environment]::GetEnvironmentVariable('COMPOSE_PROJECT_NAME')
            if ($ownedServices.Count -eq 2 -and $composeProjectName -cmatch '^nerv_full_chain_[a-z0-9_]+$') {
                Invoke-DockerCompose -Arguments @('-f', $composeFile, 'down', '--volumes', '--remove-orphans') -WorkingDirectory $repoRoot -TimeoutSeconds $cleanupTimeoutSeconds -Name 'full-chain-infrastructure-down' | Out-Null
            }
            else {
                Invoke-DockerCompose -Arguments (@('-f', $composeFile, 'stop') + @($ownedServices)) -WorkingDirectory $repoRoot -TimeoutSeconds $cleanupTimeoutSeconds -Name 'full-chain-infrastructure-stop' | Out-Null
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
    [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', $savedMsbuildNodeReuse)
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER', $savedDotnetBuildServer)
    $summary.cleanup = if (
        [string]::Equals($infrastructureCleanup, 'passed', [StringComparison]::Ordinal) -and
        @($memberSummaries | Where-Object { -not [string]::Equals([string]$_.cleanup, 'passed', [StringComparison]::Ordinal) }).Count -eq 0
    ) { 'passed' } else { 'failed' }
    Write-NervFullChainSummarySnapshot

    try { Assert-NervFullChainTestLaneSummary -SelectedMemberIds @($MemberId) -MemberSummaries @($memberSummaries) }
    catch { if ($null -eq $firstFailure) { $firstFailure = $_ } }
    Write-NervFullChainSummarySnapshot
    if ($null -ne $firstFailure) { throw $firstFailure }
    Write-Host "FullChain lane passed: expected=$($summary.expected) discovered=$($summary.discovered) passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) cleanup=$($summary.cleanup)."
}
Invoke-NervFullChainLaneScope -Action $laneAction -FinalizeAction $laneFinalizeAction
