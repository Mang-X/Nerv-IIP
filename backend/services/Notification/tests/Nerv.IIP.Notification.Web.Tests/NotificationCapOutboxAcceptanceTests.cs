using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;
using Npgsql;
using System.Data;
using System.Net.Sockets;

namespace Nerv.IIP.Notification.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class NotificationCapOutboxAcceptanceTests
{
    private const string TopicName = "OperationTaskFailedIntegrationEvent";

    [Fact]
    [Trait("Category", "cap-inmemory")]
    public async Task PostgreSQL_cap_outbox_with_inmemory_messaging_delivers_operation_failed_event_to_notification_consumer()
    {
        var adminConnectionString = ReadPostgresConnectionString();
        if (!await CanConnectPostgresAsync(adminConnectionString))
        {
            return;
        }

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_notification_cap_inmemory");
        await using var factory = CreateFactory(database.ConnectionString, "InMemory");
        await MigrateAsync(factory, database);
        await InitializeCapStorageAsync(factory);
        using var client = factory.CreateClient();

        await PublishAsync(factory, CreateFailedEvent("event-cap-inmemory", "operation-task-failed:cap-inmemory"), useTransaction: false);

        await AssertEventuallyAsync(async token =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents
                .Include(x => x.Messages)
                .SingleOrDefaultAsync(x => x.SourceEventId == "event-cap-inmemory", token);
            var published = await CapTableHasRowsAsync(dbContext, "published", token);
            var received = await CapTableHasRowsAsync(dbContext, "received", token);

            Assert.True(intent is not null, await ReadCapDebugAsync(dbContext, token));
            Assert.Equal(NotificationContractConstants.IntentTypeTask, intent.IntentType);
            Assert.True(published, "CAP outbox should record the published message.");
            Assert.True(received, "CAP inbox should record the consumed message.");
        });
    }

    [Fact]
    [Trait("Category", "cap-rabbitmq")]
    public async Task PostgreSQL_cap_outbox_with_rabbitmq_messaging_delivers_operation_failed_event_to_notification_consumer()
    {
        var adminConnectionString = ReadPostgresConnectionString();
        var rabbitMqHost = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_HOST") ?? "localhost";
        var rabbitMqPort = ReadInt("NERV_IIP_TEST_RABBITMQ_PORT", 5672);
        if (!await CanConnectPostgresAsync(adminConnectionString) || !await CanConnectTcpAsync(rabbitMqHost, rabbitMqPort))
        {
            return;
        }

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_notification_cap_rabbitmq");
        await using var factory = CreateFactory(
            database.ConnectionString,
            "RabbitMQ",
            new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = rabbitMqHost,
                ["RabbitMQ:Port"] = rabbitMqPort.ToString(),
                ["RabbitMQ:UserName"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_USERNAME") ?? "guest",
                ["RabbitMQ:Password"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_PASSWORD") ?? "guest",
            });
        await MigrateAsync(factory, database);
        await InitializeCapStorageAsync(factory);
        using var client = factory.CreateClient();

        await PublishAsync(factory, CreateFailedEvent("event-cap-rabbitmq", "operation-task-failed:cap-rabbitmq"), useTransaction: true);

        await AssertEventuallyAsync(async token =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents
                .Include(x => x.Messages)
                .SingleOrDefaultAsync(x => x.SourceEventId == "event-cap-rabbitmq", token);
            var published = await CapTableHasRowsAsync(dbContext, "published", token);
            var received = await CapTableHasRowsAsync(dbContext, "received", token);

            Assert.True(intent is not null, await ReadCapDebugAsync(dbContext, token));
            Assert.Equal(NotificationContractConstants.IntentTypeTask, intent.IntentType);
            Assert.True(published, "CAP outbox should record the published message.");
            Assert.True(received, "CAP inbox should record the consumed message.");
        });
    }

    [Fact]
    [Trait("Category", "cap-rabbitmq-dlq")]
    public async Task Rabbitmq_handler_exception_dead_letters_after_retry_threshold_and_continues_consuming()
    {
        var adminConnectionString = ReadPostgresConnectionString();
        var rabbitMqHost = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_HOST") ?? "localhost";
        var rabbitMqPort = ReadInt("NERV_IIP_TEST_RABBITMQ_PORT", 5672);
        if (!await CanConnectPostgresAsync(adminConnectionString) || !await CanConnectTcpAsync(rabbitMqHost, rabbitMqPort))
        {
            return;
        }

        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_notification_cap_rabbitmq_dlq");
        await using var factory = CreateFactory(
            database.ConnectionString,
            "RabbitMQ",
            new Dictionary<string, string?>
            {
                ["RabbitMQ:HostName"] = rabbitMqHost,
                ["RabbitMQ:Port"] = rabbitMqPort.ToString(),
                ["RabbitMQ:UserName"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_USERNAME") ?? "guest",
                ["RabbitMQ:Password"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_RABBITMQ_PASSWORD") ?? "guest",
            });
        await MigrateAsync(factory, database);
        await InitializeCapStorageAsync(factory);
        using var client = factory.CreateClient();

        await PublishAsync(
            factory,
            CreateFailedEvent("event-cap-rabbitmq-poison", "operation-task-failed:cap-rabbitmq-poison")
                with
                {
                    Payload = new OperationTaskFailedPayload(
                        OperationTaskId: string.Empty,
                        AttemptId: "attempt-event-cap-rabbitmq-poison",
                        InstanceKey: "demo-api-001",
                        OperationCode: "lifecycle.restart",
                        FinishedAtUtc: DateTimeOffset.Parse("2026-05-25T08:00:05Z"),
                        FailureCode: "timeout")
                },
            useTransaction: true);

        await AssertEventuallyAsync(async token =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deadLetter = await dbContext.Set<IntegrationEventDeadLetter>()
                .SingleOrDefaultAsync(x => x.EventId == "event-cap-rabbitmq-poison", token);

            Assert.True(deadLetter is not null, await ReadCapDebugAsync(dbContext, token));
            Assert.Equal(IntegrationEventCapFailureDeadLetterer.HandlerRetryExhaustedFailureCode, deadLetter.FailureCode);
            Assert.Equal(IntegrationEventDeadLetterStatus.Pending, deadLetter.Status);
            Assert.StartsWith(OperationTaskFailedIntegrationEventHandlerForNotification.ConsumerName, deadLetter.ConsumerName, StringComparison.Ordinal);
        });

        await PublishAsync(factory, CreateFailedEvent("event-cap-rabbitmq-after-poison", "operation-task-failed:cap-rabbitmq-after-poison"), useTransaction: true);

        await AssertEventuallyAsync(async token =>
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents
                .SingleOrDefaultAsync(x => x.SourceEventId == "event-cap-rabbitmq-after-poison", token);

            Assert.True(intent is not null, await ReadCapDebugAsync(dbContext, token));
            Assert.Equal(NotificationContractConstants.IntentTypeTask, intent.IntentType);
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        string messagingProvider,
        Dictionary<string, string?>? extraSettings = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var capVersion = $"t-{messagingProvider.ToLowerInvariant()}-{Guid.NewGuid():N}"[..20];
                var settings = new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "PostgreSQL",
                    ["ConnectionStrings:NotificationDb"] = connectionString,
                    ["Messaging:Provider"] = messagingProvider,
                    ["Cap:Version"] = capVersion,
                    ["Cap:FailedRetryCount"] = "1",
                    ["Cap:FailedRetryInterval"] = "1",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };

                if (extraSettings is not null)
                {
                    foreach (var (key, value) in extraSettings)
                    {
                        settings[key] = value;
                    }
                }

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(settings);
                });

                builder.ConfigureServices(services =>
                {
                    services.Configure<CapOptions>(options =>
                    {
                        options.FailedRetryCount = 1;
                        options.FailedRetryInterval = 1;
                    });
                });
            });
    }

    private static async Task MigrateAsync(
        WebApplicationFactory<Program> factory,
        PostgreSqlTestDatabase database)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.AssertOwns(dbContext.Database.GetConnectionString());
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
        if (!candidates.Any(CandidateSubscribesToTopic))
        {
            var discovered = string.Join(", ", candidates.Select(DescribeCandidate));
            throw new InvalidOperationException($"CAP subscriber '{TopicName}' was not discovered. Discovered subscribers: {discovered}");
        }
    }

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        OperationTaskFailedIntegrationEvent integrationEvent,
        bool useTransaction)
    {
        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<ICapPublisher>();
        if (!useTransaction)
        {
            await publisher.PublishAsync(TopicName, integrationEvent);
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(publisher, autoCommit: true);
        await publisher.PublishAsync(TopicName, integrationEvent);
    }

    private static async Task<bool> CapTableHasRowsAsync(
        ApplicationDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT EXISTS (SELECT 1 FROM cap."{tableName}");""";
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<string> ReadCapDebugAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var published = await ReadCapRowsAsync(dbContext, "published", cancellationToken);
        var received = await ReadCapRowsAsync(dbContext, "received", cancellationToken);
        return $"Notification intent was not created. CAP published={published}; CAP received={received}";
    }

    private static async Task<string> ReadCapRowsAsync(
        ApplicationDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT COALESCE(jsonb_agg(to_jsonb(rows)), '[]'::jsonb)::text
                FROM (SELECT * FROM cap."{tableName}" LIMIT 5) rows;
                """;
            return (await command.ExecuteScalarAsync(cancellationToken))?.ToString() ?? "[]";
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool CandidateSubscribesToTopic(object candidate)
    {
        return DescribeCandidate(candidate).Contains(TopicName, StringComparison.Ordinal);
    }

    private static string DescribeCandidate(object candidate)
    {
        var properties = candidate.GetType()
            .GetProperties()
            .Select(property => $"{property.Name}={property.GetValue(candidate)}");
        return $"{candidate.GetType().Name}({string.Join("; ", properties)})";
    }

    private static OperationTaskFailedIntegrationEvent CreateFailedEvent(string eventId, string idempotencyKey)
    {
        return new OperationTaskFailedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationTaskFailed",
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-25T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: $"cause-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "connector-host-001",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationTaskFailedPayload(
                OperationTaskId: $"task-{eventId}",
                AttemptId: $"attempt-{eventId}",
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                FinishedAtUtc: DateTimeOffset.Parse("2026-05-25T08:00:05Z"),
                FailureCode: "timeout"));
    }

    /// <summary>
    /// CAP delivery is real transport plus a real PostgreSQL round trip, so the completion instant is
    /// not knowable in advance — this polls the observable fact under a bounded budget. The hand-rolled
    /// loop this replaced could also pass silently: if the clock had already run past the deadline
    /// before the first attempt, it fell out of the loop with no observation and no failure.
    /// </summary>
    private static async Task AssertEventuallyAsync(Func<CancellationToken, Task> assertion)
    {
        await Eventually.AssertAsync(
            condition: "the notification CAP outbox assertion to hold",
            assertion: assertion,
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(30),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [ReadPostgresConnectionString()]));
    }

    private static string ReadPostgresConnectionString()
    {
        return Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDb")
            ?? "Host=localhost;Port=15432;Database=nerv_iip_notification_test;Username=postgres;Password=postgres";
    }

    private static async Task<bool> CanConnectPostgresAsync(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
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

    private static async Task<bool> CanConnectTcpAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static int ReadInt(string environmentVariable, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(environmentVariable), out var value) && value > 0
            ? value
            : defaultValue;
    }

}
