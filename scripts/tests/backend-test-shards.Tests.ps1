# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes one temporary unclassified backend test project
#   Writes:
#     - backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/** (temporarily)
#   Cleanup:
#     - Removes the temporary test project in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/backend-test-shards.json'
$validatorPath = Join-Path $repoRoot 'scripts/verify-backend-test-shards.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$temporaryProjectDirectory = Join-Path $repoRoot 'backend/tests/Nerv.IIP.TemporaryShardClassification.Tests'
$temporaryProjectPath = Join-Path $temporaryProjectDirectory 'Nerv.IIP.TemporaryShardClassification.Tests.csproj'
$temporaryWorkflowPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-backend-test-shards-{0}.yml" -f [Guid]::NewGuid().ToString('N'))

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
Assert-Contract ($excludedSelectors.Count -eq 48) 'Every currently excluded real PostgreSQL test selector must be explicitly classified.'

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
}
finally {
    if (Test-Path -LiteralPath $temporaryProjectDirectory) {
        Remove-Item -LiteralPath $temporaryProjectDirectory -Recurse -Force
    }
    Remove-Item -LiteralPath $temporaryWorkflowPath -Force -ErrorAction SilentlyContinue
}

Write-Host 'Backend test shard manifest contract tests passed.'
