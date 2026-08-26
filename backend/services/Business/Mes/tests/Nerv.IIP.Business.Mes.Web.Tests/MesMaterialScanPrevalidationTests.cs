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
        var qualification = new StubQualificationProvider(false);
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, qualification, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("primary", response.MaterialQualification);
        Assert.Equal("LOT-001", response.MaterialLotId);
        Assert.Equal("LINE-01", inventory.LastRequest?.LocationCode);
        Assert.Equal(new DateOnly(2026, 8, 26), inventory.LastRequest?.AsOfDate);
        Assert.Equal(0, qualification.CallCount);
    }

    [Fact]
    public async Task Frozen_mbom_substitute_is_accepted_without_inventing_an_inventory_batch_id()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-SUB", includeRequirement: false, completeReceipt: true);
        db.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-001", "OP-10", "MAT-PRIMARY", null,
            5m, 5m, 0m, "product-engineering", "snap-001", Now));
        await db.SaveChangesAsync();
        var qualification = new StubQualificationProvider(true);

        var response = await CreateHandler(
            db,
            qualification,
            new StubAvailabilityProvider(new(true, false, true))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("substitute", response.MaterialQualification);
        Assert.Equal("PV-001", qualification.LastRequest?.ProductionVersionId);
        Assert.Equal("MAT-SUB", qualification.LastRequest?.MaterialId);
        Assert.Equal(["MAT-PRIMARY"], qualification.LastRequest?.RequiredPrimaryMaterialIds);
    }

    [Fact]
    public async Task Incomplete_line_side_receipt_rejects_before_external_sources_are_called()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: false);
        await db.SaveChangesAsync();
        var qualification = new StubQualificationProvider(true);
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, qualification, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("line-side-receipt-incomplete", response.ReasonCode);
        Assert.Equal(0, qualification.CallCount);
        Assert.Equal(0, inventory.CallCount);
    }

    [Fact]
    public async Task Partially_received_issue_rejects_before_external_sources_are_called()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true, receivedQuantity: 1m);
        await db.SaveChangesAsync();
        var qualification = new StubQualificationProvider(true);
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(db, qualification, inventory).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("line-side-receipt-incomplete", response.ReasonCode);
        Assert.Equal(0, qualification.CallCount);
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
            new StubQualificationProvider(false),
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
            new StubQualificationProvider(false),
            new StubAvailabilityProvider(new(true, false, false))).Handle(Request(), CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Rejected, response.Decision);
        Assert.Equal("material-lot-blocked", response.ReasonCode);
    }

    [Fact]
    public async Task Requirement_from_another_operation_does_not_qualify_as_current_operation_primary()
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-OTHER-OP", includeRequirement: false, completeReceipt: true);
        db.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-001", "OP-20", "MAT-OTHER-OP", null,
            5m, 5m, 0m, "product-engineering", "snap-other-op", Now));
        await db.SaveChangesAsync();
        var inventory = new StubAvailabilityProvider(new(true, false, true));

        var response = await CreateHandler(
            db,
            new StubQualificationProvider(false),
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
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

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
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":[{\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"isExpired\":false,\"onHandQuantity\":5}]}")]
    [InlineData("{\"organizationId\":\"org-001\",\"environmentId\":\"env-dev\",\"skuCode\":\"MAT-001\",\"uomCode\":\"PCS\",\"siteCode\":\"SITE-01\",\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"onHandQuantity\":5,\"items\":[{\"locationCode\":\"LINE-01\",\"lotNo\":\"LOT-001\",\"isExpired\":false,\"movementAllowed\":true}]}")]
    public async Task Inventory_provider_fails_closed_when_success_json_omits_authoritative_fact(string dataJson)
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"success\":true,\"message\":\"ok\",\"code\":200,\"data\":{dataJson}}}",
                System.Text.Encoding.UTF8,
                "application/json"),
        });
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

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
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_provider_maps_503_to_source_unavailable_not_business_rejection()
    {
        var inventoryHandler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            AvailabilityRequest(),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_provider_propagates_caller_cancellation_token()
    {
        var inventoryHandler = new CancellationRecordingHttpHandler();
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);
        using var cancellation = new CancellationTokenSource();

        var pending = provider.GetAsync(AvailabilityRequest(), cancellation.Token);
        await inventoryHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(inventoryHandler.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Inventory_provider_fails_closed_when_success_response_has_mismatched_scope()
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
                    organizationId = "other-org",
                    environmentId = "env-dev",
                    skuCode = "MAT-001",
                    uomCode = "PCS",
                    siteCode = "SITE-01",
                    locationCode = "LINE-01",
                    lotNo = "LOT-001",
                    items = Array.Empty<object>(),
                },
            }),
        });
        var provider = CreateHttpProvider(new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)), inventoryHandler);

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.GetAsync(
            new MesMaterialLotAvailabilityRequest(
                "org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 26)),
            CancellationToken.None));

        Assert.StartsWith("MATERIAL_SCAN_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qualification_provider_uses_the_exact_frozen_version_and_published_mbom_substitutes()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    message = "ok",
                    code = 200,
                    data = new
                    {
                        total = 2,
                        items = new[]
                        {
                            new { productionVersionId = "PV-OLD", organizationId = "org-001", environmentId = "env-dev", skuCode = "FG-001", mbomVersionId = "MBOM-OLD:A" },
                            new { productionVersionId = "PV-001", organizationId = "org-001", environmentId = "env-dev", skuCode = "FG-001", mbomVersionId = "MBOM-001:B" },
                        },
                    },
                }),
            },
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    message = "ok",
                    code = 200,
                    data = new
                    {
                        bomCode = "MBOM-001",
                        revision = "B",
                        skuCode = "FG-001",
                        engineeringBomVersionId = "EBOM-001:A",
                        status = "published",
                        materialLines = new[]
                        {
                            new { skuCode = "MAT-PRIMARY", quantity = 1m, unitOfMeasureCode = "PCS", scrapRate = 0m, substituteSkuCodes = "MAT-SUB;MAT-ALT" },
                        },
                        recipeLines = Array.Empty<object>(),
                    },
                }),
            },
        ]);
        var engineeringHandler = new RecordingHttpHandler(_ => responses.Dequeue());
        var provider = CreateHttpProvider(engineeringHandler, new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)));

        var result = await provider.IsFrozenSubstituteAsync(
            new MesMaterialQualificationRequest("org-001", "env-dev", "FG-001", "PV-001", ["MAT-PRIMARY"], "MAT-SUB"),
            CancellationToken.None);

        Assert.True(result);
        Assert.Contains("/manufacturing-boms/MBOM-001/B", engineeringHandler.LastRequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qualification_provider_rejects_substitute_from_unrelated_mbom_primary_line()
    {
        var responses = FrozenVersionAndBomResponses(
            new { skuCode = "MAT-OTHER", quantity = 1m, unitOfMeasureCode = "PCS", scrapRate = 0m, substituteSkuCodes = "MAT-SUB" });
        var provider = CreateHttpProvider(
            new RecordingHttpHandler(_ => responses.Dequeue()),
            new RecordingHttpHandler(_ => new(HttpStatusCode.NotFound)));

        var result = await provider.IsFrozenSubstituteAsync(
            new MesMaterialQualificationRequest("org-001", "env-dev", "FG-001", "PV-001", ["MAT-PRIMARY"], "MAT-SUB"),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void Http_provider_requires_service_token_and_correlation_context_dependencies()
    {
        var constructor = Assert.Single(typeof(HttpMesMaterialPrevalidationProvider).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.Contains(parameters, parameter => parameter.ParameterType == typeof(IMesIntegrationEventContextAccessor));
        Assert.Contains(parameters, parameter =>
            parameter.ParameterType == typeof(IInternalServiceTokenProvider) && !parameter.IsOptional);
    }

    private static PrevalidateMaterialScanQueryHandler CreateHandler(
        ApplicationDbContext db,
        IMesMaterialQualificationProvider qualification,
        IMesMaterialLotAvailabilityProvider inventory) =>
        new(db, qualification, inventory, new FakeTimeProvider(Now));

    private static PrevalidateMaterialScanQuery Request() =>
        new("org-001", "env-dev", "MIR-001", "WO-001", "OP-10");

    private static MesMaterialLotAvailabilityRequest AvailabilityRequest() =>
        new("org-001", "env-dev", "MAT-001", "PCS", "SITE-01", "LINE-01", "LOT-001", new DateOnly(2026, 8, 26));

    private static Queue<HttpResponseMessage> FrozenVersionAndBomResponses(object materialLine) => new(
    [
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 200,
                data = new
                {
                    total = 1,
                    items = new[]
                    {
                        new { productionVersionId = "PV-001", organizationId = "org-001", environmentId = "env-dev", skuCode = "FG-001", mbomVersionId = "MBOM-001:B" },
                    },
                },
            }),
        },
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                message = "ok",
                code = 200,
                data = new
                {
                    bomCode = "MBOM-001",
                    revision = "B",
                    skuCode = "FG-001",
                    engineeringBomVersionId = "EBOM-001:A",
                    status = "published",
                    materialLines = new[] { materialLine },
                    recipeLines = Array.Empty<object>(),
                },
            }),
        },
    ]);

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
                5m, 5m, 0m, "product-engineering", "snap-001", Now));
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

    private static HttpMesMaterialPrevalidationProvider CreateHttpProvider(
        HttpMessageHandler productEngineeringHandler,
        HttpMessageHandler inventoryHandler) =>
        new(
            new MesProductEngineeringHttpClient(new HttpClient(productEngineeringHandler) { BaseAddress = new Uri("http://engineering") }),
            new MesInventoryHttpClient(new HttpClient(inventoryHandler) { BaseAddress = new Uri("http://inventory") }),
            new TestInternalServiceTokenProvider(),
            new StubMesIntegrationEventContextAccessor());

    private sealed class StubQualificationProvider(bool result) : IMesMaterialQualificationProvider
    {
        public int CallCount { get; private set; }
        public MesMaterialQualificationRequest? LastRequest { get; private set; }

        public Task<bool> IsFrozenSubstituteAsync(
            MesMaterialQualificationRequest request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

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
        public string LastRequestUri { get; private set; } = string.Empty;
        public string CorrelationId { get; private set; } = string.Empty;
        public string AuthorizationParameter { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
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
