using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

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
    DateTimeOffset DueUtc,
    bool IsRush = false);

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
    string? EnvironmentId = null,
    string? OperationCode = null,
    bool RequiresQualityInspection = false,
    string? RequiredSkillCode = null,
    decimal PlannedQuantity = 0m,
    OperationTaskRoutingIdentity? RoutingIdentity = null);

public interface IMesPlanningStore
{
    void AddWorkOrder(PlannedWorkOrder workOrder);

    void AddOperationTask(PlannedOperationTask operationTask);

    void AddUnavailability(WorkCenterUnavailability unavailability);

    Task<bool> UnavailabilityExistsAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default);

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

}
