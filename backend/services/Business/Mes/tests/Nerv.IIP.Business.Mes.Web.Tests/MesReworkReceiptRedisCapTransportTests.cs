using System.Collections.Concurrent;
using DotNetCore.CAP;
using DotNetCore.CAP.Filter;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Messages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Testing;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesReworkReceiptRedisCapTransportTests(ITestOutputHelper output)
{
    private const string DeploymentProfile = "Issue3010Acceptance";
    private const string DiagnosticEventId = "evt-rework-diagnostic";
    private const string SensitiveMarker = "mock-secret-payload-do-not-output";

    [MesReworkReceiptPostgresRedisFact]
    public async Task Concurrent_distinct_events_for_one_ncr_emit_one_created_receipt_after_both_deliveries_succeed()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await VerifyReceivedIdentityAsync(factory);
        await VerifyFailurePreservationAsync();
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(
            factory.Services,
            "org-transport",
            "env-transport");

        var firstEvent = NcrReworkRequestedPostgresFixtures.CreateEvent(
            eventId: "evt-rework-transport-001",
            organizationId: "org-transport",
            environmentId: "env-transport",
            idempotencyKey: "quality:rework:org-transport:env-transport:ncr-001");
        var secondEvent = firstEvent with { EventId = "evt-rework-transport-002" };
        await Task.WhenAll(
            PublishAsync(factory, firstEvent),
            PublishAsync(factory, secondEvent));

        var probe = factory.Services.GetRequiredService<ReworkReceiptTransportProbe>();
        var concurrencyGate = factory.Services.GetRequiredService<DistinctNcrDeliveryGate>();
        await PreserveFailureAsync(() => Eventually.AssertAsync(
            condition: "both concurrent NCR deliveries succeed before MES exposes one durable receipt",
            assertion: async token =>
            {
                using var assertionScope = factory.Services.CreateScope();
                var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var identity = InputIdentity(factory);
                AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, identity, firstEvent.EventId, token));
                AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, identity, secondEvent.EventId, token));
                Assert.Equal(
                    [firstEvent.EventId, secondEvent.EventId],
                    concurrencyGate.EventIds.Order(StringComparer.Ordinal).ToArray());
                var rework = await db.WorkOrders.AsNoTracking()
                    .SingleAsync(x => x.SourceNcrId == "ncr-001", token);
                Assert.Equal(WorkOrder.ReworkType, rework.WorkOrderType);
                Assert.Equal(2, await db.OperationTasks.CountAsync(x => x.WorkOrderId == rework.WorkOrderIdValue, token));
                Assert.Single(await db.ProcessedIntegrationEvents
                    .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
                    .AsNoTracking()
                    .ToArrayAsync(token));
                Assert.Equal(
                    1,
                    await db.Database.SqlQueryRaw<int>(
                            "SELECT count(*)::int AS \"Value\" FROM cap.published WHERE \"Content\" LIKE '%ReworkWorkOrderCreated%' AND \"Content\" LIKE '%\"SourceNcrId\":\"ncr-001\"%'")
                        .SingleAsync(token));
                var delivered = Assert.Single(probe.Receipts);
                Assert.Equal("ncr-001", delivered.Payload.SourceNcrId);
                Assert.Equal(rework.WorkOrderIdValue, delivered.Payload.ReworkWorkOrderId);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), [])).AsTask(),
            () => CaptureAsync(factory), output.WriteLine);

        // 在原业务断言完成后经真实 Redis/CAP 投递一个受控回执，证明首次异常不会被重试覆盖。
        var receipt = Assert.Single(probe.Receipts);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(
                nameof(ReworkWorkOrderCreatedIntegrationEvent),
                receipt with { EventId = DiagnosticEventId, CausationId = SensitiveMarker });
        }

        await PreserveFailureAsync(() => Eventually.AssertAsync(
            condition: "controlled receipt failure retains first and subsequent CAP attempts",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rows = await ReadReceivedAsync(db, token);
                var failed = Assert.Single(rows, row => row.EventId == DiagnosticEventId);
                Assert.Equal("Failed", failed.StatusName);
                Assert.Equal(3, failed.Retries);
                var observations = concurrencyGate.ObservationsFor(DiagnosticEventId);
                Assert.Equal([0, 1, 2], observations.Where(x => x.Stage == "filter-enter").Select(x => x.Retries));
                Assert.Equal(["InvalidDataException>InvalidOperationException", "FormatException", "FormatException"],
                    observations.Where(x => x.Stage == "subscriber-exception").Select(x => x.ExceptionTypes));
                Assert.Equal(3, observations.Count(x => x.Stage == "filter-passed"));
                Assert.DoesNotContain(observations, x => x.Stage == "subscriber-completed");
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), [])).AsTask(),
            () => CaptureAsync(factory), output.WriteLine);
        var summary = await CaptureAsync(factory);
        Assert.DoesNotContain(SensitiveMarker, summary);
        Assert.Contains("InvalidDataException>InvalidOperationException", summary);
        Assert.Contains("FormatException", summary);
        Assert.Contains("retries=3 status=Failed", summary);
        output.WriteLine(summary);
    }

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        NcrReworkRequestedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(NcrReworkRequestedIntegrationEvent), integrationEvent);
    }

    private static Task<string[]> ReadReceivedStatusesAsync(
        ApplicationDbContext db,
        (string Name, string Group) identity,
        string eventId,
        CancellationToken cancellationToken) =>
        db.Database.SqlQuery<string>($"""
            SELECT "StatusName" AS "Value" FROM cap.received
            WHERE "Name" = {identity.Name} AND "Group" = {identity.Group}
              AND "Content"::jsonb -> 'Value' ->> 'EventId' = {eventId}
            ORDER BY "Id"
            """)
            .ToArrayAsync(cancellationToken);

    private static (string Name, string Group) InputIdentity(WebApplicationFactory<Program> factory)
    {
        var descriptor = Assert.Single(factory.Services.GetRequiredService<IConsumerServiceSelector>().SelectCandidates(),
            candidate => candidate.ImplTypeInfo.AsType() == typeof(NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder));
        return (descriptor.TopicName, descriptor.Attribute.Group!);
    }

    private static async Task VerifyReceivedIdentityAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IDataStorage>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identity = InputIdentity(factory);
        const string target = "evt-identity-counterexample";
        var input = NcrReworkRequestedPostgresFixtures.CreateEvent(eventId: target);
        var records = new List<MediumMessage>();
        async Task<MediumMessage> StoreAsync(string name, string group, object value, StatusName status)
        {
            var message = await storage.StoreReceivedMessageAsync(name, group,
                new Message(new Dictionary<string, string?> { [Headers.MessageId] = "3199001" }, value));
            records.Add(message);
            // 固定样本不交给后台重试；这里只验证 CAP serializer/storage 的真实身份查询。
            message.Retries = factory.Services.GetRequiredService<IOptions<CapOptions>>().Value.FailedRetryCount;
            message.ExpiresAt = message.Added.AddHours(1);
            await storage.ChangeReceiveStateAsync(message, status);
            return message;
        }

        try
        {
            await StoreAsync(identity.Name, identity.Group, input, StatusName.Succeeded);
            await StoreAsync(identity.Name + ".wrong", identity.Group, input, StatusName.Failed);
            await StoreAsync(identity.Name, identity.Group + ".wrong", input, StatusName.Failed);
            await StoreAsync(identity.Name, identity.Group,
                input with { EventId = "evt-other", CausationId = target, CorrelationId = target }, StatusName.Failed);
            var receipt = new ReworkWorkOrderCreatedIntegrationEvent(
                "evt-receipt-counterexample", "ReworkWorkOrderCreated", 1, input.OccurredAtUtc,
                "business-mes", target, target, input.OrganizationId, input.EnvironmentId, "test", "identity-fixture",
                new("ncr-001", "NCR-001", "rework-counterexample", "source-001", null, "SKU-001", 1m, null, null, input.OccurredAtUtc));
            var receiptRecord = await StoreAsync(nameof(ReworkWorkOrderCreatedIntegrationEvent), "receipt-counterexample", receipt, StatusName.Failed);
            AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, identity, target, CancellationToken.None));
            var oldStatuses = await db.Database.SqlQuery<string>(
                $"SELECT \"StatusName\" AS \"Value\" FROM cap.received WHERE \"Content\" LIKE {'%' + target + '%'}").ToArrayAsync();
            Assert.NotNull(Record.Exception(() => AssertReceivedSucceeded(oldStatuses)));
            // 同一输入的第二条 received 不能被 DISTINCT/First 遮蔽。
            var duplicate = await StoreAsync(identity.Name, identity.Group, input, StatusName.Failed);
            var duplicateStatuses = await ReadReceivedStatusesAsync(db, identity, target, CancellationToken.None);
            Assert.Equal(2, duplicateStatuses.Length);
            Assert.NotNull(Record.Exception(() => AssertReceivedSucceeded(duplicateStatuses)));
            await storage.ChangeReceiveStateAsync(duplicate, StatusName.Succeeded);
            AssertReceivedSucceeded(await ReadReceivedStatusesAsync(db, identity, target, CancellationToken.None));
            await storage.DeleteReceivedMessageAsync(long.Parse(records[0].DbId));
            await storage.DeleteReceivedMessageAsync(long.Parse(duplicate.DbId));
            await storage.ChangeReceiveStateAsync(receiptRecord, StatusName.Succeeded);
            var missing = await ReadReceivedStatusesAsync(db, identity, target, CancellationToken.None);
            Assert.Empty(missing);
            Assert.NotNull(Record.Exception(() => AssertReceivedSucceeded(missing)));
        }
        finally
        {
            foreach (var record in records)
            {
                await storage.DeleteReceivedMessageAsync(long.Parse(record.DbId));
            }
        }
    }

    private static async Task PreserveFailureAsync(Func<Task> action, Func<Task<string>> capture, Action<string> write)
    {
        try
        {
            await action();
        }
        catch
        {
            try
            {
                write(await capture());
            }
            catch
            {
                // 观测失败只输出固定状态；绝不改变原始异常。
                try { write("received-diagnostics unavailable"); }
                catch { /* 输出 sink 也可能已释放，仍由原失败决定结果。 */ }
            }
            throw;
        }
    }

    private static async Task VerifyFailurePreservationAsync()
    {
        var original = new InvalidDataException(SensitiveMarker);
        var lines = new List<string>();
        var observed = await Record.ExceptionAsync(() => PreserveFailureAsync(
            () => Task.FromException(original),
            () => Task.FromException<string>(new FormatException(SensitiveMarker)), lines.Add));
        Assert.Same(original, observed);
        Assert.Equal(["received-diagnostics unavailable"], lines);
    }

    private sealed record ReceivedRow(string Id, string MessageId, string EventId, string Name, string Group, int Retries, string StatusName);

    private static Task<ReceivedRow[]> ReadReceivedAsync(ApplicationDbContext db, CancellationToken token) =>
        db.Database.SqlQueryRaw<ReceivedRow>("""
            SELECT "Id"::text AS "Id", "Content"::jsonb -> 'Headers' ->> 'cap-msg-id' AS "MessageId",
                "Content"::jsonb -> 'Value' ->> 'EventId' AS "EventId", "Name", "Group", "Retries", "StatusName"
            FROM cap.received ORDER BY "Id"
            """).ToArrayAsync(token);

    private static async Task<string> CaptureAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var rows = await ReadReceivedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), CancellationToken.None);
        return factory.Services.GetRequiredService<DistinctNcrDeliveryGate>().Describe(rows);
    }

    private static void AssertReceivedSucceeded(string[] statuses)
    {
        Assert.NotEmpty(statuses);
        Assert.All(statuses, status => Assert.Equal("Succeeded", status));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["ProductEngineering:BaseUrl"] = "https://product-engineering.test",
            ["Inventory:BaseUrl"] = "https://inventory.test",
            ["MasterData:BaseUrl"] = "https://master-data.test",
            ["Quality:BaseUrl"] = "https://quality.test",
            ["Approval:BaseUrl"] = "https://approval.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IMesMaterialRequirementSnapshotProvider>(_ => NoRequirementsSnapshotProvider.Instance);
                services.AddSingleton<ReworkReceiptTransportProbe>();
                services.AddSingleton<DistinctNcrDeliveryGate>();
                services.AddSingleton<ISubscribeFilter>(provider =>
                    provider.GetRequiredService<DistinctNcrDeliveryGate>());
                services.PostConfigure<CapOptions>(options =>
                {
                    options.SucceedMessageExpiredAfter = 3600;
                    options.CollectorCleaningInterval = 3600;
                    options.FailedRetryInterval = 1;
                    options.ConsumerThreadCount = 2;
                    options.EnableSubscriberParallelExecute = true;
                    options.SubscriberParallelExecuteThreadCount = 2;
                });
            });
        });
    }

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);

        var candidates = scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates();
        Assert.Contains(candidates, candidate =>
            candidate.ImplTypeInfo.AsType() == typeof(NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder));
        var observer = Assert.Single(candidates, candidate =>
            candidate.ImplTypeInfo.AsType() == typeof(ReworkReceiptTransportProbe));
        var prefix = scope.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        Assert.Equal($"{prefix}.{nameof(ReworkWorkOrderCreatedIntegrationEvent)}", observer.TopicName);
    }

    public sealed class ReworkReceiptTransportProbe : ICapSubscribe
    {
        private readonly ConcurrentQueue<ReworkWorkOrderCreatedIntegrationEvent> receipts = new();
        private int diagnosticAttempts;

        public IReadOnlyCollection<ReworkWorkOrderCreatedIntegrationEvent> Receipts => receipts.ToArray();

        [CapSubscribe(nameof(ReworkWorkOrderCreatedIntegrationEvent), Group = "business-mes.issue3010-rework-receipt-probe")]
        public Task ObserveAsync(
            ReworkWorkOrderCreatedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            if (integrationEvent.EventId == DiagnosticEventId)
            {
                if (Interlocked.Increment(ref diagnosticAttempts) == 1)
                {
                    throw new InvalidDataException(SensitiveMarker, new InvalidOperationException(SensitiveMarker));
                }
                throw new FormatException(SensitiveMarker);
            }
            receipts.Enqueue(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class DistinctNcrDeliveryGate : SubscribeFilter
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<string> eventIds = new(StringComparer.Ordinal);
        private readonly List<Observation> observations = [];
        private readonly Dictionary<string, (string EventId, string Category, string Stage)> deliveries = [];
        private int sequence;

        internal sealed record Observation(int Sequence, string ReceivedId, string MessageId, string EventId,
            string Category, int Retries, string Stage, string ExceptionTypes);

        public Observation[] ObservationsFor(string eventId)
        {
            lock (eventIds) { return observations.Where(x => x.EventId == eventId).ToArray(); }
        }

        private void Observe(FilterContext context, string stage, Exception? exception = null)
        {
            lock (eventIds)
            {
                var delivery = deliveries[context.MediumMessage.DbId];
                deliveries[context.MediumMessage.DbId] = (delivery.EventId, delivery.Category, stage);
                var types = new List<string>();
                for (var current = exception; current is not null && types.Count < 4; current = current.InnerException)
                {
                    types.Add(current.GetType().Name);
                }
                sequence++;
                if (observations.Count < 64)
                {
                    observations.Add(new(sequence, context.MediumMessage.DbId, context.DeliverMessage.GetId(),
                        delivery.EventId, delivery.Category, context.MediumMessage.Retries, stage, string.Join(">", types)));
                }
            }
        }

        public string Describe(ReceivedRow[] rows)
        {
            lock (eventIds)
            {
                var receivedAliases = observations.Select(x => x.ReceivedId).Concat(rows.Select(x => x.Id))
                    .Distinct(StringComparer.Ordinal).Select((id, index) => (id, alias: $"received-{index + 1}"))
                    .ToDictionary(x => x.id, x => x.alias, StringComparer.Ordinal);
                var messageAliases = observations.Select(x => x.MessageId).Concat(rows.Select(x => x.MessageId))
                    .Distinct(StringComparer.Ordinal).Select((id, index) => (id, alias: $"message-{index + 1}"))
                    .ToDictionary(x => x.id, x => x.alias, StringComparer.Ordinal);
                var lines = new List<string> { $"received-diagnostics observations={sequence} retained={observations.Count} save=unknown commit=unknown" };
                foreach (var observation in observations)
                {
                    lines.Add($"seq={observation.Sequence} {receivedAliases[observation.ReceivedId]} {messageAliases[observation.MessageId]} category={observation.Category} attempt={observation.Retries + 1} retries={observation.Retries} stage={observation.Stage} exception-types={observation.ExceptionTypes}");
                }
                foreach (var row in rows.Take(64))
                {
                    var category = deliveries.TryGetValue(row.Id, out var delivery) ? delivery.Category : "not-observed";
                    var status = row.StatusName is "Scheduled" or "Queued" or "Succeeded" or "Failed" ? row.StatusName : "unknown";
                    lines.Add($"{receivedAliases[row.Id]} {messageAliases[row.MessageId]} category={category} retries={row.Retries} status={status}");
                }
                return string.Join(Environment.NewLine, lines);
            }
        }

        public IReadOnlyCollection<string> EventIds
        {
            get
            {
                lock (eventIds)
                {
                    return eventIds.ToArray();
                }
            }
        }

        public override async Task OnSubscribeExecutingAsync(ExecutingContext context)
        {
            var value = context.Arguments.Single(x => x is NcrReworkRequestedIntegrationEvent or ReworkWorkOrderCreatedIntegrationEvent)!;
            var (id, category) = value is NcrReworkRequestedIntegrationEvent input
                ? (input.EventId, input.EventId == "evt-rework-transport-001" ? "input-first" : "input-second")
                : (((ReworkWorkOrderCreatedIntegrationEvent)value).EventId,
                    ((ReworkWorkOrderCreatedIntegrationEvent)value).EventId == DiagnosticEventId ? "receipt-controlled" : "receipt");
            lock (eventIds) { deliveries[context.MediumMessage.DbId] = (id, category, "filter-enter"); }
            Observe(context, "filter-enter");
            var integrationEvent = context.Arguments
                .OfType<NcrReworkRequestedIntegrationEvent>()
                .SingleOrDefault();
            if (integrationEvent is null)
            {
                Observe(context, "filter-passed");
                return;
            }

            lock (eventIds)
            {
                eventIds.Add(integrationEvent.EventId);
                if (eventIds.Count == 2)
                {
                    release.SetResult();
                }
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Observe(context, "filter-passed");
        }

        public override Task OnSubscribeExecutedAsync(ExecutedContext context)
        {
            Observe(context, "subscriber-completed");
            return Task.CompletedTask;
        }

        public override Task OnSubscribeExceptionAsync(ExceptionContext context)
        {
            string stage;
            lock (eventIds)
            {
                stage = deliveries[context.MediumMessage.DbId].Stage == "filter-passed" ? "subscriber-exception" : "filter-exception";
            }
            Observe(context, stage, context.Exception);
            return Task.CompletedTask;
        }
    }

    private sealed class NoRequirementsSnapshotProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsSnapshotProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MesReworkReceiptPostgresRedisFactAttribute : FactAttribute
{
    public MesReworkReceiptPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP MES rework-receipt producer proof.";
        }
    }
}
