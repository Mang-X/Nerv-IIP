using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetrySchemaConventionTests
{
    [Fact]
    public void IndustrialTelemetry_schema_tables_columns_and_migrations_history_follow_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(TelemetryTag),
            typeof(DeviceStateSnapshot),
            typeof(AlarmEvent),
            typeof(TelemetrySummary),
        };
        var failures = new List<string>();

        Assert.Equal(IndustrialTelemetryFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, IndustrialTelemetryFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, IndustrialTelemetryFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, IndustrialTelemetryFacts.ServiceName, IndustrialTelemetryFacts.Schema));

        Assert.Empty(failures);
    }

    [Fact]
    public void Runtime_source_metadata_columns_are_optional_bounded_and_commented()
    {
        using var fixture = CreateFixture();

        AssertOptionalStringColumn<DeviceStateSnapshot>(fixture, nameof(DeviceStateSnapshot.SourceSystem), "source_system", 100);
        AssertOptionalStringColumn<DeviceStateSnapshot>(fixture, nameof(DeviceStateSnapshot.SourceConnector), "source_connector", 150);
        AssertOptionalStringColumn<TelemetrySummary>(fixture, nameof(TelemetrySummary.SourceSystem), "source_system", 100);
        AssertOptionalStringColumn<TelemetrySummary>(fixture, nameof(TelemetrySummary.SourceConnector), "source_connector", 150);
    }

    [Fact]
    public void Runtime_source_metadata_is_part_of_idempotency_unique_indexes()
    {
        using var fixture = CreateFixture();

        AssertUniqueIndex<AlarmEvent>(
            fixture,
            [
                nameof(AlarmEvent.OrganizationId),
                nameof(AlarmEvent.EnvironmentId),
                nameof(AlarmEvent.DeviceAssetId),
                nameof(AlarmEvent.AlarmCode),
                nameof(AlarmEvent.ExternalAlarmId),
            ]);
        AssertUniqueIndex<DeviceStateSnapshot>(
            fixture,
            [
                nameof(DeviceStateSnapshot.OrganizationId),
                nameof(DeviceStateSnapshot.EnvironmentId),
                nameof(DeviceStateSnapshot.SourceSystem),
                nameof(DeviceStateSnapshot.SourceConnector),
                nameof(DeviceStateSnapshot.DeviceAssetId),
                nameof(DeviceStateSnapshot.SourceSequence),
            ]);
        AssertUniqueIndex<TelemetrySummary>(
            fixture,
            [
                nameof(TelemetrySummary.OrganizationId),
                nameof(TelemetrySummary.EnvironmentId),
                nameof(TelemetrySummary.SourceSystem),
                nameof(TelemetrySummary.SourceConnector),
                nameof(TelemetrySummary.DeviceAssetId),
                nameof(TelemetrySummary.TagKey),
                nameof(TelemetrySummary.SourceSequence),
            ]);
    }

    private static void AssertOptionalStringColumn<TEntity>(
        IndustrialTelemetrySchemaFixture fixture,
        string propertyName,
        string columnName,
        int maxLength)
    {
        var entityType = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        var property = entityType!.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.True(property!.IsNullable);
        Assert.Equal(columnName, property.GetColumnName());
        Assert.Equal(maxLength, property.GetMaxLength());
        Assert.False(string.IsNullOrWhiteSpace(property.GetComment()));
    }

    private static void AssertUniqueIndex<TEntity>(
        IndustrialTelemetrySchemaFixture fixture,
        IReadOnlyCollection<string> propertyNames)
    {
        var entityType = fixture.DbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static IndustrialTelemetrySchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddIndustrialTelemetryPostgreSqlPersistence("Host=localhost;Database=nerv_iip_industrial_telemetry_schema;Username=nerv;Password=nerv");
        return new IndustrialTelemetrySchemaFixture(services.BuildServiceProvider());
    }

    private sealed class IndustrialTelemetrySchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public IndustrialTelemetrySchemaFixture(ServiceProvider serviceProvider)
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
