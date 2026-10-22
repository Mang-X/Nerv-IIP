namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;

public partial record ScheduleFirstPlanJobId : IGuidStronglyTypedId;
public enum ScheduleFirstPlanJobStatus { Created, Running, Completed, Failed }

public sealed class ScheduleFirstPlanJob : Entity<ScheduleFirstPlanJobId>, IAggregateRoot
{
    private ScheduleFirstPlanJob() { }
    public ScheduleFirstPlanJob(string organizationId, string environmentId, string inputJson, DateTimeOffset createdAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        InputJson = inputJson;
        CreatedAtUtc = createdAtUtc;
    }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InputJson { get; private set; } = string.Empty;
    public ScheduleFirstPlanJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string? PlanId { get; private set; }
    public string? FailureReason { get; private set; }

    public bool Start(DateTimeOffset now)
    {
        if (Status != ScheduleFirstPlanJobStatus.Created) return false;
        Status = ScheduleFirstPlanJobStatus.Running;
        StartedAtUtc = now;
        return true;
    }
    public void Complete(string planId, DateTimeOffset now)
    {
        Status = ScheduleFirstPlanJobStatus.Completed;
        PlanId = planId;
        FinishedAtUtc = now;
    }
    public void Fail(string reason, DateTimeOffset now)
    {
        Status = ScheduleFirstPlanJobStatus.Failed;
        FailureReason = reason;
        FinishedAtUtc = now;
    }
}
