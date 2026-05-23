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
    DateTimeOffset? ExistingEndUtc = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);

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

    Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<MesScheduleResult>> GetScheduleResultsAsync(CancellationToken cancellationToken = default);

    Task CloseUnavailabilityAsync(string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default);

    Task CloseUnavailabilityAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset restoredAtUtc,
        CancellationToken cancellationToken = default);

    Task<string> ResolveWorkCenterIdAsync(string deviceAssetId, CancellationToken cancellationToken = default);

    Task<string> ResolveWorkCenterIdAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken = default);

    Task<MesScheduleResult> AddScheduleResultAsync(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default);
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

    public Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WorkOrders);
    }

    public Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationTasks);
    }

    public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Unavailabilities);
    }

    public Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scoped = _unavailabilities
            .Where(x => IsInScope(x, organizationId, environmentId))
            .ToList();
        return Task.FromResult<IReadOnlyCollection<WorkCenterUnavailability>>(scoped);
    }

    public Task<IReadOnlyCollection<MesScheduleResult>> GetScheduleResultsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ScheduleResults);
    }

    public Task CloseUnavailabilityAsync(string deviceAssetId, DateTimeOffset restoredAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseUnavailability(deviceAssetId, restoredAtUtc);
        return Task.CompletedTask;
    }

    public Task CloseUnavailabilityAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset restoredAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = _unavailabilities.FindIndex(x =>
            IsInScope(x, organizationId, environmentId)
            && string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.OrdinalIgnoreCase)
            && x.ToUtc is null);

        if (index >= 0)
        {
            var current = _unavailabilities[index];
            _unavailabilities[index] = current with { ToUtc = restoredAtUtc };
        }

        return Task.CompletedTask;
    }

    public Task<string> ResolveWorkCenterIdAsync(string deviceAssetId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveWorkCenterId(deviceAssetId));
    }

    public Task<string> ResolveWorkCenterIdAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken = default)
    {
        _ = organizationId;
        _ = environmentId;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveWorkCenterId(deviceAssetId));
    }

    public Task<MesScheduleResult> AddScheduleResultAsync(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation>? compareAssignments = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AddScheduleResult(trigger, scheduledAtUtc, plan, compareAssignments));
    }

    public Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetScheduleOperations(organizationId, environmentId));
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

    private static bool IsInScope(WorkCenterUnavailability unavailability, string organizationId, string environmentId)
    {
        var organizationMatches = unavailability.OrganizationId is null
            || string.Equals(unavailability.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = unavailability.EnvironmentId is null
            || string.Equals(unavailability.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }
}
