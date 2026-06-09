using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Infrastructure.IntegrationEvents;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceSchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        var services = CreateServices();
        using var fixture = new SchemaFixture(services.BuildServiceProvider());
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MaintenanceFacts.ServiceName, MaintenanceFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Maintenance_schema_metadata_follows_conventions_and_does_not_own_external_boundaries()
    {
        using var fixture = new SchemaFixture(CreateServices().BuildServiceProvider());
        var businessEntities = new[]
        {
            typeof(MaintenanceWorkOrder),
            typeof(SparePartLine),
            typeof(MaintenancePlan),
            typeof(MaintenanceInspection),
            typeof(DowntimeReason),
            typeof(ProcessedIntegrationEvent),
        };
        var failures = new List<string>();

        Assert.Equal(MaintenanceFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, MaintenanceFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, MaintenanceFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MaintenanceFacts.ServiceName, MaintenanceFacts.Schema));
        failures.AddRange(ProcessedIntegrationEventHasUniqueInboxIndex(fixture.DbContext.Model));
        failures.AddRange(NoExternalOwnershipColumns(fixture.DbContext));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyCollection<string> ProcessedIntegrationEventHasUniqueInboxIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(ProcessedIntegrationEvent));
        if (entity is null)
        {
            return [$"{MaintenanceFacts.ServiceName}: missing processed integration event entity metadata."];
        }

        var hasUniqueIndex = entity.GetIndexes().Any(index =>
            index.IsUnique &&
            index.GetDatabaseName() == "ux_processed_integration_events_consumer_event_id" &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ProcessedIntegrationEvent.ConsumerName),
                nameof(ProcessedIntegrationEvent.EventId),
            ]));

        return hasUniqueIndex
            ? []
            : [$"{MaintenanceFacts.ServiceName}: processed integration event inbox requires a unique consumer/event id index."];
    }

    [Fact]
    public void Maintenance_plan_runtime_window_columns_are_nullable_and_documented()
    {
        using var fixture = new SchemaFixture(CreateServices().BuildServiceProvider());
        var entity = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MaintenancePlan))
            ?? throw new InvalidOperationException("MaintenancePlan metadata was not found.");

        var windowStart = entity.FindProperty(nameof(MaintenancePlan.WindowStartUtc))
            ?? throw new InvalidOperationException("MaintenancePlan.WindowStartUtc metadata was not found.");
        var windowEnd = entity.FindProperty(nameof(MaintenancePlan.WindowEndUtc))
            ?? throw new InvalidOperationException("MaintenancePlan.WindowEndUtc metadata was not found.");

        Assert.True(windowStart.IsNullable);
        Assert.Equal("window_start_utc", windowStart.GetColumnName());
        Assert.Equal("UTC start of the optional runtime availability maintenance window.", windowStart.GetComment());
        Assert.True(windowEnd.IsNullable);
        Assert.Equal("window_end_utc", windowEnd.GetColumnName());
        Assert.Equal("UTC end of the optional runtime availability maintenance window.", windowEnd.GetComment());

        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(MaintenancePlan.OrganizationId),
                nameof(MaintenancePlan.EnvironmentId),
                nameof(MaintenancePlan.DeviceAssetId),
                nameof(MaintenancePlan.WindowStartUtc),
                nameof(MaintenancePlan.WindowEndUtc),
            ]));
    }

    private static IEnumerable<string> NoExternalOwnershipColumns(ApplicationDbContext dbContext)
    {
        var forbiddenFragments = new[]
        {
            "telemetry_sample",
            "raw_alarm_payload",
            "alarm_payload",
            "device_asset_name",
            "device_asset_model",
            "stock_balance",
            "on_hand_quantity",
            "available_quantity",
        };
        return dbContext.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => $"{entity.GetTableName()}.{property.GetColumnName()}"))
            .Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(name => $"Maintenance must not own external boundary column '{name}'.")
            .ToArray();
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddMaintenancePostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");
        return services;
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
