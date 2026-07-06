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
    IReadOnlyCollection<PlanningParameterSnapshot> PlanningParameters,
    IReadOnlyCollection<UomConversionSnapshot> UomConversions);

public sealed record DemandSnapshot(
    string DemandSourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate,
    string SourceType = "demand-source");

public sealed record InventoryAvailabilitySnapshot(
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal AvailableQuantity,
    decimal OnHandQuantity = 0m,
    decimal ReservedQuantity = 0m);

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
    decimal QuantityPerParent,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m);

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
    decimal? LotSizeMultiple,
    string? ProcurementType = null,
    string? MrpType = null,
    string? LotSizingPolicy = null,
    decimal? ReorderPointQuantity = null,
    int? PlannedDeliveryTimeDays = null,
    int? InHouseProductionTimeDays = null,
    int? GoodsReceiptProcessingTimeDays = null);

public sealed record UomConversionSnapshot(
    string FromUomCode,
    string ToUomCode,
    decimal Factor,
    decimal Offset,
    int Precision,
    string RoundingMode);

public sealed record CalculatedPlanningSuggestion(
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    DateOnly ReleaseDate,
    string ReasonCode,
    CalculatedNetRequirementExplanation NetRequirementExplanation,
    IReadOnlyCollection<CalculatedPeggingLink> PeggingLinks);

public sealed record CalculatedPeggingLink(
    string PeggingType,
    string DemandSourceReference,
    string ParentSkuCode,
    string? ComponentSkuCode,
    decimal Quantity,
    string? ProductionVersionReference,
    string? ManufacturingBomReference,
    string? RoutingReference,
    string SourceType,
    decimal GrossDemandQuantity);

public sealed record CalculatedNetRequirementExplanation(
    decimal GrossDemandQuantity,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableToNetQuantity,
    decimal ScheduledReceiptQuantity,
    decimal SafetyStockQuantity,
    decimal NetRequirementQuantity,
    decimal PlannedQuantity,
    decimal ScrapRate,
    decimal YieldRate,
    string PrimarySourceType,
    string Formula,
    IReadOnlyCollection<string> UomConversions,
    IReadOnlyCollection<string> DegradationSources);

public static class MrpCalculator
{
    public static IReadOnlyCollection<CalculatedPlanningSuggestion> Calculate(MrpCalculationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var planningParameters = input.PlanningParameters
            .GroupBy(x => SkuSiteKey.Create(x.SkuCode, x.SiteCode))
            .ToDictionary(x => x.Key, x => x.First());
        var converter = UomConverter.Create(input.UomConversions);
        var availability = input.Availability
            .Select(x => NormalizeAvailability(x, planningParameters, converter))
            .GroupBy(x => ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(x => x.Key, x => new InventoryAvailabilityState(
                x.Sum(y => ResolveOnHandQuantity(y)),
                x.Sum(y => y.ReservedQuantity),
                x.Sum(y => y.AvailableQuantity)));
        var scheduledReceipts = input.ScheduledReceipts
            .Select(x => NormalizeScheduledReceipt(x, planningParameters, converter))
            .GroupBy(x => ItemKey.Create(x.SkuCode, x.UomCode, x.SiteCode))
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.ExpectedReceiptDate)
                    .ThenBy(y => y.SourceSystem, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(y => y.SourceDocumentType, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(y => y.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
                    .Select(y => new ScheduledReceiptState(y))
                    .ToList());
        var productionVersions = input.ProductionVersions
            .GroupBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var componentsByParent = input.BomComponents
            .GroupBy(x => x.ParentSkuCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<CalculatedPlanningSuggestion>();
        var normalizedDemands = input.Demands
            .Where(x => x.DueDate >= input.HorizonStart && x.DueDate <= input.HorizonEnd)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DemandSourceReference, StringComparer.Ordinal)
            .Select(x => NormalizeDemand(x, planningParameters, converter))
            .ToList();
        var lowLevelCodes = CalculateLowLevelCodes(
            normalizedDemands,
            planningParameters,
            productionVersions,
            componentsByParent);
        var pendingByLowLevel = new SortedDictionary<int, List<Requirement>>();
        foreach (var demand in normalizedDemands)
        {
            AddPendingRequirement(pendingByLowLevel, lowLevelCodes, demand);
        }

        while (pendingByLowLevel.Count > 0)
        {
            var currentLevel = pendingByLowLevel.First();
            pendingByLowLevel.Remove(currentLevel.Key);
            var current = currentLevel.Value;
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
                        x.First().SourceType,
                        x.Sum(y => y.Quantity)))
                    .ToArray();
                planningParameters.TryGetValue(SkuSiteKey.Create(first.SkuCode, first.SiteCode), out var planningParameter);
                productionVersions.TryGetValue(first.SkuCode, out var version);
                var supply = ConsumeSupply(
                    key,
                    grossRequirement,
                    group.Key.RequiredDate,
                    Math.Max(0, planningParameter?.SafetyStockQuantity ?? 0m),
                    availability,
                    scheduledReceipts);
                suggestions.AddRange(supply.ExceptionReceipts.Select(x => BuildScheduledReceiptExceptionSuggestion(
                    x,
                    first,
                    group.Key.RequiredDate,
                    demandPegging,
                    peggingVersion: IsMakeItem(planningParameter?.ProcurementType, version) ? version : null,
                    grossRequirement,
                    supply)));
                var netRequirement = supply.Shortage;
                if (netRequirement <= 0)
                {
                    continue;
                }

                var plannedQuantities = ApplyLotSizing(
                    netRequirement,
                    planningParameter?.LotSizeMin ?? version?.LotSizeMin,
                    planningParameter?.LotSizeMax ?? version?.LotSizeMax,
                    planningParameter?.LotSizeMultiple ?? version?.LotSizeMultiple,
                    planningParameter?.LotSizingPolicy);
                var plannedQuantity = plannedQuantities.Sum();
                var isMakeItem = IsMakeItem(planningParameter?.ProcurementType, version);
                var releaseDate = group.Key.RequiredDate.AddDays(-ResolveLeadTimeDays(planningParameter, isMakeItem));
                var suggestionType = isMakeItem ? "planned-work-order" : "planned-purchase";
                var reasonCode = isMakeItem ? "net-requirement" : "component-net-requirement";
                var peggingVersion = isMakeItem ? version : null;
                var peggingLinks = demandPegging
                    .Select(x => new CalculatedPeggingLink(
                        "demand",
                        x.DemandSourceReference,
                        x.ParentSkuCode,
                        x.ComponentSkuCode,
                        x.Quantity,
                        peggingVersion?.ProductionVersionReference,
                        peggingVersion?.ManufacturingBomReference,
                        peggingVersion?.RoutingReference,
                        x.SourceType,
                        x.Quantity))
                    .Concat(supply.UsedReceipts.Select(x => new CalculatedPeggingLink(
                        "scheduled-receipt",
                        $"{x.SourceSystem}:{x.SourceDocumentType}:{x.SourceDocumentId}",
                        first.SkuCode,
                        null,
                        x.Quantity,
                        peggingVersion?.ProductionVersionReference,
                        peggingVersion?.ManufacturingBomReference,
                        peggingVersion?.RoutingReference,
                        "scheduled-receipt",
                        x.Quantity)))
                    .ToArray();
                var explanation = new CalculatedNetRequirementExplanation(
                    grossRequirement,
                    supply.OnHandQuantity,
                    supply.ReservedQuantity,
                    supply.UsedAvailableQuantity,
                    supply.UsedScheduledReceiptQuantity,
                    supply.SafetyStockQuantity,
                    netRequirement,
                    plannedQuantity,
                    first.ScrapRate,
                    first.YieldRate,
                    first.RequirementType == "component" ? "component" : demandPegging.FirstOrDefault()?.SourceType ?? "unknown",
                    BuildFormula(grossRequirement, supply.UsedAvailableQuantity, supply.UsedScheduledReceiptQuantity, netRequirement, first.ScrapRate, first.YieldRate),
                    first.UomConversions,
                    []);
                suggestions.AddRange(plannedQuantities.Select(quantity => new CalculatedPlanningSuggestion(
                    suggestionType,
                    first.SkuCode,
                    first.UomCode,
                    first.SiteCode,
                    quantity,
                    group.Key.RequiredDate,
                    releaseDate,
                    reasonCode,
                    explanation with { PlannedQuantity = quantity },
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

                    var componentUomCode = ResolvePlanningUom(component.ComponentSkuCode, first.SiteCode, component.ComponentUomCode, planningParameters);
                    var componentBaseQuantity = plannedQuantity * component.QuantityPerParent;
                    var componentScrapRate = Math.Max(0m, component.ScrapRate);
                    var componentYieldRate = component.YieldRate <= 0m ? 1m : component.YieldRate;
                    var componentRequiredBeforeUom = componentBaseQuantity * (1m + componentScrapRate) / componentYieldRate;
                    var conversion = ConvertQuantity(
                        component.ComponentSkuCode,
                        component.ComponentUomCode,
                        componentUomCode,
                        componentRequiredBeforeUom,
                        converter);
                    var componentRequirement = conversion.Quantity;
                    AddPendingRequirement(pendingByLowLevel, lowLevelCodes, new Requirement(
                        component.ComponentSkuCode,
                        componentUomCode,
                        first.SiteCode,
                        componentRequirement,
                        releaseDate,
                        demandPegging
                            .Select(x => x with
                            {
                                ParentSkuCode = first.SkuCode,
                                ComponentSkuCode = component.ComponentSkuCode,
                                Quantity = ApportionByGrossRequirement(componentRequirement, x.Quantity, grossRequirement),
                            })
                            .ToArray(),
                        [.. first.Path, normalizedComponent],
                        "component",
                        componentScrapRate,
                        componentYieldRate,
                        [.. first.UomConversions, .. conversion.Summaries]));
                }
            }
        }

        suggestions.AddRange(scheduledReceipts
            .SelectMany(x => x.Value.Select(y => new { Key = x.Key, Receipt = y }))
            .Where(x => x.Receipt.RemainingQuantity > 0)
            .OrderBy(x => x.Receipt.ExpectedReceiptDate)
            .ThenBy(x => x.Key.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Receipt.SourceSystem, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Receipt.SourceDocumentType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Receipt.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(x => BuildCancelExceptionSuggestion(x.Key, x.Receipt)));

        return suggestions;
    }

    private static CalculatedPlanningSuggestion BuildScheduledReceiptExceptionSuggestion(
        ScheduledReceiptException receiptException,
        Requirement requirement,
        DateOnly requiredDate,
        IReadOnlyCollection<DemandPegging> demandPegging,
        ProductionVersionSnapshot? peggingVersion,
        decimal grossRequirement,
        SupplyConsumption supply)
    {
        var receiptLink = new CalculatedPeggingLink(
            "scheduled-receipt",
            $"{receiptException.SourceSystem}:{receiptException.SourceDocumentType}:{receiptException.SourceDocumentId}",
            requirement.SkuCode,
            null,
            receiptException.Quantity,
            peggingVersion?.ProductionVersionReference,
            peggingVersion?.ManufacturingBomReference,
            peggingVersion?.RoutingReference,
            "scheduled-receipt",
            receiptException.Quantity);
        var peggingLinks = demandPegging
            .Select(x => new CalculatedPeggingLink(
                "demand",
                x.DemandSourceReference,
                x.ParentSkuCode,
                x.ComponentSkuCode,
                x.Quantity,
                peggingVersion?.ProductionVersionReference,
                peggingVersion?.ManufacturingBomReference,
                peggingVersion?.RoutingReference,
                x.SourceType,
                x.Quantity))
            .Append(receiptLink)
            .ToArray();

        return new CalculatedPlanningSuggestion(
            receiptException.ExceptionType,
            requirement.SkuCode,
            requirement.UomCode,
            requirement.SiteCode,
            receiptException.Quantity,
            requiredDate,
            receiptException.ExpectedReceiptDate,
            receiptException.ReasonCode,
            new CalculatedNetRequirementExplanation(
                grossRequirement,
                supply.OnHandQuantity,
                supply.ReservedQuantity,
                supply.UsedAvailableQuantity,
                receiptException.Quantity,
                supply.SafetyStockQuantity,
                0m,
                receiptException.Quantity,
                requirement.ScrapRate,
                requirement.YieldRate,
                "scheduled-receipt",
                $"{receiptException.Quantity:g29} scheduled receipt should move from {receiptException.ExpectedReceiptDate:O} to {requiredDate:O}",
                requirement.UomConversions,
                []),
            peggingLinks);
    }

    private static CalculatedPlanningSuggestion BuildCancelExceptionSuggestion(ItemKey key, ScheduledReceiptState receipt)
    {
        var quantity = receipt.RemainingQuantity;
        return new CalculatedPlanningSuggestion(
            "cancel",
            key.SkuCode,
            key.UomCode,
            key.SiteCode,
            quantity,
            receipt.ExpectedReceiptDate,
            receipt.ExpectedReceiptDate,
            "scheduled-receipt-unneeded",
            new CalculatedNetRequirementExplanation(
                0m,
                0m,
                0m,
                0m,
                quantity,
                0m,
                0m,
                quantity,
                0m,
                1m,
                "scheduled-receipt",
                $"{quantity:g29} scheduled receipt has no matching requirement",
                [],
                []),
            [
                new CalculatedPeggingLink(
                    "scheduled-receipt",
                    $"{receipt.SourceSystem}:{receipt.SourceDocumentType}:{receipt.SourceDocumentId}",
                    key.SkuCode,
                    null,
                    quantity,
                    null,
                    null,
                    null,
                    "scheduled-receipt",
                    quantity),
            ]);
    }

    private static Requirement NormalizeDemand(
        DemandSnapshot demand,
        IReadOnlyDictionary<SkuSiteKey, PlanningParameterSnapshot> planningParameters,
        UomConverter converter)
    {
        var planningUom = ResolvePlanningUom(demand.SkuCode, demand.SiteCode, demand.UomCode, planningParameters);
        var conversion = ConvertQuantity(demand.SkuCode, demand.UomCode, planningUom, demand.Quantity, converter);
        return new Requirement(
            demand.SkuCode,
            planningUom,
            demand.SiteCode,
            conversion.Quantity,
            demand.DueDate,
            [new DemandPegging(demand.DemandSourceReference, demand.SkuCode, null, SourceTypeFromDemandType(demand.SourceType), conversion.Quantity)],
            [Normalize(demand.SkuCode)],
            "demand",
            0m,
            1m,
            conversion.Summaries);
    }

    private static InventoryAvailabilitySnapshot NormalizeAvailability(
        InventoryAvailabilitySnapshot availability,
        IReadOnlyDictionary<SkuSiteKey, PlanningParameterSnapshot> planningParameters,
        UomConverter converter)
    {
        var planningUom = ResolvePlanningUom(availability.SkuCode, availability.SiteCode, availability.UomCode, planningParameters);
        var availableConversion = ConvertQuantity(availability.SkuCode, availability.UomCode, planningUom, availability.AvailableQuantity, converter);
        var onHandConversion = ConvertQuantity(availability.SkuCode, availability.UomCode, planningUom, ResolveOnHandQuantity(availability), converter);
        var reservedConversion = ConvertQuantity(availability.SkuCode, availability.UomCode, planningUom, availability.ReservedQuantity, converter);
        return availability with
        {
            UomCode = planningUom,
            AvailableQuantity = availableConversion.Quantity,
            OnHandQuantity = onHandConversion.Quantity,
            ReservedQuantity = reservedConversion.Quantity,
        };
    }

    private static ScheduledReceiptSnapshot NormalizeScheduledReceipt(
        ScheduledReceiptSnapshot receipt,
        IReadOnlyDictionary<SkuSiteKey, PlanningParameterSnapshot> planningParameters,
        UomConverter converter)
    {
        var planningUom = ResolvePlanningUom(receipt.SkuCode, receipt.SiteCode, receipt.UomCode, planningParameters);
        var conversion = ConvertQuantity(receipt.SkuCode, receipt.UomCode, planningUom, receipt.Quantity, converter);
        return receipt with
        {
            UomCode = planningUom,
            Quantity = conversion.Quantity,
        };
    }

    private static string ResolvePlanningUom(
        string skuCode,
        string siteCode,
        string fallbackUomCode,
        IReadOnlyDictionary<SkuSiteKey, PlanningParameterSnapshot> planningParameters)
    {
        return planningParameters.TryGetValue(SkuSiteKey.Create(skuCode, siteCode), out var parameter)
            && !string.IsNullOrWhiteSpace(parameter.UomCode)
            ? parameter.UomCode
            : fallbackUomCode;
    }

    private static QuantityConversion ConvertQuantity(
        string triggerSkuCode,
        string fromUomCode,
        string toUomCode,
        decimal quantity,
        UomConverter converter)
    {
        if (string.Equals(Normalize(fromUomCode), Normalize(toUomCode), StringComparison.Ordinal))
        {
            return new QuantityConversion(quantity, []);
        }

        var converted = converter.Convert(triggerSkuCode, fromUomCode, toUomCode, quantity);
        return new QuantityConversion(converted, [$"{quantity} {fromUomCode} -> {converted} {toUomCode}"]);
    }

    private static SupplyConsumption ConsumeSupply(
        ItemKey key,
        decimal requiredQuantity,
        DateOnly requiredDate,
        decimal safetyStockQuantity,
        IDictionary<ItemKey, InventoryAvailabilityState> availability,
        IReadOnlyDictionary<ItemKey, List<ScheduledReceiptState>> scheduledReceipts)
    {
        var remainingRequirement = requiredQuantity;
        var availableState = availability.TryGetValue(key, out var state)
            ? state
            : new InventoryAvailabilityState(0m, 0m, 0m);
        var availableQuantity = availableState.AvailableQuantity;
        var availableForNetting = Math.Max(0, availableQuantity - safetyStockQuantity);
        var usedAvailable = Math.Min(availableForNetting, remainingRequirement);
        if (usedAvailable > 0)
        {
            availableState.AvailableQuantity = availableQuantity - usedAvailable;
            availableState.OnHandQuantity = Math.Max(0m, availableState.OnHandQuantity - usedAvailable);
            availability[key] = availableState;
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
                usedReceipts.Add(new UsedScheduledReceipt(
                    receipt.SourceSystem,
                    receipt.SourceDocumentType,
                    receipt.SourceDocumentId,
                    receipt.ExpectedReceiptDate,
                    used,
                    receipt.ExpectedReceiptDate < requiredDate ? "reschedule-out" : null,
                    receipt.ExpectedReceiptDate < requiredDate ? "scheduled-receipt-early" : null));
                if (remainingRequirement <= 0)
                {
                    break;
                }
            }

            foreach (var receipt in receipts.Where(x => x.ExpectedReceiptDate > requiredDate && x.RemainingQuantity > 0))
            {
                if (remainingRequirement <= 0)
                {
                    break;
                }

                var used = Math.Min(receipt.RemainingQuantity, remainingRequirement);
                if (used <= 0)
                {
                    continue;
                }

                receipt.RemainingQuantity -= used;
                remainingRequirement -= used;
                usedReceipts.Add(new UsedScheduledReceipt(
                    receipt.SourceSystem,
                    receipt.SourceDocumentType,
                    receipt.SourceDocumentId,
                    receipt.ExpectedReceiptDate,
                    used,
                    "reschedule-in",
                    "scheduled-receipt-late"));
            }
        }

        return new SupplyConsumption(
            Math.Max(0, remainingRequirement),
            Math.Max(0m, availableState.OnHandQuantity + usedAvailable),
            availableState.ReservedQuantity,
            usedAvailable,
            usedReceipts.Sum(x => x.Quantity),
            safetyStockQuantity,
            usedReceipts,
            usedReceipts.Where(x => x.ExceptionType is not null)
                .Select(x => new ScheduledReceiptException(
                    x.ExceptionType!,
                    x.SourceSystem,
                    x.SourceDocumentType,
                    x.SourceDocumentId,
                    x.ExpectedReceiptDate,
                    x.Quantity,
                    x.ReasonCode!))
                .ToArray());
    }

    private static IReadOnlyCollection<decimal> ApplyLotSizing(
        decimal netRequirement,
        decimal? lotSizeMin,
        decimal? lotSizeMax,
        decimal? lotSizeMultiple,
        string? lotSizingPolicy)
    {
        if (IsLotForLot(lotSizingPolicy))
        {
            return [netRequirement];
        }

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
            // Preserve the exact planned total. If min/multiple/max rules conflict, master data
            // should reject the rule set rather than inflate the final split here.
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

    private static bool IsMakeItem(string? procurementType, ProductionVersionSnapshot? version)
    {
        if (MatchesAny(procurementType, "buy", "purchase", "purchased", "external", "outsourced"))
        {
            return false;
        }

        if (MatchesAny(procurementType, "make", "manufacture", "manufactured", "in-house", "inhouse", "produce"))
        {
            return true;
        }

        return version is not null;
    }

    private static int ResolveLeadTimeDays(PlanningParameterSnapshot? parameter, bool isMakeItem)
    {
        if (parameter is null)
        {
            return 0;
        }

        var sourceLeadTime = isMakeItem
            ? parameter.InHouseProductionTimeDays
            : parameter.PlannedDeliveryTimeDays;
        if (sourceLeadTime is null)
        {
            return Math.Max(0, parameter.LeadTimeDays);
        }

        return Math.Max(0, parameter.GoodsReceiptProcessingTimeDays ?? 0) + Math.Max(0, sourceLeadTime.Value);
    }

    private static bool IsLotForLot(string? lotSizingPolicy)
    {
        return MatchesAny(lotSizingPolicy, "lot-for-lot", "lotforlot", "lfl");
    }

    private static bool MatchesAny(string? value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return candidates.Any(candidate => string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<SkuSiteKey, int> CalculateLowLevelCodes(
        IReadOnlyCollection<Requirement> rootRequirements,
        IReadOnlyDictionary<SkuSiteKey, PlanningParameterSnapshot> planningParameters,
        IReadOnlyDictionary<string, ProductionVersionSnapshot> productionVersions,
        IReadOnlyDictionary<string, BomComponentSnapshot[]> componentsByParent)
    {
        var lowLevelCodes = new Dictionary<SkuSiteKey, int>();
        foreach (var requirement in rootRequirements)
        {
            Visit(requirement.SkuCode, requirement.SiteCode, 0, [Normalize(requirement.SkuCode)]);
        }

        return lowLevelCodes;

        void Visit(string skuCode, string siteCode, int level, HashSet<string> path)
        {
            var key = SkuSiteKey.Create(skuCode, siteCode);
            if (lowLevelCodes.TryGetValue(key, out var existing) && existing >= level)
            {
                return;
            }

            lowLevelCodes[key] = level;
            planningParameters.TryGetValue(key, out var planningParameter);
            productionVersions.TryGetValue(skuCode, out var version);
            if (!IsMakeItem(planningParameter?.ProcurementType, version) ||
                !componentsByParent.TryGetValue(skuCode, out var components))
            {
                return;
            }

            foreach (var component in components.OrderBy(x => x.ComponentSkuCode, StringComparer.OrdinalIgnoreCase))
            {
                var normalizedComponent = Normalize(component.ComponentSkuCode);
                if (path.Contains(normalizedComponent))
                {
                    continue;
                }

                Visit(
                    component.ComponentSkuCode,
                    siteCode,
                    level + 1,
                    new HashSet<string>(path, StringComparer.OrdinalIgnoreCase) { normalizedComponent });
            }
        }
    }

    private static void AddPendingRequirement(
        SortedDictionary<int, List<Requirement>> pendingByLowLevel,
        IReadOnlyDictionary<SkuSiteKey, int> lowLevelCodes,
        Requirement requirement)
    {
        var lowLevelCode = lowLevelCodes.TryGetValue(SkuSiteKey.Create(requirement.SkuCode, requirement.SiteCode), out var code)
            ? code
            : 0;
        if (!pendingByLowLevel.TryGetValue(lowLevelCode, out var pending))
        {
            pending = [];
            pendingByLowLevel[lowLevelCode] = pending;
        }

        pending.Add(requirement);
    }

    private static decimal ApportionByGrossRequirement(decimal totalQuantity, decimal sourceQuantity, decimal grossRequirement)
    {
        return grossRequirement <= 0m ? 0m : totalQuantity * sourceQuantity / grossRequirement;
    }

    private static decimal ResolveOnHandQuantity(InventoryAvailabilitySnapshot availability)
    {
        return availability.OnHandQuantity > 0m || availability.ReservedQuantity > 0m
            ? availability.OnHandQuantity
            : availability.AvailableQuantity;
    }

    private static string SourceTypeFromDemandType(string demandType)
    {
        return Normalize(demandType) switch
        {
            "SALES-ORDER" or "SALES" => "sales",
            "FORECAST" => "forecast",
            "SAFETY-STOCK" or "SAFETYSTOCK" => "safety-stock",
            "MPS" or "MASTER-PRODUCTION-SCHEDULE" => "mps",
            _ => "demand",
        };
    }

    private static string BuildFormula(
        decimal grossDemandQuantity,
        decimal availableToNetQuantity,
        decimal scheduledReceiptQuantity,
        decimal netRequirementQuantity,
        decimal scrapRate,
        decimal yieldRate)
    {
        var formula = $"{grossDemandQuantity:g29} - {availableToNetQuantity:g29} - {scheduledReceiptQuantity:g29} = {netRequirementQuantity:g29}";
        return scrapRate > 0m || yieldRate != 1m
            ? $"{formula}; scrap/yield {scrapRate:g29}/{yieldRate:g29}"
            : formula;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private readonly record struct ItemKey(string SkuCode, string UomCode, string SiteCode)
    {
        public static ItemKey Create(string skuCode, string uomCode, string siteCode)
        {
            return new ItemKey(Normalize(skuCode), Normalize(uomCode), Normalize(siteCode));
        }
    }

    private readonly record struct SkuSiteKey(string SkuCode, string SiteCode)
    {
        public static SkuSiteKey Create(string skuCode, string siteCode)
        {
            return new SkuSiteKey(Normalize(skuCode), Normalize(siteCode));
        }
    }

    private sealed record RequirementBucket(ItemKey Key, DateOnly RequiredDate);

    private sealed record DemandPegging(string DemandSourceReference, string ParentSkuCode, string? ComponentSkuCode, string SourceType, decimal Quantity);

    private sealed record Requirement(
        string SkuCode,
        string UomCode,
        string SiteCode,
        decimal Quantity,
        DateOnly RequiredDate,
        IReadOnlyCollection<DemandPegging> DemandPegging,
        IReadOnlyCollection<string> Path,
        string RequirementType,
        decimal ScrapRate,
        decimal YieldRate,
        IReadOnlyCollection<string> UomConversions);

    private sealed class ScheduledReceiptState(ScheduledReceiptSnapshot snapshot)
    {
        public DateOnly ExpectedReceiptDate { get; } = snapshot.ExpectedReceiptDate;
        public string SourceSystem { get; } = snapshot.SourceSystem;
        public string SourceDocumentType { get; } = snapshot.SourceDocumentType;
        public string SourceDocumentId { get; } = snapshot.SourceDocumentId;
        public decimal RemainingQuantity { get; set; } = snapshot.Quantity;
    }

    private sealed record UsedScheduledReceipt(
        string SourceSystem,
        string SourceDocumentType,
        string SourceDocumentId,
        DateOnly ExpectedReceiptDate,
        decimal Quantity,
        string? ExceptionType,
        string? ReasonCode);

    private sealed record ScheduledReceiptException(
        string ExceptionType,
        string SourceSystem,
        string SourceDocumentType,
        string SourceDocumentId,
        DateOnly ExpectedReceiptDate,
        decimal Quantity,
        string ReasonCode);

    private sealed record SupplyConsumption(
        decimal Shortage,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal UsedAvailableQuantity,
        decimal UsedScheduledReceiptQuantity,
        decimal SafetyStockQuantity,
        IReadOnlyCollection<UsedScheduledReceipt> UsedReceipts,
        IReadOnlyCollection<ScheduledReceiptException> ExceptionReceipts);

    private sealed record QuantityConversion(decimal Quantity, IReadOnlyCollection<string> Summaries);

    private sealed class InventoryAvailabilityState(decimal onHandQuantity, decimal reservedQuantity, decimal availableQuantity)
    {
        public decimal OnHandQuantity { get; set; } = onHandQuantity;
        public decimal ReservedQuantity { get; } = reservedQuantity;
        public decimal AvailableQuantity { get; set; } = availableQuantity;
    }

    private sealed class UomConverter
    {
        private readonly IReadOnlyDictionary<(string FromUomCode, string ToUomCode), UomConversionSnapshot> conversions;

        private UomConverter(IReadOnlyDictionary<(string FromUomCode, string ToUomCode), UomConversionSnapshot> conversions)
        {
            this.conversions = conversions;
        }

        public static UomConverter Create(IReadOnlyCollection<UomConversionSnapshot> conversions)
        {
            return new UomConverter(conversions
                .GroupBy(x => (Normalize(x.FromUomCode), Normalize(x.ToUomCode)))
                .ToDictionary(x => x.Key, x => x.First()));
        }

        // MasterData UOM conversions are global by unit pair; triggerSkuCode is only diagnostic context.
        public decimal Convert(string triggerSkuCode, string fromUomCode, string toUomCode, decimal quantity)
        {
            var from = Normalize(fromUomCode);
            var to = Normalize(toUomCode);
            if (!conversions.TryGetValue((from, to), out var conversion))
            {
                throw new InvalidOperationException($"Missing global UOM conversion from '{fromUomCode}' to planning UOM '{toUomCode}' while normalizing SKU '{triggerSkuCode}'.");
            }

            if (conversion.Factor <= 0m)
            {
                throw new InvalidOperationException($"Invalid global UOM conversion from '{fromUomCode}' to planning UOM '{toUomCode}' while normalizing SKU '{triggerSkuCode}': factor must be positive.");
            }

            var converted = Round(quantity * conversion.Factor + conversion.Offset, conversion.Precision, conversion.RoundingMode);
            if (converted < 0m)
            {
                throw new InvalidOperationException($"Invalid global UOM conversion from '{fromUomCode}' to planning UOM '{toUomCode}' while normalizing SKU '{triggerSkuCode}': negative quantity after conversion is not allowed.");
            }

            return converted;
        }

        private static decimal Round(decimal value, int precision, string roundingMode)
        {
            var digits = Math.Clamp(precision, 0, 12);
            return Normalize(roundingMode) switch
            {
                "BANKERS" or "TO-EVEN" or "TOEVEN" => Math.Round(value, digits, MidpointRounding.ToEven),
                "CEILING" or "UP" => RoundToward(value, digits, ceiling: true),
                "FLOOR" or "DOWN" => RoundToward(value, digits, ceiling: false),
                _ => Math.Round(value, digits, MidpointRounding.AwayFromZero),
            };
        }

        private static decimal RoundToward(decimal value, int digits, bool ceiling)
        {
            var scale = (decimal)Math.Pow(10, digits);
            return (ceiling ? Math.Ceiling(value * scale) : Math.Floor(value * scale)) / scale;
        }
    }
}
