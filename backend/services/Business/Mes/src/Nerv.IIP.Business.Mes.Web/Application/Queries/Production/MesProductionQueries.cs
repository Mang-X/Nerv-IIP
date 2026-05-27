using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

public sealed record ListProductionReportsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Take = 100) : IQuery<ListProductionReportsResponse>;

public sealed record ListProductionReportsResponse(IReadOnlyCollection<ProductionReportFact> Items);

public sealed record ProductionReportFact(
    string ProductionReportId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc);

public sealed class ListProductionReportsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListProductionReportsQuery, ListProductionReportsResponse>
{
    public async Task<ListProductionReportsResponse> Handle(ListProductionReportsQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var query = dbContext.ProductionReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            query = query.Where(x => x.WorkOrderId == request.WorkOrderId);
        }

        var items = await query
            .OrderByDescending(x => x.ReportedAtUtc)
            .Take(take)
            .Select(x => new ProductionReportFact(
                x.Id.ToString(),
                x.WorkOrderId,
                x.OperationTaskId,
                x.GoodQuantity,
                x.ScrapQuantity,
                0m,
                x.ReportedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListProductionReportsResponse(items);
    }
}

public sealed record ListFinishedGoodsReceiptRequestsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Take = 100) : IQuery<ListFinishedGoodsReceiptRequestsResponse>;

public sealed record ListFinishedGoodsReceiptRequestsResponse(IReadOnlyCollection<FinishedGoodsReceiptRequestFact> Items);

public sealed record FinishedGoodsReceiptRequestFact(
    string ReceiptRequestId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string ReceiptStatus,
    DateTimeOffset RequestedAtUtc);

public sealed class ListFinishedGoodsReceiptRequestsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListFinishedGoodsReceiptRequestsQuery, ListFinishedGoodsReceiptRequestsResponse>
{
    public async Task<ListFinishedGoodsReceiptRequestsResponse> Handle(ListFinishedGoodsReceiptRequestsQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var query = dbContext.FinishedGoodsReceiptRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            query = query.Where(x => x.WorkOrderId == request.WorkOrderId);
        }

        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(take)
            .Select(x => new FinishedGoodsReceiptRequestFact(
                x.Id.ToString(),
                x.WorkOrderId,
                x.SkuId,
                x.Quantity,
                "Requested",
                x.RequestedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListFinishedGoodsReceiptRequestsResponse(items);
    }
}

public sealed record ListCapacityImpactsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    int Take = 100) : IQuery<ListCapacityImpactsResponse>;

public sealed record ListCapacityImpactsResponse(IReadOnlyCollection<CapacityImpactFact> Items);

public sealed record CapacityImpactFact(
    string ImpactId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ReasonCode);

public sealed class ListCapacityImpactsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCapacityImpactsQuery, ListCapacityImpactsResponse>
{
    public async Task<ListCapacityImpactsResponse> Handle(ListCapacityImpactsQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var query = dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            query = query.Where(x => x.DeviceAssetId == request.DeviceAssetId);
        }

        var items = await query
            .OrderByDescending(x => x.FromUtc)
            .Take(take)
            .Select(x => new CapacityImpactFact(
                x.Id.Id.ToString(),
                x.WorkCenterId,
                x.DeviceAssetId,
                x.ToUtc == null ? "Open" : "Recovered",
                x.FromUtc,
                x.ToUtc,
                x.Reason))
            .ToArrayAsync(cancellationToken);
        return new ListCapacityImpactsResponse(items);
    }
}
