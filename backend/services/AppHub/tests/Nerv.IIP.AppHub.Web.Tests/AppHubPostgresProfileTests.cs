using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.AppHub.Web.Application.Queries;
using Nerv.IIP.Contracts.ConnectorProtocol;
using NetCorePal.Extensions.DependencyInjection;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubPostgresProfileTests
{
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
    }

    private static void AddPostgreSqlAppHubPersistence(IServiceCollection services, string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:AppHubDb"] = connectionString,
            })
            .Build();
        services.AddAppHubPersistence(configuration);
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
}
