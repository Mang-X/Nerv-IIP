using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

// NERV-2120: DomainInvariant / PublicContract / ProviderBehavior。
// ERP 单服务真实 PostgreSQL；事件转换/财务消费直接调用，不声称 Redis/CAP transport 或 WMS 闭环。
[Collection(ErpPostgresLaneDatabase.CollectionName)]
public sealed class PurchaseReceiptPostingRoutePostgresTests
{
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
                receiptId = await Handler(write.ServiceProvider).Handle(command, CancellationToken.None);
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
                Assert.Equal(receiptId, await Handler(read.ServiceProvider).Handle(command, CancellationToken.None));
                Assert.Empty(db.PurchaseReceipts.Local.Single().GetDomainEvents());
                await db.SaveChangesAsync();
                var otherRoute = route == PurchaseReceiptInventoryPostingRoute.Direct
                    ? PurchaseReceiptInventoryPostingRoute.Wms : PurchaseReceiptInventoryPostingRoute.Direct;
                var conflict = await Assert.ThrowsAsync<KnownException>(() => Handler(read.ServiceProvider).Handle(
                    command with { InventoryPostingRoute = otherRoute }, CancellationToken.None));
                Assert.Contains("conflicts", conflict.Message, StringComparison.Ordinal);
            }

            await using (var finance = provider.CreateAsyncScope())
            {
                var db = finance.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
                var consumer = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(db, deadLetters);
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
            var order = PurchaseOrder.Create("org-route", "env-route", "PO-legacy", "SUP-001", "SITE-001",
                [new PurchaseOrderLineDraft("10", "SKU-001", "pcs", 2m, 12.5m, new DateOnly(2026, 9, 1))]);
            order.MarkApprovalRequested("approval-legacy");
            order.ReleaseAfterApproval("approval-legacy");
            order.RegisterReceipt("10", 2m);
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            // 升级前旧表没有路径列；保留旧生产指纹，不能用新处理器生成“历史”样本。
            await db.Database.ExecuteSqlRawAsync("""
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
        var receipt = await readDb.PurchaseReceipts.SingleAsync();
        Assert.Equal(PurchaseReceiptInventoryPostingRoute.Direct, receipt.InventoryPostingRoute);
        var source = await new GetPurchaseReceiptSourceDocumentQueryHandler(readDb).Handle(
            new("org-route", "env-route", "RCV-legacy"), CancellationToken.None);
        Assert.Equal(PurchaseReceiptInventoryPostingRoute.Direct, source!.InventoryPostingRoute);
        Assert.Equal(2m, Assert.Single(source.Lines).ReceivedQuantity);
        Assert.Null(Assert.Single(source.Lines).UnitPrice);
        Assert.Null(Assert.Single(source.Lines).EstimatedUnitCost);
        Assert.Equal(receipt.Id, await Handler(read.ServiceProvider).Handle(command, CancellationToken.None));
        Assert.Empty(receipt.GetDomainEvents());
        var conflict = await Assert.ThrowsAsync<KnownException>(() => Handler(read.ServiceProvider).Handle(
            command with { InventoryPostingRoute = PurchaseReceiptInventoryPostingRoute.Wms }, CancellationToken.None));
        Assert.Contains("conflicts", conflict.Message, StringComparison.Ordinal);
        Assert.Equal(1, await readDb.PurchaseReceipts.CountAsync());
        var recorded = new PurchaseReceiptRecordedIntegrationEventConverter().Convert(new PurchaseReceiptRecordedDomainEvent(receipt));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(readDb, deadLetters);
        await consumer.HandleAsync(recorded, CancellationToken.None);
        await readDb.SaveChangesAsync();
        await consumer.HandleAsync(recorded, CancellationToken.None);
        await readDb.SaveChangesAsync();
        Assert.Equal(25m, Assert.Single(await readDb.JournalVouchers.Include(x => x.Lines).ToArrayAsync()).Lines.Sum(x => x.DebitAmount));
        Assert.Null(Assert.Single(receipt.Lines).UnitPrice);
    }

    private static RecordPurchaseReceiptCommand Command(string suffix) => new(
        "org-route", "env-route", $"RCV-{suffix}", $"PO-{suffix}",
        [new PurchaseReceiptCommandLine("10", 2m, "unrestricted")], $"receipt-{suffix}");

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
