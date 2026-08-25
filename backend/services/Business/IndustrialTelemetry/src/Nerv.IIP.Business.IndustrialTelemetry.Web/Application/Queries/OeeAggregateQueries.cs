using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public static class OeeAggregateDimensions
{
    public const string Device = "device";
    public const string WorkCenter = "work-center";
    public const string Line = "line";
    public const string Workshop = "workshop";
    public const string Shift = "shift";
    public const string Day = "day";
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public static bool IsSupported(string value) =>
        value is Device or WorkCenter or Line or Workshop or Shift or Day;
}

public sealed record QueryOeeAggregateBucketsQuery(
    string OrganizationId,
    string EnvironmentId,
    string Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetId = null,
    string? WorkCenterId = null,
    string? ShiftCode = null,
    string? LineCode = null,
    string? WorkshopCode = null) : IQuery<OeeAggregateBucketsResponse>;

public sealed record OeeAggregateBucketsResponse(
    string OrganizationId,
    string EnvironmentId,
    string Dimension,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<OeeAggregateBucket> Buckets);

public sealed record OeeAggregateBucket(
    string Dimension,
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
    IReadOnlyCollection<string> DegradedReasons);

public sealed class QueryOeeAggregateBucketsQueryValidator : AbstractValidator<QueryOeeAggregateBucketsQuery>
{
    public QueryOeeAggregateBucketsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Dimension).Must(OeeAggregateDimensions.IsSupported);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.WindowEndUtc)
            .Must((query, endUtc) => endUtc - query.WindowStartUtc <= OeeAggregateDimensions.MaximumWindow)
            .WithMessage("OEE aggregate window cannot exceed 31 days.");
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.ShiftCode).MaximumLength(100);
        RuleFor(x => x.LineCode).MaximumLength(100);
        RuleFor(x => x.WorkshopCode).MaximumLength(100);
    }
}

public sealed class QueryOeeAggregateBucketsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryOeeAggregateBucketsQuery, OeeAggregateBucketsResponse>
{
    public async Task<OeeAggregateBucketsResponse> Handle(
        QueryOeeAggregateBucketsQuery request,
        CancellationToken cancellationToken)
    {
        var facts = await dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.ReportedAtUtc >= request.WindowStartUtc)
            .Where(x => x.ReportedAtUtc < request.WindowEndUtc)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.WorkCenterId == null || x.WorkCenterId == request.WorkCenterId)
            .Where(x => request.ShiftCode == null || x.ShiftCode == request.ShiftCode)
            .Where(x => request.LineCode == null || x.LineCode == request.LineCode)
            .Where(x => request.WorkshopCode == null || x.WorkshopCode == request.WorkshopCode)
            .ToArrayAsync(cancellationToken);
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
            .ToArray();
        if (groups.Length == 0)
        {
            return Response(request, []);
        }

        var deviceIds = facts.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).ToArray();
        var earliestStart = groups.Min(x => x.StartUtc);
        var latestEnd = groups.Max(x => x.EndUtc);
        var carryIns = await dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => deviceIds.Contains(x.DeviceAssetId))
            .Where(x => x.OccurredAtUtc < earliestStart)
            .GroupBy(x => x.DeviceAssetId)
            .Select(group => group.OrderByDescending(x => x.OccurredAtUtc).First())
            .ToArrayAsync(cancellationToken);
        var inWindowStates = await dbContext.DeviceStateSnapshots
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => deviceIds.Contains(x.DeviceAssetId))
            .Where(x => x.OccurredAtUtc >= earliestStart)
            .Where(x => x.OccurredAtUtc < latestEnd)
            .OrderBy(x => x.OccurredAtUtc)
            .ToArrayAsync(cancellationToken);
        var statesByDevice = carryIns
            .Concat(inWindowStates)
            .GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<DeviceStateSnapshot>)x.OrderBy(y => y.OccurredAtUtc).ToArray(), StringComparer.Ordinal);

        var buckets = groups
            .Select(group => CalculateBucket(request.Dimension, group, statesByDevice))
            .ToArray();
        return Response(request, buckets);
    }

    private static OeeAggregateBucket CalculateBucket(
        string dimension,
        FactBucket group,
        IReadOnlyDictionary<string, IReadOnlyList<DeviceStateSnapshot>> statesByDevice)
    {
        var degradedReasons = new HashSet<string>(StringComparer.Ordinal);
        var deviceIds = group.Facts.Select(x => x.DeviceAssetId).Distinct(StringComparer.Ordinal).ToArray();
        long loadingTicks = 0;
        long productiveTicks = 0;
        var stateSampleCount = 0;
        var productiveHoursByDevice = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var deviceId in deviceIds)
        {
            statesByDevice.TryGetValue(deviceId, out var deviceStates);
            var runtime = CalculateRuntime(deviceStates ?? [], group.StartUtc, group.EndUtc);
            loadingTicks += runtime.LoadingTicks;
            productiveTicks += runtime.ProductiveTicks;
            stateSampleCount += runtime.StateSampleCount;
            productiveHoursByDevice[deviceId] = decimal.Divide(runtime.ProductiveTicks, TimeSpan.TicksPerHour);
            if (!runtime.HasState)
            {
                degradedReasons.Add("runtime-state-facts-missing");
            }
        }

        var goodQuantity = group.Facts.Sum(x => x.GoodQuantity);
        var scrapQuantity = group.Facts.Sum(x => x.ScrapQuantity);
        var reworkQuantity = group.Facts.Sum(x => x.ReworkQuantity);
        var totalOutputQuantity = goodQuantity + scrapQuantity + reworkQuantity;
        var uomCodes = group.Facts.Select(x => x.UomCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var outputUomCode = uomCodes.Length == 1 ? uomCodes[0] : null;
        if (outputUomCode is null)
        {
            degradedReasons.Add("production-uom-ambiguous");
        }

        decimal? qualityRate = outputUomCode is not null && totalOutputQuantity > 0m
            ? decimal.Divide(goodQuantity, totalOutputQuantity)
            : null;
        if (qualityRate is null)
        {
            degradedReasons.Add("production-output-missing");
        }

        decimal expectedOutputQuantity = 0m;
        var hasUsableTheory = true;
        foreach (var deviceGroup in group.Facts.GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal))
        {
            var rates = deviceGroup.Select(x => x.TheoreticalRatePerHour).Where(x => x is > 0m).Select(x => x!.Value).Distinct().ToArray();
            if (rates.Length != 1 || deviceGroup.Any(x => x.TheoreticalRatePerHour is not > 0m))
            {
                hasUsableTheory = false;
                break;
            }

            expectedOutputQuantity += productiveHoursByDevice[deviceGroup.Key] * rates[0];
        }

        decimal? performanceRate = hasUsableTheory && outputUomCode is not null && expectedOutputQuantity > 0m && totalOutputQuantity > 0m
            ? decimal.Divide(totalOutputQuantity, expectedOutputQuantity)
            : null;
        if (!hasUsableTheory)
        {
            degradedReasons.Add("theoretical-rate-missing-or-ambiguous");
        }
        if (productiveTicks <= 0)
        {
            degradedReasons.Add("productive-runtime-missing");
        }

        decimal? availabilityRate = loadingTicks > 0
            ? decimal.Divide(productiveTicks, loadingTicks)
            : null;
        if (availabilityRate is null)
        {
            degradedReasons.Add("loading-runtime-missing");
        }

        AddHistoricalDimensionDegradation(group.Facts, dimension, degradedReasons);
        var siteCode = SingleValue(group.Facts.Select(x => x.SiteCode));
        var workshopCode = SingleValue(group.Facts.Select(x => x.WorkshopCode));
        var lineCode = SingleValue(group.Facts.Select(x => x.LineCode));
        var workCenterId = SingleValue(group.Facts.Select(x => x.WorkCenterId));
        var deviceAssetId = dimension == OeeAggregateDimensions.Device
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
            degradedReasons.OrderBy(x => x, StringComparer.Ordinal).ToArray());
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

        return new RuntimeTotals(loadingTicks, productiveTicks, points.Length, points.Length > 0);
    }

    private static void AddHistoricalDimensionDegradation(
        IReadOnlyCollection<OeeProductionFact> facts,
        string dimension,
        ISet<string> reasons)
    {
        if (facts.Any(x => string.IsNullOrWhiteSpace(x.SiteCode))) reasons.Add("site-dimension-missing");
        if (facts.Any(x => string.IsNullOrWhiteSpace(x.WorkshopCode))) reasons.Add("workshop-dimension-missing");
        if (facts.Any(x => string.IsNullOrWhiteSpace(x.LineCode))) reasons.Add("line-dimension-missing");
        if (HasMultipleValues(facts.Select(x => x.SiteCode))) reasons.Add("site-dimension-ambiguous");
        if (HasMultipleValues(facts.Select(x => x.WorkshopCode))) reasons.Add("workshop-dimension-ambiguous");
        if (HasMultipleValues(facts.Select(x => x.LineCode))) reasons.Add("line-dimension-ambiguous");
        if (dimension == OeeAggregateDimensions.Day && facts.Any(x => x.BusinessDate is null || x.DayBucketStartUtc is null || x.DayBucketEndUtc is null))
        {
            reasons.Add("site-timezone-or-day-boundary-missing");
        }
        if (dimension == OeeAggregateDimensions.Shift && facts.Any(x => x.ShiftBusinessDate is null || x.ShiftBucketStartUtc is null || x.ShiftBucketEndUtc is null))
        {
            reasons.Add("shift-definition-or-boundary-missing");
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
        IReadOnlyCollection<OeeAggregateBucket> buckets) =>
        new(request.OrganizationId, request.EnvironmentId, request.Dimension, request.WindowStartUtc, request.WindowEndUtc, buckets);

    private sealed record RuntimeTotals(long LoadingTicks, long ProductiveTicks, int StateSampleCount, bool HasState);
    private sealed record StatePoint(DateTimeOffset OccurredAtUtc, string State);
    private sealed record FactBucket(BucketKey Key, OeeProductionFact[] Facts, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    private sealed record BucketKey(
        string? DimensionValue,
        DateOnly? BusinessDate,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc)
    {
        public static BucketKey From(OeeProductionFact fact, QueryOeeAggregateBucketsQuery request) =>
            request.Dimension switch
            {
                OeeAggregateDimensions.Device => new(fact.DeviceAssetId, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimensions.WorkCenter => new(fact.WorkCenterId, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimensions.Line => new(fact.LineCode, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimensions.Workshop => new(fact.WorkshopCode, null, request.WindowStartUtc, request.WindowEndUtc),
                OeeAggregateDimensions.Shift when fact.ShiftBucketStartUtc is not null && fact.ShiftBucketEndUtc is not null =>
                    new(fact.ShiftCode, fact.ShiftBusinessDate, fact.ShiftBucketStartUtc.Value, fact.ShiftBucketEndUtc.Value),
                OeeAggregateDimensions.Day when fact.DayBucketStartUtc is not null && fact.DayBucketEndUtc is not null =>
                    new(fact.SiteCode, fact.BusinessDate, fact.DayBucketStartUtc.Value, fact.DayBucketEndUtc.Value),
                _ => new(null, null, request.WindowStartUtc, request.WindowEndUtc),
            };
    }
}
