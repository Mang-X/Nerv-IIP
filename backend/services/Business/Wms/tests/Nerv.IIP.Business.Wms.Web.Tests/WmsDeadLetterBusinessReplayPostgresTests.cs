using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Web.Application.WcsAdapters;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Wms.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WmsDeadLetterBusinessReplayPostgresTests
{
    private static readonly DateTimeOffset OriginalTime = DateTimeOffset.Parse("2026-10-01T00:00:00Z");

    [WmsDeadLetterReplayPostgresFact]
    public async Task Delivery_normalized_document_replays_original_event_without_duplicates_on_postgres()
    {
        await using var f = await ReplayFixture.CreateAsync();
        var ev = new WmsOutboundOrderRequestedIntegrationEvent("space-delivery", WmsIntegrationEventTypes.OutboundOrderRequested, 1, OriginalTime,
            WmsIntegrationEventSources.BusinessErp, "corr", "cause", "org-replay", "env-replay", "system:erp", "space-delivery",
            new WmsOutboundOrderRequestedPayload(" DO-SPACE ", "SO", "CUSTOMER", "SITE", [new WmsOutboundOrderRequestedLine("1", "SKU", "kg", "LOC", "LOT", 5m)]));
        var letter = await f.AddLetterAsync(ev, WmsOutboundOrderRequestedIntegrationEventHandler.ConsumerName);
        using var response = await f.Client.PostAsync($"/api/business/v1/wms/dlq/{letter.Id}/replay", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var scope = f.Factory.Services.CreateScope();
        var document = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundOrders.SingleAsync();
        Assert.Equal("DO-SPACE", document.SourceDocumentId);
        Assert.Equal("DO-SPACE", document.OutboundOrderNo);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("data").GetProperty("status").GetString() == "replayed",
            $"Persisted OutboundOrderNo={document.OutboundOrderNo}; SourceDocumentId={document.SourceDocumentId}; HTTP={body}");
        await f.ReplayAsync(letter, "replayed");
        await f.ReplayAsync(letter, "replayed");
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OutboundOrders.CountAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Rma_normalized_document_replays_original_event_without_duplicates_on_postgres()
    {
        await using var f = await ReplayFixture.CreateAsync();
        var ev = new SalesReturnAuthorizedIntegrationEvent("space-rma", ErpIntegrationEventTypes.SalesReturnAuthorized, 1, OriginalTime,
            ErpIntegrationEventSources.BusinessErp, "corr", "cause", "org-replay", "env-replay", "system:erp", "space-rma",
            new SalesReturnAuthorizedPayload(" RMA-SPACE ", "SO", "CUSTOMER", "SITE", [new SalesReturnAuthorizedLinePayload("1", "SKU", "kg", 5m, "LOC", "LOT")]));
        var letter = await f.AddLetterAsync(ev, ErpSalesReturnAuthorizedIntegrationEventHandler.ConsumerName);
        using var response = await f.Client.PostAsync($"/api/business/v1/wms/dlq/{letter.Id}/replay", null);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var scope = f.Factory.Services.CreateScope();
        var document = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InboundOrders.SingleAsync();
        Assert.Equal("RMA-SPACE", document.SourceDocumentId);
        Assert.Equal("RMA-SPACE", document.InboundOrderNo);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("data").GetProperty("status").GetString() == "replayed",
            $"Persisted InboundOrderNo={document.InboundOrderNo}; SourceDocumentId={document.SourceDocumentId}; HTTP={body}");
        await f.ReplayAsync(letter, "replayed");
        await f.ReplayAsync(letter, "replayed");
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InboundOrders.CountAsync());
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Inventory_receipts_select_original_success_or_failure_and_batch_reads_persistent_results_on_postgres()
    {
        await using var f = await ReplayFixture.CreateAsync();
        await f.SeedAsync(db =>
        {
            db.InventoryMovementRequests.Add(Request("POST", "post-key"));
            db.InventoryMovementRequests.Add(Request("FAIL", "fail-key"));
        });
        var posted = new StockMovementPostedIntegrationEvent("posted-event", InventoryIntegrationEventTypes.StockMovementPosted, 1, OriginalTime,
            InventoryIntegrationEventSources.BusinessInventory, "original-correlation", "original-causation", "org-replay", "env-replay", "system:inventory", "posted-event",
            new StockMovementPostedPayload("MOV-POST", "inbound", "wms", "POST", "1", "post-key", "SKU", "kg", "SITE", "LOC", "LOT", null,
                "quality", "company", null, 5m, OriginalTime, null, null));
        var failed = new StockMovementPostingFailedIntegrationEvent("failed-event", InventoryIntegrationEventTypes.StockMovementPostingFailed, 1, OriginalTime,
            InventoryIntegrationEventSources.BusinessInventory, "original-correlation", "original-causation", "org-replay", "env-replay", "system:inventory", "failed-event",
            new StockMovementPostingFailedPayload("inbound", "wms", "FAIL", "1", "fail-key", "SKU", "kg", "SITE", "LOC", "LOT", null,
                "quality", "company", null, 5m, "original-rejection", "inventory rejected the original movement", OriginalTime));
        var successLetter = await f.AddLetterAsync(posted, StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted.ConsumerName);
        var failureLetter = await f.AddLetterAsync(failed, StockMovementPostingFailedIntegrationEventHandlerForMarkWmsRequestFailed.ConsumerName);
        var unsupported = await f.AddLetterAsync(posted, failureLetter.ConsumerName);
        using (var before = f.Factory.Services.CreateScope())
            unsupported = (await before.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(unsupported.Id, default))!;
        var missing = await f.AddLetterAsync(posted with { EventId = "missing", Payload = posted.Payload with { SourceDocumentId = "MISSING" } }, successLetter.ConsumerName);
        using var response = await f.Client.PostAsJsonAsync("/api/business/v1/wms/dlq/replay-batch", new { status = "pending" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(2, items.Count(x => x.GetProperty("status").GetString() == "replayed"));
        Assert.Single(items, x => x.GetProperty("status").GetString() == "failed");
        Assert.Single(items, x => x.GetProperty("status").GetString() == "noHandler");
        await f.ReplayAsync(successLetter, "replayed");
        await f.ReplayAsync(failureLetter, "replayed");
        using var read = f.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var requests = await db.InventoryMovementRequests.OrderBy(x => x.SourceDocumentId).ToArrayAsync();
        Assert.Equal(2, requests.Length);
        Assert.Equal(InventoryMovementRequestStatus.Failed, requests[0].Status);
        Assert.Equal("original-rejection", requests[0].FailureCode);
        Assert.Null(requests[0].InventoryMovementId);
        Assert.Equal(InventoryMovementRequestStatus.Posted, requests[1].Status);
        Assert.Equal("MOV-POST", requests[1].InventoryMovementId);
        Assert.Null(requests[1].FailureCode);
        Assert.Equal(unsupported, await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(unsupported.Id, default));
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed, (await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(missing.Id, default))!.Status);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Outbound_material_issue_return_and_expiration_restore_original_documents_without_duplicates_on_postgres()
    {
        var publisher = new LocalPublicationFixture();
        await using var f = await ReplayFixture.CreateAsync(services => services.AddSingleton<IIntegrationEventPublisher>(publisher));
        var outbound = new WmsOutboundOrderRequestedIntegrationEvent("outbound-event", WmsIntegrationEventTypes.OutboundOrderRequested, 1, OriginalTime,
            WmsIntegrationEventSources.BusinessErp, "corr", "cause", "org-replay", "env-replay", "system:erp", "outbound-event",
            new WmsOutboundOrderRequestedPayload("DO-REPLAY", "SO", "CUSTOMER", "SITE", [new WmsOutboundOrderRequestedLine("1", "SKU", "kg", "LOC", "LOT", 5m)]));
        var material = new MesMaterialIssueRequestedIntegrationEvent("material-event", MesIntegrationEventTypes.MaterialIssueRequested, 1, OriginalTime,
            MesIntegrationEventSources.BusinessMes, "corr", "cause", "org-replay", "env-replay", "system:mes", "material-event",
            new MesMaterialIssueRequestedPayload("MI-REQUEST", "WO", "OP", "SKU", "kg", 5m, OriginalTime, "SITE", "LOC", "LINE"));
        var returned = new SalesReturnAuthorizedIntegrationEvent("return-event", ErpIntegrationEventTypes.SalesReturnAuthorized, 1, OriginalTime,
            ErpIntegrationEventSources.BusinessErp, "corr", "cause", "org-replay", "env-replay", "system:erp", "return-event",
            new SalesReturnAuthorizedPayload("RMA-REPLAY", "SO", "CUSTOMER", "SITE", [new SalesReturnAuthorizedLinePayload("1", "SKU", "kg", 5m, "LOC", "LOT")]));
        await f.ReplayAsync(await f.AddLetterAsync(outbound, WmsOutboundOrderRequestedIntegrationEventHandler.ConsumerName), "replayed");
        await f.ReplayAsync(await f.AddLetterAsync(material, MesMaterialIssueRequestedIntegrationEventHandler.ConsumerName), "replayed");
        var returnLetter = await f.AddLetterAsync(returned, ErpSalesReturnAuthorizedIntegrationEventHandler.ConsumerName);
        await f.ReplayAsync(returnLetter, "replayed");
        await f.ReplayAsync(returnLetter, "replayed");
        await f.ReplayAsync(await f.AddLetterAsync(material, MesMaterialIssueRequestedIntegrationEventHandler.ConsumerName), "replayed");
        await f.ReplayAsync(await f.AddLetterAsync(outbound, WmsOutboundOrderRequestedIntegrationEventHandler.ConsumerName), "replayed");
        await f.SeedAsync(db =>
        {
            var order = OutboundOrder.Create("org-replay", "env-replay", "DO-EXPIRE", "sales-order", "SO", "SITE",
                [new OutboundOrderLineDraft("1", "SKU", "kg", 5m, "LOC", "LOT", null, "qualified", "company", null)]);
            db.OutboundOrders.Add(order);
            db.WarehouseTasks.Add(order.CreatePickingTask("PICK-EXPIRE", "1", "LOC", "PACK", 5m, "RES-EXPIRE"));
        });
        var expired = new InventoryReservationExpiredIntegrationEvent("expired-event", InventoryIntegrationEventTypes.StockReservationExpired, 1, OriginalTime,
            InventoryIntegrationEventSources.BusinessInventory, "corr", "cause", "org-replay", "env-replay", "system:inventory", "expired-event",
            new InventoryReservationExpiredPayload("RES-EXPIRE", "wms", "DO-EXPIRE", "1", 5m, OriginalTime));
        var expireLetter = await f.AddLetterAsync(expired, InventoryReservationExpiredIntegrationEventHandlerForCancelWmsPicking.ConsumerName);
        await f.ReplayAsync(expireLetter, "replayed");
        await f.ReplayAsync(expireLetter, "replayed");
        using var read = f.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(publisher.Events.OfType<WmsMaterialIssueOutboundPreparedIntegrationEvent>());
        Assert.Equal(3, await db.OutboundOrders.CountAsync());
        Assert.Single(await db.InboundOrders.ToArrayAsync());
        var issue = await db.OutboundOrders.Include(x => x.Lines).SingleAsync(x => x.SourceDocumentId == "MI-REQUEST");
        Assert.Equal("MI-MI-REQUEST", issue.OutboundOrderNo);
        Assert.Equal(5m, Assert.Single(issue.Lines).RequestedQuantity);
        Assert.Single(await db.WarehouseTasks.Where(x => x.SourceOrderNo == issue.OutboundOrderNo).ToArrayAsync());
        Assert.Equal(WarehouseTaskStatus.Cancelled, (await db.WarehouseTasks.SingleAsync(x => x.TaskNo == "PICK-EXPIRE")).Status);
        Assert.Null(Assert.Single((await db.OutboundOrders.Include(x => x.Lines).SingleAsync(x => x.OutboundOrderNo == "DO-EXPIRE")).Lines).InventoryReservationId);
    }

    [WmsDeadLetterReplayPostgresFact]
    public async Task Wcs_adapter_failure_rolls_back_inbox_before_http_retry_and_duplicate_replay_on_postgres()
    {
        var transport = new AdapterTransport();
        await using var f = await ReplayFixture.CreateAsync(services =>
            services.AddHttpClient<IWcsCancellationAdapter, HttpWcsCancellationAdapter>().ConfigurePrimaryHttpMessageHandler(() => transport),
            new Dictionary<string, string?> { ["Wcs:Adapters:agv:CancelEndpoint"] = "http://wcs-fixture/cancel" });
        var ev = new WmsIntegrationEvent("wcs-event", WmsIntegrationEventTypes.WcsTaskCancelled, 1, OriginalTime,
            WmsIntegrationEventSources.BusinessWms, "corr", "cause", "org-replay", "env-replay", "system:wms", "wcs-original-key",
            new WmsIntegrationPayload("WCS-ORIGINAL", null, null, null, null, null, null, "cancelled", null, "original-reason", AdapterType: "agv"));
        var letter = await f.AddLetterAsync(ev, WcsTaskCancelledIntegrationEventHandlerForAdapterCancellation.ConsumerName);
        await f.ReplayAsync(letter, "failed");
        using (var read = f.Factory.Services.CreateScope())
            Assert.Empty(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToArrayAsync());
        transport.Succeed = true;
        await f.ReplayAsync(letter, "replayed");
        await f.ReplayAsync(letter, "replayed");
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request =>
        {
            Assert.Equal("org-replay", request.OrganizationId);
            Assert.Equal("env-replay", request.EnvironmentId);
            Assert.Equal("WCS-ORIGINAL", request.ExternalTaskId);
            Assert.Equal(ev.IdempotencyKey, request.IdempotencyKey);
            Assert.Equal("original-reason", request.Reason);
        });
        using var scope = f.Factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToArrayAsync());
    }

    private sealed class LocalPublicationFixture : IIntegrationEventPublisher
    {
        public List<object> Events { get; } = [];
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Events.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class AdapterTransport : HttpMessageHandler
    {
        public bool Succeed { get; set; }
        public List<WcsCancellationHttpPayload> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((await request.Content!.ReadFromJsonAsync<WcsCancellationHttpPayload>(token))!);
            return new HttpResponseMessage(Succeed ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        }
    }

    private static InventoryMovementRequest Request(string document, string key) => InventoryMovementRequest.Create("org-replay", "env-replay", "inbound",
        document, "1", key, "SKU", "kg", "SITE", "LOC", "LOT", null, "quality", "company", null, 5m);
}
