# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates PostgreSQL lane manifest and TRX contracts with temporary fixtures
#   Writes:
#     - Temporary TRX fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/PostgresTestLane.ps1')
$manifestPath = Join-Path $repoRoot 'scripts/postgres-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-postgres-lane-$([Guid]::NewGuid().ToString('N'))"
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $member = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'inventory-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($member.expectedTestIdentities).Count -eq 1) 'The second-layer pilot must freeze exactly one Inventory test.'
    Assert-Contract (@($member.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$member.diagnosticSchemas[0], 'inventory', [StringComparison]::Ordinal)) 'The pilot member must own its restricted diagnostic schema declaration.'
    $identity = [string]$member.expectedTestIdentities[0]
    $separatorIndex = $identity.LastIndexOf('.', [StringComparison]::Ordinal)
    $class = $identity.Substring(0, $separatorIndex)
    $method = $identity.Substring($separatorIndex + 1)
    $trx = "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results><UnitTestResult testId=`"1`" testName=`"$method`" outcome=`"Passed`" /></Results><TestDefinitions><UnitTest id=`"1`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest></TestDefinitions></TestRun>"
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'passed.trx'), $trx, [Text.UTF8Encoding]::new($false))
    $result = Get-NervPostgresTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity)
    Assert-Contract ($result.passed -eq 1 -and $result.skipped -eq 0) 'A fully passed frozen identity must satisfy the lane contract.'
    $skipped = $trx.Replace('outcome="Passed"', 'outcome="NotExecuted"')
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'passed.trx'), $skipped, [Text.UTF8Encoding]::new($false))
    $invalidResult = Get-NervPostgresTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) -AllowInvalid
    Assert-Contract (-not $invalidResult.valid -and $invalidResult.skipped -eq 1) 'Failure summaries must retain the actual skipped count.'
    $rejected = $false
    try { Get-NervPostgresTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities @($identity) | Out-Null } catch { $rejected = $_.Exception.Message.Contains('0 skipped', [StringComparison]::Ordinal) }
    Assert-Contract $rejected 'An all-skipped pilot must fail closed.'

    $runnerPath = Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1'
    $runner = [IO.File]::ReadAllText($runnerPath)
    $workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
    Assert-Contract ($runner.Contains("GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')", [StringComparison]::Ordinal)) 'The runner must consume the frozen external PostgreSQL variable.'
    Assert-Contract (-not $runner.Contains('NERV_IIP_TEST_POSTGRES_ADMIN', [StringComparison]::Ordinal) -and -not $workflow.Contains('NERV_IIP_TEST_POSTGRES_ADMIN', [StringComparison]::Ordinal)) 'No CI-only PostgreSQL connection-string contract may be introduced.'
    Assert-Contract ($runner.Contains('$databaseCreated = $true', [StringComparison]::Ordinal) -and $runner.Contains('if ($databaseCreated)', [StringComparison]::Ordinal)) 'Cleanup must only target a database created by this runner.'
    $diagnosticIndex = $runner.IndexOf("'postgres-lane-failure-diagnostics'", [StringComparison]::Ordinal)
    $dropIndex = $runner.IndexOf("'postgres-lane-drop-database'", [StringComparison]::Ordinal)
    Assert-Contract ($diagnosticIndex -ge 0 -and $dropIndex -gt $diagnosticIndex) 'Failure diagnostics must be captured before database cleanup.'
    Assert-Contract ($runner.Contains('$member.diagnosticSchemas', [StringComparison]::Ordinal) -and -not $runner.Contains("n.nspname = 'inventory'", [StringComparison]::Ordinal)) 'Failure diagnostics must be derived from each governed member instead of hard-coding the pilot schema.'
    Assert-Contract ($workflow.Contains('image: postgres:18', [StringComparison]::Ordinal) -and $workflow.Contains('pg_isready -U nerv -d postgres', [StringComparison]::Ordinal)) 'The pilot must use a health-checked PostgreSQL 18 service.'
    Assert-Contract ($workflow.Contains('nerv_iip_inventory_${{ github.run_id }}_${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'The hosted pilot must use a run/attempt-scoped database.'
    Assert-Contract ($workflow.Contains('-JobName "PostgreSQL Provider Tests"', [StringComparison]::Ordinal)) 'Normalized evidence must bind to the authoritative PostgreSQL job.'
}
finally { if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force } }
Write-Output 'PostgreSQL test lane contract tests passed.'
