using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.Sqlite;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;
using Nerv.IIP.Business.Mes.Infrastructure.Migrations;
using Nerv.IIP.Coding;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesSchemaConventionTests
{
    [Fact]
    public void Operation_task_schedule_release_provenance_columns_are_explicit()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.Model.FindEntityType(typeof(OperationTask))!;

        Assert.Equal("schedule_plan_id", entity.FindProperty(nameof(OperationTask.SchedulePlanId))!.GetColumnName());
        Assert.Equal("schedule_release_revision", entity.FindProperty(nameof(OperationTask.ScheduleReleaseRevision))!.GetColumnName());
    }

    [Fact]
    public void Operation_task_required_skill_snapshot_is_nullable_and_bounded()
    {
        using var fixture = CreateFixture();
        var property = fixture.DbContext.Model.FindEntityType(typeof(OperationTask))!
            .FindProperty(nameof(OperationTask.RequiredSkillCode))!;

        Assert.Equal("required_skill_code", property.GetColumnName());
        Assert.Equal(100, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void Work_order_version_is_a_positive_postgresql_concurrency_token()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkOrder))!;
        var version = entity.FindProperty(nameof(WorkOrder.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(1L, version.GetDefaultValue());
        Assert.Contains(entity.GetCheckConstraints(), x => x.Name == "ck_work_orders_version_positive");
    }

    [Fact]
    public async Task Work_order_transformation_uom_constraint_is_sqlite_creatable_and_uses_common_trim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var sqliteContext = new ApplicationDbContext(options, new NoopMediator()))
        {
            await sqliteContext.Database.EnsureCreatedAsync();
        }

        using var fixture = CreateFixture();
        var entity = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkOrderTransformationLine))!;
        var uomConstraint = Assert.Single(
            entity.GetCheckConstraints(),
            x => x.Name == "ck_work_order_transformation_lines_uom_present");

        Assert.Equal("trim(uom_code) <> ''", uomConstraint.Sql);
    }

    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        using var fixture = CreateFixture();
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            MesFacts.ServiceName,
            MesFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Material_issue_request_supplementary_fields_are_scope_safe_and_constrained()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MaterialIssueRequest))!;

        Assert.Equal("is_supplementary", entity.FindProperty(nameof(MaterialIssueRequest.IsSupplementary))!.GetColumnName());
        Assert.Equal("original_material_issue_request_no", entity.FindProperty(nameof(MaterialIssueRequest.OriginalMaterialIssueRequestNo))!.GetColumnName());
        Assert.Contains(entity.GetCheckConstraints(), x => x.Name == "ck_material_issue_requests_supplementary_source");
        Assert.Contains(entity.GetCheckConstraints(), x => x.Name == "ck_material_issue_requests_not_self_referential");

        var originalRequestForeignKey = Assert.Single(
            entity.GetForeignKeys(),
            x => x.GetConstraintName() == "fk_material_issue_requests_original_request");
        Assert.Equal(
            [
                nameof(MaterialIssueRequest.OrganizationId),
                nameof(MaterialIssueRequest.EnvironmentId),
                nameof(MaterialIssueRequest.OriginalMaterialIssueRequestNo),
                nameof(MaterialIssueRequest.WorkOrderId),
                nameof(MaterialIssueRequest.MaterialId),
            ],
            originalRequestForeignKey.Properties.Select(x => x.Name).ToArray());
        Assert.Equal(
            [
                nameof(MaterialIssueRequest.OrganizationId),
                nameof(MaterialIssueRequest.EnvironmentId),
                nameof(MaterialIssueRequest.RequestNo),
                nameof(MaterialIssueRequest.WorkOrderId),
                nameof(MaterialIssueRequest.MaterialId),
            ],
            originalRequestForeignKey.PrincipalKey.Properties.Select(x => x.Name).ToArray());
    }

    // Contract: Governance. Authority: Issue #2246 acceptance 4 and the MES database schema catalog; provider behavior is covered separately on PostgreSQL.
    [Fact]
    public void Material_substitute_snapshot_and_issue_audit_columns_are_explicit()
    {
        using var fixture = CreateFixture();
        var model = fixture.DbContext.GetService<IDesignTimeModel>().Model;
        var requirement = model.FindEntityType(typeof(MaterialRequirement))!;
        var issue = model.FindEntityType(typeof(MaterialIssueRequest))!;

        var substituteCandidates = requirement.FindProperty(nameof(MaterialRequirement.SubstituteMaterialIdsJson))!;
        Assert.Equal("substitute_material_ids_json", substituteCandidates.GetColumnName());
        Assert.False(substituteCandidates.IsNullable);
        Assert.Null(substituteCandidates.GetMaxLength());
        Assert.Equal("text", substituteCandidates.GetColumnType());
        Assert.Equal("[]", substituteCandidates.GetDefaultValue());
        Assert.Null(substituteCandidates.GetDefaultValueSql());
        var issueAudit = issue.FindProperty(nameof(MaterialIssueRequest.SubstitutedMaterialId))!;
        Assert.Equal("substituted_material_id", issueAudit.GetColumnName());
        Assert.True(issueAudit.IsNullable);
        Assert.Equal(100, issueAudit.GetMaxLength());
        Assert.Equal("character varying(100)", issueAudit.GetColumnType());
        Assert.Null(issueAudit.GetDefaultValue());
        Assert.Null(issueAudit.GetDefaultValueSql());
    }

    // Contract: Governance. Authority: Issue #2246 acceptance 4 and the MES database schema catalog;
    // the migration operation must match the approved nullable, bounded PostgreSQL audit column without a default.
    [Fact]
    public void Material_substitute_foundation_migration_preserves_issue_audit_column_facets()
    {
        var migration = new AddMesMaterialSubstituteSnapshotFoundation();
        var upBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddMesMaterialSubstituteSnapshotFoundation)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [upBuilder]);

        var issueAudit = Assert.Single(
            upBuilder.Operations.OfType<AddColumnOperation>(),
            x => x.Name == "substituted_material_id");
        Assert.Equal(MesFacts.Schema, issueAudit.Schema);
        Assert.Equal("material_issue_requests", issueAudit.Table);
        Assert.Equal(typeof(string), issueAudit.ClrType);
        Assert.Equal("character varying(100)", issueAudit.ColumnType);
        Assert.Equal(100, issueAudit.MaxLength);
        Assert.True(issueAudit.IsNullable);
        Assert.Null(issueAudit.DefaultValue);
        Assert.Null(issueAudit.DefaultValueSql);
    }

    // Contract: Governance. Authority: Issue #2246 acceptance 4 and the MES database schema catalog;
    // the migration operation must match the approved required, unbounded PostgreSQL text snapshot with an empty-array default.
    [Fact]
    public void Material_substitute_foundation_migration_preserves_candidate_snapshot_column_facets()
    {
        var migration = new AddMesMaterialSubstituteSnapshotFoundation();
        var upBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddMesMaterialSubstituteSnapshotFoundation)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [upBuilder]);

        var substituteCandidates = Assert.Single(
            upBuilder.Operations.OfType<AddColumnOperation>(),
            x => x.Name == "substitute_material_ids_json");
        Assert.Equal(MesFacts.Schema, substituteCandidates.Schema);
        Assert.Equal("material_requirements", substituteCandidates.Table);
        Assert.Equal(typeof(string), substituteCandidates.ClrType);
        Assert.Equal("text", substituteCandidates.ColumnType);
        Assert.Null(substituteCandidates.MaxLength);
        Assert.False(substituteCandidates.IsNullable);
        Assert.Equal("[]", substituteCandidates.DefaultValue);
        Assert.Null(substituteCandidates.DefaultValueSql);
    }

    // Contract: Governance. Authority: Issue #2246 acceptance 4 and docs/architecture/database-schema-conventions.md "权威来源"/"迁移与发布";
    // the migration must update the approved Released AutoRebind provenance comment and restore the prior contract on rollback.
    [Fact]
    public void Material_substitute_foundation_migration_updates_snapshot_provenance_comment_symmetrically()
    {
        const string originalComment =
            "Production version id whose material requirement snapshot outcome was proved; it must match the current work order version.";
        const string releasedRebindComment =
            "Production version provenance for the frozen material requirement outcome; it normally matches the current work order version, while a released engineering-change auto-rebind retains the release version.";
        var migration = new AddMesMaterialSubstituteSnapshotFoundation();

        var upBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddMesMaterialSubstituteSnapshotFoundation)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [upBuilder]);

        var upgradedComment = Assert.Single(upBuilder.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(MesFacts.Schema, upgradedComment.Schema);
        Assert.Equal("work_orders", upgradedComment.Table);
        Assert.Equal("material_requirement_snapshot_production_version_id", upgradedComment.Name);
        Assert.Equal(releasedRebindComment, upgradedComment.Comment);
        Assert.Equal(originalComment, upgradedComment.OldColumn.Comment);

        var downBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddMesMaterialSubstituteSnapshotFoundation)
            .GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [downBuilder]);

        var restoredComment = Assert.Single(downBuilder.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(originalComment, restoredComment.Comment);
        Assert.Equal(releasedRebindComment, restoredComment.OldColumn.Comment);
    }

    // Contract: Governance. Authority: Issue #2246 acceptance 4 and docs/architecture/database-schema-conventions.md "权威来源"/"迁移与发布";
    // the generated migration must follow existing MES migrations and carry the configuration-complete target model.
    [Fact]
    public void Material_substitute_foundation_migration_is_latest_and_targets_the_complete_mes_model()
    {
        const string releasedRebindComment =
            "Production version provenance for the frozen material requirement outcome; it normally matches the current work order version, while a released engineering-change auto-rebind retains the release version.";
        var foundationMigration = new AddMesMaterialSubstituteSnapshotFoundation();
        var foundationId = GetMigrationId(typeof(AddMesMaterialSubstituteSnapshotFoundation));

        Assert.True(
            string.CompareOrdinal(foundationId, GetMigrationId(typeof(AddMesCollaborativeLaborAllocation))) > 0,
            $"{foundationId} must sort after the collaborative labor migration.");
        Assert.True(
            string.CompareOrdinal(foundationId, GetMigrationId(typeof(AddMesOperationTaskRequiredSkillSnapshot))) > 0,
            $"{foundationId} must sort after the required-skill migration.");

        var targetModel = foundationMigration.TargetModel;
        Assert.NotNull(targetModel.FindEntityType(typeof(MaterialRequirement))!
            .FindProperty(nameof(MaterialRequirement.SubstituteMaterialIdsJson)));
        Assert.NotNull(targetModel.FindEntityType(typeof(MaterialIssueRequest))!
            .FindProperty(nameof(MaterialIssueRequest.SubstitutedMaterialId)));
        Assert.NotNull(targetModel.FindEntityType(typeof(OperationTask))!
            .FindProperty(nameof(OperationTask.RequiredSkillCode)));
        Assert.NotNull(targetModel.FindEntityType(typeof(OperationTaskParticipant)));
        Assert.NotNull(targetModel.FindEntityType(typeof(ProductionReportLaborAllocation)));
        Assert.NotNull(targetModel.FindEntityType(typeof(WorkOrderTransformation)));
        Assert.NotNull(targetModel.FindEntityType(typeof(WorkOrderTransformationLine)));
        Assert.Equal(
            releasedRebindComment,
            targetModel.FindEntityType(typeof(WorkOrder))!
                .FindProperty(nameof(WorkOrder.MaterialRequirementSnapshotProductionVersionId))!
                .GetComment());
    }

    [Fact]
    public void Mes_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(WorkOrder),
            typeof(WorkOrderTransformation),
            typeof(WorkOrderTransformationLine),
            typeof(MesEngineeringChangeWorkOrderImpact),
            typeof(OperationTask),
            typeof(ProductionReport),
            typeof(ProductionReportMaterialConsumption),
            typeof(OutputLotGenealogy),
            typeof(DefectRecord),
            typeof(QualityHoldContext),
            typeof(QualityHoldTransition),
            typeof(MaterialRequirement),
            typeof(MaterialIssueRequest),
            typeof(ScheduleResult),
            typeof(WorkCenterUnavailability),
            typeof(DeviceAssetWorkCenterMapping),
            typeof(FinishedGoodsReceiptRequest),
            typeof(ShiftHandover),
            typeof(CodeCounter),
            typeof(CodeIdempotencyKey),
            typeof(ProcessedIntegrationEvent),
            typeof(ScheduleReleaseWatermark),
            typeof(MesSkuAvailability),
        };

        var failures = new List<string>();
        Assert.Equal(MesFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, MesFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, MesFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.JsonColumnsHaveCompatibilityComments(
            fixture.DbContext,
            MesFacts.ServiceName,
            [
                new JsonColumnRule(typeof(ScheduleResult), nameof(ScheduleResult.AssignmentsJson)),
                new JsonColumnRule(typeof(ScheduleResult), nameof(ScheduleResult.AffectedWorkOrderIdsJson)),
                new JsonColumnRule(typeof(MaterialRequirement), nameof(MaterialRequirement.SubstituteMaterialIdsJson)),
            ]));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MesFacts.ServiceName, MesFacts.Schema));
        failures.AddRange(ForeignKeysAreConfigured(fixture.DbContext));
        failures.AddRange(IndexNamesAreExplicit(fixture.DbContext, businessEntities));
        failures.AddRange(MaterialConsumptionHasIdempotencyIndex(fixture.DbContext));
        failures.AddRange(ProductionReportReversalHasUniqueOriginalReportIndex(fixture.DbContext.Model));
        failures.AddRange(ProcessedIntegrationEventHasUniqueInboxIndex(fixture.DbContext.Model));
        failures.AddRange(QualityHoldTransitionHasGovernedIdempotencyIndex(fixture.DbContext.Model));
        failures.AddRange(OperationTaskHasScheduleProvenanceIndex(fixture.DbContext.Model));
        failures.AddRange(WorkOrderTransformationHasIdempotencyIndex(fixture.DbContext.Model));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyCollection<string> OperationTaskHasScheduleProvenanceIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(OperationTask));
        var found = entity?.GetIndexes().Any(index =>
            index.GetDatabaseName() == "ix_operation_tasks_scope_schedule_plan" &&
            index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(OperationTask.OrganizationId),
                nameof(OperationTask.EnvironmentId),
                nameof(OperationTask.SchedulePlanId),
            ])) == true;
        return found ? [] : ["MES: operation task schedule provenance index is missing."];
    }

    private static IReadOnlyCollection<string> WorkOrderTransformationHasIdempotencyIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(WorkOrderTransformation));
        var found = entity?.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_work_order_transformations_scope_idempotency" &&
            index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(WorkOrderTransformation.OrganizationId),
                nameof(WorkOrderTransformation.EnvironmentId),
                nameof(WorkOrderTransformation.IdempotencyKey),
            ])) == true;
        return found
            ? []
            : [$"{MesFacts.ServiceName}: work-order transformations require a scoped unique idempotency index."];
    }

    private static IReadOnlyCollection<string> QualityHoldTransitionHasGovernedIdempotencyIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(QualityHoldTransition));
        var found = entity?.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_quality_hold_transitions_scope_idempotency_kind" &&
            index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(QualityHoldTransition.OrganizationId),
                nameof(QualityHoldTransition.EnvironmentId),
                nameof(QualityHoldTransition.SourceService),
                nameof(QualityHoldTransition.SourceDocumentId),
                nameof(QualityHoldTransition.HoldCycleId),
                nameof(QualityHoldTransition.IdempotencyKey),
                nameof(QualityHoldTransition.EventKind),
            ])) == true;
        return found ? [] : ["MES: QualityHoldTransition governed idempotency unique index is missing."];
    }

    [Fact]
    public void Processed_integration_event_idempotency_migration_deduplicates_before_unique_index()
    {
        var migration = new UseIdempotencyKeyForProcessedIntegrationEvents();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(UseIdempotencyKeyForProcessedIntegrationEvents)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        AssertInboxDeduplicationBeforeUniqueIndex(migrationBuilder, MesFacts.Schema);
    }

    [Fact]
    public void Production_report_reversal_migration_creates_unique_original_report_index()
    {
        var migration = new AddMesProductionReportReversal();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddMesProductionReportReversal)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        var hasUniqueReversalIndex = migrationBuilder.Operations.Any(operation =>
            operation is CreateIndexOperation createIndexOperation &&
            createIndexOperation.Schema == MesFacts.Schema &&
            createIndexOperation.Table == "production_reports" &&
            createIndexOperation.Name == "ux_production_reports_scope_reversed_report_no" &&
            createIndexOperation.IsUnique &&
            createIndexOperation.Columns.SequenceEqual(["organization_id", "environment_id", "reversed_report_no"]));

        Assert.True(hasUniqueReversalIndex, $"{MesFacts.Schema}: reversal migration must create a unique original-report index.");
    }

    [Fact]
    public void Production_report_reversal_audit_migration_adds_nullable_bounded_actor_column()
    {
        var migration = new AddProductionReportReversalAudit();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddProductionReportReversalAudit)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        var column = Assert.IsType<AddColumnOperation>(Assert.Single(migrationBuilder.Operations));
        Assert.Equal(MesFacts.Schema, column.Schema);
        Assert.Equal("production_reports", column.Table);
        Assert.Equal("reversed_by", column.Name);
        Assert.Equal(100, column.MaxLength);
        Assert.True(column.IsNullable);
        Assert.False(string.IsNullOrWhiteSpace(column.Comment));
    }

    private static IReadOnlyCollection<string> ProcessedIntegrationEventHasUniqueInboxIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(ProcessedIntegrationEvent));
        if (entity is null)
        {
            return [$"{MesFacts.ServiceName}: missing processed integration event entity metadata."];
        }

        var hasUniqueIndex = entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_processed_integration_events_consumer_idempotency_key" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ProcessedIntegrationEvent.ConsumerName),
                nameof(ProcessedIntegrationEvent.IdempotencyKey),
            ]));

        return hasUniqueIndex
            ? []
            : [$"{MesFacts.ServiceName}: processed integration event inbox requires a unique consumer/idempotency key index."];
    }

    private static IReadOnlyCollection<string> ProductionReportReversalHasUniqueOriginalReportIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(ProductionReport));
        if (entity is null)
        {
            return [$"{MesFacts.ServiceName}: missing entity type {nameof(ProductionReport)}."];
        }

        var hasUniqueIndex = entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_production_reports_scope_reversed_report_no" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ProductionReport.OrganizationId),
                nameof(ProductionReport.EnvironmentId),
                nameof(ProductionReport.ReversedReportNo),
            ]));

        return hasUniqueIndex
            ? []
            : [$"{MesFacts.ServiceName}: production report reversal requires a unique organization/environment/reversed report index."];
    }

    private static void AssertInboxDeduplicationBeforeUniqueIndex(MigrationBuilder migrationBuilder, string schema)
    {
        var operations = migrationBuilder.Operations;
        var dedupeSqlIndex = OperationIndex(operations, operation =>
            operation is SqlOperation sqlOperation &&
            sqlOperation.Sql.Contains($"{schema}.processed_integration_events", StringComparison.Ordinal) &&
            sqlOperation.Sql.Contains("row_number() OVER", StringComparison.Ordinal) &&
            sqlOperation.Sql.Contains("PARTITION BY \"ConsumerName\", \"IdempotencyKey\"", StringComparison.Ordinal));
        var createUniqueIndexIndex = OperationIndex(operations, operation =>
            operation is CreateIndexOperation createIndexOperation &&
            createIndexOperation.Schema == schema &&
            createIndexOperation.Table == "processed_integration_events" &&
            createIndexOperation.Name == "ux_processed_integration_events_consumer_idempotency_key" &&
            createIndexOperation.IsUnique &&
            createIndexOperation.Columns.SequenceEqual(["ConsumerName", "IdempotencyKey"]));

        Assert.True(dedupeSqlIndex >= 0, $"{schema}: migration must remove historical duplicate processed inbox rows.");
        Assert.True(createUniqueIndexIndex >= 0, $"{schema}: migration must create the consumer/idempotency unique index.");
        Assert.True(dedupeSqlIndex < createUniqueIndexIndex, $"{schema}: migration must deduplicate before creating the unique index.");
    }

    private static int OperationIndex(IReadOnlyList<MigrationOperation> operations, Func<MigrationOperation, bool> predicate)
    {
        for (var index = 0; index < operations.Count; index++)
        {
            if (predicate(operations[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyCollection<string> ForeignKeysAreConfigured(ApplicationDbContext dbContext)
    {
        var model = dbContext.Model;
        var failures = new List<string>();
        AssertForeignKey(model, typeof(OperationTask), "fk_operation_tasks_work_orders", failures);
        AssertForeignKey(model, typeof(ProductionReport), "fk_production_reports_work_orders", failures);
        AssertForeignKey(model, typeof(ProductionReport), "fk_production_reports_operation_tasks", failures);
        AssertForeignKey(model, typeof(ProductionReportMaterialConsumption), "fk_report_material_consumptions_reports", failures);
        AssertForeignKey(model, typeof(OutputLotGenealogy), "fk_output_lot_genealogies_work_orders", failures);
        AssertForeignKey(model, typeof(OutputLotGenealogy), "fk_output_lot_genealogies_operation_tasks", failures);
        AssertForeignKey(model, typeof(OutputLotGenealogy), "fk_output_lot_genealogies_reports", failures);
        AssertForeignKey(model, typeof(DefectRecord), "fk_defect_records_work_orders", failures);
        AssertForeignKey(model, typeof(QualityHoldContext), "fk_quality_hold_contexts_work_orders", failures);
        AssertForeignKey(model, typeof(MaterialRequirement), "fk_material_requirements_work_orders", failures);
        AssertForeignKey(model, typeof(MaterialIssueRequest), "fk_material_issue_requests_work_orders", failures);
        AssertForeignKey(model, typeof(FinishedGoodsReceiptRequest), "fk_receipt_requests_work_orders", failures);
        AssertForeignKey(model, typeof(WorkOrderTransformationLine), "fk_work_order_transformation_lines_source_work_order", failures);
        AssertForeignKey(model, typeof(WorkOrderTransformationLine), "fk_work_order_transformation_lines_target_work_order", failures);
        AssertForeignKey(model, typeof(WorkOrderTransformationLine), "fk_work_order_transformation_lines_transformations", failures);
        return failures;
    }

    private static IReadOnlyCollection<string> IndexNamesAreExplicit(ApplicationDbContext dbContext, IEnumerable<Type> businessEntities)
    {
        var failures = new List<string>();
        foreach (var entityType in businessEntities.Select(x => dbContext.Model.FindEntityType(x)).OfType<IEntityType>())
        {
            foreach (var index in entityType.GetIndexes())
            {
                var databaseName = index.GetDatabaseName();
                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    failures.Add($"{MesFacts.ServiceName}: index on {entityType.ClrType.Name} is missing an explicit database name.");
                }
                else if (databaseName.Contains("~", StringComparison.Ordinal))
                {
                    failures.Add($"{MesFacts.ServiceName}: index '{databaseName}' appears truncated.");
                }
            }
        }

        return failures;
    }

    private static IReadOnlyCollection<string> MaterialConsumptionHasIdempotencyIndex(ApplicationDbContext dbContext)
    {
        var entity = dbContext.Model.FindEntityType(typeof(ProductionReportMaterialConsumption));
        if (entity is null)
        {
            return [$"{MesFacts.ServiceName}: missing entity type {nameof(ProductionReportMaterialConsumption)}."];
        }

        var hasUniqueIndex = entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_report_material_consumptions_report_material_lot");
        return hasUniqueIndex
            ? []
            : [$"{MesFacts.ServiceName}: production report material consumption facts require a unique report/material/lot index."];
    }

    private static void AssertForeignKey(IModel model, Type entityType, string constraintName, List<string> failures)
    {
        var entity = model.FindEntityType(entityType);
        if (entity is null)
        {
            failures.Add($"{MesFacts.ServiceName}: missing entity type {entityType.Name}.");
            return;
        }

        if (entity.GetForeignKeys().All(x => x.GetConstraintName() != constraintName))
        {
            failures.Add($"{MesFacts.ServiceName}: missing foreign key constraint '{constraintName}' on {entityType.Name}.");
        }
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddMesPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }

    private static string GetMigrationId(Type migrationType)
    {
        return ((MigrationAttribute)Attribute.GetCustomAttribute(migrationType, typeof(MigrationAttribute))!).Id;
    }
}
