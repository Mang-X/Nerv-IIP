using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;

public partial record LabelPrintBatchId : IGuidStronglyTypedId;

public partial record LabelPrintItemId : IGuidStronglyTypedId;

public sealed class LabelPrintBatch : Entity<LabelPrintBatchId>, IAggregateRoot
{
    private LabelPrintBatch()
    {
    }

    private LabelPrintBatch(
        string organizationId,
        string environmentId,
        BarcodeRule rule,
        LabelTemplateId labelTemplateId,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        string labelValuesJson,
        int requestedQuantity)
    {
        Id = new LabelPrintBatchId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        BarcodeRuleId = rule.Id;
        LabelTemplateId = labelTemplateId;
        SourceDocumentType = BarcodeLabelText.Required(sourceDocumentType, nameof(sourceDocumentType)).ToLowerInvariant();
        SourceDocumentId = BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId));
        IdempotencyKey = BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey));
        LabelValuesJson = BarcodeLabelText.Required(labelValuesJson, nameof(labelValuesJson));
        RequestedQuantity = requestedQuantity <= 0
            ? throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "Requested quantity must be positive.")
            : requestedQuantity;
        Status = "completed";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        CompletedAtUtc = CreatedAtUtc;

        for (var sequence = 1; sequence <= requestedQuantity; sequence++)
        {
            Items.Add(LabelPrintItem.Create(sequence, rule.GenerateValue(SourceDocumentType, SourceDocumentId, sequence), null));
        }

        this.AddDomainEvent(new LabelPrintBatchCreatedDomainEvent(this));
        this.AddDomainEvent(new LabelPrintBatchCompletedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public BarcodeRuleId BarcodeRuleId { get; private set; } = null!;
    public LabelTemplateId LabelTemplateId { get; private set; } = null!;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string LabelValuesJson { get; private set; } = string.Empty;
    public int RequestedQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public List<LabelPrintItem> Items { get; private set; } = [];

    public static LabelPrintBatch Create(
        string organizationId,
        string environmentId,
        BarcodeRule rule,
        LabelTemplateId labelTemplateId,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        string labelValuesJson,
        int requestedQuantity)
    {
        return new LabelPrintBatch(organizationId, environmentId, rule, labelTemplateId, sourceDocumentType, sourceDocumentId, idempotencyKey, labelValuesJson, requestedQuantity);
    }

    public bool HasSameIdempotencyPayload(LabelPrintBatch other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && BarcodeRuleId == other.BarcodeRuleId
            && LabelTemplateId == other.LabelTemplateId
            && SourceDocumentType == other.SourceDocumentType
            && SourceDocumentId == other.SourceDocumentId
            && IdempotencyKey == other.IdempotencyKey
            && LabelValuesJson == other.LabelValuesJson
            && RequestedQuantity == other.RequestedQuantity;
    }

    public void EnsureSameIdempotencyPayload(LabelPrintBatch other)
    {
        if (!HasSameIdempotencyPayload(other))
        {
            throw new InvalidOperationException("Print batch idempotency key conflicts with a different payload.");
        }
    }
}

public sealed class LabelPrintItem : Entity<LabelPrintItemId>
{
    private LabelPrintItem()
    {
    }

    private LabelPrintItem(int sequenceNo, string labelValue, string? fileId)
    {
        Id = new LabelPrintItemId(Guid.CreateVersion7());
        SequenceNo = sequenceNo;
        LabelValue = BarcodeLabelText.Required(labelValue, nameof(labelValue));
        FileId = BarcodeLabelText.Optional(fileId);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public LabelPrintBatchId LabelPrintBatchId { get; private set; } = null!;
    public int SequenceNo { get; private set; }
    public string LabelValue { get; private set; } = string.Empty;
    public string? FileId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static LabelPrintItem Create(int sequenceNo, string labelValue, string? fileId)
    {
        return new LabelPrintItem(sequenceNo, labelValue, fileId);
    }
}
