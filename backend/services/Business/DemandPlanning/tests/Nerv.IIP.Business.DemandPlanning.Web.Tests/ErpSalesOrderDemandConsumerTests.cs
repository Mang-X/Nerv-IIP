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
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
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
        await using var database = await RedisCapTestDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
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

        await AssertEventuallyAsync(async token =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demand = await dbContext.DemandSources.AsNoTracking().SingleOrDefaultAsync(token);
            Assert.NotNull(demand);
            Assert.Equal(0m, demand.Quantity);
            Assert.Equal(4, demand.SourceVersion);
            Assert.Equal("cancelled", demand.SourceStatus);
            Assert.Equal(4, (await dbContext.SalesOrderDemandProjections.AsNoTracking().SingleAsync(token)).OrderVersion);
            Assert.Equal(4, await dbContext.ProcessedIntegrationEvents.CountAsync(token));
        });
    }

    [DemandPlanningRealPostgresRedisFact]
    public async Task Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail()
    {
        await using var database = await RedisCapTestDatabase.CreateAsync(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
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
        services.AddSingleton<ChangedV2FallbackFailureProbe>();
        services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            database.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
        services.AddCap(options =>
            {
                options.Version = RedisCapVersion("man517-retry");
                options.FailedRetryCount = 4;
                options.FailedRetryInterval = 2;
                options.FallbackWindowLookbackSeconds = 30;
                options.TopicNamePrefix = topicNamePrefix;
                options.UseEntityFramework<ApplicationDbContext>();
                options.UseConfiguredTransport(configuration, "Development");
            })
            .AddSubscriberAssembly(typeof(ChangedV2FallbackRetryProbeSubscriber).Assembly);

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
        provider.GetRequiredService<ChangedV2FallbackFailureProbe>().SetTarget(targetEventId);
        var retryPathWait = System.Diagnostics.Stopwatch.StartNew();
        await provider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(SalesOrderChangedIntegrationEvent), targetEvent);

        var failureProbe = provider.GetRequiredService<ChangedV2FallbackFailureProbe>();
        await Eventually.WaitAsync(
            condition: "CAP exhausts its three immediate delivery attempts for the failing v2 message",
            // In-memory probe counter; nothing here can block, so discarding the window token drops no budget.
            observe: _ => ValueTask.FromResult(failureProbe.InjectedFailureCount),
            isSatisfied: failures => failures >= 3,
            describe: failures => $"injectedFailures={failures}; attempts={failureProbe.AttemptCount}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(50), []));

        Assert.Equal(3, failureProbe.InjectedFailureCount);
        await using (var failedAttemptScope = provider.CreateAsyncScope())
        {
            var failedAttemptDb = failedAttemptScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var demandBeforeRetry = await failedAttemptDb.DemandSources.AsNoTracking().SingleAsync();
            Assert.Equal(1, demandBeforeRetry.SourceVersion);
            Assert.Equal(2m, demandBeforeRetry.Quantity);
            Assert.Single(await failedAttemptDb.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());
        }

        // Negative assertion: the immediate retries are exhausted, so no fourth attempt may happen until
        // the fallback scanner picks the message up. A bounded stability window fails on the first extra
        // attempt instead of sleeping once and asserting whatever the clock happened to allow.
        await Consistently.StaysAsync(
            condition: "no further delivery attempt happens before the CAP fallback scan window opens",
            // In-memory probe counter; nothing here can block, so discarding the window token drops no budget.
            observe: _ => ValueTask.FromResult(failureProbe.AttemptCount),
            isSatisfied: attempts => attempts == 3,
            describe: attempts => $"attempts={attempts}; injectedFailures={failureProbe.InjectedFailureCount}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200), []));

        var retried = await Eventually.WaitAsync(
            condition: "the CAP fallback scan redelivers the failed v2 message and the demand reaches version 2",
            observe: async token =>
            {
                await using var verificationScope = provider.CreateAsyncScope();
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var demand = await dbContext.DemandSources.AsNoTracking().SingleAsync(token);
                var processedCount = await dbContext.ProcessedIntegrationEvents.CountAsync(token);
                return (demand.SourceVersion, demand.Quantity, demand.SourceStatus, ProcessedCount: processedCount);
            },
            isSatisfied: observation => observation.SourceVersion == 2,
            describe: observation =>
                $"sourceVersion={observation.SourceVersion}; quantity={observation.Quantity}; "
                + $"sourceStatus={observation.SourceStatus}; processedEvents={observation.ProcessedCount}; "
                + $"attempts={failureProbe.AttemptCount}; injectedFailures={failureProbe.InjectedFailureCount}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(50), TimeSpan.FromMilliseconds(200), []));

        Assert.Equal(3, failureProbe.InjectedFailureCount);
        Assert.True(failureProbe.AttemptCount >= 4);
        Assert.True(retryPathWait.Elapsed >= TimeSpan.FromSeconds(25));
        Assert.Equal(4m, retried.Quantity);
        Assert.Equal("active", retried.SourceStatus);
        Assert.Equal(2, retried.ProcessedCount);
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
                ["Cap:Version"] = RedisCapVersion("man517"),
                ["InternalService:BearerToken"] = "test-internal-token",
            };
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
    }

    /// <summary>
    /// Real Redis + PostgreSQL consumption has no completion receipt, so the projection is polled on a
    /// bounded budget until the caller's assertions hold. A timeout reports the last failing assertion.
    /// </summary>
    private static async Task AssertEventuallyAsync(Func<CancellationToken, Task> assertion)
    {
        await Eventually.AssertAsync(
            condition: "the Redis CAP sales-order demand projection satisfies the asserted state",
            assertion: assertion,
            options: new EventuallyOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250), []));
    }

    private static string RedisCapVersion(string fallbackPrefix)
    {
        var configured = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return $"{fallbackPrefix}-{Guid.NewGuid():N}"[..20];
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

    private sealed class RedisCapTestDatabase(string connectionString, TemporaryDatabase? ownedDatabase) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<RedisCapTestDatabase> CreateAsync(string baseConnectionString)
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("NERV_IIP_TEST_DATABASE_LIFECYCLE"),
                    "external",
                    StringComparison.Ordinal))
            {
                await ResetExternalDatabaseAsync(baseConnectionString);
                return new RedisCapTestDatabase(baseConnectionString, null);
            }

            var temporaryDatabase = await TemporaryDatabase.CreateAsync(baseConnectionString);
            return new RedisCapTestDatabase(temporaryDatabase.ConnectionString, temporaryDatabase);
        }

        private static async Task ResetExternalDatabaseAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP SCHEMA IF EXISTS demand_planning CASCADE;
                DROP SCHEMA IF EXISTS cap CASCADE;
                """;
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (ownedDatabase is not null)
            {
                await ownedDatabase.DisposeAsync();
            }
        }
    }
}

internal sealed class ChangedV2FallbackFailureProbe
{
    private int attemptCount;
    private int injectedFailureCount;
    private string? targetEventId;

    public int AttemptCount => Volatile.Read(ref attemptCount);

    public int InjectedFailureCount => Volatile.Read(ref injectedFailureCount);

    public void SetTarget(string eventId) => Volatile.Write(ref targetEventId, eventId);

    public bool IsTarget(SalesOrderChangedIntegrationEvent integrationEvent) =>
        string.Equals(Volatile.Read(ref targetEventId), integrationEvent.EventId, StringComparison.Ordinal);

    public void ThrowDuringImmediateRetries(SalesOrderChangedIntegrationEvent integrationEvent)
    {
        if (integrationEvent.Payload.OrderVersion != 2)
        {
            return;
        }

        var attempt = Interlocked.Increment(ref attemptCount);
        if (attempt <= 3)
        {
            Interlocked.Increment(ref injectedFailureCount);
            throw new TimeoutException("Injected MAN-517 transient failure through all CAP immediate attempts.");
        }
    }
}

internal sealed class ChangedV2FallbackRetryProbeSubscriber(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore,
    ChangedV2FallbackFailureProbe probe) : ICapSubscribe
{
    [CapSubscribe(nameof(SalesOrderChangedIntegrationEvent), Group = "business-demand-planning.man517-retry-proof")]
    public async Task HandleAsync(SalesOrderChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!probe.IsTarget(integrationEvent))
        {
            return;
        }

        probe.ThrowDuringImmediateRetries(integrationEvent);

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
