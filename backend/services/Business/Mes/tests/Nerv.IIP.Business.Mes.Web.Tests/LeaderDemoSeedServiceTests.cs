using System.Net;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class LeaderDemoSeedServiceTests
{
    private const string InternalServiceToken = "leader-demo-internal-token";

    [Fact]
    public async Task Seed_retries_active_version_resolution_then_creates_released_prerequisite_once()
    {
        await using var db = CreateDbContext();
        var handler = new ProductionVersionHandler(failuresBeforeSuccess: 1);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://product-engineering") };
        var seed = new LeaderDemoSeedService(
            db,
            new MesProductEngineeringHttpClient(client),
            new TestInternalServiceTokenProvider(InternalServiceToken));

        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        var workOrder = Assert.Single(await db.WorkOrders.ToArrayAsync());
        Assert.Equal("WO-DEMO-Q01", workOrder.WorkOrderId);
        Assert.Equal(WorkOrder.ReleasedStatus, workOrder.Status);
        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(0m, workOrder.ScrapQuantity);
        Assert.Equal(ProductionVersionHandler.ProductionVersionId, workOrder.ProductionVersionId);
        var operation = Assert.Single(await db.OperationTasks.ToArrayAsync());
        Assert.True(operation.RequiresQualityInspection);
        Assert.Equal("WC-CNC-DEMO", operation.WorkCenterId);
        Assert.Equal(3, handler.RequestCount);
        Assert.Empty(await db.ProductionReports.ToArrayAsync());
        Assert.Empty(await db.DefectRecords.ToArrayAsync());
        Assert.Empty(await db.QualityHoldContexts.ToArrayAsync());
        Assert.Empty(await db.FinishedGoodsReceiptRequests.ToArrayAsync());
    }

    [Fact]
    public async Task Seed_retries_a_transient_http_timeout_instead_of_aborting_startup()
    {
        await using var db = CreateDbContext();
        var handler = new ProductionVersionHandler(timeoutsBeforeSuccess: 1);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://product-engineering") };
        var seed = new LeaderDemoSeedService(
            db,
            new MesProductEngineeringHttpClient(client),
            new TestInternalServiceTokenProvider(InternalServiceToken));

        await seed.SeedAsync("org-001", "env-dev");

        Assert.Single(await db.WorkOrders.ToArrayAsync());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Seed_rejects_an_incompatible_reserved_work_order_before_remote_resolution()
    {
        await using var db = CreateDbContext();
        var existing = WorkOrder.Create(
            "org-001", "env-dev", "WO-DEMO-Q01", "OTHER-SKU", null, 5m, 1,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
        existing.MarkReleased();
        db.WorkOrders.Add(existing);
        await db.SaveChangesAsync();
        var handler = new ProductionVersionHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://product-engineering") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(
                db,
                new MesProductEngineeringHttpClient(client),
                new TestInternalServiceTokenProvider(InternalServiceToken)).SeedAsync("org-001", "env-dev"));

        Assert.Contains("WO-DEMO-Q01", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal("OTHER-SKU", (await db.WorkOrders.SingleAsync()).SkuId);
    }

    [Fact]
    public async Task Seed_rejects_reserved_work_order_bound_to_a_different_production_version()
    {
        await using var db = CreateDbContext();
        var existing = WorkOrder.Create(
            "org-001", "env-dev", "WO-DEMO-Q01", "SKU-DEMO-001", "019b03d4-fac4-7000-8000-000000000099", 10m, 1,
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero), "pcs");
        var operations = existing.Release(
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            [new RoutingStepSnapshot("OP-DEMO-Q01-010", 10, "WC-CNC-DEMO", [], TimeSpan.FromMinutes(30), true, "OP-CNC-DEMO")]);
        db.WorkOrders.Add(existing);
        db.OperationTasks.AddRange(operations);
        await db.SaveChangesAsync();
        var handler = new ProductionVersionHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://product-engineering") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LeaderDemoSeedService(
                db,
                new MesProductEngineeringHttpClient(client),
                new TestInternalServiceTokenProvider(InternalServiceToken)).SeedAsync("org-001", "env-dev"));

        Assert.Contains("WO-DEMO-Q01", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("019b03d4-fac4-7000-8000-000000000099", (await db.WorkOrders.SingleAsync()).ProductionVersionId);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-leader-demo-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new MesSeedTestMediator());
    }

    private sealed class ProductionVersionHandler(
        int failuresBeforeSuccess = 0,
        int timeoutsBeforeSuccess = 0) : HttpMessageHandler
    {
        public const string ProductionVersionId = "019b03d4-fac4-7000-8000-000000000001";
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal($"Bearer {InternalServiceToken}", request.Headers.Authorization?.ToString());
            if (RequestCount <= timeoutsBeforeSuccess)
            {
                throw new TaskCanceledException("Simulated ProductEngineering timeout.");
            }

            if (RequestCount <= failuresBeforeSuccess)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.StartsWith("/api/business/v1/engineering/production-versions/resolve?", request.RequestUri!.PathAndQuery, StringComparison.Ordinal);
            const string json = """
                {
                  "data": {
                    "productionVersionId": "019b03d4-fac4-7000-8000-000000000001",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "skuCode": "SKU-DEMO-001",
                    "mbomVersionId": "MBOM-DEMO-001:1",
                    "routingVersionId": "ROUTING-DEMO-001:1",
                    "effectiveDate": "2026-07-01",
                    "lotSize": 10,
                    "status": "active"
                  },
                  "success": true,
                  "message": "",
                  "code": 0
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    private sealed class MesSeedTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
