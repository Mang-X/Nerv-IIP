using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;

public partial record EpcisEventId : IGuidStronglyTypedId;

public sealed class EpcisEvent : Entity<EpcisEventId>
{
    private EpcisEvent()
    {
    }

    private EpcisEvent(
        string organizationId,
        string environmentId,
        string eventType,
        string action,
        string businessStep,
        string disposition,
        string labelValue,
        string? gtin,
        string? lotNo,
        string? serialNumber,
        string? epcUri,
        string sourceWorkflow,
        string sourceDocumentId,
        LabelPrintBatchId? labelPrintBatchId,
        LabelPrintItemId? labelPrintItemId,
        ScanRecordId? scanRecordId)
    {
        Id = new EpcisEventId(Guid.CreateVersion7());
        OrganizationId = BarcodeLabelText.Required(organizationId, nameof(organizationId));
        EnvironmentId = BarcodeLabelText.Required(environmentId, nameof(environmentId));
        EventType = BarcodeLabelText.Required(eventType, nameof(eventType));
        Action = BarcodeLabelText.Required(action, nameof(action));
        BusinessStep = BarcodeLabelText.Required(businessStep, nameof(businessStep));
        Disposition = BarcodeLabelText.Required(disposition, nameof(disposition));
        LabelValue = BarcodeLabelText.Required(labelValue, nameof(labelValue));
        Gtin = BarcodeLabelText.Optional(gtin);
        LotNo = BarcodeLabelText.Optional(lotNo);
        SerialNumber = BarcodeLabelText.Optional(serialNumber);
        EpcUri = BarcodeLabelText.Optional(epcUri);
        SourceWorkflow = BarcodeLabelText.Required(sourceWorkflow, nameof(sourceWorkflow));
        SourceDocumentId = BarcodeLabelText.Required(sourceDocumentId, nameof(sourceDocumentId));
        LabelPrintBatchId = labelPrintBatchId;
        LabelPrintItemId = labelPrintItemId;
        ScanRecordId = scanRecordId;
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string BusinessStep { get; private set; } = string.Empty;
    public string Disposition { get; private set; } = string.Empty;
    public string LabelValue { get; private set; } = string.Empty;
    public string? Gtin { get; private set; }
    public string? LotNo { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? EpcUri { get; private set; }
    public string SourceWorkflow { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public LabelPrintBatchId? LabelPrintBatchId { get; private set; }
    public LabelPrintItemId? LabelPrintItemId { get; private set; }
    public ScanRecordId? ScanRecordId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static EpcisEvent Commissioning(
        string organizationId,
        string environmentId,
        LabelPrintItem item,
        string sourceWorkflow,
        string sourceDocumentId)
    {
        return new EpcisEvent(
            organizationId,
            environmentId,
            "commissioning",
            "ADD",
            "commissioning",
            "active",
            item.LabelValue,
            item.Gtin,
            item.LotNo,
            item.SerialNumber,
            item.EpcUri,
            sourceWorkflow,
            sourceDocumentId,
            null,
            item.Id,
            null);
    }

    public static EpcisEvent ObjectEvent(
        string organizationId,
        string environmentId,
        ScanRecord scanRecord)
    {
        return new EpcisEvent(
            organizationId,
            environmentId,
            "objectEvent",
            "OBSERVE",
            scanRecord.SourceWorkflow,
            "in_progress",
            scanRecord.ScannedValue,
            scanRecord.Gtin,
            scanRecord.LotNo,
            scanRecord.SerialNumber,
            scanRecord.EpcUri,
            scanRecord.SourceWorkflow,
            scanRecord.SourceDocumentId,
            null,
            null,
            scanRecord.Id);
    }
}
