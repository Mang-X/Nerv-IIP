using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using System.Text.Json;

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

        var labelValues = LabelValueInputs.Parse(labelValuesJson);
        for (var sequence = 1; sequence <= requestedQuantity; sequence++)
        {
            var item = rule.BarcodeType.StartsWith("gs1-", StringComparison.Ordinal)
                ? LabelPrintItem.CreateSerialized(sequence, rule.GenerateGs1Value(SourceDocumentType, labelValues.RequireLotNo(), labelValues.RequireSerialPrefix(), sequence), null)
                : LabelPrintItem.Create(sequence, rule.GenerateValue(SourceDocumentType, SourceDocumentId, sequence), null);
            Items.Add(item);
            if (!string.IsNullOrWhiteSpace(item.SerialNumber))
            {
                EpcisEvents.Add(EpcisEvent.Commissioning(OrganizationId, EnvironmentId, item, SourceDocumentType, SourceDocumentId));
            }
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
    public List<EpcisEvent> EpcisEvents { get; private set; } = [];

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

    private LabelPrintItem(int sequenceNo, string labelValue, string? fileId, string? gtin, string? lotNo, string? serialNumber, string? epcUri)
    {
        Id = new LabelPrintItemId(Guid.CreateVersion7());
        SequenceNo = sequenceNo;
        LabelValue = BarcodeLabelText.Required(labelValue, nameof(labelValue));
        FileId = BarcodeLabelText.Optional(fileId);
        Gtin = BarcodeLabelText.Optional(gtin);
        LotNo = BarcodeLabelText.Optional(lotNo);
        SerialNumber = BarcodeLabelText.Optional(serialNumber);
        EpcUri = BarcodeLabelText.Optional(epcUri);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public LabelPrintBatchId LabelPrintBatchId { get; private set; } = null!;
    public int SequenceNo { get; private set; }
    public string LabelValue { get; private set; } = string.Empty;
    public string? FileId { get; private set; }
    public string? Gtin { get; private set; }
    public string? LotNo { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? EpcUri { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static LabelPrintItem Create(int sequenceNo, string labelValue, string? fileId)
    {
        return new LabelPrintItem(sequenceNo, labelValue, fileId, null, null, null, null);
    }

    internal static LabelPrintItem CreateSerialized(int sequenceNo, Gs1BarcodeValue value, string? fileId)
    {
        return new LabelPrintItem(sequenceNo, value.ToAiString(), fileId, value.Gtin, value.LotNo, value.SerialNumber, value.EpcUri);
    }
}

internal sealed record LabelValueInputs(string? LotNo, string? SerialPrefix)
{
    public string RequireLotNo()
    {
        return BarcodeLabelText.Required(LotNo ?? string.Empty, "lotNo");
    }

    public string RequireSerialPrefix()
    {
        return BarcodeLabelText.Required(SerialPrefix ?? string.Empty, "serialPrefix");
    }

    public static LabelValueInputs Parse(string labelValuesJson)
    {
        using var document = JsonDocument.Parse(BarcodeLabelText.Required(labelValuesJson, nameof(labelValuesJson)));
        var root = document.RootElement;
        var lotNo = root.TryGetProperty("lotNo", out var lotElement)
            ? lotElement.GetString()
            : null;
        var serialPrefix = root.TryGetProperty("serialPrefix", out var serialElement)
            ? serialElement.GetString()
            : null;
        return new LabelValueInputs(
            BarcodeLabelText.Optional(lotNo),
            BarcodeLabelText.Optional(serialPrefix));
    }
}
