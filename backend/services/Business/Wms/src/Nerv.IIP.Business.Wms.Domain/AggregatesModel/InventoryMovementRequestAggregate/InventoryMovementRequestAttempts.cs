namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;

public static class InventoryMovementRequestAttempts
{
    public static IReadOnlyDictionary<string, InventoryMovementRequest> LatestByLine(
        IEnumerable<InventoryMovementRequest> requests)
    {
        return requests
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceDocumentLineId))
            .GroupBy(x => x.SourceDocumentLineId!, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(item => item.CreatedAtUtc)
                    .ThenByDescending(item => item.Id.Id)
                    .First(),
                StringComparer.Ordinal);
    }
}
