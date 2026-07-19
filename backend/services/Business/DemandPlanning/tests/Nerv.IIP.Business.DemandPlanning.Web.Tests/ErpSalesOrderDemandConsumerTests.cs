using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class ErpSalesOrderDemandConsumerTests
{
    [Fact]
    public async Task Concrete_event_fact_rejects_mismatched_payload_status_to_dead_letter()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        var handler = new SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var malformed = Cancelled(2) with { Payload = Payload(2, "released", 2m, "10") };

        await handler.HandleAsync(malformed, CancellationToken.None);

        Assert.Empty(await dbContext.DemandSources.ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("invalid-sales-order-demand-payload", deadLetter.FailureCode);
        Assert.Contains("requires sales order status 'cancelled'", deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    [DemandPlanningRealPostgresRedisFact]
    public async Task Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        await using var factory = CreateRedisCapFactory(
            database.ConnectionString,
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!);
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<ICapPublisher>();
            var released = Released(1, 2m, "10");
            await publisher.PublishAsync(nameof(SalesOrderReleasedIntegrationEvent), released);
            await publisher.PublishAsync(nameof(SalesOrderReleasedIntegrationEvent), released with { EventId = "evt-redelivery" });
            await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), Changed(3, 5m, "10"));
            await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), Changed(2, 4m, "10"));
            await publisher.PublishAsync(nameof(SalesOrderCancelledIntegrationEvent), Cancelled(4));
            await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), Changed(3, 9m, "10"));
        }

        await AssertEventuallyAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demand = await dbContext.DemandSources.AsNoTracking().SingleOrDefaultAsync();
            Assert.NotNull(demand);
            Assert.Equal(0m, demand.Quantity);
            Assert.Equal(4, demand.SourceVersion);
            Assert.Equal("cancelled", demand.SourceStatus);
            Assert.Equal(4, (await dbContext.SalesOrderDemandProjections.AsNoTracking().SingleAsync()).OrderVersion);
            Assert.Equal(4, await dbContext.ProcessedIntegrationEvents.CountAsync());
        });
    }

    [DemandPlanningRealPostgresRedisFact]
    public async Task Redis_cap_retry_converges_changed_v2_after_first_consumer_failure()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var redisConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var settings = new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = redisConnectionString,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var topicNamePrefix = $"man517-retry-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSingleton<ChangedV2FirstAttemptFailureProbe>();
        services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            database.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
        services.AddCap(options =>
            {
                options.Version = $"man517-retry-{Guid.NewGuid():N}"[..20];
                options.FailedRetryCount = 2;
                options.FailedRetryInterval = 2;
                options.TopicNamePrefix = topicNamePrefix;
                options.UseEntityFramework<ApplicationDbContext>();
                options.UseConfiguredTransport(configuration, "Development");
            })
            .AddSubscriberAssembly(typeof(ChangedV2RetryProbeSubscriber).Assembly);

        await using var provider = services.BuildServiceProvider();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
            await setupScope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
            await setupScope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
            var deadLetters = setupScope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            await new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters)
                .HandleAsync(Released(1, 2m, "10"), CancellationToken.None);
        }

        var targetEventId = $"evt-retry-v2-{Guid.NewGuid():N}";
        var targetEvent = Changed(2, 4m, "10") with
        {
            EventId = targetEventId,
            IdempotencyKey = $"erp:sales-order:org-001:env-dev:SO-DEMO-001:v2:retry-{Guid.NewGuid():N}",
        };
        provider.GetRequiredService<ChangedV2FirstAttemptFailureProbe>().SetTarget(targetEventId);
        await provider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(SalesOrderChangedIntegrationEvent), targetEvent);

        var firstFailureDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        var failureProbe = provider.GetRequiredService<ChangedV2FirstAttemptFailureProbe>();
        while (failureProbe.InjectedFailureCount == 0 && DateTimeOffset.UtcNow < firstFailureDeadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(1, failureProbe.InjectedFailureCount);
        await using (var failedAttemptScope = provider.CreateAsyncScope())
        {
            var failedAttemptDb = failedAttemptScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demandBeforeRetry = await failedAttemptDb.DemandSources.AsNoTracking().SingleAsync();
            Assert.Equal(1, demandBeforeRetry.SourceVersion);
            Assert.Equal(2m, demandBeforeRetry.Quantity);
            Assert.Single(await failedAttemptDb.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        }

        failureProbe.ReleaseRetry();

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            await using var verificationScope = provider.CreateAsyncScope();
            var dbContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demand = await dbContext.DemandSources.AsNoTracking().SingleAsync();
            if (demand.SourceVersion == 2)
            {
                Assert.Equal(1, failureProbe.InjectedFailureCount);
                Assert.True(failureProbe.AttemptCount >= 2);
                Assert.Equal(4m, demand.Quantity);
                Assert.Equal("active", demand.SourceStatus);
                Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"CAP did not retry the injected v2 failure within the test deadline. attempts={failureProbe.AttemptCount}, failures={failureProbe.InjectedFailureCount}");
    }

    [DemandPlanningRealPostgresFact]
    public async Task PostgreSql_inbox_and_order_watermark_survive_duplicate_out_of_order_change_and_cancel()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        await using var provider = CreatePostgresProvider(database.ConnectionString);
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
            var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
            var releasedHandler = new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
            var changedHandler = new SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
            var cancelledHandler = new SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);

            var released = Released(1, 2m, "10");
            await releasedHandler.HandleAsync(released, CancellationToken.None);
            await releasedHandler.HandleAsync(released with { EventId = "evt-redelivery" }, CancellationToken.None);
            await changedHandler.HandleAsync(Changed(3, 5m, "10"), CancellationToken.None);
            await changedHandler.HandleAsync(Changed(2, 4m, "10"), CancellationToken.None);
            await cancelledHandler.HandleAsync(Cancelled(4), CancellationToken.None);
            await changedHandler.HandleAsync(Changed(3, 9m, "10"), CancellationToken.None);
        }

        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demand = Assert.Single(await dbContext.DemandSources.AsNoTracking().ToArrayAsync());
            Assert.Equal("SO-DEMO-001", demand.SourceReference);
            Assert.Equal(0m, demand.Quantity);
            Assert.Equal(4, demand.SourceVersion);
            Assert.Equal("cancelled", demand.SourceStatus);
            Assert.Equal(4, Assert.Single(await dbContext.SalesOrderDemandProjections.AsNoTracking().ToArrayAsync()).OrderVersion);
            Assert.Equal(4, await dbContext.ProcessedIntegrationEvents.CountAsync());
        }
    }

    [DemandPlanningRealPostgresFact]
    public async Task PostgreSql_upgrade_reclassifies_legacy_manual_and_sales_order_collision_without_losing_traceability()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        await using var provider = CreatePostgresProvider(database.ConnectionString);
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrator = dbContext.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260706070015_AddForecastInputsAndMrpExceptions");
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO demand_planning.demand_sources
              (id, organization_id, environment_id, demand_type, source_reference, sku_code, uom_code, site_code, quantity, due_date, created_at_utc, updated_at_utc)
            VALUES
              ('01900000-0000-7000-8000-000000000001', 'org-001', 'env-dev', 'manual', 'SO-LEGACY-001', 'SKU-A', 'EA', 'SITE-001', 1, DATE '2026-08-15', NOW(), NOW()),
              ('01900000-0000-7000-8000-000000000002', 'org-001', 'env-dev', 'sales-order', 'SO-LEGACY-001', 'SKU-B', 'EA', 'SITE-001', 2, DATE '2026-08-16', NOW(), NOW()),
              ('01900000-0000-7000-8000-000000000003', 'org-001', 'env-dev', 'manual', 'SO-LEGACY-001:legacy-so:01900000000070008000000000000002', 'SKU-C', 'EA', 'SITE-001', 3, DATE '2026-08-17', NOW(), NOW());
            """);

        await migrator.MigrateAsync();

        var demands = await dbContext.DemandSources.AsNoTracking().OrderBy(x => x.SourceReference).ToArrayAsync();
        Assert.Equal(3, demands.Length);
        Assert.All(demands, demand => Assert.Equal("manual", demand.DemandType));
        Assert.Contains(demands, demand => demand.SourceReference == "SO-LEGACY-001");
        Assert.Contains(demands, demand => demand.SourceReference == "SO-LEGACY-001:legacy-so:01900000000070008000000000000002");
        Assert.Contains(demands, demand => demand.SourceReference == "SO-LEGACY-001:legacy-so:01900000000070008000000000000002:1");
        Assert.Equal(3, demands.Select(demand => demand.SourceReference).Distinct(StringComparer.Ordinal).Count());
    }

    [DemandPlanningRealPostgresFact]
    public async Task PostgreSql_concurrent_versions_never_regress_order_watermark_or_demand()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        await using (var provider = CreatePostgresProvider(database.ConnectionString))
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
            var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
            await new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters)
                .HandleAsync(Released(1, 2m, "10"), CancellationToken.None);
        }

        for (var lowerVersion = 2; lowerVersion <= 20; lowerVersion += 2)
        {
            var higherVersion = lowerVersion + 1;
            await Task.WhenAll(
                ProcessPostgresChangeAsync(database.ConnectionString, Changed(lowerVersion, lowerVersion, "10")),
                ProcessPostgresChangeAsync(database.ConnectionString, Changed(higherVersion, higherVersion, "10")));

            await using var verificationProvider = CreatePostgresProvider(database.ConnectionString);
            using var verificationScope = verificationProvider.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(higherVersion, (await verificationDb.SalesOrderDemandProjections.AsNoTracking().SingleAsync()).OrderVersion);
            var demand = await verificationDb.DemandSources.AsNoTracking().SingleAsync();
            Assert.Equal(higherVersion, demand.SourceVersion);
            Assert.Equal(higherVersion, demand.Quantity);
        }
    }

    [Fact]
    public async Task Release_duplicate_change_out_of_order_and_cancel_converge_by_order_version()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        var releasedHandler = new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var changedHandler = new SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var cancelledHandler = new SalesOrderCancelledIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);

        var released = Released(version: 1, quantity: 2m, lineNo: "10");
        await releasedHandler.HandleAsync(released, CancellationToken.None);
        await releasedHandler.HandleAsync(released with { EventId = "evt-redelivery" }, CancellationToken.None);

        var first = Assert.Single(await dbContext.DemandSources.AsNoTracking().ToArrayAsync());
        Assert.Equal("SO-DEMO-001", first.SourceReference);
        Assert.Equal("10", first.SourceLineReference);
        Assert.Equal(2m, first.Quantity);
        Assert.Equal(1, first.SourceVersion);
        Assert.Equal("active", first.SourceStatus);

        await changedHandler.HandleAsync(Changed(version: 3, quantity: 5m, lineNo: "10"), CancellationToken.None);
        await changedHandler.HandleAsync(Changed(version: 2, quantity: 4m, lineNo: "10"), CancellationToken.None);

        var changed = Assert.Single(await dbContext.DemandSources.AsNoTracking().ToArrayAsync());
        Assert.Equal(5m, changed.Quantity);
        Assert.Equal(3, changed.SourceVersion);

        await cancelledHandler.HandleAsync(Cancelled(version: 4), CancellationToken.None);
        await changedHandler.HandleAsync(Changed(version: 3, quantity: 9m, lineNo: "10"), CancellationToken.None);

        var cancelled = Assert.Single(await dbContext.DemandSources.AsNoTracking().ToArrayAsync());
        Assert.Equal(0m, cancelled.Quantity);
        Assert.Equal(4, cancelled.SourceVersion);
        Assert.Equal("cancelled", cancelled.SourceStatus);
        Assert.Equal(4, Assert.Single(await dbContext.SalesOrderDemandProjections.AsNoTracking().ToArrayAsync()).OrderVersion);
        Assert.Equal(4, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Full_snapshot_projects_multiple_lines_and_cancels_omitted_lines()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        var releasedHandler = new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var changedHandler = new SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var release = Released(1, 2m, "10") with
        {
            Payload = Released(1, 2m, "10").Payload with
            {
                Lines =
                [
                    new SalesOrderLineSnapshot("10", "SKU-FG-A", 2m, "EA", new DateOnly(2026, 8, 15), false),
                    new SalesOrderLineSnapshot("20", "SKU-FG-B", 3m, "EA", new DateOnly(2026, 8, 16), false),
                ],
            },
        };
        await releasedHandler.HandleAsync(release, CancellationToken.None);

        await changedHandler.HandleAsync(Changed(2, 4m, "10"), CancellationToken.None);

        var demands = await dbContext.DemandSources.AsNoTracking().OrderBy(x => x.SourceLineReference).ToArrayAsync();
        Assert.Collection(
            demands,
            active =>
            {
                Assert.Equal("10", active.SourceLineReference);
                Assert.Equal(4m, active.Quantity);
                Assert.Equal("active", active.SourceStatus);
            },
            omitted =>
            {
                Assert.Equal("20", omitted.SourceLineReference);
                Assert.Equal(0m, omitted.Quantity);
                Assert.Equal("cancelled", omitted.SourceStatus);
            });
    }

    [Fact]
    public async Task Invalid_business_payload_is_dead_lettered_without_throwing_or_creating_demand()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        var handler = new SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters);
        var invalid = Released(version: 1, quantity: 2m, lineNo: "10") with
        {
            Payload = Payload(1, "released", 2m, "10") with { SiteCode = "UNSPECIFIED" },
        };

        await handler.HandleAsync(invalid, CancellationToken.None);

        Assert.Empty(await dbContext.DemandSources.ToArrayAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            SalesOrderReleasedIntegrationEventHandlerForProjectDemandSource.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("invalid-sales-order-demand-payload", deadLetter.FailureCode);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"sales-order-demand-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreatePostgresProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
        return services.BuildServiceProvider();
    }

    private static async Task ProcessPostgresChangeAsync(string connectionString, SalesOrderChangedIntegrationEvent integrationEvent)
    {
        await using var provider = CreatePostgresProvider(connectionString);
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetters = new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(dbContext);
        await new SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetters)
            .HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateRedisCapFactory(string connectionString, string redisConnectionString)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["Persistence:AutoMigrate"] = "false",
                ["ConnectionStrings:PostgreSQL"] = connectionString,
                ["Messaging:Provider"] = "Redis",
                ["Messaging:Redis:ConnectionString"] = redisConnectionString,
                ["ConnectionStrings:Redis"] = redisConnectionString,
                ["Cap:Version"] = $"man517-{Guid.NewGuid():N}"[..20],
                ["InternalService:BearerToken"] = "test-internal-token",
            };
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
    }

    private static async Task AssertEventuallyAsync(Func<Task> assertion)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(30);
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception exception) when (exception is Xunit.Sdk.XunitException or InvalidOperationException)
            {
                lastException = exception;
                await Task.Delay(250);
            }
        }

        throw lastException ?? new Xunit.Sdk.XunitException("Timed out waiting for Redis CAP sales-order demand projection.");
    }

    private static SalesOrderReleasedIntegrationEvent Released(int version, decimal quantity, string lineNo) =>
        new(
            $"evt-released-{version}",
            ErpIntegrationEventTypes.SalesOrderReleased,
            ErpIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 7, 18, 12, version, 0, TimeSpan.Zero),
            ErpIntegrationEventSources.BusinessErp,
            "corr-so-demo-001",
            "SO-DEMO-001",
            "org-001",
            "env-dev",
            "system:erp",
            $"erp:sales-order:org-001:env-dev:SO-DEMO-001:v{version}:released",
            Payload(version, "released", quantity, lineNo));

    private static SalesOrderChangedIntegrationEvent Changed(int version, decimal quantity, string lineNo) =>
        new(
            $"evt-changed-{version}",
            ErpIntegrationEventTypes.SalesOrderChanged,
            ErpIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 7, 18, 13, version, 0, TimeSpan.Zero),
            ErpIntegrationEventSources.BusinessErp,
            "corr-so-demo-001",
            "SO-DEMO-001",
            "org-001",
            "env-dev",
            "system:erp",
            $"erp:sales-order:org-001:env-dev:SO-DEMO-001:v{version}:changed",
            Payload(version, "released", quantity, lineNo));

    private static SalesOrderCancelledIntegrationEvent Cancelled(int version) =>
        new(
            $"evt-cancelled-{version}",
            ErpIntegrationEventTypes.SalesOrderCancelled,
            ErpIntegrationEventVersions.V1,
            new DateTimeOffset(2026, 7, 18, 14, version, 0, TimeSpan.Zero),
            ErpIntegrationEventSources.BusinessErp,
            "corr-so-demo-001",
            "SO-DEMO-001",
            "org-001",
            "env-dev",
            "system:erp",
            $"erp:sales-order:org-001:env-dev:SO-DEMO-001:v{version}:cancelled",
            Payload(version, "cancelled", 2m, "10") with
            {
                Lines = [new SalesOrderLineSnapshot("10", "SKU-FG-A", 2m, "EA", new DateOnly(2026, 8, 15), true)],
            });

    private static SalesOrderLifecyclePayload Payload(int version, string status, decimal quantity, string lineNo) =>
        new(
            "sales-order-id-001",
            "SO-DEMO-001",
            "CUST-001",
            "SITE-001",
            version,
            status,
            [new SalesOrderLineSnapshot(lineNo, "SKU-FG-A", quantity, "EA", new DateOnly(2026, 8, 15), false)]);

    private sealed class TemporaryDatabase(string adminConnectionString, string databaseName, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_dp_sales_order_{Guid.NewGuid():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

internal sealed class ChangedV2FirstAttemptFailureProbe
{
    private int attemptCount;
    private int injectedFailureCount;
    private string? targetEventId;
    private readonly TaskCompletionSource retryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int AttemptCount => Volatile.Read(ref attemptCount);

    public int InjectedFailureCount => Volatile.Read(ref injectedFailureCount);

    public void SetTarget(string eventId) => Volatile.Write(ref targetEventId, eventId);

    public bool IsTarget(SalesOrderChangedIntegrationEvent integrationEvent) =>
        string.Equals(Volatile.Read(ref targetEventId), integrationEvent.EventId, StringComparison.Ordinal);

    public async Task WaitBeforeHandlingAsync(
        SalesOrderChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.Payload.OrderVersion != 2)
        {
            return;
        }

        var attempt = Interlocked.Increment(ref attemptCount);
        if (attempt == 1)
        {
            Interlocked.Increment(ref injectedFailureCount);
            throw new TimeoutException("Injected MAN-517 transient failure before the v2 DemandPlanning handler.");
        }

        await retryRelease.Task.WaitAsync(cancellationToken);
    }

    public void ReleaseRetry() => retryRelease.TrySetResult();
}

internal sealed class ChangedV2RetryProbeSubscriber(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ChangedV2FirstAttemptFailureProbe probe) : ICapSubscribe
{
    [CapSubscribe(nameof(SalesOrderChangedIntegrationEvent), Group = "business-demand-planning.man517-retry-proof")]
    public async Task HandleAsync(SalesOrderChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!probe.IsTarget(integrationEvent))
        {
            return;
        }

        await probe.WaitBeforeHandlingAsync(integrationEvent, cancellationToken);

        await new SalesOrderChangedIntegrationEventHandlerForProjectDemandSource(dbContext, deadLetterStore)
            .HandleAsync(integrationEvent, cancellationToken);
    }
}

internal sealed class DemandPlanningRealPostgresFactAttribute : FactAttribute
{
    public DemandPlanningRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP sales-order demand bridge proof.";
        }
    }
}

internal sealed class DemandPlanningRealPostgresRedisFactAttribute : FactAttribute
{
    public DemandPlanningRealPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP bridge proof.";
        }
    }
}
