using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations;
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
            typeof(AlarmRule),
            typeof(ConnectorTagManifest),
            typeof(ConnectorTagBinding),
            typeof(DeviceControlChannelBinding),
            typeof(DeviceControlCommand),
            typeof(DeviceStateSnapshot),
            typeof(AlarmEvent),
            typeof(TelemetryRawSample),
            typeof(TelemetryRollup),
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
        AssertOptionalStringColumn<TelemetrySummary>(fixture, nameof(TelemetrySummary.CollectionConnectorId), "collection_connector_id", 150);
        AssertOptionalStringColumn<TelemetryRawSample>(fixture, nameof(TelemetryRawSample.SourceSystem), "source_system", 100);
        AssertOptionalStringColumn<TelemetryRawSample>(fixture, nameof(TelemetryRawSample.SourceConnector), "source_connector", 150);
        AssertOptionalStringColumn<TelemetryRawSample>(fixture, nameof(TelemetryRawSample.CollectionConnectorId), "collection_connector_id", 150);
    }

    [Fact]
    public void Connector_manifest_keys_and_summary_coverage_index_are_exact()
    {
        using var fixture = CreateFixture();

        AssertUniqueIndex<ConnectorTagManifest>(
            fixture,
            [
                nameof(ConnectorTagManifest.OrganizationId),
                nameof(ConnectorTagManifest.EnvironmentId),
                nameof(ConnectorTagManifest.CollectionConnectorId),
            ]);
        AssertUniqueIndex<ConnectorTagBinding>(
            fixture,
            [
                nameof(ConnectorTagBinding.OrganizationId),
                nameof(ConnectorTagBinding.EnvironmentId),
                nameof(ConnectorTagBinding.CollectionConnectorId),
                nameof(ConnectorTagBinding.DeviceAssetId),
                nameof(ConnectorTagBinding.TagKey),
            ]);

        var coverageProperties = new[]
        {
            nameof(TelemetrySummary.OrganizationId),
            nameof(TelemetrySummary.EnvironmentId),
            nameof(TelemetrySummary.CollectionConnectorId),
            nameof(TelemetrySummary.DeviceAssetId),
            nameof(TelemetrySummary.TagKey),
            nameof(TelemetrySummary.BucketEndUtc),
        };
        AssertIndex<TelemetrySummary>(fixture, coverageProperties);
        AssertNoIndex<TelemetryRawSample>(fixture, coverageProperties);
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
            ],
            "status <> 'cleared'");
        AssertUniqueIndex<AlarmEvent>(
            fixture,
            [
                nameof(AlarmEvent.OrganizationId),
                nameof(AlarmEvent.EnvironmentId),
                nameof(AlarmEvent.DeviceAssetId),
                nameof(AlarmEvent.TagKey),
                nameof(AlarmEvent.ExternalAlarmId),
            ],
            "status <> 'cleared' AND tag_key IS NOT NULL");
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
        AssertUniqueIndex<TelemetryRawSample>(
            fixture,
            [
                nameof(TelemetryRawSample.OrganizationId),
                nameof(TelemetryRawSample.EnvironmentId),
                nameof(TelemetryRawSample.SourceSystem),
                nameof(TelemetryRawSample.SourceConnector),
                nameof(TelemetryRawSample.DeviceAssetId),
                nameof(TelemetryRawSample.TagKey),
                nameof(TelemetryRawSample.SourceSequence),
            ]);
        AssertUniqueIndex<TelemetryRollup>(
            fixture,
            [
                nameof(TelemetryRollup.OrganizationId),
                nameof(TelemetryRollup.EnvironmentId),
                nameof(TelemetryRollup.DeviceAssetId),
                nameof(TelemetryRollup.TagKey),
                nameof(TelemetryRollup.Grain),
                nameof(TelemetryRollup.WindowStartUtc),
            ]);
    }

    [Fact]
    public void Connector_manifest_exact_ordering_migration_backfills_before_enforcing_not_null()
    {
        var operations = new InspectableConnectorManifestExactOrderingMigration().BuildOperations();
        var expectedColumns = new HashSet<(string Table, string Column)>
        {
            ("connector_tag_manifests", "manifest_observed_at_utc_ticks"),
            ("connector_tag_manifests", "concurrency_version"),
            ("connector_tag_bindings", "activation_observed_at_utc_ticks"),
            ("connector_tag_bindings", "concurrency_version"),
        };
        var addedColumns = operations
            .OfType<AddColumnOperation>()
            .Where(operation => expectedColumns.Contains((operation.Table, operation.Name)))
            .ToArray();

        Assert.Equal(4, addedColumns.Length);
        Assert.All(addedColumns, operation =>
        {
            Assert.True(operation.IsNullable);
            Assert.Null(operation.DefaultValue);
            Assert.Null(operation.DefaultValueSql);
        });

        var backfillSql = Assert.Single(operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("EXTRACT(EPOCH FROM manifest_observed_at_utc) * 10000000", backfillSql, StringComparison.Ordinal);
        Assert.Contains("EXTRACT(EPOCH FROM activation_observed_at_utc) * 10000000", backfillSql, StringComparison.Ordinal);
        Assert.Contains("621355968000000000", backfillSql, StringComparison.Ordinal);
        Assert.Contains("concurrency_version = 1", backfillSql, StringComparison.Ordinal);
        Assert.DoesNotContain("concurrency_version = 0", backfillSql, StringComparison.Ordinal);

        var requiredColumns = operations
            .OfType<AlterColumnOperation>()
            .Where(operation => expectedColumns.Contains((operation.Table, operation.Name)))
            .ToArray();
        Assert.Equal(4, requiredColumns.Length);
        Assert.All(requiredColumns, operation => Assert.False(operation.IsNullable));
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
        IReadOnlyCollection<string> propertyNames,
        string? filter = null)
    {
        var entityType = fixture.DbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames) &&
            string.Equals(index.GetFilter(), filter, StringComparison.Ordinal));
    }

    private static void AssertIndex<TEntity>(
        IndustrialTelemetrySchemaFixture fixture,
        IReadOnlyCollection<string> propertyNames)
    {
        var entityType = fixture.DbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static void AssertNoIndex<TEntity>(
        IndustrialTelemetrySchemaFixture fixture,
        IReadOnlyCollection<string> propertyNames)
    {
        var entityType = fixture.DbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.DoesNotContain(entityType!.GetIndexes(), index =>
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

    private sealed class InspectableConnectorManifestExactOrderingMigration : AddConnectorManifestExactOrdering
    {
        public IReadOnlyList<MigrationOperation> BuildOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            base.Up(builder);
            return builder.Operations;
        }
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
