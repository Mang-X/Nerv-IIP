using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Queries;

public sealed record ListBackorderOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListBackorderOrdersResponse>;

public sealed record ListBackorderOrdersResponse(IReadOnlyCollection<BackorderOrderFact> Items, int Total);

public sealed record BackorderOrderFact(
    BackorderOrderId BackorderOrderId,
    string BackorderOrderNo,
    string OutboundOrderNo,
    string OutboundOrderLineNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string PickLocationCode,
    decimal BackorderQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc,
    string? ClosureReason);

public sealed class ListBackorderOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListBackorderOrdersQuery, ListBackorderOrdersResponse>
{
    public async Task<ListBackorderOrdersResponse> Handle(ListBackorderOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.BackorderOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<BackorderOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListBackorderOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.BackorderOrderNo.ToUpper().Contains(keyword)
                || x.OutboundOrderNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.BackorderOrderNo)
            .Skip(skip).Take(take)
            .Select(x => new BackorderOrderFact(x.Id, x.BackorderOrderNo, x.OutboundOrderNo, x.OutboundOrderLineNo,
                x.SkuCode, x.UomCode, x.SiteCode, x.PickLocationCode, x.BackorderQuantity, x.Status.ToString(),
                x.CreatedAtUtc, x.ClosedAtUtc, x.ClosureReason))
            .ToArrayAsync(cancellationToken);
        return new ListBackorderOrdersResponse(items, total);
    }
}

public sealed record ListInboundOrdersQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListInboundOrdersResponse>;

public sealed record ListInboundOrdersResponse(IReadOnlyCollection<InboundOrderListItem> Items, int Total);

public sealed record InboundOrderListItem(InboundOrderId InboundOrderId, string InboundOrderNo, string Status, DateTime CreatedAtUtc);

public sealed class ListInboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInboundOrdersQuery, ListInboundOrdersResponse>
{
    public async Task<ListInboundOrdersResponse> Handle(ListInboundOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.InboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<InboundOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListInboundOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.InboundOrderNo.ToUpper().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.InboundOrderNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new InboundOrderListItem(x.Id, x.InboundOrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListInboundOrdersResponse(items, total);
    }
}

public sealed record ListOutboundOrdersQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListOutboundOrdersResponse>;

public sealed record ListOutboundOrdersResponse(IReadOnlyCollection<OutboundOrderListItem> Items, int Total);

public sealed record OutboundOrderListItem(OutboundOrderId OutboundOrderId, string OutboundOrderNo, string Status, DateTime CreatedAtUtc);

public sealed class ListOutboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOutboundOrdersQuery, ListOutboundOrdersResponse>
{
    public async Task<ListOutboundOrdersResponse> Handle(ListOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.OutboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<OutboundOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListOutboundOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.OutboundOrderNo.ToUpper().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.OutboundOrderNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new OutboundOrderListItem(x.Id, x.OutboundOrderNo, x.Status.ToString(), x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListOutboundOrdersResponse(items, total);
    }
}

public sealed record ListWarehouseTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    WarehouseTaskType TaskType,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? OperatorUserId = null,
    string? Keyword = null) : IQuery<ListWarehouseTasksResponse>;

public sealed record ListWarehouseTasksResponse(IReadOnlyCollection<WarehouseTaskFact> Items, int Total);

public sealed record WarehouseTaskFact(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string TaskType,
    string TaskNo,
    string SourceOrderNo,
    string SourceOrderLineNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string FromLocationCode,
    string ToLocationCode,
    decimal PlannedQuantity,
    decimal ExecutedQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed class ListWarehouseTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWarehouseTasksQuery, ListWarehouseTasksResponse>
{
    public async Task<ListWarehouseTasksResponse> Handle(ListWarehouseTasksQuery request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.OperatorUserId))
        {
            return new ListWarehouseTasksResponse([], 0);
        }

        var query = dbContext.WarehouseTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.TaskType == request.TaskType);

        if (WmsListQueryFilters.TryParseStatus<WarehouseTaskStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListWarehouseTasksResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.FromLocationCode == request.LocationCode || x.ToLocationCode == request.LocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.TaskNo.ToUpper().Contains(keyword)
                || x.SourceOrderNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.TaskNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new WarehouseTaskFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.TaskType.ToString(),
                x.TaskNo,
                x.SourceOrderNo,
                x.SourceOrderLineNo,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.FromLocationCode,
                x.ToLocationCode,
                x.PlannedQuantity,
                x.ExecutedQuantity,
                x.Status.ToString(),
                x.CreatedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListWarehouseTasksResponse(items, total);
    }
}

public sealed record ListCountExecutionsQuery(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null) : IQuery<ListCountExecutionsResponse>;

public sealed record ListCountExecutionsResponse(IReadOnlyCollection<CountExecutionFact> Items, int Total);

public sealed record CountExecutionFact(
    CountExecutionId CountExecutionId,
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? VarianceQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed class ListCountExecutionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCountExecutionsQuery, ListCountExecutionsResponse>
{
    public async Task<ListCountExecutionsResponse> Handle(ListCountExecutionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.CountExecutions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId);

        if (WmsListQueryFilters.TryParseStatus<CountExecutionStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListCountExecutionsResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.LocationCode == request.LocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.CountNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword)
                || x.LocationCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.CountNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new CountExecutionFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.CountNo,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.ExpectedQuantity,
                x.CountedQuantity,
                x.VarianceQuantity,
                x.Status.ToString(),
                x.CreatedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListCountExecutionsResponse(items, total);
    }
}

public sealed record ListWcsTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId,
    WarehouseTaskId? WarehouseTaskId = null,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    bool? Failed = null,
    string? Keyword = null) : IQuery<ListWcsTasksResponse>;

public sealed record ListWcsTasksResponse(IReadOnlyCollection<WcsTaskFact> Items, int Total);

public sealed record WcsTaskFact(
    WcsTaskId WcsTaskId,
    string OrganizationId,
    string EnvironmentId,
    WarehouseTaskId WarehouseTaskId,
    string AdapterType,
    string ExternalTaskId,
    string Status,
    int AttemptCount,
    string? FailureCode,
    string? FailureMessage,
    DateTime DispatchedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ListWcsDispatchCircuitsQuery(string OrganizationId, string EnvironmentId) : IQuery<IReadOnlyCollection<WcsDispatchCircuitFact>>;

public sealed record WcsDispatchCircuitFact(string AdapterType, string DeviceId, int ConsecutiveFailureCount, bool IsOpen, DateTime? OpenedAtUtc, DateTime? LastFailureAtUtc, DateTime? ResetAtUtc);

public sealed class ListWcsDispatchCircuitsQueryHandler(ApplicationDbContext dbContext) : IQueryHandler<ListWcsDispatchCircuitsQuery, IReadOnlyCollection<WcsDispatchCircuitFact>>
{
    public async Task<IReadOnlyCollection<WcsDispatchCircuitFact>> Handle(ListWcsDispatchCircuitsQuery request, CancellationToken cancellationToken) =>
        await dbContext.WcsDispatchCircuits.AsNoTracking().Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.AdapterType).ThenBy(x => x.DeviceId)
            .Select(x => new WcsDispatchCircuitFact(x.AdapterType, x.DeviceId, x.ConsecutiveFailureCount, x.OpenedAtUtc != null, x.OpenedAtUtc, x.LastFailureAtUtc, x.ResetAtUtc))
            .ToArrayAsync(cancellationToken);
}

public sealed class ListWcsTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWcsTasksQuery, ListWcsTasksResponse>
{
    public async Task<ListWcsTasksResponse> Handle(ListWcsTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WcsTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ExternalTaskId))
        {
            query = query.Where(x => x.ExternalTaskId == request.ExternalTaskId);
        }

        if (request.WarehouseTaskId is not null)
        {
            query = query.Where(x => x.WarehouseTaskId == request.WarehouseTaskId);
        }

        if (WmsListQueryFilters.TryParseStatus<WcsTaskStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListWcsTasksResponse([], 0);
        }

        if (request.Failed is true)
        {
            query = query.Where(x => x.FailedAtUtc != null);
        }
        else if (request.Failed is false)
        {
            query = query.Where(x => x.FailedAtUtc == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.ExternalTaskId.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.DispatchedAtUtc)
            .ThenByDescending(x => x.ExternalTaskId)
            .Skip(skip)
            .Take(take)
            .Select(x => new WcsTaskFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.WarehouseTaskId,
                x.AdapterType,
                x.ExternalTaskId,
                x.Status.ToString(),
                x.AttemptCount,
                x.FailureCode,
                x.FailureMessage,
                x.DispatchedAtUtc,
                x.FailedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListWcsTasksResponse(items, total);
    }
}

public sealed record ListReceivingQualityGatesQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? GateStatus = null,
    string? Keyword = null) : IQuery<ListReceivingQualityGatesResponse>;

public sealed record ListReceivingQualityGatesResponse(IReadOnlyCollection<ReceivingQualityGateFact> Items, int Total);

public sealed record ReceivingQualityGateFact(
    InboundOrderId InboundOrderId,
    InboundOrderLineId InboundOrderLineId,
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string InboundOrderStatus,
    string SiteCode,
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string QualityGateStatus,
    string? InspectionRecordId,
    string? QualityDispositionReason,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    DateTime CreatedAtUtc);

public sealed class ListReceivingQualityGatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListReceivingQualityGatesQuery, ListReceivingQualityGatesResponse>
{
    public async Task<ListReceivingQualityGatesResponse> Handle(ListReceivingQualityGatesQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.InboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .SelectMany(order => order.Lines, (order, line) => new { order, line })
            .Where(x => x.line.QualityGateStatus != InboundQualityGateStatuses.NotRequired);

        if (!string.IsNullOrWhiteSpace(request.GateStatus))
        {
            var gateStatus = request.GateStatus.Trim().ToLowerInvariant();
            query = query.Where(x => x.line.QualityGateStatus == gateStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.order.InboundOrderNo.ToUpper().Contains(keyword)
                || x.line.SkuCode.ToUpper().Contains(keyword)
                || (x.line.InspectionRecordId != null && x.line.InspectionRecordId.ToUpper().Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.order.CreatedAtUtc)
            .ThenByDescending(x => x.order.InboundOrderNo)
            .ThenBy(x => x.line.LineNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new ReceivingQualityGateFact(
                x.order.Id,
                x.line.Id,
                x.order.OrganizationId,
                x.order.EnvironmentId,
                x.order.InboundOrderNo,
                x.order.Status.ToString(),
                x.order.SiteCode,
                x.line.LineNo,
                x.line.SkuCode,
                x.line.UomCode,
                x.line.ReceivedQuantity,
                x.line.StagingLocationCode,
                x.line.LotNo,
                x.line.SerialNo,
                x.line.QualityStatus,
                x.line.QualityGateStatus,
                x.line.InspectionRecordId,
                x.line.QualityDispositionReason,
                x.line.OwnerType,
                x.line.OwnerId,
                x.line.ProductionDate,
                x.line.ExpiryDate,
                x.order.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListReceivingQualityGatesResponse(items, total);
    }
}

public sealed record ListSupplierReturnRequestsQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListSupplierReturnRequestsResponse>;

public sealed record ListSupplierReturnRequestsResponse(IReadOnlyCollection<SupplierReturnRequestFact> Items, int Total);

public sealed record SupplierReturnRequestFact(
    SupplierReturnRequestId SupplierReturnRequestId,
    string OrganizationId,
    string EnvironmentId,
    string SupplierReturnNo,
    string InboundOrderNo,
    string InboundOrderLineNo,
    string InspectionRecordId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    string DispositionType,
    string? DispositionReason,
    string Status,
    DateTime CreatedAtUtc);

public sealed class ListSupplierReturnRequestsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSupplierReturnRequestsQuery, ListSupplierReturnRequestsResponse>
{
    public async Task<ListSupplierReturnRequestsResponse> Handle(ListSupplierReturnRequestsQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.SupplierReturnRequests
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<SupplierReturnRequestStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListSupplierReturnRequestsResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.SupplierReturnNo.ToUpper().Contains(keyword)
                || x.InboundOrderNo.ToUpper().Contains(keyword)
                || x.InspectionRecordId.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.SupplierReturnNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new SupplierReturnRequestFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.SupplierReturnNo,
                x.InboundOrderNo,
                x.InboundOrderLineNo,
                x.InspectionRecordId,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.OwnerType,
                x.OwnerId,
                x.Quantity,
                x.DispositionType,
                x.DispositionReason,
                x.Status.ToString(),
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListSupplierReturnRequestsResponse(items, total);
    }
}

internal static class WmsListQueryFilters
{
    public static bool TryParseStatus<TStatus>(string? value, out TStatus status)
        where TStatus : struct, Enum
    {
        status = default;
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        return Enum.TryParse(trimmed, true, out status) && Enum.IsDefined(status);
    }

    public static string NormalizeKeyword(string? value)
    {
        return value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
