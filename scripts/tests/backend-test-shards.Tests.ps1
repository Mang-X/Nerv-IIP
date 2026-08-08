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
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every temporary project, workflow, manifest, policy, TRX, timing-cache and collision fixture in finally
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

# Every assertion below is about *what the validator said*, so the validator is always run as a real
# process and judged by its exit code plus its output.
#
# The validator reports findings on stdout and exits 1 rather than throwing, which is what lets
# these assertions match whole sentences instead of the short fragments a thrown (and therefore
# width-wrapped) message forced — this file used to also scrape the command log to reassemble that
# text, and both workarounds are gone. Why the shape matters:
# docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 3 条).
#
# Whitespace is collapsed so that where the validator chose to break lines is not part of the
# contract. The assertions are about content, not layout.
function Invoke-ShardValidator {
    param(
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $validatorPath) + $Arguments) `
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

    $solutionMembership = Invoke-ShardValidator -Name 'backend-test-shard-solution-membership'
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

    $unclassified = Invoke-ShardValidator -Name 'backend-test-shard-unclassified-project'
    Assert-Contract (-not $unclassified.Passed) 'An unclassified temporary backend test project must fail classification.'
    Assert-Contract ($unclassified.Message.Contains('Unclassified backend test')) 'Unclassified project failure must identify the classification error.'
    Assert-Contract ($unclassified.Message.Contains('backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/Nerv.IIP.TemporaryShardClassification.Tests.csproj')) 'Unclassified project failure must identify the temporary project path.'

    $workflowContent = Get-Content -LiteralPath $workflowPath -Raw
    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^\s+- backend-tests-business-core-b\r?\n', '') -NoNewline
    $workflowValidation = Invoke-ShardValidator -Name 'backend-test-shard-workflow-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $workflowValidation.Passed) 'A workflow with a missing aggregate dependency must fail structured shard governance.'
    Assert-Contract ($workflowValidation.Message.Contains('Backend Tests aggregate must need exactly the governance and four fast shard jobs.')) 'Structured workflow validation must reject an aggregate with a missing shard dependency.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'echo "${{ needs.backend-tests-platform.result }}"') -NoNewline
    $noOpValidation = Invoke-ShardValidator -Name 'backend-test-shard-noop-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $noOpValidation.Passed) 'A no-op aggregate dependency expression must fail structured shard governance.'
    Assert-Contract ($noOpValidation.Message.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a non-failing aggregate dependency expression.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'test "${{ needs.backend-tests-platform.result }}" = "success" || true') -NoNewline
    $maskedFailureValidation = Invoke-ShardValidator -Name 'backend-test-shard-masked-aggregate-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $maskedFailureValidation.Passed) 'An aggregate assertion masked with || true must fail structured shard governance.'
    Assert-Contract ($maskedFailureValidation.Message.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a masked aggregate dependency assertion.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(\s+- name: Require all backend fast shards\r?\n)', ('$1        continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $continueOnErrorValidation = Invoke-ShardValidator -Name 'backend-test-shard-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $continueOnErrorValidation.Passed) 'An aggregate step with continue-on-error must fail structured shard governance.'
    Assert-Contract ($continueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")) 'Structured workflow validation must reject an aggregate continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^(    if: always\(\)\r?\n)', ('$1    continue-on-error: true' + [Environment]::NewLine)) -NoNewline
    $jobContinueOnErrorValidation = Invoke-ShardValidator -Name 'backend-test-shard-job-continue-on-error-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
    Assert-Contract (-not $jobContinueOnErrorValidation.Passed) 'An aggregate job with continue-on-error must fail structured shard governance.'
    Assert-Contract ($jobContinueOnErrorValidation.Message.Contains("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")) 'Structured workflow validation must reject an aggregate job continue-on-error configuration.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)(-TrxFilePrefix backend-tests-platform)', '$1 -TestCommand "Write-Output pass"') -NoNewline
    $bypassValidation = Invoke-ShardValidator -Name 'backend-test-shard-command-bypass-contract' -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
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
        $evidenceValidation = Invoke-ShardValidator -Name "backend-test-shard-evidence-$($evidenceMutation.Name)-contract" -Arguments @('-WorkflowPath', $temporaryWorkflowPath)
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
    $policyCoverage = Invoke-ShardValidator -Name 'backend-test-shard-policy-coverage-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
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
    $laneAttribution = Invoke-ShardValidator -Name 'backend-test-shard-lane-attribution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
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
        $wholeSolution = Invoke-ShardValidator -Name 'backend-test-shard-whole-solution-contract' -Arguments @('-ManifestPath', $temporaryManifestPath)
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
    $collision = Invoke-ShardValidator -Name 'backend-test-shard-selector-collision-contract' -Arguments @('-PolicyPath', $temporaryPolicyPath)
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

# The policy gate and the timing report are deliberately separate programs. This is a boundary
# assertion — which file may depend on which — and source text is the only place a dependency
# boundary is visible; the behavioural half (a gap in timing data exits 0) is asserted below.
$validatorSource = Get-Content -LiteralPath $validatorPath -Raw
foreach ($timingToken in @('BackendTestShardTimings', 'test-evidence-baseline.json', 'elapsedMilliseconds', 'backend-test-shard-timings')) {
    Assert-Contract (-not $validatorSource.Contains($timingToken)) "The shard policy hard gate must not consume timing data ('$timingToken'); timing lives in the report-only balance script."
}

function Invoke-ShardBalance {
    param(
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [string] $Name
    )

    try {
        $result = Invoke-NativeCommandOutput `
            -Command 'pwsh' `
            -Arguments (@('-NoProfile', '-File', $balanceScript) + $Arguments) `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 300 `
            -Name $Name
        return [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
    }
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
    $reducedBalance = Invoke-ShardBalance -Name 'shard-balance-missing-assembly-timing' -Arguments @(
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
    $emptyBalance = Invoke-ShardBalance -Name 'shard-balance-no-timing-source' -Arguments @(
        '-TimingCachePath', $emptyCachePath, '-FallbackEvidencePath', $absentFallback, '-NoRefresh'
    )
    Assert-Contract ($emptyBalance.Passed) 'A completely unavailable timing source must still exit 0.'
    Assert-Contract ($emptyBalance.Message.Contains('timing-source-unavailable')) 'A completely unavailable timing source must be named, not silently estimated.'

    # The committed snapshot is the offline fallback; with it, the real repository state reports with
    # no missing-assembly warning at all, which is what "the 17/64 lost keys are gone" looks like.
    $fallbackBalance = Invoke-ShardBalance -Name 'shard-balance-committed-fallback' -Arguments @(
        '-TimingCachePath', (Join-Path $timingFixtureRoot 'no-such-cache.json'),
        '-FallbackEvidencePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json'),
        '-NoRefresh'
    )
    Assert-Contract ($fallbackBalance.Passed) 'The balance report must fall back to the committed snapshot without failing.'
    Assert-Contract ($fallbackBalance.Message.Contains('committed-evidence-snapshot')) 'The balance report must name the fallback timing source it used.'
    Assert-Contract (-not $fallbackBalance.Message.Contains('timing-assembly-missing')) 'Every currently classified backend assembly must resolve a timing key from the committed snapshot.'

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
    $rearranged = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $donor = @($rearranged.fastShards | Where-Object { [string] $_.id -ceq 'business-core-a' })[0]
    $receiver = @($rearranged.fastShards | Where-Object { [string] $_.id -ceq 'business-core-b' })[0]
    $movedProject = [string] @($donor.projects)[0]
    $donor.projects = @(@($donor.projects) | Where-Object { [string] $_ -cne $movedProject })
    $receiver.projects = @(@($receiver.projects) + @($movedProject))
    Assert-Contract (@($receiver.projects) -contains $movedProject -and -not (@($donor.projects) -contains $movedProject)) 'The rearrangement fixture must actually move a project between shards.'

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
    # every fast-shard exclusion still resolves to exactly the same MAN-661 source/rule/test identities.
    $evidencePolicy = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json
    function Get-ShardPolicyKeySet {
        param(
            [Parameter(Mandatory)] [object] $ShardManifest,
            [Parameter(Mandatory)] [object] $EvidencePolicy
        )

        $keys = [System.Collections.Generic.List[string]]::new()
        foreach ($shard in @($ShardManifest.fastShards)) {
            foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
                foreach ($rule in @($EvidencePolicy.rules)) {
                    foreach ($identity in @($rule.testIdentities)) {
                        if ([string] $identity -ceq $selector -or ([string] $identity).StartsWith("$selector.", [StringComparison]::Ordinal)) {
                            [void] $keys.Add("$([string] $rule.sourceId)|$([string] $rule.id)|$identity")
                        }
                    }
                }
            }
        }

        return @($keys | Sort-Object -Unique)
    }

    $policyKeysBefore = @(Get-ShardPolicyKeySet -ShardManifest $manifest -EvidencePolicy $evidencePolicy)
    $policyKeysAfter = @(Get-ShardPolicyKeySet -ShardManifest $rearranged -EvidencePolicy $evidencePolicy)
    Assert-Contract ($policyKeysBefore.Count -gt 0) 'The policy key set must be non-empty, otherwise its stability is vacuous.'
    Assert-Contract ((($policyKeysBefore -join "`n") -ceq ($policyKeysAfter -join "`n"))) 'A shard rearrangement must not change a single MAN-661 policy key.'

    # The same statement about the policy file itself: a rule matches on test identity and reason, and
    # its lane fields are logical lanes. A shard-suffixed lane in a rule would re-couple the two.
    foreach ($rule in @($evidencePolicy.rules)) {
        foreach ($laneValue in @(@($rule.allowedLanes) + @([string] $rule.requiredLane))) {
            Assert-Contract (-not ([string] $laneValue -cmatch '-shard-[0-9]')) "Evidence policy rule '$($rule.id)' must key on a logical lane, never on a shard: '$laneValue'."
        }
        Assert-Contract (@($rule.testIdentities).Count -gt 0 -or [string] $rule.classification -ceq 'quarantined') "Evidence policy rule '$($rule.id)' must key on explicit test identities."
    }

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
            try {
                $result = Invoke-NativeCommandOutput `
                    -Command 'pwsh' `
                    -Arguments @('-NoProfile', '-File', $timingUpdateScript, '-OutputPath', $degradedCachePath) `
                    -WorkingDirectory $repoRoot `
                    -TimeoutSeconds 300 `
                    -Name 'shard-timings-degraded-refresh'
                $degraded = [pscustomobject]@{ Passed = $true; Message = ("$($result.Stdout)" -replace '\s+', ' ') }
            }
            catch {
                $degraded = [pscustomobject]@{ Passed = $false; Message = ("$($_.Exception.Message)" -replace '\s+', ' ') }
            }
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
