using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Maintenance;
using Nerv.IIP.Contracts.Quality;
using Npgsql;
using System.Data;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MesCapSubscriptionCollection
{
    public const string Name = "MES CAP subscription";
}

[Collection(MesCapSubscriptionCollection.Name)]
public sealed class MesCapSubscriptionTests
{
    private const string AssetUnavailableTopic = "Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent";
    private const string AssetRestoredTopic = "Nerv.IIP.Contracts.Maintenance.AssetRestoredIntegrationEvent";
    private const string SchedulePlanReleasedTopic = "Nerv.IIP.Contracts.Scheduling.SchedulePlanReleasedIntegrationEvent";
    private const string SchedulePlanInvalidatedTopic = "Nerv.IIP.Contracts.Scheduling.SchedulePlanInvalidatedIntegrationEvent";
    private const string NcrDispositionDecidedTopic = "Nerv.IIP.Contracts.Quality.NcrDispositionDecidedIntegrationEvent";
    private const string InspectionResultTopic = "Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent";
    private const string StockMovementPostedTopic = "Nerv.IIP.Contracts.Inventory.StockMovementPostedIntegrationEvent";
    private const string StockMovementPostingFailedTopic = "Nerv.IIP.Contracts.Inventory.StockMovementPostingFailedIntegrationEvent";

    [Fact]
    public void Mes_cap_registration_discovers_maintenance_asset_event_subscribers()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "test-mes-registration",
            })
            .Build();

        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(nameof(Mes_cap_registration_discovers_maintenance_asset_event_subscribers)));

        services.AddMesCapIntegrationEvents(configuration, "Development");

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates().ToArray();

        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, AssetUnavailableTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, AssetRestoredTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, SchedulePlanReleasedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, SchedulePlanInvalidatedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, NcrDispositionDecidedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, InspectionResultTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, StockMovementPostedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, StockMovementPostingFailedTopic));
    }

    [Fact]
    public void Mes_testing_cap_registration_discovers_subscribers_without_storage_wiring()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "test-mes-registration-testing",
            })
            .Build();

        services.AddLogging();
        services.AddMediatR(options => options.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(nameof(Mes_testing_cap_registration_discovers_subscribers_without_storage_wiring)));

        services.AddMesCapIntegrationEvents(configuration, "Testing", isTesting: true);

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates().ToArray();

        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, AssetUnavailableTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, AssetRestoredTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, SchedulePlanReleasedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, SchedulePlanInvalidatedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, NcrDispositionDecidedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, InspectionResultTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, StockMovementPostedTopic));
        Assert.Contains(candidates, candidate => CandidateSubscribesToTopic(candidate, StockMovementPostingFailedTopic));
        Assert.Null(provider.GetService<IStorageInitializer>());
    }

    [Fact]
    public void Mes_runtime_consumer_registration_includes_new_cap_consumer_types()
    {
        var services = new ServiceCollection();

        services.AddMesIntegrationEventConsumers();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [PostgreSqlFact]
    [Trait("Category", "cap-inmemory")]
    public async Task PostgreSQL_cap_with_inmemory_messaging_delivers_asset_unavailable_event_to_mes_consumer()
    {
        var adminConnectionString = ReadPostgresConnectionString();
        await using var database = await DisposablePostgresDatabase.CreateAsync(adminConnectionString, "mes_cap_inmemory");
        await using var factory = CreateFactory(database.ConnectionString);
        await MigrateAsync(factory);
        await InitializeCapStorageAsync(factory);
        await SeedScheduleFactsAsync(factory);

        await PublishAsync(factory, CreateUnavailableEvent(DateTimeOffset.Parse("2026-05-23T08:00:00Z")));

        await AssertEventuallyAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var window = await dbContext.WorkCenterUnavailabilities.SingleOrDefaultAsync(x => x.DeviceAssetId == "ASSET-CNC-01");
            var result = await dbContext.ScheduleResults.SingleOrDefaultAsync();

            Assert.True(window is not null, "MES CAP consumer should persist the work-center unavailable window.");
            Assert.Equal("WC-A", window.WorkCenterId);
            Assert.Null(window.ToUtc);
            Assert.True(result is not null, "MES CAP consumer should auto-reschedule after asset unavailable.");
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = connectionString,
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = "test-mes-inmemory",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(settings);
                });
            });
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task InitializeCapStorageAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(CancellationToken.None);

        var selector = scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates().ToArray();
        if (!candidates.Any(candidate => CandidateSubscribesToTopic(candidate, AssetUnavailableTopic)))
        {
            var discovered = string.Join(", ", candidates.Select(DescribeCandidate));
            throw new InvalidOperationException($"CAP subscriber '{AssetUnavailableTopic}' was not discovered. Discovered subscribers: {discovered}");
        }
    }

    private static async Task SeedScheduleFactsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var now = DateTimeOffset.Parse("2026-05-23T08:00:00Z");
        var store = scope.ServiceProvider.GetRequiredService<IMesPlanningStore>();
        store.MapDeviceAssetToWorkCenter("ASSET-CNC-01", "WC-A");
        store.AddWorkOrder(new PlannedWorkOrder("org-001", "env-dev", "WO-001", "SKU-1", null, 1m, 10, now.AddDays(1)));
        store.AddOperationTask(new PlannedOperationTask("WO-001", "OP-10", OperationTaskStatus.Queued, 10, "WC-A", [], now, TimeSpan.FromHours(2)));
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
    }

    private static async Task PublishAsync(WebApplicationFactory<Program> factory, AssetUnavailableIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<ICapPublisher>();
        await publisher.PublishAsync(AssetUnavailableTopic, integrationEvent);
    }

    private static AssetUnavailableIntegrationEvent CreateUnavailableEvent(DateTimeOffset fromUtc)
    {
        return new AssetUnavailableIntegrationEvent(
            "evt-mes-cap-asset-unavailable",
            MaintenanceIntegrationEventTypes.AssetUnavailable,
            MaintenanceIntegrationEventVersions.V1,
            fromUtc,
            MaintenanceIntegrationEventSources.Maintenance,
            "corr-mes-cap",
            "cause-mes-cap",
            "org-001",
            "env-dev",
            "maintenance",
            "maintenance.AssetUnavailable:ASSET-CNC-01:20260523080000",
            new AssetUnavailablePayload("ASSET-CNC-01", "breakdown", fromUtc));
    }

    private static bool CandidateSubscribesToTopic(object candidate, string topic)
    {
        return DescribeCandidate(candidate).Contains(topic, StringComparison.Ordinal);
    }

    private static string DescribeCandidate(object candidate)
    {
        var properties = candidate.GetType()
            .GetProperties()
            .Select(property => $"{property.Name}={property.GetValue(candidate)}");
        return $"{candidate.GetType().Name}({string.Join("; ", properties)})";
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

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static string ReadPostgresConnectionString()
    {
        return MesPostgreSqlTestSettings.ReadConnectionString();
    }

    private sealed class DisposablePostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private DisposablePostgresDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            this.adminConnectionString = adminConnectionString;
            this.databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<DisposablePostgresDatabase> CreateAsync(string adminConnectionString, string prefix)
        {
            var databaseName = $"{prefix}_{Guid.NewGuid():N}";
            await using var adminConnection = new NpgsqlConnection(adminConnectionString);
            await adminConnection.OpenAsync();
            await using var createCommand = adminConnection.CreateCommand();
            createCommand.CommandText = $"""CREATE DATABASE "{databaseName}";""";
            await createCommand.ExecuteNonQueryAsync();

            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            };
            return new DisposablePostgresDatabase(adminConnectionString, databaseName, builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var cleanupConnection = new NpgsqlConnection(adminConnectionString);
            await cleanupConnection.OpenAsync();
            await using var terminateCommand = cleanupConnection.CreateCommand();
            terminateCommand.CommandText = """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName;
                """;
            terminateCommand.Parameters.AddWithValue("databaseName", databaseName);
            await terminateCommand.ExecuteNonQueryAsync();

            await using var dropCommand = cleanupConnection.CreateCommand();
            dropCommand.CommandText = $"""DROP DATABASE IF EXISTS "{databaseName}";""";
            await dropCommand.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        var connectionString = MesPostgreSqlTestSettings.ReadConnectionString();
        if (!MesPostgreSqlTestSettings.CanConnect(connectionString))
        {
            Skip = $"PostgreSQL unavailable for MES CAP acceptance test: {MesPostgreSqlTestSettings.Describe(connectionString)}";
        }
    }
}

internal static class MesPostgreSqlTestSettings
{
    public static string ReadConnectionString()
    {
        return Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__BusinessMesDb")
            ?? "Host=localhost;Port=15432;Database=nerv_iip_mes_test;Username=postgres;Password=postgres";
    }

    public static bool CanConnect(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            builder.Timeout = Math.Min(builder.Timeout, 2);
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string Describe(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"Host={builder.Host};Port={builder.Port};Database={builder.Database}";
        }
        catch (ArgumentException)
        {
            return "connection string could not be parsed";
        }
    }
}
