namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

public sealed class StockCountRecountRequiredException(string message) : InvalidOperationException(message);
