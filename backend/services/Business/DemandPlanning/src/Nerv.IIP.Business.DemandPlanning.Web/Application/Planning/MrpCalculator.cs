namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

public sealed record MrpCalculationInput(
    string OrganizationId,
    string EnvironmentId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    IReadOnlyCollection<DemandSnapshot> Demands,
    IReadOnlyCollection<InventoryAvailabilitySnapshot> Availability,
    IReadOnlyCollection<ProductionVersionSnapshot> ProductionVersions,
    IReadOnlyCollection<BomComponentSnapshot> BomComponents);

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
    string RoutingReference);

public sealed record BomComponentSnapshot(
    string ParentSkuCode,
    string ComponentSkuCode,
    string ComponentUomCode,
    decimal QuantityPerParent);

public sealed record CalculatedPlanningSuggestion(
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
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
    // MVP scope: deterministic daily buckets with single-level MBOM explosion; recursive multi-level expansion is a later planning slice.
    public static IReadOnlyCollection<CalculatedPlanningSuggestion> Calculate(MrpCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var availability = input.Availability
            .GroupBy(x => (x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.AvailableQuantity));
        var productionVersions = input.ProductionVersions.ToDictionary(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase);
        var componentsByParent = input.BomComponents
            .GroupBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<CalculatedPlanningSuggestion>();

        foreach (var demand in input.Demands.OrderBy(x => x.DueDate).ThenBy(x => x.DemandSourceReference, StringComparer.Ordinal))
        {
            if (demand.DueDate < input.HorizonStart || demand.DueDate > input.HorizonEnd)
            {
                continue;
            }

            var fgKey = (demand.SkuCode, demand.UomCode, demand.SiteCode);
            var fgAvailable = availability.GetValueOrDefault(fgKey);
            var plannedWorkOrderQuantity = Math.Max(0, demand.Quantity - fgAvailable);
            availability[fgKey] = Math.Max(0, fgAvailable - demand.Quantity);
            if (plannedWorkOrderQuantity <= 0)
            {
                continue;
            }

            productionVersions.TryGetValue(demand.SkuCode, out var version);
            var versionReference = version?.ProductionVersionReference;
            var mbomReference = version?.ManufacturingBomReference;
            var routingReference = version?.RoutingReference;
            suggestions.Add(new CalculatedPlanningSuggestion(
                "planned-work-order",
                demand.SkuCode,
                demand.UomCode,
                demand.SiteCode,
                plannedWorkOrderQuantity,
                demand.DueDate,
                "finished-good-net-requirement",
                [
                    new CalculatedPeggingLink(
                        "demand",
                        demand.DemandSourceReference,
                        demand.SkuCode,
                        null,
                        plannedWorkOrderQuantity,
                        versionReference,
                        mbomReference,
                        routingReference),
                ]));

            if (!componentsByParent.TryGetValue(demand.SkuCode, out var components))
            {
                continue;
            }

            foreach (var component in components.OrderBy(x => x.ComponentSkuCode, StringComparer.Ordinal))
            {
                var grossRequirement = plannedWorkOrderQuantity * component.QuantityPerParent;
                var componentKey = (component.ComponentSkuCode, component.ComponentUomCode, demand.SiteCode);
                var componentAvailable = availability.GetValueOrDefault(componentKey);
                var purchaseQuantity = Math.Max(0, grossRequirement - componentAvailable);
                availability[componentKey] = Math.Max(0, componentAvailable - grossRequirement);
                if (purchaseQuantity <= 0)
                {
                    continue;
                }

                suggestions.Add(new CalculatedPlanningSuggestion(
                    "planned-purchase",
                    component.ComponentSkuCode,
                    component.ComponentUomCode,
                    demand.SiteCode,
                    purchaseQuantity,
                    demand.DueDate,
                    "component-net-requirement",
                    [
                        new CalculatedPeggingLink(
                            "bom-component",
                            demand.DemandSourceReference,
                            demand.SkuCode,
                            component.ComponentSkuCode,
                            purchaseQuantity,
                            versionReference,
                            mbomReference,
                            routingReference),
                    ]));
            }
        }

        return suggestions;
    }
}
