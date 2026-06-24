using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;
using Nerv.IIP.Business.Mes.Infrastructure.Migrations;
using Nerv.IIP.Coding;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesSchemaConventionTests
{
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
    public void Mes_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(WorkOrder),
            typeof(OperationTask),
            typeof(ProductionReport),
            typeof(ProductionReportMaterialConsumption),
            typeof(DefectRecord),
            typeof(QualityHoldContext),
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
            ]));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MesFacts.ServiceName, MesFacts.Schema));
        failures.AddRange(ForeignKeysAreConfigured(fixture.DbContext));
        failures.AddRange(IndexNamesAreExplicit(fixture.DbContext, businessEntities));
        failures.AddRange(MaterialConsumptionHasIdempotencyIndex(fixture.DbContext));
        failures.AddRange(ProcessedIntegrationEventHasUniqueInboxIndex(fixture.DbContext.Model));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
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
        AssertForeignKey(model, typeof(DefectRecord), "fk_defect_records_work_orders", failures);
        AssertForeignKey(model, typeof(QualityHoldContext), "fk_quality_hold_contexts_work_orders", failures);
        AssertForeignKey(model, typeof(MaterialRequirement), "fk_material_requirements_work_orders", failures);
        AssertForeignKey(model, typeof(MaterialIssueRequest), "fk_material_issue_requests_work_orders", failures);
        AssertForeignKey(model, typeof(FinishedGoodsReceiptRequest), "fk_receipt_requests_work_orders", failures);
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
}
