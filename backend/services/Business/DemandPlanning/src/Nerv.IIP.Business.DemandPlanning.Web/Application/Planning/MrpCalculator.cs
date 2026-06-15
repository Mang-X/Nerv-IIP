namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public sealed record MrpCalculationInput(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<DemandSnapshot> Demands,
    IReadOnlyCollection<InventoryAvailabilitySnapshot> Availability,
    IReadOnlyCollection<ProductionVersionSnapshot> ProductionVersions,
    IReadOnlyCollection<BomComponentSnapshot> BomComponents,
    IReadOnlyCollection<ScheduledReceiptSnapshot> ScheduledReceipts,
    IReadOnlyCollection<PlanningParameterSnapshot> PlanningParameters);

public sealed record DemandSnapshot(
    string DemandSourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate);

public sealed record InventoryAvailabilitySnapshot(
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal AvailableQuantity);

public sealed record ProductionVersionSnapshot(
    string ParentSkuCode,
    string ProductionVersionReference,
    string ManufacturingBomReference,
    string RoutingReference,
    decimal? LotSizeMin = null,
    decimal? LotSizeMax = null,
    decimal? LotSizeMultiple = null);

public sealed record BomComponentSnapshot(
    string ParentSkuCode,
    string ComponentSkuCode,
    string ComponentUomCode,
    decimal QuantityPerParent);

public sealed record ScheduledReceiptSnapshot(
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly ExpectedReceiptDate,
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId);

public sealed record PlanningParameterSnapshot(
    string SkuCode,
    string UomCode,
    string SiteCode,
    int LeadTimeDays,
    decimal SafetyStockQuantity,
    decimal? LotSizeMin,
    decimal? LotSizeMax,
    decimal? LotSizeMultiple);

public sealed record CalculatedPlanningSuggestion(
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    DateOnly ReleaseDate,
    string ReasonCode,
    IReadOnlyCollection<CalculatedPeggingLink> PeggingLinks);

public sealed record CalculatedPeggingLink(
    string PeggingType,
    string DemandSourceReference,
    string ParentSkuCode,
    string? ComponentSkuCode,
    decimal Quantity,
    string? ProductionVersionReference,
    string? ManufacturingBomReference,
    string? RoutingReference);

public static class MrpCalculator
{
    public static IReadOnlyCollection<CalculatedPlanningSuggestion> Calculate(MrpCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var availability = input.Availability
            .GroupBy(x => ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.AvailableQuantity));
        var scheduledReceipts = input.ScheduledReceipts
            .GroupBy(x => ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.ExpectedReceiptDate)
                    .ThenBy(y => y.SourceSystem, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(y => y.SourceDocumentType, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(y => y.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
                    .Select(y => new ScheduledReceiptState(y))
                    .ToList());
        var planningParameters = input.PlanningParameters
            .GroupBy(x => ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(x => x.Key, x => x.First());
        var productionVersions = input.ProductionVersions
            .GroupBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var componentsByParent = input.BomComponents
            .GroupBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<CalculatedPlanningSuggestion>();
        var pending = input.Demands
            .Where(x => x.DueDate >= input.HorizonStart && x.DueDate <= input.HorizonEnd)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DemandSourceReference, StringComparer.Ordinal)
            .Select(x => new Requirement(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.Quantity,
                x.DueDate,
                [new DemandPegging(x.DemandSourceReference, x.SkuCode, null, x.Quantity)],
                [Normalize(x.SkuCode)]))
            .ToList();

        while (pending.Count > 0)
        {
            var current = pending;
            pending = [];
            foreach (var group in current
                .GroupBy(x => new RequirementBucket(ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode), x.RequiredDate))
                .OrderBy(x => x.Key.RequiredDate)
                .ThenBy(x => x.Key.Key.SkuCode, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var key = group.Key.Key;
                var grossRequirement = group.Sum(x => x.Quantity);
                var demandPegging = group
                    .SelectMany(x => x.DemandPegging)
                    .GroupBy(x => $"{x.DemandSourceReference}\u001f{x.ParentSkuCode}\u001f{x.ComponentSkuCode}", StringComparer.Ordinal)
                    .Select(x => new DemandPegging(
                        x.First().DemandSourceReference,
                        x.First().ParentSkuCode,
                        x.First().ComponentSkuCode,
                        x.Sum(y => y.Quantity)))
                    .ToArray();
                planningParameters.TryGetValue(key, out var planningParameter);
                productionVersions.TryGetValue(first.SkuCode, out var version);
                var supply = ConsumeSupply(
                    key,
                    grossRequirement + Math.Max(0, planningParameter?.SafetyStockQuantity ?? 0m),
                    group.Key.RequiredDate,
                    availability,
                    scheduledReceipts);
                var netRequirement = supply.Shortage;
                if (netRequirement <= 0)
                {
                    continue;
                }

                var plannedQuantities = ApplyLotSizing(
                    netRequirement,
                    planningParameter?.LotSizeMin ?? version?.LotSizeMin,
                    planningParameter?.LotSizeMax ?? version?.LotSizeMax,
                    planningParameter?.LotSizeMultiple ?? version?.LotSizeMultiple);
                var plannedQuantity = plannedQuantities.Sum();
                var releaseDate = group.Key.RequiredDate.AddDays(-Math.Max(0, planningParameter?.LeadTimeDays ?? 0));
                var isMakeItem = version is not null;
                var suggestionType = isMakeItem ? "planned-work-order" : "planned-purchase";
                var reasonCode = isMakeItem ? "net-requirement" : "component-net-requirement";
                var peggingLinks = demandPegging
                    .Select(x => new CalculatedPeggingLink(
                        "demand",
                        x.DemandSourceReference,
                        x.ParentSkuCode,
                        x.ComponentSkuCode,
                        x.Quantity,
                        version?.ProductionVersionReference,
                        version?.ManufacturingBomReference,
                        version?.RoutingReference))
                    .Concat(supply.UsedReceipts.Select(x => new CalculatedPeggingLink(
                        "scheduled-receipt",
                        $"{x.SourceSystem}:{x.SourceDocumentType}:{x.SourceDocumentId}",
                        first.SkuCode,
                        null,
                        x.Quantity,
                        version?.ProductionVersionReference,
                        version?.ManufacturingBomReference,
                        version?.RoutingReference)))
                    .ToArray();
                suggestions.AddRange(plannedQuantities.Select(quantity => new CalculatedPlanningSuggestion(
                    suggestionType,
                    first.SkuCode,
                    first.UomCode,
                    first.SiteCode,
                    quantity,
                    group.Key.RequiredDate,
                    releaseDate,
                    reasonCode,
                    peggingLinks)));

                if (!isMakeItem || !componentsByParent.TryGetValue(first.SkuCode, out var components))
                {
                    continue;
                }

                foreach (var component in components.OrderBy(x => x.ComponentSkuCode, StringComparer.OrdinalIgnoreCase))
                {
                    var normalizedComponent = Normalize(component.ComponentSkuCode);
                    if (first.Path.Contains(normalizedComponent, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    pending.Add(new Requirement(
                        component.ComponentSkuCode,
                        component.ComponentUomCode,
                        first.SiteCode,
                        plannedQuantity * component.QuantityPerParent,
                        releaseDate,
                        demandPegging
                            .Select(x => x with
                            {
                                ParentSkuCode = first.SkuCode,
                                ComponentSkuCode = component.ComponentSkuCode,
                                Quantity = plannedQuantity * component.QuantityPerParent,
                            })
                            .ToArray(),
                        [.. first.Path, normalizedComponent]));
                }
            }
        }

        return suggestions;
    }

    private static SupplyConsumption ConsumeSupply(
        ItemKey key,
        decimal requiredQuantity,
        DateOnly requiredDate,
        IDictionary<ItemKey, decimal> availability,
        IReadOnlyDictionary<ItemKey, List<ScheduledReceiptState>> scheduledReceipts)
    {
        var remainingRequirement = requiredQuantity;
        var availableQuantity = availability.TryGetValue(key, out var available) ? available : 0m;
        var usedAvailable = Math.Min(availableQuantity, remainingRequirement);
        if (usedAvailable > 0)
        {
            availability[key] = availableQuantity - usedAvailable;
            remainingRequirement -= usedAvailable;
        }

        var usedReceipts = new List<UsedScheduledReceipt>();
        if (remainingRequirement > 0 && scheduledReceipts.TryGetValue(key, out var receipts))
        {
            foreach (var receipt in receipts.Where(x => x.ExpectedReceiptDate <= requiredDate && x.RemainingQuantity > 0))
            {
                var used = Math.Min(receipt.RemainingQuantity, remainingRequirement);
                if (used <= 0)
                {
                    continue;
                }

                receipt.RemainingQuantity -= used;
                remainingRequirement -= used;
                usedReceipts.Add(new UsedScheduledReceipt(receipt.SourceSystem, receipt.SourceDocumentType, receipt.SourceDocumentId, used));
                if (remainingRequirement <= 0)
                {
                    break;
                }
            }
        }

        return new SupplyConsumption(Math.Max(0, remainingRequirement), usedReceipts);
    }

    private static IReadOnlyCollection<decimal> ApplyLotSizing(decimal netRequirement, decimal? lotSizeMin, decimal? lotSizeMax, decimal? lotSizeMultiple)
    {
        var quantity = netRequirement;
        if (lotSizeMin is > 0 && quantity < lotSizeMin.Value)
        {
            quantity = lotSizeMin.Value;
        }

        if (lotSizeMultiple is > 0)
        {
            quantity = Math.Ceiling(quantity / lotSizeMultiple.Value) * lotSizeMultiple.Value;
        }

        if (lotSizeMax is > 0 && quantity > lotSizeMax.Value)
        {
            var split = new List<decimal>();
            var remaining = quantity;
            while (remaining > lotSizeMax.Value)
            {
                split.Add(lotSizeMax.Value);
                remaining -= lotSizeMax.Value;
            }

            if (remaining > 0)
            {
                split.Add(remaining);
            }

            return split;
        }

        return [quantity];
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private readonly record struct ItemKey(string SkuCode, string UomCode, string SiteCode)
    {
        public static ItemKey Create(string skuCode, string uomCode, string siteCode)
        {
            return new ItemKey(Normalize(skuCode), Normalize(uomCode), Normalize(siteCode));
        }
    }

    private sealed record RequirementBucket(ItemKey Key, DateOnly RequiredDate);

    private sealed record DemandPegging(string DemandSourceReference, string ParentSkuCode, string? ComponentSkuCode, decimal Quantity);

    private sealed record Requirement(
        string SkuCode,
        string UomCode,
        string SiteCode,
        decimal Quantity,
        DateOnly RequiredDate,
        IReadOnlyCollection<DemandPegging> DemandPegging,
        IReadOnlyCollection<string> Path);

    private sealed class ScheduledReceiptState(ScheduledReceiptSnapshot snapshot)
    {
        public DateOnly ExpectedReceiptDate { get; } = snapshot.ExpectedReceiptDate;
        public string SourceSystem { get; } = snapshot.SourceSystem;
        public string SourceDocumentType { get; } = snapshot.SourceDocumentType;
        public string SourceDocumentId { get; } = snapshot.SourceDocumentId;
        public decimal RemainingQuantity { get; set; } = snapshot.Quantity;
    }

    private sealed record UsedScheduledReceipt(string SourceSystem, string SourceDocumentType, string SourceDocumentId, decimal Quantity);

    private sealed record SupplyConsumption(decimal Shortage, IReadOnlyCollection<UsedScheduledReceipt> UsedReceipts);
}
