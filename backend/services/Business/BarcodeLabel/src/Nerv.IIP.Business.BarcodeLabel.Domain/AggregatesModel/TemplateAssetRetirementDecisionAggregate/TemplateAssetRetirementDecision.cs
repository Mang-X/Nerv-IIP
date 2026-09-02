using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

public partial record TemplateAssetRetirementDecisionId : IGuidStronglyTypedId;

public sealed class TemplateAssetRetirementDecision : Entity<TemplateAssetRetirementDecisionId>, IAggregateRoot
{
    public const string PendingStatus = "pending";
    public const string UnreferencedResult = "unreferenced";
    public const string RequiredPermission = "business.barcodes.template-assets.retire";

    private TemplateAssetRetirementDecision()
    {
    }

    private TemplateAssetRetirementDecision(
        string organizationId,
        string environmentId,
        LabelTemplateId labelTemplateId,
        string templateCode,
        string templateFileId,
        string templateAssetSha256,
        string idempotencyKey,
        string requesterSubject,
        string permission,
        string reason,
        string correlationId)
    {
        Id = new TemplateAssetRetirementDecisionId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        LabelTemplateId = labelTemplateId ?? throw new ArgumentNullException(nameof(labelTemplateId));
        TemplateCode = BarcodeLabelText.Required(templateCode, nameof(templateCode));
        TemplateFileId = BarcodeLabelText.Required(templateFileId, nameof(templateFileId));
        TemplateAssetSha256 = BarcodeLabelText.Required(templateAssetSha256, nameof(templateAssetSha256));
        IdempotencyKey = BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey));
        RequesterSubject = BarcodeLabelText.Required(requesterSubject, nameof(requesterSubject));
        Permission = string.Equals(permission, RequiredPermission, StringComparison.Ordinal)
            ? permission
            : throw new ArgumentException("Retirement permission is not supported.", nameof(permission));
        Reason = BarcodeLabelText.Required(reason, nameof(reason));
        CorrelationId = BarcodeLabelText.Required(correlationId, nameof(correlationId));
        ReferenceResult = UnreferencedResult;
        Status = PendingStatus;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public LabelTemplateId LabelTemplateId { get; private set; } = null!;
    public string TemplateCode { get; private set; } = string.Empty;
    public string TemplateFileId { get; private set; } = string.Empty;
    public string TemplateAssetSha256 { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequesterSubject { get; private set; } = string.Empty;
    public string Permission { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public string ReferenceResult { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TemplateAssetRetirementDecision Create(
        string organizationId,
        string environmentId,
        LabelTemplateId labelTemplateId,
        string templateCode,
        string templateFileId,
        string templateAssetSha256,
        string idempotencyKey,
        string requesterSubject,
        string permission,
        string reason,
        string correlationId) =>
        new(
            organizationId,
            environmentId,
            labelTemplateId,
            templateCode,
            templateFileId,
            templateAssetSha256,
            idempotencyKey,
            requesterSubject,
            permission,
            reason,
            correlationId);

    public bool HasSameRequest(
        string organizationId,
        string environmentId,
        LabelTemplateId labelTemplateId,
        string templateFileId,
        string templateAssetSha256,
        string reason) =>
        OrganizationId == organizationId
        && EnvironmentId == environmentId
        && LabelTemplateId == labelTemplateId
        && TemplateFileId == templateFileId
        && TemplateAssetSha256 == templateAssetSha256
        && Reason == reason;
}
