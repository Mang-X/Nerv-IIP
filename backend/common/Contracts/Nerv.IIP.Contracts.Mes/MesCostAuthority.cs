namespace Nerv.IIP.Contracts.Mes;

public static class MesFinishedGoodsCostAuthorityStatuses
{
    public const string Available = "available";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
}

public sealed record MesFinishedGoodsReceiptCostAuthorityRequest(
    string OrganizationId,
    string EnvironmentId,
    string ReceiptRequestNo,
    string WorkOrderId,
    string IdempotencyKey);

public sealed record MesFinishedGoodsReceiptCostAuthorityResponse(
    string Status,
    decimal? CapitalizedUnitCost = null,
    string? ProvenanceEventId = null,
    string? ReasonCode = null);
