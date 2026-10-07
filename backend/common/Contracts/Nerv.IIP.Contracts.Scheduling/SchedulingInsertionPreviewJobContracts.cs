namespace Nerv.IIP.Contracts.Scheduling;

public sealed record SchedulingInsertionPreviewRequestContract(
    string OrganizationId, string EnvironmentId, string PlanId, string WorkOrderId, int ContractVersion = 1);
public sealed record SchedulingInsertionPreviewInputContract(
    string OrganizationId, string EnvironmentId, string PlanId, string WorkOrderId,
    DateTimeOffset HorizonStartUtc, DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<string> WorkOrderIds, int ContractVersion = 1);
public enum SchedulingInsertionPreviewJobStatusContract { Created, Running, Completed, Failed }
public sealed record SchedulingInsertionPreviewJobContract(
    Guid JobId, SchedulingInsertionPreviewJobStatusContract Status, SchedulingInsertionPreviewInputContract Input,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? FinishedAtUtc,
    SchedulePlanContract? Preview, string? FailureReason);
