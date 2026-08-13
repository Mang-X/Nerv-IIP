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
    'quality-postgres-profile',
    'mes-postgres-profile',
    'wms-postgres-profile',
    'erp-postgres-profile',
    'demandplanning-postgres-profile',
    'acceptance-postgres-profile',
    'maintenance-device-pause-postgres'
)
function Get-NervCSharpMethodBody([string]$Source, [string]$MethodName) {
    $signatureIndex = $Source.IndexOf(" $MethodName(", [StringComparison]::Ordinal)
    if ($signatureIndex -lt 0) { return $null }
    $openIndex = $Source.IndexOf('{', $signatureIndex, [StringComparison]::Ordinal)
    if ($openIndex -lt 0) { return $null }
    $depth = 0
    for ($cursor = $openIndex; $cursor -lt $Source.Length; $cursor++) {
        if ($Source[$cursor] -eq '{') { $depth++ }
        elseif ($Source[$cursor] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Source.Substring($openIndex, $cursor - $openIndex + 1) }
        }
    }
    return $null
}
# 每条冻结用例都必须先把自己的 schema 删掉再迁移：成员数据库在成员内跨用例共享，
# 漏掉重置不会假绿，但会以"上一条用例的残留"形式产生难排查的失败。此前这只是约定。
function Assert-FrozenIdentityResetsSchema([string]$SourcePath, [string]$MethodName) {
    $source = [IO.File]::ReadAllText($SourcePath)
    $body = Get-NervCSharpMethodBody -Source $source -MethodName $MethodName
    if ($null -eq $body) { throw "Frozen identity '$MethodName' was not found in '$([IO.Path]::GetFileName($SourcePath))'." }
    if ($body -cnotmatch '(?:Reset|Drop)[A-Za-z]*SchemaAsync\s*\(') {
        throw "Frozen identity '$MethodName' must reset its schema before migrating; the shared member database is reused across the member's tests."
    }
}
# 重置必须是 DROP SCHEMA ... CASCADE：漏掉 CASCADE 会在存在外键/视图时静默失败成"删不掉"。
function Assert-LaneResetDropsCascade([string]$SourcePath) {
    $source = [IO.File]::ReadAllText($SourcePath)
    if ($source -cnotmatch 'DROP SCHEMA IF EXISTS \{[A-Za-z]+\} CASCADE') {
        throw "Lane reset in '$([IO.Path]::GetFileName($SourcePath))' must drop the governed schema with IF EXISTS and CASCADE."
    }
}
# 穷举已知的内层库工厂习语，而不是逐成员点名自己历史上用过的那一个：按成员点名时，
# 有人顺手改用共享库里现成的另一个工厂就不会被拒（#1510 的教训——漏拼写要从类型集穷举）。
# 内层库工厂清单从**行为**穷举，而不是手维护点名：扫测试树里所有含 `CREATE DATABASE` 的源文件，
# 取其中声明的数据库工厂类型名。手维护清单每接一个服务就要靠人记得补一次，实测漏掉了
# DisposablePostgresDatabase / WorldHistoryTemporaryDatabase / IndustrialTelemetryPostgresTestDatabase 三项
# （#1510 的教训：漏拼写要从类型集穷举，不从审核点名补特例）。
function Get-NervInnerDatabaseFactories([string]$RepositoryRoot) {
    $discovered = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($sourceFile in @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'backend') -Filter '*.cs' -File -Recurse)) {
        $sourceText = [IO.File]::ReadAllText($sourceFile.FullName)
        if (-not $sourceText.Contains('CREATE DATABASE', [StringComparison]::Ordinal)) { continue }
        foreach ($declaration in [regex]::Matches($sourceText, 'class\s+(?<name>[A-Za-z0-9_]*(?:Database|TestSettings))\b')) {
            $factoryType = $declaration.Groups['name'].Value
            if ($factoryType.EndsWith('Name', [StringComparison]::Ordinal)) { continue }
            $discovered.Add("$factoryType.CreateAsync(") | Out-Null
            $discovered.Add("$factoryType.CreateDatabaseAsync(") | Out-Null
        }
    }
    return @($discovered)
}
$script:InnerDatabaseFactories = @(Get-NervInnerDatabaseFactories -RepositoryRoot $repoRoot | ForEach-Object { $_.TrimEnd('(') })
# 能力串兜底只认**字符串字面量**里的建库语句：帮助类的注释里会解释"为什么不许 CREATE DATABASE"，
# 裸子串扫描会把这句解释本身判红。
function Assert-NoDatabaseCreationStatement([string]$SourcePath) {
    $source = [IO.File]::ReadAllText($SourcePath)
    if ($source -cmatch '"[^"\r\n]*CREATE DATABASE') {
        throw "Lane source '$([IO.Path]::GetFileName($SourcePath))' must not issue CREATE DATABASE; the runner owns the member database."
    }
}
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
    # 对账用**第二条独立推导路径**：从调用点反推（`X.CreateAsync(` 的类型名，且其声明文件含 CREATE DATABASE），
    # 要求声明侧穷举必须覆盖它。冻结一份已知名单会随工厂增删而过期——DisposablePostgresDatabase 就是在
    # NERV-822 后续与 MES 批次里被删掉的，快照名单只会制造假红。
    $discoveredFactories = [Collections.Generic.HashSet[string]]::new([string[]]@($script:InnerDatabaseFactories), [StringComparer]::Ordinal)
    $databaseDeclaringTypes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $callSiteTypes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($backendSource in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'backend') -Filter '*.cs' -File -Recurse)) {
        $backendSourceText = [IO.File]::ReadAllText($backendSource.FullName)
        if ($backendSourceText.Contains('CREATE DATABASE', [StringComparison]::Ordinal)) {
            foreach ($declaration in [regex]::Matches($backendSourceText, 'class\s+(?<name>[A-Za-z0-9_]+)')) { $databaseDeclaringTypes.Add($declaration.Groups['name'].Value) | Out-Null }
        }
        foreach ($callSite in [regex]::Matches($backendSourceText, '(?<name>[A-Za-z0-9_]+)\.CreateAsync\(')) { $callSiteTypes.Add($callSite.Groups['name'].Value) | Out-Null }
    }
    foreach ($callSiteType in $callSiteTypes) {
        if (-not $databaseDeclaringTypes.Contains($callSiteType)) { continue }
        $expectedFactory = "$callSiteType.CreateAsync"
        Assert-Contract ($discoveredFactories.Contains($expectedFactory)) "Inner-database factory discovery missed '$expectedFactory', which the test tree actually calls; the declaration scan and the call-site scan must agree."
    }
    $sharedGovernedFactory = 'PostgreSqlTestDatabase.CreateAsync'
    Assert-Contract ($discoveredFactories.Contains($sharedGovernedFactory)) 'Discovery must always find the shared governed PostgreSqlTestDatabase helper.'
    Assert-Contract ($discoveredFactories.Count -ge 5) 'Inner-database factory discovery must enumerate the test tree, not a hand-maintained list.'
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
    # 2026-08-13 裁决：lane 成员默认 test-owned——NERV-822 正把手写建库统一收敛到共享 helper，
    # runner 注入的成员库只保留给确有失败诊断价值的成员。因此不再断言"唯一 test-owned"，
    # 改为断言两种归属都还有成员（任一清空，它那半边契约就不再被行使）且每个成员都显式声明。
    $manifestDocument = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $activeMembers = @($manifestDocument.members | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) })
    $testOwnedCount = @($activeMembers | Where-Object { [string]::Equals([string]$_.databaseOwnership, 'test-owned', [StringComparison]::Ordinal) }).Count
    $runnerOwnedCount = @($activeMembers | Where-Object { [string]::Equals([string]$_.databaseOwnership, 'runner', [StringComparison]::Ordinal) }).Count
    Assert-Contract ($testOwnedCount -ge 1 -and $runnerOwnedCount -ge 1) 'Both ownership forms must stay represented; if one empties, its half of the contract stops being exercised.'
    Assert-Contract (($testOwnedCount + $runnerOwnedCount) -eq $activeMembers.Count) 'Every active member must declare one of the two governed ownership forms.'
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
    # Quality 同理：五个类共 21 条用例，只有 8 条是真实 PostgreSQL 证明。
    $qualityMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'quality-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($qualityMember.expectedTestIdentities).Count -eq 8) 'The Quality member must freeze exactly its eight governed PostgreSQL identities.'
    Assert-Contract (@($qualityMember.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$qualityMember.diagnosticSchemas[0], 'quality', [StringComparison]::Ordinal)) 'Quality business and CAP tables share one schema, which the member must declare.'
    foreach ($qualitySource in @(
            'QualityCalibrationRecordQueryTests.cs',
            'QualityCapaRedrivePostgresProfileTests.cs',
            'QualityInspectionTaskPostgresProfileTests.cs',
            'QualityReinspectionPostgresProfileTests.cs',
            'QualitySpcAnalysisTests.cs')) {
        $qualitySourcePath = Join-Path $repoRoot "backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/$qualitySource"
        Assert-Contract (Test-Path -LiteralPath $qualitySourcePath -PathType Leaf) "Quality lane source '$qualitySource' must exist."
        Assert-LaneOwnedDatabase -SourcePath $qualitySourcePath -InnerDatabaseFactory 'QualityPostgresTestDatabase.CreateAsync'
        Assert-LaneOwnedDatabase -SourcePath $qualitySourcePath -InnerDatabaseFactory 'TemporaryPostgresDatabase.CreateAsync'
    }
    # 三个直接 new DbContextOptionsBuilder 的 Quality 类必须把迁移历史表钉在 quality schema：
    # 默认落 public 时 ResetSchemaAsync 删不掉它，下一条用例的 MigrateAsync 会以为迁移已应用而静默不建表。
    # 只扫 InspectionTask 一个文件会留下盲区：SpcAnalysis 的 CreatePostgresProvider 与 Calibration 的
    # refused 探针也各有一处裸 builder（都已钉，但写的是 "quality" 字面量）。契约因此覆盖全部五个
    # Quality lane 源，正则同时接受常量与字面量两种钉法。
    $qualityPinnedBuilders = 0
    foreach ($qualitySource in @(
            'QualityCalibrationRecordQueryTests.cs',
            'QualityCapaRedrivePostgresProfileTests.cs',
            'QualityInspectionTaskPostgresProfileTests.cs',
            'QualityReinspectionPostgresProfileTests.cs',
            'QualitySpcAnalysisTests.cs')) {
        $qualitySourceText = [IO.File]::ReadAllText((Join-Path $repoRoot "backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/$qualitySource"))
        $historyOverrides = ([regex]::Matches($qualitySourceText, 'MigrationsHistoryTable\("__EFMigrationsHistory", (?:QualityFacts\.Schema|"quality")\)')).Count
        $rawNpgsqlBuilders = ([regex]::Matches($qualitySourceText, 'UseNpgsql\(')).Count
        Assert-Contract ($historyOverrides -eq $rawNpgsqlBuilders) "Every raw DbContext option builder in '$qualitySource' must pin __EFMigrationsHistory to the quality schema; observed $rawNpgsqlBuilders builders and $historyOverrides pinned."
        $qualityPinnedBuilders += $historyOverrides
    }
    Assert-Contract ($qualityPinnedBuilders -eq 5) 'The Quality lane sources must keep exactly their five pinned raw builders; a new unpinned one silently reintroduces the public-schema history table.'

    $telemetryMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'industrialtelemetry-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($telemetryMember.expectedTestIdentities).Count -eq 7) 'The IndustrialTelemetry member must freeze exactly its seven governed PostgreSQL identities.'
    Assert-Contract (@($telemetryMember.diagnosticSchemas).Count -eq 1 -and [string]::Equals([string]$telemetryMember.diagnosticSchemas[0], 'industrial_telemetry', [StringComparison]::Ordinal)) 'IndustrialTelemetry business and CAP tables share one schema, which the member must declare.'
    Assert-MethodScopedFilter -Member $telemetryMember
    Assert-MethodScopedFilter -Member $qualityMember
    # MES：六个类共 19 条用例，只有 11 条是真实 PostgreSQL 证明；CAP 的原生存储表落在独立 cap schema，
    # 业务表与 EF 侧 cap_* 表落在 mes schema，两者都必须声明才能在失败时留下完整诊断。
    $mesMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'mes-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($mesMember.expectedTestIdentities).Count -eq 11) 'The MES member must freeze exactly its eleven governed PostgreSQL identities.'
    Assert-Contract ([string]::Equals((@($mesMember.diagnosticSchemas) -join ','), 'mes,cap', [StringComparison]::Ordinal)) 'The MES member must declare both the mes schema and the native CAP storage schema.'
    Assert-MethodScopedFilter -Member $mesMember
    foreach ($mesSource in @(
            'MesCapSubscriptionTests.cs',
            'MesSchedulePlanProvenancePostgresTests.cs',
            'RushWorkOrderHttpPostgresTests.cs',
            'SkuDisabledConsumerTests.cs',
            'TelemetryProductionReportCandidatePostgresTests.cs',
            'WorkOrderCapitalizationConcurrencyPostgresTests.cs')) {
        $mesSourcePath = Join-Path $repoRoot "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/$mesSource"
        Assert-Contract (Test-Path -LiteralPath $mesSourcePath -PathType Leaf) "MES lane source '$mesSource' must exist."
        Assert-LaneOwnedDatabase -SourcePath $mesSourcePath -InnerDatabaseFactory 'PostgreSqlTestDatabase.CreateAsync'
        Assert-LaneOwnedDatabase -SourcePath $mesSourcePath -InnerDatabaseFactory 'MesPostgreSqlTestSettings.CreateDatabaseAsync'
    }
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

    # WMS：五个类共 20 条用例，只有 9 条是真实 PostgreSQL 证明，因此 filter 逐条精确到方法。
    # 归属 test-owned：NERV-822③ 的 #1563 已把这五个类的手写建库收敛到共享 PostgreSqlTestDatabase，
    # lane 因此只证明执行数与冻结身份，不声称能在成员数据库里留下诊断。
    $wmsMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'wms-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($wmsMember.expectedTestIdentities).Count -eq 9) 'The WMS member must freeze exactly its nine governed PostgreSQL identities.'
    Assert-Contract ([string]::Equals([string]$wmsMember.databaseOwnership, 'test-owned', [StringComparison]::Ordinal)) 'WMS tests own governed temporary databases per NERV-822, so the member must be registered as test-owned.'
    Assert-MethodScopedFilter -Member $wmsMember
    foreach ($wmsSource in @(
            'WarehouseTaskActionConcurrencyPostgresTests.cs',
            'WcsDispatchConcurrencyPostgresTests.cs',
            'WmsQualityInspectionGateConsumerTests.cs',
            'WmsShortPickBackorderTests.cs',
            'WmsWorkAssignmentMigrationPostgresTests.cs')) {
        $wmsSourcePath = Join-Path $repoRoot "backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/$wmsSource"
        Assert-Contract (Test-Path -LiteralPath $wmsSourcePath -PathType Leaf) "WMS lane source '$wmsSource' must exist."
        $wmsSourceText = [IO.File]::ReadAllText($wmsSourcePath)
        Assert-Contract ($wmsSourceText.Contains('PostgreSqlTestDatabase.CreateAsync', [StringComparison]::Ordinal)) "WMS lane source '$wmsSource' must build its database through the governed shared helper."
        Assert-Contract ($wmsSourceText -cnotmatch '"[^"\r\n]*CREATE DATABASE') "WMS lane source '$wmsSource' must not hand-roll CREATE DATABASE; NERV-822 converged these files onto the shared helper."
    }

    # 「其余服务（…）仍属于拆解③后续批次」这句已经三次把已接入的服务写回未接入列表（#1553 的 Quality、
    # #1555 的 Quality/IndustrialTelemetry、#1557 的 WMS）。只改文字会让它第四次回潮，因此把它变成门禁：
    # 该句列出的服务集合与 manifest 里 active 成员的 service 集合，交集必须为空。
    function Assert-PendingServiceListExcludesLaneMembers([string]$ReadinessPath, [object[]]$ActiveMembers) {
        $readiness = [IO.File]::ReadAllText($ReadinessPath)
        $sentence = [regex]::Match($readiness, '其余服务（(?<list>[^）]*)）仍属于拆解③后续批次')
        if (-not $sentence.Success) {
            # 全部接入后该句合法消失，但必须换成同样可核的收尾表述，否则"没有待接入服务"就无从证伪。
            if ($readiness.Contains('拆解③登记的服务至此全部接入', [StringComparison]::Ordinal)) { return }
            throw 'The readiness narrative must either name the pending services or state that none remain, so the gate has something to check.'
        }
        $pendingServices = @($sentence.Groups['list'].Value -split '、' | ForEach-Object { $_.Replace(' 等', '').Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($activeMember in $ActiveMembers) {
            foreach ($pendingService in $pendingServices) {
                if ([string]::Equals($pendingService, [string]$activeMember.service, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Service '$pendingService' is already an active lane member but the readiness narrative still lists it as pending."
                }
            }
        }
    }
    Assert-PendingServiceListExcludesLaneMembers -ReadinessPath (Join-Path $repoRoot 'docs/architecture/implementation-readiness.md') -ActiveMembers $activeMembers
    $regressedReadinessPath = Join-Path $fixtureRoot 'regressed-readiness.md'
    [IO.File]::WriteAllText($regressedReadinessPath, '其余服务（WMS、ERP、DemandPlanning 等）仍属于拆解③后续批次。', [Text.UTF8Encoding]::new($false))
    $pendingListRejected = $false
    try { Assert-PendingServiceListExcludesLaneMembers -ReadinessPath $regressedReadinessPath -ActiveMembers $activeMembers } catch { $pendingListRejected = $_.Exception.Message.Contains('still lists it as pending', [StringComparison]::Ordinal) }
    Assert-Contract $pendingListRejected 'Listing an already-onboarded service as pending must fail closed.'
    # runner 形态必须留一根钉：MasterData 是裁决原文里 runner 的动机样本（失败时要留 CAP outbox 状态），
    # 钉住它，"runner 半边契约仍被行使"才不是一句空话。
    $masterDataOwnership = @($activeMembers | Where-Object { [string]::Equals([string]$_.id, 'masterdata-postgres-profile', [StringComparison]::Ordinal) })
    Assert-Contract ($masterDataOwnership.Count -eq 1 -and [string]::Equals([string]$masterDataOwnership[0].databaseOwnership, 'runner', [StringComparison]::Ordinal)) 'MasterData must stay runner-owned; it is the decision''s worked example for keeping failure diagnostics.'

    # 第八批三成员：DemandPlanning 走 2026-08-13 裁决的默认归属（test-owned，NERV-822 的 #1565 已把
    # 该文件三条用例与 redis-cap 用例一并收敛到共享 PostgreSqlTestDatabase）；ERP 与跨业务 Acceptance
    # 按裁决的例外判据保持 runner——判据是失败诊断价值：Acceptance 的终局跨四个 schema，必须能在成员
    # 数据库里看到。ERP 本就没有手写建库；Acceptance 的手写建库（内嵌 TemporaryPostgresDatabase）由本批删除。
    $demandPlanningMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'demandplanning-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($demandPlanningMember.expectedTestIdentities).Count -eq 3) 'The DemandPlanning member must freeze exactly its three PostgreSQL identities.'
    Assert-Contract ([string]::Equals([string]$demandPlanningMember.databaseOwnership, 'test-owned', [StringComparison]::Ordinal)) 'DemandPlanning runs on governed temporary databases, so the member must be test-owned.'
    Assert-MethodScopedFilter -Member $demandPlanningMember
    $demandPlanningSourcePath = Join-Path $repoRoot 'backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs'
    $demandPlanningSource = [IO.File]::ReadAllText($demandPlanningSourcePath)
    Assert-Contract ($demandPlanningSource.Contains('PostgreSqlTestDatabase.CreateAsync', [StringComparison]::Ordinal)) 'DemandPlanning lane tests must build their databases through the governed shared helper.'
    Assert-Contract ($demandPlanningSource -cnotmatch '"[^"\r\n]*CREATE DATABASE') 'DemandPlanning lane tests must not hand-roll CREATE DATABASE.'
    foreach ($frozenDemandPlanningIdentity in @($demandPlanningMember.expectedTestIdentities)) {
        Assert-Contract (-not ([string]$frozenDemandPlanningIdentity).Contains('.Redis_cap_', [StringComparison]::Ordinal)) 'The Redis/CAP transport identities stay owned by the redis-cap lane, not by postgres.'
    }
    $redisCapManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/redis-cap-test-lane.json') -Raw | ConvertFrom-Json -Depth 20
    $redisCapIdentities = [Collections.Generic.HashSet[string]]::new([string[]]@($redisCapManifest.members | ForEach-Object { @($_.expectedTestIdentities) }), [StringComparer]::Ordinal)
    foreach ($frozenDemandPlanningIdentity in @($demandPlanningMember.expectedTestIdentities)) {
        $frozenIdentityKey = [string]$frozenDemandPlanningIdentity
        Assert-Contract (-not $redisCapIdentities.Contains($frozenIdentityKey)) 'No identity may be owned by both the postgres and redis-cap lanes.'
    }
    $erpMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'erp-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($erpMember.expectedTestIdentities).Count -eq 4) 'The ERP member must freeze exactly its four PostgreSQL identities.'
    Assert-Contract ([string]::Equals([string]$erpMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) 'ERP keeps runner-owned databases for failure diagnostics.'
    $acceptanceMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId 'acceptance-postgres-profile' -RepositoryRoot $repoRoot
    Assert-Contract (@($acceptanceMember.expectedTestIdentities).Count -eq 3) 'The cross-service acceptance member must freeze exactly its three PostgreSQL identities.'
    Assert-Contract ([string]::Equals((@($acceptanceMember.diagnosticSchemas) -join ','), 'industrial_telemetry,inventory,maintenance,wms', [StringComparison]::Ordinal)) 'The cross-service acceptance member must declare every schema its scenarios migrate.'
    Assert-Contract ([string]::Equals([string]$acceptanceMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) 'The cross-service acceptance member keeps runner-owned databases so its four-schema end state stays diagnosable.'
    Assert-MethodScopedFilter -Member $acceptanceMember
    foreach ($runnerOwnedSource in @(
            'backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/BusinessPartnerChangedPostgresAcceptanceTests.cs',
            'backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpCostAccountingPostgresAcceptanceTests.cs',
            'backend/tests/Nerv.IIP.Business.Acceptance.Tests/RuntimeHoursMaintenancePostgresAcceptanceTests.cs',
            'backend/tests/Nerv.IIP.Business.Acceptance.Tests/WmsInventoryRpcIdempotencyAcceptanceTests.cs')) {
        $runnerOwnedSourcePath = Join-Path $repoRoot $runnerOwnedSource
        Assert-Contract (Test-Path -LiteralPath $runnerOwnedSourcePath -PathType Leaf) "Lane source '$runnerOwnedSource' must exist."
        $runnerOwnedSourceText = [IO.File]::ReadAllText($runnerOwnedSourcePath)
        Assert-Contract ($runnerOwnedSourceText -cnotmatch '"[^"\r\n]*CREATE DATABASE') "Lane source '$runnerOwnedSource' must not hand-roll CREATE DATABASE; the runner owns the member database."
        Assert-LaneOwnedDatabase -SourcePath $runnerOwnedSourcePath -InnerDatabaseFactory 'PostgreSqlTestDatabase.CreateAsync'
    }
    # 跨业务 acceptance 用 EnsureCreatedAsync 会在共享成员库上直接跳过建表（库已存在），必须走迁移。
    $runtimeHoursSource = [IO.File]::ReadAllText((Join-Path $repoRoot 'backend/tests/Nerv.IIP.Business.Acceptance.Tests/RuntimeHoursMaintenancePostgresAcceptanceTests.cs'))
    Assert-Contract (-not $runtimeHoursSource.Contains('EnsureCreatedAsync(', [StringComparison]::Ordinal)) 'Lane members must migrate rather than EnsureCreated, which silently skips schema creation on an existing member database.'
    Assert-Contract ($runtimeHoursSource.Contains('MigrateAsync(', [StringComparison]::Ordinal)) 'The cross-service acceptance member must create its schemas through migrations.'

    # 逐成员、逐冻结身份地把"先重置再迁移"和"重置用 CASCADE"变成门禁，而不是靠每个作者自觉。
    $resetDeclaringSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($governedMemberId in $script:GovernedPostgresMemberIds) {
        $governedMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId $governedMemberId -RepositoryRoot $repoRoot
        $projectDirectory = Split-Path -Parent (Join-Path $repoRoot ([string]$governedMember.project))
        # 重置不变量只适用于 runner 归属：test-owned 成员每条用例自建临时库，本来就从零开始，
        # 它的对应不变量是"必须断言跑在自有库里"，由上面的 AppHub 断言覆盖。
        if (-not [string]::Equals([string]$governedMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) { continue }
        foreach ($frozenIdentity in @($governedMember.expectedTestIdentities)) {
            $methodSeparator = ([string]$frozenIdentity).LastIndexOf('.', [StringComparison]::Ordinal)
            $frozenClass = ([string]$frozenIdentity).Substring(0, $methodSeparator)
            $frozenMethod = ([string]$frozenIdentity).Substring($methodSeparator + 1)
            $frozenSourcePath = Join-Path $projectDirectory ("$($frozenClass.Substring($frozenClass.LastIndexOf('.', [StringComparison]::Ordinal) + 1)).cs")
            Assert-Contract (Test-Path -LiteralPath $frozenSourcePath -PathType Leaf) "Frozen identity '$frozenIdentity' must live in '$frozenSourcePath'."
            Assert-FrozenIdentityResetsSchema -SourcePath $frozenSourcePath -MethodName $frozenMethod
            # 冻结用例的方法体里不得出现任何已穷举的内层库工厂：换一个工厂名就绕过去的漏洞，
            # 由"从行为穷举工厂清单 + 逐方法体检查"两条一起堵死。
            $frozenBody = Get-NervCSharpMethodBody -Source ([IO.File]::ReadAllText($frozenSourcePath)) -MethodName $frozenMethod
            foreach ($innerDatabaseFactory in $script:InnerDatabaseFactories) {
                if ($frozenBody.Contains($innerDatabaseFactory, [StringComparison]::Ordinal)) {
                    throw "Frozen identity '$frozenMethod' must not create an inner database ('$innerDatabaseFactory') the lane cannot diagnose or clean."
                }
            }
            # 能力串兜底：最可能的回潮路径是从 git 历史把刚删掉的 helper 抄回本文件，那样工厂名可以是任何
            # 新名字，但建库能力本身逃不掉 CREATE DATABASE，而 lane 用例没有任何合法理由自己建库。
            if ($frozenBody -cmatch '"[^"\r\n]*CREATE DATABASE') {
                throw "Frozen identity '$frozenMethod' must not issue CREATE DATABASE; the runner owns the member database."
            }
        }
        # 只扫本 lane 拥有的文件：冻结身份所在的类，以及该成员的 *PostgresLaneDatabase 帮助类。
        # 同目录下的规模种子文件（归 NERV-677）有自己的生命周期，不受本契约约束。
        $memberOwnedSources = @(
            @($governedMember.expectedTestIdentities | ForEach-Object {
                $ownedClass = ([string]$_).Substring(0, ([string]$_).LastIndexOf('.', [StringComparison]::Ordinal))
                Join-Path $projectDirectory ("$($ownedClass.Substring($ownedClass.LastIndexOf('.', [StringComparison]::Ordinal) + 1)).cs")
            })
            @(Get-ChildItem -LiteralPath $projectDirectory -Filter '*PostgresLaneDatabase.cs' -File | ForEach-Object { $_.FullName })
        )
        foreach ($memberSource in $memberOwnedSources) {
            $memberSourceText = [IO.File]::ReadAllText($memberSource)
            if ($memberSourceText -cmatch 'Task (?:Reset|Drop)[A-Za-z]*SchemaAsync\s*\(') { $resetDeclaringSources.Add($memberSource) | Out-Null }
            # 帮助类文件整体属于 lane，可以整文件扫；测试类文件不行——例如
            # ErpSalesOrderDemandConsumerTests.cs 同文件里住着 redis-cap lane 的两条用例，
            # 它们合法使用内层库。测试类的禁用范围因此落在冻结用例的方法体上（见下）。
            if ($memberSource.EndsWith('PostgresLaneDatabase.cs', [StringComparison]::Ordinal)) {
                foreach ($innerDatabaseFactory in $script:InnerDatabaseFactories) {
                    Assert-LaneOwnedDatabase -SourcePath $memberSource -InnerDatabaseFactory $innerDatabaseFactory
                }
                Assert-NoDatabaseCreationStatement -SourcePath $memberSource
            }
        }
    }
    Assert-Contract ($resetDeclaringSources.Count -ge 10) 'Every governed member must own a schema-reset implementation that this contract can inspect.'
    # 逐成员闭合：总量下界挡不住"某成员的重置实现藏在既非冻结身份类、也非 *PostgresLaneDatabase.cs 的文件里"，
    # 那种情况下它会逃过 CASCADE 形态检查而总数仍然达标。
    foreach ($runnerMemberId in @($script:GovernedPostgresMemberIds)) {
        $runnerMember = Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId $runnerMemberId -RepositoryRoot $repoRoot
        if (-not [string]::Equals([string]$runnerMember.databaseOwnership, 'runner', [StringComparison]::Ordinal)) { continue }
        $runnerProjectDirectory = Split-Path -Parent (Join-Path $repoRoot ([string]$runnerMember.project))
        $memberResetSources = @($resetDeclaringSources | Where-Object { $_.StartsWith($runnerProjectDirectory, [StringComparison]::Ordinal) })
        Assert-Contract ($memberResetSources.Count -ge 1) "Member '$runnerMemberId' must own at least one inspectable schema-reset implementation."
    }
    foreach ($resetDeclaringSource in $resetDeclaringSources) { Assert-LaneResetDropsCascade -SourcePath $resetDeclaringSource }

    $missingResetSourcePath = Join-Path $fixtureRoot 'missing-reset-source.cs'
    [IO.File]::WriteAllText($missingResetSourcePath, "public async Task Postgres_forgets_to_reset()`n{`n    await dbContext.Database.MigrateAsync();`n}`n", [Text.UTF8Encoding]::new($false))
    $missingResetRejected = $false
    try { Assert-FrozenIdentityResetsSchema -SourcePath $missingResetSourcePath -MethodName 'Postgres_forgets_to_reset' } catch { $missingResetRejected = $_.Exception.Message.Contains('must reset its schema before migrating', [StringComparison]::Ordinal) }
    Assert-Contract $missingResetRejected 'A frozen identity that skips the schema reset must fail closed.'
    $presentResetSourcePath = Join-Path $fixtureRoot 'present-reset-source.cs'
    [IO.File]::WriteAllText($presentResetSourcePath, "public async Task Postgres_resets()`n{`n    await ResetSchemaAsync();`n    await dbContext.Database.MigrateAsync();`n}`n", [Text.UTF8Encoding]::new($false))
    Assert-FrozenIdentityResetsSchema -SourcePath $presentResetSourcePath -MethodName 'Postgres_resets'
    $noCascadeSourcePath = Join-Path $fixtureRoot 'no-cascade-reset-source.cs'
    [IO.File]::WriteAllText($noCascadeSourcePath, "internal static async Task ResetSchemaAsync()`n{`n    command.CommandText = `$`"DROP SCHEMA IF EXISTS {quotedSchema}`";`n}`n", [Text.UTF8Encoding]::new($false))
    $noCascadeRejected = $false
    try { Assert-LaneResetDropsCascade -SourcePath $noCascadeSourcePath } catch { $noCascadeRejected = $_.Exception.Message.Contains('IF EXISTS and CASCADE', [StringComparison]::Ordinal) }
    Assert-Contract $noCascadeRejected 'A schema reset without CASCADE must fail closed.'

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

    # 闭合契约：政策里每一条 requiredLane: postgres 的身份，都必须落在 manifest 的成员身份（active 或
    # deferred）或显式的规模种子豁免里。穷举从政策的类型集来，不从审核点名来——否则第三个"漏网"类
    # 可以无声出现（#1510 的教训）。
    $policyDocument = Get-Content -LiteralPath (Join-Path $repoRoot 'scripts/test-evidence-policy.json') -Raw | ConvertFrom-Json -Depth 20
    $manifestDocument = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    $manifestIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($manifestMember in @($manifestDocument.members)) {
        foreach ($manifestIdentity in @($manifestMember.expectedTestIdentities)) { $manifestIdentities.Add([string]$manifestIdentity) | Out-Null }
    }
    $exemptions = @($manifestDocument.scaleSeedExemptions)
    $exemptIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($exemption in $exemptions) {
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$exemption.owner) -and -not [string]::IsNullOrWhiteSpace([string]$exemption.reason)) 'Every scale-seed exemption must name its owner and reason.'
        Assert-Contract (-not $manifestIdentities.Contains([string]$exemption.identity)) "Identity '$($exemption.identity)' cannot be both a lane member identity and an exemption."
        $exemptIdentities.Add([string]$exemption.identity) | Out-Null
    }
    $uncoveredIdentities = @(
        foreach ($policyRule in @($policyDocument.rules | Where-Object { [string]::Equals([string]$_.requiredLane, 'postgres', [StringComparison]::Ordinal) })) {
            foreach ($policyIdentity in @($policyRule.testIdentities)) {
                if (-not $manifestIdentities.Contains([string]$policyIdentity) -and -not $exemptIdentities.Contains([string]$policyIdentity)) { [string]$policyIdentity }
            }
        }
    )
    Assert-Contract ($uncoveredIdentities.Count -eq 0) "Every policy identity with requiredLane 'postgres' must be a manifest member identity or a declared scale-seed exemption; uncovered: $($uncoveredIdentities -join ', ')."
    # 变异对照：把一条已覆盖身份从 manifest 与豁免里同时摘掉，闭合检查必须点名它。
    $mutatedIdentity = 'Nerv.IIP.Business.Wms.Web.Tests.WcsDispatchConcurrencyPostgresTests.Concurrent_wcs_claim_inserts_keep_one_owner_and_classify_the_loser'
    Assert-Contract ($manifestIdentities.Contains($mutatedIdentity)) 'The closure mutation fixture must start from a covered identity.'
    $mutatedIdentities = [Collections.Generic.HashSet[string]]::new([string[]]@($manifestIdentities), [StringComparer]::Ordinal)
    $mutatedIdentities.Remove($mutatedIdentity) | Out-Null
    $mutatedUncovered = @(
        foreach ($policyRule in @($policyDocument.rules | Where-Object { [string]::Equals([string]$_.requiredLane, 'postgres', [StringComparison]::Ordinal) })) {
            foreach ($policyIdentity in @($policyRule.testIdentities)) {
                if (-not $mutatedIdentities.Contains([string]$policyIdentity) -and -not $exemptIdentities.Contains([string]$policyIdentity)) { [string]$policyIdentity }
            }
        }
    )
    Assert-Contract ($mutatedUncovered.Count -eq 1 -and [string]::Equals($mutatedUncovered[0], $mutatedIdentity, [StringComparison]::Ordinal)) 'Dropping a governed identity must be reported by the closure contract.'
    # deferred 登记必须写明理由，且不得被 runner 选中执行。
    foreach ($deferredMember in @($manifestDocument.members | Where-Object { [string]::Equals([string]$_.status, 'deferred', [StringComparison]::Ordinal) })) {
        Assert-Contract (-not [string]::IsNullOrWhiteSpace([string]$deferredMember.deferredReason)) "Deferred member '$($deferredMember.id)' must record why it cannot join the lane."
        $selectedMemberIdSet = [Collections.Generic.HashSet[string]]::new([string[]]@($script:GovernedPostgresMemberIds), [StringComparer]::Ordinal)
        Assert-Contract (-not $selectedMemberIdSet.Contains([string]$deferredMember.id)) "Deferred member '$($deferredMember.id)' must not be selected by the hosted job."
        $deferredRejected = $false
        try { Import-NervPostgresTestLaneMember -ManifestPath $manifestPath -MemberId ([string]$deferredMember.id) -RepositoryRoot $repoRoot | Out-Null }
        catch { $deferredRejected = $_.Exception.Message.Contains('is not active', [StringComparison]::Ordinal) }
        Assert-Contract $deferredRejected "The runner must refuse to execute deferred member '$($deferredMember.id)'."
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
& (Join-Path $PSScriptRoot 'postgres-test-database-consumers.Tests.ps1')
Write-Output 'PostgreSQL test lane contract tests passed.'
