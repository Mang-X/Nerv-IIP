namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;

public partial record ScheduleInsertionPreviewJobId : IGuidStronglyTypedId;
public enum ScheduleInsertionPreviewJobStatus { Created, Running, Completed, Failed }

public sealed class ScheduleInsertionPreviewJob : Entity<ScheduleInsertionPreviewJobId>, IAggregateRoot
{
    private ScheduleInsertionPreviewJob() { }
    public ScheduleInsertionPreviewJob(string organizationId, string environmentId, string inputJson, DateTimeOffset createdAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        InputJson = inputJson;
        CreatedAtUtc = createdAtUtc;
    }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InputJson { get; private set; } = string.Empty;
    public ScheduleInsertionPreviewJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string? PreviewJson { get; private set; }
    public string? FailureReason { get; private set; }

    public bool Start(DateTimeOffset now)
    {
        if (Status != ScheduleInsertionPreviewJobStatus.Created) return false;
        Status = ScheduleInsertionPreviewJobStatus.Running;
        StartedAtUtc = now;
        return true;
    }
    public void Complete(string previewJson, DateTimeOffset now)
    {
        Status = ScheduleInsertionPreviewJobStatus.Completed;
        PreviewJson = previewJson;
        FinishedAtUtc = now;
    }
    public void Fail(string reason, DateTimeOffset now)
    {
        Status = ScheduleInsertionPreviewJobStatus.Failed;
        FailureReason = reason;
        FinishedAtUtc = now;
    }
}
