using System.Data;
using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using StackExchange.Redis;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
    private const decimal ClientSuppliedUnitCost = 99.99m;
    private const decimal ErpCapitalizedUnitCost = 12.34m;
    private const decimal ReceiptQuantity = 5m;

    [RealPostgresRedisMesInventoryFact]
    public async Task External_process_proves_exact_produced_lot_link_for_success_and_explicit_failure()
    {
        var mesPostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_MES_POSTGRES")!;
        var inventoryPostgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_INVENTORY_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION")!;
        var probeRunId = Environment.GetEnvironmentVariable("NERV_IIP_TEST_PROBE_RUN_ID")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = redis,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(options =>
            options.RegisterServicesFromAssembly(typeof(MesInventoryProducedLotPostgresRedisAcceptanceTests).Assembly));
        services.AddDbContext<MesDbContext>(options => options.UseNpgsql(
            mesPostgres,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema)));
        services.AddCap(options =>
        {
            options.Version = capVersion;
            options.UseEntityFramework<MesDbContext>();
            options.UseConfiguredTransport(configuration, "Development");
        });

        var source = await SeedReceiptPairAsync(mesPostgres, probeRunId);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
        await WaitForConsumerGroupsAsync(redis, capVersion);
        await SeedProducedLotsAsync(provider, source);
        var receiptBoundary = await CreateReceiptRequestsAsync(provider, source);
        source = receiptBoundary.Source;
        var publisher = provider.GetRequiredService<ICapPublisher>();
        var successEvent = receiptBoundary.SuccessEvent;
        var successReplayEvent = receiptBoundary.SuccessReplayEvent;
        var failureEvent = receiptBoundary.FailureEvent;
        var failureReplayEvent = receiptBoundary.FailureReplayEvent;
        var pendingEvent = receiptBoundary.PendingEvent;
        Assert.NotEqual(successEvent.EventId, successReplayEvent.EventId);
        Assert.Equal(successEvent.IdempotencyKey, successReplayEvent.IdempotencyKey);
        Assert.NotEqual(failureEvent.EventId, failureReplayEvent.EventId);
        Assert.Equal(failureEvent.IdempotencyKey, failureReplayEvent.IdempotencyKey);
        Assert.NotEqual(source.ClientSuppliedUnitCost, source.ErpCapitalizedUnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successReplayEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureReplayEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, pendingEvent.Payload.UnitCost);
        Assert.Equal(
            InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            pendingEvent.Payload.UnitCostAuthorityReference);
        Assert.Equal(source.ClientSuppliedUnitCost, receiptBoundary.SuccessCommandUnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, receiptBoundary.PendingCommandUnitCost);
        Assert.Null(receiptBoundary.PendingUnitCost);
        Assert.Equal(0, receiptBoundary.PendingDomainEventCount);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), successEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), successReplayEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), failureEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), failureReplayEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), pendingEvent);

        // Real cross-process Redis CAP transport: the observable facts live in the two PostgreSQL databases,
        // so poll them on a bounded budget instead of guessing a single completion instant.
        try
        {
            var observed = await Eventually.WaitAsync(
                condition: "MES and Inventory closed both Redis CAP produced-lot paths",
                observe: async token => (
                    Mes: await ReadMesFactsAsync(mesPostgres, source, token),
                    Inventory: await ReadInventoryFactsAsync(inventoryPostgres, source, token)),
                isSatisfied: state => state.Mes.SuccessStatus == "Posted"
                    && state.Mes.SuccessMovementId is not null
                    && state.Mes.FailureStatus == "InventoryPostingFailed"
                    && !string.IsNullOrWhiteSpace(state.Mes.FailureCode)
                    && state.Mes.SuccessUnitCost == source.ErpCapitalizedUnitCost
                    && state.Mes.ErpCapitalizedUnitCost == source.ErpCapitalizedUnitCost
                    && state.Mes.PendingStatus == "Requested"
                    && state.Mes.PendingUnitCost is null
                    && state.Mes.PendingErpCapitalizedUnitCost is null
                    && state.Mes.PendingPublishedMessageCount == 0
                    && state.Inventory.SuccessMovementCount == 1
                    && state.Inventory.FailureMovementCount == 0
                    && state.Inventory.PendingMovementCount == 0
                    && state.Inventory.SuccessRequestedUnitCost == source.ErpCapitalizedUnitCost
                    && state.Inventory.SuccessUnitCost == source.ErpCapitalizedUnitCost
                    && state.Inventory.SuccessMovementAmount == ReceiptQuantity * source.ErpCapitalizedUnitCost
                    && state.Inventory.LedgerMovingAverageUnitCost == source.ErpCapitalizedUnitCost
                    && state.Inventory.LedgerInventoryValue == ReceiptQuantity * source.ErpCapitalizedUnitCost,
                describe: state =>
                    $"SuccessStatus={state.Mes.SuccessStatus}, SuccessMovement={state.Mes.SuccessMovementId}, " +
                    $"FailureStatus={state.Mes.FailureStatus}, FailureCode={state.Mes.FailureCode}, " +
                    $"InventorySuccess={state.Inventory.SuccessMovementCount}, " +
                    $"InventoryFailure={state.Inventory.FailureMovementCount}, " +
                    $"PendingStatus={state.Mes.PendingStatus}, " +
                    $"PendingPublished={state.Mes.PendingPublishedMessageCount}, " +
                    $"PendingInventory={state.Inventory.PendingMovementCount}, " +
                    $"MesUnitCost={state.Mes.SuccessUnitCost}, " +
                    $"RequestedUnitCost={state.Inventory.SuccessRequestedUnitCost}, " +
                    $"EffectiveUnitCost={state.Inventory.SuccessUnitCost}, " +
                    $"MovementAmount={state.Inventory.SuccessMovementAmount}, " +
                    $"LedgerMovingAverage={state.Inventory.LedgerMovingAverageUnitCost}, " +
                    $"LedgerValue={state.Inventory.LedgerInventoryValue}",
                options: new EventuallyOptions(
                    Timeout: TimeSpan.FromSeconds(90),
                    PollInterval: TimeSpan.FromMilliseconds(500),
                    SensitiveValues: [mesPostgres, inventoryPostgres, redis]));

            Assert.Equal(source.SuccessLotNo, observed.Inventory.SuccessLotNo);
            Assert.Equal(source.WorkOrderId, observed.Inventory.SuccessSourceDocumentLineId);
            Assert.Equal("business-mes", observed.Inventory.SuccessSourceService);
            Assert.Equal(0, observed.Inventory.SimilarSourceMovementCount);
            Assert.Equal(source.ErpCapitalizedUnitCost, observed.Mes.SuccessUnitCost);
            Assert.Equal(source.ErpCapitalizedUnitCost, observed.Inventory.SuccessRequestedUnitCost);
            Assert.Equal(source.ErpCapitalizedUnitCost, observed.Inventory.SuccessUnitCost);
            Assert.Equal(ReceiptQuantity * source.ErpCapitalizedUnitCost, observed.Inventory.SuccessMovementAmount);
            Assert.Equal(source.ErpCapitalizedUnitCost, observed.Inventory.LedgerMovingAverageUnitCost);
            Assert.Equal(ReceiptQuantity * source.ErpCapitalizedUnitCost, observed.Inventory.LedgerInventoryValue);
            Assert.Equal("Requested", observed.Mes.PendingStatus);
            Assert.Null(observed.Mes.PendingUnitCost);
            Assert.Null(observed.Mes.PendingErpCapitalizedUnitCost);
            Assert.Equal(1L, observed.Mes.PendingPublishedMessageCount);
            Assert.Equal(0, observed.Inventory.PendingMovementCount);

            var transport = await ReadMessagingFactsAsync(
                mesPostgres,
                inventoryPostgres,
                successEvent.EventId,
                successReplayEvent.EventId,
                failureEvent.EventId,
                failureReplayEvent.EventId,
                pendingEvent.EventId);
            Assert.Equal(5L, transport.PublishedEventCount);
            Assert.Equal(5L, transport.InventoryReceivedEventCount);
            Assert.Equal(0L, transport.InventoryDeadLetterCount);
        }
        catch (EventuallyTimeoutException timeout)
        {
            // The messaging tables are only read once, on failure: they are diagnostics for the timeout, not
            // part of the awaited condition.
            var finalMessagingFacts = await ReadMessagingFactsAsync(
                mesPostgres,
                inventoryPostgres,
                successEvent.EventId,
                successReplayEvent.EventId,
                failureEvent.EventId,
                failureReplayEvent.EventId,
                pendingEvent.EventId);
            throw new TimeoutException(
                $"{timeout.Message} " +
                $"PublishedEvents={finalMessagingFacts.PublishedEventCount}, " +
                $"InventoryReceivedEvents={finalMessagingFacts.InventoryReceivedEventCount}, " +
                $"InventoryDeadLetters={finalMessagingFacts.InventoryDeadLetterCount}.",
                timeout);
        }
    }

    private static async Task<ReceiptBoundaryFacts> CreateReceiptRequestsAsync(
        IServiceProvider provider,
        ProbeSource source)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var handler = new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext);
        var requestedAtUtc = DateTimeOffset.UtcNow;

        var successCommand = new CreateFinishedGoodsReceiptRequestCommand(
            source.OrganizationId,
            source.EnvironmentId,
            source.WorkOrderId,
            source.SkuId,
            ReceiptQuantity,
            source.UomCode,
            requestedAtUtc,
            source.ClientSuppliedUnitCost,
            IdempotencyKey: $"man528-client-cost-success-{source.ProbeRunId}",
            ProducedLotNo: source.SuccessLotNo);
        var successResult = await handler.Handle(successCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var failureCommand = successCommand with
        {
            IdempotencyKey = $"man528-client-cost-failure-{source.ProbeRunId}",
            ProducedLotNo = source.FailureLotNo,
        };
        var failureResult = await handler.Handle(failureCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var pendingCommand = successCommand with
        {
            WorkOrderId = source.PendingWorkOrderId,
            IdempotencyKey = $"man528-missing-cost-pending-{source.ProbeRunId}",
            ProducedLotNo = source.PendingLotNo,
        };
        var pendingResult = await handler.Handle(pendingCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var successReceipt = await dbContext.FinishedGoodsReceiptRequests
            .SingleAsync(x => x.RequestNo == successResult.RequestNo);
        var failureReceipt = await dbContext.FinishedGoodsReceiptRequests
            .SingleAsync(x => x.RequestNo == failureResult.RequestNo);
        var pendingReceipt = await dbContext.FinishedGoodsReceiptRequests
            .SingleAsync(x => x.RequestNo == pendingResult.RequestNo);
        var locationResolver = new ConfiguredMesFinishedGoodsReceiptLocationResolver(
            new MesFinishedGoodsReceiptLocationOptions
            {
                SiteCode = "finished-goods",
                LocationCode = "receiving",
            });
        var converter = new FinishedGoodsReceiptRequestedIntegrationEventConverter(locationResolver);
        var successDomainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(
            Assert.Single(successReceipt.GetDomainEvents()));
        var failureDomainEvent = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(
            Assert.Single(failureReceipt.GetDomainEvents()));
        var generatedSuccessEvent = converter.Convert(successDomainEvent);
        var generatedFailureEvent = converter.Convert(failureDomainEvent);
        Assert.Equal(source.ErpCapitalizedUnitCost, generatedSuccessEvent.Payload.UnitCost);
        Assert.Equal(
            InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            generatedSuccessEvent.Payload.UnitCostAuthorityReference);
        var successEvent = generatedSuccessEvent with
        {
            Payload = generatedSuccessEvent.Payload with
            {
                UnitCost = source.ClientSuppliedUnitCost,
                UnitCostAuthorityReference = InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            },
        };
        var successReplayEvent = converter.Convert(successDomainEvent) with
        {
            Payload = generatedSuccessEvent.Payload with
            {
                UnitCost = source.ClientSuppliedUnitCost,
                UnitCostAuthorityReference = InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            },
        };
        var failureEvent = generatedFailureEvent with
        {
            Payload = generatedFailureEvent.Payload with
            {
                UnitCost = source.ClientSuppliedUnitCost,
                InventoryReservationId = "not-a-guid",
                UnitCostAuthorityReference = InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            },
        };
        var failureReplayEvent = converter.Convert(failureDomainEvent) with
        {
            Payload = generatedFailureEvent.Payload with
            {
                UnitCost = source.ClientSuppliedUnitCost,
                InventoryReservationId = "not-a-guid",
                UnitCostAuthorityReference = InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt,
            },
        };
        var pendingEvent = new InventoryMovementRequestedIntegrationEvent(
            $"evt-man528-pending-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            requestedAtUtc,
            InventoryIntegrationEventSources.BusinessMes,
            $"corr-man528-{source.ProbeRunId}",
            pendingResult.RequestNo,
            source.OrganizationId,
            source.EnvironmentId,
            "system:acceptance-probe",
            $"mes:finished-goods-receipt:{source.OrganizationId}:{source.EnvironmentId}:{pendingResult.RequestNo}",
            new InventoryMovementRequestedPayload(
                MovementType: "inbound",
                SourceService: InventoryIntegrationEventSources.BusinessMes,
                SourceDocumentId: pendingResult.RequestNo,
                SourceDocumentLineId: source.PendingWorkOrderId,
                IdempotencyKey: $"mes:finished-goods-receipt:{source.OrganizationId}:{source.EnvironmentId}:{pendingResult.RequestNo}",
                SkuCode: source.SkuId,
                UomCode: source.UomCode,
                SiteCode: "finished-goods",
                LocationCode: "receiving",
                LotNo: source.PendingLotNo,
                SerialNo: null,
                QualityStatus: InventoryQualityStatuses.Unrestricted,
                OwnerType: "production",
                OwnerId: null,
                Quantity: ReceiptQuantity,
                RequestedAtUtc: requestedAtUtc,
                UnitCost: source.ClientSuppliedUnitCost,
                UnitCostAuthorityReference: InventoryMovementUnitCostAuthorityReferences.MesFinishedGoodsReceipt));

        Assert.Equal(source.ErpCapitalizedUnitCost, successReceipt.UnitCost);
        Assert.Equal(source.ErpCapitalizedUnitCost, failureReceipt.UnitCost);
        Assert.Null(pendingReceipt.UnitCost);
        Assert.Empty(pendingReceipt.GetDomainEvents());
        Assert.NotEqual(source.ClientSuppliedUnitCost, successReceipt.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, successReplayEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureEvent.Payload.UnitCost);
        Assert.Equal(source.ClientSuppliedUnitCost, failureReplayEvent.Payload.UnitCost);

        return new ReceiptBoundaryFacts(
            source with
            {
                SuccessRequestNo = successResult.RequestNo,
                FailureRequestNo = failureResult.RequestNo,
                PendingRequestNo = pendingResult.RequestNo,
            },
            successEvent,
            successReplayEvent,
            failureEvent,
            failureReplayEvent,
            pendingEvent,
            successCommand.UnitCost,
            pendingCommand.UnitCost,
            pendingReceipt.UnitCost,
            pendingReceipt.GetDomainEvents().Count);
    }

    private static async Task SeedProducedLotsAsync(IServiceProvider provider, ProbeSource source)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var lots = new[]
        {
            (source.WorkOrderId, source.SuccessLotNo, "OP-MAN528-S", "RPT-MAN528-S"),
            (source.WorkOrderId, source.FailureLotNo, "OP-MAN528-F", "RPT-MAN528-F"),
            (source.PendingWorkOrderId, source.PendingLotNo, "OP-MAN528-P", "RPT-MAN528-P"),
        };
        foreach (var (workOrderId, lotNo, operationTaskId, reportNo) in lots)
        {
            dbContext.OperationTasks.Add(OperationTask.Queue(
                source.OrganizationId,
                source.EnvironmentId,
                workOrderId,
                operationTaskId,
                10,
                "WC-MAN528",
                [],
                now,
                TimeSpan.FromHours(1),
                source.SkuId,
                source.UomCode,
                ReceiptQuantity));
            dbContext.ProductionReports.Add(ProductionReport.Record(
                source.OrganizationId,
                source.EnvironmentId,
                reportNo,
                workOrderId,
                operationTaskId,
                ReceiptQuantity,
                0m,
                completesOperation: true,
                now,
                producedLotNo: lotNo));
            dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
                source.OrganizationId,
                source.EnvironmentId,
                workOrderId,
                operationTaskId,
                reportNo,
                lotNo,
                null,
                ReceiptQuantity,
                now));
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task WaitForConsumerGroupsAsync(string redisConnectionString, string capVersion)
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var database = connection.GetDatabase();
        var required = new[]
        {
            (Stream: nameof(InventoryMovementRequestedIntegrationEvent), Group: $"business-inventory.movement-requested.{capVersion}"),
            (Stream: nameof(StockMovementPostedIntegrationEvent), Group: $"business-mes.stock-movement-posted.{capVersion}"),
            (Stream: nameof(StockMovementPostingFailedIntegrationEvent), Group: $"business-mes.stock-movement-posting-failed.{capVersion}"),
        };
        // Real external processes registering CAP consumer groups on Redis: bounded polling of an observable
        // fact, reporting per-group readiness so a timeout names the group that never appeared.
        // StackExchange.Redis has no CancellationToken overloads, so the window token is genuinely unusable
        // in this observation rather than dropped; Eventually still abandons it when the window closes.
        await Eventually.WaitAsync(
            condition: "the Inventory request and MES posted/failed CAP consumer groups exist on Redis",
            observe: async _ =>
            {
                var missing = new List<string>();
                foreach (var item in required)
                {
                    try
                    {
                        var groups = await database.StreamGroupInfoAsync(item.Stream);
                        if (!groups.Any(group => group.Name == item.Group))
                        {
                            missing.Add(item.Group);
                        }
                    }
                    catch (RedisServerException exception) when (
                        exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
                    {
                        missing.Add($"{item.Group} (stream {item.Stream} not created yet)");
                    }
                }

                return missing;
            },
            isSatisfied: missing => missing.Count == 0,
            describe: missing => missing.Count == 0
                ? "all consumer groups registered"
                : $"missing={string.Join(", ", missing)}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromMinutes(6),
                PollInterval: TimeSpan.FromMilliseconds(500),
                SensitiveValues: [redisConnectionString]));
    }

    private static async Task<ProbeSource> SeedReceiptPairAsync(string connectionString, string probeRunId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var suffix = probeRunId.Replace("-", string.Empty, StringComparison.Ordinal);
        suffix = suffix.Length <= 20 ? suffix : suffix[^20..];
        var source = new ProbeSource(
            $"org-man528-{suffix}",
            $"env-man528-{suffix}",
            $"WO-MAN528-{suffix}",
            $"WO-MAN528-PENDING-{suffix}",
            $"SKU-MAN528-{suffix}",
            "EA",
            $"FGR-MAN528-S-{suffix}",
            $"FGR-MAN528-F-{suffix}",
            $"FGR-MAN528-PENDING-{suffix}",
            $"LOT-MAN528-S-{suffix}",
            $"LOT-MAN528-F-{suffix}",
            $"LOT-MAN528-PENDING-{suffix}",
            ClientSuppliedUnitCost,
            0m,
            probeRunId);

        await using var transaction = await connection.BeginTransactionAsync();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO mes.work_orders
                (id, organization_id, environment_id, work_order_id, sku_id, uom_code, quantity, priority,
                 due_utc, status, created_at_utc, completed_quantity, scrap_quantity, cost_report_count,
                 material_movement_count, over_receipt_tolerance_percent, capitalized_unit_cost)
            VALUES
                (@work_order_row_id, @organization_id, @environment_id, @work_order_id, @sku_id, @uom_code, 10, 10,
                 @due_utc, 'created', @requested_at_utc, 10, 0, 0, 0, 0, @erp_capitalized_unit_cost),
                (@pending_work_order_row_id, @organization_id, @environment_id, @pending_work_order_id, @sku_id, @uom_code, 10, 10,
                 @due_utc, 'created', @requested_at_utc, 10, 0, 0, 0, 0, NULL);
            """;
        insert.Parameters.AddWithValue("work_order_row_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("pending_work_order_row_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("organization_id", source.OrganizationId);
        insert.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        insert.Parameters.AddWithValue("work_order_id", source.WorkOrderId);
        insert.Parameters.AddWithValue("pending_work_order_id", source.PendingWorkOrderId);
        insert.Parameters.AddWithValue("sku_id", source.SkuId);
        insert.Parameters.AddWithValue("uom_code", source.UomCode);
        insert.Parameters.AddWithValue("requested_at_utc", DateTimeOffset.UtcNow);
        insert.Parameters.AddWithValue("due_utc", DateTimeOffset.UtcNow.AddHours(8));
        insert.Parameters.AddWithValue("erp_capitalized_unit_cost", ErpCapitalizedUnitCost);
        await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        await using var authority = connection.CreateCommand();
        authority.CommandText = """
            SELECT capitalized_unit_cost
            FROM mes.work_orders
            WHERE organization_id = @organization_id
              AND environment_id = @environment_id
              AND work_order_id = @work_order_id;
            """;
        authority.Parameters.AddWithValue("organization_id", source.OrganizationId);
        authority.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        authority.Parameters.AddWithValue("work_order_id", source.WorkOrderId);
        var authorityValue = await authority.ExecuteScalarAsync();
        if (authorityValue is not decimal erpCapitalizedUnitCost)
        {
            throw new InvalidOperationException("MAN-528 probe could not read the ERP-authoritative capitalized unit cost projection.");
        }

        return source with { ErpCapitalizedUnitCost = erpCapitalizedUnitCost };
    }

    private static async Task<ReceiptFacts> ReadMesFactsAsync(
        string connectionString,
        ProbeSource source,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT receipt.request_no, receipt.status, receipt.posted_inventory_movement_id,
                   receipt.inventory_posting_failure_code, receipt.unit_cost, work_order.capitalized_unit_cost
            FROM mes.finished_goods_receipt_requests AS receipt
            INNER JOIN mes.work_orders AS work_order
                ON work_order.organization_id = receipt.organization_id
               AND work_order.environment_id = receipt.environment_id
               AND work_order.work_order_id = receipt.work_order_id
            WHERE receipt.organization_id = @organization_id
              AND receipt.environment_id = @environment_id
              AND receipt.request_no IN (@success_request_no, @failure_request_no, @pending_request_no);
            """;
        command.Parameters.AddWithValue("organization_id", source.OrganizationId);
        command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        command.Parameters.AddWithValue("success_request_no", source.SuccessRequestNo);
        command.Parameters.AddWithValue("failure_request_no", source.FailureRequestNo);
        command.Parameters.AddWithValue("pending_request_no", source.PendingRequestNo);
        string? successStatus = null;
        string? successMovementId = null;
        string? failureStatus = null;
        string? failureCode = null;
        decimal? successUnitCost = null;
        decimal? erpCapitalizedUnitCost = null;
        string? pendingStatus = null;
        decimal? pendingUnitCost = null;
        decimal? pendingErpCapitalizedUnitCost = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var requestNo = reader.GetString(0);
                if (requestNo == source.SuccessRequestNo)
                {
                    successStatus = reader.GetString(1);
                    successMovementId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    successUnitCost = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
                    erpCapitalizedUnitCost = reader.IsDBNull(5) ? null : reader.GetDecimal(5);
                }
                else if (requestNo == source.FailureRequestNo)
                {
                    failureStatus = reader.GetString(1);
                    failureCode = reader.IsDBNull(3) ? null : reader.GetString(3);
                }
                else if (requestNo == source.PendingRequestNo)
                {
                    pendingStatus = reader.GetString(1);
                    pendingUnitCost = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
                    pendingErpCapitalizedUnitCost = reader.IsDBNull(5) ? null : reader.GetDecimal(5);
                }
            }
        }

        await using var pendingMessages = connection.CreateCommand();
        pendingMessages.CommandText = """
            SELECT COUNT(*)
            FROM mes.cap_published_messages
            WHERE "Content" LIKE @pending_request_pattern;
            """;
        pendingMessages.Parameters.AddWithValue("pending_request_pattern", $"%{source.PendingRequestNo}%");
        var pendingPublishedMessageCount = (long)(await pendingMessages.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return new ReceiptFacts(
            successStatus,
            successMovementId,
            failureStatus,
            failureCode,
            successUnitCost,
            erpCapitalizedUnitCost,
            pendingStatus,
            pendingUnitCost,
            pendingErpCapitalizedUnitCost,
            pendingPublishedMessageCount);
    }

    private static async Task<InventoryFacts> ReadInventoryFactsAsync(
        string connectionString,
        ProbeSource source,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT source_document_id, source_service, source_document_line_id, lot_no,
                   requested_unit_cost, unit_cost, movement_amount
            FROM inventory.stock_movements
            WHERE organization_id = @organization_id
              AND environment_id = @environment_id
              AND source_service = 'business-mes'
              AND source_document_id IN (@success_request_no, @failure_request_no, @pending_request_no, @similar_request_no);
            """;
        command.Parameters.AddWithValue("organization_id", source.OrganizationId);
        command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        command.Parameters.AddWithValue("success_request_no", source.SuccessRequestNo);
        command.Parameters.AddWithValue("failure_request_no", source.FailureRequestNo);
        command.Parameters.AddWithValue("pending_request_no", source.PendingRequestNo);
        command.Parameters.AddWithValue("similar_request_no", source.SuccessRequestNo + "-SIMILAR");
        var successCount = 0;
        var failureCount = 0;
        var pendingCount = 0;
        var similarCount = 0;
        string? successSourceService = null;
        string? successSourceDocumentLineId = null;
        string? successLotNo = null;
        decimal? successRequestedUnitCost = null;
        decimal? successUnitCost = null;
        decimal? successMovementAmount = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var requestNo = reader.GetString(0);
                if (requestNo == source.SuccessRequestNo)
                {
                    successCount++;
                    successSourceService = reader.GetString(1);
                    successSourceDocumentLineId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    successLotNo = reader.IsDBNull(3) ? null : reader.GetString(3);
                    successRequestedUnitCost = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
                    successUnitCost = reader.IsDBNull(5) ? null : reader.GetDecimal(5);
                    successMovementAmount = reader.IsDBNull(6) ? null : reader.GetDecimal(6);
                }
                else if (requestNo == source.FailureRequestNo)
                {
                    failureCount++;
                }
                else if (requestNo == source.PendingRequestNo)
                {
                    pendingCount++;
                }
                else
                {
                    similarCount++;
                }
            }
        }

        decimal? ledgerMovingAverageUnitCost = null;
        decimal? ledgerInventoryValue = null;
        await using (var ledger = connection.CreateCommand())
        {
            ledger.CommandText = """
                SELECT moving_average_unit_cost, inventory_value
                FROM inventory.stock_ledgers
                WHERE organization_id = @organization_id
                  AND environment_id = @environment_id
                  AND sku_code = @sku_code
                  AND uom_code = @uom_code
                  AND site_code = 'finished-goods'
                  AND location_code = 'receiving'
                  AND lot_no = @lot_no
                  AND serial_no IS NULL
                  AND quality_status = 'unrestricted'
                  AND owner_type = 'production'
                  AND owner_id IS NULL;
                """;
            ledger.Parameters.AddWithValue("organization_id", source.OrganizationId);
            ledger.Parameters.AddWithValue("environment_id", source.EnvironmentId);
            ledger.Parameters.AddWithValue("sku_code", source.SkuId);
            ledger.Parameters.AddWithValue("uom_code", source.UomCode);
            ledger.Parameters.AddWithValue("lot_no", source.SuccessLotNo);
            await using var ledgerReader = await ledger.ExecuteReaderAsync(cancellationToken);
            if (await ledgerReader.ReadAsync(cancellationToken))
            {
                ledgerMovingAverageUnitCost = ledgerReader.GetDecimal(0);
                ledgerInventoryValue = ledgerReader.GetDecimal(1);
            }
        }

        return new InventoryFacts(
            successCount,
            failureCount,
            pendingCount,
            similarCount,
            successSourceService,
            successSourceDocumentLineId,
            successLotNo,
            successRequestedUnitCost,
            successUnitCost,
            successMovementAmount,
            ledgerMovingAverageUnitCost,
            ledgerInventoryValue);
    }

    private static async Task<MessagingFacts> ReadMessagingFactsAsync(
        string mesConnectionString,
        string inventoryConnectionString,
        params string[] eventIds)
    {
        var eventParameters = eventIds.Select((_, index) => $"@event_id_{index}").ToArray();
        var patternParameters = eventIds.Select((_, index) => $"@event_pattern_{index}").ToArray();
        var contentPredicate = string.Join(" OR ", patternParameters.Select(parameter => $"\"Content\" LIKE {parameter}"));
        var eventPredicate = string.Join(", ", eventParameters);
        long publishedEventCount;
        await using (var mesConnection = new NpgsqlConnection(mesConnectionString))
        {
            await mesConnection.OpenAsync();
            await using var published = mesConnection.CreateCommand();
            published.CommandText = $"""
                SELECT COUNT(*)
                FROM mes.cap_published_messages
                WHERE {contentPredicate};
                """;
            for (var index = 0; index < eventIds.Length; index++)
            {
                published.Parameters.AddWithValue(patternParameters[index], $"%{eventIds[index]}%");
            }

            publishedEventCount = (long)(await published.ExecuteScalarAsync() ?? 0L);
        }

        long inventoryReceivedEventCount;
        long inventoryDeadLetterCount;
        await using (var inventoryConnection = new NpgsqlConnection(inventoryConnectionString))
        {
            await inventoryConnection.OpenAsync();
            await using var consumed = inventoryConnection.CreateCommand();
            consumed.CommandText = $"""
                SELECT
                    (SELECT COUNT(*) FROM inventory.cap_received_messages
                     WHERE {contentPredicate}),
                    (SELECT COUNT(*) FROM inventory.integration_event_dead_letters
                     WHERE event_id IN ({eventPredicate}));
                """;
            for (var index = 0; index < eventIds.Length; index++)
            {
                consumed.Parameters.AddWithValue(eventParameters[index], eventIds[index]);
                consumed.Parameters.AddWithValue(patternParameters[index], $"%{eventIds[index]}%");
            }

            await using var reader = await consumed.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                inventoryReceivedEventCount = reader.GetInt64(0);
                inventoryDeadLetterCount = reader.GetInt64(1);
            }
            else
            {
                inventoryReceivedEventCount = 0;
                inventoryDeadLetterCount = 0;
            }
        }

        return new MessagingFacts(publishedEventCount, inventoryReceivedEventCount, inventoryDeadLetterCount);
    }

    private sealed record ProbeSource(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string PendingWorkOrderId,
        string SkuId,
        string UomCode,
        string SuccessRequestNo,
        string FailureRequestNo,
        string PendingRequestNo,
        string SuccessLotNo,
        string FailureLotNo,
        string PendingLotNo,
        decimal ClientSuppliedUnitCost,
        decimal ErpCapitalizedUnitCost,
        string ProbeRunId);

    private sealed record ReceiptBoundaryFacts(
        ProbeSource Source,
        InventoryMovementRequestedIntegrationEvent SuccessEvent,
        InventoryMovementRequestedIntegrationEvent SuccessReplayEvent,
        InventoryMovementRequestedIntegrationEvent FailureEvent,
        InventoryMovementRequestedIntegrationEvent FailureReplayEvent,
        InventoryMovementRequestedIntegrationEvent PendingEvent,
        decimal? SuccessCommandUnitCost,
        decimal? PendingCommandUnitCost,
        decimal? PendingUnitCost,
        int PendingDomainEventCount);

    private sealed record ReceiptFacts(
        string? SuccessStatus,
        string? SuccessMovementId,
        string? FailureStatus,
        string? FailureCode,
        decimal? SuccessUnitCost,
        decimal? ErpCapitalizedUnitCost,
        string? PendingStatus,
        decimal? PendingUnitCost,
        decimal? PendingErpCapitalizedUnitCost,
        long PendingPublishedMessageCount);

    private sealed record InventoryFacts(
        int SuccessMovementCount,
        int FailureMovementCount,
        int PendingMovementCount,
        int SimilarSourceMovementCount,
        string? SuccessSourceService,
        string? SuccessSourceDocumentLineId,
        string? SuccessLotNo,
        decimal? SuccessRequestedUnitCost,
        decimal? SuccessUnitCost,
        decimal? SuccessMovementAmount,
        decimal? LedgerMovingAverageUnitCost,
        decimal? LedgerInventoryValue);

    private sealed record MessagingFacts(
        long PublishedEventCount,
        long InventoryReceivedEventCount,
        long InventoryDeadLetterCount);
}

internal sealed class RealPostgresRedisMesInventoryFactAttribute : FactAttribute
{
    public RealPostgresRedisMesInventoryFactAttribute()
    {
        var required = new[]
        {
            "NERV_IIP_TEST_MES_POSTGRES",
            "NERV_IIP_TEST_INVENTORY_POSTGRES",
            "NERV_IIP_TEST_REDIS",
            "NERV_IIP_TEST_CAP_VERSION",
            "NERV_IIP_TEST_PROBE_RUN_ID",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-528 MES/Inventory PostgreSQL, Redis, CAP version, and probe-run variables to run the external-process success/failure probe.";
        }
    }
}
