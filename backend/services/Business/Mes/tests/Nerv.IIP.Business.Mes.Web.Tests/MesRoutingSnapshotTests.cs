using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesRoutingSnapshotTests
{
    [Fact]
    public async Task Convert_plan_without_explicit_work_center_freezes_published_routing_and_is_releasable()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var captured = MesRoutingSnapshotResult.Captured(
            "product-engineering-http:PV-001:ROUTE-1000:A",
            [
                new MesRoutingOperationSnapshot(10, "MIX", "WC-MIX", ["WC-MIX-ALT"], 45, true),
                new MesRoutingOperationSnapshot(20, "PACK", "WC-PACK", [], 15, false),
            ]);
        var routingProvider = new FakeRoutingSnapshotProvider(captured);
        var requestedAtUtc = DateTimeOffset.Parse("2026-07-21T08:00:00Z");
        var command = NewCommand(requestedAtUtc, idempotencyKey: "routing-snapshot-001");
        var handler = new ConvertPlanToWorkOrderCommandHandler(
            dbContext,
            new RuleScheduler(),
            null,
            NoMaterialRequirementsProvider.Instance,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            routingProvider);

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.ReferenceId, replay.ReferenceId);
        Assert.Single(routingProvider.Requests);
        var request = Assert.Single(routingProvider.Requests);
        Assert.Equal("PV-001", request.ProductionVersionId);
        Assert.Equal("SKU-FG-1000", request.SkuId);
        Assert.Equal(12m, request.WorkOrderQuantity);

        var workOrder = await dbContext.WorkOrders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("SUG-001", workOrder.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("DEMAND-001", workOrder.SourcePlanReference?.SourceDemandReference);

        var tasks = await dbContext.OperationTasks.AsNoTracking()
            .OrderBy(x => x.OperationSequence)
            .ToArrayAsync(CancellationToken.None);
        Assert.Collection(
            tasks,
            mix =>
            {
                Assert.Equal($"{workOrder.WorkOrderId}-OP-10", mix.OperationTaskId);
                Assert.Equal(10, mix.OperationSequence);
                Assert.Equal("MIX", mix.OperationCode);
                Assert.Equal("WC-MIX", mix.WorkCenterId);
                Assert.Equal(["WC-MIX-ALT"], mix.AlternativeWorkCenterIdList);
                Assert.Equal(TimeSpan.FromMinutes(45), mix.Duration);
                Assert.Equal("SKU-FG-1000", mix.SkuCode);
                Assert.Equal("PCS", mix.UomCode);
                Assert.Equal(12m, mix.PlannedQuantity);
                Assert.True(mix.RequiresQualityInspection);
            },
            pack =>
            {
                Assert.Equal($"{workOrder.WorkOrderId}-OP-20", pack.OperationTaskId);
                Assert.Equal(20, pack.OperationSequence);
                Assert.Equal("PACK", pack.OperationCode);
                Assert.Equal("WC-PACK", pack.WorkCenterId);
                Assert.Empty(pack.AlternativeWorkCenterIdList);
                Assert.Equal(TimeSpan.FromMinutes(15), pack.Duration);
                Assert.False(pack.RequiresQualityInspection);
            });

        var release = await new ReleaseWorkOrderCommandHandler(dbContext, NoMaterialRequirementsProvider.Instance).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", workOrder.WorkOrderId, requestedAtUtc.AddMinutes(1)),
            CancellationToken.None);

        Assert.Equal("Accepted", release.Status);
        Assert.Equal(
            "released",
            (await dbContext.WorkOrders.SingleAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Convert_plan_without_valid_published_routing_rejects_before_creating_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var routingProvider = new FakeRoutingSnapshotProvider(
            MesRoutingSnapshotResult.Missing("product-engineering:routing:ROUTE-MISSING:A"));
        var handler = new ConvertPlanToWorkOrderCommandHandler(
            dbContext,
            new RuleScheduler(),
            null,
            NoMaterialRequirementsProvider.Instance,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            routingProvider);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCommand(DateTimeOffset.Parse("2026-07-21T08:00:00Z"), "routing-snapshot-missing"),
            CancellationToken.None));

        Assert.Contains("ROUTING_SNAPSHOT_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dbContext.WorkOrders.Local);
        Assert.Empty(dbContext.OperationTasks.Local);
    }

    [Fact]
    public async Task Http_provider_resolves_exact_production_version_and_preserves_routing_operation_contract()
    {
        var requests = new List<string>();
        var httpHandler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            requests.Add(pathAndQuery);
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                return JsonEnvelope(new
                {
                    productionVersionId = "PV-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "SKU-FG-1000",
                    mbomVersionId = "MBOM-1000:A",
                    routingVersionId = "ROUTE-1000:A",
                    effectiveDate = "2026-07-21",
                    lotSize = 12m,
                    status = "active",
                });
            }

            if (pathAndQuery.StartsWith("/api/business/v1/engineering/routings/ROUTE-1000/A?", StringComparison.Ordinal))
            {
                return JsonEnvelope(new
                {
                    routingCode = "ROUTE-1000",
                    revision = "A",
                    skuCode = "SKU-FG-1000",
                    status = "Published",
                    effectiveDate = "2026-07-01",
                    operations = new object[]
                    {
                        new
                        {
                            sequence = 10,
                            workCenterCode = "WC-MIX",
                            operationCode = "MIX",
                            operationName = "Mix",
                            standardMinutes = 45,
                            setupMinutes = 5,
                            runMinutes = 35,
                            teardownMinutes = 5,
                            controlKey = "quality",
                            requiresReporting = true,
                            requiresQualityInspection = true,
                            isOutsourced = false,
                        },
                        new
                        {
                            sequence = 20,
                            workCenterCode = "WC-PACK",
                            operationCode = "PACK",
                            operationName = "Pack",
                            standardMinutes = 15,
                            setupMinutes = 0,
                            runMinutes = 15,
                            teardownMinutes = 0,
                            controlKey = "standard",
                            requiresReporting = true,
                            requiresQualityInspection = false,
                            isOutsourced = false,
                        },
                    },
                });
            }

            throw new InvalidOperationException($"Unexpected ProductEngineering request: {pathAndQuery}");
        });
        var snapshotProvider = new HttpMesProductEngineeringRoutingSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(httpHandler)
            {
                BaseAddress = new Uri("http://product-engineering"),
            }));

        var result = await snapshotProvider.GetSnapshotAsync(
            new MesRoutingSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG-1000",
                "PV-001",
                12m,
                DateTimeOffset.Parse("2026-07-21T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal(MesRoutingSnapshotStatus.Captured, result.Status);
        Assert.Equal("product-engineering-http:PV-001:ROUTE-1000:A", result.SourceSystem);
        Assert.Collection(
            result.Operations,
            operation =>
            {
                Assert.Equal(10, operation.Sequence);
                Assert.Equal("MIX", operation.OperationCode);
                Assert.Equal("WC-MIX", operation.WorkCenterId);
                Assert.Empty(operation.AlternativeWorkCenterIds);
                Assert.Equal(45, operation.StandardMinutes);
                Assert.True(operation.RequiresQualityInspection);
            },
            operation => Assert.Equal("PACK", operation.OperationCode));
        Assert.Equal(2, requests.Count);
        Assert.Contains("organizationId=org-001", requests[0], StringComparison.Ordinal);
        Assert.Contains("environmentId=env-dev", requests[0], StringComparison.Ordinal);
        Assert.Contains("skuCode=SKU-FG-1000", requests[0], StringComparison.Ordinal);
        Assert.Contains("lotSize=12", requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_provider_rejects_unpublished_routing()
    {
        var httpHandler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            if (pathAndQuery.StartsWith("/api/business/v1/engineering/production-versions/resolve?", StringComparison.Ordinal))
            {
                return JsonEnvelope(new
                {
                    productionVersionId = "PV-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    skuCode = "SKU-FG-1000",
                    mbomVersionId = "MBOM-1000:A",
                    routingVersionId = "ROUTE-1000:A",
                    effectiveDate = "2026-07-21",
                    lotSize = 12m,
                    status = "active",
                });
            }

            return JsonEnvelope(new
            {
                routingCode = "ROUTE-1000",
                revision = "A",
                skuCode = "SKU-FG-1000",
                status = "Draft",
                effectiveDate = "2026-07-01",
                operations = new[]
                {
                    new
                    {
                        sequence = 10,
                        workCenterCode = "WC-MIX",
                        operationCode = "MIX",
                        operationName = "Mix",
                        standardMinutes = 45,
                        setupMinutes = 5,
                        runMinutes = 35,
                        teardownMinutes = 5,
                        controlKey = "quality",
                        requiresReporting = true,
                        requiresQualityInspection = true,
                        isOutsourced = false,
                    },
                },
            });
        });
        var snapshotProvider = new HttpMesProductEngineeringRoutingSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(httpHandler)
            {
                BaseAddress = new Uri("http://product-engineering"),
            }));

        var result = await snapshotProvider.GetSnapshotAsync(
            new MesRoutingSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG-1000",
                "PV-001",
                12m,
                DateTimeOffset.Parse("2026-07-21T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal(MesRoutingSnapshotStatus.Missing, result.Status);
        Assert.Empty(result.Operations);
        Assert.Equal("product-engineering:routing:ROUTE-1000:A", result.SourceSystem);
    }

    [Fact]
    public async Task Http_provider_rejects_production_version_from_another_scope()
    {
        var requestCount = 0;
        var snapshotProvider = new HttpMesProductEngineeringRoutingSnapshotProvider(
            new MesProductEngineeringHttpClient(new HttpClient(new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return JsonEnvelope(new
                {
                    productionVersionId = "PV-001",
                    organizationId = "org-other",
                    environmentId = "env-dev",
                    skuCode = "SKU-FG-1000",
                    mbomVersionId = "MBOM-1000:A",
                    routingVersionId = "ROUTE-1000:A",
                    effectiveDate = "2026-07-21",
                    lotSize = 12m,
                    status = "active",
                });
            }))
            {
                BaseAddress = new Uri("http://product-engineering"),
            }));

        var result = await snapshotProvider.GetSnapshotAsync(
            new MesRoutingSnapshotRequest(
                "org-001",
                "env-dev",
                "WO-001",
                "SKU-FG-1000",
                "PV-001",
                12m,
                DateTimeOffset.Parse("2026-07-21T08:00:00Z")),
            CancellationToken.None);

        Assert.Equal(MesRoutingSnapshotStatus.Missing, result.Status);
        Assert.Equal("product-engineering:production-version:PV-001", result.SourceSystem);
        Assert.Equal(1, requestCount);
    }

    private static ConvertPlanToWorkOrderCommand NewCommand(DateTimeOffset requestedAtUtc, string idempotencyKey)
    {
        return new ConvertPlanToWorkOrderCommand(
            "org-001",
            "env-dev",
            "SUG-001",
            null,
            requestedAtUtc,
            "SKU-FG-1000",
            "PV-001",
            12m,
            "PCS",
            requestedAtUtc.AddDays(2),
            null,
            "DemandPlanning",
            "PlanningSuggestion",
            "SUG-001",
            "DEMAND-001",
            idempotencyKey);
    }

    private static HttpResponseMessage JsonEnvelope<T>(T data)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                new { data, success = true, message = "OK", code = 0 },
                options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
    }

    private sealed class FakeRoutingSnapshotProvider(MesRoutingSnapshotResult result) : IMesRoutingSnapshotProvider
    {
        public List<MesRoutingSnapshotRequest> Requests { get; } = [];

        public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
            MesRoutingSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class NoMaterialRequirementsProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoMaterialRequirementsProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}

internal sealed class SingleOperationRoutingSnapshotProvider : IMesRoutingSnapshotProvider
{
    public static readonly SingleOperationRoutingSnapshotProvider Instance = new();

    public Task<MesRoutingSnapshotResult> GetSnapshotAsync(
        MesRoutingSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesRoutingSnapshotResult.Captured(
            $"test:{request.ProductionVersionId}:ROUTE-TEST:A",
            [new MesRoutingOperationSnapshot(10, "OP-10", "WC-TEST", [], 30, false)]));
    }
}
