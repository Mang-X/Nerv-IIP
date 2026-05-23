using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;

namespace Nerv.IIP.Business.Erp.Domain.DomainEvents;

public sealed record PurchaseRequisitionCreatedDomainEvent(PurchaseRequisition PurchaseRequisition) : IDomainEvent;
public sealed record PurchaseOrderReleasedDomainEvent(PurchaseOrder PurchaseOrder) : IDomainEvent;
public sealed record PurchaseReceiptRecordedDomainEvent(PurchaseReceipt PurchaseReceipt) : IDomainEvent;
