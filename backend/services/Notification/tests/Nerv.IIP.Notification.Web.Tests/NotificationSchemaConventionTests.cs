using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Infrastructure.Migrations;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using Nerv.IIP.Testing.EntityFramework;
using NetCorePal.Extensions.DistributedTransactions.CAP.Persistence;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationSchemaConventionTests
{
    [Fact]
    public void Notification_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(NotificationIntent),
            typeof(NotificationMessage),
            typeof(NotificationTask),
            typeof(DeliveryAttempt),
            typeof(ProcessedIntegrationEvent),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "Notification", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "Notification", businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "Notification", "notification"));

        Assert.Equal("notification", fixture.DbContext.Model.GetDefaultSchema());
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Notification_schema_defines_required_tables_and_indexes()
    {
        using var fixture = CreateFixture();

        AssertTable(fixture.DbContext, typeof(NotificationIntent), "notification_intents");
        AssertTable(fixture.DbContext, typeof(NotificationMessage), "notification_messages");
        AssertTable(fixture.DbContext, typeof(NotificationTask), "notification_tasks");
        AssertTable(fixture.DbContext, typeof(DeliveryAttempt), "delivery_attempts");
        AssertTable(fixture.DbContext, typeof(ProcessedIntegrationEvent), "processed_integration_events");
        AssertTable(fixture.DbContext, typeof(PublishedMessage), "cap_published_messages");
        AssertTable(fixture.DbContext, typeof(ReceivedMessage), "cap_received_messages");
        AssertTable(fixture.DbContext, typeof(CapLock), "cap_locks");

        AssertUniqueIndex(
            fixture.DbContext,
            typeof(NotificationIntent),
            nameof(NotificationIntent.OrganizationId),
            nameof(NotificationIntent.EnvironmentId),
            nameof(NotificationIntent.SourceService),
            nameof(NotificationIntent.SourceEventType),
            nameof(NotificationIntent.DedupeKey));

        AssertDescendingCreatedAtIndex(
            fixture.DbContext,
            typeof(NotificationMessage),
            nameof(NotificationMessage.RecipientRef),
            nameof(NotificationMessage.Status),
            nameof(NotificationMessage.CreatedAtUtc));

        AssertDescendingCreatedAtIndex(
            fixture.DbContext,
            typeof(NotificationTask),
            nameof(NotificationTask.RecipientRef),
            nameof(NotificationTask.Status),
            nameof(NotificationTask.CreatedAtUtc));

        AssertUniqueIndex(
            fixture.DbContext,
            typeof(ProcessedIntegrationEvent),
            "ConsumerName",
            "IdempotencyKey");
    }

    [Fact]
    public void Notification_schema_defines_message_foreign_keys()
    {
        using var fixture = CreateFixture();

        AssertForeignKey(
            fixture.DbContext,
            typeof(NotificationTask),
            typeof(NotificationMessage),
            nameof(NotificationTask.MessageId));

        AssertForeignKey(
            fixture.DbContext,
            typeof(DeliveryAttempt),
            typeof(NotificationMessage),
            nameof(DeliveryAttempt.NotificationMessageId));

        var deliveryAttempt = fixture.DbContext.Model.FindEntityType(typeof(DeliveryAttempt));
        Assert.NotNull(deliveryAttempt);
        var messageId = deliveryAttempt.FindProperty(nameof(DeliveryAttempt.NotificationMessageId));
        Assert.NotNull(messageId);
        Assert.Equal(typeof(NotificationMessageId), messageId.ClrType);
    }

    [Fact]
    public void Initial_notification_migration_contains_key_indexes_and_foreign_keys()
    {
        var migration = new InitialNotificationSchema();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(InitialNotificationSchema)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        AssertCreateIndex(
            migrationBuilder,
            "notification_intents",
            true,
            "OrganizationId",
            "EnvironmentId",
            "SourceService",
            "SourceEventType",
            "DedupeKey");

        AssertCreateIndex(
            migrationBuilder,
            "processed_integration_events",
            true,
            "ConsumerName",
            "EventId");

        AssertCreateIndex(
            migrationBuilder,
            "notification_messages",
            false,
            "RecipientRef",
            "Status",
            "CreatedAtUtc");

        AssertCreateIndex(
            migrationBuilder,
            "notification_tasks",
            false,
            "RecipientRef",
            "Status",
            "CreatedAtUtc");

        AssertForeignKeyOperation(migrationBuilder, "notification_tasks", "notification_messages", "MessageId");
        AssertForeignKeyOperation(migrationBuilder, "delivery_attempts", "notification_messages", "NotificationMessageId");
    }

    [Fact]
    public void Processed_integration_event_idempotency_migration_deduplicates_before_unique_index()
    {
        var migration = new UseIdempotencyKeyForProcessedIntegrationEvents();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(UseIdempotencyKeyForProcessedIntegrationEvents)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        AssertInboxDeduplicationBeforeUniqueIndex(migrationBuilder, "notification");
    }

    [Fact]
    public void Notification_cap_storage_migration_contains_required_tables()
    {
        var migration = new AddNotificationCapStorage();
        var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddNotificationCapStorage)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [migrationBuilder]);

        AssertCreateTable(migrationBuilder, "cap_published_messages");
        AssertCreateTable(migrationBuilder, "cap_received_messages");
        AssertCreateTable(migrationBuilder, "cap_locks");
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(NotificationIntent).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notification")));

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private static void AssertTable(DbContext dbContext, Type entityType, string expectedTableName)
    {
        var entity = dbContext.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        Assert.Equal("notification", entity.GetSchema());
        Assert.Equal(expectedTableName, entity.GetTableName());
    }

    private static void AssertIndex(DbContext dbContext, Type entityType, params string[] propertyNames)
    {
        var entity = dbContext.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var index = entity.GetIndexes().SingleOrDefault(x => x.Properties.Select(p => p.Name).SequenceEqual(propertyNames));
        Assert.NotNull(index);
    }

    private static void AssertDescendingCreatedAtIndex(DbContext dbContext, Type entityType, params string[] propertyNames)
    {
        var entity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var index = entity.GetIndexes().SingleOrDefault(x => x.Properties.Select(p => p.Name).SequenceEqual(propertyNames));
        Assert.NotNull(index);
        Assert.Equal([false, false, true], index.IsDescending);
    }

    private static void AssertUniqueIndex(DbContext dbContext, Type entityType, params string[] propertyNames)
    {
        var entity = dbContext.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var index = entity.GetIndexes().SingleOrDefault(x => x.Properties.Select(p => p.Name).SequenceEqual(propertyNames));
        Assert.NotNull(index);
        Assert.True(index.IsUnique, $"{entityType.Name} index on {string.Join(", ", propertyNames)} must be unique.");
    }

    private static void AssertForeignKey(DbContext dbContext, Type entityType, Type principalEntityType, params string[] propertyNames)
    {
        var entity = dbContext.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var foreignKey = entity.GetForeignKeys().SingleOrDefault(x =>
            x.PrincipalEntityType.ClrType == principalEntityType
            && x.Properties.Select(p => p.Name).SequenceEqual(propertyNames));
        Assert.NotNull(foreignKey);
    }

    private static void AssertCreateIndex(MigrationBuilder migrationBuilder, string tableName, bool unique, params string[] columns)
    {
        var operation = migrationBuilder.Operations
            .OfType<CreateIndexOperation>()
            .SingleOrDefault(x =>
                x.Table == tableName
                && x.IsUnique == unique
                && x.Columns.SequenceEqual(columns));
        Assert.NotNull(operation);
        if (columns[^1] == "CreatedAtUtc")
        {
            Assert.NotNull(operation.IsDescending);
            Assert.Equal([false, false, true], operation.IsDescending);
        }
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

    private static void AssertCreateTable(MigrationBuilder migrationBuilder, string tableName)
    {
        var operation = migrationBuilder.Operations
            .OfType<CreateTableOperation>()
            .SingleOrDefault(x => x.Schema == "notification" && x.Name == tableName);
        Assert.NotNull(operation);
    }

    private static void AssertForeignKeyOperation(MigrationBuilder migrationBuilder, string tableName, string principalTableName, params string[] columns)
    {
        var addForeignKeyOperation = migrationBuilder.Operations
            .OfType<AddForeignKeyOperation>()
            .SingleOrDefault(x =>
                x.Table == tableName
                && x.PrincipalTable == principalTableName
                && x.Columns.SequenceEqual(columns));
        if (addForeignKeyOperation is not null)
        {
            return;
        }

        var createTableForeignKey = migrationBuilder.Operations
            .OfType<CreateTableOperation>()
            .Where(x => x.Name == tableName)
            .SelectMany(x => x.ForeignKeys)
            .SingleOrDefault(x =>
                x.PrincipalTable == principalTableName
                && x.Columns.SequenceEqual(columns));
        Assert.NotNull(createTableForeignKey);
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
