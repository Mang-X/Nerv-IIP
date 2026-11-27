using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Wms;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

// NERV-2120: DomainInvariant / PublicContract / ProviderBehavior。
// ERP 单服务真实 PostgreSQL；事件转换/财务消费直接调用，不声称 Redis/CAP transport 或 WMS 闭环。
[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class PurchaseReceiptPostingRoutePostgresTests
{
    [PurchaseReceiptPostgresFact]
    public async Task Partial_direct_receipt_and_order_closure_cannot_both_commit_from_same_snapshot()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateConcurrencyProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var suffix in new[] { "cancel-first", "close-first", "receipt-before-cancel", "receipt-before-close" })
                AddReleasedOrder(db, suffix, alreadyReceived: suffix is "close-first" or "receipt-before-close");
            await db.SaveChangesAsync();
        }

        foreach (var (suffix, cancel, receiptWins) in new[]
        {
            ("cancel-first", true, false),
            ("close-first", false, false),
            ("receipt-before-cancel", true, true),
            ("receipt-before-close", false, true),
        })
        {
            var command = cancel ? Command(suffix) : Command(suffix) with
            {
                Lines = [new PurchaseReceiptCommandLine("10", 1m, "unrestricted", "RAW-A-01")],
            };
            await using var receiptScope = provider.CreateAsyncScope();
            await using var closureScope = provider.CreateAsyncScope();
            await LoadOrderAsync(receiptScope.ServiceProvider, command.PurchaseOrderNo);
            await LoadOrderAsync(closureScope.ServiceProvider, command.PurchaseOrderNo);
            using var causation = receiptScope.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>()
                .BeginScope($"command:test:{suffix}");
            var receiptSender = receiptScope.ServiceProvider.GetRequiredService<ISender>();
            var closureSender = closureScope.ServiceProvider.GetRequiredService<ISender>();

            Task CloseOrCancelAsync(ISender sender) => cancel
                ? sender.Send(new CancelPurchaseOrderCommand(command.OrganizationId, command.EnvironmentId, command.PurchaseOrderNo, "cancel"))
                : sender.Send(new ClosePurchaseOrderLineCommand(command.OrganizationId, command.EnvironmentId, command.PurchaseOrderNo, "10", "close"));

            await using var read = provider.CreateAsyncScope();
            var readDb = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (receiptWins)
            {
                var receipt = await receiptSender.Send(command);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CloseOrCancelAsync(closureSender));

                Assert.Equal(receipt.PurchaseReceiptId, (await read.ServiceProvider.GetRequiredService<ISender>().Send(command)).PurchaseReceiptId);
                Assert.Equal(1, await readDb.CodeIdempotencyKeys.CountAsync(x => x.IdempotencyKey == command.IdempotencyKey));
            }
            else
            {
                await CloseOrCancelAsync(closureSender);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => receiptSender.Send(command));

                Assert.False(await readDb.PurchaseReceipts.AnyAsync(x => x.PurchaseReceiptNo == command.PurchaseReceiptNo));
                Assert.False(await readDb.CodeIdempotencyKeys.AnyAsync(x => x.IdempotencyKey == command.IdempotencyKey));
                await Assert.ThrowsAsync<KnownException>(() => read.ServiceProvider.GetRequiredService<ISender>().Send(command));
            }

            var order = await LoadOrderAsync(read.ServiceProvider, command.PurchaseOrderNo);
            var line = Assert.Single(order.Lines);
            Assert.Equal(receiptWins ? PurchaseOrderStatus.Released : cancel ? PurchaseOrderStatus.Cancelled : PurchaseOrderStatus.Closed, order.Status);
            Assert.Equal(receiptWins ? 2m : cancel ? 0m : 1m, line.ReceivedQuantity);
            Assert.Equal(!receiptWins && !cancel, line.FinalDelivery);
        }
    }

    [PurchaseReceiptPostgresFact]
    public async Task Inbound_projection_conflict_rolls_back_and_redelivery_applies_once()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateConcurrencyProvider();
        const string suffix = "inbound-race";
        const string orderNo = "PO-" + suffix;
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            AddReleasedOrder(db, suffix);
            await db.SaveChangesAsync();
        }

        var first = InboundEvent("INB-first", orderNo);
        var second = InboundEvent("INB-second", orderNo);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        // 保留先读到的 PO 版本，让两个 scope 的提交顺序可复现。
        await using (var stale = provider.CreateAsyncScope())
        {
            await LoadOrderAsync(stale.ServiceProvider, orderNo);
            await using var winner = provider.CreateAsyncScope();
            await InboundHandler(winner.ServiceProvider, deadLetters).HandleAsync(first, CancellationToken.None);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                InboundHandler(stale.ServiceProvider, deadLetters).HandleAsync(second, CancellationToken.None));
        }

        await using (var retry = provider.CreateAsyncScope())
        {
            var db = retry.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(first.Payload.PublicReference, (await db.PurchaseReceipts.SingleAsync()).PurchaseReceiptNo);
            Assert.Equal(first.IdempotencyKey, (await db.ProcessedIntegrationEvents.SingleAsync()).IdempotencyKey);
            Assert.Equal(1, await db.CodeIdempotencyKeys.CountAsync());
            await InboundHandler(retry.ServiceProvider, deadLetters).HandleAsync(second, CancellationToken.None);
        }

        await using var replay = provider.CreateAsyncScope();
        await InboundHandler(replay.ServiceProvider, deadLetters).HandleAsync(second, CancellationToken.None);
        var finalDb = replay.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await LoadOrderAsync(replay.ServiceProvider, orderNo);
        Assert.Equal(PurchaseOrderStatus.Released, order.Status);
        Assert.Equal(2m, Assert.Single(order.Lines).ReceivedQuantity);
        var receipts = await finalDb.PurchaseReceipts.Include(x => x.Lines).OrderBy(x => x.PurchaseReceiptNo).ToArrayAsync();
        Assert.Equal(new[] { first.Payload.PublicReference, second.Payload.PublicReference }, receipts.Select(x => x.PurchaseReceiptNo));
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(PurchaseReceiptInventoryPostingRoute.Wms, receipt.InventoryPostingRoute);
            Assert.Equal(1m, Assert.Single(receipt.Lines).ReceivedQuantity);
        });
        Assert.Equal(2, await finalDb.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(2, await finalDb.CodeIdempotencyKeys.CountAsync());
        Assert.Empty(await deadLetters.ListAsync(
            WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
    }

    [PurchaseReceiptPostgresFact]
    public async Task Wms_batches_and_cancellation_share_the_purchase_order_write_boundary()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateConcurrencyProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var suffix in new[] { "wms-cancel-first", "wms-batches" })
            {
                AddReleasedOrder(db, suffix, authority: PurchaseReceiptInventoryPostingRoute.Wms);
            }
            await db.SaveChangesAsync();
        }

        foreach (var cancelFirst in new[] { true, false })
        {
            var orderNo = cancelFirst ? "PO-wms-cancel-first" : "PO-wms-batches";
            var request = new AcceptWmsPurchaseReceiptCommand(
                "org-route", "env-route", Guid.NewGuid(), orderNo, "SITE-001", 1.2m, [new("10", 1m)]);
            var competingBatch = request with { WmsReceiptBatchId = Guid.NewGuid() };
            await using var stale = provider.CreateAsyncScope();
            await LoadOrderAsync(stale.ServiceProvider, orderNo);
            using var staleCausation = stale.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>()
                .BeginScope("command:test:wms-stale");
            await using (var winner = provider.CreateAsyncScope())
            {
                using var winnerCausation = winner.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>()
                    .BeginScope("command:test:wms-winner");
                var sender = winner.ServiceProvider.GetRequiredService<ISender>();
                if (cancelFirst)
                {
                    await sender.Send(new CancelPurchaseOrderCommand(
                        request.OrganizationId, request.EnvironmentId, orderNo, "cancel"));
                }
                else
                {
                    await sender.Send(competingBatch);
                }
            }

            var staleSender = stale.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleSender.Send(request));

            await using var recovery = provider.CreateAsyncScope();
            using var recoveryCausation = recovery.ServiceProvider.GetRequiredService<IErpIntegrationEventContextAccessor>()
                .BeginScope("command:test:wms-recovery");
            var recoverySender = recovery.ServiceProvider.GetRequiredService<ISender>();
            var query = new GetWmsPurchaseReceiptAcceptanceQuery(
                request.OrganizationId, request.EnvironmentId, request.WmsReceiptBatchId);
            Assert.Null(await recoverySender.Send(query));
            if (cancelFirst)
            {
                await Assert.ThrowsAsync<KnownException>(() => recoverySender.Send(request));
            }
            else
            {
                var first = await recoverySender.Send(query with { WmsReceiptBatchId = competingBatch.WmsReceiptBatchId });
                Assert.NotNull(first);
                var second = await recoverySender.Send(request);
                Assert.NotEqual(first.PurchaseReceiptNo, second.PurchaseReceiptNo);
                Assert.Equal(second.PurchaseReceiptNo, (await recoverySender.Send(query))?.PurchaseReceiptNo);
            }
        }

        await using var verification = provider.CreateAsyncScope();
        var cancelled = await LoadOrderAsync(verification.ServiceProvider, "PO-wms-cancel-first");
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, Assert.Single(cancelled.Lines).ReceivedQuantity);
        var received = await LoadOrderAsync(verification.ServiceProvider, "PO-wms-batches");
        Assert.Equal(2m, Assert.Single(received.Lines).ReceivedQuantity);
    }

    private static void AddReleasedOrder(ApplicationDbContext db, string suffix, bool alreadyReceived = false,
        PurchaseReceiptInventoryPostingRoute authority = PurchaseReceiptInventoryPostingRoute.Direct)
    {
        var order = PurchaseOrder.Create("org-route", "env-route", $"PO-{suffix}", "SUP-001", "SITE-001", "CNY",
            [new PurchaseOrderLineDraft("10", "SKU-001", "pcs", 3m, 12.5m, new DateOnly(2026, 9, 1), UnderReceiptTolerancePercent: 70m)], authority);
        order.MarkApprovalRequested($"approval-{suffix}");
        order.ReleaseAfterApproval($"approval-{suffix}");
        if (alreadyReceived)
            order.RegisterReceipt("10", 1m);
        db.PurchaseOrders.Add(order);
    }

    private static Task<PurchaseOrder> LoadOrderAsync(IServiceProvider services, string purchaseOrderNo) =>
        services.GetRequiredService<ApplicationDbContext>().PurchaseOrders.Include(x => x.Lines)
            .SingleAsync(x => x.PurchaseOrderNo == purchaseOrderNo);

    private static ServiceProvider CreateConcurrencyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationEvents(typeof(Program));
        services.AddHttpContextAccessor();
        services.AddScoped<IErpIntegrationEventContextAccessor, HttpErpIntegrationEventContextAccessor>();
        services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(RecordPurchaseReceiptCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddErpPostgreSqlPersistence(ErpPostgresLaneDatabase.ConnectionString);
        services.AddScoped<ErpCodingService>();
        services.AddSingleton<IWmsInboundCancellationClient, NoopWmsInboundCancellationClient>();
        return services.BuildServiceProvider();
    }

    private static WmsIntegrationEvent InboundEvent(string inboundNo, string orderNo) => new(
        $"evt-{inboundNo}", WmsIntegrationEventTypes.InboundOrderCompleted, 1,
        new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), WmsIntegrationEventSources.BusinessWms, "corr", "cause",
        "org-route", "env-route", "system:test", $"key-{inboundNo}",
        new WmsIntegrationPayload(inboundNo, null, null, null, null, null, null, "completed", null, null,
            Lines: [new WmsIntegrationPayloadLine("10", "SKU-001", "pcs", "SITE-001", "RAW-A-01", 1m, "qualified")],
            SourceDocumentType: WmsSourceDocumentTypes.PurchaseOrder, SourceDocumentId: orderNo));

    private static WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt InboundHandler(
        IServiceProvider services, IIntegrationEventDeadLetterStore deadLetters) => new(
        services.GetRequiredService<ApplicationDbContext>(),
        deadLetters,
        services.GetRequiredService<ErpCodingService>(),
        NullLogger<WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt>.Instance,
        services.GetRequiredService<IErpIntegrationEventContextAccessor>());

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopWmsInboundCancellationClient : IWmsInboundCancellationClient
    {
        public Task CancelOpenInboundOrdersForPurchaseOrderAsync(
            string organizationId, string environmentId, string purchaseOrderNo, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [PurchaseReceiptPostgresFact]
    public async Task Routes_persist_replay_without_events_and_keep_financial_facts_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
            await db.Database.MigrateAsync();
            foreach (var suffix in new[] { "direct", "wms" })
            {
                var order = PurchaseOrder.Create("org-route", "env-route", $"PO-{suffix}", "SUP-001", "SITE-001",
                    [new PurchaseOrderLineDraft("10", "SKU-001", "pcs", 3m, 12.5m, new DateOnly(2026, 9, 1))]);
                order.MarkApprovalRequested($"approval-{suffix}");
                order.ReleaseAfterApproval($"approval-{suffix}");
                db.PurchaseOrders.Add(order);
            }
            await db.SaveChangesAsync();
        }

        foreach (var (suffix, route, inventoryCount) in new[]
        {
            ("direct", PurchaseReceiptInventoryPostingRoute.Direct, 1),
            ("wms", PurchaseReceiptInventoryPostingRoute.Wms, 0),
        })
        {
            var command = Command(suffix) with { InventoryPostingRoute = route };
            PurchaseReceiptId receiptId;
            PurchaseReceiptRecordedIntegrationEvent recorded;
            await using (var write = provider.CreateAsyncScope())
            {
                var db = write.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                receiptId = (await Handler(write.ServiceProvider).Handle(command, CancellationToken.None)).PurchaseReceiptId;
                var receipt = db.PurchaseReceipts.Local.Single();
                var movements = receipt.GetDomainEvents().OfType<PurchaseReceiptInventoryMovementRequestedDomainEvent>().ToArray();
                Assert.Equal(inventoryCount, movements.Length);
                if (inventoryCount == 1)
                {
                    var inbound = new PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter().Convert(Assert.Single(movements));
                    Assert.Equal("inbound", inbound.Payload.MovementType);
                    Assert.Equal(2m, inbound.Payload.Quantity);
                    Assert.Equal(12.5m, inbound.Payload.UnitCost);
                }
                recorded = new PurchaseReceiptRecordedIntegrationEventConverter().Convert(
                    Assert.Single(receipt.GetDomainEvents().OfType<PurchaseReceiptRecordedDomainEvent>()));
                await db.SaveChangesAsync();
            }

            await using (var amend = provider.CreateAsyncScope())
            {
                var db = amend.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var order = await db.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.PurchaseOrderNo == $"PO-{suffix}");
                var change = order.RequestChange([new PurchaseOrderLineChangeDraft("10", 3m, 99m, new DateOnly(2026, 9, 1))]);
                change.AssignApprovalChain($"amend-{suffix}");
                order.ApplyApprovedChange($"amend-{suffix}");
                await db.SaveChangesAsync();
            }

            await using (var read = provider.CreateAsyncScope())
            {
                var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var source = await new GetPurchaseReceiptSourceDocumentQueryHandler(db).Handle(
                    new("org-route", "env-route", $"RCV-{suffix}"), CancellationToken.None);
                Assert.NotNull(source);
                Assert.Equal(route, source.InventoryPostingRoute);
                Assert.Equal(2m, Assert.Single(source.Lines).ReceivedQuantity);
                Assert.Equal("CNY", source.CurrencyCode);
                Assert.Equal(1m, source.ExchangeRate);
                Assert.Equal(12.5m, Assert.Single(source.Lines).UnitPrice);
                Assert.Equal(12.5m, Assert.Single(source.Lines).EstimatedUnitCost);
                Assert.Equal(receiptId, (await Handler(read.ServiceProvider).Handle(command, CancellationToken.None)).PurchaseReceiptId);
                Assert.Empty(db.PurchaseReceipts.Local.Single().GetDomainEvents());
                await db.SaveChangesAsync();
                var otherRoute = route == PurchaseReceiptInventoryPostingRoute.Direct
                    ? PurchaseReceiptInventoryPostingRoute.Wms : PurchaseReceiptInventoryPostingRoute.Direct;
                var conflict = await Assert.ThrowsAsync<KnownException>(() => Handler(read.ServiceProvider).Handle(
                    command with { InventoryPostingRoute = otherRoute }, CancellationToken.None));
                Assert.Equal("本次提交与此前的创建内容不一致，请重新发起创建。", conflict.Message);
            }

            await using (var finance = provider.CreateAsyncScope())
            {
                var db = finance.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
                var consumer = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(db, deadLetters, ErpTestCoding.For(db));
                await consumer.HandleAsync(recorded, CancellationToken.None);
                await db.SaveChangesAsync();
                await consumer.HandleAsync(recorded, CancellationToken.None);
                await db.SaveChangesAsync();
                Assert.Empty(await deadLetters.ListAsync(
                    PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual.ConsumerName,
                    IntegrationEventDeadLetterStatus.Pending, CancellationToken.None));
            }
        }

        await using var final = provider.CreateAsyncScope();
        var finalDb = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await finalDb.PurchaseReceipts.CountAsync());
        Assert.Equal(2, await finalDb.CodeIdempotencyKeys.CountAsync());
        var vouchers = await finalDb.JournalVouchers.Include(x => x.Lines).ToArrayAsync();
        Assert.Equal(2, vouchers.Length);
        foreach (var voucher in vouchers)
        {
            Assert.Equal(25m, voucher.Lines.Sum(x => x.DebitAmount));
            Assert.Equal(25m, voucher.Lines.Sum(x => x.CreditAmount));
        }
        Assert.All(await finalDb.PurchaseOrders.Include(x => x.Lines).ToArrayAsync(),
            order => Assert.Equal(2m, Assert.Single(order.Lines).ReceivedQuantity));

        var followingReceipt = Command("direct") with
        {
            PurchaseReceiptNo = "RCV-direct-next",
            IdempotencyKey = "receipt-direct-next",
            Lines = [new PurchaseReceiptCommandLine("10", 1m, "unrestricted", "RAW-A-01")],
        };
        var next = await Handler(final.ServiceProvider).Handle(followingReceipt, CancellationToken.None);
        Assert.Equal("RCV-direct-next", next.PurchaseReceiptNo);
        await finalDb.SaveChangesAsync();

        await using var excess = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<KnownException>(() => Handler(excess.ServiceProvider).Handle(
            followingReceipt with { PurchaseReceiptNo = "RCV-direct-excess", IdempotencyKey = "receipt-direct-excess" },
            CancellationToken.None));

        await using var readAfter = provider.CreateAsyncScope();
        var readAfterDb = readAfter.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(3, await readAfterDb.PurchaseReceipts.CountAsync());
        var directOrder = await readAfterDb.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.PurchaseOrderNo == "PO-direct");
        Assert.Equal(3m, Assert.Single(directOrder.Lines).ReceivedQuantity);
        var replay = await Handler(readAfter.ServiceProvider).Handle(Command("direct"), CancellationToken.None);
        Assert.Equal("RCV-direct", replay.PurchaseReceiptNo);
        Assert.NotEqual(next.PurchaseReceiptId, replay.PurchaseReceiptId);
    }

    [PurchaseReceiptPostgresFact]
    public async Task Legacy_receipt_migrates_to_direct_and_old_key_replays_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateProvider();
        var command = Command("legacy");
        await using (var setup = provider.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var previousMigration = db.Database.GetMigrations().TakeWhile(x => !x.EndsWith("_AddPurchaseReceiptInventoryPostingRoute", StringComparison.Ordinal)).Last();
            await db.GetService<IMigrator>().MigrateAsync(previousMigration);
            // 历史样本使用旧表列，避免当前 EF 模型向旧表写入新增列。
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO erp.purchase_orders
                    (id, organization_id, environment_id, purchase_order_no, supplier_code, site_code,
                     currency_code, status, version, approval_chain_id, total_amount, created_at_utc)
                VALUES ('00000000-0000-0000-0000-000000002118', 'org-route', 'env-route', 'PO-legacy',
                        'SUP-001', 'SITE-001', 'CNY', 'Closed', 1, 'approval-legacy', 25, '2026-09-01T00:00:00Z');
                INSERT INTO erp.purchase_order_lines
                    (id, purchase_order_id, line_no, sku_code, uom_code, ordered_quantity, received_quantity,
                     unit_price, promised_date, over_receipt_tolerance_percent, under_receipt_tolerance_percent, final_delivery)
                VALUES ('00000000-0000-0000-0000-000000002119', '00000000-0000-0000-0000-000000002118',
                        '10', 'SKU-001', 'pcs', 2, 2, 12.5, '2026-09-01', 0, 0, false);
                INSERT INTO erp.purchase_receipts
                    (id, organization_id, environment_id, purchase_receipt_no, purchase_order_no,
                     supplier_code, site_code, currency_code, exchange_rate, quality_status, status, recorded_at_utc)
                VALUES ('00000000-0000-0000-0000-000000002120', 'org-route', 'env-route', 'RCV-legacy', 'PO-legacy',
                        'SUP-001', 'SITE-001', 'CNY', 1, 'unrestricted', 'Recorded', '2026-09-01T00:00:00Z');
                INSERT INTO erp.purchase_receipt_lines
                    (id, purchase_receipt_id, purchase_order_line_no, sku_code, uom_code, location_code, received_quantity, quality_status)
                VALUES ('00000000-0000-0000-0000-000000002121', '00000000-0000-0000-0000-000000002120',
                        '10', 'SKU-001', 'pcs', 'SITE-001', 2, 'unrestricted')
                """);
            await setup.ServiceProvider.GetRequiredService<ErpCodingService>().AllocateAsync(
                command.OrganizationId, command.EnvironmentId, "purchase-receipt", command.PurchaseReceiptNo,
                command.IdempotencyKey,
                ErpCodingService.Fingerprint(command.PurchaseOrderNo, command.ExchangeRate,
                    command.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.ReceivedQuantity}:{x.QualityStatus}:{x.FinalDelivery}")),
                CancellationToken.None);
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var readDb = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legacyOrder = await readDb.PurchaseOrders.SingleAsync();
        Assert.Equal(PurchaseReceiptInventoryPostingRoute.Direct, legacyOrder.ReceivingAuthority);
        var receipt = await readDb.PurchaseReceipts.SingleAsync();
        Assert.Equal(PurchaseReceiptInventoryPostingRoute.Direct, receipt.InventoryPostingRoute);
        var source = await new GetPurchaseReceiptSourceDocumentQueryHandler(readDb).Handle(
            new("org-route", "env-route", "RCV-legacy"), CancellationToken.None);
        Assert.Equal(PurchaseReceiptInventoryPostingRoute.Direct, source!.InventoryPostingRoute);
        Assert.Equal(2m, Assert.Single(source.Lines).ReceivedQuantity);
        Assert.Null(Assert.Single(source.Lines).UnitPrice);
        Assert.Null(Assert.Single(source.Lines).EstimatedUnitCost);
        Assert.Equal(receipt.Id, (await Handler(read.ServiceProvider).Handle(command, CancellationToken.None)).PurchaseReceiptId);
        Assert.Empty(receipt.GetDomainEvents());
        var conflict = await Assert.ThrowsAsync<KnownException>(() => Handler(read.ServiceProvider).Handle(
            command with { InventoryPostingRoute = PurchaseReceiptInventoryPostingRoute.Wms }, CancellationToken.None));
        Assert.Equal("本次提交与此前的创建内容不一致，请重新发起创建。", conflict.Message);
        Assert.Equal(1, await readDb.PurchaseReceipts.CountAsync());
        var recorded = new PurchaseReceiptRecordedIntegrationEventConverter().Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(readDb, deadLetters, ErpTestCoding.For(readDb));
        await consumer.HandleAsync(recorded, CancellationToken.None);
        await readDb.SaveChangesAsync();
        await consumer.HandleAsync(recorded, CancellationToken.None);
        await readDb.SaveChangesAsync();
        Assert.Equal(25m, Assert.Single(await readDb.JournalVouchers.Include(x => x.Lines).ToArrayAsync()).Lines.Sum(x => x.DebitAmount));
        Assert.Null(Assert.Single(receipt.Lines).UnitPrice);
    }

    private static RecordPurchaseReceiptCommand Command(string suffix) => new(
        "org-route", "env-route", $"RCV-{suffix}", $"PO-{suffix}",
        [new PurchaseReceiptCommandLine("10", 2m, "unrestricted", "RAW-A-01")], $"receipt-{suffix}");

    private static RecordPurchaseReceiptCommandHandler Handler(IServiceProvider services) => new(
        services.GetRequiredService<ApplicationDbContext>(), services.GetRequiredService<ErpCodingService>());

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(RecordPurchaseReceiptCommand).Assembly));
        services.AddErpPostgreSqlPersistence(ErpPostgresLaneDatabase.ConnectionString);
        services.AddScoped<ErpCodingService>();
        return services.BuildServiceProvider();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PurchaseReceiptPostgresFactAttribute : FactAttribute
{
    public PurchaseReceiptPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP purchase-receipt route acceptance test.";
    }
}
