using Nerv.IIP.Business.Scheduling.Domain.Services;

namespace Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;

public partial record OrderUrgencyBusinessPriorityId : IGuidStronglyTypedId;
public partial record OrderUrgencyBusinessPriorityChangeId : IGuidStronglyTypedId;
public partial record OrderUrgencySnapshotId : IGuidStronglyTypedId;

public sealed class OrderUrgencyBusinessPriority : Entity<OrderUrgencyBusinessPriorityId>, IAggregateRoot
{
    private OrderUrgencyBusinessPriority()
    {
    }

    private OrderUrgencyBusinessPriority(
        string organizationId,
        string environmentId,
        string orderId,
        string businessReference,
        BusinessPriorityLevel level,
        string setBy,
        string reason,
        DateTimeOffset setAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        OrganizationId = Required(organizationId, nameof(organizationId));
        EnvironmentId = Required(environmentId, nameof(environmentId));
        OrderId = Required(orderId, nameof(orderId));
        BusinessReference = Required(businessReference, nameof(businessReference));
        Level = level;
        SetBy = Required(setBy, nameof(setBy));
        Reason = Required(reason, nameof(reason));
        ValidateExpiry(setAtUtc, expiresAtUtc);
        SetAtUtc = setAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Revision = 1;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public string BusinessReference { get; private set; } = string.Empty;
    public BusinessPriorityLevel Level { get; private set; }
    public string SetBy { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset SetAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public long Revision { get; private set; }

    public static OrderUrgencyBusinessPriority Create(
        string organizationId,
        string environmentId,
        string orderId,
        string businessReference,
        BusinessPriorityLevel level,
        string setBy,
        string reason,
        DateTimeOffset setAtUtc,
        DateTimeOffset? expiresAtUtc) =>
        new(organizationId, environmentId, orderId, businessReference, level, setBy, reason, setAtUtc, expiresAtUtc);

    public OrderUrgencyBusinessPriorityChange InitialChange() =>
        OrderUrgencyBusinessPriorityChange.Record(
            OrganizationId, EnvironmentId, OrderId, BusinessReference, Revision,
            null, Level, SetBy, Reason, SetAtUtc, ExpiresAtUtc);

    public OrderUrgencyBusinessPriorityChange Change(
        BusinessPriorityLevel level,
        string setBy,
        string reason,
        DateTimeOffset setAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        var actor = Required(setBy, nameof(setBy));
        var normalizedReason = Required(reason, nameof(reason));
        ValidateExpiry(setAtUtc, expiresAtUtc);
        var previous = Level;
        Revision++;
        Level = level;
        SetBy = actor;
        Reason = normalizedReason;
        SetAtUtc = setAtUtc;
        ExpiresAtUtc = expiresAtUtc;

        return OrderUrgencyBusinessPriorityChange.Record(
            OrganizationId, EnvironmentId, OrderId, BusinessReference, Revision,
            previous, level, actor, normalizedReason, setAtUtc, expiresAtUtc);
    }

    public bool IsEffectiveAt(DateTimeOffset atUtc) => !ExpiresAtUtc.HasValue || ExpiresAtUtc > atUtc;

    public BusinessPriorityFact ToFact(DateTimeOffset atUtc) => new(
        Level,
        IsEffectiveAt(atUtc) ? "manual" : "expired-manual",
        Reason,
        SetAtUtc,
        ExpiresAtUtc,
        Revision);

    private static void ValidateExpiry(DateTimeOffset setAtUtc, DateTimeOffset? expiresAtUtc)
    {
        if (expiresAtUtc.HasValue && expiresAtUtc <= setAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Expiry must be after the setting time.");
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }
}

public sealed class OrderUrgencySnapshot : Entity<OrderUrgencySnapshotId>
{
    private OrderUrgencySnapshot()
    {
    }

    public OrderUrgencySnapshot(
        string organizationId,
        string environmentId,
        string orderId,
        string businessReference,
        OrderUrgencyLevel level,
        string modelVersion,
        string inputFingerprint,
        long businessPriorityRevision,
        DateTimeOffset calculationBucketUtc,
        DateTimeOffset calculatedAtUtc,
        string resultJson)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        OrderId = orderId;
        BusinessReference = businessReference;
        Level = level;
        ModelVersion = modelVersion;
        InputFingerprint = inputFingerprint;
        BusinessPriorityRevision = businessPriorityRevision;
        CalculationBucketUtc = calculationBucketUtc;
        CalculatedAtUtc = calculatedAtUtc;
        ResultJson = resultJson;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public string BusinessReference { get; private set; } = string.Empty;
    public OrderUrgencyLevel Level { get; private set; }
    public string ModelVersion { get; private set; } = string.Empty;
    public string InputFingerprint { get; private set; } = string.Empty;
    public long BusinessPriorityRevision { get; private set; }
    public DateTimeOffset CalculationBucketUtc { get; private set; }
    public DateTimeOffset CalculatedAtUtc { get; private set; }
    public string ResultJson { get; private set; } = string.Empty;
}

public sealed class OrderUrgencyBusinessPriorityChange : Entity<OrderUrgencyBusinessPriorityChangeId>
{
    private OrderUrgencyBusinessPriorityChange()
    {
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public string BusinessReference { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public BusinessPriorityLevel? PreviousLevel { get; private set; }
    public BusinessPriorityLevel NewLevel { get; private set; }
    public string ChangedBy { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    internal static OrderUrgencyBusinessPriorityChange Record(
        string organizationId,
        string environmentId,
        string orderId,
        string businessReference,
        long revision,
        BusinessPriorityLevel? previousLevel,
        BusinessPriorityLevel newLevel,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc,
        DateTimeOffset? expiresAtUtc) => new()
        {
            OrganizationId = organizationId,
            EnvironmentId = environmentId,
            OrderId = orderId,
            BusinessReference = businessReference,
            Revision = revision,
            PreviousLevel = previousLevel,
            NewLevel = newLevel,
            ChangedBy = changedBy,
            Reason = reason,
            ChangedAtUtc = changedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
}
