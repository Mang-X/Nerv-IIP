namespace Nerv.IIP.Contracts.Scheduling;

public static class SchedulingFirstPlanJobLimits
{
    public const int MaxOrderCount = 500;
}

public sealed record SchedulingFirstPlanOrderContract(string WorkOrderId, int Priority, bool IsRush);
public sealed record SchedulingFirstPlanInputContract(
    string OrganizationId, string EnvironmentId,
    DateTimeOffset HorizonStartUtc, DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingFirstPlanOrderContract> Orders, int ContractVersion = 1);
public enum SchedulingFirstPlanJobStatusContract { Created, Running, Completed, Failed }
public sealed record SchedulingFirstPlanJobContract(
    Guid JobId, SchedulingFirstPlanJobStatusContract Status, SchedulingFirstPlanInputContract Input,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? FinishedAtUtc,
    string? PlanId, string? FailureReason);
