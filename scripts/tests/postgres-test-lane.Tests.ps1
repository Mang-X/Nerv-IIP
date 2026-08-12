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
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/PostgresTestLane.ps1')
. (Join-Path $repoRoot 'scripts/lib/CiRequiredSummary.ps1')
$manifestPath = Join-Path $repoRoot 'scripts/postgres-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-postgres-lane-$([Guid]::NewGuid().ToString('N'))"
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-MasterDataDiagnosticSchemas([object]$Member) {
    $schemas = @($Member.diagnosticSchemas)
    if ($schemas.Count -ne 2 -or
        -not [string]::Equals([string]$schemas[0], 'business_masterdata', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$schemas[1], 'cap', [StringComparison]::Ordinal)) {
        throw 'The MasterData member must retain restricted business_masterdata and CAP outbox diagnostics.'
    }
}
function Assert-SchedulingLaneOwnedDatabase([string]$SourcePath) {
    $source = [IO.File]::ReadAllText($SourcePath)
    if (-not $source.Contains('[Collection(SchedulingPostgresLaneDatabase.CollectionName)]', [StringComparison]::Ordinal)) {
        throw "Scheduling lane source '$([IO.Path]::GetFileName($SourcePath))' must join the serializing lane collection."
    }
    if ($source.Contains('PostgreSqlTestDatabase.CreateAsync', [StringComparison]::Ordinal)) {
        throw "Scheduling lane source '$([IO.Path]::GetFileName($SourcePath))' must not create an inner database the lane cannot diagnose or clean."
    }
}
function Assert-PostgresWorkflowMemberBatch([string]$WorkflowPath) {
    $document = ConvertFrom-NervCiRequiredSummaryWorkflow -Path $WorkflowPath -WorkingDirectory $repoRoot
    $job = $document.jobs.'postgres-provider-tests'
    $testSteps = @($job.steps | Where-Object { [string]::Equals((Get-NervCiRequiredSummaryStringValue -Object $_ -PropertyName 'id'), 'postgres-tests', [StringComparison]::Ordinal) })
    if ($testSteps.Count -ne 1) { throw 'PostgreSQL Provider Tests must contain exactly one authoritative postgres-tests step.' }
    $invalidBatchMessage = 'The authoritative PostgreSQL test step must select Inventory, MasterData and Scheduling exactly through one AST-validated assignment and runner invocation.'
    $expectedMemberIds = @('inventory-postgres-profile', 'masterdata-postgres-profile', 'scheduling-postgres-profile')
    $run = [regex]::Replace([string]$testSteps[0].run, '\$\{\{.*?\}\}', 'github-expression')
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($run, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0 -or $ast.EndBlock.Statements.Count -ne 2) { throw $invalidBatchMessage }

    $assignment = $ast.EndBlock.Statements[0]
    if ($assignment -isnot [System.Management.Automation.Language.AssignmentStatementAst] -or
        $assignment.Operator -ne [System.Management.Automation.Language.TokenKind]::Equals -or
        $assignment.Left -isnot [System.Management.Automation.Language.VariableExpressionAst] -or
        -not [string]::Equals($assignment.Left.VariablePath.UserPath, 'members', [StringComparison]::Ordinal)) {
        throw $invalidBatchMessage
    }
    $arrayExpression = if ($assignment.Right -is [System.Management.Automation.Language.CommandExpressionAst]) { $assignment.Right.Expression } else { $null }
    $arrayLiteral = if ($arrayExpression -is [System.Management.Automation.Language.ArrayExpressionAst] -and
        $arrayExpression.SubExpression.Statements.Count -eq 1 -and
        $arrayExpression.SubExpression.Statements[0] -is [System.Management.Automation.Language.PipelineAst] -and
        $arrayExpression.SubExpression.Statements[0].PipelineElements.Count -eq 1 -and
        $arrayExpression.SubExpression.Statements[0].PipelineElements[0] -is [System.Management.Automation.Language.CommandExpressionAst]) {
        $arrayExpression.SubExpression.Statements[0].PipelineElements[0].Expression
    } else { $null }
    $memberValues = if ($arrayLiteral -is [System.Management.Automation.Language.ArrayLiteralAst]) { @($arrayLiteral.Elements) } else { @() }
    if ($memberValues.Count -ne $expectedMemberIds.Count) { throw $invalidBatchMessage }
    for ($memberIndex = 0; $memberIndex -lt $expectedMemberIds.Count; $memberIndex++) {
        if ($memberValues[$memberIndex] -isnot [System.Management.Automation.Language.StringConstantExpressionAst] -or
            -not [string]::Equals($memberValues[$memberIndex].Value, $expectedMemberIds[$memberIndex], [StringComparison]::Ordinal)) {
            throw $invalidBatchMessage
        }
    }

    $pipeline = $ast.EndBlock.Statements[1]
    $command = if ($pipeline -is [System.Management.Automation.Language.PipelineAst] -and $pipeline.PipelineElements.Count -eq 1) { $pipeline.PipelineElements[0] } else { $null }
    if ($command -isnot [System.Management.Automation.Language.CommandAst] -or
        -not [string]::Equals($command.GetCommandName(), './scripts/run-postgres-test-lane.ps1', [StringComparison]::Ordinal)) {
        throw $invalidBatchMessage
    }
    $memberParameters = @($command.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and [string]::Equals($_.ParameterName, 'MemberId', [StringComparison]::OrdinalIgnoreCase) })
    if ($memberParameters.Count -ne 1) { throw $invalidBatchMessage }
    $parameterIndex = [Array]::IndexOf([object[]]$command.CommandElements, $memberParameters[0])
    $memberArgument = if ($parameterIndex -ge 0 -and $parameterIndex + 1 -lt $command.CommandElements.Count) { $command.CommandElements[$parameterIndex + 1] } else { $null }
    if ($memberArgument -isnot [System.Management.Automation.Language.VariableExpressionAst] -or
        -not [string]::Equals($memberArgument.VariablePath.UserPath, 'members', [StringComparison]::Ordinal)) {
        throw $invalidBatchMessage
    }
}
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $member = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'inventory-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($member.expectedTestIdentities).Count -eq 1) 'The second-layer pilot must freeze exactly one Inventory test.'
    Assert-Contract (@($member.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$member.diagnosticSchemas[0], 'inventory', [StringComparison]::Ordinal)) 'The pilot member must own its restricted diagnostic schema declaration.'
    $masterDataMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'masterdata-postgres-profile' -RepositoryRoot $repoRoot
    $masterDataIdentities = @(
        'Nerv.IIP.Business.MasterData.Web.Tests.MasterDataPostgresProfileTests.Postgres_cap_concurrent_operation_recovers_loser_and_persists_exactly_one_audit_and_outbox',
        'Nerv.IIP.Business.MasterData.Web.Tests.MasterDataPostgresProfileTests.Postgres_device_reference_batch_uses_two_fixed_relational_reads_for_one_and_two_hundred_references',
        'Nerv.IIP.Business.MasterData.Web.Tests.MasterDataPostgresProfileTests.Postgres_disable_endpoint_transaction_fact_persists_audit_and_cap_outbox_with_operation_identity',
        'Nerv.IIP.Business.MasterData.Web.Tests.MasterDataPostgresProfileTests.Postgres_store_persists_master_data_aggregates',
        'Nerv.IIP.Business.MasterData.Web.Tests.MasterDataPostgresProfileTests.Postgres_work_calendar_update_replaces_owned_details_after_reload'
    )
    Assert-Contract ([string]::Equals([string]$masterDataMember.service, 'MasterData', [StringComparison]::Ordinal)) 'The first checklist-three batch must register MasterData as its own lane member.'
    Assert-Contract ([string]::Equals([string]$masterDataMember.project, 'backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The MasterData member must target the owning test project.'
    Assert-MasterDataDiagnosticSchemas -Member $masterDataMember
    $missingCapManifestPath = Join-Path $fixtureRoot 'missing-masterdata-cap-diagnostics.json'
    $missingCapManifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -Depth 20
    $missingCapManifestMember = @($missingCapManifest.members | Where-Object { [string]::Equals([string]$_.id, 'masterdata-postgres-profile', [StringComparison]::Ordinal) })[0]
    $missingCapManifestMember.diagnosticSchemas = @($missingCapManifestMember.diagnosticSchemas | Where-Object { -not [string]::Equals([string]$_, 'cap', [StringComparison]::Ordinal) })
    Assert-Contract (@($missingCapManifestMember.diagnosticSchemas).Count -eq 1) 'The CAP diagnostics mutation must remove exactly one governed schema.'
    [IO.File]::WriteAllText($missingCapManifestPath, (($missingCapManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $missingCapMember = Import-NervPostgresTestLaneMember -ManifestPath $missingCapManifestPath -MemberId 'masterdata-postgres-profile' -RepositoryRoot $repoRoot
    $missingCapRejected = $false
    try { Assert-MasterDataDiagnosticSchemas -Member $missingCapMember } catch { $missingCapRejected = $_.Exception.Message.Contains('CAP outbox diagnostics', [StringComparison]::Ordinal) }
    Assert-Contract $missingCapRejected 'Removing CAP from the MasterData diagnostic schemas must fail the contract.'
    Assert-Contract ([string]::Equals((@($masterDataMember.expectedTestIdentities) -join "`n"), ($masterDataIdentities -join "`n"), [StringComparison]::Ordinal)) 'The MasterData member must freeze exactly the five profile identities and exclude the world-bible seed test.'

    $schedulingMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'scheduling-postgres-profile' -RepositoryRoot $repoRoot
    $schedulingIdentities = @(
        'Nerv.IIP.Business.Scheduling.Web.Tests.OrderUrgencyRetentionPostgresCapacityTests.Representative_capacity_scan_and_overlapping_workers_are_safe_on_PostgreSQL',
        'Nerv.IIP.Business.Scheduling.Web.Tests.RecordSchedulePlanInvalidationsPostgresProfileTests.Postgres_calendar_event_handler_changes_the_generated_plan_query_state_once',
        'Nerv.IIP.Business.Scheduling.Web.Tests.RecordSchedulePlanInvalidationsPostgresProfileTests.Postgres_records_generated_calendar_invalidation_without_matching_released_or_other_calendar_plans',
        'Nerv.IIP.Business.Scheduling.Web.Tests.RecordSchedulePlanInvalidationsPostgresProfileTests.Postgres_records_invalidation_for_a_generated_plan_matched_by_resource',
        'Nerv.IIP.Business.Scheduling.Web.Tests.ScheduleReleaseGovernancePostgresProfileTests.Concurrent_releases_converge_to_one_active_plan_with_monotonic_revisions',
        'Nerv.IIP.Business.Scheduling.Web.Tests.ScheduleReleaseGovernancePostgresProfileTests.Migration_normalizes_historical_duplicate_releases_with_exact_timestamp_tie'
    )
    Assert-Contract ([string]::Equals([string]$schedulingMember.service, 'Scheduling', [StringComparison]::Ordinal)) 'The second checklist-three batch must register Scheduling as its own lane member.'
    Assert-Contract ([string]::Equals([string]$schedulingMember.project, 'backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The Scheduling member must target the owning test project.'
    Assert-Contract (@($schedulingMember.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$schedulingMember.diagnosticSchemas[0], 'scheduling', [StringComparison]::Ordinal)) 'The Scheduling member must own its restricted diagnostic schema declaration.'
    Assert-Contract ([string]::Equals((@($schedulingMember.expectedTestIdentities) -join "`n"), ($schedulingIdentities -join "`n"), [StringComparison]::Ordinal)) 'The Scheduling member must freeze exactly the six governed profile and capacity identities.'
    $schedulingFilterClasses = @(
        'Nerv.IIP.Business.Scheduling.Web.Tests.OrderUrgencyRetentionPostgresCapacityTests',
        'Nerv.IIP.Business.Scheduling.Web.Tests.RecordSchedulePlanInvalidationsPostgresProfileTests',
        'Nerv.IIP.Business.Scheduling.Web.Tests.ScheduleReleaseGovernancePostgresProfileTests'
    )
    foreach ($schedulingClass in $schedulingFilterClasses) {
        Assert-Contract ([string]$schedulingMember.filter).Contains("FullyQualifiedName~$schedulingClass", [StringComparison]::Ordinal) "The Scheduling member filter must select '$schedulingClass'."
    }
    $schedulingSourceDirectory = Join-Path $repoRoot 'backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests'
    foreach ($schedulingClass in $schedulingFilterClasses) {
        $schedulingSourcePath = Join-Path $schedulingSourceDirectory "$($schedulingClass.Substring($schedulingClass.LastIndexOf('.', [StringComparison]::Ordinal) + 1)).cs"
        Assert-Contract (Test-Path -LiteralPath $schedulingSourcePath -PathType Leaf) "The Scheduling lane source '$schedulingSourcePath' must exist."
        Assert-SchedulingLaneOwnedDatabase -SourcePath $schedulingSourcePath
    }
    $innerDatabaseSourcePath = Join-Path $fixtureRoot 'inner-database-scheduling-source.cs'
    [IO.File]::WriteAllText(
        $innerDatabaseSourcePath,
        "[Collection(SchedulingPostgresLaneDatabase.CollectionName)]`nawait PostgreSqlTestDatabase.CreateAsync(connectionString, `"nerv_scheduling_test`");`n",
        [Text.UTF8Encoding]::new($false))
    $innerDatabaseRejected = $false
    try { Assert-SchedulingLaneOwnedDatabase -SourcePath $innerDatabaseSourcePath } catch { $innerDatabaseRejected = $_.Exception.Message.Contains('inner database', [StringComparison]::Ordinal) }
    Assert-Contract $innerDatabaseRejected 'Reintroducing an inner Scheduling database must fail the lane-owned database contract.'
    $unserializedSourcePath = Join-Path $fixtureRoot 'unserialized-scheduling-source.cs'
    [IO.File]::WriteAllText($unserializedSourcePath, "public sealed class ScheduleReleaseGovernancePostgresProfileTests`n", [Text.UTF8Encoding]::new($false))
    $unserializedRejected = $false
    try { Assert-SchedulingLaneOwnedDatabase -SourcePath $unserializedSourcePath } catch { $unserializedRejected = $_.Exception.Message.Contains('serializing lane collection', [StringComparison]::Ordinal) }
    Assert-Contract $unserializedRejected 'Dropping the serializing collection must fail closed, because the members share one governed database.'
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

    $selectedMemberIds = @('inventory-postgres-profile', 'masterdata-postgres-profile', 'scheduling-postgres-profile')
    $validMemberSummaries = @(
        [pscustomobject]@{ memberId = 'inventory-postgres-profile'; expected = 1; discovered = 1; passed = 1; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' },
        [pscustomobject]@{ memberId = 'masterdata-postgres-profile'; expected = 5; discovered = 5; passed = 5; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' },
        [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 6; passed = 6; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }
    )
    Assert-NervPostgresTestLaneSummary -SelectedMemberIds $selectedMemberIds -MemberSummaries $validMemberSummaries
    $invalidSummaryCases = @(
        @{ name = 'missing-member'; members = @($validMemberSummaries[0], $validMemberSummaries[1]); diagnostic = 'summarized 2' },
        @{ name = 'zero-discovery'; members = @($validMemberSummaries[0], $validMemberSummaries[1], [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 0; passed = 0; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }); diagnostic = 'discovered 0' },
        @{ name = 'partial-discovery'; members = @($validMemberSummaries[0], $validMemberSummaries[1], [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 5; passed = 5; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }); diagnostic = 'discovered 5' },
        @{ name = 'skipped'; members = @($validMemberSummaries[0], $validMemberSummaries[1], [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 6; passed = 5; failed = 0; skipped = 1; cleanup = 'passed'; outcome = 'passed' }); diagnostic = '1 skipped' },
        @{ name = 'failed'; members = @($validMemberSummaries[0], $validMemberSummaries[1], [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 6; passed = 5; failed = 1; skipped = 0; cleanup = 'passed'; outcome = 'failed' }); diagnostic = "outcome 'failed'" },
        @{ name = 'cleanup-failed'; members = @($validMemberSummaries[0], $validMemberSummaries[1], [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 6; passed = 6; failed = 0; skipped = 0; cleanup = 'failed'; outcome = 'passed' }); diagnostic = "cleanup 'failed'" }
    )
    foreach ($case in $invalidSummaryCases) {
        $summaryRejected = $false
        try { Assert-NervPostgresTestLaneSummary -SelectedMemberIds $selectedMemberIds -MemberSummaries @($case.members) } catch { $summaryRejected = $_.Exception.Message.Contains([string]$case.diagnostic, [StringComparison]::Ordinal) }
        Assert-Contract $summaryRejected "PostgreSQL aggregate case '$($case.name)' must fail closed with its governed diagnostic."
    }

    $runnerPath = Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1'
    $runner = [IO.File]::ReadAllText($runnerPath)
    $workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
    $workflow = [IO.File]::ReadAllText($workflowPath)
    Assert-PostgresWorkflowMemberBatch -WorkflowPath $workflowPath
    $authoritativeAssignment = "`$members = @('inventory-postgres-profile', 'masterdata-postgres-profile', 'scheduling-postgres-profile')"
    Assert-Contract ($workflow.Contains($authoritativeAssignment, [StringComparison]::Ordinal)) 'The authoritative workflow assignment must select the full governed member batch.'
    $droppedMemberCases = @(
        @{ name = 'scheduling'; assignment = "`$members = @('inventory-postgres-profile', 'masterdata-postgres-profile')" },
        @{ name = 'masterdata'; assignment = "`$members = @('inventory-postgres-profile', 'scheduling-postgres-profile')" }
    )
    foreach ($droppedMemberCase in $droppedMemberCases) {
        $mutatedWorkflowPath = Join-Path $fixtureRoot "dropped-$($droppedMemberCase.name)-ci.yml"
        [IO.File]::WriteAllText($mutatedWorkflowPath, $workflow.Replace($authoritativeAssignment, [string]$droppedMemberCase.assignment), [Text.UTF8Encoding]::new($false))
        $workflowMutationRejected = $false
        try { Assert-PostgresWorkflowMemberBatch -WorkflowPath $mutatedWorkflowPath } catch { $workflowMutationRejected = $true }
        Assert-Contract $workflowMutationRejected "Removing $($droppedMemberCase.name) from the authoritative workflow step must fail the structural contract."
    }
    $commentMaskedWorkflowPath = Join-Path $fixtureRoot 'comment-masked-dropped-scheduling-ci.yml'
    $commentMaskedAssignment = "# $authoritativeAssignment`n          `$members = @('inventory-postgres-profile', 'masterdata-postgres-profile')"
    [IO.File]::WriteAllText($commentMaskedWorkflowPath, $workflow.Replace($authoritativeAssignment, $commentMaskedAssignment), [Text.UTF8Encoding]::new($false))
    $commentMaskedMutationRejected = $false
    try { Assert-PostgresWorkflowMemberBatch -WorkflowPath $commentMaskedWorkflowPath } catch { $commentMaskedMutationRejected = $true }
    Assert-Contract $commentMaskedMutationRejected 'A comment must not mask an active workflow assignment that removes Scheduling.'
    Assert-Contract ($runner.Contains('[string[]] $MemberId', [StringComparison]::Ordinal)) 'The runner must accept an explicit ordered member batch.'
    Assert-Contract ($runner.Contains('foreach ($selectedMemberId in $MemberId)', [StringComparison]::Ordinal)) 'The runner must execute every selected member instead of authenticating only the pilot.'
    Assert-Contract ($runner.Contains("Join-Path `$ResultsDirectory ([string]`$member.id)", [StringComparison]::Ordinal)) 'Each selected member must own an isolated TRX directory.'
    Assert-Contract ($runner.Contains('$summary.members = @($memberSummaries)', [StringComparison]::Ordinal)) 'The dependency summary must retain per-member evidence.'
    Assert-Contract ($runner.Contains("`$memberSummaries.Count -ne `$MemberId.Count", [StringComparison]::Ordinal)) 'The aggregate runner must reject incomplete member execution.'
    Assert-Contract ($runner.Contains("GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')", [StringComparison]::Ordinal)) 'The runner must consume the frozen external PostgreSQL variable.'
    Assert-Contract (-not $runner.Contains('NERV_IIP_TEST_POSTGRES_ADMIN', [StringComparison]::Ordinal) -and -not $workflow.Contains('NERV_IIP_TEST_POSTGRES_ADMIN', [StringComparison]::Ordinal)) 'No CI-only PostgreSQL connection-string contract may be introduced.'
    Assert-Contract ($runner.Contains('$databaseCreated = $true', [StringComparison]::Ordinal) -and $runner.Contains('if ($databaseCreated)', [StringComparison]::Ordinal)) 'Cleanup must only target a database created by this runner.'
    $diagnosticIndex = $runner.IndexOf('-failure-diagnostics"', [StringComparison]::Ordinal)
    $dropIndex = $runner.IndexOf('-drop-database"', [StringComparison]::Ordinal)
    Assert-Contract ($diagnosticIndex -ge 0 -and $dropIndex -gt $diagnosticIndex) 'Failure diagnostics must be captured before database cleanup.'
    Assert-Contract ($runner.Contains('$member.diagnosticSchemas', [StringComparison]::Ordinal) -and -not $runner.Contains("n.nspname = 'inventory'", [StringComparison]::Ordinal)) 'Failure diagnostics must be derived from each governed member instead of hard-coding the pilot schema.'
    Assert-Contract ($workflow.Contains('image: postgres:18', [StringComparison]::Ordinal) -and $workflow.Contains('pg_isready -U nerv -d postgres', [StringComparison]::Ordinal)) 'The pilot must use a health-checked PostgreSQL 18 service.'
    Assert-Contract ($workflow.Contains('-DatabaseSuffix ${{ github.run_id }}_${{ github.run_attempt }}', [StringComparison]::Ordinal)) 'Hosted members must derive distinct governed databases from the same run/attempt suffix.'
    Assert-Contract ($workflow.Contains('-JobName "PostgreSQL Provider Tests"', [StringComparison]::Ordinal)) 'Normalized evidence must bind to the authoritative PostgreSQL job.'
}
finally { if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force } }
Write-Output 'PostgreSQL test lane contract tests passed.'
