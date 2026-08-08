# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes one temporary unclassified backend test project
#     - Creates and removes one temporary backend project that is not a solution member
#   Writes:
#     - backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/** (temporarily)
#     - backend/common/Nerv.IIP.TemporarySolutionMembership/** (temporarily)
#     - OS temporary directory: workflow, manifest, policy, shard TRX and timing-cache fixtures (temporarily)
#     - artifacts/backend-test-shards-collision-*.cs selector-collision fixture (temporarily)
#     - artifacts/shard-fixture-*.slnf rearranged solution filters (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every temporary project, workflow, manifest, policy, TRX, timing-cache, solution filter and collision fixture in finally
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/backend-test-shards.json'
$validatorPath = Join-Path $repoRoot 'scripts/verify-backend-test-shards.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$temporaryProjectDirectory = Join-Path $repoRoot 'backend/tests/Nerv.IIP.TemporaryShardClassification.Tests'
$temporaryProjectPath = Join-Path $temporaryProjectDirectory 'Nerv.IIP.TemporaryShardClassification.Tests.csproj'
$temporarySolutionMemberDirectory = Join-Path $repoRoot 'backend/common/Nerv.IIP.TemporarySolutionMembership'
$temporarySolutionMemberPath = Join-Path $temporarySolutionMemberDirectory 'Nerv.IIP.TemporarySolutionMembership.csproj'
$temporaryWorkflowPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-{0}.yml" -f [Guid]::NewGuid().ToString('N'))
$timeoutResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-timeout-{0}" -f [Guid]::NewGuid().ToString('N'))
$executionTrxDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-execution-{0}" -f [Guid]::NewGuid().ToString('N'))
$temporaryPolicyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-policy-{0}.json" -f [Guid]::NewGuid().ToString('N'))
$temporaryManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-manifest-{0}.json" -f [Guid]::NewGuid().ToString('N'))
# The validator resolves policy sourcePath against the repository root, so the collision fixture
# must live inside the repo. artifacts/ is gitignored.
$temporaryCollisionRelativePath = "artifacts/backend-test-shards-collision-{0}.cs" -f [Guid]::NewGuid().ToString('N')
$temporaryCollisionSourcePath = Join-Path $repoRoot $temporaryCollisionRelativePath
$runnerPath = Join-Path $repoRoot 'scripts/run-backend-test-shard.ps1'
$diagnosticsPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardDiagnostics.ps1'
$selectorAssertionsPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardSelectors.ps1'

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

# Every assertion below is about *what the script under test said*, so it is always run as a real
# process and judged by its exit code plus its output.
#
# The validator reports findings on stdout and exits 1 rather than throwing, which is what lets
# these assertions match whole sentences instead of the short fragments a thrown (and therefore
# width-wrapped) message forced — this file used to also scrape the command log to reassemble that
# text, and both workarounds are gone. Why the shape matters:
# docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 3 条).
#
# Whitespace is collapsed so that where the script chose to break lines is not part of the
# contract. The assertions are about content, not layout.
#
# One helper, parameterized by script path: the validator, the balance report and the timing
# refresher are three programs invoked identically, and this file previously carried three
# character-identical copies of this body that differed only in which path they hard-coded.
function Invoke-GovernedScript {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $ScriptPath) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 300 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
}

Assert-Contract (Test-Path -LiteralPath $manifestPath) 'Backend test shard manifest is missing.'
Assert-Contract (Test-Path -LiteralPath $validatorPath) 'Backend test shard validator is missing.'

Invoke-PwshScript -ScriptPath $validatorPath -WorkingDirectory $repoRoot -Name 'backend-test-shard-validator'

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
Assert-Contract ($fastShards.Count -eq 4) 'Phase 1 must define exactly four fast backend shards.'
Assert-Contract (((@($fastShards.id) | Sort-Object) -join '|') -ceq 'business-core-a|business-core-b|business-gateway|platform') 'Fast shard IDs must remain the four phase-1 CI jobs.'
Assert-Contract (((@($heavyLanes.id) | Sort-Object) -join '|') -ceq 'full-chain|performance|real-postgres') 'Heavy lane IDs must remain explicit and separate from fast shards.'
$businessGatewayShard = @($fastShards | Where-Object { $_.id -eq 'business-gateway' })
# The BusinessGateway assembly used to be alone in its shard because it cost 869s and serialized
# every other assembly behind it. MAN-663 removed that cost (23s on run 30999368607) and MAN-669
# PR-A rebalanced the shards by measured TRX elapsed, so "exactly one project" is no longer the
# contract — the lane identity is. What must not drift is which shard owns that assembly, because
# MAN-661 maps evidence lane backend-shard-1 to this job's name.
Assert-Contract ($businessGatewayShard.Count -eq 1 -and @($businessGatewayShard[0].projects) -contains 'backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj') 'The BusinessGateway assembly must stay in the fast shard whose evidence lane is named after it.'
# Which fast shard owns the acceptance suite is a balancing decision (PR-A moved it from
# business-core-b to business-core-a); that it stays inside the *default fast gate* rather than
# drifting into an opt-in heavy lane is the contract.
$acceptanceOwners = @($fastShards | Where-Object { @($_.projects) -contains 'backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj' })
Assert-Contract ($acceptanceOwners.Count -eq 1) 'Regular business acceptance facts must be part of the default fast gate.'
$excludedSelectors = @(
    foreach ($shard in $fastShards) {
        $classes = $shard.PSObject.Properties['excludedTestClasses']
        $methods = $shard.PSObject.Properties['excludedTests']
        if ($null -ne $classes) { @($classes.Value) }
        if ($null -ne $methods) { @($methods.Value) }
    }
)
Assert-Contract ($excludedSelectors.Count -eq 49) 'Every currently excluded real PostgreSQL test selector must be explicitly classified.'
Assert-Contract ($excludedSelectors -contains 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed') 'The PostgreSQL test database real selector must remain method-scoped.'
Assert-Contract (-not ($excludedSelectors -contains 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests')) 'A mixed fast test class must not be excluded wholesale.'
$platformShard = @($fastShards | Where-Object { $_.id -eq 'platform' })[0]
$platformExcludedClasses = @($platformShard.excludedTestClasses)
$platformExcludedTestsProperty = $platformShard.PSObject.Properties['excludedTests']
$platformExcludedTests = if ($null -eq $platformExcludedTestsProperty) { @() } else { @($platformExcludedTestsProperty.Value) }
Assert-Contract ($platformExcludedTests -contains 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed') 'The PostgreSQL test database real selector must be in excludedTests, not the class selector list.'
Assert-Contract ($platformExcludedTests -contains 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Initializer_failure_drops_database_and_redacts_diagnostics') 'Every narrowed PostgreSQL database selector must be method-scoped.'
Assert-Contract (-not ($platformExcludedClasses -contains 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed')) 'A method selector must not be treated as a class selector.'
Assert-Contract (Test-Path -LiteralPath $diagnosticsPath) 'Timeout diagnostics must use a separately testable helper, not a production command bypass.'
Assert-Contract (Test-Path -LiteralPath $selectorAssertionsPath) 'Real PostgreSQL selector discovery and execution checks must be separately testable.'
. $diagnosticsPath
. $selectorAssertionsPath

$runnerBypassText = ''
try {
    Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $runnerPath, '-ShardId', 'platform', '-ResultsDirectory', $timeoutResultsDirectory, '-TrxFilePrefix', 'bypass-contract', '-TestCommand', 'Write-Output pass') -WorkingDirectory $repoRoot -Name 'backend-test-shard-command-parameter-contract' | Out-Null
    throw 'The production fast-shard runner must reject a command replacement parameter.'
}
catch {
    $runnerBypassText = $_.Exception.Message
}
Assert-Contract ($runnerBypassText.Contains("A parameter cannot be found that matches parameter name 'TestCommand'")) 'The production fast-shard runner must reject a command replacement parameter before test execution.'

$staleSelectorText = ''
try {
    Assert-BackendTestShardSelectorDiscovery -Selector 'Nerv.IIP.Tests.StaleSelector' -MethodSelector $true -DiscoveredTests @()
}
catch {
    $staleSelectorText = $_.Exception.Message
}
Assert-Contract ($staleSelectorText.Contains("Real PostgreSQL selector 'Nerv.IIP.Tests.StaleSelector' discovery must match exactly one test")) 'A stale real PostgreSQL selector must fail discovery before execution.'

$classSelector = 'Nerv.IIP.Tests.ClassSelector'
$classDiscovery = @(Assert-BackendTestShardSelectorDiscovery -Selector $classSelector -MethodSelector $false -DiscoveredTests @("$classSelector.CaseOne", "$classSelector.CaseTwo"))
Assert-Contract ($classDiscovery.Count -eq 2) 'A class-scoped real PostgreSQL selector must retain every discovered test.'
Assert-BackendTestShardSelectorExecution -Selector $classSelector -DiscoveredTests $classDiscovery -TrxResults @(
    [pscustomobject]@{ testName = "$classSelector.CaseOne"; outcome = 'Passed' },
    [pscustomobject]@{ testName = "$classSelector.CaseTwo"; outcome = 'Passed' }
)

$notExecutedSelectorText = ''
try {
    Assert-BackendTestShardSelectorExecution -Selector 'Nerv.IIP.Tests.DiscoveredSelector' -DiscoveredTests @('Nerv.IIP.Tests.DiscoveredSelector.Case') -TrxResults @()
}
catch {
    $notExecutedSelectorText = $_.Exception.Message
}
Assert-Contract ($notExecutedSelectorText.Contains("Real PostgreSQL selector 'Nerv.IIP.Tests.DiscoveredSelector' must execute every discovered test as Passed")) 'A discovered real PostgreSQL selector without TRX execution must fail closed.'

$runnerSource = Get-Content -LiteralPath $runnerPath -Raw
Assert-Contract (-not $runnerSource.Contains('No test matches the given testcase filter')) 'The zero-execution guard must not depend on localized dotnet console text.'
Assert-Contract ($runnerSource.Contains('Assert-BackendTestShardProjectExecution')) 'The fast shard runner must prove classified-project execution from the TRX the MAN-661 collector consumes.'
Assert-Contract ($runnerSource.Contains('"FullyQualifiedName!~$_."')) 'Class selectors must be anchored with a trailing dot so a sibling class sharing the prefix is not silently excluded.'

New-Item -ItemType Directory -Path $executionTrxDirectory -Force | Out-Null
Set-Content -LiteralPath (Join-Path $executionTrxDirectory 'shard.trx') -NoNewline -Value @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="00000000-0000-0000-0000-000000000001" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions>
    <UnitTest id="00000000-0000-0000-0000-000000000002" name="Case" storage="/w/bin/Release/net10.0/Nerv.IIP.Coding.Tests.dll"><TestMethod className="Nerv.IIP.Coding.Tests.CodingTests" name="Case" /></UnitTest>
  </TestDefinitions>
</TestRun>
'@
$executedAssemblies = @(Get-BackendTestShardExecutedAssemblies -ResultsDirectory $executionTrxDirectory)
Assert-Contract ((@($executedAssemblies) -join '|') -ceq 'Nerv.IIP.Coding.Tests.dll') 'Executed shard assemblies must be read from namespaced TRX storage attributes.'
Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj') -ExecutedAssemblies $executedAssemblies

$zeroExecutionText = ''
try {
    Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj', 'backend/tests/Nerv.IIP.Silent.Tests/Nerv.IIP.Silent.Tests.csproj') -ExecutedAssemblies $executedAssemblies
}
catch {
    $zeroExecutionText = $_.Exception.Message
}
Assert-Contract ($zeroExecutionText.Contains('produced no executed test result for classified projects: Nerv.IIP.Silent.Tests')) 'A classified project whose tests were all filtered away must fail closed regardless of console language.'

$driftText = ''
try {
    Assert-BackendTestShardProjectExecution -ShardId 'contract' -ClassifiedProjects @('backend/tests/Nerv.IIP.Coding.Tests/Nerv.IIP.Coding.Tests.csproj') -ExecutedAssemblies @($executedAssemblies + 'Nerv.IIP.Drifted.Tests.dll')
}
catch {
    $driftText = $_.Exception.Message
}
Assert-Contract ($driftText.Contains('executed assemblies it does not classify: Nerv.IIP.Drifted.Tests')) 'A shard running an assembly it does not classify must fail closed.'

$classifiedProjects = @($fastShards.projects) + @($heavyLanes.projects)
Assert-Contract (($classifiedProjects | Sort-Object -Unique).Count -eq $classifiedProjects.Count) 'Every backend test project must be classified exactly once.'
Assert-Contract ($classifiedProjects.Count -eq 66) 'The checked-in backend test inventory must contain 66 classified projects.'
Assert-Contract (@($fastShards | Where-Object { $_.id -eq 'platform' })[0].projects -contains 'backend/tests/Nerv.IIP.Testing.Tests/Nerv.IIP.Testing.Tests.csproj') 'MAN-662 shared test-infrastructure facts must run in the default fast gate.'
Assert-Contract (@($fastShards | Where-Object { $_.id -eq 'platform' })[0].projects -contains 'backend/tests/Nerv.IIP.FastEndpoints.ProcessIsolation.Tests/Nerv.IIP.FastEndpoints.ProcessIsolation.Tests.csproj') 'MAN-662 FastEndpoints process-isolation facts must run in the default fast gate.'
Assert-Contract (((@($fastShards.evidenceLane) | Sort-Object) -join '|') -ceq 'backend-shard-1|backend-shard-2|backend-shard-3|backend-shard-4') 'Every fast shard must own one MAN-661 schema-v1 backend shard lane.'
Assert-Contract ((@($fastShards.jobName) | Sort-Object -Unique).Count -eq $fastShards.Count) 'Every fast shard evidence lane must be owned by exactly one CI job name.'
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')
$laneJobs = Get-NervTestEvidenceLaneJobs
foreach ($shard in $fastShards) {
    Assert-Contract ($laneJobs.Contains([string] $shard.evidenceLane)) "Fast shard evidence lane '$($shard.evidenceLane)' must be allowlisted for MAN-661 rerun and baseline authority."
    Assert-Contract ([string] $laneJobs[[string] $shard.evidenceLane] -ceq [string] $shard.jobName) "Fast shard evidence lane '$($shard.evidenceLane)' must be bound to its own CI job name."
}

foreach ($shard in $fastShards) {
    $filterPath = Join-Path $repoRoot $shard.solutionFilter
    $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
    Assert-Contract ($filter.solution.path -eq '../Nerv.IIP.sln') "Solution filter $($shard.solutionFilter) must target the backend solution."
    Assert-Contract ((@($filter.solution.projects | Where-Object { $_ -match '^\.\./' })).Count -eq 0) "Solution filter $($shard.solutionFilter) project paths must be relative to backend/Nerv.IIP.sln."
}

# Solution membership must be enforced for *non-test* backend projects too. A project reachable only
# as a transitive ProjectReference has no entry in the solution configuration map, so a
# `--configuration Release` shard emits it into bin/Debug and every shard silently tests Release
# assemblies linked against a Debug dependency. Planting a non-test project proves the check is the
# general one and not the pre-existing `*.Tests.csproj`-only rule: this fixture is invisible to that
# rule, so if the general check is weakened away the validator passes and this contract goes red.
$solutionMembership = $null
try {
    New-Item -ItemType Directory -Path $temporarySolutionMemberDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporarySolutionMemberPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $solutionMembership = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-solution-membership'
}
finally {
    if (Test-Path -LiteralPath $temporarySolutionMemberDirectory) {
        Remove-Item -LiteralPath $temporarySolutionMemberDirectory -Recurse -Force
    }
}
Assert-Contract (-not $solutionMembership.Passed) 'A backend project outside backend/Nerv.IIP.sln must fail shard governance.'
Assert-Contract ($solutionMembership.Message.Contains('bin/Debug')) 'Shard governance must reject a backend project that is not a solution member, naming the Release/Debug consequence.'
Assert-Contract ($solutionMembership.Message.Contains('backend/common/Nerv.IIP.TemporarySolutionMembership/Nerv.IIP.TemporarySolutionMembership.csproj')) 'The solution-membership failure must identify the offending project path.'
Assert-Contract (-not $solutionMembership.Message.Contains('Unclassified backend test')) 'The solution-membership contract must be tripped by a non-test project, not by the test classification rule.'
Assert-Contract (@(Get-Content -LiteralPath (Join-Path $repoRoot 'backend/Nerv.IIP.sln') | Where-Object { $_ -match 'Nerv\.IIP\.Contracts\.Mes\.csproj' }).Count -eq 1) 'Nerv.IIP.Contracts.Mes must stay a solution member; outside the solution every Release shard builds it as Debug.'

try {
    New-Item -ItemType Directory -Path $temporaryProjectDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporaryProjectPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $unclassified = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-unclassified-project'
    Assert-Contract (-not $unclassified.Passed) 'An unclassified temporary backend test project must fail classification.'
    Assert-Contract ($unclassified.Message.Contains('Unclassified backend test')) 'Unclassified project failure must identify the classification error.'
    Assert-Contract ($unclassified.Message.Contains('backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/Nerv.IIP.TemporaryShardClassification.Tests.csproj')) 'Unclassified project failure must identify the temporary project path.'

    $workflowContent = Get-Content -LiteralPath $workflowPath -Raw
    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^\s+- backend-tests-business-core-b\r?\n', '') -NoNewline
    $workflowValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-workflow-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $workflowValidation.Passed) 'A workflow with a missing aggregate dependency must fail structured shard governance.'
    Assert-Contract ($workflowValidation.Message.Contains('Backend Tests aggregate must need exactly the governance and four fast shard jobs.')) 'Structured workflow validation must reject an aggregate with a missing shard dependency.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'echo "${{ needs.backend-tests-platform.result }}"') -NoNewline
    $noOpValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-noop-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $noOpValidation.Passed) 'A no-op aggregate dependency expression must fail structured shard governance.'
    Assert-Contract ($noOpValidation.Message.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a non-failing aggregate dependency expression.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'test "${{ needs.backend-tests-platform.result }}" = "success" || true') -NoNewline
    $maskedFailureValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-masked-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $maskedFailureValidation.Passed) 'An aggregate assertion masked with || true must fail structured shard governance.'
    Assert-Contract ($maskedFailureValidation.Message.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a masked aggregate dependency assertion.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(\s+- name: Require all backend fast shards\r?\n)', ('$1        continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $continueOnErrorValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $continueOnErrorValidation.Passed) 'An aggregate step with continue-on-error must fail structured shard governance.'
    Assert-Contract ($continueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")) 'Structured workflow validation must reject an aggregate continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(    if: always\(\)\r?\n)', ('$1    continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $jobContinueOnErrorValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-job-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $jobContinueOnErrorValidation.Passed) 'An aggregate job with continue-on-error must fail structured shard governance.'
    Assert-Contract ($jobContinueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")) 'Structured workflow validation must reject an aggregate job continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)(-TrxFilePrefix backend-tests-platform)', '$1 -TestCommand "Write-Output pass"') -NoNewline
    $bypassValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-command-bypass-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $bypassValidation.Passed) 'A fast shard command replacement parameter must fail structured shard governance.'
    Assert-Contract ($bypassValidation.Message.Contains("Fast shard job 'backend-tests-platform' must not supply a command replacement parameter.")) 'Structured workflow validation must reject a command replacement parameter.'

    foreach ($evidenceMutation in @(
            @{
                Name = 'raw-artifact-upload'
                Pattern = '(?m)^(\s+)path: \$\{\{ steps\.collect-shard-evidence\.outputs\.evidence-path \}\}'
                Replacement = '$1path: artifacts/test-evidence-raw/${{ github.run_id }}/attempt-${{ github.run_attempt }}/backend-shard-1'
                Expected = 'must upload only the collector-published redacted evidence path'
            },
            @{
                Name = 'sibling-lane-claim'
                Pattern = '-SelectedLanes backend-shard-1'
                Replacement = '-SelectedLanes backend-shard-2'
                Expected = "must not claim the sibling evidence lane 'backend-shard-2'"
            },
            @{
                Name = 'piped-shard-runner'
                Pattern = '(?m)^(\s+)-TrxFilePrefix backend-tests-platform$'
                Replacement = '$1-TrxFilePrefix backend-tests-platform | tee shard.log'
                Expected = 'must not wrap the shard runner in a shell pipeline'
            },
            @{
                Name = 'best-effort-collection'
                Pattern = '(?m)^(\s+)id: collect-shard-evidence\r?\n(\s+)if: always\(\)'
                Replacement = '$1id: collect-shard-evidence' + [Environment]::NewLine + '$2if: success()'
                Expected = 'evidence collection must run with if: always()'
            }
        )) {
        Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace $evidenceMutation.Pattern, $evidenceMutation.Replacement) -NoNewline
        $evidenceValidation = Invoke-GovernedScript -ScriptPath $validatorPath -Name "backend-test-shard-evidence-$($evidenceMutation.Name)-contract" -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
        Assert-Contract (-not $evidenceValidation.Passed) "Evidence mutation '$($evidenceMutation.Name)' must fail structured shard governance."
        Assert-Contract ($evidenceValidation.Message.Contains($evidenceMutation.Expected)) "Structured workflow validation must reject the '$($evidenceMutation.Name)' evidence mutation."
    }

    $policy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    foreach ($rule in @($policy.rules)) {
        if ([string] $rule.requiredLane -ceq 'postgres') {
            $rule.testIdentities = @()
            $rule.expectedRuntimeTestCount = 0
            break
        }
    }
    Set-Content -LiteralPath $temporaryPolicyPath -Value ($policy | ConvertTo-Json -Depth 100) -NoNewline
    $policyCoverage = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-policy-coverage-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
    Assert-Contract (-not $policyCoverage.Passed) 'A fast shard exclusion without a MAN-661 registered skip must fail shard governance.'
    Assert-Contract ($policyCoverage.Message.Contains('is not registered in the MAN-661 evidence policy as an environment-gated real-dependency skip')) 'Shard governance must reject an exclusion the evidence policy does not register.'

    # The under-declaration has to be planted on whichever shard currently owns the one exclusion
    # whose MAN-661 requiredLane is not `postgres`; pinning that to a shard id made this negative
    # test silently pass the moment MAN-669 PR-A moved that exclusion to another shard.
    $laneManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $fullChainShards = @($laneManifest.fastShards | Where-Object { @($_.excludedTestLanes | ForEach-Object { [string] $_ }) -contains 'full-chain' })
    Assert-Contract ($fullChainShards.Count -eq 1) 'Exactly one fast shard must own the full-chain exclusion for the lane-attribution contract to be able to under-declare it.'
    $fullChainShards[0].excludedTestLanes = @('real-postgres')
    Set-Content -LiteralPath $temporaryManifestPath -Value ($laneManifest | ConvertTo-Json -Depth 100) -NoNewline
    $laneAttribution = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-lane-attribution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
    Assert-Contract (-not $laneAttribution.Passed) 'A shard that under-declares its excluded test lanes must fail shard governance.'
    Assert-Contract ($laneAttribution.Message.Contains('must declare excludedTestLanes [full-chain, real-postgres]')) 'Shard governance must derive owner lanes from the MAN-661 requiredLane instead of trusting the declaration.'

    # MAN-669 PR-B: no shard may fall back to building the whole solution. backend/Nerv.IIP.sln is a
    # readable file and would otherwise be reported as a malformed solution filter rather than as
    # the thing it is, so the rejection has to be explicit — and therefore has to be tested.
    #
    # Every spelling below names the same file, and each one must land in the *whole-solution*
    # branch rather than in the downstream "invalid JSON" report — the misleading diagnostic that
    # branch exists to prevent. The first four were covered from the start; the last four are the
    # ones a hand-written `^\./` strip let through (#1494 review, 微瑕 1: "`backend//Nerv.IIP.sln`
    # 或绝对路径拼法会绕过新分支、落回「JSON 非法」误报"), and they are why the comparison now
    # canonicalizes with GetFullPath instead of trimming one prefix.
    #
    # Both halves are asserted per spelling: the run must fail, AND it must fail with the
    # whole-solution finding rather than with "invalid JSON" — a failure-only assertion would be
    # green for all eight even with the branch deleted, because every spelling fails either way.
    $solutionSpelling = [string] (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).solution
    foreach ($wholeSolutionSpelling in @(
            $solutionSpelling,
            "./$solutionSpelling",
            ($solutionSpelling -replace '/', '\'),
            $solutionSpelling.ToLowerInvariant(),
            ($solutionSpelling -replace '/', '//'),
            ($solutionSpelling -replace '/', '/./'),
            ("$(Split-Path -Parent $solutionSpelling)/../$solutionSpelling"),
            ((Join-Path $repoRoot $solutionSpelling) -replace '\\', '/')
        )) {
        $wholeSolutionManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $wholeSolutionManifest.fastShards[0].solutionFilter = $wholeSolutionSpelling
        Set-Content -LiteralPath $temporaryManifestPath -Value ($wholeSolutionManifest | ConvertTo-Json -Depth 100) -NoNewline
        $wholeSolution = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-whole-solution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
        Assert-Contract (-not $wholeSolution.Passed) "A fast shard pointed at the whole backend solution ('$wholeSolutionSpelling') must fail shard governance."
        Assert-Contract ($wholeSolution.Message.Contains('must build its own solution filter, not the whole backend solution')) "Shard governance must reject a fast shard that rebuilds the entire backend solution, however '$wholeSolutionSpelling' is spelled."
        Assert-Contract (-not $wholeSolution.Message.Contains('solution filter is invalid JSON')) "'$wholeSolutionSpelling' must be diagnosed as the whole solution, not as a malformed solution filter."
    }

    $collisionSelector = 'Nerv.IIP.Testing.PostgreSql.Tests.PostgreSqlTestDatabaseTests.Parallel_databases_are_isolated_initialized_and_removed'
    $collisionMethod = $collisionSelector.Substring($collisionSelector.LastIndexOf('.') + 1)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $temporaryCollisionSourcePath) | Out-Null
    Set-Content -LiteralPath $temporaryCollisionSourcePath -NoNewline -Value "public sealed class Fixture { public void $collisionMethod() { } public void ${collisionMethod}Extra() { } }"
    $collisionPolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    $collisionSourceIds = @($collisionPolicy.rules | Where-Object { @($_.testIdentities) -ccontains $collisionSelector } | ForEach-Object { [string] $_.sourceId })
    foreach ($collisionSource in @($collisionPolicy.sources)) {
        if ($collisionSourceIds -contains [string] $collisionSource.id) {
            $collisionSource.sourcePath = $temporaryCollisionRelativePath
        }
    }
    Set-Content -LiteralPath $temporaryPolicyPath -Value ($collisionPolicy | ConvertTo-Json -Depth 100) -NoNewline
    $collision = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-selector-collision-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
    Assert-Contract (-not $collision.Passed) 'A method selector that substring-excludes a sibling member must fail shard governance.'
    Assert-Contract ($collision.Message.Contains('would also substring-exclude a sibling member')) 'Shard governance must reject a method selector that swallows a prefix-sharing sibling.'

    $timeoutText = ''
    $timedOut = $false
    $timeoutDiagnostics = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-Command', '[Console]::Out.WriteLine("partial-diagnostic Password=super-secret"); [Console]::Out.Flush(); Start-Sleep -Seconds 3') -WorkingDirectory $repoRoot -TimeoutSeconds 1 -Name 'backend-test-shard-timeout-contract' | Out-Null
    }
    catch {
        $timedOut = $true
        $timeoutText = $_.Exception.Message
        $timeoutDiagnostics = Get-BackendTestShardFailureDiagnostics -ErrorRecord $_ -TrxFilePrefix 'timeout-contract'
    }
    Assert-Contract ($timedOut -and -not [string]::IsNullOrWhiteSpace($timeoutText)) 'The bounded timeout diagnostic helper contract must time out.'
    Assert-Contract ($timeoutDiagnostics.Contains('partial-diagnostic')) 'The bounded timeout diagnostic helper contract must preserve buffered stdout content.'
    Assert-Contract (-not $timeoutDiagnostics.Contains('super-secret')) 'Buffered shard diagnostics must be redacted before they reach any retained log.'
    Assert-Contract (-not (Test-Path -LiteralPath $timeoutResultsDirectory)) 'Buffered shard diagnostics must stay in the job log instead of an uploaded results directory.'
}
finally {
    if (Test-Path -LiteralPath $temporaryProjectDirectory) {
        Remove-Item -LiteralPath $temporaryProjectDirectory -Recurse -Force
    }
    Remove-Item -LiteralPath $temporaryWorkflowPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $timeoutResultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $executionTrxDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryPolicyPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryManifestPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryCollisionSourcePath -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------------------------
# #1507 — timing is a report-only cache keyed by assembly; policy keys carry no lane/shard dimension.
#
# The failure being regression-tested is concrete: MAN-663 changed the shared host and MAN-669 PR-A
# re-homed assemblies between shards. Neither touched a test, yet both invalidated keys in a
# committed timing snapshot, and clearing that required a human to regenerate and re-commit it. The
# assertions below fix both halves of the cause — the measurement is no longer keyed on topology,
# and a gap in it can no longer turn anything red.
# ---------------------------------------------------------------------------------------------
. (Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1')

$balanceScript = Join-Path $repoRoot 'scripts/report-backend-test-shard-balance.ps1'
$timingUpdateScript = Join-Path $repoRoot 'scripts/update-backend-test-shard-timings.ps1'
Assert-Contract (Test-Path -LiteralPath $balanceScript -PathType Leaf) 'The report-only shard balance entry point is missing.'
Assert-Contract (Test-Path -LiteralPath $timingUpdateScript -PathType Leaf) 'The shard timing cache refresher is missing.'

# The policy gate and the timing report are deliberately separate programs, and this asserts the
# dependency direction between the two files. It is deliberately an **AST** judgement rather than a
# `Contains()` over the raw source: the previous spelling scanned raw text, so writing the words
# `test-evidence-baseline.json` in a *comment* inside the validator — explaining why it does not
# read that file, which is exactly the comment someone would write — turned this contract red over
# a sentence. Comments are not dependencies. What the AST checks instead is what a dependency
# actually looks like in PowerShell: dot-sourcing the timing library, calling one of the functions
# it defines, or naming a timing file in a string literal the script can act on.
#
# The behavioural half — a gap in timing data still exits 0 — is asserted below.
$validatorAst = [System.Management.Automation.Language.Parser]::ParseFile($validatorPath, [ref] $null, [ref] $null)
$timingLibraryPath = Join-Path $repoRoot 'scripts/lib/BackendTestShardTimings.ps1'
$timingLibraryAst = [System.Management.Automation.Language.Parser]::ParseFile($timingLibraryPath, [ref] $null, [ref] $null)
$timingFunctionNames = @(
    $timingLibraryAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) |
        ForEach-Object { [string] $_.Name } |
        Sort-Object -Unique
)
Assert-Contract ($timingFunctionNames.Count -gt 0) 'The timing library must define functions for the dependency-boundary assertion to have anything to look for.'

$validatorCommands = @($validatorAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))
foreach ($command in $validatorCommands) {
    $commandName = [string] $command.GetCommandName()
    Assert-Contract (-not ($timingFunctionNames -ccontains $commandName)) "The shard policy hard gate must not call the timing library function '$commandName'; timing lives in the report-only balance script."
    # A dot-source is a CommandAst whose invocation operator is `.`; its single argument is the path.
    if ($command.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Dot) { continue }
    $dotSourced = ($command.Extent.Text -replace '\\', '/')
    Assert-Contract (-not $dotSourced.Contains('BackendTestShardTimings')) 'The shard policy hard gate must not dot-source the timing library; timing lives in the report-only balance script.'
}

# String literals only — the parser hands back the *value* of a literal and never the text of a
# comment, so this is precise where the old raw-text scan was not.
$validatorLiterals = @(
    $validatorAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
    }, $true) | ForEach-Object { [string] $_.Extent.Text }
)
foreach ($timingToken in @('test-evidence-baseline.json', 'backend-test-shard-timings', 'elapsedMilliseconds')) {
    $offending = @($validatorLiterals | Where-Object { $_.Contains($timingToken) })
    Assert-Contract ($offending.Count -eq 0) "The shard policy hard gate must not name timing data ('$timingToken') in an evaluated string; timing lives in the report-only balance script."
}

$timingFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-shard-timing-fixture-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $timingFixtureRoot -Force | Out-Null
    $absentFallback = Join-Path $timingFixtureRoot 'no-such-snapshot.json'
    $snapshot = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-baseline.json') -Raw | ConvertFrom-Json
    $allObservations = @(
        foreach ($row in @(Get-NervShardTimingRowsFromEvidenceSummary -Summary $snapshot)) {
            [pscustomobject]@{ runId = 'fixture-run-1'; assembly = [string] $row.assembly; lane = [string] $row.lane; elapsedMilliseconds = [double] $row.elapsedMilliseconds }
        }
    )
    Assert-Contract ($allObservations.Count -gt 0) 'The timing fixture needs at least one observation to be meaningful.'

    # (a) Remove one assembly's timing data. The balance must degrade to a named report-only warning
    #     plus an estimate and exit 0 — never red.
    $businessCoreB = @($fastShards | Where-Object { [string] $_.id -ceq 'business-core-b' })[0]
    $droppedAssembly = Get-NervShardTimingAssemblyKey -Name ([string] @($businessCoreB.projects)[0])
    $reducedObservations = @($allObservations | Where-Object { [string] $_.assembly -cne $droppedAssembly })
    Assert-Contract ($reducedObservations.Count -lt $allObservations.Count) "The missing-timing fixture must actually remove '$droppedAssembly'."
    $reducedCachePath = Join-Path $timingFixtureRoot 'reduced-timings.json'
    Set-Content -LiteralPath $reducedCachePath -NoNewline -Value (
        (New-NervShardTimingCache -Observations $reducedObservations -Runs @([pscustomobject]@{ workflowRunId = 'fixture-run-1' })) | ConvertTo-Json -Depth 20
    )
    $reducedBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-missing-assembly-timing' -Arguments @(
        '-TimingCachePath', $reducedCachePath, '-FallbackEvidencePath', $absentFallback, '-NoRefresh'
    )
    Assert-Contract ($reducedBalance.Passed) 'A shard assembly with no timing observation must stay report-only and exit 0.'
    Assert-Contract ($reducedBalance.Message.Contains('timing-assembly-missing')) 'Missing timing data must be reported with its structured warning code.'
    Assert-Contract ($reducedBalance.Message.Contains($droppedAssembly)) 'The missing-timing warning must name the assembly it estimated.'
    Assert-Contract ($reducedBalance.Message.Contains('report-only')) 'The missing-timing warning must say it is report-only.'

    # No timing data at all — the offline / no-token / expired-artifact path — is also report-only.
    $emptyCachePath = Join-Path $timingFixtureRoot 'empty-timings.json'
    Set-Content -LiteralPath $emptyCachePath -NoNewline -Value (
        (New-NervShardTimingCache -Observations @() -Runs @()) | ConvertTo-Json -Depth 20
    )
    $emptyBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-no-timing-source' -Arguments @(
        '-TimingCachePath', $emptyCachePath, '-FallbackEvidencePath', $absentFallback, '-NoRefresh'
    )
    Assert-Contract ($emptyBalance.Passed) 'A completely unavailable timing source must still exit 0.'
    Assert-Contract ($emptyBalance.Message.Contains('timing-source-unavailable')) 'A completely unavailable timing source must be named, not silently estimated.'

    # The committed snapshot is the offline fallback. What is asserted here is that the fallback
    # *source* is selected and that the report it produces is structurally complete — one priced row
    # per fast shard plus the spread line.
    #
    # What is deliberately NOT asserted is that the snapshot covers every classified assembly. That
    # assertion existed, and it was the deleted red gate growing back in a wider form: the snapshot
    # is a committed file, so any *new backend test project* — a change that touches no timing code
    # and breaks nothing — has no row in it and produced a `timing-assembly-missing` warning, which
    # this contract then turned into a red Backend Test Shard Governance job until a human
    # regenerated and re-committed the snapshot. That is the exact human refresh ceremony #1507
    # deleted, re-imposed by a test, over a warning whose own text says "This is report-only".
    # docs/architecture/test-evidence-governance.md states the same rule in prose: coverage gaps are
    # report-only warnings, and the committed snapshot is never required to be complete.
    #
    # The gap count is printed instead of asserted, so a human reading the job log can see the
    # coverage drift that is worth knowing about and worthless as a gate.
    $fallbackBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-committed-fallback' -Arguments @(
        '-TimingCachePath', (Join-Path $timingFixtureRoot 'no-such-cache.json'),
        '-FallbackEvidencePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json'),
        '-NoRefresh'
    )
    Assert-Contract ($fallbackBalance.Passed) 'The balance report must fall back to the committed snapshot without failing.'
    Assert-Contract ($fallbackBalance.Message.Contains('committed-evidence-snapshot')) 'The balance report must name the fallback timing source it used.'
    foreach ($shard in $fastShards) {
        $shardRowPattern = "$([regex]::Escape([string] $shard.id)) [0-9,]+ ms over [0-9]+ assemblies \([0-9]+ measured, [0-9]+ estimated\) \[$([regex]::Escape([string] $shard.evidenceLane))\]"
        Assert-Contract ($fallbackBalance.Message -cmatch $shardRowPattern) "The committed-snapshot fallback report must price fast shard '$($shard.id)' with a measured/estimated split."
    }
    Assert-Contract ($fallbackBalance.Message -cmatch 'spread \(max-min\)/mean: [0-9.]+%') 'The committed-snapshot fallback report must still report the spread it was asked for.'
    $fallbackCoverageGaps = @([regex]::Matches($fallbackBalance.Message, 'timing-assembly-missing')).Count
    Write-Host "  [report-only] committed-snapshot fallback coverage gaps: $fallbackCoverageGaps"

    # The positive form of the same rule, which is what the deleted assertion should have been: a
    # backend test project the committed snapshot has never seen must be *balanced and reported*,
    # not punished. Nothing needs to exist on disk — the balance report prices whatever the manifest
    # classifies — so this is the cheapest possible stand-in for "someone added a test project".
    $newProjectManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $newProjectRelativePath = 'backend/tests/Nerv.IIP.BrandNew.Tests/Nerv.IIP.BrandNew.Tests.csproj'
    $newProjectAssembly = Get-NervShardTimingAssemblyKey -Name $newProjectRelativePath
    $newProjectShard = @($newProjectManifest.fastShards | Where-Object { [string] $_.id -ceq 'business-gateway' })[0]
    $newProjectShard.projects = @(@($newProjectShard.projects) + @($newProjectRelativePath))
    $newProjectManifestPath = Join-Path $timingFixtureRoot 'manifest-with-new-project.json'
    Set-Content -LiteralPath $newProjectManifestPath -NoNewline -Value ($newProjectManifest | ConvertTo-Json -Depth 100)
    $newProjectBalance = Invoke-GovernedScript -ScriptPath $balanceScript -Name 'shard-balance-new-test-project' -Arguments @(
        '-ManifestPath', $newProjectManifestPath,
        '-TimingCachePath', (Join-Path $timingFixtureRoot 'no-such-cache.json'),
        '-FallbackEvidencePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json'),
        '-NoRefresh'
    )
    Assert-Contract ($newProjectBalance.Passed) 'Adding a backend test project must not turn the shard balance report red; a coverage gap is report-only by construction.'
    Assert-Contract ($newProjectBalance.Message.Contains($newProjectAssembly)) 'A backend test project with no measurement must be named in a report-only warning rather than silently estimated.'
    Assert-Contract ($newProjectBalance.Message.Contains('This is report-only.')) 'The coverage-gap warning must say it is report-only.'

    # The aggregation口径 itself, offline: two runs' extracted evidence bundles in, one median per
    # assembly out. Also pins the two rules that are easy to get silently wrong — a bundle whose
    # collection failed carries diagnostics rather than measurements and must not become a sample,
    # and an assembly observed in two lanes of the *same* run is one sample of the summed work, not
    # two samples of half of it.
    $evidenceFixture = Join-Path $timingFixtureRoot 'evidence'
    $runOne = Join-Path $evidenceFixture 'run-1'
    $runTwo = Join-Path $evidenceFixture 'run-2'
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-a') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-b') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runOne 'lane-failed') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $runTwo 'lane-a') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $runOne 'lane-a/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-1'
        assemblies = @(
            @{ lane = 'backend-shard-1'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 40 },
            @{ lane = 'backend-shard-1'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 1000 }
        )
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runOne 'lane-b/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-2'
        assemblies = @(@{ lane = 'backend-shard-2'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 60 })
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runOne 'lane-failed/summary.json') -NoNewline -Value (@{
        collectionStatus = 'failed'; lane = 'backend-shard-3'
        assemblies = @(@{ lane = 'backend-shard-3'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 999999 })
    } | ConvertTo-Json -Depth 10)
    Set-Content -LiteralPath (Join-Path $runTwo 'lane-a/summary.json') -NoNewline -Value (@{
        collectionStatus = 'succeeded'; lane = 'backend-shard-1'
        assemblies = @(
            @{ lane = 'backend-shard-1'; assembly = 'Split.Tests.dll'; elapsedMilliseconds = 200 },
            @{ lane = 'backend-shard-1'; assembly = 'Solo.Tests.dll'; elapsedMilliseconds = 3000 }
        )
    } | ConvertTo-Json -Depth 10)

    $aggregated = @(Merge-NervShardTimingObservations -Observations @(
        @(Get-NervShardTimingObservationsFromEvidenceDirectory -Path $runOne -RunId 'run-1') +
        @(Get-NervShardTimingObservationsFromEvidenceDirectory -Path $runTwo -RunId 'run-2')
    ))
    $splitRow = @($aggregated | Where-Object { [string] $_.assembly -ceq 'split.tests.dll' })
    $soloRow = @($aggregated | Where-Object { [string] $_.assembly -ceq 'solo.tests.dll' })
    Assert-Contract ($splitRow.Count -eq 1 -and $soloRow.Count -eq 1) 'Aggregation must produce exactly one row per assembly across runs.'
    Assert-Contract ([double] $splitRow[0].elapsedMilliseconds -eq 150.0) "An assembly split across two lanes of one run must be summed first, then medianed; got $($splitRow[0].elapsedMilliseconds)."
    Assert-Contract ([int] $splitRow[0].observationCount -eq 2) 'Two lanes of one run must count as one observation, not two.'
    Assert-Contract ([double] $soloRow[0].elapsedMilliseconds -eq 2000.0) "Two runs must produce the median of the two values; got $($soloRow[0].elapsedMilliseconds)."
    Assert-Contract ([int] $soloRow[0].observationCount -eq 2) 'A failed-collection bundle must not become a third observation.'

    # (b) Simulate a shard rearrangement and prove the keys survive it.
    #
    # The fixture deliberately moves a project that *owns exclusions*, and moves its exclusion
    # selectors and derived `excludedTestLanes` with it. A rearrangement that only shuffles project
    # paths never reaches the one place in the policy gate where a shard id and a MAN-661 lane meet
    # — the `excludedTestLanes` derivation in verify-backend-test-shards.ps1 — so it could not
    # distinguish a lane-free policy key from a lane-coupled one, which is the entire claim under
    # test. Which project that is stays *derived* rather than pinned: pinning it to a shard id is
    # how the neighbouring lane-attribution fixture once went silently vacuous when MAN-669 PR-A
    # moved an exclusion to another shard.
    $evidencePolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    $policyLaneToHeavyLane = @{}
    foreach ($heavyLane in @($manifest.heavyLanes)) { $policyLaneToHeavyLane[[string] $heavyLane.policyLane] = [string] $heavyLane.id }

    function Get-ShardDerivedExcludedTestLanes {
        # The production derivation, restated only as a fixture helper: the heavy lanes a shard's
        # exclusions require. verify-backend-test-shards.ps1 computes the same thing and compares it
        # to what the shard declares, which is what makes a fixture that forgets to move
        # excludedTestLanes fail — see the negative control below.
        param(
            [Parameter(Mandatory)] [object] $Shard,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            [Parameter(Mandatory)] [hashtable] $PolicyLaneToHeavyLane
        )

        $lanes = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $Shard)) {
            foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($EvidencePolicy.rules))) {
                $policyLane = [string] $match.requiredLane
                if ($PolicyLaneToHeavyLane.ContainsKey($policyLane)) { [void] $lanes.Add([string] $PolicyLaneToHeavyLane[$policyLane]) }
            }
        }
        return @($lanes | Sort-Object)
    }

    $rearranged = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    # Donor: the fast shard that owns the one exclusion whose required lane is not the PostgreSQL
    # one, so the move actually changes both shards' derived excludedTestLanes rather than leaving
    # them identical by coincidence.
    $donorCandidates = @(
        foreach ($shard in @($rearranged.fastShards)) {
            $shardLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $shard -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
            if (@($shardLanes).Count -gt 1) { $shard }
        }
    )
    Assert-Contract ($donorCandidates.Count -eq 1) 'Exactly one fast shard must own exclusions from more than one heavy lane, otherwise the rearrangement fixture cannot move a lane between shards.'
    $donor = $donorCandidates[0]
    $receiver = @($rearranged.fastShards | Where-Object { -not [string]::Equals([string] $_.id, [string] $donor.id, [StringComparison]::Ordinal) })[0]

    # The moved project is the donor project whose assembly owns the multi-lane exclusion.
    $donorExtraLane = @(@(Get-ShardDerivedExcludedTestLanes -Shard $donor -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane) | Where-Object { -not [string]::Equals($_, 'real-postgres', [StringComparison]::Ordinal) })[0]
    $donorExtraLaneSelectors = @(
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $donor)) {
            foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($evidencePolicy.rules))) {
                if ([string]::Equals([string] $policyLaneToHeavyLane[[string] $match.requiredLane], $donorExtraLane, [StringComparison]::Ordinal)) { $selector }
            }
        }
    ) | Sort-Object -Unique
    Assert-Contract (@($donorExtraLaneSelectors).Count -gt 0) "The rearrangement fixture must find the donor selectors that require heavy lane '$donorExtraLane'."
    $movedProjects = @(
        foreach ($project in @($donor.projects)) {
            $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension([string] $project)
            foreach ($selector in @($donorExtraLaneSelectors)) {
                if (([string] $selector).StartsWith("$assemblyName.", [StringComparison]::Ordinal)) { [string] $project }
            }
        }
    ) | Sort-Object -Unique
    Assert-Contract (@($movedProjects).Count -eq 1) "The rearrangement fixture must resolve heavy lane '$donorExtraLane' to exactly one donor project; got $(@($movedProjects).Count)."
    $movedProject = [string] @($movedProjects)[0]
    $movedAssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($movedProject)
    $movedSelectors = @(Get-BackendTestShardExcludedSelectors -Shard $donor | Where-Object { ([string] $_).StartsWith("$movedAssemblyName.", [StringComparison]::Ordinal) })
    Assert-Contract (@($movedSelectors).Count -gt 0) 'The moved project must carry at least one exclusion selector, otherwise the policy half of this fixture is inert.'

    function Move-ShardExclusionSelectors {
        # Splits one shard's excludedTestClasses/excludedTests into "stays" and "moves", by the
        # assembly the selector belongs to. Both lists are optional properties, so both directions
        # have to tolerate an absent property rather than assume an empty array.
        param(
            [Parameter(Mandatory)] [object] $FromShard,
            [Parameter(Mandatory)] [object] $ToShard,
            [Parameter(Mandatory)] [string] $AssemblyName
        )

        foreach ($propertyName in @('excludedTestClasses', 'excludedTests')) {
            $fromProperty = $FromShard.PSObject.Properties[$propertyName]
            if ($null -eq $fromProperty) { continue }
            $all = @(@($fromProperty.Value) | ForEach-Object { [string] $_ })
            $moving = @($all | Where-Object { $_.StartsWith("$AssemblyName.", [StringComparison]::Ordinal) })
            if ($moving.Count -eq 0) { continue }
            $fromProperty.Value = @($all | Where-Object { -not $_.StartsWith("$AssemblyName.", [StringComparison]::Ordinal) })
            $toProperty = $ToShard.PSObject.Properties[$propertyName]
            if ($null -eq $toProperty) {
                $ToShard | Add-Member -NotePropertyName $propertyName -NotePropertyValue (@($moving) | Sort-Object -Unique) -Force
            }
            else {
                $toProperty.Value = @(@(@($toProperty.Value) | ForEach-Object { [string] $_ }) + $moving) | Sort-Object -Unique
            }
        }
    }

    $donor.projects = @(@($donor.projects) | Where-Object { -not [string]::Equals([string] $_, $movedProject, [StringComparison]::Ordinal) })
    $receiver.projects = @(@($receiver.projects) + @($movedProject)) | Sort-Object -Unique
    Move-ShardExclusionSelectors -FromShard $donor -ToShard $receiver -AssemblyName $movedAssemblyName
    $donor.excludedTestLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $donor -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
    $receiver.excludedTestLanes = @(Get-ShardDerivedExcludedTestLanes -Shard $receiver -EvidencePolicy $evidencePolicy -PolicyLaneToHeavyLane $policyLaneToHeavyLane)
    Assert-Contract (@($receiver.projects) -contains $movedProject -and -not (@($donor.projects) -contains $movedProject)) 'The rearrangement fixture must actually move a project between shards.'
    Assert-Contract ((@($receiver.excludedTestLanes) -contains $donorExtraLane) -and -not (@($donor.excludedTestLanes) -contains $donorExtraLane)) "The rearrangement fixture must move heavy lane '$donorExtraLane' with the project that requires it, otherwise it never reaches the excludedTestLanes coupling point."

    $fullTimings = Get-NervShardTimingLookup -CachePath (Join-Path $timingFixtureRoot 'no-such-cache.json') -FallbackEvidencePath (Join-Path $repoRoot 'scripts/test-evidence-baseline.json')
    foreach ($case in @(
            @{ Name = 'original'; Manifest = ($manifest) },
            @{ Name = 'rearranged'; Manifest = $rearranged }
        )) {
        $report = Get-NervShardBalanceReport -Manifest $case.Manifest -Timings $fullTimings
        $lost = @($report.warnings | Where-Object { [string] $_.code -ceq 'timing-assembly-missing' })
        Assert-Contract ($lost.Count -eq 0) "Shard layout '$($case.Name)' must resolve every timing key; lost $($lost.Count)."
    }

    # Control: the *old* lane+assembly key would have lost keys on exactly this rearrangement. Without
    # this the assertion above would still pass if timing lookup were reduced to a no-op, and it is
    # what goes red if anyone puts the lane back into the key.
    $laneKeyedSnapshot = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($row in @($snapshot.assemblies)) {
        [void] $laneKeyedSnapshot.Add("$([string] $row.lane)|$(Get-NervShardTimingAssemblyKey -Name ([string] $row.assembly))")
    }
    $laneKeyedLost = @(
        foreach ($shard in @($rearranged.fastShards)) {
            foreach ($project in @($shard.projects)) {
                $laneKey = "$([string] $shard.evidenceLane)|$(Get-NervShardTimingAssemblyKey -Name ([string] $project))"
                if (-not $laneKeyedSnapshot.Contains($laneKey)) { $laneKey }
            }
        }
    )
    Assert-Contract ($laneKeyedLost.Count -gt 0) 'The rearrangement fixture must be one that the old lane+assembly key would have failed on, otherwise the assembly-keyed assertion is vacuous.'

    # Policy keys must be identical across the rearrangement. This is the "政策门禁零失键" acceptance:
    # every fast-shard exclusion still resolves to exactly the same MAN-661 source/rule/test
    # identities. The key derivation is the **production** one — Get-BackendTestShardPolicyIdentity*
    # from scripts/lib/BackendTestShardSelectors.ps1, the same pair verify-backend-test-shards.ps1
    # runs. A key set rebuilt inside this file would have asserted its own arithmetic and stayed
    # green even with the lane put back into the production key.
    function Get-ShardPolicyKeySet {
        param(
            [Parameter(Mandatory)] [object] $ShardManifest,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            # Control switch. When set, the shard's evidence lane is spliced into each key — i.e.
            # what the key would look like if policy were coupled to the shard topology the way
            # timing used to be. Nothing in production takes this path; it exists so the assertion
            # below has something that demonstrably *does* break.
            [switch] $KeyOnLane
        )

        $keys = [System.Collections.Generic.List[string]]::new()
        foreach ($shard in @($ShardManifest.fastShards)) {
            foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
                foreach ($match in @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($EvidencePolicy.rules))) {
                    $key = Get-BackendTestShardPolicyIdentityKey -Match $match
                    if ($KeyOnLane) { $key = "$([string] $shard.evidenceLane)|$key" }
                    [void] $keys.Add($key)
                }
            }
        }

        return @($keys | Sort-Object -Unique)
    }

    $policyKeysBefore = @(Get-ShardPolicyKeySet -ShardManifest $manifest -EvidencePolicy $evidencePolicy)
    $policyKeysAfter = @(Get-ShardPolicyKeySet -ShardManifest $rearranged -EvidencePolicy $evidencePolicy)
    Assert-Contract ($policyKeysBefore.Count -gt 0) 'The policy key set must be non-empty, otherwise its stability is vacuous.'
    Assert-Contract ((($policyKeysBefore -join "`n") -ceq ($policyKeysAfter -join "`n"))) 'A shard rearrangement must not change a single MAN-661 policy key.'

    # Control, and the reason the assertion above is not a tautology: run the *same* derivation with
    # the lane spliced back into the key and the same rearrangement does lose keys. Without this, a
    # key set that happened to be topology-invariant for an unrelated reason — or a rearrangement
    # too weak to move anything a key could see — would read as a passing contract.
    $laneKeyedPolicyBefore = @(Get-ShardPolicyKeySet -ShardManifest $manifest -EvidencePolicy $evidencePolicy -KeyOnLane)
    $laneKeyedPolicyAfter = [System.Collections.Generic.HashSet[string]]::new([string[]] @(Get-ShardPolicyKeySet -ShardManifest $rearranged -EvidencePolicy $evidencePolicy -KeyOnLane))
    $laneKeyedPolicyLost = @($laneKeyedPolicyBefore | Where-Object { -not $laneKeyedPolicyAfter.Contains([string] $_) })
    Assert-Contract ($laneKeyedPolicyLost.Count -gt 0) 'The rearrangement must be one a lane-coupled policy key would have failed on, otherwise the lane-free assertion above proves nothing.'

    # The gate itself, over the rearranged topology. The assertions above compare derivations; this
    # runs the real policy gate as a process and requires it to be satisfied by a shard layout it has
    # never seen, including the excludedTestLanes derivation that is the only place a shard id meets
    # a MAN-661 lane. Solution filters are regenerated for the two touched shards because the gate
    # also requires filter and manifest to agree project-for-project, and a filter mismatch would
    # fail the run for a reason that has nothing to do with policy keys.
    function New-ShardFixtureSolutionFilter {
        param(
            [Parameter(Mandatory)] [object] $Shard,
            [Parameter(Mandatory)] [string] $Directory,
            [Parameter(Mandatory)] [string] $RepositoryRelativeDirectory
        )

        # Project entries in a .slnf are relative to the solution the filter points at, which is
        # backend/Nerv.IIP.sln, so a manifest path is the same string minus its `backend/` prefix.
        $projects = @(@($Shard.projects) | ForEach-Object { ([string] $_) -replace '^backend/', '' } | Sort-Object -Unique)
        $fileName = "shard-fixture-{0}-{1}.slnf" -f ([string] $Shard.id), ([Guid]::NewGuid().ToString('N'))
        $filterPath = Join-Path $Directory $fileName
        Set-Content -LiteralPath $filterPath -NoNewline -Value ([pscustomobject]@{
            solution = [pscustomobject]@{ path = '../backend/Nerv.IIP.sln'; projects = $projects }
        } | ConvertTo-Json -Depth 10)
        return "$RepositoryRelativeDirectory/$fileName"
    }

    $fixtureFilterDirectory = Join-Path $repoRoot 'artifacts'
    New-Item -ItemType Directory -Path $fixtureFilterDirectory -Force | Out-Null
    $fixtureFilterPaths = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($shard in @($donor, $receiver)) {
            $relativeFilter = New-ShardFixtureSolutionFilter -Shard $shard -Directory $fixtureFilterDirectory -RepositoryRelativeDirectory 'artifacts'
            [void] $fixtureFilterPaths.Add((Join-Path $repoRoot $relativeFilter))
            $shard.solutionFilter = $relativeFilter
        }

        $rearrangedManifestPath = Join-Path $timingFixtureRoot 'rearranged-manifest.json'
        Set-Content -LiteralPath $rearrangedManifestPath -NoNewline -Value ($rearranged | ConvertTo-Json -Depth 100)
        $rearrangedGate = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-rearranged-policy-gate' -Arguments @('-ManifestPath', $rearrangedManifestPath)
        Assert-Contract ($rearrangedGate.Passed) "A shard rearrangement that moves a project with its exclusions must lose zero policy keys and satisfy the policy gate unchanged; the gate reported: $($rearrangedGate.Message)"

        # Negative control for the fixture, not for the product: with excludedTestLanes left behind,
        # the same rearrangement must be rejected at exactly the coupling point. This is what proves
        # the fixture reaches that rule instead of passing it by.
        $underDeclaredManifest = Get-Content -LiteralPath $rearrangedManifestPath -Raw | ConvertFrom-Json
        $underDeclaredReceiver = @($underDeclaredManifest.fastShards | Where-Object { [string]::Equals([string] $_.id, [string] $receiver.id, [StringComparison]::Ordinal) })[0]
        $underDeclaredReceiver.excludedTestLanes = @(@($underDeclaredReceiver.excludedTestLanes) | Where-Object { -not [string]::Equals([string] $_, $donorExtraLane, [StringComparison]::Ordinal) })
        $underDeclaredManifestPath = Join-Path $timingFixtureRoot 'rearranged-manifest-under-declared.json'
        Set-Content -LiteralPath $underDeclaredManifestPath -NoNewline -Value ($underDeclaredManifest | ConvertTo-Json -Depth 100)
        $underDeclaredGate = Invoke-GovernedScript -ScriptPath $validatorPath -Name 'backend-test-shard-rearranged-under-declared-lane' -Arguments @('-ManifestPath', $underDeclaredManifestPath)
        Assert-Contract (-not $underDeclaredGate.Passed) 'The rearrangement fixture must actually exercise the excludedTestLanes derivation; a shard that keeps a moved exclusion lane must fail.'
        Assert-Contract ($underDeclaredGate.Message.Contains('must declare excludedTestLanes')) 'The under-declared control must fail at the excludedTestLanes coupling point, not somewhere else.'
    }
    finally {
        foreach ($fixtureFilterPath in $fixtureFilterPaths) { Remove-Item -LiteralPath $fixtureFilterPath -Force -ErrorAction SilentlyContinue }
    }

    # The three semantic hard gates are derived from policy plus runtime records, and take no shard
    # manifest at all — so re-homing a project can only reach them through the *lane* a shard
    # certifies. Evaluated with the production engine (Get-NervTestEvidenceViolations), under the
    # donor's lane and the receiver's lane, over the same synthetic runtime skip for each moved
    # identity: the verdict per test identity must be byte-identical.
    $movedIdentities = @(
        foreach ($selector in @($movedSelectors)) {
            Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules @($evidencePolicy.rules)
        }
    )
    Assert-Contract (@($movedIdentities).Count -gt 0) 'The moved exclusions must resolve to policy identities for the hard-gate comparison to have inputs.'

    function Get-HardGateVerdicts {
        param(
            [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Matches,
            [Parameter(Mandatory)] [object] $EvidencePolicy,
            [Parameter(Mandatory)] [string] $Lane
        )

        $records = @(
            foreach ($match in @($Matches)) {
                $rule = @($EvidencePolicy.rules | Where-Object { [string]::Equals([string] $_.id, [string] $match.ruleId, [StringComparison]::Ordinal) })[0]
                # The reason patterns registered for these rules are fully anchored literals, so the
                # literal they accept is the pattern with its anchors and escapes removed. Asserted
                # rather than assumed, so a future non-literal pattern fails loudly here instead of
                # silently producing a record that matches nothing.
                $reason = (([string] $rule.reasonPattern) -replace '^\^', '' -replace '\$$', '') -replace '\\(.)', '$1'
                Assert-Contract ($reason -cmatch [string] $rule.reasonPattern) "The hard-gate fixture must synthesize a skip reason that rule '$($rule.id)' actually accepts."
                [pscustomobject]@{
                    lane = $Lane
                    testName = [string] $match.identity
                    outcome = 'skipped'
                    skipReason = $reason
                }
            }
        )

        $violations = @(Get-NervTestEvidenceViolations -Records $records -Policy $EvidencePolicy -SelectedLanes @($Lane) -RunnerOs 'Linux')
        return @($violations | ForEach-Object { "$([string] $_.code)|$([string] $_.id)" } | Sort-Object)
    }

    $donorLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane ([string] $donor.evidenceLane))
    $receiverLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane ([string] $receiver.evidenceLane))
    Assert-Contract ((($donorLaneVerdicts -join "`n") -ceq ($receiverLaneVerdicts -join "`n"))) "Moving a project between shards must not change a single unregistered-skip / illegal-quarantine / zero-execution verdict; donor lane reported [$($donorLaneVerdicts -join ', ')] and receiver lane [$($receiverLaneVerdicts -join ', ')]."
    Assert-Contract (@($donorLaneVerdicts | Where-Object { $_.StartsWith('unregistered-skip|', [StringComparison]::Ordinal) }).Count -eq 0) 'A registered skip must stay registered under the shard lane that owns it.'

    # Control: both verdict sets above are legitimately *empty* — a registered skip in an allowed
    # lane is not a violation — and two empty sets compare equal no matter what the engine does.
    # This proves the fixture is live: the same records under a lane the rules do not allow do
    # produce `unregistered-skip`, so "identical and empty" is a result rather than a dead input.
    $foreignLaneVerdicts = @(Get-HardGateVerdicts -Matches $movedIdentities -EvidencePolicy $evidencePolicy -Lane 'connector-host')
    Assert-Contract (@($foreignLaneVerdicts | Where-Object { $_.StartsWith('unregistered-skip|', [StringComparison]::Ordinal) }).Count -gt 0) 'The hard-gate fixture must be able to produce a violation at all, otherwise the equal-verdicts assertion above compares two empty sets for free.'

    # The same statement about the policy file itself: a rule matches on test identity and reason, and
    # its lane fields are logical lanes. A shard-suffixed lane in a rule would re-couple the two.
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($laneValue in @(@($rule.allowedLanes) + @([string] $rule.requiredLane))) {
            Assert-Contract (-not ([string] $laneValue -cmatch '-shard-[0-9]')) "Evidence policy rule '$($rule.id)' must key on a logical lane, never on a shard: '$laneValue'."
        }
        Assert-Contract (@($rule.testIdentities).Count -gt 0 -or [string] $rule.classification -ceq 'quarantined') "Evidence policy rule '$($rule.id)' must key on explicit test identities."
    }

    # Lane is a rule's *applicability condition*, never part of its identity key, and the two are
    # easy to confuse because `Test-NervRuleApplies` does read `allowedLanes`/`requiredLane`. What it
    # reads is the **logical** lane: it strips any `-shard-N` suffix before matching, so the shard
    # dimension is gone by the time any comparison happens. That is what keeps the third hard gate
    # ("a selected real-dependency lane executed nothing") meaningful while leaving a re-shard unable
    # to change a verdict. Asserted rather than described: every rule must decide identically for a
    # logical lane and for every shard spelling of it.
    # Narrative: docs/architecture/test-evidence-governance.md, "Timing data is a cache, not a
    # governed asset" (lane as applicability condition versus identity key).
    $logicalLanesUnderTest = @('backend', 'connector-host', 'postgres', 'full-chain', 'performance')
    $laneSuffixCases = 0
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($runnerOs in @('Linux', 'Windows')) {
            foreach ($logicalLane in $logicalLanesUnderTest) {
                $logicalVerdict = [bool] (Test-NervRuleApplies -Rule $rule -SelectedLanes @($logicalLane) -RunnerOs $runnerOs)
                foreach ($shardIndex in @(1, 2, 3, 4, 17)) {
                    $shardedVerdict = [bool] (Test-NervRuleApplies -Rule $rule -SelectedLanes @("$logicalLane-shard-$shardIndex") -RunnerOs $runnerOs)
                    Assert-Contract ($shardedVerdict -eq $logicalVerdict) "Rule '$($rule.id)' must decide identically for logical lane '$logicalLane' and its shard spelling '$logicalLane-shard-$shardIndex' on $runnerOs; got $logicalVerdict vs $shardedVerdict."
                    $laneSuffixCases++
                }
            }
        }
    }
    Assert-Contract ($laneSuffixCases -gt 0) 'The lane-suffix independence contract must actually evaluate rules.'

    # The refresher's degradation path: with a `gh` that fails, the cache is simply not refreshed and
    # the entry point still exits 0. Asserted with a stub on PATH rather than by calling GitHub, so
    # the case is deterministic and offline. Restricted to the platforms the Backend Test Shard
    # Governance job and local development actually run on: on Windows a PATH stub has to satisfy
    # PATHEXT resolution through `Process.Start`, which is a different mechanism and would make this
    # a test of the stub rather than of the degradation.
    if (-not $IsWindows) {
        $stubBin = Join-Path $timingFixtureRoot 'stub-bin'
        New-Item -ItemType Directory -Path $stubBin -Force | Out-Null
        $stubGh = Join-Path $stubBin 'gh'
        Set-Content -LiteralPath $stubGh -NoNewline -Value "#!/bin/sh`necho 'stub gh: unavailable' 1>&2`nexit 1`n"
        Invoke-NativeCommandOutput -Command 'chmod' -Arguments @('+x', $stubGh) -WorkingDirectory $repoRoot -Name 'shard-timings-stub-chmod' | Out-Null

        $degradedCachePath = Join-Path $timingFixtureRoot 'degraded-timings.json'
        $originalPath = $env:PATH
        $degraded = $null
        try {
            $env:PATH = "$stubBin$([IO.Path]::PathSeparator)$originalPath"
            $degraded = Invoke-GovernedScript -ScriptPath $timingUpdateScript -Name 'shard-timings-degraded-refresh' -Arguments @('-OutputPath', $degradedCachePath)
        }
        finally {
            $env:PATH = $originalPath
        }

        Assert-Contract ($degraded.Passed) 'An unavailable GitHub CLI must leave the timing refresher at exit 0; a timing cache miss is not a repository defect.'
        Assert-Contract ($degraded.Message.Contains('was not refreshed')) 'The timing refresher must say it did not refresh instead of pretending it did.'
        Assert-Contract (-not (Test-Path -LiteralPath $degradedCachePath)) 'A failed refresh must not write a cache file at all, so the previous cache stays authoritative.'
    }

    # Determinism debt rows are keyed on source path + pattern + line hash. No lane, no shard.
    $determinismBaseline = Get-Content -LiteralPath (Join-Path $repoRoot 'backend/test-determinism-baseline.json') -Raw | ConvertFrom-Json
    Assert-Contract (@($determinismBaseline.exceptions).Count -gt 0) 'The determinism baseline must carry rows for its key shape to be assertable.'
    foreach ($exception in @($determinismBaseline.exceptions)) {
        foreach ($property in @($exception.PSObject.Properties.Name)) {
            Assert-Contract ($property -cnotmatch '(?i)lane|shard') "Determinism debt row '$($exception.path)' must not carry a lane or shard dimension: '$property'."
        }
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string] $exception.path)) 'Every determinism debt row must key on a source path.'
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string] $exception.lineTextSha256)) 'Every determinism debt row must key on its line hash.'
    }
}
finally {
    Remove-Item -LiteralPath $timingFixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
Assert-Contract (-not (Test-Path -LiteralPath $timingFixtureRoot)) 'The shard timing fixtures must be cleaned up.'

Write-Host 'Backend test shard manifest contract tests passed.'
