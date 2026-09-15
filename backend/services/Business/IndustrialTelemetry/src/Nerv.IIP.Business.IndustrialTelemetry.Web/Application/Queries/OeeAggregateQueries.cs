using FluentValidation;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq.Expressions;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.IndustrialTelemetry;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public static class OeeAggregateMaterializationLimits
{
    public const int MaximumProductionFactCount = 10_000;
    public const int MaximumStateSampleCount = 10_000;
}

internal static class OeeAggregateQueryPlan
{
    internal static async Task<MaterializedProductionFactSet> LoadFactsAsync(
        ApplicationDbContext dbContext,
        QueryOeeAggregateBucketsQuery request,
        CancellationToken cancellationToken)
    {
        var scopedFacts = dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.AggregationOccurredAtUtc >= request.WindowStartUtc)
            .Where(x => x.AggregationOccurredAtUtc < request.WindowEndUtc);
        var selection = SelectionFor(request);
        var selectedDeviceIds = scopedFacts
            .Where(selection)
            .Select(x => x.DeviceAssetId)
            .Distinct();
        var contextFacts = await scopedFacts
            .Where(x => selectedDeviceIds.Contains(x.DeviceAssetId))
            .OrderBy(x => x.AggregationOccurredAtUtc)
            .ThenBy(x => x.SourceReportNo)
            .Take(OeeAggregateMaterializationLimits.MaximumProductionFactCount + 1)
            .ToArrayAsync(cancellationToken);
        if (contextFacts.Length > OeeAggregateMaterializationLimits.MaximumProductionFactCount)
        {
            throw new KnownException(
                $"OEE aggregate window exceeds the {OeeAggregateMaterializationLimits.MaximumProductionFactCount} production-fact materialization limit; narrow the window or add dimension filters.");
        }

        var matchesSelection = selection.Compile();
        return new MaterializedProductionFactSet(
            contextFacts,
            contextFacts.Where(matchesSelection).ToArray());
    }

    private static Expression<Func<OeeProductionFact, bool>> SelectionFor(
        QueryOeeAggregateBucketsQuery request) =>
        x =>
            (request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId) &&
            (request.WorkCenterId == null || x.WorkCenterId == request.WorkCenterId) &&
            (request.ShiftCode == null || x.ShiftCode == request.ShiftCode) &&
            (request.LineCode == null || x.LineCode == request.LineCode) &&
            (request.WorkshopCode == null || x.WorkshopCode == request.WorkshopCode) &&
            (request.BusinessDate == null || x.BusinessDate == request.BusinessDate);

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

internal sealed record MaterializedProductionFactSet(
    OeeProductionFact[] ContextFacts,
    OeeProductionFact[] SelectedFacts);

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
        var materializedFacts = await OeeAggregateQueryPlan.LoadFactsAsync(dbContext, request, cancellationToken);
        var facts = materializedFacts.SelectedFacts;
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
        var runtimeWindows = BuildRuntimeWindows(materializedFacts.ContextFacts, groups, request);

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
        IReadOnlyCollection<OeeProductionFact> contextFacts,
        IReadOnlyCollection<FactBucket> groups,
        QueryOeeAggregateBucketsQuery request)
    {
        var windows = new Dictionary<DeviceBucketKey, List<RuntimeWindow>>();
        var segmentsByDevice = contextFacts
            .GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => BuildRuntimeOwnership(x, request),
                StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var deviceId in group.Facts.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal))
            {
                foreach (var segment in segmentsByDevice[deviceId].Hierarchy)
                {
                    if (!MatchesHierarchyFilters(segment.Fact, request) ||
                        !SegmentBelongsToBucket(segment.Fact, group.Key, request))
                    {
                        continue;
                    }

                    var eligibleWindows = new[] { new RuntimeWindow(segment.StartUtc, segment.EndUtc) };
                    if (request.ShiftCode is not null)
                    {
                        eligibleWindows = IntersectWindows(eligibleWindows, segmentsByDevice[deviceId].Shifts
                            .Where(x => x.Fact.ShiftCode == request.ShiftCode)
                            .Select(x => new RuntimeWindow(x.StartUtc, x.EndUtc)));
                    }
                    if (request.BusinessDate is not null)
                    {
                        eligibleWindows = IntersectWindows(eligibleWindows, segmentsByDevice[deviceId].BusinessDays
                            .Where(x => x.Fact.BusinessDate == request.BusinessDate)
                            .Select(x => new RuntimeWindow(x.StartUtc, x.EndUtc)));
                    }
                    foreach (var eligibleWindow in eligibleWindows)
                    {
                        AddWindow(deviceId, group.Key, eligibleWindow.StartUtc, eligibleWindow.EndUtc, group.StartUtc, group.EndUtc);
                    }
                }
            }
        }

        return windows.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RuntimeWindow>)x.Value,
            EqualityComparer<DeviceBucketKey>.Default);

        static RuntimeOwnership BuildRuntimeOwnership(
            IEnumerable<OeeProductionFact> deviceFacts,
            QueryOeeAggregateBucketsQuery request)
        {
            var orderedFacts = deviceFacts
                .OrderBy(x => x.AggregationOccurredAtUtc)
                .ThenBy(x => x.SourceReportNo, StringComparer.Ordinal)
                .ToArray();
            return new(
                BuildSegments(HierarchyKey.From, static _ => null),
                BuildSegments(ShiftKey.From, static fact =>
                    fact.ShiftBucketStartUtc is not null && fact.ShiftBucketEndUtc is not null
                        ? new RuntimeWindow(fact.ShiftBucketStartUtc.Value, fact.ShiftBucketEndUtc.Value)
                        : null),
                BuildSegments(BusinessDayKey.From, static fact =>
                    BucketKey.TryGetBusinessDayBounds(fact, out var startUtc, out var endUtc)
                        ? new RuntimeWindow(startUtc, endUtc)
                        : null));

            IReadOnlyList<RuntimeOwnershipSegment> BuildSegments<TKey>(
                Func<OeeProductionFact, TKey> keySelector,
                Func<OeeProductionFact, RuntimeWindow?> authoritativeWindow)
                where TKey : notnull
            {
                var segments = new List<RuntimeOwnershipSegment>();
                var currentFact = orderedFacts[0];
                var currentKey = keySelector(currentFact);
                var currentStart = request.WindowStartUtc;
                foreach (var fact in orderedFacts.Skip(1))
                {
                    var nextKey = keySelector(fact);
                    if (EqualityComparer<TKey>.Default.Equals(nextKey, currentKey))
                    {
                        continue;
                    }

                    AddSegment(currentFact, currentStart, fact.AggregationOccurredAtUtc);
                    var currentWindow = authoritativeWindow(currentFact);
                    var nextWindow = authoritativeWindow(fact);
                    currentStart = currentWindow is not null && nextWindow is not null &&
                                   currentWindow.EndUtc <= nextWindow.StartUtc
                        ? nextWindow.StartUtc
                        : fact.AggregationOccurredAtUtc;
                    currentFact = fact;
                    currentKey = nextKey;
                }
                AddSegment(currentFact, currentStart, request.WindowEndUtc);
                return segments;

                void AddSegment(OeeProductionFact fact, DateTimeOffset startUtc, DateTimeOffset endUtc)
                {
                    var window = authoritativeWindow(fact);
                    var clippedStart = startUtc < request.WindowStartUtc ? request.WindowStartUtc : startUtc;
                    var clippedEnd = endUtc > request.WindowEndUtc ? request.WindowEndUtc : endUtc;
                    if (window is not null)
                    {
                        clippedStart = clippedStart < window.StartUtc ? window.StartUtc : clippedStart;
                        clippedEnd = clippedEnd > window.EndUtc ? window.EndUtc : clippedEnd;
                    }
                    if (clippedEnd > clippedStart)
                    {
                        segments.Add(new RuntimeOwnershipSegment(fact, clippedStart, clippedEnd));
                    }
                }
            }
        }

        static RuntimeWindow[] IntersectWindows(
            IEnumerable<RuntimeWindow> left,
            IEnumerable<RuntimeWindow> right) =>
            (from leftWindow in left
             from rightWindow in right
             let startUtc = leftWindow.StartUtc > rightWindow.StartUtc ? leftWindow.StartUtc : rightWindow.StartUtc
             let endUtc = leftWindow.EndUtc < rightWindow.EndUtc ? leftWindow.EndUtc : rightWindow.EndUtc
             where endUtc > startUtc
             select new RuntimeWindow(startUtc, endUtc)).ToArray();

        void AddWindow(
            string deviceId,
            BucketKey key,
            DateTimeOffset segmentStartUtc,
            DateTimeOffset segmentEndUtc,
            DateTimeOffset bucketStartUtc,
            DateTimeOffset bucketEndUtc)
        {
            var clippedStart = segmentStartUtc < bucketStartUtc ? bucketStartUtc : segmentStartUtc;
            var clippedEnd = segmentEndUtc > bucketEndUtc ? bucketEndUtc : segmentEndUtc;
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
            if (deviceWindows.Count > 0 && deviceWindows[^1].EndUtc == clippedStart)
            {
                deviceWindows[^1] = deviceWindows[^1] with { EndUtc = clippedEnd };
                return;
            }
            deviceWindows.Add(new RuntimeWindow(clippedStart, clippedEnd));
        }
    }

    private static bool MatchesHierarchyFilters(
        OeeProductionFact fact,
        QueryOeeAggregateBucketsQuery request) =>
        (request.WorkCenterId is null || fact.WorkCenterId == request.WorkCenterId) &&
        (request.LineCode is null || fact.LineCode == request.LineCode) &&
        (request.WorkshopCode is null || fact.WorkshopCode == request.WorkshopCode);

    private static bool SegmentBelongsToBucket(
        OeeProductionFact fact,
        BucketKey bucket,
        QueryOeeAggregateBucketsQuery request) =>
        request.Dimension is not (OeeAggregateDimension.WorkCenter or OeeAggregateDimension.Line or OeeAggregateDimension.Workshop) ||
        BucketKey.From(fact, request) == bucket;

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
        long changeoverTicks = 0;
        var stateSampleCount = 0;
        var productiveHoursByDevice = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var deviceId in deviceIds)
        {
            statesByDevice.TryGetValue(deviceId, out var deviceStates);
            var deviceLoadingTicks = 0L;
            var deviceProductiveTicks = 0L;
            var deviceChangeoverTicks = 0L;
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
                deviceChangeoverTicks += runtime.ChangeoverTicks;
                deviceStateSampleCount += runtime.StateSampleCount;
                deviceHasCompleteCoverage &= runtime.HasCompleteCoverage;
            }
            loadingTicks += deviceLoadingTicks;
            productiveTicks += deviceProductiveTicks;
            changeoverTicks += deviceChangeoverTicks;
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
            degradedReasons.OrderBy(x => x).ToArray(),
            Math.Round(decimal.Divide(changeoverTicks, TimeSpan.TicksPerMinute), 6));
    }

    private static void EnsureStateMaterializationLimit(int materializedStateCount)
    {
        if (materializedStateCount > OeeAggregateMaterializationLimits.MaximumStateSampleCount)
        {
            throw new KnownException(
                $"OEE aggregate window exceeds the {OeeAggregateMaterializationLimits.MaximumStateSampleCount} device-state materialization limit; narrow the window or add dimension filters.");
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
        long changeoverTicks = 0;
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
            if (EquipmentRuntimeDeviceStates.IsChangeoverState(points[index].State))
            {
                changeoverTicks += ticks;
            }
            if (EquipmentRuntimeDeviceStates.IsProductiveRuntime(points[index].State))
            {
                productiveTicks += ticks;
            }
        }

        var hasCompleteCoverage = carryIn is not null ||
            inWindow.FirstOrDefault()?.OccurredAtUtc == startUtc;
        return new RuntimeTotals(loadingTicks, productiveTicks, changeoverTicks, points.Length, hasCompleteCoverage);
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

    private sealed record RuntimeTotals(long LoadingTicks, long ProductiveTicks, long ChangeoverTicks, int StateSampleCount, bool HasCompleteCoverage);
    private sealed record StatePoint(DateTimeOffset OccurredAtUtc, string State);
    private sealed record RuntimeWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
    private sealed record RuntimeOwnership(
        IReadOnlyList<RuntimeOwnershipSegment> Hierarchy,
        IReadOnlyList<RuntimeOwnershipSegment> Shifts,
        IReadOnlyList<RuntimeOwnershipSegment> BusinessDays);
    private sealed record RuntimeOwnershipSegment(OeeProductionFact Fact, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
    private sealed record HierarchyKey(string WorkCenterId, string? SiteCode, string? WorkshopCode, string? LineCode)
    {
        internal static HierarchyKey From(OeeProductionFact fact) =>
            new(fact.WorkCenterId, fact.SiteCode, fact.WorkshopCode, fact.LineCode);
    }
    private sealed record ShiftKey(string? ShiftCode, DateTimeOffset? StartUtc, DateTimeOffset? EndUtc)
    {
        internal static ShiftKey From(OeeProductionFact fact) =>
            new(fact.ShiftCode, fact.ShiftBucketStartUtc, fact.ShiftBucketEndUtc);
    }
    private sealed record BusinessDayKey(DateOnly? BusinessDate, string? SiteTimezone)
    {
        internal static BusinessDayKey From(OeeProductionFact fact) => new(fact.BusinessDate, fact.SiteTimezone);
    }
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

        internal static bool TryGetBusinessDayBounds(
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
