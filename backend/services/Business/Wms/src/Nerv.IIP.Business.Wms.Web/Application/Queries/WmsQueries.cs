using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Queries;

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
    string TaskType,
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

        if (!WmsListQueryFilters.TryParseStatus<WarehouseTaskType>(request.TaskType, out var taskType))
        {
            return new ListWarehouseTasksResponse([], 0);
        }

        var query = dbContext.WarehouseTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.TaskType == taskType);

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
