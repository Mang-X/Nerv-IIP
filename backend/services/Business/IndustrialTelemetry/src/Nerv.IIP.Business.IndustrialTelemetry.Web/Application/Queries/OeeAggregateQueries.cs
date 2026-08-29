using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

[JsonConverter(typeof(OeeAggregateDimensionJsonConverter))]
public enum OeeAggregateDimension
{
    [EnumMember(Value = "device")]
    Device,
    [EnumMember(Value = "workCenter")]
    WorkCenter,
    [EnumMember(Value = "line")]
    Line,
    [EnumMember(Value = "workshop")]
    Workshop,
    [EnumMember(Value = "shift")]
    Shift,
    [EnumMember(Value = "day")]
    Day,
}

public sealed class OeeAggregateDimensionJsonConverter()
    : JsonStringEnumConverter<OeeAggregateDimension>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonConverter(typeof(OeeAggregateDegradedReasonJsonConverter))]
public enum OeeAggregateDegradedReason
{
    [EnumMember(Value = "runtimeStateFactsMissing")]
    RuntimeStateFactsMissing,
    [EnumMember(Value = "runtimeStateCoverageIncomplete")]
    RuntimeStateCoverageIncomplete,
    [EnumMember(Value = "productionUomAmbiguous")]
    ProductionUomAmbiguous,
    [EnumMember(Value = "productionOutputMissing")]
    ProductionOutputMissing,
    [EnumMember(Value = "theoreticalRateMissingOrAmbiguous")]
    TheoreticalRateMissingOrAmbiguous,
    [EnumMember(Value = "productiveRuntimeMissing")]
    ProductiveRuntimeMissing,
    [EnumMember(Value = "loadingRuntimeMissing")]
    LoadingRuntimeMissing,
    [EnumMember(Value = "historicalDimensionLegacyUnresolved")]
    HistoricalDimensionLegacyUnresolved,
    [EnumMember(Value = "historicalHierarchyMissing")]
    HistoricalHierarchyMissing,
    [EnumMember(Value = "historicalTimezoneMissing")]
    HistoricalTimezoneMissing,
    [EnumMember(Value = "historicalTimezoneInvalid")]
    HistoricalTimezoneInvalid,
    [EnumMember(Value = "historicalShiftDefinitionMissing")]
    HistoricalShiftDefinitionMissing,
    [EnumMember(Value = "historicalShiftDefinitionInvalid")]
    HistoricalShiftDefinitionInvalid,
    [EnumMember(Value = "historicalReportOutsideShiftWindow")]
    HistoricalReportOutsideShiftWindow,
    [EnumMember(Value = "historicalLocalTimeInvalid")]
    HistoricalLocalTimeInvalid,
    [EnumMember(Value = "historicalLocalTimeAmbiguous")]
    HistoricalLocalTimeAmbiguous,
    [EnumMember(Value = "siteDimensionMissing")]
    SiteDimensionMissing,
    [EnumMember(Value = "workshopDimensionMissing")]
    WorkshopDimensionMissing,
    [EnumMember(Value = "lineDimensionMissing")]
    LineDimensionMissing,
    [EnumMember(Value = "siteDimensionAmbiguous")]
    SiteDimensionAmbiguous,
    [EnumMember(Value = "workshopDimensionAmbiguous")]
    WorkshopDimensionAmbiguous,
    [EnumMember(Value = "lineDimensionAmbiguous")]
    LineDimensionAmbiguous,
    [EnumMember(Value = "siteTimezoneOrDayBoundaryMissing")]
    SiteTimezoneOrDayBoundaryMissing,
    [EnumMember(Value = "shiftDefinitionOrBoundaryMissing")]
    ShiftDefinitionOrBoundaryMissing,
}

public sealed class OeeAggregateDegradedReasonJsonConverter()
    : JsonStringEnumConverter<OeeAggregateDegradedReason>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public static class OeeAggregateMaterializationLimits
{
    public const int MaximumProductionFactCount = 10_000;
    public const int MaximumStateSampleCount = 10_000;
}

internal static class OeeAggregateQueryPlan
{
    internal static IQueryable<OeeProductionFact> BuildFacts(
        ApplicationDbContext dbContext,
        QueryOeeAggregateBucketsQuery request) =>
        dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.AggregationOccurredAtUtc >= request.WindowStartUtc)
            .Where(x => x.AggregationOccurredAtUtc < request.WindowEndUtc)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.WorkCenterId == null || x.WorkCenterId == request.WorkCenterId)
            .Where(x => request.ShiftCode == null || x.ShiftCode == request.ShiftCode)
            .Where(x => request.LineCode == null || x.LineCode == request.LineCode)
            .Where(x => request.WorkshopCode == null || x.WorkshopCode == request.WorkshopCode)
            .Where(x => request.BusinessDate == null || x.BusinessDate == request.BusinessDate)
            .OrderBy(x => x.AggregationOccurredAtUtc)
            .ThenBy(x => x.SourceReportNo)
            .Take(OeeAggregateMaterializationLimits.MaximumProductionFactCount + 1);

    internal static IQueryable<OeeProductionFact> BuildHierarchyTimelineFacts(
        ApplicationDbContext dbContext,
        QueryOeeAggregateBucketsQuery request,
        IReadOnlyCollection<string> deviceIds) =>
        dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.AggregationOccurredAtUtc >= request.WindowStartUtc)
            .Where(x => x.AggregationOccurredAtUtc < request.WindowEndUtc)
            .Where(x => deviceIds.Contains(x.DeviceAssetId))
            .OrderBy(x => x.AggregationOccurredAtUtc)
            .ThenBy(x => x.SourceReportNo)
            .Take(OeeAggregateMaterializationLimits.MaximumProductionFactCount + 1);

    internal static IQueryable<DeviceStateSnapshot> BuildInWindowStates(
        ApplicationDbContext dbContext,
        QueryOeeAggregateBucketsQuery request,
        IReadOnlyCollection<string> deviceIds,
        DateTimeOffset earliestStart,
        DateTimeOffset latestEnd,
        int maximumRows) =>
        dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => deviceIds.Contains(x.DeviceAssetId))
            .Where(x => x.OccurredAtUtc >= earliestStart)
            .Where(x => x.OccurredAtUtc < latestEnd)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.RecordedAtUtc)
            .ThenBy(x => x.SourceSequence)
            .Take(maximumRows + 1);

    internal static IQueryable<DeviceStateSnapshot> BuildCarryInStates(
        ApplicationDbContext dbContext,
        QueryOeeAggregateBucketsQuery request,
        IReadOnlyCollection<string> deviceIds,
        DateTimeOffset earliestStart) =>
        dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => deviceIds.Contains(x.DeviceAssetId))
            .Where(x => x.OccurredAtUtc < earliestStart)
            .GroupBy(x => x.DeviceAssetId)
            .Select(group => group
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.RecordedAtUtc)
                .ThenByDescending(x => x.SourceSequence)
                .First())
            .Take(OeeAggregateMaterializationLimits.MaximumStateSampleCount + 1);
}

public sealed record QueryOeeAggregateBucketsQuery(
    string OrganizationId,
    string EnvironmentId,
    OeeAggregateDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetId = null,
    string? WorkCenterId = null,
    string? ShiftCode = null,
    string? LineCode = null,
    string? WorkshopCode = null,
    DateOnly? BusinessDate = null,
    int Skip = 0,
    int Take = 100) : IQuery<OeeAggregateBucketsResponse>;

public sealed record OeeAggregateBucketsResponse(
    string OrganizationId,
    string EnvironmentId,
    OeeAggregateDimension Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<OeeAggregateBucket> Buckets,
    int TotalCount,
    int Skip,
    int Take);

public sealed record OeeAggregateBucket(
    OeeAggregateDimension Dimension,
    string? DimensionValue,
    string? SiteCode,
    string? WorkshopCode,
    string? LineCode,
    string? WorkCenterId,
    string? DeviceAssetId,
    string? ShiftCode,
    DateOnly? BusinessDate,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int DeviceCount,
    int StateSampleCount,
    int ProductionFactCount,
    decimal? AvailabilityRate,
    decimal? PerformanceRate,
    decimal? QualityRate,
    decimal? OeeRate,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    string? OutputUomCode,
    decimal? ExpectedOutputQuantity,
    bool IsDegraded,
    IReadOnlyCollection<OeeAggregateDegradedReason> DegradedReasons);

public sealed class QueryOeeAggregateBucketsQueryValidator : AbstractValidator<QueryOeeAggregateBucketsQuery>
{
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public QueryOeeAggregateBucketsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.WindowEndUtc)
            .Must((query, endUtc) => endUtc - query.WindowStartUtc <= MaximumWindow)
            .WithMessage("OEE aggregate window cannot exceed 31 days.");
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.ShiftCode).MaximumLength(100);
        RuleFor(x => x.LineCode).MaximumLength(100);
        RuleFor(x => x.WorkshopCode).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}

public sealed class QueryOeeAggregateBucketsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryOeeAggregateBucketsQuery, OeeAggregateBucketsResponse>
{
    public async Task<OeeAggregateBucketsResponse> Handle(
        QueryOeeAggregateBucketsQuery request,
        CancellationToken cancellationToken)
    {
        var facts = await OeeAggregateQueryPlan.BuildFacts(dbContext, request).ToArrayAsync(cancellationToken);
        EnsureProductionFactMaterializationLimit(facts.Length);
        if (facts.Length == 0)
        {
            return Response(request, []);
        }

        var groups = facts
            .GroupBy(x => BucketKey.From(x, request))
            .Select(group => new FactBucket(
                group.Key,
                group.ToArray(),
                group.Key.StartUtc < request.WindowStartUtc ? request.WindowStartUtc : group.Key.StartUtc,
                group.Key.EndUtc > request.WindowEndUtc ? request.WindowEndUtc : group.Key.EndUtc))
            .Where(x => x.EndUtc > x.StartUtc)
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.Key.DimensionValue, StringComparer.Ordinal)
            .ThenBy(x => x.Key.SiteCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.WorkshopCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.LineCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.BusinessDate)
            .ToArray();
        if (groups.Length == 0)
        {
            return Response(request, []);
        }

        var deviceIds = facts.Select(x => x.DeviceAssetId).Where(x => x != null).Select(x => x!).Distinct(StringComparer.Ordinal).ToArray();
        var runtimeFacts = facts;
        if (request.Dimension is OeeAggregateDimension.WorkCenter or OeeAggregateDimension.Line or OeeAggregateDimension.Workshop)
        {
            runtimeFacts = await OeeAggregateQueryPlan
                .BuildHierarchyTimelineFacts(dbContext, request, deviceIds)
                .ToArrayAsync(cancellationToken);
            EnsureProductionFactMaterializationLimit(runtimeFacts.Length);
        }
        var earliestStart = groups.Min(x => x.StartUtc);
        var latestEnd = groups.Max(x => x.EndUtc);
        var carryIns = await OeeAggregateQueryPlan
            .BuildCarryInStates(dbContext, request, deviceIds, earliestStart)
            .ToArrayAsync(cancellationToken);
        EnsureStateMaterializationLimit(carryIns.Length);
        var remainingStateCapacity = OeeAggregateMaterializationLimits.MaximumStateSampleCount - carryIns.Length;
        var inWindowStates = await OeeAggregateQueryPlan
            .BuildInWindowStates(dbContext, request, deviceIds, earliestStart, latestEnd, remainingStateCapacity)
            .ToArrayAsync(cancellationToken);
        EnsureStateMaterializationLimit(carryIns.Length + inWindowStates.Length);
        var statesByDevice = carryIns
            .Concat(inWindowStates)
            .GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<DeviceStateSnapshot>)x
                    .OrderBy(y => y.OccurredAtUtc)
                    .ThenBy(y => y.RecordedAtUtc)
                    .ThenBy(y => y.SourceSequence, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var runtimeWindows = BuildRuntimeWindows(runtimeFacts, request);

        var allBuckets = groups
            .Select(group => CalculateBucket(
                request.Dimension,
                group,
                statesByDevice,
                runtimeWindows))
            .ToArray();
        var buckets = allBuckets.Skip(request.Skip).Take(request.Take).ToArray();
        return Response(request, buckets, allBuckets.Length);
    }

    private static IReadOnlyDictionary<DeviceBucketKey, IReadOnlyList<RuntimeWindow>> BuildRuntimeWindows(
        IReadOnlyCollection<OeeProductionFact> facts,
        QueryOeeAggregateBucketsQuery request)
    {
        var windows = new Dictionary<DeviceBucketKey, List<RuntimeWindow>>();
        foreach (var deviceGroup in facts.GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal))
        {
            var orderedFacts = deviceGroup
                .OrderBy(x => x.AggregationOccurredAtUtc)
                .ThenBy(x => x.SourceReportNo, StringComparer.Ordinal)
                .ToArray();
            if (request.Dimension is OeeAggregateDimension.WorkCenter or OeeAggregateDimension.Line or OeeAggregateDimension.Workshop)
            {
                var currentKey = BucketKey.From(orderedFacts[0], request);
                var currentStart = request.WindowStartUtc;
                foreach (var fact in orderedFacts.Skip(1))
                {
                    var nextKey = BucketKey.From(fact, request);
                    if (nextKey == currentKey)
                    {
                        continue;
                    }

                    AddWindow(deviceGroup.Key, currentKey, currentStart, fact.AggregationOccurredAtUtc);
                    currentKey = nextKey;
                    currentStart = fact.AggregationOccurredAtUtc;
                }
                AddWindow(deviceGroup.Key, currentKey, currentStart, request.WindowEndUtc);
                continue;
            }

            foreach (var key in orderedFacts.Select(x => BucketKey.From(x, request)).Distinct())
            {
                AddWindow(deviceGroup.Key, key, key.StartUtc, key.EndUtc);
            }
        }

        return windows.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RuntimeWindow>)x.Value,
            EqualityComparer<DeviceBucketKey>.Default);

        void AddWindow(string deviceId, BucketKey key, DateTimeOffset startUtc, DateTimeOffset endUtc)
        {
            var clippedStart = startUtc < request.WindowStartUtc ? request.WindowStartUtc : startUtc;
            var clippedEnd = endUtc > request.WindowEndUtc ? request.WindowEndUtc : endUtc;
            if (clippedEnd <= clippedStart)
            {
                return;
            }

            var lookupKey = new DeviceBucketKey(deviceId, key);
            if (!windows.TryGetValue(lookupKey, out var deviceWindows))
            {
                deviceWindows = [];
                windows.Add(lookupKey, deviceWindows);
            }
            deviceWindows.Add(new RuntimeWindow(clippedStart, clippedEnd));
        }
    }

    private static OeeAggregateBucket CalculateBucket(
        OeeAggregateDimension dimension,
        FactBucket group,
        IReadOnlyDictionary<string, IReadOnlyList<DeviceStateSnapshot>> statesByDevice,
        IReadOnlyDictionary<DeviceBucketKey, IReadOnlyList<RuntimeWindow>> runtimeWindows)
    {
        var degradedReasons = new HashSet<OeeAggregateDegradedReason>();
        var hasCompleteRuntimeCoverage = true;
        var deviceIds = group.Facts.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).ToArray();
        long loadingTicks = 0;
        long productiveTicks = 0;
        var stateSampleCount = 0;
        var productiveHoursByDevice = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var deviceId in deviceIds)
        {
            statesByDevice.TryGetValue(deviceId, out var deviceStates);
            var deviceLoadingTicks = 0L;
            var deviceProductiveTicks = 0L;
            var deviceStateSampleCount = 0;
            var deviceHasCompleteCoverage = true;
            if (!runtimeWindows.TryGetValue(new DeviceBucketKey(deviceId, group.Key), out var deviceRuntimeWindows))
            {
                deviceHasCompleteCoverage = false;
                deviceRuntimeWindows = [];
            }
            foreach (var window in deviceRuntimeWindows)
            {
                var runtime = CalculateRuntime(deviceStates ?? [], window.StartUtc, window.EndUtc);
                deviceLoadingTicks += runtime.LoadingTicks;
                deviceProductiveTicks += runtime.ProductiveTicks;
                deviceStateSampleCount += runtime.StateSampleCount;
                deviceHasCompleteCoverage &= runtime.HasCompleteCoverage;
            }
            loadingTicks += deviceLoadingTicks;
            productiveTicks += deviceProductiveTicks;
            stateSampleCount += deviceStateSampleCount;
            productiveHoursByDevice[deviceId] = decimal.Divide(deviceProductiveTicks, TimeSpan.TicksPerHour);
            if (deviceHasCompleteCoverage) continue;

            hasCompleteRuntimeCoverage = false;
            degradedReasons.Add(deviceStateSampleCount == 0
                ? OeeAggregateDegradedReason.RuntimeStateFactsMissing
                : OeeAggregateDegradedReason.RuntimeStateCoverageIncomplete);
        }

        var goodQuantity = group.Facts.Sum(x => x.GoodQuantity);
        var scrapQuantity = group.Facts.Sum(x => x.ScrapQuantity);
        var reworkQuantity = group.Facts.Sum(x => x.ReworkQuantity);
        var totalOutputQuantity = goodQuantity + scrapQuantity + reworkQuantity;
        var uomCodes = group.Facts.Select(x => x.UomCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputUomCode = uomCodes.Length == 1 ? uomCodes[0] : null;
        if (outputUomCode is null)
        {
            degradedReasons.Add(OeeAggregateDegradedReason.ProductionUomAmbiguous);
        }

        decimal? qualityRate = outputUomCode is not null && totalOutputQuantity > 0m
            ? decimal.Divide(goodQuantity, totalOutputQuantity)
            : null;
        if (qualityRate is null)
        {
            degradedReasons.Add(OeeAggregateDegradedReason.ProductionOutputMissing);
        }

        decimal expectedOutputQuantity = 0m;
        var hasUsableTheory = true;
        foreach (var deviceGroup in group.Facts.GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal))
        {
            if (!productiveHoursByDevice.TryGetValue(deviceGroup.Key, out var productiveHours))
            {
                hasUsableTheory = false;
                break;
            }

            var rates = deviceGroup.Select(x => x.TheoreticalRatePerHour).Where(x => x is > 0m).Select(x => x!.Value).Distinct().ToArray();
            if (rates.Length != 1 || deviceGroup.Any(x => x.TheoreticalRatePerHour is not > 0m))
            {
                hasUsableTheory = false;
                break;
            }

            expectedOutputQuantity += productiveHours * rates[0];
        }

        decimal? performanceRate = hasUsableTheory && outputUomCode is not null && expectedOutputQuantity > 0m && totalOutputQuantity > 0m
            ? decimal.Divide(totalOutputQuantity, expectedOutputQuantity)
            : null;
        if (!hasUsableTheory)
        {
            degradedReasons.Add(OeeAggregateDegradedReason.TheoreticalRateMissingOrAmbiguous);
        }
        if (productiveTicks <= 0)
        {
            degradedReasons.Add(OeeAggregateDegradedReason.ProductiveRuntimeMissing);
        }

        decimal? availabilityRate = loadingTicks > 0
            ? decimal.Divide(productiveTicks, loadingTicks)
            : null;
        if (availabilityRate is null)
        {
            degradedReasons.Add(OeeAggregateDegradedReason.LoadingRuntimeMissing);
        }

        if (!hasCompleteRuntimeCoverage)
        {
            availabilityRate = null;
            performanceRate = null;
            qualityRate = null;
        }

        AddHistoricalDimensionDegradation(group.Facts, dimension, degradedReasons);
        if (group.Facts.Any(x => x.HistoricalDimensionStatus != OeeHistoricalDimensionStatus.Resolved))
        {
            availabilityRate = null;
            performanceRate = null;
            qualityRate = null;
        }

        var siteCode = SingleValue(group.Facts.Select(x => x.SiteCode));
        var workshopCode = SingleValue(group.Facts.Select(x => x.WorkshopCode));
        var lineCode = SingleValue(group.Facts.Select(x => x.LineCode));
        var workCenterId = SingleValue(group.Facts.Select(x => x.WorkCenterId));
        var deviceAssetId = dimension == OeeAggregateDimension.Device
            ? group.Key.DimensionValue
            : SingleValue(group.Facts.Select(x => x.DeviceAssetId));
        var shiftCode = SingleValue(group.Facts.Select(x => x.ShiftCode));
        decimal? oeeRate = availabilityRate is not null && performanceRate is not null && qualityRate is not null
            ? availabilityRate.Value * performanceRate.Value * qualityRate.Value
            : null;
        return new OeeAggregateBucket(
            dimension,
            group.Key.DimensionValue,
            siteCode,
            workshopCode,
            lineCode,
            workCenterId,
            deviceAssetId,
            shiftCode,
            group.Key.BusinessDate,
            group.StartUtc,
            group.EndUtc,
            deviceIds.Length,
            stateSampleCount,
            group.Facts.Length,
            Round(availabilityRate),
            Round(performanceRate),
            Round(qualityRate),
            Round(oeeRate),
            Math.Round(goodQuantity, 6),
            Math.Round(scrapQuantity, 6),
            Math.Round(reworkQuantity, 6),
            outputUomCode,
            performanceRate is null ? null : Math.Round(expectedOutputQuantity, 6),
            degradedReasons.Count > 0,
            degradedReasons.OrderBy(x => x).ToArray());
    }

    private static void EnsureStateMaterializationLimit(int materializedStateCount)
    {
        if (materializedStateCount > OeeAggregateMaterializationLimits.MaximumStateSampleCount)
        {
            throw new KnownException(
                $"OEE aggregate window exceeds the {OeeAggregateMaterializationLimits.MaximumStateSampleCount} device-state materialization limit; narrow the window or add dimension filters.");
        }
    }

    private static void EnsureProductionFactMaterializationLimit(int materializedFactCount)
    {
        if (materializedFactCount > OeeAggregateMaterializationLimits.MaximumProductionFactCount)
        {
            throw new KnownException(
                $"OEE aggregate window exceeds the {OeeAggregateMaterializationLimits.MaximumProductionFactCount} production-fact materialization limit; narrow the window or add dimension filters.");
        }
    }

    private static RuntimeTotals CalculateRuntime(
        IReadOnlyList<DeviceStateSnapshot> states,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var carryIn = states.LastOrDefault(x => x.OccurredAtUtc < startUtc);
        var inWindow = states.Where(x => x.OccurredAtUtc >= startUtc && x.OccurredAtUtc < endUtc).ToArray();
        var points = carryIn is null
            ? inWindow.Select(x => new StatePoint(x.OccurredAtUtc, x.State)).ToArray()
            : new[] { new StatePoint(startUtc, carryIn.State) }
                .Concat(inWindow.Select(x => new StatePoint(x.OccurredAtUtc, x.State)))
                .ToArray();
        long loadingTicks = 0;
        long productiveTicks = 0;
        for (var index = 0; index < points.Length; index++)
        {
            var segmentStart = points[index].OccurredAtUtc < startUtc ? startUtc : points[index].OccurredAtUtc;
            var segmentEnd = index + 1 < points.Length ? points[index + 1].OccurredAtUtc : endUtc;
            if (segmentEnd <= segmentStart || EquipmentRuntimeDeviceStates.IsPlannedDownState(points[index].State))
            {
                continue;
            }

            var ticks = segmentEnd.UtcTicks - segmentStart.UtcTicks;
            loadingTicks += ticks;
            if (EquipmentRuntimeDeviceStates.IsProductiveRuntime(points[index].State))
            {
                productiveTicks += ticks;
            }
        }

        var hasCompleteCoverage = carryIn is not null ||
            inWindow.FirstOrDefault()?.OccurredAtUtc == startUtc;
        return new RuntimeTotals(loadingTicks, productiveTicks, points.Length, hasCompleteCoverage);
    }

    private static void AddHistoricalDimensionDegradation(
        IReadOnlyCollection<OeeProductionFact> facts,
        OeeAggregateDimension dimension,
        ISet<OeeAggregateDegradedReason> reasons)
    {
        foreach (var status in facts
            .Select(x => x.HistoricalDimensionStatus)
            .Where(x => x != OeeHistoricalDimensionStatus.Resolved)
            .Distinct())
        {
            reasons.Add(status switch
            {
                OeeHistoricalDimensionStatus.LegacyUnresolved => OeeAggregateDegradedReason.HistoricalDimensionLegacyUnresolved,
                OeeHistoricalDimensionStatus.MissingHierarchy => OeeAggregateDegradedReason.HistoricalHierarchyMissing,
                OeeHistoricalDimensionStatus.MissingTimezone => OeeAggregateDegradedReason.HistoricalTimezoneMissing,
                OeeHistoricalDimensionStatus.InvalidTimezone => OeeAggregateDegradedReason.HistoricalTimezoneInvalid,
                OeeHistoricalDimensionStatus.MissingShiftDefinition => OeeAggregateDegradedReason.HistoricalShiftDefinitionMissing,
                OeeHistoricalDimensionStatus.InvalidShiftDefinition => OeeAggregateDegradedReason.HistoricalShiftDefinitionInvalid,
                OeeHistoricalDimensionStatus.ReportOutsideShiftWindow => OeeAggregateDegradedReason.HistoricalReportOutsideShiftWindow,
                OeeHistoricalDimensionStatus.InvalidLocalTime => OeeAggregateDegradedReason.HistoricalLocalTimeInvalid,
                OeeHistoricalDimensionStatus.AmbiguousLocalTime => OeeAggregateDegradedReason.HistoricalLocalTimeAmbiguous,
                _ => throw new UnreachableException(),
            });
        }

        if (facts.Any(x => string.IsNullOrWhiteSpace(x.SiteCode))) reasons.Add(OeeAggregateDegradedReason.SiteDimensionMissing);
        if (facts.Any(x => string.IsNullOrWhiteSpace(x.WorkshopCode))) reasons.Add(OeeAggregateDegradedReason.WorkshopDimensionMissing);
        if (facts.Any(x => string.IsNullOrWhiteSpace(x.LineCode))) reasons.Add(OeeAggregateDegradedReason.LineDimensionMissing);
        if (HasMultipleValues(facts.Select(x => x.SiteCode))) reasons.Add(OeeAggregateDegradedReason.SiteDimensionAmbiguous);
        if (HasMultipleValues(facts.Select(x => x.WorkshopCode))) reasons.Add(OeeAggregateDegradedReason.WorkshopDimensionAmbiguous);
        if (HasMultipleValues(facts.Select(x => x.LineCode))) reasons.Add(OeeAggregateDegradedReason.LineDimensionAmbiguous);
        if (dimension == OeeAggregateDimension.Day && facts.Any(x => x.BusinessDate is null || string.IsNullOrWhiteSpace(x.SiteTimezone)))
        {
            reasons.Add(OeeAggregateDegradedReason.SiteTimezoneOrDayBoundaryMissing);
        }
        if (dimension == OeeAggregateDimension.Shift && facts.Any(x => x.BusinessDate is null || x.ShiftBucketStartUtc is null || x.ShiftBucketEndUtc is null))
        {
            reasons.Add(OeeAggregateDegradedReason.ShiftDefinitionOrBoundaryMissing);
        }
    }

    private static string? SingleValue(IEnumerable<string?> values)
    {
        var distinct = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static bool HasMultipleValues(IEnumerable<string?> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(2).Count() > 1;

    private static decimal? Round(decimal? value) => value is null ? null : Math.Round(value.Value, 6);

    private static OeeAggregateBucketsResponse Response(
        QueryOeeAggregateBucketsQuery request,
        IReadOnlyCollection<OeeAggregateBucket> buckets,
        int totalCount = 0) =>
        new(
            request.OrganizationId,
            request.EnvironmentId,
            request.Dimension,
            request.WindowStartUtc,
            request.WindowEndUtc,
            buckets,
            totalCount,
            request.Skip,
            request.Take);

    private sealed record RuntimeTotals(long LoadingTicks, long ProductiveTicks, int StateSampleCount, bool HasCompleteCoverage);
    private sealed record StatePoint(DateTimeOffset OccurredAtUtc, string State);
    private sealed record RuntimeWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
    private sealed record FactBucket(BucketKey Key, OeeProductionFact[] Facts, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
    private sealed record DeviceBucketKey(string DeviceId, BucketKey Bucket);

    private sealed record BucketKey(
        string? DimensionValue,
        string? SiteCode,
        string? WorkshopCode,
        string? LineCode,
        DateOnly? BusinessDate,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc)
    {
        internal static string? DimensionValueFor(OeeProductionFact fact, OeeAggregateDimension dimension) =>
            dimension switch
            {
                OeeAggregateDimension.Device => fact.DeviceAssetId,
                OeeAggregateDimension.WorkCenter => fact.WorkCenterId,
                OeeAggregateDimension.Line => fact.LineCode,
                OeeAggregateDimension.Workshop => fact.WorkshopCode,
                OeeAggregateDimension.Shift => fact.ShiftCode,
                OeeAggregateDimension.Day => fact.SiteCode,
                _ => throw new UnreachableException(),
            };

        public static BucketKey From(OeeProductionFact fact, QueryOeeAggregateBucketsQuery request) =>
            request.Dimension switch
            {
                OeeAggregateDimension.Device => new(fact.DeviceAssetId, null, null, null, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimension.WorkCenter => new(fact.WorkCenterId, fact.SiteCode, fact.WorkshopCode, fact.LineCode, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimension.Line => new(fact.LineCode, fact.SiteCode, fact.WorkshopCode, fact.LineCode, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimension.Workshop => new(fact.WorkshopCode, fact.SiteCode, fact.WorkshopCode, null, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimension.Shift when fact.BusinessDate is not null && fact.ShiftBucketStartUtc is not null && fact.ShiftBucketEndUtc is not null =>
                    new(fact.ShiftCode, fact.SiteCode, fact.WorkshopCode, fact.LineCode, fact.BusinessDate, fact.ShiftBucketStartUtc.Value, fact.ShiftBucketEndUtc.Value),
                OeeAggregateDimension.Day when TryGetBusinessDayBounds(fact, out var dayStartUtc, out var dayEndUtc) =>
                    new(fact.SiteCode, fact.SiteCode, null, null, fact.BusinessDate, dayStartUtc, dayEndUtc),
                _ => new(null, null, null, null, null, request.WindowStartUtc, request.WindowEndUtc),
            };

        private static bool TryGetBusinessDayBounds(
            OeeProductionFact fact,
            out DateTimeOffset startUtc,
            out DateTimeOffset endUtc)
        {
            startUtc = default;
            endUtc = default;
            if (fact.BusinessDate is null || string.IsNullOrWhiteSpace(fact.SiteTimezone))
            {
                return false;
            }

            TimeZoneInfo timezone;
            try
            {
                timezone = TimeZoneInfo.FindSystemTimeZoneById(fact.SiteTimezone);
            }
            catch (TimeZoneNotFoundException)
            {
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                return false;
            }

            var localStart = DateTime.SpecifyKind(fact.BusinessDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var localEnd = DateTime.SpecifyKind(fact.BusinessDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            if (timezone.IsInvalidTime(localStart) ||
                timezone.IsInvalidTime(localEnd) ||
                timezone.IsAmbiguousTime(localStart) ||
                timezone.IsAmbiguousTime(localEnd))
            {
                return false;
            }

            startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timezone), TimeSpan.Zero);
            endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone), TimeSpan.Zero);
            return true;
        }
    }
}
