using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetCore.CAP;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsPurchaseOrderReleasePostgresTests
{
    private const string Consumer = "business-wms.erp-purchase-order-released";
    private const string CancellationConsumer = "business-wms.erp-purchase-order-cancelled";
    private const string Route = "PurchaseOrderReceiving:Routes:0:";

    [WmsDeadLetterReplayPostgresFact]
    public async Task Migration_separates_unbound_candidate_requests_from_confirmed_history()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var release = Released();
        await DeliverAsync(fixture, release);
        await DeliverAsync(fixture, release with
        {
            Payload = release.Payload with { PurchaseOrderNo = "PO-CANCELLED" }
        });
        await DeliverAsync(fixture, release with
        {
            Payload = release.Payload with { PurchaseOrderNo = "PO-LEGACY" }
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batchId = Guid.NewGuid();
        await db.Database.MigrateAsync("20261127175533_BindInboundErpPurchaseReceipt");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE wms.inbound_orders
            SET receipt_confirmation_key = 'candidate', receipt_confirmation_fingerprint = 'fingerprint',
                receipt_batch_id = {batchId}, receipt_exchange_rate = 1.25
            WHERE inbound_order_no = 'PO-123';

            UPDATE wms.inbound_orders
            SET receipt_confirmation_key = 'cancelled', receipt_confirmation_fingerprint = 'fingerprint',
                receipt_batch_id = {Guid.NewGuid()}, receipt_exchange_rate = 1.25
            WHERE inbound_order_no = 'PO-CANCELLED';

            UPDATE wms.inbound_orders SET receipt_confirmation_key = 'legacy'
            WHERE inbound_order_no = 'PO-LEGACY';

            UPDATE wms.inbound_order_lines SET received_quantity = 12.5;
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO wms.purchase_order_cancellations
                (organization_id, environment_id, purchase_order_no, purchase_order_id, site_code,
                    purchase_order_version, cancelled_at_utc, reason)
            VALUES ('org-replay', 'env-replay', 'PO-CANCELLED', {release.Payload.PurchaseOrderId},
                'SITE', 4, {Cancelled().OccurredAtUtc.UtcDateTime}, 'supplier cancellation')
            """);

        await db.Database.MigrateAsync();
        var orders = await db.InboundOrders.Include(x => x.Lines).ToDictionaryAsync(x => x.InboundOrderNo);
        var staged = orders["PO-123"];
        Assert.Equal(batchId, staged.ReceiptBatchId);
        Assert.Equal(1.25m, staged.ReceiptExchangeRate);
        Assert.Equal("candidate", staged.ReceiptRequestKey);
        Assert.Null(staged.ReceiptConfirmationKey);
        var line = Assert.Single(staged.Lines);
        Assert.Equal(12.5m, line.RequestedReceiptQuantity);
        Assert.Equal(0m, line.ReceivedQuantity);
        Assert.Equal(InboundOrderStatus.Cancelled, orders["PO-CANCELLED"].Status);
        Assert.Equal(0m, Assert.Single(orders["PO-CANCELLED"].Lines).ReceivedQuantity);
        Assert.Equal("legacy", orders["PO-LEGACY"].ReceiptConfirmationKey);
        Assert.Equal(12.5m, Assert.Single(orders["PO-LEGACY"].Lines).ReceivedQuantity);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Staged_receipt_keeps_actual_zero_and_cancellation_closes_the_expectation()
    {
        var erp = new ReceiptAcceptanceStub((_, _) =>
            throw new WmsLifecycleConflictException("confirm-inbound-receipt", "erp-acceptance-conflict"));
        await using var fixture = await ReplayFixture.CreateAsync(
            services => services.AddSingleton<IWmsPurchaseReceiptAcceptanceClient>(erp), Settings());
        await DeliverAsync(fixture, Released());
        var receipt = await AssignForConfirmationAsync(fixture, "PO-123");

        var staged = await SendAsync(fixture, receipt);
        Assert.Equal(receipt.ExpectedVersion + 1, staged.Version);
        var bind = new BindInboundErpReceiptCommand(receipt, await BatchIdAsync(fixture));
        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => SendAsync(fixture, bind));
        Assert.Equal(0, await SendAsync(fixture,
            new CancelInboundOrdersForSourceCommand("org-replay", "env-replay", WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "legacy call")));

        using (var read = fixture.Factory.Services.CreateScope())
        {
            var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.InboundOrders.Include(x => x.Lines).SingleAsync();
            Assert.Equal(bind.BatchId, order.ReceiptBatchId);
            Assert.Equal(0m, Assert.Single(order.Lines).ReceivedQuantity);
            Assert.Null(order.ReceiptConfirmationKey);

            var handler = new ListInboundOrdersQueryHandler(db);
            var query = new ListInboundOrdersQuery("org-replay", "env-replay",
                InboundOrderId: order.Id, SiteCodes: [order.SiteCode], SiteWideScope: true);
            var listed = Assert.Single((await handler.Handle(query, default)).Items);
            Assert.Equal("erp-pending", listed.PurchaseReceipt?.Status);
            Assert.Equal(12.5m, Assert.Single(listed.PurchaseReceipt!.Lines).ExpectedQuantity);
            Assert.Empty((await handler.Handle(query with { SiteCodes = ["OTHER-SITE"] }, default)).Items);
            Assert.Empty((await handler.Handle(query with { EnvironmentId = "other-env" }, default)).Items);
        }

        await DeliverAsync(fixture, Cancelled());
        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => SendAsync(fixture, bind));
        await Assert.ThrowsAsync<WmsLifecycleConflictException>(() => SendAsync(fixture, receipt));
        Assert.Single(erp.Requests);

        using var closedScope = fixture.Factory.Services.CreateScope();
        var closedDb = closedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var closed = await closedDb.InboundOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(InboundOrderStatus.Cancelled, closed.Status);
        Assert.Equal(0m, Assert.Single(closed.Lines).ReceivedQuantity);
        Assert.Empty(await closedDb.InventoryMovementRequests.ToListAsync());
        Assert.Empty(await closedDb.WarehouseTasks.ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Ambiguous_ack_replays_the_frozen_batch_and_committed_actual_survives_cancellation()
    {
        var ackReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var erp = new ReceiptAcceptanceStub(async (call, token) =>
        {
            if (call == 1)
            {
                throw new KnownException("ERP accepted but the response was lost");
            }

            ackReached.SetResult();
            await releaseAck.Task.WaitAsync(token);
            return "RCV-123";
        });
        await using var fixture = await ReplayFixture.CreateAsync(
            services => services.AddSingleton<IWmsPurchaseReceiptAcceptanceClient>(erp), Settings());
        await DeliverAsync(fixture, Released());
        var receipt = await AssignForConfirmationAsync(fixture, "PO-123");

        var staged = await SendAsync(fixture, receipt);
        Assert.Equal(receipt.ExpectedVersion + 1, staged.Version);
        var bind = new BindInboundErpReceiptCommand(receipt, await BatchIdAsync(fixture));
        await Assert.ThrowsAsync<KnownException>(() => SendAsync(fixture, bind));
        await Assert.ThrowsAsync<WmsIdempotencyConflictException>(
            () => SendAsync(fixture, receipt with { ExchangeRate = 2m }));
        Assert.Equal(staged, await SendAsync(fixture, receipt));
        var binding = SendAsync(fixture, bind);
        try
        {
            await ackReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            releaseAck.TrySetResult();
            if (binding.IsFaulted) await binding;
            throw;
        }

        var cancellation = DeliverAsync(fixture, Cancelled());
        try
        {
            using var observe = fixture.Factory.Services.CreateScope();
            var observer = observe.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await Eventually.AssertAsync(
                condition: "The cancellation waits for receipt binding on the purchase-order transaction lock",
                assertion: async token =>
                {
                    var waiters = await observer.Database.SqlQueryRaw<long>("""
                        SELECT count(*) AS "Value" FROM pg_locks
                        WHERE database = (SELECT oid FROM pg_database WHERE datname = current_database())
                            AND locktype = 'advisory' AND NOT granted AND objsubid = 1
                        """).SingleAsync(token);
                    Assert.Equal(1, waiters);
                },
                options: new EventuallyOptions(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), []));
        }
        finally
        {
            releaseAck.SetResult();
            await Task.WhenAll(binding, cancellation);
        }

        var accepted = await binding;
        Assert.Equal(receipt.ExpectedVersion + 2, accepted.Version);
        Assert.Equal(accepted, await SendAsync(fixture, receipt));
        await Assert.ThrowsAsync<WmsUnprocessableException>(() => SendAsync(fixture,
            new CompleteInboundOrderCommand(receipt.InboundOrderId, "quality-complete",
                OrganizationId: receipt.OrganizationId, EnvironmentId: receipt.EnvironmentId, ActorPrincipalId: receipt.ActorPrincipalId,
                AuthorizedSiteCodes: receipt.AuthorizedSiteCodes, ScopeKind: receipt.ScopeKind, ScopeId: receipt.ScopeId,
                ExpectedVersion: accepted.Version)));

        Assert.Equal(2, erp.Requests.Count);
        Assert.All(erp.Requests, request => Assert.Equal((bind.BatchId, "PO-123", 1.25m, 12.5m, 0m), request));
        Assert.Equal(0, await SendAsync(fixture,
            new CancelInboundOrdersForSourceCommand("org-replay", "env-replay", WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "legacy call")));
        using var read = fixture.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.InboundOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(InboundOrderStatus.Open, order.Status);
        Assert.Equal("RCV-123", order.ErpPurchaseReceiptNo);
        Assert.Equal(WmsText.IdempotencyKey("receipt"), order.ReceiptConfirmationKey);
        Assert.Equal(12.5m, Assert.Single(order.Lines).ReceivedQuantity);
        Assert.Empty(await db.InventoryMovementRequests.ToListAsync());
        Assert.Empty(await db.WarehouseTasks.ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Bound_unrestricted_receipt_posts_frozen_cost_and_retries_the_same_movement()
    {
        var erp = new ReceiptAcceptanceTransport();
        var settings = Settings();
        settings[Route + "QualityStatus"] = "unrestricted";
        settings["Messaging:Provider"] = "InMemory";
        settings["Wms:WorkPoolSeed:Enabled"] = "false";
        await using var fixture = await ReplayFixture.CreateAsync(
            services => services.AddHttpClient<IWmsPurchaseReceiptAcceptanceClient, HttpWmsPurchaseReceiptAcceptanceClient>()
                .ConfigurePrimaryHttpMessageHandler(() => erp), settings, "Development");
        await CapTestHost.WaitForCapBootstrapAsync(fixture.Factory.Services);
        var release = Released();
        await DeliverAsync(fixture, release with
        {
            Payload = release.Payload with
            {
                Lines = [.. release.Payload.Lines, new("010", "SKU-B", "pcs", 2m, new DateOnly(2026, 10, 3))],
            },
        });
        var receipt = (await AssignForConfirmationAsync(fixture, "PO-123")) with
        {
            Lines = [new("020", 12.5m), new("010", 2m)],
        };
        var completion = new CompleteInboundOrderCommand(receipt.InboundOrderId, "complete",
            OrganizationId: "org-replay", EnvironmentId: "env-replay", ActorPrincipalId: "operator",
            AuthorizedSiteCodes: ["SITE"], ScopeKind: "site", ScopeId: "SITE", ExpectedVersion: receipt.ExpectedVersion);

        await Assert.ThrowsAsync<WmsUnprocessableException>(() => SendAsync(fixture, completion));
        await SendAsync(fixture, receipt);
        var batchId = await BatchIdAsync(fixture);
        var accepted = await SendAsync(fixture, new BindInboundErpReceiptCommand(receipt, batchId));
        completion = completion with { ExpectedVersion = accepted.Version };
        erp.ReceiptNo = "WRONG";
        await Assert.ThrowsAsync<WmsUnprocessableException>(() => SendAsync(fixture, completion));
        erp.ReceiptNo = "RCV-123";
        using var response = await fixture.Client.PostAsJsonAsync(
            $"/api/business/v1/wms/inbound-orders/{receipt.InboundOrderId}/complete", new
            {
                inboundOrderId = receipt.InboundOrderId.ToString(),
                completion.IdempotencyKey,
                completion.OrganizationId,
                completion.EnvironmentId,
                completion.ActorPrincipalId,
                completion.AuthorizedSiteCodes,
                completion.ScopeKind,
                completion.ScopeId,
                completion.ExpectedVersion,
            });
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, responseBody);
        using var responseJson = JsonDocument.Parse(responseBody);
        Assert.True(responseJson.RootElement.GetProperty("success").GetBoolean(), responseBody);
        var posted = await SendAsync(fixture, completion);
        Assert.Equal(posted, await SendAsync(fixture, completion with { IdempotencyKey = "changed-client-key" }));
        Assert.NotNull(posted.RequestId);
        await AssertPostingAsync("InventoryPostingPending", "pending");

        string key;
        using (var read = fixture.Factory.Services.CreateScope())
        {
            var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = await db.InventoryMovementRequests.SingleAsync(x => x.SourceDocumentLineId == "020");
            Assert.Equal(6.25m, (await db.InventoryMovementRequests.SingleAsync(x => x.SourceDocumentLineId == "010")).UnitCost);
            Assert.Equal(15.625m, request.UnitCost);
            Assert.Equal("020", request.SourceDocumentLineId);
            Assert.Contains(batchId.ToString("N"), request.IdempotencyKey);
            key = request.IdempotencyKey;
            var time = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
            var failed = new StockMovementPostingFailedIntegrationEvent(
                "failure", InventoryIntegrationEventTypes.StockMovementPostingFailed, 1, time,
                InventoryIntegrationEventSources.BusinessInventory, "corr", "cause", "org-replay", "env-replay",
                "system:inventory", "failure", new("inbound", "wms", "PO-123", "020", key, "SKU-A", "kg",
                    "SITE", "STAGE-A", null, null, "unrestricted", "company", null, 12.5m,
                    "posting-rejected", "temporary failure", time));
            var failureLetter = await fixture.AddLetterAsync(failed, StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed.ConsumerName);
            await fixture.ReplayAsync(failureLetter, "replayed");
        }

        await AssertPostingAsync("InventoryPostingFailed", "failed");
        erp.ReceiptNo = "LOOKUP-MUST-NOT-RUN-ON-RETRY";
        var retry = new RetryInboundInventoryPostingCommand(receipt.InboundOrderId, "another-key",
            new("org-replay", "env-replay", "operator", ["SITE"], "site", "SITE", "org-replay",
                "env-replay", "SITE", null, null));
        Assert.Equal(posted, await SendAsync(fixture, retry));
        Assert.Equal(posted, await SendAsync(fixture, retry));
        await AssertPostingAsync("InventoryPostingPending", "pending");
        using var verification = fixture.Factory.Services.CreateScope();
        var verifiedDb = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var retried = await verifiedDb.InventoryMovementRequests.SingleAsync(x => x.SourceDocumentLineId == "020");
        Assert.Equal(key, retried.IdempotencyKey);
        Assert.Equal(15.625m, retried.UnitCost);
        Assert.Equal("Pending", retried.Status.ToString());
        Assert.Equal(2, await verifiedDb.InventoryMovementRequests.CountAsync());
        var requests = await ReadOutboxAsync(verifiedDb, nameof(InventoryMovementRequestedIntegrationEvent));
        var originalAndRetry = requests.Select(message => message.GetProperty("Payload"))
            .Where(payload => payload.GetProperty("SourceDocumentLineId").GetString() == "020").ToArray();
        Assert.Equal(2, originalAndRetry.Length);
        Assert.All(originalAndRetry, payload =>
        {
            Assert.Equal(key, payload.GetProperty("IdempotencyKey").GetString());
            Assert.Equal(15.625m, payload.GetProperty("UnitCost").GetDecimal());
        });
        Assert.Single(requests, message => message.GetProperty("Payload").GetProperty("SourceDocumentLineId").GetString() == "010");
        Assert.Empty(await ReadOutboxAsync(verifiedDb, nameof(WmsIntegrationEvent)));

        async Task DeliverPostedAsync(InventoryMovementRequest movement)
        {
            using var delivery = fixture.Factory.Services.CreateScope();
            var time = DateTimeOffset.Parse("2026-10-03T00:00:00Z");
            var eventId = $"posted-{movement.SourceDocumentLineId}";
            var postedEvent = new StockMovementPostedIntegrationEvent(
                eventId, InventoryIntegrationEventTypes.StockMovementPosted, 1, time, InventoryIntegrationEventSources.BusinessInventory,
                "corr", "cause", movement.OrganizationId, movement.EnvironmentId, "system:inventory", eventId,
                new($"MOV-{movement.SourceDocumentLineId}", "inbound", "wms", movement.SourceDocumentId,
                    movement.SourceDocumentLineId, movement.IdempotencyKey, movement.SkuCode, movement.UomCode,
                    movement.SiteCode, movement.LocationCode, movement.LotNo, null, movement.QualityStatus,
                    movement.OwnerType, null, movement.Quantity, time, movement.UnitCost, null));
            await delivery.ServiceProvider.GetRequiredService<StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted>()
                .HandleCapAsync(postedEvent, default);
        }

        await DeliverPostedAsync(await verifiedDb.InventoryMovementRequests.SingleAsync(x => x.SourceDocumentLineId == "010"));
        await AssertPostingAsync("InventoryPostingPending", "pending", "MOV-010");
        Assert.Empty(await ReadOutboxAsync(verifiedDb, nameof(WmsIntegrationEvent)));
        await verifiedDb.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cap.test_reject_inbound_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Injected inbound completion outbox commit failure';
            END;
            $$;
            CREATE CONSTRAINT TRIGGER test_reject_inbound_completion AFTER INSERT ON cap.published
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                WHEN (NEW."Name" = 'WmsIntegrationEvent') EXECUTE FUNCTION cap.test_reject_inbound_completion()
            """);
        Assert.NotNull(await Record.ExceptionAsync(() => DeliverPostedAsync(retried)));
        await AssertPostingAsync("InventoryPostingPending", "pending", "MOV-010");
        Assert.Empty(await ReadOutboxAsync(verifiedDb, nameof(WmsIntegrationEvent)));
        await verifiedDb.Database.ExecuteSqlRawAsync("DROP TRIGGER test_reject_inbound_completion ON cap.published");

        await DeliverPostedAsync(retried);
        var completedAt = await AssertPostingAsync("Completed", "posted", "MOV-010", "MOV-020");
        await DeliverPostedAsync(retried);
        Assert.Equal(completedAt, await AssertPostingAsync("Completed", "posted", "MOV-010", "MOV-020"));
        var acknowledgement = Assert.Single(await ReadOutboxAsync(verifiedDb, nameof(WmsIntegrationEvent))).GetProperty("Payload");
        Assert.Equal("Completed", acknowledgement.GetProperty("Status").GetString());
        Assert.Equal(batchId, acknowledgement.GetProperty("WmsReceiptBatchId").GetGuid());
        Assert.Equal("RCV-123", acknowledgement.GetProperty("SourceDocumentId").GetString());
        Assert.Equal(WmsSourceDocumentTypes.PurchaseReceipt, acknowledgement.GetProperty("SourceDocumentType").GetString());

        async Task<DateTime?> AssertPostingAsync(string orderStatus, string postingStatus, params string[] movementIds)
        {
            var query = new ListInboundOrdersQuery("org-replay", "env-replay",
                InboundOrderId: receipt.InboundOrderId, SiteCodes: ["SITE"], SiteWideScope: true);
            var item = Assert.Single((await SendAsync(fixture, query)).Items);
            Assert.Equal(orderStatus, item.Status);
            Assert.Equal(postingStatus, item.InventoryPostingStatus);
            Assert.Equal(movementIds, item.InventoryMovementIds!.Order(StringComparer.Ordinal));

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var completedAt = await db.InboundOrders
                .Where(x => x.Id == receipt.InboundOrderId).Select(x => x.CompletedAtUtc).SingleAsync();
            Assert.Equal(orderStatus == "Completed", completedAt.HasValue);
            return completedAt;
        }
    }

    private static async Task<JsonElement[]> ReadOutboxAsync(ApplicationDbContext db, string topic)
    {
        var content = await db.Database.SqlQuery<string>(
            $"""SELECT "Content" AS "Value" FROM cap.published WHERE "Name" = {topic}""").ToArrayAsync();
        return content.Select(value =>
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.GetProperty("Value").Clone();
        }).ToArray();
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Concurrent_cancellation_replays_and_release_converge_to_the_closed_source()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var cancellation = Cancelled();
        var first = await fixture.AddLetterAsync(cancellation, CancellationConsumer);
        var second = await fixture.AddLetterAsync(cancellation with
        {
            EventId = "cancel-republished",
            IdempotencyKey = "cancel-republished-key"
        }, CancellationConsumer);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using (var gate = await db.Database.BeginTransactionAsync())
        {
            await PurchaseOrderInboundLock.AcquireAsync(db, "org-replay", "env-replay", "PO-123", default);
            var deliveries = Task.WhenAll(fixture.ReplayAsync(first, "replayed"), fixture.ReplayAsync(second, "replayed"),
                DeliverAsync(fixture, Released()), DeliverAsync(fixture, cancellation));
            try
            {
                await Eventually.AssertAsync(
                    condition: "all four deliveries wait on the scoped PostgreSQL transaction lock",
                    assertion: async token =>
                    {
                        var waiters = await db.Database.SqlQueryRaw<long>("""
                            SELECT count(*) AS "Value" FROM pg_locks
                            WHERE database = (SELECT oid FROM pg_database WHERE datname = current_database())
                                AND locktype = 'advisory' AND NOT granted AND objsubid = 1
                            """).SingleAsync(token);
                        Assert.Equal(4, waiters);
                    },
                    options: new EventuallyOptions(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), []));
            }
            finally
            {
                await gate.RollbackAsync();
                await deliveries;
            }
        }

        var marker = await db.PurchaseOrderCancellations.SingleAsync();
        Assert.Equal(("org-replay", "env-replay", "PO-123", 4),
            (marker.OrganizationId, marker.EnvironmentId, marker.PurchaseOrderNo, marker.PurchaseOrderVersion));
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync(x => x.ConsumerName == CancellationConsumer));
        Assert.InRange(await db.InboundOrders.CountAsync(), 0, 1);
        Assert.False(await db.InboundOrders.AnyAsync(x => x.Status != InboundOrderStatus.Cancelled));
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Failed_inbound_update_rolls_back_cancellation_and_inbox_then_original_replay_commits()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        await DeliverAsync(fixture, Released());
        var cancellation = Cancelled();
        var letter = await fixture.AddLetterAsync(cancellation, CancellationConsumer);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION wms.test_reject_cancellation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Injected cancellation commit failure';
                END;
                $$;
                CREATE CONSTRAINT TRIGGER test_reject_cancellation AFTER UPDATE ON wms.inbound_orders
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    WHEN (NEW.status = 'Cancelled') EXECUTE FUNCTION wms.test_reject_cancellation()
                """);
            await fixture.ReplayAsync(letter, "failed");
            Assert.Empty(await db.PurchaseOrderCancellations.ToListAsync());
            Assert.False(await db.ProcessedIntegrationEvents.AnyAsync(x => x.ConsumerName == CancellationConsumer));
            Assert.Equal(InboundOrderStatus.Open, (await db.InboundOrders.SingleAsync()).Status);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_reject_cancellation ON wms.inbound_orders");
        }

        await fixture.ReplayAsync(letter, "replayed");
        using var read = fixture.Factory.Services.CreateScope();
        var persisted = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(InboundOrderStatus.Cancelled, (await persisted.InboundOrders.SingleAsync()).Status);
        Assert.Equal(4, (await persisted.PurchaseOrderCancellations.SingleAsync()).PurchaseOrderVersion);
        Assert.Equal(cancellation.EventId, (await persisted.ProcessedIntegrationEvents
            .SingleAsync(x => x.ConsumerName == CancellationConsumer)).EventId);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Cancellation_rejects_foreign_source_invalid_version_and_conflicting_source_facts()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var cancellation = Cancelled();
        var foreign = cancellation with { EventId = "foreign", SourceService = "business-mes" };
        var invalid = cancellation with { EventId = "invalid", Payload = cancellation.Payload with { PurchaseOrderVersion = 0 } };
        await DeliverAsync(fixture, foreign);
        await DeliverAsync(fixture, invalid);
        Assert.Equal("unexpected-source-service", (await fixture.LetterAsync(foreign, CancellationConsumer)).FailureCode);
        Assert.Equal("invalid-purchase-order-cancellation", (await fixture.LetterAsync(invalid, CancellationConsumer)).FailureCode);

        await DeliverAsync(fixture, cancellation);
        var conflict = cancellation with { EventId = "conflict", Payload = cancellation.Payload with { PurchaseOrderVersion = 5 } };
        await DeliverAsync(fixture, conflict);
        var letter = await fixture.LetterAsync(conflict, CancellationConsumer);
        Assert.Equal("purchase-order-cancellation-conflict", letter.FailureCode);
        await fixture.ReplayAsync(letter, "failed");

        var release = Released();
        var mismatchedRelease = release with { Payload = release.Payload with { PurchaseOrderId = "another-erp-id" } };
        await DeliverAsync(fixture, mismatchedRelease);
        Assert.Equal("purchase-order-cancellation-conflict", (await fixture.LetterAsync(mismatchedRelease, Consumer)).FailureCode);

        using var read = fixture.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, (await db.PurchaseOrderCancellations.SingleAsync()).PurchaseOrderVersion);
        Assert.Equal(cancellation.EventId, (await db.ProcessedIntegrationEvents.SingleAsync(x => x.ConsumerName == CancellationConsumer)).EventId);
        Assert.Empty(await db.InboundOrders.ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Cancellation_closes_only_the_scoped_unreceived_auto_order()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var release = Released();
        var accepted = release with { Payload = release.Payload with { PurchaseOrderNo = "PO-ACCEPTED" } };
        await DeliverAsync(fixture, release);
        await DeliverAsync(fixture, accepted);
        await fixture.SeedAsync(db =>
        {
            db.InboundOrders.Add(ManualReceipt("org-replay", "env-replay"));
            db.InboundOrders.Add(PendingReceipt("other-org", "env-replay"));
            db.InboundOrders.Add(PendingReceipt("org-replay", "other-env"));
        });
        using (var receive = fixture.Factory.Services.CreateScope())
        {
            var db = receive.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE wms.inbound_order_lines
                SET received_quantity = 12.5
                WHERE inbound_order_id = (SELECT id FROM wms.inbound_orders WHERE inbound_order_no = 'PO-ACCEPTED');

                UPDATE wms.inbound_orders
                SET receipt_confirmation_key = 'legacy', receipt_confirmation_fingerprint = 'legacy-fingerprint',
                    version = version + 1
                WHERE inbound_order_no = 'PO-ACCEPTED';
                """);
        }
        Assert.Equal(0, await SendAsync(fixture,
            new CancelInboundOrdersForSourceCommand("org-replay", "env-replay", WmsSourceDocumentTypes.PurchaseOrder, "PO-ACCEPTED", "legacy call")));

        var cancellation = Cancelled();
        await DeliverAsync(fixture, cancellation);
        await DeliverAsync(fixture, cancellation with
        {
            EventId = "cancel-accepted",
            IdempotencyKey = "cancel-accepted-key",
            Payload = cancellation.Payload with { PurchaseOrderNo = "PO-ACCEPTED" }
        });
        await DeliverAsync(fixture, release);
        await DeliverAsync(fixture, release with
        {
            Payload = release.Payload with { ReceivingAuthority = PurchaseReceiptInventoryPostingRoute.Direct }
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orders = await read.InboundOrders.Include(x => x.Lines).ToListAsync();
        var closed = Assert.Single(orders, x => x.Status == InboundOrderStatus.Cancelled);
        Assert.Equal(("org-replay", "env-replay", "PO-123", "supplier cancellation"),
            (closed.OrganizationId, closed.EnvironmentId, closed.InboundOrderNo, closed.CancellationReason));
        Assert.Equal(0m, Assert.Single(closed.Lines).ReceivedQuantity);
        var received = Assert.Single(orders, x => x.InboundOrderNo == "PO-ACCEPTED");
        Assert.Equal(InboundOrderStatus.Open, received.Status);
        Assert.Equal(2, received.Version);
        Assert.Equal("legacy", received.ReceiptConfirmationKey);
        Assert.Equal(12.5m, Assert.Single(received.Lines).ReceivedQuantity);
        var manual = Assert.Single(orders, x => x.InboundOrderNo == "MANUAL-123");
        Assert.Equal(InboundOrderStatus.Open, manual.Status);
        Assert.Equal(2m, Assert.Single(manual.Lines).ReceivedQuantity);
        Assert.Empty(await read.InventoryMovementRequests.ToListAsync());
        Assert.Empty(await read.WarehouseTasks.ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Cancellation_before_release_replays_without_creating_an_inbound_order()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var cancellation = Cancelled();
        var letter = await fixture.AddLetterAsync(cancellation, CancellationConsumer);
        await fixture.ReplayAsync(letter, "replayed");

        var release = Released();
        await DeliverAsync(fixture, release);
        await fixture.ReplayAsync(await fixture.AddLetterAsync(release, Consumer), "replayed");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.InboundOrders.ToListAsync());
        Assert.Single(await db.ProcessedIntegrationEvents.Where(x => x.ConsumerName == CancellationConsumer).ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Replay_creates_expected_only_receipt_with_original_order_and_line_numbers()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var message = Released();
        var letter = await fixture.AddLetterAsync(message, Consumer);
        await fixture.ReplayAsync(letter, "replayed");
        await fixture.ReplayAsync(letter, "replayed");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.InboundOrders.Include(x => x.Lines).SingleAsync();
        Assert.Equal(("org-replay", "env-replay", "PO-123", WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "SITE"),
            (order.OrganizationId, order.EnvironmentId, order.InboundOrderNo, order.SourceDocumentType, order.SourceDocumentId, order.SiteCode));
        var line = Assert.Single(order.Lines);
        Assert.Equal(("020", "SKU-A", "kg", 12.5m, 0m, "STAGE-A", "quality", "company"),
            (line.LineNo, line.SkuCode, line.UomCode, line.ExpectedQuantity!.Value, line.ReceivedQuantity,
                line.StagingLocationCode, line.QualityStatus, line.OwnerType));
        Assert.Empty(await db.InventoryMovementRequests.ToListAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Concurrent_deliveries_with_distinct_envelopes_create_one_receipt_for_the_scoped_po()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var message = Released();
        await fixture.SeedAsync(db => db.InboundOrders.Add(ManualReceipt("another-org", "env-replay")));
        await Task.WhenAll(DeliverAsync(fixture, message),
            DeliverAsync(fixture, message with { EventId = "redelivery", IdempotencyKey = "alternate-key" }));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orders = await db.InboundOrders.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal("PO-123", Assert.Single(orders, x => x.OrganizationId == "org-replay").InboundOrderNo);
        Assert.Equal(2m, Assert.Single(Assert.Single(orders, x => x.OrganizationId == "another-org").Lines).ReceivedQuantity);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Missing_scoped_route_can_be_corrected_before_original_event_replay()
    {
        var settings = Settings();
        settings[Route + "EnvironmentId"] = "other-env";
        await using var fixture = await ReplayFixture.CreateAsync(settings: settings);
        var message = Released();
        await DeliverAsync(fixture, message);
        var letter = await fixture.LetterAsync(message, Consumer);
        Assert.Equal("missing-purchase-order-receiving-route", letter.FailureCode);
        await fixture.ReplayAsync(letter, "failed");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.InboundOrders.ToListAsync());
        }

        fixture.Factory.Services.GetRequiredService<IConfiguration>()[Route + "EnvironmentId"] = "env-replay";
        await fixture.ReplayAsync(letter, "replayed");
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Manual_receipt_requires_reconciliation_before_replay_can_create_an_expectation()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        using (var create = fixture.Factory.Services.CreateScope())
        {
            await create.ServiceProvider.GetRequiredService<ISender>().Send(CreateManualReceipt("MANUAL-123"));
        }
        var message = Released();
        await DeliverAsync(fixture, message);
        var letter = await fixture.LetterAsync(message, Consumer);
        Assert.Equal("purchase-order-inbound-conflict", letter.FailureCode);
        await fixture.ReplayAsync(letter, "failed");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var manual = await db.InboundOrders.Include(x => x.Lines).SingleAsync();
            Assert.Equal(2m, Assert.Single(manual.Lines).ReceivedQuantity);
            manual.Cancel("duplicate manual expectation corrected before physical receipt");
            await db.SaveChangesAsync();
        }

        await fixture.ReplayAsync(letter, "replayed");
        using var read = fixture.Factory.Services.CreateScope();
        var conflict = await Assert.ThrowsAsync<KnownException>(() =>
            read.ServiceProvider.GetRequiredService<ISender>().Send(CreateManualReceipt("OTHER-123")));
        Assert.Contains("已有待收货来源单", conflict.Message);
        var orders = await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().InboundOrders.ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Contains(orders, x => x.InboundOrderNo == "MANUAL-123" && x.Status == InboundOrderStatus.Cancelled);
        Assert.Contains(orders, x => x.InboundOrderNo == "PO-123" && x.Status == InboundOrderStatus.Open);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Running_wms_subscribes_to_the_concrete_canonical_topic()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!, "nerv_wms_1752_cap_probe");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:PostgreSQL", database.ConnectionString);
                builder.UseSetting("Messaging:Provider", "InMemory");
                builder.UseSetting("Persistence:AutoMigrate", "true");
                builder.UseSetting("Wms:WorkPoolSeed:Enabled", "false");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = Settings();
                    settings["ConnectionStrings:PostgreSQL"] = database.ConnectionString;
                    settings["Messaging:Provider"] = "InMemory";
                    settings["Persistence:AutoMigrate"] = "true";
                    settings["Wms:WorkPoolSeed:Enabled"] = "false";
                    config.AddInMemoryCollection(settings);
                });
            });
            using var scope = factory.Services.CreateScope();
            var persisted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(database.DatabaseName, persisted.Database.GetDbConnection().Database);
            await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
            var publisher = scope.ServiceProvider.GetRequiredService<ICapPublisher>();
            var topic = PurchaseOrderReleasedV2IntegrationEvent.Topic("Development");
            await publisher.PublishAsync(topic, Released(), Headers("wms"));
            await publisher.PublishAsync(topic, WireRelease("direct", "PO-DIRECT"), Headers("direct"));
            await publisher.PublishAsync(topic, WireRelease(null, "PO-LEGACY"), Headers("legacy"));
            await publisher.PublishAsync(topic, WireRelease("unknown", "PO-UNKNOWN"), Headers("unknown"));

            await Eventually.AssertAsync(
                condition: "CAP completes the supported authorities and fails the unknown authority",
                assertion: async token =>
                {
                    using var read = factory.Services.CreateScope();
                    var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var statuses = await db.Database.SqlQueryRaw<AuthorityDelivery>("""
                        SELECT "Content"::jsonb -> 'Headers' ->> 'test-authority' AS "Authority", "StatusName" AS "Status"
                        FROM cap.received WHERE "Name" = {0}
                        """, topic).ToListAsync(token);
                    Assert.Contains(new AuthorityDelivery("wms", "Succeeded"), statuses);
                    Assert.Contains(new AuthorityDelivery("direct", "Succeeded"), statuses);
                    Assert.Contains(new AuthorityDelivery("legacy", "Succeeded"), statuses);
                    Assert.Contains(new AuthorityDelivery("unknown", "Failed"), statuses);
                },
                options: new EventuallyOptions(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), []));

            var order = Assert.Single(await persisted.InboundOrders.AsNoTracking().Include(x => x.Lines).ToArrayAsync());
            Assert.Equal("PO-123", order.InboundOrderNo);
            var line = Assert.Single(order.Lines);
            Assert.Equal(12.5m, line.ExpectedQuantity);
            Assert.Equal(0m, line.ReceivedQuantity);
            Assert.Empty(await persisted.InventoryMovementRequests.ToArrayAsync());

            await publisher.PublishAsync(PurchaseOrderCancelledIntegrationEvent.Topic("Development"), Cancelled());
            await Eventually.AssertAsync(
                condition: "WMS consumes the canonical cancellation topic and closes the pending order",
                assertion: async token =>
                {
                    using var read = factory.Services.CreateScope();
                    var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    Assert.Equal(InboundOrderStatus.Cancelled, (await db.InboundOrders.SingleAsync(token)).Status);
                    Assert.Equal("cancel-123", (await db.ProcessedIntegrationEvents
                        .SingleAsync(x => x.ConsumerName == CancellationConsumer, token)).EventId);
                },
                options: new EventuallyOptions(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), []));
        }
        finally
        {
            await database.DropAsync();
        }
    }

    private sealed record AuthorityDelivery(string Authority, string Status);

    private static Dictionary<string, string?> Headers(string authority) => new() { ["test-authority"] = authority };

    private static JsonObject WireRelease(string? authority, string orderNo)
    {
        var release = Released();
        var wire = JsonSerializer.SerializeToNode(release with
        {
            EventId = orderNo,
            IdempotencyKey = orderNo,
            Payload = release.Payload with { PurchaseOrderNo = orderNo }
        })!.AsObject();
        var payload = wire["Payload"]!.AsObject();
        if (authority is null)
        {
            payload.Remove("ReceivingAuthority");
        }
        else
        {
            payload["ReceivingAuthority"] = authority;
        }

        return wire;
    }

    private static async Task<ConfirmInboundReceiptCommand> AssignForConfirmationAsync(ReplayFixture fixture, string orderNo)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.InboundOrders.SingleAsync(x => x.InboundOrderNo == orderNo);
        order.AssignWorkPool("receiving", null, order.Version);
        await db.SaveChangesAsync();
        return new(order.Id, order.OrganizationId, order.EnvironmentId, "operator",
            ["SITE"], "site", "SITE", order.Version, "receipt", [new("020", 12.5m)], ExchangeRate: 1.25m);
    }

    private static async Task<Guid> BatchIdAsync(ReplayFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return Assert.IsType<Guid>(await db.InboundOrders.Select(x => x.ReceiptBatchId).SingleAsync());
    }

    private static async Task<T> SendAsync<T>(ReplayFixture fixture, IRequest<T> request)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private sealed class ReceiptAcceptanceTransport : HttpMessageHandler
    {
        private Guid? committedBatch;
        public string ReceiptNo { get; set; } = "RCV-123";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                Assert.True(body.GetProperty("unrestricted").GetBoolean());
                committedBatch = body.GetProperty("wmsReceiptBatchId").GetGuid();
            }
            else if (committedBatch is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.EndsWith($"/{committedBatch}?organizationId=org-replay&environmentId=env-replay", request.RequestUri!.PathAndQuery);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    data = new
                    {
                        wmsReceiptBatchId = committedBatch, purchaseReceiptNo = ReceiptNo,
                        purchaseOrderNo = "PO-123", siteCode = "SITE", exchangeRate = 1.25m,
                        lines = new[] { new { purchaseOrderLineNo = "020", skuCode = "SKU-A", uomCode = "kg",
                            receivedQuantity = 12.5m, locationCode = "STAGE-A", qualityStatus = "unrestricted", unitCost = 15.625m },
                            new { purchaseOrderLineNo = "010", skuCode = "SKU-B", uomCode = "pcs",
                                receivedQuantity = 2m, locationCode = "STAGE-A", qualityStatus = "unrestricted", unitCost = 6.25m } },
                    },
                }),
            };
        }
    }

    private sealed class ReceiptAcceptanceStub(Func<int, CancellationToken, Task<string>> respond)
        : IWmsPurchaseReceiptAcceptanceClient
    {
        public Task<IReadOnlyDictionary<string, decimal>> GetBoundUnitCostsAsync(InboundOrder inbound, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This fixture exercises ERP acceptance only.");

        public List<(Guid Batch, string Order, decimal Rate, decimal Requested, decimal Actual)> Requests { get; } = [];

        public Task<string> GetOrAcceptAsync(InboundOrder inbound, Guid batchId, CancellationToken cancellationToken)
        {
            var line = Assert.Single(inbound.Lines);
            Requests.Add((batchId, inbound.SourceDocumentId, inbound.ReceiptExchangeRate!.Value,
                line.RequestedReceiptQuantity!.Value, line.ReceivedQuantity));
            return respond(Requests.Count, cancellationToken);
        }
    }

    private static async Task DeliverAsync(ReplayFixture fixture, PurchaseOrderCancelledIntegrationEvent message)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ErpPurchaseOrderCancelledIntegrationEventHandler>()
            .HandleCapAsync(message, default);
    }

    private static InboundOrder PendingReceipt(string organization, string environment) => InboundOrder.CreatePendingReceipt(
        organization, environment, "PO-123", WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "SITE",
        [new ExpectedInboundOrderLine("020", "SKU-A", "kg", 12.5m, "STAGE-A", "quality", "company", null)]);

    private static async Task DeliverAsync(ReplayFixture fixture, PurchaseOrderReleasedV2IntegrationEvent message)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ErpPurchaseOrderReleasedIntegrationEventHandler>()
            .HandleCapAsync(message, default);
    }

    private static CreateInboundOrderCommand CreateManualReceipt(string number) => new(
        "org-replay", "env-replay", number, WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "SITE",
        [new WmsInboundLineInput("020", "SKU-A", "kg", 2m, "MANUAL-STAGE", null, null, "quality", "company", null)]);

    private static InboundOrder ManualReceipt(string organization, string environment) => InboundOrder.Create(
        organization, environment, "MANUAL-123", WmsSourceDocumentTypes.PurchaseOrder, "PO-123", "SITE",
        [new InboundOrderLineDraft("020", "SKU-A", "kg", 2m, "MANUAL-STAGE", null, null, "quality", "company", null)]);

    private static Dictionary<string, string?> Settings() => new()
    {
        [Route + "OrganizationId"] = "org-replay",
        [Route + "EnvironmentId"] = "env-replay",
        [Route + "SiteCode"] = "SITE",
        [Route + "StagingLocationCode"] = "STAGE-A",
        [Route + "QualityStatus"] = "quality",
        [Route + "OwnerType"] = "company"
    };

    private static PurchaseOrderCancelledIntegrationEvent Cancelled() => new(
        "cancel-123", PurchaseOrderCancelledIntegrationEvent.Type, PurchaseOrderCancelledIntegrationEvent.Version,
        DateTimeOffset.Parse("2026-10-02T00:00:00Z"), ErpIntegrationEventSources.BusinessErp,
        "corr-123", "cause-123", "org-replay", "env-replay", "system:erp", "cancel-key-123",
        new(Released().Payload.PurchaseOrderId, "PO-123", "SITE", 4, "supplier cancellation"));

    private static PurchaseOrderReleasedV2IntegrationEvent Released() => new(
        "release-123", PurchaseOrderReleasedV2IntegrationEvent.Type, PurchaseOrderReleasedV2IntegrationEvent.Version,
        DateTimeOffset.Parse("2026-10-01T00:00:00Z"), ErpIntegrationEventSources.BusinessErp,
        "corr-123", "cause-123", "org-replay", "env-replay", "system:erp", "release-key-123",
        new("01800000-0000-7000-8000-000000000123", "PO-123", "SUPPLIER", "SITE",
            [new("020", "SKU-A", "kg", 12.5m, new DateOnly(2026, 10, 12))])
        {
            ReceivingAuthority = PurchaseReceiptInventoryPostingRoute.Wms
        });
}
