using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Application.Planning;

public sealed record PlannedWorkOrder(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    int Priority,
    DateTimeOffset DueUtc);

public sealed record PlannedOperationTask(
    string WorkOrderId,
    string OperationTaskId,
    OperationTaskStatus Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    TimeSpan Duration,
    DateTimeOffset? ExistingStartUtc = null,
    DateTimeOffset? ExistingEndUtc = null);

public sealed record MesScheduleResult(
    int ScheduleVersion,
    RescheduleTrigger Trigger,
    DateTimeOffset ScheduledAtUtc,
    IReadOnlyCollection<ScheduledOperation> Assignments,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public interface IMesPlanningStore
{
    IReadOnlyCollection<PlannedWorkOrder> WorkOrders { get; }
    IReadOnlyCollection<PlannedOperationTask> OperationTasks { get; }
    IReadOnlyCollection<WorkCenterUnavailability> Unavailabilities { get; }
    IReadOnlyCollection<MesScheduleResult> ScheduleResults { get; }

    void AddWorkOrder(PlannedWorkOrder workOrder);

    void AddOperationTask(PlannedOperationTask operationTask);

    void AddUnavailability(WorkCenterUnavailability unavailability);

    void CloseUnavailability(string deviceAssetId, DateTimeOffset restoredAtUtc);

    void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId);

    string ResolveWorkCenterId(string deviceAssetId);

    MesScheduleResult AddScheduleResult(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments = null);

    IReadOnlyCollection<ScheduleOperation> GetScheduleOperations(string organizationId, string environmentId);
}

/// <summary>
/// Process-local MES planning store for the first rescheduling vertical slice.
/// Schedule state is intentionally not durable until the MES persistence model lands.
/// </summary>
public sealed class InMemoryMesPlanningStore : IMesPlanningStore
{
    private readonly List<PlannedWorkOrder> _workOrders = [];
    private readonly List<PlannedOperationTask> _operationTasks = [];
    private readonly List<WorkCenterUnavailability> _unavailabilities = [];
    private readonly List<MesScheduleResult> _scheduleResults = [];
    private readonly Dictionary<string, string> _assetWorkCenterMap = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<PlannedWorkOrder> WorkOrders => _workOrders;

    public IReadOnlyCollection<PlannedOperationTask> OperationTasks => _operationTasks;

    public IReadOnlyCollection<WorkCenterUnavailability> Unavailabilities => _unavailabilities;

    public IReadOnlyCollection<MesScheduleResult> ScheduleResults => _scheduleResults;

    public void AddWorkOrder(PlannedWorkOrder workOrder)
    {
        ArgumentNullException.ThrowIfNull(workOrder);
        if (_workOrders.Any(x =>
                x.OrganizationId == workOrder.OrganizationId
                && x.EnvironmentId == workOrder.EnvironmentId
                && x.WorkOrderId == workOrder.WorkOrderId))
        {
            throw new InvalidOperationException($"Work order already exists: {workOrder.WorkOrderId}");
        }

        _workOrders.Add(workOrder);
    }

    public void AddOperationTask(PlannedOperationTask operationTask)
    {
        ArgumentNullException.ThrowIfNull(operationTask);
        _operationTasks.Add(operationTask);
    }

    public void AddUnavailability(WorkCenterUnavailability unavailability)
    {
        ArgumentNullException.ThrowIfNull(unavailability);
        _unavailabilities.Add(unavailability);
    }

    public void CloseUnavailability(string deviceAssetId, DateTimeOffset restoredAtUtc)
    {
        var index = _unavailabilities.FindIndex(x =>
            string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.OrdinalIgnoreCase)
            && x.ToUtc is null);

        if (index < 0)
        {
            return;
        }

        var current = _unavailabilities[index];
        _unavailabilities[index] = current with { ToUtc = restoredAtUtc };
    }

    public void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId)
    {
        _assetWorkCenterMap[deviceAssetId] = workCenterId;
    }

    public string ResolveWorkCenterId(string deviceAssetId)
    {
        return _assetWorkCenterMap.TryGetValue(deviceAssetId, out var workCenterId)
            ? workCenterId
            : deviceAssetId;
    }

    public MesScheduleResult AddScheduleResult(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var affected = FindAffectedWorkOrders(plan, compareAssignments);
        var result = new MesScheduleResult(
            _scheduleResults.Count + 1,
            trigger,
            scheduledAtUtc,
            plan.Assignments,
            affected);
        _scheduleResults.Add(result);
        return result;
    }

    public IReadOnlyCollection<ScheduleOperation> GetScheduleOperations(string organizationId, string environmentId)
    {
        var workOrders = _workOrders
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToDictionary(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase);

        return _operationTasks
            .Where(x => workOrders.ContainsKey(x.WorkOrderId))
            .Select(x =>
            {
                var workOrder = workOrders[x.WorkOrderId];
                return new ScheduleOperation(
                    x.WorkOrderId,
                    x.OperationTaskId,
                    x.Status,
                    x.OperationSequence,
                    workOrder.Priority,
                    workOrder.DueUtc,
                    x.EarliestStartUtc,
                    x.Duration,
                    x.WorkCenterId,
                    x.AlternativeWorkCenterIds,
                    x.ExistingStartUtc,
                    x.ExistingEndUtc);
            })
            .ToList();
    }

    private IReadOnlyCollection<string> FindAffectedWorkOrders(
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments)
    {
        var previousAssignments = compareAssignments ?? _scheduleResults.LastOrDefault()?.Assignments;
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
}
