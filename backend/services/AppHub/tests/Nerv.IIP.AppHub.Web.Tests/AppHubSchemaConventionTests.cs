using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.IntegrationEvents;
using Nerv.IIP.AppHub.Infrastructure.Migrations;
using Nerv.IIP.Testing.EntityFramework;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubSchemaConventionTests
{
    [Fact]
    public void AppHub_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(AppHubApplication),
            typeof(ApplicationVersion),
            typeof(ManagedNode),
            typeof(ApplicationInstance),
            typeof(InstanceHeartbeat),
            typeof(ConnectorCollectionHealthProjection),
            typeof(InstanceStateHistory),
            typeof(InstanceStatusChange),
            typeof(RegistrationIdempotency),
            typeof(ProcessedIntegrationEvent),
        };

        var jsonColumns = new[]
        {
            new JsonColumnRule(typeof(ApplicationInstance), nameof(ApplicationInstance.Metadata)),
            new JsonColumnRule(typeof(ApplicationInstance), nameof(ApplicationInstance.Capabilities)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "AppHub", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "AppHub", businessEntities));
        failures.AddRange(SchemaConventionAssertions.JsonColumnsHaveCompatibilityComments(fixture.DbContext, "AppHub", jsonColumns));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "AppHub", "apphub"));
        failures.AddRange(ProcessedIntegrationEventHasUniqueInboxIndex(fixture.DbContext.Model));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        var instance = fixture.DbContext.Model.FindEntityType(typeof(ApplicationInstance))!;
        Assert.Contains(instance.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([
            nameof(ApplicationInstance.OrganizationId), nameof(ApplicationInstance.EnvironmentId), nameof(ApplicationInstance.InstanceKey)]));
        var idempotency = fixture.DbContext.Model.FindEntityType(typeof(RegistrationIdempotency))!;
        Assert.Contains(idempotency.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([
            nameof(RegistrationIdempotency.OrganizationId), nameof(RegistrationIdempotency.EnvironmentId), nameof(RegistrationIdempotency.IdempotencyKey)]));
    }

    [Fact]
    public void Processed_integration_event_idempotency_migration_deduplicates_before_unique_index()
    {
        var migration = new UseIdempotencyKeyForProcessedIntegrationEvents();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(UseIdempotencyKeyForProcessedIntegrationEvents)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        AssertInboxDeduplicationBeforeUniqueIndex(migrationBuilder, "apphub");
    }

    [Fact]
    public void Collection_health_migration_backfills_registration_scope_without_fake_empty_values()
    {
        var migration = new AddConnectorCollectionHealthProjection();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddConnectorCollectionHealthProjection).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        var addedScopeColumns = builder.Operations.OfType<AddColumnOperation>().Where(x => x.Table == "registration_idempotency" && x.Name is "OrganizationId" or "EnvironmentId").ToArray();
        Assert.Equal(2, addedScopeColumns.Length);
        Assert.All(addedScopeColumns, column => { Assert.True(column.IsNullable); Assert.Null(column.DefaultValue); });
        Assert.Contains(builder.Operations.OfType<SqlOperation>(), operation => operation.Sql.Contains("FROM apphub.application_instances", StringComparison.Ordinal) && operation.Sql.Contains("RAISE EXCEPTION", StringComparison.Ordinal));
    }

    [Fact]
    public void Current_model_matches_the_latest_migration_snapshot()
    {
        using var fixture = CreateFixture();
        var snapshot = Assert.IsAssignableFrom<ModelSnapshot>(
            fixture.DbContext.GetService<IMigrationsAssembly>().ModelSnapshot);
        var snapshotModel = fixture.DbContext.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var currentModel = fixture.DbContext.GetService<IDesignTimeModel>().Model;
        var modelDiffer = fixture.DbContext.GetService<IMigrationsModelDiffer>();

        var differences = modelDiffer.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.Empty(differences);
    }

    private static IReadOnlyCollection<string> ProcessedIntegrationEventHasUniqueInboxIndex(IModel model)
    {
        var entity = model.FindEntityType(typeof(ProcessedIntegrationEvent));
        if (entity is null)
        {
            return ["AppHub: missing processed integration event entity metadata."];
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
            : ["AppHub: processed integration event inbox requires a unique consumer/idempotency key index."];
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

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:AppHubDb"] = "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            })
            .Build();
        services.AddAppHubPersistence(configuration);

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
