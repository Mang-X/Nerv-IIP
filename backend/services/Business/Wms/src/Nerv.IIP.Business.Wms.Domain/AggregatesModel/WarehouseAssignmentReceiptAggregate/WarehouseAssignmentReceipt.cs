namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseAssignmentReceiptAggregate;

public partial record WarehouseAssignmentReceiptId : IGuidStronglyTypedId;

public sealed class WarehouseAssignmentReceipt
    : Entity<WarehouseAssignmentReceiptId>, IAggregateRoot
{
    private WarehouseAssignmentReceipt()
    {
    }

    private WarehouseAssignmentReceipt(
        string organizationId,
        string environmentId,
        string resourceCategory,
        string resourceId,
        string idempotencyKey,
        string payloadFingerprint,
        string siteCode,
        string poolCode,
        string? operatorPrincipalId,
        string assignedByPrincipalId,
        long resultVersion)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        ResourceCategory = WmsText.Required(resourceCategory, nameof(resourceCategory));
        ResourceId = WmsText.Required(resourceId, nameof(resourceId));
        IdempotencyKey = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        PayloadFingerprint = WmsText.Required(
            payloadFingerprint,
            nameof(payloadFingerprint));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        PoolCode = WmsText.Required(poolCode, nameof(poolCode));
        OperatorPrincipalId = WmsText.Optional(operatorPrincipalId);
        AssignedByPrincipalId = WmsText.Required(
            assignedByPrincipalId,
            nameof(assignedByPrincipalId));
        if (resultVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultVersion),
                resultVersion,
                "Assignment result version must be positive.");
        }

        ResultVersion = resultVersion;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ResourceCategory { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayloadFingerprint { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string PoolCode { get; private set; } = string.Empty;
    public string? OperatorPrincipalId { get; private set; }
    public string AssignedByPrincipalId { get; private set; } = string.Empty;
    public long ResultVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static WarehouseAssignmentReceipt Create(
        string organizationId,
        string environmentId,
        string resourceCategory,
        string resourceId,
        string idempotencyKey,
        string payloadFingerprint,
        string siteCode,
        string poolCode,
        string? operatorPrincipalId,
        string assignedByPrincipalId,
        long resultVersion) =>
        new(
            organizationId,
            environmentId,
            resourceCategory,
            resourceId,
            idempotencyKey,
            payloadFingerprint,
            siteCode,
            poolCode,
            operatorPrincipalId,
            assignedByPrincipalId,
            resultVersion);

    public bool MatchesPayload(string payloadFingerprint) =>
        string.Equals(
            PayloadFingerprint,
            WmsText.Required(payloadFingerprint, nameof(payloadFingerprint)),
            StringComparison.Ordinal);
}
