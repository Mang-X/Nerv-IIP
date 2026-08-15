using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;

public partial record WarehouseTaskActionReceiptId : IGuidStronglyTypedId;

public sealed class WarehouseTaskActionReceipt
    : Entity<WarehouseTaskActionReceiptId>, IAggregateRoot
{
    private WarehouseTaskActionReceipt()
    {
    }

    private WarehouseTaskActionReceipt(
        string organizationId,
        string environmentId,
        WarehouseTaskId warehouseTaskId,
        string action,
        string idempotencyKey,
        string payloadFingerprint,
        string resultStatus,
        long resultVersion,
        decimal resultExecutedQuantity,
        decimal resultDifferenceQuantity)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        WarehouseTaskId = warehouseTaskId;
        Action = WmsText.Required(action, nameof(action));
        IdempotencyKey = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        PayloadFingerprint = WmsText.Required(payloadFingerprint, nameof(payloadFingerprint));
        ResultStatus = WmsText.Required(resultStatus, nameof(resultStatus));
        if (resultVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultVersion),
                resultVersion,
                "Result version must be positive.");
        }

        if (resultExecutedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultExecutedQuantity),
                resultExecutedQuantity,
                "Result executed quantity cannot be negative.");
        }

        if (resultDifferenceQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultDifferenceQuantity),
                resultDifferenceQuantity,
                "Result difference quantity cannot be negative.");
        }

        ResultVersion = resultVersion;
        ResultExecutedQuantity = resultExecutedQuantity;
        ResultDifferenceQuantity = resultDifferenceQuantity;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public WarehouseTaskId WarehouseTaskId { get; private set; } = default!;
    public string Action { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public string ResultStatus { get; private set; } = string.Empty;
    public long ResultVersion { get; private set; }
    public decimal ResultExecutedQuantity { get; private set; }
    public decimal ResultDifferenceQuantity { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static WarehouseTaskActionReceipt Create(
        string organizationId,
        string environmentId,
        WarehouseTaskId warehouseTaskId,
        string action,
        string idempotencyKey,
        string payloadFingerprint,
        string resultStatus,
        long resultVersion,
        decimal resultExecutedQuantity,
        decimal resultDifferenceQuantity)
    {
        return new WarehouseTaskActionReceipt(
            organizationId,
            environmentId,
            warehouseTaskId,
            action,
            idempotencyKey,
            payloadFingerprint,
            resultStatus,
            resultVersion,
            resultExecutedQuantity,
            resultDifferenceQuantity);
    }

    public bool MatchesPayload(string payloadFingerprint) =>
        string.Equals(
            PayloadFingerprint,
            WmsText.Required(payloadFingerprint, nameof(payloadFingerprint)),
            StringComparison.Ordinal);

    public void EnsurePayloadMatches(string payloadFingerprint)
    {
        if (!MatchesPayload(payloadFingerprint))
        {
            throw new InvalidOperationException(
                "The idempotency key was already used with a different warehouse task action payload.");
        }
    }
}
