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
$manifestPath = Join-Path $repoRoot 'scripts/backend-test-shards.json'
$validatorPath = Join-Path $repoRoot 'scripts/verify-backend-test-shards.ps1'
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$temporaryProjectDirectory = Join-Path $repoRoot 'backend/tests/Nerv.IIP.TemporaryShardClassification.Tests'
$temporaryProjectPath = Join-Path $temporaryProjectDirectory 'Nerv.IIP.TemporaryShardClassification.Tests.csproj'

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

& pwsh -NoProfile -File $validatorPath
Assert-Contract ($LASTEXITCODE -eq 0) 'Backend test shard validator must accept the checked-in classification.'

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
Assert-Contract ($fastShards.Count -eq 4) 'Phase 1 must define exactly four fast backend shards.'
Assert-Contract (((@($fastShards.id) | Sort-Object) -join '|') -ceq 'business-core-a|business-core-b|business-gateway|platform') 'Fast shard IDs must remain the four phase-1 CI jobs.'
Assert-Contract (((@($heavyLanes.id) | Sort-Object) -join '|') -ceq 'full-chain|performance|real-acceptance') 'Heavy lane IDs must remain explicit and separate from fast shards.'
Assert-Contract ((@($fastShards | Where-Object { $_.id -eq 'business-gateway' }).projects).Count -eq 1) 'BusinessGateway must stay isolated in its own fast shard before MAN-663.'

$classifiedProjects = @($fastShards.projects) + @($heavyLanes.projects)
Assert-Contract (($classifiedProjects | Sort-Object -Unique).Count -eq $classifiedProjects.Count) 'Every backend test project must be classified exactly once.'
Assert-Contract ($classifiedProjects.Count -eq 64) 'The checked-in backend test inventory must contain 64 classified projects.'

foreach ($shard in $fastShards) {
    $filterPath = Join-Path $repoRoot $shard.solutionFilter
    $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
    Assert-Contract ($filter.solution.path -eq '../Nerv.IIP.sln') "Solution filter $($shard.solutionFilter) must target the backend solution."
    Assert-Contract ((@($filter.solution.projects | Where-Object { $_ -match '^\.\./' })).Count -eq 0) "Solution filter $($shard.solutionFilter) project paths must be relative to backend/Nerv.IIP.sln."
}

$workflowContent = Get-Content -LiteralPath $workflowPath -Raw
foreach ($requiredText in @(
    'backend-tests-business-gateway:',
    'backend-tests-platform:',
    'backend-tests-business-core-a:',
    'backend-tests-business-core-b:',
    'backend-tests:',
    'name: Backend Tests',
    'needs:',
    'backend-tests-business-gateway',
    'backend-tests-platform',
    'backend-tests-business-core-a',
    'backend-tests-business-core-b',
    'actions/upload-artifact@v4',
    'LogFileName=backend-tests-business-gateway.trx',
    'LogFileName=backend-tests-platform.trx',
    'LogFileName=backend-tests-business-core-a.trx',
    'LogFileName=backend-tests-business-core-b.trx'
)) {
    Assert-Contract ($workflowContent.Contains($requiredText)) "CI workflow is missing required backend shard contract: $requiredText"
}

try {
    New-Item -ItemType Directory -Path $temporaryProjectDirectory -Force | Out-Null
    Set-Content -LiteralPath $temporaryProjectPath -Value '<Project Sdk="Microsoft.NET.Sdk" />' -NoNewline

    $validatorText = ''
    try {
        & $validatorPath
        throw 'An unclassified temporary backend test project must fail classification.'
    }
    catch {
        $validatorText = $_.Exception.Message
    }
    Assert-Contract ($validatorText.Contains('Unclassified backend test projects:')) 'Unclassified project failure must identify the classification error.'
    Assert-Contract ($validatorText.Contains('backend/tests/Nerv.IIP.TemporaryShardClassification.Tests/Nerv.IIP.TemporaryShardClassification.Tests.csproj')) 'Unclassified project failure must identify the temporary project path.'
}
finally {
    if (Test-Path -LiteralPath $temporaryProjectDirectory) {
        Remove-Item -LiteralPath $temporaryProjectDirectory -Recurse -Force
    }
}

Write-Host 'Backend test shard manifest contract tests passed.'
