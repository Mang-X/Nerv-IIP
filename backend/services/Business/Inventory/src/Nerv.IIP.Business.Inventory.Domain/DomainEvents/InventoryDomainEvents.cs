using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Domain.DomainEvents;

public sealed record StockMovementPostedDomainEvent(StockMovement StockMovement) : IDomainEvent;

public sealed record StockAvailabilityChangedDomainEvent(StockLedger StockLedger) : IDomainEvent;

public sealed record StockCountVarianceConfirmedDomainEvent(StockCountTask StockCountTask, StockMovement AdjustmentMovement) : IDomainEvent;
