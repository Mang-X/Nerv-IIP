using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;

public sealed record LabelPrintBatchCreatedDomainEvent(LabelPrintBatch LabelPrintBatch) : IDomainEvent;

public sealed record LabelPrintBatchCompletedDomainEvent(LabelPrintBatch LabelPrintBatch) : IDomainEvent;

public sealed record LabelScannedDomainEvent(ScanRecord ScanRecord) : IDomainEvent;

public sealed record InventoryMovementRequestedFromScanDomainEvent(ScanRecord ScanRecord) : IDomainEvent;

public sealed record ScanRejectedDomainEvent(ScanRecord ScanRecord) : IDomainEvent;
