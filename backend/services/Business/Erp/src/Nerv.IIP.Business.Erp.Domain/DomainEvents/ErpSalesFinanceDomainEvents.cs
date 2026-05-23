using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;

namespace Nerv.IIP.Business.Erp.Domain.DomainEvents;

public sealed record DeliveryOrderReleasedDomainEvent(DeliveryOrder DeliveryOrder) : IDomainEvent;
public sealed record AccountPayableCreatedDomainEvent(AccountPayable AccountPayable) : IDomainEvent;
public sealed record AccountReceivableCreatedDomainEvent(AccountReceivable AccountReceivable) : IDomainEvent;
public sealed record CostCandidateCreatedDomainEvent(CostCandidate CostCandidate) : IDomainEvent;
public sealed record JournalVoucherPostedDomainEvent(JournalVoucher JournalVoucher) : IDomainEvent;
