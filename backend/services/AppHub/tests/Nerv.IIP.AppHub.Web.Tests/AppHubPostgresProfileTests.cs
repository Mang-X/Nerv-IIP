using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Infrastructure.Repositories;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.AppHub.Web.Application.Queries;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DependencyInjection;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubPostgresProfileTests
{
    [AppHubRealPostgresFact]
    public async Task Concurrent_first_collection_health_reports_use_real_unique_constraint_and_publish_once_each()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var barrier = new ConcurrentCollectionHealthInsertBarrier();
        var domainEvents = new SnapshotDomainEventRecorder();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        AddPostgreSqlAppHubPersistence(services, database.ConnectionString, barrier);
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
        services.AddSingleton(domainEvents);
        services.AddSingleton<INotificationHandler<InstanceStateSnapshotRecordedDomainEvent>>(domainEvents);
        services.AddScoped<AppHubDatabaseMigrationRunner>();

        await using var provider = services.BuildServiceProvider();
        using (var migrationScope = provider.CreateScope())
        {
            await migrationScope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>().MigrateAsync();
            var mediator = migrationScope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new RegisterApplicationCommand(AppHubPostgresSamples.Registration("pg-concurrent-health")));
        }

        var epoch = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var earlier = DateTimeOffset.Parse("2026-07-13T01:00:00Z");
        var later = earlier.AddMinutes(1);
        var low = AppHubPostgresSamples.StateWithCollectionHealth(epoch, earlier, 5);
        var high = AppHubPostgresSamples.StateWithCollectionHealth(epoch, later, 8);

        await Task.WhenAll(SendSnapshotAsync(provider, low), SendSnapshotAsync(provider, high));

        using var assertionScope = provider.CreateScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var projection = Assert.Single(await db.ConnectorCollectionHealth.AsNoTracking().ToListAsync());
        Assert.Equal(8, projection.ReceivedCount);
        Assert.Equal(later, projection.ReportedAtUtc);
        Assert.Equal(2, barrier.FirstProjectionAttempts);
        Assert.Equal(
            [earlier, later],
            domainEvents.ObservedAtUtc.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Postgres_store_generates_guid_strong_ids_on_add()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        AddPostgreSqlAppHubPersistence(services, connectionString);
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
        services.AddScoped<AppHubDatabaseMigrationRunner>();

        await using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        var migrationRunner = scope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>();
        await migrationRunner.MigrateAsync();
        await AssertMigrationsHistoryTableInSchemaAsync(db, "apphub");

        var application = new AppHubApplication("org-id", "env-id", "app-key", "App Name", "1.0.0");

        Assert.Null(application.Id);
        var version = Assert.Single(application.Versions);
        Assert.Null(version.Id);

        db.Applications.Add(application);
        await db.SaveChangesAsync();

        Assert.NotNull(application.Id);
        Assert.NotEqual(Guid.Empty, application.Id.Id);
        Assert.NotNull(version.Id);
        Assert.NotEqual(Guid.Empty, version.Id.Id);
    }

    [Fact]
    public async Task Postgres_store_persists_registration_heartbeat_and_state()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        AddPostgreSqlAppHubPersistence(services, connectionString);
        services.AddIntegrationEvents(typeof(Program));
        services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
        services.AddScoped<AppHubDatabaseMigrationRunner>();

        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            var migrationRunner = scope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>();
            await migrationRunner.MigrateAsync();
            await AssertMigrationsHistoryTableInSchemaAsync(db, "apphub");

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new RegisterApplicationCommand(AppHubPostgresSamples.Registration("pg-apphub-001")));
            await mediator.Send(new RecordApplicationHeartbeatCommand(AppHubPostgresSamples.Heartbeat()));
            await mediator.Send(new RecordInstanceStateSnapshotCommand(AppHubPostgresSamples.State("running", "healthy")));
        }

        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var detail = await mediator.Send(new GetApplicationInstanceDetailQuery("org-001", "env-dev", "demo-api-001"));

            Assert.Equal("running", detail.ReportedStatus);
            Assert.Equal("healthy", detail.HealthStatus);
            Assert.NotNull(detail.LastHeartbeatAtUtc);
            Assert.Equal(DateTimeOffset.Parse("2026-05-17T00:00:10Z"), detail.LastStateObservedAtUtc);
        }
    }

    private static class AppHubPostgresSamples
    {
        private static readonly ConnectorRequestContext Context = new(
            "1.0",
            "1.0",
            "corr-pg-apphub",
            DateTimeOffset.Parse("2026-05-17T00:00:00Z"),
            "org-001",
            "env-dev",
            "connector-host-001");

        public static ApplicationRegistration Registration(string idempotencyKey) => new(
            Context,
            idempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        public static ApplicationHeartbeat Heartbeat() => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-17T00:00:05Z"),
            true,
            DateTimeOffset.Parse("2026-05-17T00:00:00Z"),
            12,
            new Dictionary<string, string>());

        public static InstanceStateSnapshot State(string reportedStatus, string healthStatus) => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-17T00:00:10Z"),
            reportedStatus,
            healthStatus,
            "summary",
            new Dictionary<string, string>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>());

        public static InstanceStateSnapshot StateWithCollectionHealth(
            Guid counterEpoch,
            DateTimeOffset reportedAtUtc,
            long receivedCount) => new(
            Context with { OccurredAtUtc = reportedAtUtc },
            "demo-api-001",
            reportedAtUtc,
            "running",
            "healthy",
            "summary",
            new Dictionary<string, string>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>(),
            new ConnectorCollectionHealth(
                "demo-api-001",
                "opcua",
                counterEpoch,
                reportedAtUtc,
                receivedCount,
                0,
                0,
                reportedAtUtc));
    }

    private static async Task SendSnapshotAsync(ServiceProvider provider, InstanceStateSnapshot snapshot)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new RecordInstanceStateSnapshotCommand(snapshot));
    }

    private static void AddPostgreSqlAppHubPersistence(
        IServiceCollection services,
        string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:AppHubDb"] = connectionString,
            })
            .Build();
        services.AddAppHubPersistence(configuration, usePostgreSql: true);
        services.AddSingleton<IConnectorIngestionTokenService>(
            new ConnectorIngestionTokenService(configuration, new TestHostEnvironment(), TimeProvider.System));
        if (interceptor is not null)
        {
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Nerv.IIP.AppHub.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task AssertMigrationsHistoryTableInSchemaAsync(ApplicationDbContext db, string schema)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = '__EFMigrationsHistory'
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);

        var exists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(exists, $"Expected EF migrations history table in schema '{schema}'.");
    }

    private sealed class ConcurrentCollectionHealthInsertBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int firstProjectionAttempts;

        public int FirstProjectionAttempts => firstProjectionAttempts;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ConnectorCollectionHealthProjection>()
                .Any(entry => entry.State == EntityState.Added))
            {
                var attempt = Interlocked.Increment(ref firstProjectionAttempts);
                if (attempt == 2)
                {
                    completion.TrySetResult();
                }

                await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class SnapshotDomainEventRecorder : INotificationHandler<InstanceStateSnapshotRecordedDomainEvent>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<DateTimeOffset> observedAtUtc = [];

        public IReadOnlyCollection<DateTimeOffset> ObservedAtUtc => observedAtUtc;

        public Task Handle(InstanceStateSnapshotRecordedDomainEvent notification, CancellationToken cancellationToken)
        {
            observedAtUtc.Add(notification.ObservedAtUtc);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_apphub_health_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres"
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            var testConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            }.ConnectionString;
            return new TemporaryDatabase(adminConnectionString, databaseName, testConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

internal sealed class AppHubRealPostgresFactAttribute : FactAttribute
{
    public AppHubRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run real PostgreSQL AppHub collection-health concurrency proof.";
        }
    }
}
