using System.Data;
using System.Text;
using System.Text.Json;
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
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Npgsql;
using StackExchange.Redis;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;


public sealed partial class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
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
        Assert.Equal(ErpCapitalizedUnitCost, generatedSuccessEvent.Payload.UnitCost);
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

        Assert.Equal(ErpCapitalizedUnitCost, successReceipt.UnitCost);
        Assert.Equal(ErpCapitalizedUnitCost, failureReceipt.UnitCost);
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

        // External ERP authority projection prerequisite: the successful work order's cost has already
        // been consumed by the real MES ERP handler, while the pending work order deliberately has no row.
        await using var provenance = connection.CreateCommand();
        provenance.Transaction = transaction;
        provenance.CommandText = """
            INSERT INTO mes.processed_integration_events
                ("Id", "ConsumerName", "EventId", "EventType", "EventVersion", "SourceService", "IdempotencyKey", "ProcessedAtUtc")
            VALUES
                (@id, @consumer_name, @event_id, @event_type, @event_version, @source_service, @idempotency_key, @processed_at_utc);
            """;
        provenance.Parameters.AddWithValue("id", Guid.CreateVersion7());
        provenance.Parameters.AddWithValue("consumer_name", WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName);
        provenance.Parameters.AddWithValue("event_id", $"evt-man528-cost-capitalized-{source.ProbeRunId}");
        provenance.Parameters.AddWithValue("event_type", ErpIntegrationEventTypes.WorkOrderCostCapitalized);
        provenance.Parameters.AddWithValue("event_version", ErpIntegrationEventVersions.V1);
        provenance.Parameters.AddWithValue("source_service", ErpIntegrationEventSources.BusinessErp);
        provenance.Parameters.AddWithValue(
            "idempotency_key",
            $"work-order-cost-capitalized:{source.OrganizationId}:{source.EnvironmentId}:{source.WorkOrderId}");
        provenance.Parameters.AddWithValue("processed_at_utc", DateTimeOffset.UtcNow);
        await provenance.ExecuteNonQueryAsync();
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
        if (authorityValue is not decimal authorityReadbackCost)
        {
            throw new InvalidOperationException("MAN-528 probe could not read the ERP-authoritative capitalized unit cost projection.");
        }
        // The fixed 12.34m value is the independent contract; PostgreSQL readback is only the observed authority fact.
        Assert.Equal(ErpCapitalizedUnitCost, authorityReadbackCost);

        await using var provenanceReadback = connection.CreateCommand();
        provenanceReadback.CommandText = """
            SELECT COUNT(*) FROM mes.processed_integration_events
            WHERE "ConsumerName" = @consumer_name
              AND "EventType" = @event_type
              AND "EventVersion" = @event_version
              AND "SourceService" = @source_service
              AND "IdempotencyKey" = @success_idempotency_key;
            """;
        provenanceReadback.Parameters.AddWithValue("consumer_name", WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName);
        provenanceReadback.Parameters.AddWithValue("event_type", ErpIntegrationEventTypes.WorkOrderCostCapitalized);
        provenanceReadback.Parameters.AddWithValue("event_version", ErpIntegrationEventVersions.V1);
        provenanceReadback.Parameters.AddWithValue("source_service", ErpIntegrationEventSources.BusinessErp);
        provenanceReadback.Parameters.AddWithValue(
            "success_idempotency_key",
            $"work-order-cost-capitalized:{source.OrganizationId}:{source.EnvironmentId}:{source.WorkOrderId}");
        if ((long)(await provenanceReadback.ExecuteScalarAsync() ?? 0L) != 1L)
        {
            throw new InvalidOperationException("MAN-528 probe could not read back the successful ERP authority provenance projection.");
        }

        return source;
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

        await using var pendingAuthority = connection.CreateCommand();
        pendingAuthority.CommandText = """
            SELECT COUNT(*)
            FROM mes.processed_integration_events
            WHERE "ConsumerName" = 'business-mes.work-order-cost-capitalized'
              AND "IdempotencyKey" = @pending_authority_idempotency_key;
            """;
        pendingAuthority.Parameters.AddWithValue(
            "pending_authority_idempotency_key",
            $"work-order-cost-capitalized:{source.OrganizationId}:{source.EnvironmentId}:{source.PendingWorkOrderId}");
        var pendingAuthorityProvenanceCount = (long)(await pendingAuthority.ExecuteScalarAsync(cancellationToken) ?? 0L);

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
            pendingAuthorityProvenanceCount);
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
                   requested_unit_cost, unit_cost, quantity, movement_amount
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
        decimal? successQuantity = null;
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
                    successQuantity = reader.IsDBNull(6) ? null : reader.GetDecimal(6);
                    successMovementAmount = reader.IsDBNull(7) ? null : reader.GetDecimal(7);
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
            successQuantity,
            successMovementAmount,
            ledgerMovingAverageUnitCost,
            ledgerInventoryValue);
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
        long PendingAuthorityProvenanceCount);

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
        decimal? SuccessQuantity,
        decimal? SuccessMovementAmount,
        decimal? LedgerMovingAverageUnitCost,
        decimal? LedgerInventoryValue);

    private sealed record MessagingFacts(
        IReadOnlyDictionary<string, EventMessageFact> EventFacts,
        PendingRedisFact PendingEventRedisFact,
        CapReceivedEventFact? PendingCapReceivedFact,
        string? PendingAuthorityStatus,
        string? PendingAuthorityReason,
        long InventoryDeadLetterCount);

    internal sealed record EventMessageFact(long PublishedCount, long ReceivedCount);

    internal sealed record CapReceivedEventFact(
        string EventId,
        long RowId,
        string? Name,
        string? Group,
        string StatusName,
        int RetryCount);

    private sealed record PendingRedisFact(
        string StreamName,
        string ConsumerGroup,
        bool IsPresent,
        string? EventId,
        string? IdempotencyKey,
        string? StreamEntryId,
        string? ConsumerName,
        int DeliveryCount);

}
