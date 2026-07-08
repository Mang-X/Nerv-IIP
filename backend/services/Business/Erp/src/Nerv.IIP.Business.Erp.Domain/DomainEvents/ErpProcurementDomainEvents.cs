using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;

namespace Nerv.IIP.Business.Erp.Domain.DomainEvents;

public sealed record PurchaseRequisitionCreatedDomainEvent(PurchaseRequisition PurchaseRequisition) : IDomainEvent;
public sealed record PurchaseRequisitionConvertedDomainEvent(PurchaseRequisition PurchaseRequisition) : IDomainEvent;
public sealed record PurchaseOrderReleasedDomainEvent(PurchaseOrder PurchaseOrder) : IDomainEvent;
public sealed record PurchaseReceiptRecordedDomainEvent(PurchaseReceipt PurchaseReceipt) : IDomainEvent;
public sealed record PurchaseReceiptInventoryMovementRequestedDomainEvent(PurchaseReceipt PurchaseReceipt, PurchaseReceiptLine Line) : IDomainEvent;
public sealed record SupplierInvoiceMatchedDomainEvent(SupplierInvoice SupplierInvoice) : IDomainEvent;
