using System.Data;
using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using Npgsql;
using StackExchange.Redis;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class MesInventoryProducedLotPostgresRedisAcceptanceTests
{
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
        var publisher = provider.GetRequiredService<ICapPublisher>();
        var successEvent = MovementRequested(
            source,
            source.SuccessRequestNo,
            source.SuccessLotNo,
            $"mes:finished-goods-receipt:{source.OrganizationId}:{source.EnvironmentId}:{source.SuccessRequestNo}",
            inventoryReservationId: null);
        var failureEvent = MovementRequested(
            source,
            source.FailureRequestNo,
            source.FailureLotNo,
            $"mes:finished-goods-receipt:{source.OrganizationId}:{source.EnvironmentId}:{source.FailureRequestNo}",
            inventoryReservationId: "not-a-guid");
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), successEvent);
        await publisher.PublishAsync(nameof(InventoryMovementRequestedIntegrationEvent), failureEvent);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var mesFacts = await ReadMesFactsAsync(mesPostgres, source);
            var inventoryFacts = await ReadInventoryFactsAsync(inventoryPostgres, source);
            if (mesFacts.SuccessStatus == "Posted"
                && mesFacts.SuccessMovementId is not null
                && mesFacts.FailureStatus == "InventoryPostingFailed"
                && !string.IsNullOrWhiteSpace(mesFacts.FailureCode)
                && inventoryFacts.SuccessMovementCount == 1
                && inventoryFacts.FailureMovementCount == 0)
            {
                Assert.Equal(source.SuccessLotNo, inventoryFacts.SuccessLotNo);
                Assert.Equal(source.WorkOrderId, inventoryFacts.SuccessSourceDocumentLineId);
                Assert.Equal("business-mes", inventoryFacts.SuccessSourceService);
                Assert.Equal(0, inventoryFacts.SimilarSourceMovementCount);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        var finalMesFacts = await ReadMesFactsAsync(mesPostgres, source);
        var finalInventoryFacts = await ReadInventoryFactsAsync(inventoryPostgres, source);
        var finalMessagingFacts = await ReadMessagingFactsAsync(
            mesPostgres,
            inventoryPostgres,
            successEvent.EventId,
            failureEvent.EventId);
        throw new TimeoutException(
            "MES/Inventory did not close both Redis CAP paths within 90 seconds. " +
            $"SuccessStatus={finalMesFacts.SuccessStatus}, SuccessMovement={finalMesFacts.SuccessMovementId}, " +
            $"FailureStatus={finalMesFacts.FailureStatus}, FailureCode={finalMesFacts.FailureCode}, " +
            $"InventorySuccess={finalInventoryFacts.SuccessMovementCount}, InventoryFailure={finalInventoryFacts.FailureMovementCount}, " +
            $"PublisherSuccess={finalMessagingFacts.SuccessPublishStatus}, PublisherFailure={finalMessagingFacts.FailurePublishStatus}, " +
            $"InventoryRetainedReceipts={finalMessagingFacts.InventoryRetainedReceiptCount}, " +
            $"InventoryDeadLetters={finalMessagingFacts.InventoryDeadLetterCount}.");
    }

    private static InventoryMovementRequestedIntegrationEvent MovementRequested(
        ProbeSource source,
        string requestNo,
        string lotNo,
        string idempotencyKey,
        string? inventoryReservationId)
    {
        var now = DateTimeOffset.UtcNow;
        return new InventoryMovementRequestedIntegrationEvent(
            $"evt-man528-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            now,
            InventoryIntegrationEventSources.BusinessMes,
            $"corr-man528-{source.ProbeRunId}",
            requestNo,
            source.OrganizationId,
            source.EnvironmentId,
            "system:acceptance-probe",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessMes,
                requestNo,
                source.WorkOrderId,
                idempotencyKey,
                source.SkuId,
                source.UomCode,
                "finished-goods",
                "receiving",
                lotNo,
                null,
                InventoryQualityStatuses.Unrestricted,
                "production",
                null,
                5m,
                now,
                inventoryReservationId));
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
        var deadline = DateTimeOffset.UtcNow.AddMinutes(6);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ready = true;
            foreach (var item in required)
            {
                try
                {
                    var groups = await database.StreamGroupInfoAsync(item.Stream);
                    ready &= groups.Any(group => group.Name == item.Group);
                }
                catch (RedisServerException exception) when (
                    exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
                {
                    ready = false;
                }
            }

            if (ready)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            "MAN-528 Redis acceptance timed out waiting for the Inventory request and MES posted/failed consumer groups.");
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
            $"SKU-MAN528-{suffix}",
            "EA",
            $"FGR-MAN528-S-{suffix}",
            $"FGR-MAN528-F-{suffix}",
            $"LOT-MAN528-S-{suffix}",
            $"LOT-MAN528-F-{suffix}",
            probeRunId);

        await using var transaction = await connection.BeginTransactionAsync();
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO mes.work_orders
                (id, organization_id, environment_id, work_order_id, sku_id, uom_code, quantity, priority,
                 due_utc, status, created_at_utc, completed_quantity, scrap_quantity, cost_report_count,
                 material_movement_count, over_receipt_tolerance_percent)
            VALUES
                (@work_order_row_id, @organization_id, @environment_id, @work_order_id, @sku_id, @uom_code, 10, 10,
                 @due_utc, 'created', @requested_at_utc, 0, 0, 0, 0, 0);

            INSERT INTO mes.finished_goods_receipt_requests
                (id, organization_id, environment_id, request_no, work_order_id, sku_id, quantity, uom_code,
                 requested_at_utc, produced_lot_no, status, posted_quantity)
            VALUES
                (@success_id, @organization_id, @environment_id, @success_request_no, @work_order_id, @sku_id, 5, @uom_code,
                 @requested_at_utc, @success_lot_no, 'Requested', 0),
                (@failure_id, @organization_id, @environment_id, @failure_request_no, @work_order_id, @sku_id, 5, @uom_code,
                 @requested_at_utc, @failure_lot_no, 'Requested', 0);
            """;
        insert.Parameters.AddWithValue("work_order_row_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("success_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("failure_id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("organization_id", source.OrganizationId);
        insert.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        insert.Parameters.AddWithValue("success_request_no", source.SuccessRequestNo);
        insert.Parameters.AddWithValue("failure_request_no", source.FailureRequestNo);
        insert.Parameters.AddWithValue("work_order_id", source.WorkOrderId);
        insert.Parameters.AddWithValue("sku_id", source.SkuId);
        insert.Parameters.AddWithValue("uom_code", source.UomCode);
        insert.Parameters.AddWithValue("requested_at_utc", DateTimeOffset.UtcNow);
        insert.Parameters.AddWithValue("due_utc", DateTimeOffset.UtcNow.AddHours(8));
        insert.Parameters.AddWithValue("success_lot_no", source.SuccessLotNo);
        insert.Parameters.AddWithValue("failure_lot_no", source.FailureLotNo);
        await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return source;
    }

    private static async Task<ReceiptFacts> ReadMesFactsAsync(string connectionString, ProbeSource source)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT request_no, status, posted_inventory_movement_id, inventory_posting_failure_code
            FROM mes.finished_goods_receipt_requests
            WHERE organization_id = @organization_id
              AND environment_id = @environment_id
              AND request_no IN (@success_request_no, @failure_request_no);
            """;
        command.Parameters.AddWithValue("organization_id", source.OrganizationId);
        command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        command.Parameters.AddWithValue("success_request_no", source.SuccessRequestNo);
        command.Parameters.AddWithValue("failure_request_no", source.FailureRequestNo);
        string? successStatus = null;
        string? successMovementId = null;
        string? failureStatus = null;
        string? failureCode = null;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(0) == source.SuccessRequestNo)
            {
                successStatus = reader.GetString(1);
                successMovementId = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
            else
            {
                failureStatus = reader.GetString(1);
                failureCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            }
        }

        return new ReceiptFacts(successStatus, successMovementId, failureStatus, failureCode);
    }

    private static async Task<InventoryFacts> ReadInventoryFactsAsync(string connectionString, ProbeSource source)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT source_document_id, source_service, source_document_line_id, lot_no
            FROM inventory.stock_movements
            WHERE organization_id = @organization_id
              AND environment_id = @environment_id
              AND source_service = 'business-mes'
              AND source_document_id IN (@success_request_no, @failure_request_no, @similar_request_no);
            """;
        command.Parameters.AddWithValue("organization_id", source.OrganizationId);
        command.Parameters.AddWithValue("environment_id", source.EnvironmentId);
        command.Parameters.AddWithValue("success_request_no", source.SuccessRequestNo);
        command.Parameters.AddWithValue("failure_request_no", source.FailureRequestNo);
        command.Parameters.AddWithValue("similar_request_no", source.SuccessRequestNo + "-SIMILAR");
        var successCount = 0;
        var failureCount = 0;
        var similarCount = 0;
        string? successSourceService = null;
        string? successSourceDocumentLineId = null;
        string? successLotNo = null;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestNo = reader.GetString(0);
            if (requestNo == source.SuccessRequestNo)
            {
                successCount++;
                successSourceService = reader.GetString(1);
                successSourceDocumentLineId = reader.IsDBNull(2) ? null : reader.GetString(2);
                successLotNo = reader.IsDBNull(3) ? null : reader.GetString(3);
            }
            else if (requestNo == source.FailureRequestNo)
            {
                failureCount++;
            }
            else
            {
                similarCount++;
            }
        }

        return new InventoryFacts(
            successCount,
            failureCount,
            similarCount,
            successSourceService,
            successSourceDocumentLineId,
            successLotNo);
    }

    private static async Task<MessagingFacts> ReadMessagingFactsAsync(
        string mesConnectionString,
        string inventoryConnectionString,
        string successEventId,
        string failureEventId)
    {
        string? successPublishStatus = null;
        string? failurePublishStatus = null;
        await using (var mesConnection = new NpgsqlConnection(mesConnectionString))
        {
            await mesConnection.OpenAsync();
            await using var published = mesConnection.CreateCommand();
            published.CommandText = """
                SELECT "Content", "StatusName"
                FROM mes.cap_published_messages
                WHERE "Content" LIKE @success_event_id OR "Content" LIKE @failure_event_id;
                """;
            published.Parameters.AddWithValue("success_event_id", $"%{successEventId}%");
            published.Parameters.AddWithValue("failure_event_id", $"%{failureEventId}%");
            await using var reader = await published.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var content = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (content.Contains(successEventId, StringComparison.Ordinal))
                {
                    successPublishStatus = reader.GetString(1);
                }
                else if (content.Contains(failureEventId, StringComparison.Ordinal))
                {
                    failurePublishStatus = reader.GetString(1);
                }
            }
        }

        var inventoryRetainedReceiptCount = 0;
        var inventoryDeadLetterCount = 0;
        await using (var inventoryConnection = new NpgsqlConnection(inventoryConnectionString))
        {
            await inventoryConnection.OpenAsync();
            await using var consumed = inventoryConnection.CreateCommand();
            consumed.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM inventory.cap_received_messages
                     WHERE "Content" LIKE @success_event_pattern OR "Content" LIKE @failure_event_pattern),
                    (SELECT COUNT(*) FROM inventory.integration_event_dead_letters
                     WHERE event_id IN (@success_event_id, @failure_event_id));
                """;
            consumed.Parameters.AddWithValue("success_event_id", successEventId);
            consumed.Parameters.AddWithValue("failure_event_id", failureEventId);
            consumed.Parameters.AddWithValue("success_event_pattern", $"%{successEventId}%");
            consumed.Parameters.AddWithValue("failure_event_pattern", $"%{failureEventId}%");
            await using var reader = await consumed.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                inventoryRetainedReceiptCount = reader.GetInt32(0);
                inventoryDeadLetterCount = reader.GetInt32(1);
            }
        }

        return new MessagingFacts(
            successPublishStatus,
            failurePublishStatus,
            inventoryRetainedReceiptCount,
            inventoryDeadLetterCount);
    }

    private sealed record ProbeSource(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string SkuId,
        string UomCode,
        string SuccessRequestNo,
        string FailureRequestNo,
        string SuccessLotNo,
        string FailureLotNo,
        string ProbeRunId);

    private sealed record ReceiptFacts(
        string? SuccessStatus,
        string? SuccessMovementId,
        string? FailureStatus,
        string? FailureCode);

    private sealed record InventoryFacts(
        int SuccessMovementCount,
        int FailureMovementCount,
        int SimilarSourceMovementCount,
        string? SuccessSourceService,
        string? SuccessSourceDocumentLineId,
        string? SuccessLotNo);

    private sealed record MessagingFacts(
        string? SuccessPublishStatus,
        string? FailurePublishStatus,
        int InventoryRetainedReceiptCount,
        int InventoryDeadLetterCount);
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
