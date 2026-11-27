using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetCore.CAP;
using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Web.Endpoints.DeadLetters;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionTasks;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;
using StackExchange.Redis;
using Xunit.Abstractions;
using ErpDb = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using InventoryDb = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using QualityDb = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;
using WmsDb = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

// Service HTTP via TestServer, with independent PostgreSQL databases and real Redis/CAP delivery.
public sealed class WarehousePurchaseReceiptBrokerAcceptanceTests(ITestOutputHelper output)
{
    private const string Organization = "org-receipt-acceptance";
    private const string EnvironmentId = "env-receipt-acceptance";
    private const string Site = "SITE-01";
    private const string Location = "RECEIVING";
    private const string Token = "warehouse-receipt-acceptance-token";

    [WarehousePurchaseReceiptBrokerFact]
    public Task Warehouse_release_explicit_receipt_and_inventory_completion_converge_to_one_valued_erp_receipt() =>
        RunReceiptAsync();

    [WarehousePurchaseReceiptBrokerFact]
    public Task Passed_receiving_inspection_posts_unrestricted_inventory() =>
        RunReceiptAsync("passed", "passed", "unrestricted", 5m);

    [WarehousePurchaseReceiptBrokerFact]
    public Task Concession_receiving_inspection_posts_restricted_inventory() =>
        RunReceiptAsync("conditional-release", "conditional-release", "restricted", 15m);

    [WarehousePurchaseReceiptBrokerFact]
    public Task Rejected_receiving_inspection_posts_blocked_inventory_and_opens_one_return() =>
        RunReceiptAsync("failed", "rejected", "blocked", 15m);

    private async Task RunReceiptAsync(string? resultLine = null, string? disposition = null,
        string stockQuality = "unrestricted", decimal measured = 0)
    {
        var postgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var version = $"wr-{Guid.NewGuid():N}"[..20];
        await using var redisScope = new CapRedisScope(await ConnectionMultiplexer.ConnectAsync(redis), version);
        output.WriteLine($"CAP Redis topic namespace {version}.");
        await using var erpDatabase = await PostgreSqlTestDatabase.CreateAsync(postgres, "nerv_1752_erp");
        await using var wmsDatabase = await PostgreSqlTestDatabase.CreateAsync(postgres, "nerv_1752_wms");
        await using var inventoryDatabase = await PostgreSqlTestDatabase.CreateAsync(postgres, "nerv_1752_inventory");
        await using var qualityDatabase = await PostgreSqlTestDatabase.CreateAsync(postgres, "nerv_1752_quality");
        await using var erp = Host<RecordPurchaseReceiptEndpoint>(erpDatabase.ConnectionString, redis, version, new()
        {
            ["PurchaseOrderReceiving:WmsScopes:0:OrganizationId"] = Organization,
            ["PurchaseOrderReceiving:WmsScopes:0:EnvironmentId"] = EnvironmentId,
            ["PurchaseOrderReceiving:WmsScopes:0:SiteCode"] = Site,
            ["Wms:BaseUrl"] = "https://wms.test",
        });
        await using var inventory = Host<ListInventoryDeadLettersEndpoint>(inventoryDatabase.ConnectionString, redis, version, new()
        {
            ["Inventory:Seed:Enabled"] = "false",
        });
        await using var wms = Host<ConfirmInboundReceiptEndpoint>(wmsDatabase.ConnectionString, redis, version, new()
        {
            ["PurchaseOrderReceiving:Routes:0:OrganizationId"] = Organization,
            ["PurchaseOrderReceiving:Routes:0:EnvironmentId"] = EnvironmentId,
            ["PurchaseOrderReceiving:Routes:0:SiteCode"] = Site,
            ["PurchaseOrderReceiving:Routes:0:StagingLocationCode"] = Location,
            ["PurchaseOrderReceiving:Routes:0:QualityStatus"] = resultLine is null ? "unrestricted" : "quality",
            ["PurchaseOrderReceiving:Routes:0:OwnerType"] = "company",
            ["Wms:WorkPoolSeed:Enabled"] = "false",
            ["Erp:BaseUrl"] = "https://erp.test",
            ["Inventory:BaseUrl"] = "https://inventory.test",
        }, services =>
        {
            services.AddHttpClient<IWmsPurchaseReceiptAcceptanceClient, HttpWmsPurchaseReceiptAcceptanceClient>()
                .ConfigurePrimaryHttpMessageHandler(() => erp.Server.CreateHandler());
            services.AddHttpClient<IWmsInventoryReservationClient, HttpWmsInventoryReservationClient>()
                .ConfigurePrimaryHttpMessageHandler(() => inventory.Server.CreateHandler());
        });
        await using var quality = resultLine is null ? null
            : Host<CreateInspectionRecordFromTaskEndpoint>(qualityDatabase.ConnectionString, redis, version, new()
            {
                ["Quality:Seed:Enabled"] = "false",
                ["ConnectionStrings:Redis"] = redis,
            });
        using var qualityHttp = quality is null ? null : Client(quality);
        using var wmsHttp = Client(wms);
        using var erpHttp = Client(erp);
        await CapTestHost.WaitForCapBootstrapAsync(erp.Services);
        await CapTestHost.WaitForCapBootstrapAsync(wms.Services);
        await CapTestHost.WaitForCapBootstrapAsync(inventory.Services);
        if (quality is not null)
        {
            await CapTestHost.WaitForCapBootstrapAsync(quality.Services);
            await using var scope = quality.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<QualityDb>();
            var plan = InspectionPlan.Create(Organization, EnvironmentId, "PLAN-RECEIVING", "receiving", "SKU-01",
                null, null, null, null);
            plan.AddCharacteristic("dimension", "Dimension", "measurement", "major", required: true, "100%",
                "variable", 5m, 0m, 10m, null, null);
            plan.Activate();
            db.InspectionPlans.Add(plan);
            await db.SaveChangesAsync();
        }
        await using (var scope = inventory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
            db.StockLocations.Add(StockLocation.CreateOrUpdate(null, Organization, EnvironmentId,
                Location, "receiving", Site, null, "active"));
            await db.SaveChangesAsync();
        }

        const string orderNo = "PO-RECEIVE";
        await ReleaseAsync(orderNo);
        await WaitAsync("WMS persists one pending order with the original line and zero actual receipt", async () =>
        {
            await using var scope = wms.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
            var order = Assert.Single(await db.InboundOrders.Include(x => x.Lines).ToListAsync());
            Assert.Equal(orderNo, order.InboundOrderNo);
            Assert.Equal(InboundOrderStatus.Open, order.Status);
            Assert.Equal(Organization, order.OrganizationId);
            Assert.Equal(EnvironmentId, order.EnvironmentId);
            var line = Assert.Single(order.Lines);
            Assert.Equal("020", line.LineNo);
            Assert.Equal(12m, line.ExpectedQuantity);
            Assert.Equal(0m, line.ReceivedQuantity);
            Assert.Empty(await db.InventoryMovementRequests.ToArrayAsync());
        });

        InboundOrderId inboundOrderId;
        long assignedVersion;
        const string actor = "acceptance-warehouse-operator";
        const string poolCode = "ACCEPTANCE-RECEIVING";
        await using (var scope = wms.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
            var now = DateTime.UtcNow;
            db.WarehouseWorkPools.Add(WarehouseWorkPool.Create(Organization, EnvironmentId, poolCode, "Receiving", Site));
            db.WarehouseWorkPoolMemberships.Add(WarehouseWorkPoolMembership.Create(Organization, EnvironmentId,
                poolCode, actor, now.AddMinutes(-5), now.AddHours(1)));
            var order = await db.InboundOrders.SingleAsync();
            order.AssignWorkPool(poolCode, actor, order.Version);
            await db.SaveChangesAsync();
            inboundOrderId = order.Id;
            assignedVersion = order.Version;
        }

        var path = $"/api/business/v1/wms/inbound-orders/{inboundOrderId}";
        var confirmation = new
        {
            organizationId = Organization, environmentId = EnvironmentId, actorPrincipalId = actor,
            authorizedSiteCodes = new[] { Site }, scopeKind = "self", scopeId = actor,
            expectedVersion = assignedVersion, idempotencyKey = "confirm-receipt", exchangeRate = 1.25m,
            lines = new[] { new { lineNo = "020", receivedQuantity = 12m } },
        };
        using (var denied = await wmsHttp.PostAsJsonAsync($"{path}/confirm-receipt",
            confirmation with { authorizedSiteCodes = new[] { "SITE-OTHER" } }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            output.WriteLine("WMS confirm-receipt with out-of-scope site returned HTTP 403.");
        }
        await using (var scope = erp.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDb>();
            Assert.Empty(await db.PurchaseReceipts.ToArrayAsync());
        }
        var confirmed = await PostAsync(wmsHttp, $"{path}/confirm-receipt", confirmation);
        var replay = await PostAsync(wmsHttp, $"{path}/confirm-receipt", confirmation);
        Assert.Equal(confirmed.GetProperty("version").GetInt64(), replay.GetProperty("version").GetInt64());

        var productionDate = new DateOnly(2026, 11, 1);
        var expiryDate = new DateOnly(2036, 11, 1);
        var completion = new
        {
            organizationId = Organization, environmentId = EnvironmentId, actorPrincipalId = actor,
            authorizedSiteCodes = new[] { Site }, scopeKind = "self", scopeId = actor,
            expectedVersion = confirmed.GetProperty("version").GetInt64(), idempotencyKey = "complete-receipt",
            lines = disposition == "rejected"
                ? new[] { new InboundOrderLineCapture("020", null, productionDate, expiryDate) }
                : null,
        };
        if (quality is not null)
        {
            using var premature = await wmsHttp.PostAsJsonAsync($"{path}/complete", completion);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, premature.StatusCode);

            string? taskId = null;
            long taskVersion = 0;
            await WaitAsync("Quality creates one pending inspection task from the ERP receipt", async () =>
            {
                await using var scope = quality.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<QualityDb>();
                var task = Assert.Single(await db.InspectionTasks.ToArrayAsync());
                Assert.Equal("erp", task.SourceService);
                Assert.Equal("020", task.SourceDocumentLineId);
                Assert.Equal(12m, task.Quantity);
                Assert.Equal("pending", task.Status);
                taskId = task.Id.ToString();
                taskVersion = task.Version;
            });
            const string inspector = "acceptance-quality-inspector";
            var qualityTaskPath = $"/api/business/v1/quality/inspection-tasks/{taskId}";
            await PostAsync(qualityHttp!, $"{qualityTaskPath}/claim", new
            {
                organizationId = Organization, environmentId = EnvironmentId, actorPrincipalId = inspector,
                expectedVersion = taskVersion, idempotencyKey = "claim-inspection",
            });
            var reported = await PostAsync(qualityHttp!, $"{qualityTaskPath}/inspection-record", new
            {
                organizationId = Organization, environmentId = EnvironmentId, inspectorUserId = inspector,
                idempotencyKey = "quality-decision",
                dispositionReason = "acceptance inspection result",
                resultLines = new[]
                {
                    new
                    {
                        characteristicCode = "dimension", observedValue = measured.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        measuredValue = measured, result = resultLine, attachmentFileIds = Array.Empty<string>(),
                        defectQuantity = resultLine == "passed" ? (decimal?)null : 12m,
                    },
                },
            });
            Assert.Equal(disposition, reported.GetProperty("result").GetString());
            await WaitAsync("WMS projects the Quality decision on the bound receipt", async () =>
            {
                await using var scope = wms.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                var inbound = await db.InboundOrders.Include(x => x.Lines).SingleAsync();
                Assert.Equal(disposition, Assert.Single(inbound.Lines).ErpReceiptInspectionResult);
                Assert.Empty(await db.InventoryMovementRequests.ToArrayAsync());
                completion = completion with { expectedVersion = inbound.Version };
            });
        }
        await PostAsync(wmsHttp, $"{path}/complete", completion);

        await WaitAsync("Inventory durably posts 12 units at frozen cost and ERP consumes the WMS completion", async () =>
        {
            await using var erpScope = erp.Services.CreateAsyncScope();
            var erpDb = erpScope.ServiceProvider.GetRequiredService<ErpDb>();
            var receipt = Assert.Single(await erpDb.PurchaseReceipts.ToArrayAsync());
            Assert.Equal(orderNo, receipt.PurchaseOrderNo);
            Assert.Equal(1.25m, receipt.ExchangeRate);
            Assert.Single(await erpDb.ProcessedIntegrationEvents
                .Where(x => x.ConsumerName == "business-erp.wms-inbound-completed-purchase-receipt").ToArrayAsync());
            await using var wmsScope = wms.Services.CreateAsyncScope();
            var wmsDb = wmsScope.ServiceProvider.GetRequiredService<WmsDb>();
            var inbound = await wmsDb.InboundOrders.Include(x => x.Lines).SingleAsync();
            Assert.Equal(InboundOrderStatus.Completed, inbound.Status);
            Assert.Equal(receipt.PurchaseReceiptNo, inbound.ErpPurchaseReceiptNo);
            Assert.Equal(12m, Assert.Single(inbound.Lines).ReceivedQuantity);
            var request = Assert.Single(await wmsDb.InventoryMovementRequests.ToArrayAsync());
            Assert.Equal(2.5m, request.UnitCost);
            Assert.Equal(stockQuality, request.QualityStatus);
            Assert.False(string.IsNullOrWhiteSpace(request.InventoryMovementId));
            if (disposition == "rejected")
            {
                var returnRequest = Assert.Single(await wmsDb.SupplierReturnRequests.ToArrayAsync());
                var outbound = Assert.Single(await wmsDb.OutboundOrders.Include(x => x.Lines).ToArrayAsync());
                Assert.Equal(orderNo, returnRequest.InboundOrderNo);
                Assert.Equal(receipt.PurchaseReceiptNo, outbound.SourceDocumentId);
                Assert.Equal(returnRequest.SupplierReturnNo, outbound.OutboundOrderNo);
                var returnLine = Assert.Single(outbound.Lines);
                Assert.Equal("020", returnLine.LineNo);
                Assert.Equal(12m, returnLine.RequestedQuantity);
                Assert.Equal("blocked", returnLine.QualityStatus);
            }
            else
            {
                Assert.Empty(await wmsDb.SupplierReturnRequests.ToArrayAsync());
            }
            await using var inventoryScope = inventory.Services.CreateAsyncScope();
            var inventoryDb = inventoryScope.ServiceProvider.GetRequiredService<InventoryDb>();
            Assert.Single(await inventoryDb.StockMovements.ToArrayAsync());
            var ledger = Assert.Single(await inventoryDb.StockLedgers.ToArrayAsync());
            Assert.Equal(12m, ledger.OnHandQuantity);
            Assert.Equal(30m, ledger.InventoryValue);
            Assert.Equal(stockQuality, ledger.QualityStatus);
        });
        output.WriteLine($"PostgreSQL verified one ERP receipt, one WMS completion consumption and one Inventory movement. Ledger quantity 12, value 30, stock quality {stockQuality}.");
        if (disposition == "rejected")
        {
            string outboundId;
            string outboundNo;
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                var outbound = await db.OutboundOrders.SingleAsync();
                outbound.AssignWorkPool(poolCode, actor, outbound.Version);
                await db.SaveChangesAsync();
                outboundId = outbound.Id.ToString();
                outboundNo = outbound.OutboundOrderNo;
            }

            var outboundPath = $"/api/business/v1/wms/outbound-orders/{outboundId}";
            await PostAsync(wmsHttp, $"{outboundPath}/picking-tasks", new
            {
                lineNo = "020", fromLocationCode = Location, toLocationCode = "PACK",
                quantity = 12m, idempotencyKey = "pick-return",
            });
            await using (var scope = inventory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
                var reservation = Assert.Single(await db.StockReservations.ToArrayAsync());
                Assert.Equal(outboundNo, reservation.SourceDocumentId);
                Assert.Equal((productionDate, expiryDate), (reservation.ProductionDate, reservation.ExpiryDate));
                Assert.Equal(12m, (await db.StockLedgers.SingleAsync()).ReservedQuantity);
            }
            output.WriteLine($"Return reserved 12 blocked units at {productionDate:yyyy-MM-dd}/{expiryDate:yyyy-MM-dd}.");

            string taskPath;
            long taskVersion;
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                var task = await db.WarehouseTasks.SingleAsync(x => x.SourceOrderNo == outboundNo);
                taskPath = $"/api/business/v1/wms/picking-tasks/{task.Id}";
                taskVersion = task.Version;
            }
            var pick = new
            {
                organizationId = Organization, environmentId = EnvironmentId, actorPrincipalId = actor,
                authorizedSiteCodes = new[] { Site }, scopeKind = "self", scopeId = actor,
                expectedVersion = taskVersion, idempotencyKey = "start-return-pick", executedQuantity = 12m,
            };
            var started = await PostAsync(wmsHttp, $"{taskPath}/start", pick);
            await PostAsync(wmsHttp, $"{taskPath}/complete", pick with
            {
                expectedVersion = started.GetProperty("version").GetInt64(), idempotencyKey = "complete-return-pick",
            });
            long outboundVersion;
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                outboundVersion = (await db.OutboundOrders.SingleAsync()).Version;
            }
            var ship = new
            {
                organizationId = Organization, environmentId = EnvironmentId, actorPrincipalId = actor,
                authorizedSiteCodes = new[] { Site }, scopeKind = "self", scopeId = actor,
                expectedVersion = outboundVersion, idempotencyKey = "ship-return", passed = true,
            };
            var shipped = await PostAsync(wmsHttp, $"{outboundPath}/complete", ship);
            var shipmentReplay = await PostAsync(wmsHttp, $"{outboundPath}/complete", ship);
            Assert.Equal(shipped.GetProperty("requestId").GetRawText(), shipmentReplay.GetProperty("requestId").GetRawText());

            await WaitAsync("Supplier return decrements the original blocked stock and posts one GR/IR reversal", AssertReturnAsync);

            var completionTopic = $"{version}.{nameof(WmsIntegrationEvent)}";
            JsonElement completionEnvelope;
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
                var serialized = await db.Database.SqlQuery<string>($$"""
                    SELECT ("Content"::jsonb -> 'Value')::text AS "Value"
                    FROM cap.published
                    WHERE "Name" = {{completionTopic}}
                      AND "Content"::jsonb #>> '{Value,EventType}' = {{WmsIntegrationEventTypes.OutboundOrderCompleted}}
                    """).SingleAsync();
                completionEnvelope = JsonSerializer.Deserialize<JsonElement>(serialized);
            }

            var eventId = completionEnvelope.GetProperty("EventId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(eventId));
            const string replayHeader = "warehouse-receipt-acceptance-replay";
            await using (var scope = wms.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(
                    nameof(WmsIntegrationEvent), completionEnvelope, new Dictionary<string, string?> { [replayHeader] = "true" });
            }

            var returnGroup = $"{WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName}.{version}";
            await WaitAsync("ERP CAP acknowledges the original and republished WMS completion", async () =>
            {
                await using var scope = erp.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ErpDb>();
                var replayFlags = await db.Database.SqlQuery<string>($$"""
                    SELECT COALESCE("Content"::jsonb -> 'Headers' ->> {{replayHeader}}, 'false') AS "Value"
                    FROM cap.received
                    WHERE "Group" = {{returnGroup}}
                      AND "Version" = {{version}}
                      AND "Name" = {{completionTopic}}
                      AND "StatusName" = 'Succeeded'
                      AND "Content"::jsonb #>> '{Value,EventId}' = {{eventId}}
                    """).ToArrayAsync();
                Assert.Equal(new[] { "false", "true" }, replayFlags.Order(StringComparer.Ordinal));
                Assert.False(await db.Set<IntegrationEventDeadLetter>().AnyAsync(x =>
                    x.ConsumerName == WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn.ConsumerName
                    && x.EventId == eventId));
            });
            await AssertReturnAsync();
            output.WriteLine("CAP delivered both copies of the WMS completion; ERP and Inventory retained one return posting.");

            async Task AssertReturnAsync()
            {
                await using var inventoryScope = inventory.Services.CreateAsyncScope();
                var inventoryDb = inventoryScope.ServiceProvider.GetRequiredService<InventoryDb>();
                Assert.Equal(2, await inventoryDb.StockMovements.CountAsync());
                var balance = await inventoryDb.StockLedgers.SingleAsync();
                Assert.Equal((productionDate, expiryDate), (balance.ProductionDate, balance.ExpiryDate));
                Assert.Equal((0m, 0m, 0m), (balance.OnHandQuantity, balance.ReservedQuantity, balance.InventoryValue));

                await using var wmsScope = wms.Services.CreateAsyncScope();
                var wmsDb = wmsScope.ServiceProvider.GetRequiredService<WmsDb>();
                Assert.Equal(OutboundOrderStatus.Completed, (await wmsDb.OutboundOrders.SingleAsync()).Status);

                await using var erpScope = erp.Services.CreateAsyncScope();
                var erpDb = erpScope.ServiceProvider.GetRequiredService<ErpDb>();
                var receipt = await erpDb.PurchaseReceipts.SingleAsync();
                var returned = Assert.Single(await erpDb.PurchaseReturns.Include(x => x.Lines).ToArrayAsync());
                Assert.Equal(receipt.PurchaseReceiptNo, returned.PurchaseReceiptNo);
                Assert.Equal(24m, returned.GrIrReversalAmount);
                var vouchers = await erpDb.JournalVouchers.Include(x => x.Lines).ToArrayAsync();
                Assert.Equal(2, vouchers.Length);
                var accrual = Assert.Single(vouchers, x => x.SourceNo == receipt.PurchaseReceiptNo);
                var reversal = Assert.Single(vouchers, x => x.SourceNo == returned.PurchaseReturnNo);
                Assert.Equal(30m, Assert.Single(accrual.Lines, x => x.AccountCode == "GR-IR").LocalCreditAmount);
                Assert.Equal(30m, Assert.Single(reversal.Lines, x => x.AccountCode == "GR-IR").LocalDebitAmount);
            }
        }
        if (quality is not null) return;

        const string cancelledOrder = "PO-CANCEL";
        await ReleaseAsync(cancelledOrder);
        await WaitAsync("WMS persists the second order before ERP cancellation", async () =>
        {
            await using var scope = wms.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
            Assert.True(await db.InboundOrders.AnyAsync(x => x.InboundOrderNo == cancelledOrder));
        });
        await PostAsync(erpHttp, $"/api/business/v1/erp/purchase-orders/{cancelledOrder}/cancel", new
        {
            organizationId = Organization, environmentId = EnvironmentId, reason = "supplier cancellation",
        });
        await WaitAsync("WMS closes only the cancelled order with zero actual quantity and no movement", async () =>
        {
            await using var scope = wms.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WmsDb>();
            var cancelled = await db.InboundOrders.Include(x => x.Lines).SingleAsync(x => x.InboundOrderNo == cancelledOrder);
            Assert.Equal(InboundOrderStatus.Cancelled, cancelled.Status);
            Assert.Equal(0m, Assert.Single(cancelled.Lines).ReceivedQuantity);
            Assert.Equal(InboundOrderStatus.Completed, (await db.InboundOrders.SingleAsync(x => x.Id == inboundOrderId)).Status);
            Assert.Single(await db.InventoryMovementRequests.ToArrayAsync());
        });
        output.WriteLine("Redis/CAP cancellation projected a terminal WMS order with zero actual quantity and no additional Inventory request.");

        async Task ReleaseAsync(string purchaseOrderNo)
        {
            await using var scope = erp.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ErpDb>();
            var approval = $"approval-{purchaseOrderNo}";
            var order = PurchaseOrder.Create(Organization, EnvironmentId, purchaseOrderNo, "SUP-01", Site, "CNY",
                [new PurchaseOrderLineDraft("020", "SKU-01", "ea", 12m, 2m, new DateOnly(2026, 12, 1))],
                PurchaseReceiptInventoryPostingRoute.Wms);
            order.MarkApprovalRequested(approval);
            db.PurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<ICapPublisher>().PublishAsync(nameof(ApprovalCompletedIntegrationEvent),
                new ApprovalCompletedIntegrationEvent(approval, ApprovalIntegrationEventTypes.ApprovalApproved, 1,
                    new DateTimeOffset(2026, 11, 27, 8, 0, 0, TimeSpan.Zero),
                    ApprovalIntegrationEventSources.BusinessApproval, approval, approval,
                    Organization, EnvironmentId, "user:buyer", approval,
                    new ApprovalCompletedPayload(approval, ApprovalResults.Approved, "user", "buyer", null, null,
                        new ApprovalDocumentReferencePayload(ApprovalSourceServices.BusinessErp, ApprovalDocumentTypes.PurchaseOrder,
                            purchaseOrderNo, null))));
        }

        Task WaitAsync(string condition, Func<Task> assertion) => Eventually.AssertAsync(condition, _ => assertion(),
            new EventuallyOptions(TimeSpan.FromSeconds(45), TimeSpan.FromMilliseconds(250), [postgres, redis])).AsTask();
    }

    private static WebApplicationFactory<T> Host<T>(string postgres, string redis, string version,
        Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null) where T : class
    {
        settings["Persistence:Provider"] = "PostgreSQL";
        settings["Persistence:AutoMigrate"] = "true";
        settings["ConnectionStrings:PostgreSQL"] = postgres;
        settings["Messaging:Provider"] = "Redis";
        settings["Messaging:Redis:ConnectionString"] = redis;
        settings["Cap:Version"] = version;
        settings["Cap:TopicNamePrefix"] = version;
        settings["InternalService:BearerToken"] = Token;
        return new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseDefaultServiceProvider((_, options) =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
                    options.SerializerOptions.AddNetCorePalJsonConverters());
                services.AddFastEndpoints(options =>
                {
                    options.Assemblies = [typeof(T).Assembly];
                    options.DisableAutoDiscovery = true;
                    options.IncludeAbstractValidators = true;
                });
                configure?.Invoke(services);
            });
        });
    }

    private static HttpClient Client<T>(WebApplicationFactory<T> factory) where T : class
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private async Task<JsonElement> PostAsync(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} returned {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), body);
        output.WriteLine($"{path} returned HTTP {(int)response.StatusCode}.");
        return json.RootElement.GetProperty("data").Clone();
    }

    private sealed class CapRedisScope(ConnectionMultiplexer connection, string version) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var ownedConnection = connection;
            var database = ownedConnection.GetDatabase();
            foreach (var endpoint in ownedConnection.GetEndPoints())
            {
                var server = ownedConnection.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                await foreach (var key in server.KeysAsync(database.Database, $"{version}.*"))
                {
                    await database.KeyDeleteAsync(key);
                }
                await foreach (var key in server.KeysAsync(database.Database, $"{version}.*"))
                {
                    throw new InvalidOperationException($"CAP Redis namespace still contains {key} after cleanup.");
                }
            }
        }
    }
}

internal sealed class WarehousePurchaseReceiptBrokerFactAttribute : FactAttribute
{
    public WarehousePurchaseReceiptBrokerFactAttribute()
    {
        if (new[] { "NERV_IIP_TEST_POSTGRES", "NERV_IIP_TEST_REDIS" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the warehouse receipt broker acceptance.";
    }
}
