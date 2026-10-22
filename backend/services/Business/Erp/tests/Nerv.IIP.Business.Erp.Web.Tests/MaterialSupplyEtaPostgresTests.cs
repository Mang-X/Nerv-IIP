using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Procurement;
using Nerv.IIP.Contracts.Erp;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection("ERP PostgreSQL acceptance")]
public sealed class MaterialSupplyEtaPostgresTests
{
    [MaterialSupplyEtaPostgresFact]
    public async Task Query_accumulates_open_quantity_and_isolates_organization_and_environment_on_postgres()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        AddReleasedOrder(dbContext, "org-001", "env-dev", "PO-001", 4m, new DateOnly(2026, 6, 3));
        AddReleasedOrder(dbContext, "org-001", "env-dev", "PO-002", 7m, new DateOnly(2026, 6, 5));
        var partlyReceived = AddReleasedOrder(dbContext, "org-001", "env-dev", "PO-PARTIAL", 10m, new DateOnly(2026, 6, 1));
        partlyReceived.RegisterReceipt("10", 9m);
        AddReleasedOrder(dbContext, "org-001", "env-other", "PO-OTHER-ENV", 100m, new DateOnly(2026, 6, 1));
        AddReleasedOrder(dbContext, "org-other", "env-dev", "PO-OTHER-ORG", 100m, new DateOnly(2026, 6, 1));
        await dbContext.SaveChangesAsync();

        var response = await new ResolveMaterialSupplyEtasQueryHandler(dbContext).Handle(
            new ResolveMaterialSupplyEtasQuery(
                "org-001",
                "env-dev",
                [new MaterialSupplyEtaRequestItem("SKU-RM-1000", "kg", 10m)]),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("kg", item.UomCode);
        Assert.Equal(12m, item.OpenPurchaseQuantity);
        Assert.Equal(new DateOnly(2026, 6, 5), item.ExpectedAvailableDate);
    }

    private static PurchaseOrder AddReleasedOrder(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        decimal quantity,
        DateOnly promisedDate)
    {
        var order = PurchaseOrder.Create(
            organizationId,
            environmentId,
            purchaseOrderNo,
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("10", "SKU-RM-1000", "kg", quantity, 1m, promisedDate)]);
        order.MarkApprovalRequested($"approval-{purchaseOrderNo}");
        order.ReleaseAfterApproval($"approval-{purchaseOrderNo}");
        dbContext.PurchaseOrders.Add(order);
        return order;
    }

    private sealed class NoopMediator : IMediator
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

[AttributeUsage(AttributeTargets.Method)]
public sealed class MaterialSupplyEtaPostgresFactAttribute : FactAttribute
{
    public MaterialSupplyEtaPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP material supply ETA acceptance test.";
        }
    }
}
