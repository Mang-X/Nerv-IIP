using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialReadinessLiveCoverageTests
{
    [Fact]
    public async Task Query_uses_live_inventory_and_erp_eta_without_rewriting_frozen_requirement()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var capturedAtUtc = DateTimeOffset.Parse("2026-09-20T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-ETA-001", "FG-001", "PV-001", 1m, 10, capturedAtUtc));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001",
            "env-dev",
            "WO-ETA-001",
            "OP-10",
            "MAT-001",
            null,
            requiredQuantity: 10m,
            uomCode: "PCS",
            availableQuantity: 9m,
            stagedQuantity: 0m,
            sourceSystem: "product-engineering-http:PV-001:MBOM-001:A",
            sourceSnapshotId: "MBOM-001:A:MAT-001",
            capturedAtUtc: capturedAtUtc,
            substituteMaterialIds: ["MAT-ALT-001"]));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var expectedAtUtc = DateTimeOffset.Parse("2026-09-25T00:00:00Z");
        var coverage = new StubLiveCoverageProvider(new MesMaterialReadinessLiveCoverageResult(
            InventoryAvailable: true,
            ErpAvailable: true,
            [new MesMaterialReadinessLiveCoverageItem(
                "MAT-001",
                null,
                "PCS",
                2m,
                expectedAtUtc,
                MesMaterialAvailabilitySources.ErpPurchaseOrderPromisedDate)]));

        var response = await new GetMaterialReadinessQueryHandler(dbContext, coverage).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-ETA-001"),
            CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal(10m, row.RequiredQuantity);
        Assert.Equal("PCS", row.UomCode);
        Assert.Equal(2m, row.AvailableQuantity);
        Assert.Equal(8m, row.ShortageQuantity);
        Assert.Equal(expectedAtUtc, row.ExpectedAvailableAtUtc);
        Assert.Equal(MesMaterialAvailabilitySources.ErpPurchaseOrderPromisedDate, row.ExpectedAvailabilitySource);
        Assert.Equal(["MAT-ALT-001"], row.SubstituteMaterialIds);

        var frozen = await dbContext.MaterialRequirements.AsNoTracking().SingleAsync();
        Assert.Equal(10m, frozen.RequiredQuantity);
        Assert.Equal("PCS", frozen.UomCode);
        Assert.Equal(9m, frozen.AvailableQuantity);
        Assert.Equal("product-engineering-http:PV-001:MBOM-001:A", frozen.SourceSystem);
        Assert.Equal("MBOM-001:A:MAT-001", frozen.SourceSnapshotId);
        Assert.Equal(capturedAtUtc, frozen.CapturedAtUtc);
        Assert.Equal(["MAT-ALT-001"], frozen.GetSubstituteMaterialIds());
        Assert.False(dbContext.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Query_keeps_shortage_and_never_uses_wms_prepared_time_when_live_sources_are_unavailable()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var capturedAtUtc = DateTimeOffset.Parse("2026-09-20T08:00:00Z");
        var preparedAtUtc = DateTimeOffset.Parse("2026-09-21T06:30:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-ETA-002", "FG-002", "PV-002", 1m, 10, capturedAtUtc));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-ETA-002", "OP-10", "MAT-002", null,
            requiredQuantity: 10m,
            uomCode: "PCS",
            availableQuantity: 10m,
            stagedQuantity: 0m,
            sourceSystem: "product-engineering-http:PV-002:MBOM-002:A",
            sourceSnapshotId: "MBOM-002:A:MAT-002",
            capturedAtUtc: capturedAtUtc,
            substituteMaterialIds: []));
        var issue = MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-ETA-002", "WO-ETA-002", "OP-10", "MAT-002", "PCS", 10m, capturedAtUtc);
        issue.LinkWarehouseOutbound("WMS-OUT-002", "PICK-002", preparedAtUtc);
        dbContext.MaterialIssueRequests.Add(issue);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var coverage = new StubLiveCoverageProvider(new MesMaterialReadinessLiveCoverageResult(
            InventoryAvailable: false,
            ErpAvailable: false,
            []));

        var response = await new GetMaterialReadinessQueryHandler(dbContext, coverage).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-ETA-002"),
            CancellationToken.None);

        Assert.Equal("Blocked", response.ReadinessStatus);
        var row = Assert.Single(response.Items);
        Assert.Equal(0m, row.AvailableQuantity);
        Assert.Equal(10m, row.ShortageQuantity);
        Assert.Equal(MesMaterialShortageStages.AwaitingDelivery, row.ShortageStage);
        Assert.Null(row.ExpectedAvailableAtUtc);
        Assert.Null(row.ExpectedAvailabilitySource);
    }

    [Fact]
    public async Task Http_provider_sums_primary_and_substitute_inventory_then_batches_the_real_shortage_to_erp()
    {
        var inventory = new StubMaterialAvailabilityReader(request =>
            request.MaterialIds.Contains("MAT-ALT-003", StringComparer.Ordinal)
                ? new MesMaterialAvailabilityReadResult(true, 4m)
                : new MesMaterialAvailabilityReadResult(true, 0m));
        using var erpHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 0,
                data = new
                {
                    items = new[]
                    {
                        new
                        {
                            skuCode = "MAT-003",
                            uomCode = "PCS",
                            shortageQuantity = 5m,
                            openPurchaseQuantity = 5m,
                            expectedAvailableDate = "2026-09-28",
                        },
                    },
                },
            }),
        });
        using var erpHttpClient = new HttpClient(erpHandler) { BaseAddress = new Uri("http://erp.test") };
        var provider = new HttpMesMaterialReadinessLiveCoverageProvider(
            inventory,
            new MesErpHttpClient(erpHttpClient),
            new TestInternalServiceTokenProvider("internal-token"),
            NullLogger<HttpMesMaterialReadinessLiveCoverageProvider>.Instance);

        var result = await provider.ResolveAsync(
            new MesMaterialReadinessLiveCoverageRequest(
                "org-001",
                "env-dev",
                [new MesMaterialReadinessLiveCoverageRequestItem(
                    "MAT-003", null, "PCS", 10m, 9m, 1m, 0m, ["MAT-ALT-003"])]),
            CancellationToken.None);

        Assert.True(result.InventoryAvailable);
        Assert.True(result.ErpAvailable);
        Assert.Equal(["MAT-003", "MAT-ALT-003"], inventory.LastRequest!.MaterialIds);
        var row = Assert.Single(result.Items);
        Assert.Equal(4m, row.AvailableQuantity);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T00:00:00Z"), row.ExpectedAvailableAtUtc);
        Assert.Equal(MesMaterialAvailabilitySources.ErpPurchaseOrderPromisedDate, row.ExpectedAvailabilitySource);
        Assert.Equal(HttpMethod.Post, erpHandler.Method);
        Assert.Equal("/api/business/v1/erp/material-supply-etas/resolve", erpHandler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", erpHandler.AuthorizationScheme);
        Assert.Equal("internal-token", erpHandler.AuthorizationParameter);
        using var body = JsonDocument.Parse(erpHandler.RequestBody!);
        Assert.Equal("org-001", body.RootElement.GetProperty("organizationId").GetString());
        Assert.Equal("env-dev", body.RootElement.GetProperty("environmentId").GetString());
        var requested = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("MAT-003", requested.GetProperty("skuCode").GetString());
        Assert.Equal("PCS", requested.GetProperty("uomCode").GetString());
        Assert.Equal(5m, requested.GetProperty("shortageQuantity").GetDecimal());
    }

    [Fact]
    public async Task Query_keeps_same_material_in_different_uoms_as_distinct_readiness_rows()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var capturedAtUtc = DateTimeOffset.Parse("2026-09-21T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-UOM-001", "FG-001", "PV-001", 1m, 10, capturedAtUtc));
        dbContext.MaterialRequirements.AddRange(
            MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-UOM-001", "OP-10", "MAT-SAME", null,
                10m, 8m, 0m, "MBOM", "MBOM:PCS", capturedAtUtc, [], "PCS"),
            MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-UOM-001", "OP-20", "MAT-SAME", null,
                2m, 0m, 0m, "MBOM", "MBOM:BOX", capturedAtUtc, [], "BOX"));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-UOM-001", "WO-UOM-001", "OP-10", "MAT-SAME", "PCS", 2m, capturedAtUtc));
        await dbContext.SaveChangesAsync();
        var coverage = new StubLiveCoverageProvider(new MesMaterialReadinessLiveCoverageResult(
            true,
            true,
            [
                new MesMaterialReadinessLiveCoverageItem("MAT-SAME", null, "PCS", 8m, null, null),
                new MesMaterialReadinessLiveCoverageItem("MAT-SAME", null, "BOX", 0m, DateTimeOffset.Parse("2026-09-30T00:00:00Z"), MesMaterialAvailabilitySources.ErpPurchaseOrderPromisedDate),
            ]));

        var response = await new GetMaterialReadinessQueryHandler(dbContext, coverage).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-UOM-001"),
            CancellationToken.None);

        Assert.Collection(
            response.Items.OrderBy(x => x.UomCode, StringComparer.Ordinal),
            box =>
            {
                Assert.Equal("BOX", box.UomCode);
                Assert.Equal(2m, box.RequiredQuantity);
                Assert.Equal(2m, box.ShortageQuantity);
            },
            pcs =>
            {
                Assert.Equal("PCS", pcs.UomCode);
                Assert.Equal(10m, pcs.RequiredQuantity);
                Assert.Equal(8m, pcs.AvailableQuantity);
                Assert.Equal(2m, pcs.RequestedQuantity);
            });
    }

    [Fact]
    public async Task Http_provider_keeps_eta_empty_when_open_purchase_quantity_cannot_cover_the_shortage()
    {
        var inventory = new StubMaterialAvailabilityReader(_ => new MesMaterialAvailabilityReadResult(true, 2m));
        using var erpHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 0,
                data = new
                {
                    items = new[]
                    {
                        new
                        {
                            skuCode = "MAT-005",
                            uomCode = "PCS",
                            shortageQuantity = 8m,
                            openPurchaseQuantity = 7m,
                            expectedAvailableDate = (string?)null,
                        },
                    },
                },
            }),
        });
        var provider = CreateHttpProvider(inventory, erpHandler);

        var result = await provider.ResolveAsync(
            new MesMaterialReadinessLiveCoverageRequest(
                "org-001",
                "env-dev",
                [new MesMaterialReadinessLiveCoverageRequestItem("MAT-005", null, "PCS", 10m, 0m, 0m, 0m, [])]),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Null(row.ExpectedAvailableAtUtc);
        Assert.Null(row.ExpectedAvailabilitySource);
    }

    [Fact]
    public async Task Http_provider_does_not_claim_an_eta_when_inventory_or_erp_is_unavailable()
    {
        var unavailableInventory = new StubMaterialAvailabilityReader(_ =>
            new MesMaterialAvailabilityReadResult(false, 0m));
        using var unusedErpHandler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("ERP must not be called when Inventory is unavailable."));
        var inventoryUnavailableProvider = CreateHttpProvider(unavailableInventory, unusedErpHandler);
        var request = new MesMaterialReadinessLiveCoverageRequest(
            "org-001",
            "env-dev",
            [new MesMaterialReadinessLiveCoverageRequestItem("MAT-004", null, "PCS", 10m, 8m, 0m, 0m, [])]);

        var inventoryUnavailable = await inventoryUnavailableProvider.ResolveAsync(request, CancellationToken.None);

        Assert.False(inventoryUnavailable.InventoryAvailable);
        Assert.False(inventoryUnavailable.ErpAvailable);
        Assert.Null(Assert.Single(inventoryUnavailable.Items).ExpectedAvailableAtUtc);
        Assert.Null(unusedErpHandler.Method);

        var availableInventory = new StubMaterialAvailabilityReader(_ =>
            new MesMaterialAvailabilityReadResult(true, 2m));
        using var failedErpHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var erpUnavailableProvider = CreateHttpProvider(availableInventory, failedErpHandler);

        var erpUnavailable = await erpUnavailableProvider.ResolveAsync(request, CancellationToken.None);

        Assert.True(erpUnavailable.InventoryAvailable);
        Assert.False(erpUnavailable.ErpAvailable);
        var current = Assert.Single(erpUnavailable.Items);
        Assert.Equal(2m, current.AvailableQuantity);
        Assert.Null(current.ExpectedAvailableAtUtc);
        Assert.Null(current.ExpectedAvailabilitySource);
    }

    private static HttpMesMaterialReadinessLiveCoverageProvider CreateHttpProvider(
        IMesMaterialAvailabilityReader inventory,
        HttpMessageHandler erpHandler) => new(
            inventory,
            new MesErpHttpClient(new HttpClient(erpHandler) { BaseAddress = new Uri("http://erp.test") }),
            new TestInternalServiceTokenProvider("internal-token"),
            NullLogger<HttpMesMaterialReadinessLiveCoverageProvider>.Instance);

    private sealed class StubLiveCoverageProvider(MesMaterialReadinessLiveCoverageResult result)
        : IMesMaterialReadinessLiveCoverageProvider
    {
        public Task<MesMaterialReadinessLiveCoverageResult> ResolveAsync(
            MesMaterialReadinessLiveCoverageRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class StubMaterialAvailabilityReader(
        Func<MesMaterialAvailabilityReadRequest, MesMaterialAvailabilityReadResult> resolve)
        : IMesMaterialAvailabilityReader
    {
        public MesMaterialAvailabilityReadRequest? LastRequest { get; private set; }

        public Task<MesMaterialAvailabilityReadResult> ReadAsync(
            MesMaterialAvailabilityReadRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(resolve(request));
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}

internal sealed class FrozenMaterialReadinessLiveCoverageProvider : IMesMaterialReadinessLiveCoverageProvider
{
    public static readonly FrozenMaterialReadinessLiveCoverageProvider Instance = new();

    public Task<MesMaterialReadinessLiveCoverageResult> ResolveAsync(
        MesMaterialReadinessLiveCoverageRequest request,
        CancellationToken cancellationToken) => Task.FromResult(new MesMaterialReadinessLiveCoverageResult(
            InventoryAvailable: true,
            ErpAvailable: true,
            request.Items.Select(item => new MesMaterialReadinessLiveCoverageItem(
                item.MaterialId,
                item.MaterialLotId,
                item.UomCode,
                item.FrozenAvailableQuantity,
                null,
                null)).ToArray()));
}
