using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductEngineeringReleaseEventHandlerTests
{
    [Fact]
    public async Task ProductionVersionCreatedHandler_BindsCreatedMesWorkOrdersWithoutProductionVersionForSameSku()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-release-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-FG-001",
                "SKU-FG-1000",
                null,
                10m,
                10,
                DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
                "PCS",
                null));
            dbContext.WorkOrders.Add(WorkOrder.Create(
                "org-001",
                "env-dev",
                "WO-FG-002",
                "SKU-OTHER",
                null,
                10m,
                10,
                DateTimeOffset.Parse("2026-06-02T16:00:00Z"),
                "PCS",
                null));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());

            await handler.HandleAsync(CreateProductionVersionCreatedEvent(), CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var matchingWorkOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-FG-001");
        var otherWorkOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-FG-002");
        Assert.Equal("PV-FG-1000", matchingWorkOrder.ProductionVersionId);
        Assert.Null(otherWorkOrder.ProductionVersionId);
        Assert.Equal(1, await assertionDbContext.ProcessedIntegrationEvents.CountAsync());
    }

    private static ProductionVersionCreatedIntegrationEvent CreateProductionVersionCreatedEvent()
    {
        return new ProductionVersionCreatedIntegrationEvent(
            "evt-product-engineering-pv-created-001",
            ProductEngineeringIntegrationEventTypes.ProductionVersionCreated,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-01T07:30:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "product-engineering",
            "product-engineering:production-version-created:org-001:env-dev:PV-FG-1000",
            new ProductionVersionCreatedPayload(
                "PV-FG-1000",
                "SKU-FG-1000",
                "MBOM-FG:A",
                "ROUTE-FG:A",
                new DateOnly(2026, 6, 1),
                null));
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }
}
