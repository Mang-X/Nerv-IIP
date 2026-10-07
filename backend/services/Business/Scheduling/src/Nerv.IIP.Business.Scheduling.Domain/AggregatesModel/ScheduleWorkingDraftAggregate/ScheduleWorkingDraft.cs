namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleWorkingDraftAggregate;

public partial record ScheduleWorkingDraftId : IGuidStronglyTypedId;

public sealed class ScheduleWorkingDraft : Entity<ScheduleWorkingDraftId>, IAggregateRoot
{
    private ScheduleWorkingDraft() { }

    public ScheduleWorkingDraft(string organizationId, string environmentId, string planId, string userId,
        string stateJson, DateTimeOffset savedAtUtc)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        PlanId = planId;
        UserId = userId;
        Replace(stateJson, savedAtUtc);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PlanId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string StateJson { get; private set; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; private set; }

    public void Replace(string stateJson, DateTimeOffset savedAtUtc)
    {
        StateJson = stateJson;
        SavedAtUtc = savedAtUtc;
    }
}
