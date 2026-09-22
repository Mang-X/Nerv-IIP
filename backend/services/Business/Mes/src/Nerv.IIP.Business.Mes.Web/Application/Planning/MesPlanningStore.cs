namespace Nerv.IIP.Business.Mes.Web.Application.Planning;

public enum OperationTaskStatus
{
    Queued,
    InProgress,
    Completed,
    Cancelled,
}

public sealed record WorkCenterUnavailability(
    string WorkCenterId,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string Reason,
    string? DeviceAssetId = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);

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
    // 产出 SKU 必须由调用方给出：这条记录以前没有 SKU 字段，于是 PersistentMesPlanningStore
    // 落库时只能不传，让 OperationTask 回落成工单号（#3112）。声明为必填而非可空默认，
    // 关掉的是**漏传**——「建工序却没给 SKU」不再可表达。
    // 它**关不掉「传错」**：这仍是一个 string，把工单号当实参传进来照样编译；
    // 「传的是不是工单真实 SKU」由用例断言承担，不由类型承担。
    // 位置放在可选尾巴之前，因为 C# 要求必填参数先于可选参数。
    string SkuCode,
    DateTimeOffset? ExistingStartUtc = null,
    DateTimeOffset? ExistingEndUtc = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public interface IMesPlanningStore
{
    void AddWorkOrder(PlannedWorkOrder workOrder);

    void AddOperationTask(PlannedOperationTask operationTask);

    void AddUnavailability(WorkCenterUnavailability unavailability);

    void MapDeviceAssetToWorkCenter(string deviceAssetId, string workCenterId);

    Task<IReadOnlyCollection<PlannedWorkOrder>> GetWorkOrdersAsync(CancellationToken cancellationToken = default);

    Task<bool> WorkOrderExistsAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlannedOperationTask>> GetOperationTasksAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken = default);

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
    private readonly Dictionary<string, string> _assetWorkCenterMap = new(StringComparer.OrdinalIgnoreCase);

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

    private static bool IsInScope(WorkCenterUnavailability unavailability, string organizationId, string environmentId)
    {
        var organizationMatches = unavailability.OrganizationId is null
            || string.Equals(unavailability.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = unavailability.EnvironmentId is null
            || string.Equals(unavailability.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }
}
