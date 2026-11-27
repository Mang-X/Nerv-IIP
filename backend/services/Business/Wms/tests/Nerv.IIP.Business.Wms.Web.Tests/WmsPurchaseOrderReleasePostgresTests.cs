using DotNetCore.CAP;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsPurchaseOrderReleasePostgresTests
{
    private const string Consumer = "business-wms.erp-purchase-order-released";
    private const string Route = "PurchaseOrderReceiving:Routes:0:";

    [WmsDeadLetterReplayPostgresFact]
    public async Task Replay_creates_expected_only_receipt_with_original_order_and_line_numbers()
    {
        await using var fixture = await ReplayFixture.CreateAsync(settings: Settings());
        var message = Released();
        await fixture.ReplayAsync(await fixture.AddLetterAsync(message, Consumer), "replayed");

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
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings()));
                builder.UseSetting("Persistence:AutoMigrate", "true");
                builder.UseSetting("Wms:WorkPoolSeed:Enabled", "false");
            });
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
                .PublishAsync(PurchaseOrderReleasedV2IntegrationEvent.Topic("Development"), Released());
            await Eventually.AssertAsync(
                condition: "WMS persists the canonical PO release as an expected-only receipt",
                assertion: async token =>
                {
                    using var read = factory.Services.CreateScope();
                    var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var orders = await db.InboundOrders.AsNoTracking().Include(x => x.Lines)
                        .Where(x => x.OrganizationId == "org-replay" && x.EnvironmentId == "env-replay"
                            && x.InboundOrderNo == "PO-123")
                        .ToArrayAsync(token);
                    var line = Assert.Single(Assert.Single(orders).Lines);
                    Assert.Equal(12.5m, line.ExpectedQuantity);
                    Assert.Equal(0m, line.ReceivedQuantity);
                },
                options: new EventuallyOptions(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250), []));
        }
        finally
        {
            await database.DropAsync();
        }
    }

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

    private static PurchaseOrderReleasedV2IntegrationEvent Released() => new(
        "release-123", PurchaseOrderReleasedV2IntegrationEvent.Type, PurchaseOrderReleasedV2IntegrationEvent.Version,
        DateTimeOffset.Parse("2026-10-01T00:00:00Z"), ErpIntegrationEventSources.BusinessErp,
        "corr-123", "cause-123", "org-replay", "env-replay", "system:erp", "release-key-123",
        new("01800000-0000-7000-8000-000000000123", "PO-123", "SUPPLIER", "SITE",
            [new("020", "SKU-A", "kg", 12.5m, new DateOnly(2026, 10, 12))]));
}
