using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

public sealed record ListProductionReportsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null) : IQuery<ListProductionReportsResponse>;

public sealed record ListProductionReportsResponse(
    IReadOnlyCollection<ProductionReportFact> Items,
    int Total);

public sealed record ProductionReportFact(
    string ProductionReportId,
    string ReportNo,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset ReportedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? ScrapReasonCode = null,
    string? DefectRecordNo = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string? ReversedReportNo = null,
    string? ReversalReason = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    DateTimeOffset? InventoryPostingFailedAtUtc = null,
    // 报工所属工单当前状态,供 Console 报工冲销按钮按工单生命周期分级(已关闭工单禁用冲销,MAN-444/#798)。
    string? WorkOrderStatus = null,
    // 冲销本报工的负向记录单号(服务端逐行反查,若本报工已被冲销则非空)。供 Console「已冲销」判定与
    // 原单→冲销单互链**跨服务端分页稳定**,避免前端只从当前页推断已冲销状态(MAN-444/#798 review)。
    string? ReversalReportNo = null);

public sealed record GetProductionReportQuery(
    string OrganizationId,
    string EnvironmentId,
    string ReportNo) : IQuery<GetProductionReportResponse>;

public sealed record GetProductionReportResponse(
    ProductionReportFact Report,
    IReadOnlyCollection<ConsumedMaterialLotFact> ConsumedMaterialLots);

public sealed record ConsumedMaterialLotFact(
    string MaterialId,
    string MaterialLotId,
    decimal ConsumedQuantity,
    string UomCode,
    string MaterialIssueRequestNo);

internal static class ProductionReportFactProjection
{
    public static IQueryable<ProductionReportFact> SelectFacts(
        this IQueryable<ProductionReport> query,
        ApplicationDbContext dbContext) =>
        query.Select(x => new ProductionReportFact(
            x.Id.ToString(),
            x.ReportNo,
            x.WorkOrderId,
            x.OperationTaskId,
            x.GoodQuantity,
            x.ScrapQuantity,
            x.ReworkQuantity,
            x.ReportedAtUtc,
            x.WorkOrderId,
            x.OperationTaskId,
            x.ScrapReasonCode,
            x.DefectRecordNo,
            x.ProducedLotNo,
            x.SerialNo,
            x.ReversedReportNo,
            x.ReversalReason,
            dbContext.ProductionReportMaterialConsumptions
                .Where(consumption => consumption.OrganizationId == x.OrganizationId
                    && consumption.EnvironmentId == x.EnvironmentId
                    && consumption.ReportNo == x.ReportNo
                    && consumption.InventoryPostingFailureCode != null)
                .OrderByDescending(consumption => consumption.InventoryPostingFailedAtUtc)
                .Select(consumption => consumption.InventoryPostingFailureCode)
                .FirstOrDefault(),
            dbContext.ProductionReportMaterialConsumptions
                .Where(consumption => consumption.OrganizationId == x.OrganizationId
                    && consumption.EnvironmentId == x.EnvironmentId
                    && consumption.ReportNo == x.ReportNo
                    && consumption.InventoryPostingFailureCode != null)
                .OrderByDescending(consumption => consumption.InventoryPostingFailedAtUtc)
                .Select(consumption => consumption.InventoryPostingFailureMessage)
                .FirstOrDefault(),
            dbContext.ProductionReportMaterialConsumptions
                .Where(consumption => consumption.OrganizationId == x.OrganizationId
                    && consumption.EnvironmentId == x.EnvironmentId
                    && consumption.ReportNo == x.ReportNo
                    && consumption.InventoryPostingFailureCode != null)
                .OrderByDescending(consumption => consumption.InventoryPostingFailedAtUtc)
                .Select(consumption => consumption.InventoryPostingFailedAtUtc)
                .FirstOrDefault(),
            dbContext.WorkOrders
                .Where(workOrder => workOrder.OrganizationId == x.OrganizationId
                    && workOrder.EnvironmentId == x.EnvironmentId
                    && workOrder.WorkOrderIdValue == x.WorkOrderId)
                .Select(workOrder => workOrder.Status)
                .FirstOrDefault(),
            dbContext.ProductionReports
                .Where(reversal => reversal.OrganizationId == x.OrganizationId
                    && reversal.EnvironmentId == x.EnvironmentId
                    && reversal.ReversedReportNo == x.ReportNo)
                .Select(reversal => reversal.ReportNo)
                .FirstOrDefault()));
}

public sealed class GetProductionReportQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetProductionReportQuery, GetProductionReportResponse>
{
    public async Task<GetProductionReportResponse> Handle(GetProductionReportQuery request, CancellationToken cancellationToken)
    {
        var report = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReportNo == request.ReportNo)
            .SelectFacts(dbContext)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Production report was not found. ReportNo = {request.ReportNo}");

        var consumedMaterialLots = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ReportNo == request.ReportNo)
            .OrderBy(x => x.MaterialId)
            .ThenBy(x => x.MaterialLotId)
            .Select(x => new ConsumedMaterialLotFact(
                x.MaterialId, x.MaterialLotId, x.ConsumedQuantity, x.UomCode, x.MaterialIssueRequestNo))
            .ToArrayAsync(cancellationToken);

        return new GetProductionReportResponse(report, consumedMaterialLots);
    }
}

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            // Keep this provider-neutral for the EF InMemory contract tests; Npgsql-specific ILike would need a separate test path.
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.ReportNo.ToLower().Contains(keyword) ||
                x.WorkOrderId.ToLower().Contains(keyword) ||
                x.OperationTaskId.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.OperationTaskIdValue == x.OperationTaskId &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.ReportedAtUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(take)
            .SelectFacts(dbContext)
            .ToArrayAsync(cancellationToken);
        return new ListProductionReportsResponse(items, total);
    }
}

public sealed record ListFinishedGoodsReceiptRequestsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Status = null) : IQuery<ListFinishedGoodsReceiptRequestsResponse>;

public sealed record ListFinishedGoodsReceiptRequestsResponse(
    IReadOnlyCollection<FinishedGoodsReceiptRequestFact> Items,
    int Total);

public sealed record FinishedGoodsReceiptRequestFact(
    string ReceiptRequestId,
    string RequestNo,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    decimal PostedQuantity,
    decimal RemainingQuantity,
    decimal? UnitCost,
    string ReceiptStatus,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? SkuCode = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string? PostedInventoryMovementId = null,
    DateTimeOffset? PostedAtUtc = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    DateTimeOffset? InventoryPostingFailedAtUtc = null);

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.RequestNo.ToLower().Contains(keyword) ||
                x.WorkOrderId.ToLower().Contains(keyword) ||
                x.SkuId.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLower();
            query = query.Where(x => x.Status.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkOrderId == x.WorkOrderId &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(take)
            .Select(x => new FinishedGoodsReceiptRequestFact(
                x.Id.ToString(),
                x.RequestNo,
                x.WorkOrderId,
                x.SkuId,
                x.Quantity,
                x.PostedQuantity,
                x.Quantity - x.PostedQuantity,
                x.UnitCost,
                x.Status,
                x.RequestedAtUtc,
                x.WorkOrderId,
                x.SkuId,
                x.ProducedLotNo,
                x.SerialNo,
                x.PostedInventoryMovementId,
                x.PostedAtUtc,
                x.InventoryPostingFailureCode,
                x.InventoryPostingFailureMessage,
                x.InventoryPostingFailedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListFinishedGoodsReceiptRequestsResponse(items, total);
    }
}

public sealed record ListCapacityImpactsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    int Skip = 0,
    int Take = 100,
    string? WorkCenterId = null,
    string? Keyword = null,
    string? ShiftId = null,
    string? Status = null) : IQuery<ListCapacityImpactsResponse>;

public sealed record ListCapacityImpactsResponse(
    IReadOnlyCollection<CapacityImpactFact> Items,
    int Total);

public sealed record CapacityImpactFact(
    string ImpactId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ReasonCode,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null);

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

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            query = query.Where(x => x.WorkCenterId == request.WorkCenterId);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.DowntimeEventNo.ToLower().Contains(keyword) ||
                x.WorkCenterId.ToLower().Contains(keyword) ||
                (x.DeviceAssetId != null && x.DeviceAssetId.ToLower().Contains(keyword)) ||
                x.Reason.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = status switch
            {
                "open" => query.Where(x => x.ToUtc == null),
                "recovered" => query.Where(x => x.ToUtc != null),
                _ => query.Where(_ => false),
            };
        }

        if (!string.IsNullOrWhiteSpace(request.ShiftId))
        {
            var shiftId = request.ShiftId.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkCenterId == x.WorkCenterId &&
                task.ShiftId == shiftId &&
                (x.DeviceAssetId == null || task.DeviceAssetId == x.DeviceAssetId)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.FromUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(take)
            .Select(x => new CapacityImpactFact(
                x.DowntimeEventNo,
                x.WorkCenterId,
                x.DeviceAssetId,
                x.ToUtc == null ? "Open" : "Recovered",
                x.FromUtc,
                x.ToUtc,
                x.Reason,
                x.WorkCenterId,
                null,
                x.DeviceAssetId,
                null))
            .ToArrayAsync(cancellationToken);
        return new ListCapacityImpactsResponse(items, total);
    }
}
