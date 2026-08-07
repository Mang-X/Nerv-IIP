# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes one temporary unclassified backend test project
#     - Creates and removes one temporary backend project that is not a solution member
#   Writes:
#     - backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/** (temporarily)
#     - backend/common/Nerv.IIP.TemporarySolutionMembership/** (temporarily)
#     - OS temporary directory: workflow, manifest, policy and shard TRX fixtures (temporarily)
#     - artifacts/backend-test-shards-collision-*.cs selector-collision fixture (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes every temporary project, workflow, manifest, policy, TRX and collision fixture in finally
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

Write-Host 'Backend test shard manifest contract tests passed.'
