namespace Nerv.IIP.Business.Mes.Web.Application.Planning;

internal sealed class InMemoryMesPlanningStore : IMesPlanningStore
{
    private readonly List<PlannedWorkOrder> _workOrders = [];
    private readonly List<PlannedOperationTask> _operationTasks = [];
    private readonly List<WorkCenterUnavailability> _unavailabilities = [];

    public IReadOnlyCollection<PlannedWorkOrder> WorkOrders => _workOrders;

    public IReadOnlyCollection<PlannedOperationTask> OperationTasks => _operationTasks;

    public IReadOnlyCollection<WorkCenterUnavailability> Unavailabilities => _unavailabilities;

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

    public Task<bool> UnavailabilityExistsAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_unavailabilities.Any(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.DeviceAssetId == deviceAssetId &&
            x.FromUtc == fromUtc));
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

    public Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WorkOrders);
    }

    public Task<bool> WorkOrderExistsAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_workOrders.Any(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.WorkOrderId == workOrderId));
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

    private static bool IsInScope(WorkCenterUnavailability unavailability, string organizationId, string environmentId)
    {
        var organizationMatches = unavailability.OrganizationId is null
            || string.Equals(unavailability.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = unavailability.EnvironmentId is null
            || string.Equals(unavailability.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }
}
