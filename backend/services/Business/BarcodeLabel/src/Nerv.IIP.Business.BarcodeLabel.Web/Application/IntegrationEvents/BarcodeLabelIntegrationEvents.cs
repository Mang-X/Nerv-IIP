using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEvents;

public sealed record LabelPrintBatchCreatedIntegrationEvent(
    LabelPrintBatchId PrintBatchId,
    BarcodeRuleId BarcodeRuleId,
    LabelTemplateId LabelTemplateId,
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int Quantity)
{
    public const string EventName = "barcode.LabelPrintBatchCreated";
}

public sealed record LabelPrintBatchCompletedIntegrationEvent(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int Quantity)
{
    public const string EventName = "barcode.LabelPrintBatchCompleted";
}

public sealed record LabelScannedIntegrationEvent(
    ScanRecordId ScanRecordId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey)
{
    public const string EventName = "barcode.LabelScanned";
}

public sealed record ScanRejectedIntegrationEvent(
    ScanRecordId ScanRecordId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string IdempotencyKey,
    string? RejectionReason)
{
    public const string EventName = "barcode.ScanRejected";
}
