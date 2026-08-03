# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes one temporary unclassified backend test project
#   Writes:
#     - backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/** (temporarily)
#     - OS temporary directory: one workflow fixture and one timeout-results directory (temporarily)
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes the temporary test project, workflow fixture, and timeout-results directory in finally
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
$temporaryWorkflowPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-{0}.yml" -f [Guid]::NewGuid().ToString('N'))
$timeoutResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-timeout-{0}" -f [Guid]::NewGuid().ToString('N'))
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
$businessCoreBShard = @($fastShards | Where-Object { $_.id -eq 'business-core-b' })
Assert-Contract ($businessGatewayShard.Count -eq 1 -and @($businessGatewayShard[0].projects).Count -eq 1) 'BusinessGateway must stay isolated in its own fast shard before MAN-663.'
Assert-Contract ($businessCoreBShard.Count -eq 1 -and @($businessCoreBShard[0].projects) -contains 'backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj') 'Regular business acceptance facts must be part of the default fast gate.'
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

$classifiedProjects = @($fastShards.projects) + @($heavyLanes.projects)
Assert-Contract (($classifiedProjects | Sort-Object -Unique).Count -eq $classifiedProjects.Count) 'Every backend test project must be classified exactly once.'
Assert-Contract ($classifiedProjects.Count -eq 64) 'The checked-in backend test inventory must contain 64 classified projects.'

foreach ($shard in $fastShards) {
    $filterPath = Join-Path $repoRoot $shard.solutionFilter
    $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
    Assert-Contract ($filter.solution.path -eq '../Nerv.IIP.sln') "Solution filter $($shard.solutionFilter) must target the backend solution."
    Assert-Contract ((@($filter.solution.projects | Where-Object { $_ -match '^\.\./' })).Count -eq 0) "Solution filter $($shard.solutionFilter) project paths must be relative to backend/Nerv.IIP.sln."
}

try {
    New-Item -ItemType Directory -Path $temporaryProjectDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporaryProjectPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $validatorText = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $validatorPath) -WorkingDirectory $repoRoot -Name 'backend-test-shard-unclassified-project' | Out-Null
        throw 'An unclassified temporary backend test project must fail classification.'
    }
    catch {
        $validatorText = $_.Exception.Message
        $logMatch = [regex]::Match($validatorText, 'Logs: (?<path>.+)$')
        if ($logMatch.Success) {
            foreach ($logName in @('stdout.log', 'stderr.log')) {
                $logPath = Join-Path $logMatch.Groups['path'].Value $logName
                if (Test-Path -LiteralPath $logPath) {
                    $validatorText += "`n" + (Get-Content -LiteralPath $logPath -Raw)
                }
            }
        }
    }
    Assert-Contract ($validatorText.Contains('Unclassified backend test')) 'Unclassified project failure must identify the classification error.'
    Assert-Contract ($validatorText.Contains('backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/Nerv.IIP.TemporaryShardClassification.Tests.csproj')) 'Unclassified project failure must identify the temporary project path.'

    $workflowContent = Get-Content -LiteralPath $workflowPath -Raw
    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)^\s+- backend-tests-business-core-b\r?\n', '') -NoNewline
    $workflowValidationText = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $validatorPath, '-WorkflowPath', $temporaryWorkflowPath) -WorkingDirectory $repoRoot -Name 'backend-test-shard-workflow-contract' | Out-Null
        throw 'A workflow with a missing aggregate dependency must fail structured shard governance.'
    }
    catch {
        $workflowValidationText = $_.Exception.Message
        $logMatch = [regex]::Match($workflowValidationText, 'Logs: (?<path>.+)$')
        if ($logMatch.Success) {
            foreach ($logName in @('stdout.log', 'stderr.log')) {
                $logPath = Join-Path $logMatch.Groups['path'].Value $logName
                if (Test-Path -LiteralPath $logPath) {
                    $workflowValidationText += "`n" + (Get-Content -LiteralPath $logPath -Raw)
                }
            }
        }
    }
    Assert-Contract ($workflowValidationText.Contains('Backend Tests aggregate must need exactly')) 'Structured workflow validation must reject an aggregate with a missing shard dependency.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'echo "${{ needs.backend-tests-platform.result }}"') -NoNewline
    $noOpValidationText = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $validatorPath, '-WorkflowPath', $temporaryWorkflowPath) -WorkingDirectory $repoRoot -Name 'backend-test-shard-noop-aggregate-contract' | Out-Null
        throw 'A no-op aggregate dependency expression must fail structured shard governance.'
    }
    catch {
        $noOpValidationText = $_.Exception.Message
    }
    Assert-Contract ($noOpValidationText.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a non-failing aggregate dependency expression.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace 'test "\$\{\{ needs\.backend-tests-platform\.result \}\}" = "success"', 'test "${{ needs.backend-tests-platform.result }}" = "success" || true') -NoNewline
    $maskedFailureValidationText = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $validatorPath, '-WorkflowPath', $temporaryWorkflowPath) -WorkingDirectory $repoRoot -Name 'backend-test-shard-masked-aggregate-contract' | Out-Null
        throw 'An aggregate assertion masked with || true must fail structured shard governance.'
    }
    catch {
        $maskedFailureValidationText = $_.Exception.Message
    }
    Assert-Contract ($maskedFailureValidationText.Contains("Backend Tests aggregate must fail when 'backend-tests-platform' is not success.")) 'Structured workflow validation must reject a masked aggregate dependency assertion.'

    Set-Content -LiteralPath $temporaryWorkflowPath -Value ($workflowContent -replace '(?m)(-TrxFilePrefix backend-tests-platform)', '$1 -TestCommand "Write-Output pass"') -NoNewline
    $bypassValidationText = ''
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-File', $validatorPath, '-WorkflowPath', $temporaryWorkflowPath) -WorkingDirectory $repoRoot -Name 'backend-test-shard-command-bypass-contract' | Out-Null
        throw 'A fast shard command replacement parameter must fail structured shard governance.'
    }
    catch {
        $bypassValidationText = $_.Exception.Message
    }
    Assert-Contract ($bypassValidationText.Contains("Fast shard job 'backend-tests-platform' must not supply a command replacement parameter.")) 'Structured workflow validation must reject a command replacement parameter.'

    $timeoutText = ''
    $timedOut = $false
    try {
        Invoke-NativeCommandOutput -Command 'pwsh' -Arguments @('-NoProfile', '-Command', '[Console]::Out.WriteLine("partial-diagnostic"); [Console]::Out.Flush(); Start-Sleep -Seconds 3') -WorkingDirectory $repoRoot -TimeoutSeconds 1 -Name 'backend-test-shard-timeout-contract' | Out-Null
    }
    catch {
        $timedOut = $true
        $timeoutText = $_.Exception.Message
        Save-BackendTestShardTimeoutDiagnostics -ErrorRecord $_ -ResultsDirectory $timeoutResultsDirectory -TrxFilePrefix 'timeout-contract'
    }
    Assert-Contract ($timedOut -and -not [string]::IsNullOrWhiteSpace($timeoutText)) 'The bounded timeout diagnostic helper contract must time out.'
    Assert-Contract (Test-Path -LiteralPath (Join-Path $timeoutResultsDirectory 'timeout-contract.timeout.stdout.log')) 'The bounded shard timeout contract must preserve diagnostics in the exact results directory.'
    Assert-Contract ((Get-Content -LiteralPath (Join-Path $timeoutResultsDirectory 'timeout-contract.timeout.stdout.log') -Raw).Contains('partial-diagnostic')) 'The bounded timeout diagnostic helper contract must preserve buffered stdout content.'
}
finally {
    if (Test-Path -LiteralPath $temporaryProjectDirectory) {
        Remove-Item -LiteralPath $temporaryProjectDirectory -Recurse -Force
    }
    Remove-Item -LiteralPath $temporaryWorkflowPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $timeoutResultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Backend test shard manifest contract tests passed.'
