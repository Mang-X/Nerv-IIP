using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using DeviceAssetWorkCenterMapping = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.DeviceAssetWorkCenterMapping;
using DomainOperationTask = Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate.OperationTask;
using DomainScheduleResult = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleResult;
using DomainWorkCenterUnavailability = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability;
using ScheduledOperationSnapshot = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduledOperationSnapshot;
using ScheduleTrigger = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleTrigger;

namespace Nerv.IIP.Business.Mes.Web.Application.Planning;

public sealed class PersistentMesPlanningStore(ApplicationDbContext dbContext, IOperationTaskRepository operationTaskRepository) : IMesPlanningStore
{
    public void AddWorkOrder(PlannedWorkOrder workOrder)
    {
        ArgumentNullException.ThrowIfNull(workOrder);
        dbContext.WorkOrders.Add(WorkOrder.Create(
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId,
            workOrder.SkuId,
            workOrder.ProductionVersionId,
            workOrder.Quantity,
            workOrder.Priority,
            workOrder.DueUtc));
    }

    public void AddOperationTask(PlannedOperationTask operationTask)
    {
        ArgumentNullException.ThrowIfNull(operationTask);
        var localWorkOrders = dbContext.WorkOrders.Local
            .Where(x => string.Equals(x.WorkOrderIdValue, operationTask.WorkOrderId, StringComparison.OrdinalIgnoreCase));
        if (operationTask.OrganizationId is not null && operationTask.EnvironmentId is not null)
        {
            localWorkOrders = localWorkOrders.Where(x =>
                x.OrganizationId == operationTask.OrganizationId &&
                x.EnvironmentId == operationTask.EnvironmentId);
        }

        var localWorkOrder = localWorkOrders.LastOrDefault();
        var organizationId = operationTask.OrganizationId ?? localWorkOrder?.OrganizationId ?? "unknown";
        var environmentId = operationTask.EnvironmentId ?? localWorkOrder?.EnvironmentId ?? "unknown";
        dbContext.OperationTasks.Add(DomainOperationTask.Create(
            organizationId,
            environmentId,
            operationTask.WorkOrderId,
            operationTask.OperationTaskId,
            ToDomainStatus(operationTask.Status),
            operationTask.OperationSequence,
            operationTask.WorkCenterId,
            operationTask.AlternativeWorkCenterIds,
            operationTask.EarliestStartUtc,
            operationTask.Duration,
            operationTask.ExistingStartUtc,
            operationTask.ExistingEndUtc));
    }

    public void AddUnavailability(WorkCenterUnavailability unavailability)
    {
        ArgumentNullException.ThrowIfNull(unavailability);
        var workCenterSegment = unavailability.WorkCenterId.Length <= 30 ? unavailability.WorkCenterId : unavailability.WorkCenterId[..30];
        dbContext.WorkCenterUnavailabilities.Add(DomainWorkCenterUnavailability.Open(
            unavailability.OrganizationId,
            unavailability.EnvironmentId,
            $"UNAV-{workCenterSegment}-{unavailability.FromUtc:yyyyMMddHHmmssfff}",
            unavailability.WorkCenterId,
            unavailability.FromUtc,
            unavailability.ToUtc,
            unavailability.Reason,
            unavailability.DeviceAssetId));
    }

    public void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId)
    {
        var existing = dbContext.DeviceAssetWorkCenterMappings.Local
            .SingleOrDefault(x => string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Remap(workCenterId);
            return;
        }

        dbContext.DeviceAssetWorkCenterMappings.Add(DeviceAssetWorkCenterMapping.Create(deviceAssetId, workCenterId));
    }

    public async Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkOrders
            .AsNoTracking()
            .OrderBy(x => x.WorkOrderIdValue)
            .Select(x => new PlannedWorkOrder(
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkOrderIdValue,
                x.SkuId,
                x.ProductionVersionId,
                x.Quantity,
                x.Priority,
                x.DueUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> WorkOrderExistsAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.WorkOrders.Local.Any(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderIdValue == workOrderId))
        {
            return Task.FromResult(true);
        }

        return dbContext.WorkOrders.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.WorkOrderIdValue == workOrderId,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.OperationTasks
            .AsNoTracking()
            .OrderBy(x => x.OperationTaskIdValue)
            .Select(x => new PlannedOperationTask(
                x.WorkOrderId,
                x.OperationTaskIdValue,
                ToWebStatus(x.Status),
                x.OperationSequence,
                x.WorkCenterId,
                x.AlternativeWorkCenterIdList,
                x.EarliestStartUtc,
                x.Duration,
                x.ExistingStartUtc,
                x.ExistingEndUtc,
                x.OrganizationId,
                x.EnvironmentId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .OrderBy(x => x.FromUtc)
            .ToListAsync(cancellationToken);
        return persisted
            .Concat(dbContext.WorkCenterUnavailabilities.Local)
            .Select(x => new WorkCenterUnavailability(
                x.WorkCenterId,
                x.FromUtc,
                x.ToUtc,
                x.Reason,
                x.DeviceAssetId,
                x.OrganizationId,
                x.EnvironmentId))
            .ToList();
    }

    public async Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x =>
                (x.OrganizationId == null || x.OrganizationId == organizationId) &&
                (x.EnvironmentId == null || x.EnvironmentId == environmentId))
            .OrderBy(x => x.FromUtc)
            .ToListAsync(cancellationToken);
        return persisted
            .Concat(dbContext.WorkCenterUnavailabilities.Local.Where(x => IsInScope(x, organizationId, environmentId)))
            .Select(x => new WorkCenterUnavailability(
                x.WorkCenterId,
                x.FromUtc,
                x.ToUtc,
                x.Reason,
                x.DeviceAssetId,
                x.OrganizationId,
                x.EnvironmentId))
            .ToList();
    }

    public async Task<IReadOnlyCollection<MesScheduleResult>> GetScheduleResultsAsync(CancellationToken cancellationToken = default)
    {
        var results = await dbContext.ScheduleResults
            .AsNoTracking()
            .OrderBy(x => x.ScheduleVersion)
            .ToListAsync(cancellationToken);
        return results.Select(ToWebScheduleResult).ToList();
    }

    public async Task CloseUnavailabilityAsync(string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.WorkCenterUnavailabilities
            .Where(x => x.DeviceAssetId == deviceAssetId && x.ToUtc == null)
            .OrderByDescending(x => x.FromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        current?.Close(restoredAtUtc);
    }

    public async Task CloseUnavailabilityAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset restoredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var current = await dbContext.WorkCenterUnavailabilities
            .Where(x =>
                (x.OrganizationId == null || x.OrganizationId == organizationId) &&
                (x.EnvironmentId == null || x.EnvironmentId == environmentId) &&
                x.DeviceAssetId == deviceAssetId &&
                x.ToUtc == null)
            .OrderByDescending(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ThenByDescending(x => x.FromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        current?.Close(restoredAtUtc);
    }

    public async Task<string> ResolveWorkCenterIdAsync(string deviceAssetId, CancellationToken cancellationToken = default)
    {
        var localMapped = dbContext.DeviceAssetWorkCenterMappings.Local
            .Where(x => x.DeviceAssetId == deviceAssetId)
            .Select(x => x.WorkCenterId)
            .LastOrDefault();
        if (localMapped is not null)
        {
            return localMapped;
        }

        var mapped = await dbContext.DeviceAssetWorkCenterMappings
            .AsNoTracking()
            .Where(x => x.DeviceAssetId == deviceAssetId)
            .Select(x => x.WorkCenterId)
            .SingleOrDefaultAsync(cancellationToken);

        return mapped ?? deviceAssetId;
    }

    public async Task<string> ResolveWorkCenterIdAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken = default)
    {
        var localMapped = dbContext.DeviceAssetWorkCenterMappings.Local
            .Where(x =>
                IsInScope(x, organizationId, environmentId) &&
                x.DeviceAssetId == deviceAssetId)
            .OrderByDescending(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.WorkCenterId)
            .FirstOrDefault();
        if (localMapped is not null)
        {
            return localMapped;
        }

        var mapped = await dbContext.DeviceAssetWorkCenterMappings
            .AsNoTracking()
            .Where(x =>
                (x.OrganizationId == null || x.OrganizationId == organizationId) &&
                (x.EnvironmentId == null || x.EnvironmentId == environmentId) &&
                x.DeviceAssetId == deviceAssetId)
            .OrderByDescending(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .Select(x => x.WorkCenterId)
            .FirstOrDefaultAsync(cancellationToken);

        return mapped ?? deviceAssetId;
    }

    public async Task<MesScheduleResult> AddScheduleResultAsync(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var affected = await FindAffectedWorkOrdersAsync(plan, compareAssignments, cancellationToken);
        var version = await dbContext.ScheduleResults.CountAsync(cancellationToken) + 1;
        var result = DomainScheduleResult.Create(
            version,
            Enum.Parse<ScheduleTrigger>(trigger.ToString()),
            scheduledAtUtc,
            plan.Assignments.Select(x => new ScheduledOperationSnapshot(
                x.WorkOrderId,
                x.OperationTaskId,
                x.WorkCenterId,
                x.StartUtc,
                x.EndUtc,
                x.Reason)).ToList(),
            affected);
        dbContext.ScheduleResults.Add(result);
        return ToWebScheduleResult(result);
    }

    public async Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        var persistedWorkOrders = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        var persistedWorkOrderIds = persistedWorkOrders.Select(x => x.Id).ToHashSet();
        var workOrders = persistedWorkOrders
            .Concat(dbContext.WorkOrders.Local.Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                !persistedWorkOrderIds.Contains(x.Id)))
            .GroupBy(x => x.WorkOrderIdValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        var workOrderIds = workOrders.Keys.ToList();
        var persistedOperationTasks = await operationTaskRepository.GetByScopeWorkOrdersAsync(
            organizationId,
            environmentId,
            workOrderIds,
            cancellationToken);
        var persistedOperationTaskIds = persistedOperationTasks.Select(x => x.Id).ToHashSet();
        var operationTasks = persistedOperationTasks
            .Concat(dbContext.OperationTasks.Local.Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrders.ContainsKey(x.WorkOrderId) &&
                !persistedOperationTaskIds.Contains(x.Id)))
            .ToList();

        return operationTasks.Select(x =>
        {
            var workOrder = workOrders[x.WorkOrderId];
            return new ScheduleOperation(
                x.WorkOrderId,
                x.OperationTaskIdValue,
                ToWebStatus(x.Status),
                x.OperationSequence,
                workOrder.Priority,
                workOrder.DueUtc,
                x.EarliestStartUtc,
                x.Duration,
                x.WorkCenterId,
                x.AlternativeWorkCenterIdList,
                x.ExistingStartUtc,
                x.ExistingEndUtc);
        }).ToList();
    }

    private async Task<IReadOnlyCollection<string>> FindAffectedWorkOrdersAsync(
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments,
        CancellationToken cancellationToken)
    {
        var previousAssignments = compareAssignments;
        if (previousAssignments is null)
        {
            var latest = await dbContext.ScheduleResults
                .AsNoTracking()
                .OrderByDescending(x => x.ScheduleVersion)
                .FirstOrDefaultAsync(cancellationToken);
            previousAssignments = latest?.Assignments.Select(ToWebScheduledOperation).ToList();
        }

        if (previousAssignments is null)
        {
            return [];
        }

        var previousByTask = previousAssignments.ToDictionary(x => x.OperationTaskId, StringComparer.OrdinalIgnoreCase);
        return plan.Assignments
            .Where(x => previousByTask.TryGetValue(x.OperationTaskId, out var prior) && x.StartUtc > prior.StartUtc)
            .Select(x => x.WorkOrderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MesScheduleResult ToWebScheduleResult(DomainScheduleResult result)
    {
        return new MesScheduleResult(
            result.ScheduleVersion,
            Enum.Parse<RescheduleTrigger>(result.Trigger.ToString()),
            result.ScheduledAtUtc,
            result.Assignments.Select(ToWebScheduledOperation).ToList(),
            result.AffectedWorkOrderIds);
    }

    private static ScheduledOperation ToWebScheduledOperation(ScheduledOperationSnapshot snapshot)
    {
        return new ScheduledOperation(
            snapshot.WorkOrderId,
            snapshot.OperationTaskId,
            snapshot.WorkCenterId,
            snapshot.StartUtc,
            snapshot.EndUtc,
            snapshot.Reason);
    }

    private static OperationTaskStatus ToWebStatus(OperationTaskLifecycleStatus status)
    {
        return Enum.Parse<OperationTaskStatus>(status.ToString());
    }

    private static OperationTaskLifecycleStatus ToDomainStatus(OperationTaskStatus status)
    {
        return Enum.Parse<OperationTaskLifecycleStatus>(status.ToString());
    }

    private static bool IsInScope(DomainWorkCenterUnavailability unavailability, string organizationId, string environmentId)
    {
        var organizationMatches = unavailability.OrganizationId is null
            || string.Equals(unavailability.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = unavailability.EnvironmentId is null
            || string.Equals(unavailability.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }

    private static bool IsInScope(DeviceAssetWorkCenterMapping mapping, string organizationId, string environmentId)
    {
        var organizationMatches = mapping.OrganizationId is null
            || string.Equals(mapping.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = mapping.EnvironmentId is null
            || string.Equals(mapping.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }
}
