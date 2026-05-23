using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;

public sealed class LabelPrintBatchCreatedIntegrationEventConverter
    : IIntegrationEventConverter<LabelPrintBatchCreatedDomainEvent, LabelPrintBatchCreatedIntegrationEvent>
{
    public LabelPrintBatchCreatedIntegrationEvent Convert(LabelPrintBatchCreatedDomainEvent domainEvent)
    {
        var batch = domainEvent.LabelPrintBatch;
        return new LabelPrintBatchCreatedIntegrationEvent(
            batch.Id,
            batch.BarcodeRuleId,
            batch.LabelTemplateId,
            batch.OrganizationId,
            batch.EnvironmentId,
            batch.SourceDocumentType,
            batch.SourceDocumentId,
            batch.IdempotencyKey,
            batch.RequestedQuantity);
    }
}

public sealed class LabelPrintBatchCompletedIntegrationEventConverter
    : IIntegrationEventConverter<LabelPrintBatchCompletedDomainEvent, LabelPrintBatchCompletedIntegrationEvent>
{
    public LabelPrintBatchCompletedIntegrationEvent Convert(LabelPrintBatchCompletedDomainEvent domainEvent)
    {
        var batch = domainEvent.LabelPrintBatch;
        return new LabelPrintBatchCompletedIntegrationEvent(
            batch.Id,
            batch.OrganizationId,
            batch.EnvironmentId,
            batch.SourceDocumentType,
            batch.SourceDocumentId,
            batch.IdempotencyKey,
            batch.RequestedQuantity);
    }
}

public sealed class LabelScannedIntegrationEventConverter
    : IIntegrationEventConverter<LabelScannedDomainEvent, LabelScannedIntegrationEvent>
{
    public LabelScannedIntegrationEvent Convert(LabelScannedDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        return new LabelScannedIntegrationEvent(
            scan.Id,
            scan.OrganizationId,
            scan.EnvironmentId,
            scan.DeviceCode,
            scan.ScannedValue,
            scan.SourceWorkflow,
            scan.SourceDocumentId,
            scan.IdempotencyKey);
    }
}

public sealed class ScanRejectedIntegrationEventConverter
    : IIntegrationEventConverter<ScanRejectedDomainEvent, ScanRejectedIntegrationEvent>
{
    public ScanRejectedIntegrationEvent Convert(ScanRejectedDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        return new ScanRejectedIntegrationEvent(
            scan.Id,
            scan.OrganizationId,
            scan.EnvironmentId,
            scan.DeviceCode,
            scan.ScannedValue,
            scan.SourceWorkflow,
            scan.SourceDocumentId,
            scan.IdempotencyKey,
            scan.RejectionReason);
    }
}
