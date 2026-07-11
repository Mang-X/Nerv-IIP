using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsQualityInspectionGateConsumerTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private sealed record PostgresInspectionScenario(
        string EventType,
        string InboundOrderNo,
        string ExpectedGateStatus,
        bool AllowsPutaway,
        string TargetLocationCode);

    [Fact]
    public async Task Quality_passed_event_releases_wms_putaway_gate_for_received_stock()
    {
        var databaseName = nameof(Quality_passed_event_releases_wms_putaway_gate_for_received_stock);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-PASS-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-pass-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-QA-PASS-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-PASS-001");
        Assert.Equal(InboundOrderStatus.Completed, persistedInbound.Status);
        var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-PASS-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None);
        await assertionContext.SaveChangesAsync(CancellationToken.None);
        Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
    }

    [Fact]
    public async Task Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact()
    {
        var databaseName = nameof(Quality_rejected_event_keeps_putaway_blocked_and_records_supplier_return_fact);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-REJ-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-rej-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-QA-REJ-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-REJ-001");
        var supplierReturn = await assertionContext.Set<SupplierReturnRequest>().SingleAsync();
        Assert.Equal("IN-QA-REJ-001", supplierReturn.InboundOrderNo);
        Assert.Equal("QI-001", supplierReturn.InspectionRecordId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-REJ-001", "LINE-001", "LOC-STAGE", "LOC-A-01", 5m),
            CancellationToken.None));
    }

    [Fact]
    public async Task Quality_rejected_event_creates_supplier_return_outbound_with_immutable_receipt_reference()
    {
        var databaseName = nameof(Quality_rejected_event_creates_supplier_return_outbound_with_immutable_receipt_reference);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-RETURN-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-return-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(
                CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "IN-QA-RETURN-001"),
                CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var outbound = Assert.Single(await assertionContext.OutboundOrders.Include(x => x.Lines).ToListAsync());
        Assert.Equal("RTS-IN-QA-RETURN-001-LINE-001-QI-001", outbound.OutboundOrderNo);
        Assert.Equal("purchase-receipt-return", outbound.SourceDocumentType);
        Assert.Equal("IN-QA-RETURN-001", outbound.SourceDocumentId);
        var line = Assert.Single(outbound.Lines);
        Assert.Equal("LINE-001", line.LineNo);
        Assert.Equal(5m, line.RequestedQuantity);
    }

    [Fact]
    public async Task Quality_conditional_release_event_allows_restricted_putaway_gate()
    {
        var databaseName = nameof(Quality_conditional_release_event_allows_restricted_putaway_gate);
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-COND-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-cond-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionConditionalReleased, "IN-QA-COND-001"), CancellationToken.None);
        }

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-COND-001");
        var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
            new CreatePutawayTaskCommand(persistedInbound.Id, "PUT-QA-COND-001", "LINE-001", "LOC-STAGE", "LOC-RESTRICTED-01", 5m),
            CancellationToken.None);
        await assertionContext.SaveChangesAsync(CancellationToken.None);
        Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
    }

    [Fact]
    public async Task Quality_divergence_event_is_dead_lettered_without_retry_exception()
    {
        var databaseName = nameof(Quality_divergence_event_is_dead_lettered_without_retry_exception);
        var databaseRoot = new InMemoryDatabaseRoot();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await using (var dbContext = CreateContext(databaseName, databaseRoot))
        {
            var createdInbound = QualityRequiredInboundOrder("IN-QA-DIV-001");
            dbContext.InboundOrders.Add(createdInbound);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                new CompleteInboundOrderCommand(createdInbound.Id, "idem-in-div-001"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(dbContext, deadLetters);
            await handler.HandleAsync(
                CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "IN-QA-DIV-001", inspectedQuantity: 6m),
                CancellationToken.None);
        }

        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("quality-inspection-result-divergence", deadLetter.FailureCode);

        await using var assertionContext = CreateContext(databaseName, databaseRoot);
        var persistedInbound = await assertionContext.InboundOrders.SingleAsync(x => x.InboundOrderNo == "IN-QA-DIV-001");
        Assert.Equal(InboundOrderStatus.PendingQualityCheck, persistedInbound.Status);
    }

    [WmsRealPostgresFact]
    public async Task Quality_events_persist_gate_and_putaway_outcome_on_postgres()
    {
        var postgresConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await TemporaryPostgresDatabase.CreateAsync(postgresConnectionString, "wms_quality_gate");

        await using (var dbContext = CreatePostgresContext(database.ConnectionString))
        {
            await dbContext.Database.MigrateAsync();
            var handler = new QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());

            foreach (var scenario in PostgresInspectionScenarios())
            {
                var createdInbound = QualityRequiredInboundOrder(scenario.InboundOrderNo);
                dbContext.InboundOrders.Add(createdInbound);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                await new CompleteInboundOrderCommandHandler(dbContext).Handle(
                    new CompleteInboundOrderCommand(createdInbound.Id, $"idem-{scenario.InboundOrderNo.ToLowerInvariant()}"),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);

                await handler.HandleAsync(
                    CreateInspectionEvent(scenario.EventType, scenario.InboundOrderNo),
                    CancellationToken.None);
            }
        }

        await using var assertionContext = CreatePostgresContext(database.ConnectionString);
        foreach (var scenario in PostgresInspectionScenarios())
        {
            var persistedInbound = await assertionContext.InboundOrders
                .Include(x => x.Lines)
                .SingleAsync(x => x.InboundOrderNo == scenario.InboundOrderNo);
            var persistedLine = Assert.Single(persistedInbound.Lines);
            Assert.Equal(InboundOrderStatus.Completed, persistedInbound.Status);
            Assert.Equal(scenario.ExpectedGateStatus, persistedLine.QualityGateStatus);
            Assert.Equal("QI-001", persistedLine.InspectionRecordId);

            if (scenario.AllowsPutaway)
            {
                var task = await new CreatePutawayTaskCommandHandler(assertionContext).Handle(
                    new CreatePutawayTaskCommand(persistedInbound.Id, $"PUT-{scenario.InboundOrderNo}", "LINE-001", "LOC-STAGE", scenario.TargetLocationCode, 5m),
                    CancellationToken.None);
                await assertionContext.SaveChangesAsync(CancellationToken.None);
                Assert.True(await assertionContext.WarehouseTasks.AnyAsync(x => x.Id == task));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => new CreatePutawayTaskCommandHandler(assertionContext).Handle(
                    new CreatePutawayTaskCommand(persistedInbound.Id, $"PUT-{scenario.InboundOrderNo}", "LINE-001", "LOC-STAGE", scenario.TargetLocationCode, 5m),
                    CancellationToken.None));
            }
        }

        var supplierReturn = await assertionContext.SupplierReturnRequests.SingleAsync();
        Assert.Equal("IN-QA-PG-REJ-001", supplierReturn.InboundOrderNo);
        Assert.Equal("QI-001", supplierReturn.InspectionRecordId);
    }

    private static IReadOnlyCollection<PostgresInspectionScenario> PostgresInspectionScenarios()
    {
        return
        [
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionPassed,
                "IN-QA-PG-PASS-001",
                InboundQualityGateStatuses.Passed,
                true,
                "LOC-A-01"),
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                "IN-QA-PG-COND-001",
                InboundQualityGateStatuses.ConditionalReleased,
                true,
                "LOC-RESTRICTED-01"),
            new PostgresInspectionScenario(
                QualityIntegrationEventTypes.InspectionRejected,
                "IN-QA-PG-REJ-001",
                InboundQualityGateStatuses.Rejected,
                false,
                "LOC-A-01"),
        ];
    }

    private static InboundOrder QualityRequiredInboundOrder(string inboundOrderNo)
    {
        return InboundOrder.Create(
            "org-001",
            "env-dev",
            inboundOrderNo,
            "purchase-receipt",
            "PO-001",
            "SITE-01",
            [new InboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 5m, "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")]);
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(
        string eventType,
        string inboundOrderNo,
        decimal inspectedQuantity = 5m)
    {
        var result = eventType == QualityIntegrationEventTypes.InspectionPassed
            ? "passed"
            : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                ? "conditional-release"
                : "rejected";
        return new InspectionResultIntegrationEvent(
            "quality-event-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection-result:org-001:env-dev:QI-001:{eventType}",
            new InspectionResultPayload(
                "QI-001",
                "PLAN-001",
                "receiving",
                "wms",
                inboundOrderNo,
                "SKU-FG-1000",
                inspectedQuantity,
                result,
                eventType == QualityIntegrationEventTypes.InspectionRejected ? "critical-defect" : null,
                [],
                DateTimeOffset.UtcNow,
                new StockReleaseDimensionPayload("kg", "SITE-01", "LOC-STAGE", "LOT-001", null, "quality", "company", "owner-001")));
    }

    private static ApplicationDbContext CreateContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ApplicationDbContext CreatePostgresContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "wms"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class TemporaryPostgresDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private TemporaryPostgresDatabase(string adminConnectionString, string connectionString, string databaseName)
        {
            this.adminConnectionString = adminConnectionString;
            ConnectionString = connectionString;
            this.databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryPostgresDatabase> CreateAsync(string baseConnectionString, string prefix)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database,
            };
            var databaseName = $"nerv_iip_{prefix}_{Guid.CreateVersion7():N}";
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection);
            await command.ExecuteNonQueryAsync();
            return new TemporaryPostgresDatabase(adminBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();",
                connection))
            {
                terminate.Parameters.AddWithValue("databaseName", databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"""DROP DATABASE IF EXISTS "{databaseName}";""", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class WmsRealPostgresFactAttribute : FactAttribute
    {
        public WmsRealPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run this real PostgreSQL WMS quality gate consumer test.";
            }
        }
    }
}
