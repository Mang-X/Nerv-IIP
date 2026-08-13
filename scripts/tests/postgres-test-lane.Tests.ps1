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
$script:GovernedPostgresMemberIds = @(
    'inventory-postgres-profile',
    'masterdata-postgres-profile',
    'scheduling-postgres-profile',
    'apphub-postgres-profile',
    'barcodelabel-postgres-profile',
    'filestorage-postgres-profile',
    'industrialtelemetry-postgres-profile',
    'maintenance-device-pause-postgres'
)
function Assert-LaneOwnedDatabase([string]$SourcePath, [string]$InnerDatabaseFactory) {
    $source = [IO.File]::ReadAllText($SourcePath)
    if ($source.Contains($InnerDatabaseFactory, [StringComparison]::Ordinal)) {
        throw "Lane source '$([IO.Path]::GetFileName($SourcePath))' must not create an inner database the lane cannot diagnose or clean."
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
    $invalidBatchMessage = 'The authoritative PostgreSQL test step must select every governed lane member exactly through one AST-validated assignment and runner invocation.'
    $expectedMemberIds = @($script:GovernedPostgresMemberIds)
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

    $smallServiceMembers = @(
        @{ id = 'barcodelabel-postgres-profile'; service = 'BarcodeLabel'; schema = 'barcode'; identities = @(
                'Nerv.IIP.Business.BarcodeLabel.Web.Tests.BarcodeLabelPostgresProfileTests.Postgres_unique_conflicts_are_mapped_for_scan_natural_key_and_epcis_event')
            source = 'backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelPostgresProfileTests.cs'
            innerDatabaseFactory = 'TemporaryPostgresDatabase.CreateAsync' },
        @{ id = 'filestorage-postgres-profile'; service = 'FileStorage'; schema = 'filestorage'; identities = @(
                'Nerv.IIP.FileStorage.Web.Tests.FileStorageRestartPersistenceTests.Metadata_usage_and_download_grant_survive_web_host_restart')
            source = 'backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageRestartPersistenceTests.cs'
            innerDatabaseFactory = 'PostgreSqlTestDatabase.CreateAsync' },
        @{ id = 'maintenance-device-pause-postgres'; service = 'Maintenance'; schema = 'maintenance'; identities = @(
                'Nerv.IIP.Business.Maintenance.Web.Tests.MaintenanceIntegrationEventHandlerTests.Device_disabled_consumer_durably_blocks_pm_generation_on_postgres')
            source = 'backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs'
            innerDatabaseFactory = 'TemporaryPostgresDatabase.CreateAsync' }
    )
    foreach ($smallServiceMember in $smallServiceMembers) {
        $laneMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId ([string]$smallServiceMember.id) -RepositoryRoot $repoRoot
        Assert-Contract ([string]::Equals([string]$laneMember.service, [string]$smallServiceMember.service, [StringComparison]::Ordinal)) "Member '$($smallServiceMember.id)' must register service '$($smallServiceMember.service)'."
        Assert-Contract (@($laneMember.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$laneMember.diagnosticSchemas[0], [string]$smallServiceMember.schema, [StringComparison]::Ordinal)) "Member '$($smallServiceMember.id)' must declare its own restricted diagnostic schema."
        Assert-Contract ([string]::Equals((@($laneMember.expectedTestIdentities) -join "`n"), (@($smallServiceMember.identities) -join "`n"), [StringComparison]::Ordinal)) "Member '$($smallServiceMember.id)' must freeze exactly its governed identities."
        $laneSourcePath = Join-Path $repoRoot ([string]$smallServiceMember.source)
        Assert-Contract (Test-Path -LiteralPath $laneSourcePath -PathType Leaf) "Lane source '$($smallServiceMember.source)' must exist."
        Assert-LaneOwnedDatabase -SourcePath $laneSourcePath -InnerDatabaseFactory ([string]$smallServiceMember.innerDatabaseFactory)
    }
    # AppHub 是唯一的 test-owned 成员：NERV-822 要求它的三条用例各自拥有临时数据库，并在初始化前
    # 断言"不在非自有库上初始化"。lane 因此只证明执行数与冻结身份，不声称能在成员数据库里留下诊断。
    $appHubMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'apphub-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract ([string]::Equals([string]$appHubMember.databaseOwnership, 'test-owned', [StringComparison]::Ordinal)) 'AppHub owns its temporary databases per NERV-822 and must be registered as test-owned.'
    Assert-Contract (@($appHubMember.expectedTestIdentities).Count -eq 3) 'The AppHub member must freeze exactly its three PostgreSQL identities.'
    $appHubSourcePath = Join-Path $repoRoot 'backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs'
    $appHubSource = [IO.File]::ReadAllText($appHubSourcePath)
    Assert-Contract ($appHubSource.Contains('PostgreSqlTestDatabase.CreateAsync', [StringComparison]::Ordinal)) 'A test-owned member must build its databases through the governed PostgreSqlTestDatabase helper.'
    Assert-Contract ($appHubSource.Contains('.AssertOwns(', [StringComparison]::Ordinal)) 'The NERV-822 guard that refuses to initialize outside the owned database must stay.'
    Assert-Contract (([regex]::Matches($appHubSource, '\.AssertOwns\(')).Count -ge 3) 'Every AppHub PostgreSQL test must assert it initializes inside its own temporary database.'
    # runner 归属的成员反过来不许自建内层库；两种归属的断言互斥，避免"改了归属就没人管"。
    foreach ($runnerOwnedMemberId in @($script:GovernedPostgresMemberIds | Where-Object { -not [string]::Equals($_, 'apphub-postgres-profile', [StringComparison]::Ordinal) })) {
        $runnerOwnedMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId $runnerOwnedMemberId -RepositoryRoot $repoRoot
        Assert-Contract ([string]::Equals([string]$runnerOwnedMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) "Member '$runnerOwnedMemberId' must declare runner-owned databases."
    }
    # IndustrialTelemetry 的四个类里 47 条用例只有 7 条是真实 PostgreSQL 证明，类级 filter 会让 TRX
    # 身份集合不等于冻结身份而红；因此该成员的 filter 必须逐条精确到方法。
    function Assert-MethodScopedFilter([object]$Member) {
        $segments = @(([string]$Member.filter) -split '\|')
        $frozen = [Collections.Generic.HashSet[string]]::new([string[]]@($Member.expectedTestIdentities), [StringComparer]::Ordinal)
        $selectedIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($segment in $segments) {
            if (-not $segment.StartsWith('FullyQualifiedName~', [StringComparison]::Ordinal)) { throw "Member '$($Member.id)' filter segment '$segment' must be a FullyQualifiedName selector." }
            $selected = $segment.Substring('FullyQualifiedName~'.Length)
            if (-not $frozen.Contains($selected)) { throw "Member '$($Member.id)' filter segment must name a frozen identity, not the enclosing class." }
            # 段数相等 + 每段合法仍允许"两段重复同一身份、另一身份漏选"：必须要求段集合与冻结集合相等。
            if (-not $selectedIdentities.Add($selected)) { throw "Member '$($Member.id)' filter repeats identity '$selected'; a repeated segment can hide a missing one." }
        }
        if (-not $selectedIdentities.SetEquals($frozen)) { throw "Member '$($Member.id)' filter must select exactly its frozen identity set." }
    }
    $telemetryMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'industrialtelemetry-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($telemetryMember.expectedTestIdentities).Count -eq 7) 'The IndustrialTelemetry member must freeze exactly its seven governed PostgreSQL identities.'
    Assert-Contract (@($telemetryMember.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$telemetryMember.diagnosticSchemas[0], 'industrial_telemetry', [StringComparison]::Ordinal)) 'IndustrialTelemetry business and CAP tables share one schema, which the member must declare.'
    Assert-MethodScopedFilter -Member $telemetryMember
    $classScopedMember = [pscustomobject]@{
        id = 'industrialtelemetry-postgres-profile'
        filter = 'FullyQualifiedName~Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.IndustrialTelemetryIdempotentConcurrencyTests'
        expectedTestIdentities = @($telemetryMember.expectedTestIdentities)
    }
    $duplicateSegmentMember = [pscustomobject]@{
        id = 'industrialtelemetry-postgres-profile'
        filter = (@($telemetryMember.expectedTestIdentities)[0..5] + @($telemetryMember.expectedTestIdentities)[0] | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
        expectedTestIdentities = @($telemetryMember.expectedTestIdentities)
    }
    $duplicateSegmentRejected = $false
    try { Assert-MethodScopedFilter -Member $duplicateSegmentMember } catch { $duplicateSegmentRejected = $_.Exception.Message.Contains('repeats identity', [StringComparison]::Ordinal) }
    Assert-Contract $duplicateSegmentRejected 'A repeated filter segment that hides a missing identity must fail closed.'
    $classScopedRejected = $false
    try { Assert-MethodScopedFilter -Member $classScopedMember } catch { $classScopedRejected = $true }
    Assert-Contract $classScopedRejected 'A class-scoped IndustrialTelemetry filter must fail closed, because it would execute non-PostgreSQL siblings.'
    foreach ($telemetrySource in @(
            'IndustrialTelemetryDeviceControlReadFaceTests.cs',
            'IndustrialTelemetryHistorianTests.cs',
            'IndustrialTelemetryIdempotentConcurrencyTests.cs',
            'IndustrialTelemetryOeePostgresQueryTests.cs')) {
        $telemetrySourcePath = Join-Path $repoRoot "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/$telemetrySource"
        Assert-Contract (Test-Path -LiteralPath $telemetrySourcePath -PathType Leaf) "IndustrialTelemetry lane source '$telemetrySource' must exist."
        # 这四个类改造前用的是 IndustrialTelemetryPostgresTestDatabase（Postgres 非 PostgreSql，子串不命中），
        # 且该类仍在同一测试项目里被规模种子用例合法使用——只扫共享 helper 的名字对回潮毫无鉴别力。
        foreach ($telemetryFactory in @('IndustrialTelemetryPostgresTestDatabase.CreateAsync', 'PostgreSqlTestDatabase.CreateAsync')) {
            Assert-LaneOwnedDatabase -SourcePath $telemetrySourcePath -InnerDatabaseFactory $telemetryFactory
        }
    }

    # AppHub 曾有两条"环境变量缺失就 return"的静默空跑用例；lane 只有在它们真正被冻结、
    # 且缺变量时是显式 skip 的情况下才算证明了 AppHub 的 PostgreSQL 语义。
    $appHubSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs'))
    Assert-Contract (-not $appHubSource.Contains('if (string.IsNullOrWhiteSpace(connectionString))', [StringComparison]::Ordinal)) 'AppHub PostgreSQL tests must not silently return when the governed connection string is missing.'
    Assert-Contract ((([regex]::Matches($appHubSource, '\[AppHubRealPostgresFact\]')).Count) -eq 3) 'All three AppHub PostgreSQL tests must be gated by the visible-skip fact attribute.'
    $silentSkipSourcePath = Join-Path $fixtureRoot 'silent-skip-apphub-source.cs'
    [IO.File]::WriteAllText($silentSkipSourcePath, "if (string.IsNullOrWhiteSpace(connectionString))`n{`n    return;`n}`n", [Text.UTF8Encoding]::new($false))
    $silentSkipDetected = [IO.File]::ReadAllText($silentSkipSourcePath).Contains('if (string.IsNullOrWhiteSpace(connectionString))', [StringComparison]::Ordinal)
    Assert-Contract $silentSkipDetected 'The silent-return detector must recognize the pattern it forbids.'

    $selectedMemberIds = @($script:GovernedPostgresMemberIds)
    $validMemberSummaries = @(
        foreach ($governedMemberId in $selectedMemberIds) {
            $governedMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId $governedMemberId -RepositoryRoot $repoRoot
            $governedCount = @($governedMember.expectedTestIdentities).Count
            [pscustomobject]@{ memberId = $governedMemberId; expected = $governedCount; discovered = $governedCount; passed = $governedCount; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }
        }
    )
    Assert-Contract ($validMemberSummaries.Count -eq $selectedMemberIds.Count) 'Every governed member id must resolve to a manifest member.'
    Assert-NervPostgresTestLaneSummary -SelectedMemberIds $selectedMemberIds -MemberSummaries $validMemberSummaries
    # 变异只改最后一个成员，其余成员保持合格：证明聚合断言逐成员生效，而不是"有一个成员通过就放行"。
    $lastIndex = $validMemberSummaries.Count - 1
    $lastMemberId = [string]$validMemberSummaries[$lastIndex].memberId
    $lastExpected = [int]$validMemberSummaries[$lastIndex].expected
    $healthyPrefix = @($validMemberSummaries[0..($lastIndex - 1)])
    $invalidSummaryCases = @(
        @{ name = 'missing-member'; members = $healthyPrefix; diagnostic = "summarized $($healthyPrefix.Count)" },
        @{ name = 'zero-discovery'; members = @($healthyPrefix + [pscustomobject]@{ memberId = $lastMemberId; expected = $lastExpected; discovered = 0; passed = 0; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }); diagnostic = 'discovered 0' },
        @{ name = 'skipped'; members = @($healthyPrefix + [pscustomobject]@{ memberId = $lastMemberId; expected = $lastExpected; discovered = $lastExpected; passed = $lastExpected - 1; failed = 0; skipped = 1; cleanup = 'passed'; outcome = 'passed' }); diagnostic = '1 skipped' },
        @{ name = 'failed'; members = @($healthyPrefix + [pscustomobject]@{ memberId = $lastMemberId; expected = $lastExpected; discovered = $lastExpected; passed = $lastExpected - 1; failed = 1; skipped = 0; cleanup = 'passed'; outcome = 'failed' }); diagnostic = "outcome 'failed'" },
        @{ name = 'cleanup-failed'; members = @($healthyPrefix + [pscustomobject]@{ memberId = $lastMemberId; expected = $lastExpected; discovered = $lastExpected; passed = $lastExpected; failed = 0; skipped = 0; cleanup = 'failed'; outcome = 'passed' }); diagnostic = "cleanup 'failed'" }
    )
    $schedulingIndex = [Array]::IndexOf([string[]]$selectedMemberIds, 'scheduling-postgres-profile')
    $partialDiscoveryMembers = @(
        foreach ($summaryIndex in 0..$lastIndex) {
            if ($summaryIndex -eq $schedulingIndex) {
                [pscustomobject]@{ memberId = 'scheduling-postgres-profile'; expected = 6; discovered = 5; passed = 5; failed = 0; skipped = 0; cleanup = 'passed'; outcome = 'passed' }
            }
            else { $validMemberSummaries[$summaryIndex] }
        }
    )
    $invalidSummaryCases += @{ name = 'partial-discovery'; members = $partialDiscoveryMembers; diagnostic = 'discovered 5' }
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
    $authoritativeAssignment = "`$members = @('" + ($selectedMemberIds -join "', '") + "')"
    Assert-Contract ($workflow.Contains($authoritativeAssignment, [StringComparison]::Ordinal)) 'The authoritative workflow assignment must select the full governed member batch.'
    $droppedMemberCases = @(
        foreach ($droppedMemberId in @('maintenance-device-pause-postgres', 'apphub-postgres-profile', 'masterdata-postgres-profile')) {
            $remainingIds = @($selectedMemberIds | Where-Object { -not [string]::Equals($_, $droppedMemberId, [StringComparison]::Ordinal) })
            @{ name = $droppedMemberId; assignment = "`$members = @('" + ($remainingIds -join "', '") + "')" }
        }
    )
    foreach ($droppedMemberCase in $droppedMemberCases) {
        $mutatedWorkflowPath = Join-Path $fixtureRoot "dropped-$($droppedMemberCase.name)-ci.yml"
        [IO.File]::WriteAllText($mutatedWorkflowPath, $workflow.Replace($authoritativeAssignment, [string]$droppedMemberCase.assignment), [Text.UTF8Encoding]::new($false))
        $workflowMutationRejected = $false
        try { Assert-PostgresWorkflowMemberBatch -WorkflowPath $mutatedWorkflowPath } catch { $workflowMutationRejected = $true }
        Assert-Contract $workflowMutationRejected "Removing $($droppedMemberCase.name) from the authoritative workflow step must fail the structural contract."
    }
    $commentMaskedWorkflowPath = Join-Path $fixtureRoot 'comment-masked-dropped-last-member-ci.yml'
    $commentMaskedAssignment = "# $authoritativeAssignment`n          `$members = @('" + (@($selectedMemberIds | Select-Object -First ($selectedMemberIds.Count - 1)) -join "', '") + "')"
    [IO.File]::WriteAllText($commentMaskedWorkflowPath, $workflow.Replace($authoritativeAssignment, $commentMaskedAssignment), [Text.UTF8Encoding]::new($false))
    $commentMaskedMutationRejected = $false
    try { Assert-PostgresWorkflowMemberBatch -WorkflowPath $commentMaskedWorkflowPath } catch { $commentMaskedMutationRejected = $true }
    Assert-Contract $commentMaskedMutationRejected 'A comment must not mask an active workflow assignment that drops a governed member.'
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
