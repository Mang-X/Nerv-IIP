using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;

public partial record LabelPrintBatchId : IGuidStronglyTypedId;

public partial record LabelPrintItemId : IGuidStronglyTypedId;

public sealed record LabelPrintBatchSnapshot(
    string TemplateFileId,
    string TemplateAssetSha256,
    string VariableSchemaJson,
    string BarcodeType,
    string RendererContractVersion);

public enum TemplateAssetReferenceDisposition
{
    NotTarget,
    Unreachable,
    Reachable,
    Hold,
    Unknown,
}

public sealed class LabelPrintBatch : Entity<LabelPrintBatchId>, IAggregateRoot
{
    private const string Pending = "pending";
    private const string SentToPrinter = "sent-to-printer";
    private const string DeliveryUnknown = "delivery-unknown";
    private const string Printed = "printed";
    private const string Failed = "failed";

    private LabelPrintBatch()
    {
    }

    private LabelPrintBatch(
        string organizationId,
        string environmentId,
        BarcodeRule rule,
        LabelTemplateId labelTemplateId,
        LabelPrintBatchSnapshot? snapshot,
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
        if (snapshot is not null)
        {
            TemplateFileIdSnapshot = BarcodeLabelText.Required(snapshot.TemplateFileId, nameof(snapshot.TemplateFileId));
            TemplateAssetSha256 = BarcodeLabelText.Required(snapshot.TemplateAssetSha256, nameof(snapshot.TemplateAssetSha256));
            VariableSchemaJsonSnapshot = BarcodeLabelText.Required(snapshot.VariableSchemaJson, nameof(snapshot.VariableSchemaJson));
            BarcodeTypeSnapshot = BarcodeLabelText.Required(snapshot.BarcodeType, nameof(snapshot.BarcodeType));
            RendererContractVersion = BarcodeLabelText.Required(snapshot.RendererContractVersion, nameof(snapshot.RendererContractVersion));
            if (!string.Equals(BarcodeTypeSnapshot, rule.BarcodeType, StringComparison.Ordinal))
            {
                throw new ArgumentException("Barcode type snapshot must match the selected barcode rule.", nameof(snapshot));
            }
        }

        SourceDocumentType = BarcodeLabelText.Required(sourceDocumentType, nameof(sourceDocumentType)).ToLowerInvariant();
        SourceDocumentId = BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId));
        IdempotencyKey = BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey));
        LabelValuesJson = BarcodeLabelText.Required(labelValuesJson, nameof(labelValuesJson));
        RequestedQuantity = requestedQuantity <= 0
            ? throw new ArgumentOutOfRangeException(nameof(requestedQuantity), "Requested quantity must be positive.")
            : requestedQuantity;
        Status = Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public BarcodeRuleId BarcodeRuleId { get; private set; } = null!;
    public LabelTemplateId LabelTemplateId { get; private set; } = null!;
    public string? TemplateFileIdSnapshot { get; private set; }
    public string? TemplateAssetSha256 { get; private set; }
    public string? VariableSchemaJsonSnapshot { get; private set; }
    public string? BarcodeTypeSnapshot { get; private set; }
    public string? RendererContractVersion { get; private set; }
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string LabelValuesJson { get; private set; } = string.Empty;
    public int RequestedQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? PrinterId { get; private set; }
    public string? PrintJobId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public List<LabelPrintItem> Items { get; private set; } = [];
    public List<EpcisEvent> EpcisEvents { get; private set; } = [];

    internal static LabelPrintBatch ReconstituteHistorical(
        string organizationId,
        string environmentId,
        BarcodeRule rule,
        LabelTemplateId labelTemplateId,
        LabelPrintBatchSnapshot snapshot,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        string labelValuesJson,
        int requestedQuantity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var batch = new LabelPrintBatch(
            organizationId,
            environmentId,
            rule,
            labelTemplateId,
            snapshot,
            sourceDocumentType,
            sourceDocumentId,
            idempotencyKey,
            labelValuesJson,
            requestedQuantity);
        batch.AddHistoricalItems(rule, LabelValueInputs.Parse(labelValuesJson));
        batch.CompleteCreation();
        return batch;
    }

    public static LabelPrintBatch CreateWithAllocatedSerialNumbers(
        string organizationId,
        string environmentId,
        BarcodeRule rule,
        LabelTemplateId labelTemplateId,
        LabelPrintBatchSnapshot snapshot,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        string labelValuesJson,
        int requestedQuantity,
        IReadOnlyList<string> allocatedSerialNumbers)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(allocatedSerialNumbers);
        var batch = new LabelPrintBatch(
            organizationId,
            environmentId,
            rule,
            labelTemplateId,
            snapshot,
            sourceDocumentType,
            sourceDocumentId,
            idempotencyKey,
            labelValuesJson,
            requestedQuantity);
        batch.AddAllocatedItems(
            rule,
            LabelValueInputs.Parse(labelValuesJson),
            NormalizeAllocatedSerialNumbers(allocatedSerialNumbers, requestedQuantity));
        batch.CompleteCreation();
        return batch;
    }

    internal static LabelPrintBatch CreateLegacyWithoutReplaySnapshot(
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
        var batch = new LabelPrintBatch(
            organizationId,
            environmentId,
            rule,
            labelTemplateId,
            null,
            sourceDocumentType,
            sourceDocumentId,
            idempotencyKey,
            labelValuesJson,
            requestedQuantity);
        batch.AddHistoricalItems(rule, LabelValueInputs.Parse(labelValuesJson));
        batch.CompleteCreation();
        return batch;
    }

    public bool HasCompleteReplaySnapshot =>
        !string.IsNullOrWhiteSpace(TemplateFileIdSnapshot)
        && !string.IsNullOrWhiteSpace(TemplateAssetSha256)
        && !string.IsNullOrWhiteSpace(VariableSchemaJsonSnapshot)
        && !string.IsNullOrWhiteSpace(BarcodeTypeSnapshot)
        && !string.IsNullOrWhiteSpace(RendererContractVersion);

    public void EnsureCompleteReplaySnapshot()
    {
        if (!HasCompleteReplaySnapshot)
        {
            throw new InvalidOperationException("Print batch does not contain a complete replay snapshot.");
        }
    }

    public TemplateAssetReferenceDisposition GetTemplateAssetReferenceDisposition(
        LabelTemplateId labelTemplateId,
        string templateFileId,
        string templateAssetSha256)
    {
        var hasAnyReplayFact = TemplateFileIdSnapshot is not null
            || TemplateAssetSha256 is not null
            || VariableSchemaJsonSnapshot is not null
            || BarcodeTypeSnapshot is not null
            || RendererContractVersion is not null;
        if (!hasAnyReplayFact)
        {
            return TemplateAssetReferenceDisposition.NotTarget;
        }

        if (!HasCompleteReplaySnapshot)
        {
            return string.IsNullOrWhiteSpace(TemplateFileIdSnapshot) || TemplateFileIdSnapshot == templateFileId
                ? TemplateAssetReferenceDisposition.Unknown
                : TemplateAssetReferenceDisposition.NotTarget;
        }

        if (TemplateFileIdSnapshot != templateFileId)
        {
            return TemplateAssetReferenceDisposition.NotTarget;
        }

        if (LabelTemplateId != labelTemplateId || TemplateAssetSha256 != templateAssetSha256)
        {
            return TemplateAssetReferenceDisposition.Unknown;
        }

        if (Status == DeliveryUnknown)
        {
            return TemplateAssetReferenceDisposition.Hold;
        }

        if (Status is Pending or Failed)
        {
            return TemplateAssetReferenceDisposition.Reachable;
        }

        if (Status is not (SentToPrinter or Printed) || Items.Count == 0)
        {
            return TemplateAssetReferenceDisposition.Unknown;
        }

        var itemDispositions = Items.Select(item => item.GetTemplateAssetReferenceDisposition()).ToArray();
        if (itemDispositions.Any(disposition => disposition == TemplateAssetReferenceDisposition.Unknown))
        {
            return TemplateAssetReferenceDisposition.Unknown;
        }

        return itemDispositions.Any(disposition => disposition == TemplateAssetReferenceDisposition.Reachable)
            ? TemplateAssetReferenceDisposition.Reachable
            : TemplateAssetReferenceDisposition.Unreachable;
    }

    public void EnsureCanBeDispatched()
    {
        if (Status is not (Pending or Failed))
        {
            throw Reject(
                Status == DeliveryUnknown
                    ? LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeDispatched
                    : LabelPrintLifecycleRejectionReason.BatchCannotBeDispatched,
                $"Print batch in status '{Status}' cannot be dispatched.");
        }
    }

    public bool HasSameIdempotencyPayload(LabelPrintBatch other)
    {
        return OrganizationId == other.OrganizationId
            && EnvironmentId == other.EnvironmentId
            && BarcodeRuleId == other.BarcodeRuleId
            && LabelTemplateId == other.LabelTemplateId
            && TemplateFileIdSnapshot == other.TemplateFileIdSnapshot
            && TemplateAssetSha256 == other.TemplateAssetSha256
            && VariableSchemaJsonSnapshot == other.VariableSchemaJsonSnapshot
            && BarcodeTypeSnapshot == other.BarcodeTypeSnapshot
            && RendererContractVersion == other.RendererContractVersion
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

    public bool HasSameAllocationRequest(
        BarcodeRuleId barcodeRuleId,
        LabelTemplateId labelTemplateId,
        string sourceDocumentType,
        string sourceDocumentId,
        string idempotencyKey,
        string labelValuesJson,
        int requestedQuantity)
    {
        return BarcodeRuleId == barcodeRuleId
            && LabelTemplateId == labelTemplateId
            && SourceDocumentType == BarcodeLabelText.Required(sourceDocumentType, nameof(sourceDocumentType)).ToLowerInvariant()
            && SourceDocumentId == BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId))
            && IdempotencyKey == BarcodeLabelText.Required(idempotencyKey, nameof(idempotencyKey))
            && LabelValuesJson == BarcodeLabelText.Required(labelValuesJson, nameof(labelValuesJson))
            && RequestedQuantity == requestedQuantity;
    }

    public void RecordSentToPrinter(string printerId, string printJobId)
    {
        if (Status is not (Pending or Failed))
        {
            throw Reject(
                LabelPrintLifecycleRejectionReason.BatchCannotBeDispatched,
                $"Print batch in status '{Status}' cannot be sent to a printer.");
        }

        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = BarcodeLabelText.Required(printJobId, nameof(printJobId));
        FailureReason = null;
        CompletedAtUtc = null;
        Status = SentToPrinter;
    }

    public void RecordReprintSentToPrinter(string printerId, string printJobId)
    {
        EnsureCanRecordReprintResult();
        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = BarcodeLabelText.Required(printJobId, nameof(printJobId));
        FailureReason = null;
    }

    public void RecordPrinted()
    {
        if (Status != SentToPrinter)
        {
            throw new InvalidOperationException($"Print batch in status '{Status}' cannot be marked printed.");
        }

        foreach (var item in Items)
        {
            item.MarkPrinted();
        }

        Status = Printed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(new LabelPrintBatchCompletedDomainEvent(this));
    }

    public void RecordDeliveryUnknown(string printerId, string printJobId, string failureReason)
    {
        if (Status is not (Pending or Failed))
        {
            throw new InvalidOperationException($"Print batch in status '{Status}' cannot record unknown delivery.");
        }

        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = BarcodeLabelText.Required(printJobId, nameof(printJobId));
        FailureReason = BarcodeLabelText.Required(failureReason, nameof(failureReason));
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Status = DeliveryUnknown;
    }

    public void RecordReprintDeliveryUnknown(string printerId, string printJobId, string failureReason)
    {
        EnsureCanRecordReprintResult();
        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = BarcodeLabelText.Required(printJobId, nameof(printJobId));
        FailureReason = BarcodeLabelText.Required(failureReason, nameof(failureReason));
    }

    public void RecordPrintFailed(string printerId, string failureReason)
    {
        if (Status is not (Pending or Failed))
        {
            throw new InvalidOperationException($"Print batch in status '{Status}' cannot be marked failed.");
        }

        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = null;
        FailureReason = BarcodeLabelText.Required(failureReason, nameof(failureReason));
        Status = Failed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordReprintFailed(string printerId, string failureReason)
    {
        EnsureCanRecordReprintResult();
        PrinterId = BarcodeLabelText.Required(printerId, nameof(printerId));
        PrintJobId = null;
        FailureReason = BarcodeLabelText.Required(failureReason, nameof(failureReason));
    }

    public void ReprintItem(int sequenceNo)
    {
        FindItem(sequenceNo).MarkReprinted();
    }

    public void EnsureItemCanBeReprinted(int sequenceNo)
    {
        if (Status is not (SentToPrinter or Printed))
        {
            throw Reject(
                Status switch
                {
                    DeliveryUnknown => LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeReprinted,
                    Failed => LabelPrintLifecycleRejectionReason.FailedBatchRequiresDispatch,
                    _ => LabelPrintLifecycleRejectionReason.BatchCannotBeReprinted,
                },
                $"Print batch in status '{Status}' cannot dispatch a reprint.");
        }

        FindItem(sequenceNo).EnsureCanBeRedispatched();
    }

    public void VoidItem(int sequenceNo, string voidReason)
    {
        FindItem(sequenceNo).Void(voidReason);
    }

    public void ConsumeItem(string labelValue)
    {
        var item = Items.SingleOrDefault(x => x.LabelValue == BarcodeLabelText.Required(labelValue, nameof(labelValue)));
        item?.Consume();
    }

    public void ConsumeItem(LabelPrintItemId itemId)
    {
        Items.SingleOrDefault(x => x.Id == itemId)?.Consume();
    }

    private LabelPrintItem FindItem(int sequenceNo)
    {
        return Items.SingleOrDefault(x => x.SequenceNo == sequenceNo)
            ?? throw Reject(
                LabelPrintLifecycleRejectionReason.PrintItemNotFound,
                $"Print item not found, SequenceNo = {sequenceNo}.");
    }

    private void EnsureCanRecordReprintResult()
    {
        if (Status is not (SentToPrinter or Printed))
        {
            throw new InvalidOperationException($"Print batch in status '{Status}' cannot record a reprint result.");
        }
    }

    private static LabelPrintLifecycleRejectedException Reject(
        LabelPrintLifecycleRejectionReason reason,
        string message) =>
        new(reason, message);

    private LabelPrintItem CreateLegacyItem(BarcodeRule rule, LabelValueInputs labelValues, int sequence)
    {
        return rule.BarcodeType.StartsWith("gs1-", StringComparison.Ordinal)
            ? LabelPrintItem.CreateSerialized(
                OrganizationId,
                EnvironmentId,
                sequence,
                rule.GenerateGs1Value(SourceDocumentType, labelValues.RequireLotNo(), labelValues.RequireSerialPrefix(), sequence),
                null)
            : LabelPrintItem.Create(
                OrganizationId,
                EnvironmentId,
                sequence,
                rule.GenerateValue(SourceDocumentType, SourceDocumentId, sequence),
                null);
    }

    private void AddHistoricalItems(BarcodeRule rule, LabelValueInputs labelValues)
    {
        for (var sequence = 1; sequence <= RequestedQuantity; sequence++)
        {
            AddItem(CreateLegacyItem(rule, labelValues, sequence));
        }
    }

    private void AddAllocatedItems(
        BarcodeRule rule,
        LabelValueInputs labelValues,
        IReadOnlyList<string> serialNumbers)
    {
        for (var sequence = 1; sequence <= RequestedQuantity; sequence++)
        {
            AddItem(CreateAllocatedItem(rule, labelValues, sequence, serialNumbers[sequence - 1]));
        }
    }

    private void AddItem(LabelPrintItem item)
    {
        Items.Add(item);
        if (!string.IsNullOrWhiteSpace(item.SerialNumber))
        {
            EpcisEvents.Add(EpcisEvent.Commissioning(
                OrganizationId,
                EnvironmentId,
                item,
                SourceDocumentType,
                SourceDocumentId));
        }
    }

    private void CompleteCreation() =>
        this.AddDomainEvent(new LabelPrintBatchCreatedDomainEvent(this));

    private LabelPrintItem CreateAllocatedItem(
        BarcodeRule rule,
        LabelValueInputs labelValues,
        int sequence,
        string serialNumber)
    {
        return rule.BarcodeType.StartsWith("gs1-", StringComparison.Ordinal)
            ? LabelPrintItem.CreateSerialized(
                OrganizationId,
                EnvironmentId,
                sequence,
                rule.GenerateGs1Value(SourceDocumentType, labelValues.RequireLotNo(), serialNumber),
                null)
            : LabelPrintItem.CreateSerializedPlain(
                OrganizationId,
                EnvironmentId,
                sequence,
                rule.GenerateSerializedValue(SourceDocumentType, serialNumber),
                serialNumber,
                null);
    }

    private static IReadOnlyList<string> NormalizeAllocatedSerialNumbers(
        IReadOnlyList<string> serialNumbers,
        int requestedQuantity)
    {
        if (serialNumbers.Count != requestedQuantity)
        {
            throw new ArgumentException("Allocated serial count must equal requested quantity.", nameof(serialNumbers));
        }

        var normalized = serialNumbers
            .Select(serialNumber => BarcodeLabelText.Required(serialNumber, nameof(serialNumbers)))
            .ToArray();
        if (normalized.Any(serialNumber => serialNumber.Length > 150))
        {
            throw new ArgumentException("Allocated serial numbers cannot exceed 150 characters.", nameof(serialNumbers));
        }

        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new ArgumentException("Allocated serial numbers must be unique.", nameof(serialNumbers));
        }

        return normalized;
    }
}

public sealed class LabelPrintItem : Entity<LabelPrintItemId>
{
    private const string Created = "created";
    private const string Printed = "printed";
    private const string Reprinted = "reprinted";
    private const string Voided = "voided";
    private const string Consumed = "consumed";

    private LabelPrintItem()
    {
    }

    private LabelPrintItem(
        string organizationId,
        string environmentId,
        int sequenceNo,
        string labelValue,
        string? fileId,
        string? gtin,
        string? lotNo,
        string? serialNumber,
        string? epcUri)
    {
        Id = new LabelPrintItemId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        SequenceNo = sequenceNo;
        LabelValue = BarcodeLabelText.Required(labelValue, nameof(labelValue));
        FileId = BarcodeLabelText.Optional(fileId);
        Gtin = BarcodeLabelText.Optional(gtin);
        LotNo = BarcodeLabelText.Optional(lotNo);
        SerialNumber = BarcodeLabelText.Optional(serialNumber);
        EpcUri = BarcodeLabelText.Optional(epcUri);
        Status = Created;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public LabelPrintBatchId LabelPrintBatchId { get; private set; } = null!;
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public int SequenceNo { get; private set; }
    public string LabelValue { get; private set; } = string.Empty;
    public string? FileId { get; private set; }
    public string? Gtin { get; private set; }
    public string? LotNo { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? EpcUri { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? VoidReason { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static LabelPrintItem Create(
        string organizationId,
        string environmentId,
        int sequenceNo,
        string labelValue,
        string? fileId)
    {
        return new LabelPrintItem(organizationId, environmentId, sequenceNo, labelValue, fileId, null, null, null, null);
    }

    internal static LabelPrintItem CreateSerialized(
        string organizationId,
        string environmentId,
        int sequenceNo,
        Gs1BarcodeValue value,
        string? fileId)
    {
        return new LabelPrintItem(organizationId, environmentId, sequenceNo, value.ToAiString(), fileId, value.Gtin, value.LotNo, value.SerialNumber, value.EpcUri);
    }

    internal static LabelPrintItem CreateSerializedPlain(
        string organizationId,
        string environmentId,
        int sequenceNo,
        string labelValue,
        string serialNumber,
        string? fileId)
    {
        return new LabelPrintItem(organizationId, environmentId, sequenceNo, labelValue, fileId, null, null, serialNumber, null);
    }

    internal void MarkPrinted()
    {
        if (Status == Voided)
        {
            return;
        }

        if (Status != Created)
        {
            return;
        }

        Status = Printed;
    }

    internal void MarkReprinted()
    {
        EnsureCanBeReprinted();
        Status = Reprinted;
    }

    internal void EnsureCanBeReprinted()
    {
        EnsureCanBeRedispatched();

        if (Status is not (Printed or Reprinted))
        {
            throw new InvalidOperationException($"Label in status '{Status}' cannot be reprinted.");
        }
    }

    internal void EnsureCanBeRedispatched()
    {
        if (Status == Voided)
        {
            throw new LabelPrintLifecycleRejectedException(
                LabelPrintLifecycleRejectionReason.PrintItemVoided,
                "Voided labels cannot be reprinted.");
        }

        if (Status == Consumed)
        {
            throw new LabelPrintLifecycleRejectedException(
                LabelPrintLifecycleRejectionReason.PrintItemConsumed,
                "Consumed labels cannot be reprinted.");
        }
    }

    internal void Void(string voidReason)
    {
        if (Status == Consumed)
        {
            throw new LabelPrintLifecycleRejectedException(
                LabelPrintLifecycleRejectionReason.ConsumedPrintItemCannotBeVoided,
                "Consumed labels cannot be voided.");
        }

        if (Status == Voided)
        {
            return;
        }

        VoidReason = BarcodeLabelText.Required(voidReason, nameof(voidReason));
        VoidedAtUtc = DateTimeOffset.UtcNow;
        Status = Voided;
    }

    internal void Consume()
    {
        if (Status == Voided)
        {
            throw new InvalidOperationException("Voided labels cannot be consumed.");
        }

        if (Status is not (Printed or Reprinted))
        {
            return;
        }

        Status = Consumed;
        ConsumedAtUtc = DateTimeOffset.UtcNow;
    }

    internal TemplateAssetReferenceDisposition GetTemplateAssetReferenceDisposition() =>
        Status switch
        {
            Created or Printed or Reprinted => TemplateAssetReferenceDisposition.Reachable,
            Voided or Consumed => TemplateAssetReferenceDisposition.Unreachable,
            _ => TemplateAssetReferenceDisposition.Unknown,
        };
}

public enum LabelPrintLifecycleRejectionReason
{
    BatchCannotBeDispatched,
    BatchDeliveryUnknownCannotBeDispatched,
    BatchCannotBeReprinted,
    BatchDeliveryUnknownCannotBeReprinted,
    FailedBatchRequiresDispatch,
    PrintItemNotFound,
    PrintItemVoided,
    PrintItemConsumed,
    ConsumedPrintItemCannotBeVoided,
}

public sealed class LabelPrintLifecycleRejectedException(
    LabelPrintLifecycleRejectionReason reason,
    string message) : InvalidOperationException(message)
{
    public LabelPrintLifecycleRejectionReason Reason { get; } = reason;
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
