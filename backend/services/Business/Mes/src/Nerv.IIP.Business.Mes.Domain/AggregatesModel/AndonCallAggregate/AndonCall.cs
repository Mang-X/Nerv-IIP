namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

public partial record AndonCallId : IGuidStronglyTypedId;

public enum AndonCallCategory { MaterialShortage, Equipment, Quality, Process }
public enum AndonCallStatus { Open, Claimed, Closed }

public sealed class AndonCall : Entity<AndonCallId>, IAggregateRoot
{
    private AndonCall() { }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RaiseIntentKey { get; private set; } = string.Empty;
    public AndonCallCategory Category { get; private set; }
    public string WorkOrderId { get; private set; } = string.Empty;
    public string OperationTaskIdValue { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public string CallerId { get; private set; } = string.Empty;
    public DateTimeOffset RaisedAtUtc { get; private set; }
    public AndonCallStatus Status { get; private set; }
    public string? ResponderId { get; private set; }
    public string? ClaimIntentKey { get; private set; }
    public DateTimeOffset? FirstRespondedAtUtc { get; private set; }
    public string? CloseIntentKey { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset? EscalatedAtUtc { get; private set; }
    public string? EscalationRecipientId { get; private set; }
    public double? EscalationTimeoutSeconds { get; private set; }
    public RowVersion RowVersion { get; private set; } = new(0);
    public TimeSpan? ResponseDuration => FirstRespondedAtUtc - RaisedAtUtc;

    public static AndonCall Raise(string organizationId, string environmentId, string intentKey,
        AndonCallCategory category, string workOrderId, string operationTaskId, string workCenterId,
        string callerId, DateTimeOffset raisedAtUtc) => new()
    {
        OrganizationId = DomainGuard.RequiredBounded(organizationId, nameof(organizationId), 100),
        EnvironmentId = DomainGuard.RequiredBounded(environmentId, nameof(environmentId), 100),
        RaiseIntentKey = DomainGuard.RequiredBounded(intentKey, nameof(intentKey), 150),
        Category = Enum.IsDefined(category) ? category : throw new KnownException("呼叫类别无效。"),
        WorkOrderId = DomainGuard.RequiredBounded(workOrderId, nameof(workOrderId), 100),
        OperationTaskIdValue = DomainGuard.RequiredBounded(operationTaskId, nameof(operationTaskId), 100),
        WorkCenterId = DomainGuard.RequiredBounded(workCenterId, nameof(workCenterId), 100),
        CallerId = DomainGuard.RequiredBounded(callerId, nameof(callerId), 100),
        RaisedAtUtc = raisedAtUtc.ToUniversalTime(),
        Status = AndonCallStatus.Open
    };

    public void Claim(string responderId, string intentKey, DateTimeOffset respondedAtUtc)
    {
        responderId = DomainGuard.RequiredBounded(responderId, nameof(responderId), 100);
        intentKey = DomainGuard.RequiredBounded(intentKey, nameof(intentKey), 150);
        if (ClaimIntentKey == intentKey && ResponderId == responderId) return;
        if (Status != AndonCallStatus.Open) throw new KnownException("呼叫已被认领，不能再次认领。");
        if (respondedAtUtc < RaisedAtUtc) throw new KnownException("首次响应时间不能早于呼叫时间。");
        ResponderId = responderId;
        ClaimIntentKey = intentKey;
        FirstRespondedAtUtc = respondedAtUtc.ToUniversalTime();
        Status = AndonCallStatus.Claimed;
    }

    public void Close(string responderId, string intentKey, DateTimeOffset closedAtUtc)
    {
        responderId = DomainGuard.RequiredBounded(responderId, nameof(responderId), 100);
        intentKey = DomainGuard.RequiredBounded(intentKey, nameof(intentKey), 150);
        if (ResponderId != responderId) throw new KnownException("仅呼叫认领人可以关闭呼叫。");
        if (CloseIntentKey == intentKey) return;
        if (Status != AndonCallStatus.Claimed) throw new KnownException("仅已认领的呼叫可以关闭。");
        if (closedAtUtc < FirstRespondedAtUtc) throw new KnownException("关闭时间不能早于首次响应时间。");
        CloseIntentKey = intentKey;
        ClosedAtUtc = closedAtUtc.ToUniversalTime();
        Status = AndonCallStatus.Closed;
    }

    // 调用方传入当前 scope/category 的显式配置；聚合不提供默认时限或接收人。
    public bool TryEscalate(DateTimeOffset nowUtc, TimeSpan unclaimedTimeout, string recipientId)
    {
        if (Status != AndonCallStatus.Open || EscalatedAtUtc is not null) return false;
        if (unclaimedTimeout <= TimeSpan.Zero) throw new KnownException("未认领升级时限必须大于零。");
        recipientId = DomainGuard.RequiredBounded(recipientId, nameof(recipientId), 100);
        if (nowUtc - RaisedAtUtc < unclaimedTimeout) return false;
        EscalatedAtUtc = nowUtc.ToUniversalTime();
        EscalationRecipientId = recipientId;
        EscalationTimeoutSeconds = unclaimedTimeout.TotalSeconds;
        this.AddDomainEvent(new DomainEvents.AndonCallEscalatedDomainEvent(this));
        return true;
    }
}
