using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record ListMesWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null) : IQuery<ListMesWorkOrdersResponse>;

public sealed record ListMesWorkOrdersResponse(
    IReadOnlyCollection<MesWorkOrderExecutionFact> Items,
    int Total);

public sealed record MesWorkOrderExecutionFact(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    int Priority,
    DateTimeOffset DueUtc,
    string Status,
    IReadOnlyCollection<MesOperationTaskExecutionFact> OperationTasks);

public sealed record MesOperationTaskExecutionFact(
    string OperationTaskId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    long DurationTicks,
    DateTimeOffset? ExistingStartUtc,
    DateTimeOffset? ExistingEndUtc);

public sealed class ListMesWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMesWorkOrdersQuery, ListMesWorkOrdersResponse>
{
    public async Task<ListMesWorkOrdersResponse> Handle(ListMesWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);
        var workOrdersQuery = dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            workOrdersQuery = workOrdersQuery.Where(x => x.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            workOrdersQuery = workOrdersQuery.Where(x =>
                x.WorkOrderIdValue.ToLower().Contains(keyword) ||
                x.SkuId.ToLower().Contains(keyword) ||
                (x.ProductionVersionId != null && x.ProductionVersionId.ToLower().Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            workOrdersQuery = workOrdersQuery.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkOrderId == x.WorkOrderIdValue &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
        }

        var total = await workOrdersQuery.CountAsync(cancellationToken);
        var workOrders = await workOrdersQuery
            .OrderBy(x => x.DueUtc)
            .ThenBy(x => x.WorkOrderIdValue)
            .Skip(skip)
            .Take(take)
            .Select(x => new
            {
                x.WorkOrderIdValue,
                x.SkuId,
                x.ProductionVersionId,
                x.Quantity,
                x.Priority,
                x.DueUtc,
                x.Status,
            })
            .ToListAsync(cancellationToken);

        // Keep this IN-list bounded by the clamped `take` value above; this endpoint returns a
        // compact execution snapshot for scheduling/acceptance flows, not an unbounded export.
        var workOrderIds = workOrders.Select(x => x.WorkOrderIdValue).ToArray();
        var tasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .OrderBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .Select(x => new
            {
                x.WorkOrderId,
                x.OperationTaskIdValue,
                x.Status,
                x.OperationSequence,
                x.WorkCenterId,
                x.AlternativeWorkCenterIds,
                x.EarliestStartUtc,
                x.DurationTicks,
                x.ExistingStartUtc,
                x.ExistingEndUtc,
            })
            .ToListAsync(cancellationToken);

        var tasksByWorkOrder = tasks
            .GroupBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Select(task => new MesOperationTaskExecutionFact(
                    task.OperationTaskIdValue,
                    task.Status.ToString(),
                    task.OperationSequence,
                    task.WorkCenterId,
                    SplitAlternatives(task.AlternativeWorkCenterIds),
                    task.EarliestStartUtc,
                    task.DurationTicks,
                    task.ExistingStartUtc,
                    task.ExistingEndUtc)).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var items = workOrders.Select(x => new MesWorkOrderExecutionFact(
            x.WorkOrderIdValue,
            x.SkuId,
            x.ProductionVersionId,
            x.Quantity,
            x.Priority,
            x.DueUtc,
            x.Status,
            tasksByWorkOrder.GetValueOrDefault(x.WorkOrderIdValue, []))).ToArray();

        return new ListMesWorkOrdersResponse(items, total);
    }

    private static IReadOnlyCollection<string> SplitAlternatives(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
