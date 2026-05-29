using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Numbering;
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
            typeof(ScheduleResult),
            typeof(WorkCenterUnavailability),
            typeof(DeviceAssetWorkCenterMapping),
            typeof(FinishedGoodsReceiptRequest),
            typeof(NumberingCounter),
            typeof(NumberingIdempotencyKey),
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

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyCollection<string> ForeignKeysAreConfigured(ApplicationDbContext dbContext)
    {
        var model = dbContext.Model;
        var failures = new List<string>();
        AssertForeignKey(model, typeof(OperationTask), "fk_operation_tasks_work_orders", failures);
        AssertForeignKey(model, typeof(ProductionReport), "fk_production_reports_work_orders", failures);
        AssertForeignKey(model, typeof(ProductionReport), "fk_production_reports_operation_tasks", failures);
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
