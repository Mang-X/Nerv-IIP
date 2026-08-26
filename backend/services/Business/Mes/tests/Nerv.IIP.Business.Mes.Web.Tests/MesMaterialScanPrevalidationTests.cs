using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesMaterialScanPrevalidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-26T08:00:00Z");

    [Fact]
    public async Task Accepted_primary_material_requires_completed_line_side_lot_and_inventory_authority()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        await db.SaveChangesAsync();
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("material-scan-accepted", response.ReasonCode);
        Assert.Equal("MIR-001", response.MaterialIssueRequestId);
        Assert.Equal("WO-001", response.WorkOrderId);
        Assert.Equal("OP-10", response.OperationTaskId);
        Assert.Equal("MAT-PRIMARY", response.MaterialId);
        Assert.Equal("primary", response.MaterialQualification);
        Assert.Equal("LOT-001", response.MaterialLotId);
        Assert.Equal("LINE-01", inventory.LastRequest?.LocationCode);
        Assert.Equal(new DateOnly(2026, 8, 26), inventory.LastRequest?.AsOfDate);
    }

    [Fact]
    public async Task Frozen_mbom_substitute_is_accepted_without_inventing_an_inventory_batch_id()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-SUB", includeRequirement: false, completeReceipt: true);
        db.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-001", "OP-10", "MAT-PRIMARY", null,
            5m, 5m, 0m, "product-engineering", "snap-001", Now, ["MAT-SUB"]));
        await db.SaveChangesAsync();
        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, false, true))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("material-scan-accepted", response.ReasonCode);
        Assert.Equal("MIR-001", response.MaterialIssueRequestId);
        Assert.Equal("WO-001", response.WorkOrderId);
        Assert.Equal("OP-10", response.OperationTaskId);
        Assert.Equal("MAT-SUB", response.MaterialId);
        Assert.Equal("substitute", response.MaterialQualification);
    }

    [Fact]
    public async Task Incomplete_line_side_receipt_rejects_before_external_sources_are_called()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: false);
        await db.SaveChangesAsync();
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("line-side-receipt-incomplete", response.ReasonCode);
        Assert.Equal(0, inventory.CallCount);
    }

    [Fact]
    public async Task Partially_received_issue_rejects_before_external_sources_are_called()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true, receivedQuantity: 1m);
        await db.SaveChangesAsync();
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("line-side-receipt-incomplete", response.ReasonCode);
        Assert.Equal(0, inventory.CallCount);
    }

    [Fact]
    public async Task Inventory_expiry_is_a_distinct_business_rejection()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        await db.SaveChangesAsync();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, true, false))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-lot-expired", response.ReasonCode);
    }

    [Fact]
    public async Task Inventory_movement_block_is_a_distinct_business_rejection()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        await db.SaveChangesAsync();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, false, false))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-lot-blocked", response.ReasonCode);
    }

    [Fact]
    public async Task Missing_material_issue_is_a_distinct_business_rejection()
    {
        await using var db = CreateDbContext();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, false, true))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-issue-request-not-found", response.ReasonCode);
    }

    [Theory]
    [InlineData("WO-OTHER", "OP-10", "work-order-mismatch")]
    [InlineData("WO-001", "OP-OTHER", "operation-task-mismatch")]
    public async Task Issue_linkage_mismatch_is_a_distinct_business_rejection(
        string workOrderId,
        string operationTaskId,
        string expectedReason)
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        await db.SaveChangesAsync();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, false, true))).Handle(
                new PrevalidateMaterialScanQuery("org-001", "env-dev", "MIR-001", workOrderId, operationTaskId),
                CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal(expectedReason, response.ReasonCode);
    }

    [Theory]
    [InlineData("workOrder")]
    [InlineData("operationTask")]
    public async Task Missing_mes_context_is_a_distinct_business_rejection(string missingFact)
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        if (missingFact == "workOrder")
        {
            db.WorkOrders.Remove(Assert.Single(db.WorkOrders.Local));
        }
        else
        {
            db.OperationTasks.Remove(Assert.Single(db.OperationTasks.Local));
        }
        await db.SaveChangesAsync();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(true, false, true))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("mes-context-not-found", response.ReasonCode);
    }

    [Fact]
    public async Task Missing_inventory_lot_is_a_distinct_business_rejection()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        await db.SaveChangesAsync();

        var response = await CreateHandler(
            db,
            new StubAvailabilityProvider(new(false, false, false))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-lot-not-found", response.ReasonCode);
    }

    [Fact]
    public async Task Requirement_from_another_operation_does_not_qualify_as_current_operation_primary()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-OTHER-OP", includeRequirement: false, completeReceipt: true);
        db.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-001", "OP-20", "MAT-OTHER-OP", null,
            5m, 5m, 0m, "product-engineering", "snap-other-op", Now, []));
        await db.SaveChangesAsync();
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(
            db,
            inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-not-required", response.ReasonCode);
        Assert.Equal(0, inventory.CallCount);
    }

    [Fact]
    public async Task Inventory_provider_forwards_exact_lot_scope_without_inventory_batch_id()
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 200,
                data = new
                {
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "MAT-001",
                    uomCode = "PCS",
                    siteCode = "SITE-01",
                    locationCode = "LINE-01",
                    lotNo = "LOT-001",
                    onHandQuantity = 5m,
                    items = new[] { new { locationCode = "LINE-01", lotNo = "LOT-001", isExpired = false, movementAllowed = true, onHandQuantity = 5m } },
                },
            }),
        });
        var provider = CreateHttpProvider(inventoryHandler);

        var result = await provider.GetAsync(
            new MesMaterialLotAvailabilityRequest(
                "org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 26)),
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.Contains("lotNo=LOT-001", inventoryHandler.LastRequestUri, StringComparison.Ordinal);
        Assert.Contains("locationCode=LINE-01", inventoryHandler.LastRequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain("inventoryBatchId", inventoryHandler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("corr-001", inventoryHandler.CorrelationId);
        Assert.Equal("internal-token", inventoryHandler.AuthorizationParameter);
    }

    [Fact]
    public async Task Inventory_provider_maps_malformed_success_response_to_source_unavailable()
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ malformed", System.Text.Encoding.UTF8, "application/json"),
        });
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":null}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":[{\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"isExpired\":false,\"onHandQuantity\":5}]}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":[{\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"isExpired\":false,\"movementAllowed\":true}]}")]
    public async Task Inventory_provider_fails_closed_when_success_json_omits_or_nulls_authoritative_fact(string dataJson)
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{dataJson}}}",
                System.Text.Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_provider_fails_closed_when_aggregate_on_hand_contradicts_line_facts()
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 200,
                data = new
                {
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "MAT-001",
                    uomCode = "PCS",
                    siteCode = "SITE-01",
                    locationCode = "LINE-01",
                    lotNo = "LOT-001",
                    onHandQuantity = 0m,
                    items = new[]
                    {
                        new { locationCode = "LINE-01", lotNo = "LOT-001", isExpired = false, movementAllowed = true, onHandQuantity = 5m },
                    },
                },
            }),
        });
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_provider_maps_503_to_source_unavailable_not_business_rejection()
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_provider_propagates_caller_cancellation_token()
    {
        var inventoryHandler = new CancellationRecordingHttpHandler();
        var provider = CreateHttpProvider(inventoryHandler);
        using var cancellation = new CancellationTokenSource();

        var pending = provider.GetAsync(AvailabilityRequest(), cancellation.Token);
        await inventoryHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(inventoryHandler.LastCancellationToken.CanBeCanceled);
    }

    [Theory]
    [InlineData("organizationId", "other-org")]
    [InlineData("environmentId", "env-other")]
    [InlineData("skuCode", "MAT-OTHER")]
    [InlineData("uomCode", "KG")]
    [InlineData("siteCode", "SITE-OTHER")]
    [InlineData("locationCode", "LINE-OTHER")]
    [InlineData("lotNo", "LOT-OTHER")]
    public async Task Inventory_provider_fails_closed_when_success_response_has_mismatched_scope(
        string field,
        string mismatchedValue)
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(InventoryEnvelopeWithScopeMutation(field, mismatchedValue)),
        });
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            new MesMaterialLotAvailabilityRequest(
                "org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 26)),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"locationCode\":null,\"lotNo\":\"LOT-001\",\"isExpired\":false,\"movementAllowed\":true,\"onHandQuantity\":5}")]
    [InlineData("{\"locationCode\":\"LINE-OTHER\",\"lotNo\":\"LOT-001\",\"isExpired\":false,\"movementAllowed\":true,\"onHandQuantity\":5}")]
    [InlineData("{\"locationCode\":\"LINE-01\",\"lotNo\":null,\"isExpired\":false,\"movementAllowed\":true,\"onHandQuantity\":5}")]
    [InlineData("{\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-OTHER\",\"isExpired\":false,\"movementAllowed\":true,\"onHandQuantity\":5}")]
    public async Task Inventory_provider_fails_closed_when_line_fact_is_null_or_outside_requested_scope(string lineJson)
    {
        var inventoryHandler = new RecordingHttpHandler(_ => JsonEnvelopeResponse(
            $"{{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":[{lineJson}]}}"));
        var provider = CreateHttpProvider(inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Http_provider_requires_service_token_and_correlation_context_dependencies()
    {
        var constructor = Assert.Single(typeof(HttpMesMaterialLotAvailabilityProvider).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(IMesIntegrationEventContextAccessor));
        Assert.Contains(parameters, parameter =>
            parameter.ParameterType == typeof(IInternalServiceTokenProvider) && !parameter.IsOptional);
    }

    private static PrevalidateMaterialScanQueryHandler CreateHandler(
        ApplicationDbContext db,
        IMesMaterialLotAvailabilityProvider inventory) =>
        new(db, inventory, new FakeTimeProvider(Now));

    private static PrevalidateMaterialScanQuery Request() =>
        new("org-001", "env-dev", "MIR-001", "WO-001", "OP-10");

    private static MesMaterialLotAvailabilityRequest AvailabilityRequest() =>
        new("org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 26));


    private static object InventoryEnvelopeWithScopeMutation(string field, string mismatchedValue)
    {
        var data = new Dictionary<string, object?>
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["skuCode"] = "MAT-001",
            ["uomCode"] = "PCS",
            ["siteCode"] = "SITE-01",
            ["locationCode"] = "LINE-01",
            ["lotNo"] = "LOT-001",
            ["onHandQuantity"] = 5m,
            ["items"] = new[]
            {
                new { locationCode = "LINE-01", lotNo = "LOT-001", isExpired = false, movementAllowed = true, onHandQuantity = 5m },
            },
        };
        data[field] = mismatchedValue;
        return new { success = true, message = "ok", code = 200, data };
    }

    private static HttpResponseMessage JsonEnvelopeResponse(string dataJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $"{{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{dataJson}}}",
            System.Text.Encoding.UTF8,
            "application/json"),
    };


    private static void SeedMesFacts(
        ApplicationDbContext db,
        string materialId,
        bool includeRequirement,
        bool completeReceipt,
        decimal? receivedQuantity = null)
    {
        db.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001", 10m, 1, Now.AddDays(1), "PCS"));
        db.OperationTasks.Add(OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-10", OperationTaskLifecycleStatus.Queued,
            10, "WC-01", [], Now, TimeSpan.FromHours(1), null, null));
        if (includeRequirement)
        {
            db.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-001", "OP-10", materialId, null,
                5m, 5m, 0m, "product-engineering", "snap-001", Now, []));
        }

        var issue = MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-001", "WO-001", "OP-10", materialId, "PCS", 5m, Now);
        if (completeReceipt)
        {
            issue.ConfirmAndPostLineSideReceipt(
                new MaterialTransferLocations(
                    "SITE-01", "WH-01", "SITE-01", "LINE-01",
                    [new MaterialTransferAllocation("SITE-01", "WH-01", "LOT-001", receivedQuantity ?? 5m)]),
                Now.AddMinutes(5), receivedQuantity ?? 5m, "LOT-001");
        }

        db.MaterialIssueRequests.Add(issue);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-material-scan-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static HttpMesMaterialLotAvailabilityProvider CreateHttpProvider(HttpMessageHandler inventoryHandler) =>
        new(
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new TestInternalServiceTokenProvider(),
            new StubMesIntegrationEventContextAccessor());

    private sealed class StubAvailabilityProvider(MesMaterialLotAvailabilityResult result)
        : IMesMaterialLotAvailabilityProvider
    {
        public int CallCount { get; private set; }
        public MesMaterialLotAvailabilityRequest? LastRequest { get; private set; }

        public Task<MesMaterialLotAvailabilityResult> GetAsync(
            MesMaterialLotAvailabilityRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string LastRequestUri { get; private set; } = string.Empty;
        public string CorrelationId { get; private set; } = string.Empty;
        public string AuthorizationParameter { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            CallCount++;
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            CorrelationId = request.Headers.GetValues("X-Correlation-Id").Single();
            AuthorizationParameter = request.Headers.Authorization?.Parameter ?? string.Empty;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "internal-token";
    }

    private sealed class StubMesIntegrationEventContextAccessor : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => new("corr-001", "cause-001");
    }

    private sealed class CancellationRecordingHttpHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastCancellationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            LastCancellationToken = cancellationToken;
            Started.TrySetResult();
            await PendingOperation.UntilCanceledAsync(cancellationToken);
            throw new InvalidOperationException("The cancellation test must not complete normally.");
        }
    }
}
