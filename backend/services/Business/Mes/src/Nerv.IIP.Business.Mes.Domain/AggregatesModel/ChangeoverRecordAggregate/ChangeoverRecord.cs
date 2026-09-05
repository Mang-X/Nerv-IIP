namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.ChangeoverRecordAggregate;

public partial record ChangeoverRecordId : IGuidStronglyTypedId;

public enum ChangeoverToolingCheckResult
{
    Passed = 0,
    Failed = 1,
    NotRequired = 2
}

public sealed class ChangeoverRecord : Entity<ChangeoverRecordId>, IAggregateRoot
{
    private ChangeoverRecord()
    {
    }

    private ChangeoverRecord(
        string organizationId,
        string environmentId,
        string changeoverNo,
        string workCenterId,
        string deviceAssetId,
        string operatorId,
        ChangeoverToolingCheckResult toolingCheckResult,
        DateTimeOffset startedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        ChangeoverNo = DomainGuard.Required(changeoverNo, nameof(changeoverNo));
        WorkCenterId = DomainGuard.Required(workCenterId, nameof(workCenterId));
        DeviceAssetId = DomainGuard.Required(deviceAssetId, nameof(deviceAssetId));
        OperatorId = DomainGuard.Required(operatorId, nameof(operatorId));
        ToolingCheckResult = toolingCheckResult;
        StartedAtUtc = startedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ChangeoverNo { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string DeviceAssetId { get; private set; } = string.Empty;
    public string OperatorId { get; private set; } = string.Empty;
    public ChangeoverToolingCheckResult ToolingCheckResult { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static ChangeoverRecord Start(
        string organizationId,
        string environmentId,
        string changeoverNo,
        string workCenterId,
        string deviceAssetId,
        string operatorId,
        ChangeoverToolingCheckResult toolingCheckResult,
        DateTimeOffset startedAtUtc) =>
        new(
            organizationId,
            environmentId,
            changeoverNo,
            workCenterId,
            deviceAssetId,
            operatorId,
            toolingCheckResult,
            startedAtUtc);

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (CompletedAtUtc is not null)
        {
            throw new KnownException("换型记录已结束。");
        }

        if (completedAtUtc < StartedAtUtc)
        {
            throw new KnownException("换型结束时间不能早于开始时间。");
        }

        CompletedAtUtc = completedAtUtc;
    }
}
