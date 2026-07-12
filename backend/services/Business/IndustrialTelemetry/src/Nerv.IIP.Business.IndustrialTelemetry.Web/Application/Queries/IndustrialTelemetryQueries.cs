using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record PagedListResponse<T>(IReadOnlyCollection<T> Items, int Total);

public sealed record ListTelemetryTagsQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, int Skip = 0, int Take = 100) : IQuery<PagedListResponse<TelemetryTagListItem>>;

public sealed record TelemetryTagListItem(
    TelemetryTagId TelemetryTagId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    string ValueType,
    string UnitCode,
    string SamplingPolicy,
    bool IsWritable,
    decimal? ControlMinValue,
    decimal? ControlMaxValue,
    IReadOnlyCollection<string> ControlAllowedValues);

public sealed class ListTelemetryTagsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTelemetryTagsQuery, PagedListResponse<TelemetryTagListItem>>
{
    public async Task<PagedListResponse<TelemetryTagListItem>> Handle(ListTelemetryTagsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TelemetryTags
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId);
        var total = await query.CountAsync(cancellationToken);
        // Project the raw allowed-values JSON column (ControlAllowedValues is a computed property that is
        // not translatable in LINQ) and deserialize after materialization.
        var projected = await query
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.TagKey)
            .Select(x => new TelemetryTagProjection(x.Id, x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.ValueType, x.UnitCode, x.SamplingPolicy, x.IsWritable, x.ControlMinValue, x.ControlMaxValue, x.ControlAllowedValuesJson))
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArrayAsync(cancellationToken);
        var items = projected
            .Select(x => new TelemetryTagListItem(
                x.TelemetryTagId,
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.TagKey,
                x.ValueType,
                x.UnitCode,
                x.SamplingPolicy,
                x.IsWritable,
                x.ControlMinValue,
                x.ControlMaxValue,
                DeserializeAllowedValues(x.ControlAllowedValuesJson)))
            .ToArray();
        return new PagedListResponse<TelemetryTagListItem>(items, total);
    }

    private static IReadOnlyCollection<string> DeserializeAllowedValues(string controlAllowedValuesJson)
    {
        if (string.IsNullOrWhiteSpace(controlAllowedValuesJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<IReadOnlyCollection<string>>(controlAllowedValuesJson) ?? [];
    }

    private sealed record TelemetryTagProjection(
        TelemetryTagId TelemetryTagId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string TagKey,
        string ValueType,
        string UnitCode,
        string SamplingPolicy,
        bool IsWritable,
        decimal? ControlMinValue,
        decimal? ControlMaxValue,
        string ControlAllowedValuesJson);
}

// Latest instantaneous tag value for the device-control write form. Sources the newest raw sample's
// LastValue (the last instantaneous reading in the bucket), not the bucket average, so operators see a
// real "current value" rather than an aggregate. HasSample is false when no raw sample exists yet.
public sealed record GetTelemetryTagCurrentValueQuery(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey)
    : IQuery<TelemetryTagCurrentValueResponse>;

public sealed record TelemetryTagCurrentValueResponse(
    string DeviceAssetId,
    string TagKey,
    bool HasSample,
    decimal? Value,
    DateTimeOffset? OccurredAtUtc);

public sealed class GetTelemetryTagCurrentValueQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetTelemetryTagCurrentValueQuery, TelemetryTagCurrentValueResponse>
{
    public async Task<TelemetryTagCurrentValueResponse> Handle(GetTelemetryTagCurrentValueQuery request, CancellationToken cancellationToken)
    {
        var normalizedTagKey = request.TagKey.Trim().ToLowerInvariant();
        var latest = await dbContext.TelemetryRawSamples
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId
                && x.TagKey == normalizedTagKey)
            .OrderByDescending(x => x.BucketEndUnixTimeMilliseconds)
            .Select(x => new { x.LastValue, x.BucketEndUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null
            ? new TelemetryTagCurrentValueResponse(request.DeviceAssetId, normalizedTagKey, false, null, null)
            : new TelemetryTagCurrentValueResponse(request.DeviceAssetId, normalizedTagKey, true, latest.LastValue, latest.BucketEndUtc);
    }
}

public sealed record ListAlarmRulesQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, bool? IsEnabled, int Skip = 0, int Take = 100) : IQuery<PagedListResponse<AlarmRuleListItem>>;

public sealed record AlarmRuleListItem(
    AlarmRuleId AlarmRuleId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string RuleCode,
    string AlarmCode,
    string Severity,
    string TagKey,
    string ComparisonOperator,
    decimal ThresholdValue,
    string UnitCode,
    bool IsEnabled,
    decimal DeadbandValue,
    int OnDelaySeconds,
    int OffDelaySeconds,
    int MinDurationSeconds,
    string Priority,
    DateTimeOffset UpdatedAtUtc);

public sealed class ListAlarmRulesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAlarmRulesQuery, PagedListResponse<AlarmRuleListItem>>
{
    public async Task<PagedListResponse<AlarmRuleListItem>> Handle(ListAlarmRulesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.AlarmRules
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.IsEnabled == null || x.IsEnabled == request.IsEnabled);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.RuleCode)
            .Select(x => new AlarmRuleListItem(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.RuleCode,
                x.AlarmCode,
                x.Severity,
                x.TagKey,
                x.ComparisonOperator,
                x.ThresholdValue,
                x.UnitCode,
                x.IsEnabled,
                x.DeadbandValue,
                x.OnDelaySeconds,
                x.OffDelaySeconds,
                x.MinDurationSeconds,
                x.Priority,
                x.UpdatedAtUtc))
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArrayAsync(cancellationToken);
        return new PagedListResponse<AlarmRuleListItem>(items, total);
    }
}

public sealed record ListAlarmEventsQuery(
    string? OrganizationId,
    string? EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? DeviceAssetIds = null) : IQuery<PagedListResponse<AlarmEventListItem>>;

public sealed record AlarmEventListItem(
    AlarmEventId AlarmEventId,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    string Priority,
    string? TagKey,
    decimal? ObservedValue,
    decimal? ThresholdValue,
    string? UnitCode,
    string Status,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset? ClearedAtUtc,
    string ExternalAlarmId,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy,
    DateTimeOffset? ShelvedAtUtc,
    DateTimeOffset? ShelvedUntilUtc,
    string? ShelvedBy,
    string? ShelveReason,
    DateTimeOffset? EscalatedAtUtc,
    string? EscalationReason,
    IReadOnlyCollection<string> EscalationRecipientRefs);

public sealed class ListAlarmEventsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAlarmEventsQuery, PagedListResponse<AlarmEventListItem>>
{
    public async Task<PagedListResponse<AlarmEventListItem>> Handle(ListAlarmEventsQuery request, CancellationToken cancellationToken)
    {
        var status = NormalizeStatus(request.Status);
        var deviceAssetIds = SplitCsv(request.DeviceAssetIds);
        var query = dbContext.AlarmEvents
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => deviceAssetIds.Count == 0 || deviceAssetIds.Contains(x.DeviceAssetId));
        query = status switch
        {
            null => query,
            "active" => query.Where(x => x.Status != "cleared"),
            _ => query.Where(x => x.Status == status),
        };
        var total = await query.CountAsync(cancellationToken);
        // Lifecycle priority BEFORE pagination so unacknowledged alarms always precede handled ones
        // across pages: raised(0) > shelved(1) > acknowledged(2) > (other/unknown 3) > cleared(4),
        // then newest-first. A CASE expression translates on PostgreSQL and the test providers.
        // Then a unique tie-breaker so alarms sharing a status + RaisedAtUtc (batch/bucket generation
        // produces identical timestamps) keep a total order — otherwise the DB could swap them between
        // requests and cause cross-page duplicates/omissions. The alarm's natural active-unique key
        // (device + alarm code + external id) is used because the strongly-typed Id is not orderable
        // by the InMemory test provider; these string columns order deterministically on both.
        var alarmEvents = await query
            .OrderBy(x =>
                x.Status == "raised" ? 0
                : x.Status == "shelved" ? 1
                : x.Status == "acknowledged" ? 2
                : x.Status == "cleared" ? 4
                : 3)
            .ThenByDescending(x => x.RaisedAtUtc)
            .ThenBy(x => x.DeviceAssetId)
            .ThenBy(x => x.AlarmCode)
            .ThenBy(x => x.ExternalAlarmId)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToArrayAsync(cancellationToken);
        var items = alarmEvents
            .Select(x => new AlarmEventListItem(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.DeviceAssetId,
                x.AlarmCode,
                x.Severity,
                x.Priority,
                x.TagKey,
                x.ObservedValue,
                x.ThresholdValue,
                x.UnitCode,
                x.Status,
                x.RaisedAtUtc,
                x.ClearedAtUtc,
                x.ExternalAlarmId,
                x.AcknowledgedAtUtc,
                x.AcknowledgedBy,
                x.ShelvedAtUtc,
                x.ShelvedUntilUtc,
                x.ShelvedBy,
                x.ShelveReason,
                x.EscalatedAtUtc,
                x.EscalationReason,
                x.EscalationRecipientRefs))
            .ToArray();
        return new PagedListResponse<AlarmEventListItem>(items, total);
    }

    private static string? NormalizeStatus(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyCollection<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }
}

public sealed record QueryDeviceStateTimelineQuery(string DeviceAssetId, string? OrganizationId, string? EnvironmentId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) : IQuery<IReadOnlyCollection<DeviceTimelineItem>>;

public sealed record DeviceTimelineItem(string ItemType, string DeviceAssetId, string? TagKey, string Value, DateTimeOffset OccurredAtUtc);

public sealed class QueryDeviceStateTimelineQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryDeviceStateTimelineQuery, IReadOnlyCollection<DeviceTimelineItem>>
{
    public async Task<IReadOnlyCollection<DeviceTimelineItem>> Handle(QueryDeviceStateTimelineQuery request, CancellationToken cancellationToken)
    {
        var states = await dbContext.DeviceStateSnapshots
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.OccurredAtUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.OccurredAtUtc <= request.ToUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new DeviceTimelineItem("state", x.DeviceAssetId, null, x.State, x.OccurredAtUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var rawSamples = await dbContext.TelemetryRawSamples
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.BucketEndUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.BucketStartUtc <= request.ToUtc)
            .OrderByDescending(x => x.BucketEndUtc)
            .Select(x => new DeviceTimelineItem("sample", x.DeviceAssetId, x.TagKey, x.AverageValue.ToString(CultureInfo.InvariantCulture), x.BucketEndUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var rollups = await dbContext.TelemetryRollups
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.WindowEndUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.WindowStartUtc <= request.ToUtc)
            .OrderByDescending(x => x.WindowEndUtc)
            .Select(x => new DeviceTimelineItem(x.Grain == TelemetryRollupGrain.Hourly ? "hourly" : "daily", x.DeviceAssetId, x.TagKey, x.AverageValue.ToString(CultureInfo.InvariantCulture), x.WindowEndUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);

        return states.Concat(rawSamples).Concat(rollups)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(100)
            .ToArray();
    }
}

public sealed record QueryOeeQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc) : IQuery<OeeResponse>;

public sealed record OeeResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int StateSampleCount,
    decimal? AvailabilityRate,
    decimal LoadingRate,
    int ProductionFactCount,
    decimal? GoodQuantity,
    decimal? ScrapQuantity,
    decimal? ReworkQuantity,
    string? OutputUomCode,
    decimal? TheoreticalRatePerHour,
    decimal? ExpectedOutputQuantity,
    decimal? PerformanceRate,
    decimal? QualityRate,
    decimal? OeeRate,
    bool IsDegraded,
    IReadOnlyCollection<string> DegradedReasons);

public sealed record QueryRuntimeHoursQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc) : IQuery<RuntimeHoursResponse>;

public sealed record RuntimeHoursDailyItem(
    string BusinessDate,
    decimal RuntimeHours,
    decimal LoadingHours,
    int StateSampleCount);

public sealed record RuntimeHoursResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int StateSampleCount,
    decimal TotalRuntimeHours,
    decimal TotalLoadingHours,
    bool HasRuntimeSamples,
    IReadOnlyCollection<RuntimeHoursDailyItem> Daily);

public sealed class QueryOeeQueryValidator : AbstractValidator<QueryOeeQuery>
{
    public QueryOeeQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
    }
}

public sealed class QueryRuntimeHoursQueryValidator : AbstractValidator<QueryRuntimeHoursQuery>
{
    public QueryRuntimeHoursQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
    }
}

public sealed class QueryOeeQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryOeeQuery, OeeResponse>
{
    public async Task<OeeResponse> Handle(QueryOeeQuery request, CancellationToken cancellationToken)
    {
        var carryInState = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc < request.WindowStartUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new OeeStatePoint(request.WindowStartUtc, x.State))
            .FirstOrDefaultAsync(cancellationToken);

        var inWindowStates = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc >= request.WindowStartUtc)
            .Where(x => x.OccurredAtUtc < request.WindowEndUtc)
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new OeeStatePoint(x.OccurredAtUtc, x.State))
            .ToArrayAsync(cancellationToken);

        var states = carryInState is null
            ? inWindowStates
            : [carryInState, .. inWindowStates];

        var runtimeRates = CalculateRuntimeRates(states, request.WindowStartUtc, request.WindowEndUtc);
        var productionFacts = await dbContext.OeeProductionFacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.ReportedAtUtc >= request.WindowStartUtc)
            .Where(x => x.ReportedAtUtc < request.WindowEndUtc)
            .ToArrayAsync(cancellationToken);
        var factors = CalculateProductionFactors(productionFacts, runtimeRates.ProductiveRuntimeHours, states.Length > 0);

        decimal? availabilityRate = states.Length > 0 ? Math.Round(runtimeRates.AvailabilityRate, 6) : null;
        return new OeeResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            states.Length,
            availabilityRate,
            Math.Round(runtimeRates.LoadingRate, 6),
            productionFacts.Length,
            RoundNullable(factors.GoodQuantity),
            RoundNullable(factors.ScrapQuantity),
            RoundNullable(factors.ReworkQuantity),
            factors.OutputUomCode,
            RoundNullable(factors.TheoreticalRatePerHour),
            RoundNullable(factors.ExpectedOutputQuantity),
            RoundNullable(factors.PerformanceRate),
            RoundNullable(factors.QualityRate),
            availabilityRate is not null && factors.PerformanceRate is not null && factors.QualityRate is not null
                ? Math.Round(availabilityRate.Value * factors.PerformanceRate.Value * factors.QualityRate.Value, 6)
                : null,
            factors.DegradedReasons.Count > 0,
            factors.DegradedReasons);
    }

    private static OeeProductionFactors CalculateProductionFactors(
        IReadOnlyCollection<OeeProductionFact> productionFacts,
        decimal productiveRuntimeHours,
        bool hasStateSamples)
    {
        var degradedReasons = new List<string>();
        if (!hasStateSamples)
        {
            degradedReasons.Add("runtime-state-facts-missing");
        }

        if (productionFacts.Count == 0)
        {
            degradedReasons.Add("production-facts-missing");
            return new OeeProductionFactors(null, null, null, null, null, null, null, null, degradedReasons);
        }

        var goodQuantity = productionFacts.Sum(x => x.GoodQuantity);
        var scrapQuantity = productionFacts.Sum(x => x.ScrapQuantity);
        var reworkQuantity = productionFacts.Sum(x => x.ReworkQuantity);
        var totalOutputQuantity = goodQuantity + scrapQuantity + reworkQuantity;
        var uomCodes = productionFacts.Select(x => x.UomCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

        var theoryRates = productionFacts
            .Select(x => x.TheoreticalRatePerHour)
            .Where(x => x is > 0m)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        decimal? theoreticalRatePerHour = theoryRates.Length == 1 && productionFacts.All(x => x.TheoreticalRatePerHour is > 0m)
            ? theoryRates[0]
            : null;
        if (theoreticalRatePerHour is null)
        {
            degradedReasons.Add("theoretical-rate-missing-or-ambiguous");
        }

        decimal? expectedOutputQuantity = null;
        decimal? performanceRate = null;
        if (productiveRuntimeHours <= 0m)
        {
            degradedReasons.Add("productive-runtime-missing");
        }
        else if (theoreticalRatePerHour is not null && totalOutputQuantity > 0m && outputUomCode is not null)
        {
            expectedOutputQuantity = productiveRuntimeHours * theoreticalRatePerHour.Value;
            if (expectedOutputQuantity > 0m)
            {
                performanceRate = decimal.Divide(totalOutputQuantity, expectedOutputQuantity.Value);
            }
        }

        return new OeeProductionFactors(
            goodQuantity,
            scrapQuantity,
            reworkQuantity,
            outputUomCode,
            theoreticalRatePerHour,
            expectedOutputQuantity,
            performanceRate,
            qualityRate,
            degradedReasons);
    }

    private static decimal? RoundNullable(decimal? value) => value is null ? null : Math.Round(value.Value, 6);

    private static OeeRuntimeRates CalculateRuntimeRates(IReadOnlyList<OeeStatePoint> states, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
    {
        if (states.Count == 0)
        {
            return new OeeRuntimeRates(0m, 0m, 0m);
        }

        var totalTicks = windowEndUtc.UtcTicks - windowStartUtc.UtcTicks;
        var loadingTicks = 0L;
        var productiveRuntimeTicks = 0L;
        for (var i = 0; i < states.Count; i++)
        {
            var segmentStart = states[i].OccurredAtUtc < windowStartUtc ? windowStartUtc : states[i].OccurredAtUtc;
            var segmentEnd = i + 1 < states.Count ? states[i + 1].OccurredAtUtc : windowEndUtc;
            if (segmentEnd > windowEndUtc)
            {
                segmentEnd = windowEndUtc;
            }

            if (segmentEnd <= segmentStart || IsPlannedDownState(states[i].State))
            {
                continue;
            }

            var segmentTicks = segmentEnd.UtcTicks - segmentStart.UtcTicks;
            loadingTicks += segmentTicks;
            if (IsProductiveRuntimeState(states[i].State))
            {
                productiveRuntimeTicks += segmentTicks;
            }
        }

        if (loadingTicks <= 0)
        {
            return new OeeRuntimeRates(0m, 0m, 0m);
        }

        var availabilityRate = decimal.Divide(productiveRuntimeTicks, loadingTicks);
        var loadingRate = totalTicks <= 0 ? 0m : decimal.Divide(loadingTicks, totalTicks);
        return new OeeRuntimeRates(availabilityRate, loadingRate, decimal.Divide(productiveRuntimeTicks, TimeSpan.TicksPerHour));
    }

    private static bool IsProductiveRuntimeState(string state)
    {
        return EquipmentRuntimeDeviceStates.IsProductiveRuntime(state);
    }

    private static bool IsPlannedDownState(string state)
    {
        return EquipmentRuntimeDeviceStates.IsPlannedDownState(state);
    }

    private sealed record OeeRuntimeRates(decimal AvailabilityRate, decimal LoadingRate, decimal ProductiveRuntimeHours);

    private sealed record OeeProductionFactors(
        decimal? GoodQuantity,
        decimal? ScrapQuantity,
        decimal? ReworkQuantity,
        string? OutputUomCode,
        decimal? TheoreticalRatePerHour,
        decimal? ExpectedOutputQuantity,
        decimal? PerformanceRate,
        decimal? QualityRate,
        IReadOnlyCollection<string> DegradedReasons);

    private sealed record OeeStatePoint(DateTimeOffset OccurredAtUtc, string State);
}

public sealed class QueryRuntimeHoursQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryRuntimeHoursQuery, RuntimeHoursResponse>
{
    private static readonly TimeSpan QueryChunkSize = TimeSpan.FromDays(366);

    public async Task<RuntimeHoursResponse> Handle(QueryRuntimeHoursQuery request, CancellationToken cancellationToken)
    {
        var windowStartUtc = request.WindowStartUtc.ToUniversalTime();
        var windowEndUtc = request.WindowEndUtc.ToUniversalTime();
        var stateSampleCount = await CountStateSamplesAsync(request, windowStartUtc, windowEndUtc, cancellationToken);
        var buckets = new Dictionary<string, RuntimeHoursBucket>(StringComparer.Ordinal);
        for (var chunkStartUtc = windowStartUtc; chunkStartUtc < windowEndUtc;)
        {
            var chunkEndUtc = GetNextChunkEndUtc(chunkStartUtc, windowEndUtc);
            if (chunkEndUtc > windowEndUtc)
            {
                chunkEndUtc = windowEndUtc;
            }

            MergeBuckets(buckets, await QueryChunkBucketsAsync(request, chunkStartUtc, chunkEndUtc, cancellationToken));
            chunkStartUtc = chunkEndUtc;
        }

        var orderedBuckets = buckets.Values
            .OrderBy(x => x.BusinessDate, StringComparer.Ordinal)
            .ToArray();
        return new RuntimeHoursResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            windowStartUtc,
            windowEndUtc,
            stateSampleCount,
            Math.Round(orderedBuckets.Sum(x => x.RuntimeHours), 6),
            Math.Round(orderedBuckets.Sum(x => x.LoadingHours), 6),
            stateSampleCount > 0,
            orderedBuckets
                .Select(x => new RuntimeHoursDailyItem(
                    x.BusinessDate,
                    Math.Round(x.RuntimeHours, 6),
                    Math.Round(x.LoadingHours, 6),
                    x.StateSampleCount))
                .ToArray());
    }

    private static DateTimeOffset GetNextChunkEndUtc(DateTimeOffset chunkStartUtc, DateTimeOffset windowEndUtc)
    {
        var chunkEndUtc = new DateTimeOffset(chunkStartUtc.UtcDateTime.Date.Add(QueryChunkSize), TimeSpan.Zero);
        return chunkEndUtc < windowEndUtc ? chunkEndUtc : windowEndUtc;
    }

    private async Task<int> CountStateSamplesAsync(
        QueryRuntimeHoursQuery request,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        var hasCarryInState = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc < windowStartUtc)
            .AnyAsync(cancellationToken);
        var inWindowStateCount = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc >= windowStartUtc)
            .Where(x => x.OccurredAtUtc < windowEndUtc)
            .CountAsync(cancellationToken);

        return inWindowStateCount + (hasCarryInState ? 1 : 0);
    }

    private async Task<IReadOnlyCollection<RuntimeHoursBucket>> QueryChunkBucketsAsync(
        QueryRuntimeHoursQuery request,
        DateTimeOffset chunkStartUtc,
        DateTimeOffset chunkEndUtc,
        CancellationToken cancellationToken)
    {
        var carryInState = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc < chunkStartUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.SourceSequence)
            .Select(x => new RuntimeHoursStatePoint(chunkStartUtc, x.State))
            .FirstOrDefaultAsync(cancellationToken);

        var inWindowStates = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc >= chunkStartUtc)
            .Where(x => x.OccurredAtUtc < chunkEndUtc)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.RecordedAtUtc)
            .ThenBy(x => x.SourceSequence)
            .Select(x => new RuntimeHoursStatePoint(x.OccurredAtUtc, x.State))
            .ToArrayAsync(cancellationToken);

        var states = carryInState is null
            ? inWindowStates
            : [carryInState, .. inWindowStates];
        return CalculateDailyRuntimeHours(states, chunkStartUtc, chunkEndUtc);
    }

    private static void MergeBuckets(Dictionary<string, RuntimeHoursBucket> target, IReadOnlyCollection<RuntimeHoursBucket> source)
    {
        foreach (var bucket in source)
        {
            target[bucket.BusinessDate] = target.TryGetValue(bucket.BusinessDate, out var existing)
                ? existing with
                {
                    RuntimeHours = existing.RuntimeHours + bucket.RuntimeHours,
                    LoadingHours = existing.LoadingHours + bucket.LoadingHours,
                    StateSampleCount = existing.StateSampleCount + bucket.StateSampleCount,
                }
                : bucket;
        }
    }

    private static IReadOnlyCollection<RuntimeHoursBucket> CalculateDailyRuntimeHours(
        IReadOnlyList<RuntimeHoursStatePoint> states,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        if (states.Count == 0)
        {
            return [];
        }

        var buckets = new Dictionary<string, RuntimeHoursBucket>(StringComparer.Ordinal);
        for (var i = 0; i < states.Count; i++)
        {
            var segmentStart = states[i].OccurredAtUtc < windowStartUtc ? windowStartUtc : states[i].OccurredAtUtc;
            var segmentEnd = i + 1 < states.Count ? states[i + 1].OccurredAtUtc : windowEndUtc;
            if (segmentEnd > windowEndUtc)
            {
                segmentEnd = windowEndUtc;
            }

            if (segmentEnd <= segmentStart || EquipmentRuntimeDeviceStates.IsPlannedDownState(states[i].State))
            {
                continue;
            }

            var isProductive = EquipmentRuntimeDeviceStates.IsProductiveRuntime(states[i].State);
            var cursor = segmentStart.ToUniversalTime();
            var end = segmentEnd.ToUniversalTime();
            while (cursor < end)
            {
                var nextDayUtc = new DateTimeOffset(cursor.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                var sliceEnd = nextDayUtc < end ? nextDayUtc : end;
                var businessDate = DateOnly.FromDateTime(cursor.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!buckets.TryGetValue(businessDate, out var bucket))
                {
                    bucket = new RuntimeHoursBucket(businessDate, 0m, 0m, 0);
                }

                var hours = (decimal)(sliceEnd - cursor).TotalHours;
                buckets[businessDate] = bucket with
                {
                    RuntimeHours = bucket.RuntimeHours + (isProductive ? hours : 0m),
                    LoadingHours = bucket.LoadingHours + hours,
                    StateSampleCount = bucket.StateSampleCount + 1,
                };
                cursor = sliceEnd;
            }
        }

        return buckets.Values.ToArray();
    }

    private sealed record RuntimeHoursStatePoint(DateTimeOffset OccurredAtUtc, string State);

    private sealed record RuntimeHoursBucket(string BusinessDate, decimal RuntimeHours, decimal LoadingHours, int StateSampleCount);
}

public sealed record GetRuntimeCurrentStateQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset AsOfUtc,
    int FreshnessMaxAgeMinutes = 60) : IQuery<EquipmentRuntimeCurrentStateResponse>;

public sealed class GetRuntimeCurrentStateQueryValidator : AbstractValidator<GetRuntimeCurrentStateQuery>
{
    public GetRuntimeCurrentStateQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FreshnessMaxAgeMinutes).GreaterThan(0);
    }
}

public sealed class GetRuntimeCurrentStateQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetRuntimeCurrentStateQuery, EquipmentRuntimeCurrentStateResponse>
{
    public async Task<EquipmentRuntimeCurrentStateResponse> Handle(GetRuntimeCurrentStateQuery request, CancellationToken cancellationToken)
    {
        var latestState = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.OccurredAtUtc <= request.AsOfUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.SourceSequence)
            .Select(x => new RuntimeStateProjection(x.Id, x.DeviceAssetId, x.State, x.OccurredAtUtc, x.SourceSequence, x.RecordedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        var activeAlarms = await dbContext.AlarmEvents
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.RaisedAtUtc <= request.AsOfUtc)
            .Where(x => x.ClearedAtUtc == null || x.ClearedAtUtc > request.AsOfUtc)
            .OrderByDescending(x => x.RaisedAtUtc)
            .Select(x => new RuntimeAlarmProjection(x.Id, x.DeviceAssetId, x.AlarmCode, x.Severity, x.RaisedAtUtc, x.ExternalAlarmId))
            .ToArrayAsync(cancellationToken);

        var alarms = activeAlarms
            .Select(x => new EquipmentRuntimeAlarmSummary(
                x.AlarmEventId.ToString(),
                x.DeviceAssetId,
                x.AlarmCode,
                x.Severity,
                x.RaisedAtUtc,
                x.ExternalAlarmId))
            .ToArray();

        var isSourceFresh = latestState is not null
            && latestState.OccurredAtUtc >= request.AsOfUtc.AddMinutes(-request.FreshnessMaxAgeMinutes);

        return new EquipmentRuntimeCurrentStateResponse(
            ContractVersion: 1,
            OrganizationId: request.OrganizationId,
            EnvironmentId: request.EnvironmentId,
            DeviceAssetId: request.DeviceAssetId,
            CurrentState: latestState?.State,
            StateOccurredAtUtc: latestState?.OccurredAtUtc,
            IsSourceFresh: isSourceFresh,
            ActiveAlarms: alarms);
    }
}

public sealed record QueryRuntimeAvailabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string>? DeviceAssetIds,
    IReadOnlyCollection<string>? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60) : IQuery<EquipmentRuntimeAvailabilityResponse>;

public sealed class QueryRuntimeAvailabilityQueryValidator : AbstractValidator<QueryRuntimeAvailabilityQuery>
{
    public QueryRuntimeAvailabilityQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.FreshnessMaxAgeMinutes).GreaterThan(0);
    }
}

public sealed class QueryRuntimeAvailabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryRuntimeAvailabilityQuery, EquipmentRuntimeAvailabilityResponse>
{
    public async Task<EquipmentRuntimeAvailabilityResponse> Handle(QueryRuntimeAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var requestedDeviceAssetIds = request.DeviceAssetIds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requestedWorkCenterIds = request.WorkCenterIds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedWorkCenterIds is { Length: > 0 })
        {
            throw new KnownException("P0 runtime availability does not support workCenterIds direct query; pass deviceAssetIds.");
        }

        if (requestedDeviceAssetIds is null or { Length: 0 })
        {
            throw new KnownException("deviceAssetIds is required in P0 runtime availability.");
        }

        var latestStates = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.OccurredAtUtc <= request.WindowEndUtc)
            .Where(x => requestedDeviceAssetIds.Contains(x.DeviceAssetId))
            .GroupBy(x => x.DeviceAssetId)
            .Select(group => group
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.RecordedAtUtc)
                .ThenByDescending(x => x.SourceSequence)
                .Select(x => new RuntimeStateProjection(x.Id, x.DeviceAssetId, x.State, x.OccurredAtUtc, x.SourceSequence, x.RecordedAtUtc))
                .First())
            .ToArrayAsync(cancellationToken);

        var activeAlarms = await dbContext.AlarmEvents
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.RaisedAtUtc <= request.WindowEndUtc)
            .Where(x => x.ClearedAtUtc == null || x.ClearedAtUtc > request.WindowStartUtc)
            .Where(x => requestedDeviceAssetIds.Contains(x.DeviceAssetId))
            .OrderBy(x => x.RaisedAtUtc)
            .Select(x => new RuntimeAlarmProjection(x.Id, x.DeviceAssetId, x.AlarmCode, x.Severity, x.RaisedAtUtc, x.ExternalAlarmId))
            .ToArrayAsync(cancellationToken);

        var deviceAssetIds = requestedDeviceAssetIds ?? latestStates
            .Select(x => x.DeviceAssetId)
            .Concat(activeAlarms.Select(x => x.DeviceAssetId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var windows = new List<EquipmentRuntimeAvailabilityWindowContract>();
        foreach (var alarm in activeAlarms)
        {
            windows.Add(new EquipmentRuntimeAvailabilityWindowContract(
                DeviceAssetId: alarm.DeviceAssetId,
                WorkCenterId: null,
                AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
                ReasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                Severity: MapAlarmSeverity(alarm.Severity),
                StartUtc: Max(alarm.RaisedAtUtc, request.WindowStartUtc),
                EndUtc: request.WindowEndUtc,
                SourceType: EquipmentRuntimeSourceType.Alarm,
                SourceReferenceId: alarm.AlarmEventId.ToString(),
                MessageKey: EquipmentRuntimeReasonCodes.ActiveAlarm,
                SubstituteDeviceAssetIds: []));
        }

        foreach (var state in latestStates)
        {
            if (!IsAvailableState(state.State))
            {
                windows.Add(new EquipmentRuntimeAvailabilityWindowContract(
                    DeviceAssetId: state.DeviceAssetId,
                    WorkCenterId: null,
                    AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
                    ReasonCode: EquipmentRuntimeReasonCodes.StateUnavailable,
                    Severity: EquipmentRuntimeSeverity.Blocked,
                    StartUtc: Max(state.OccurredAtUtc, request.WindowStartUtc),
                    EndUtc: request.WindowEndUtc,
                    SourceType: EquipmentRuntimeSourceType.DeviceState,
                    SourceReferenceId: state.DeviceStateSnapshotId.ToString(),
                    MessageKey: EquipmentRuntimeReasonCodes.StateUnavailable,
                    SubstituteDeviceAssetIds: []));
            }
        }

        foreach (var state in latestStates)
        {
            var staleStartUtc = state.OccurredAtUtc.AddMinutes(request.FreshnessMaxAgeMinutes);
            if (staleStartUtc < request.WindowEndUtc)
            {
                windows.Add(new EquipmentRuntimeAvailabilityWindowContract(
                    DeviceAssetId: state.DeviceAssetId,
                    WorkCenterId: null,
                    AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unknown,
                    ReasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                    Severity: EquipmentRuntimeSeverity.Warning,
                    StartUtc: Max(staleStartUtc, request.WindowStartUtc),
                    EndUtc: request.WindowEndUtc,
                    SourceType: EquipmentRuntimeSourceType.StaleSource,
                    SourceReferenceId: state.DeviceStateSnapshotId.ToString(),
                    MessageKey: EquipmentRuntimeReasonCodes.SourceStale,
                    SubstituteDeviceAssetIds: []));
            }
        }

        foreach (var deviceAssetId in deviceAssetIds.Where(deviceAssetId => latestStates.All(x => !string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.OrdinalIgnoreCase))))
        {
            windows.Add(new EquipmentRuntimeAvailabilityWindowContract(
                DeviceAssetId: deviceAssetId,
                WorkCenterId: null,
                AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unknown,
                ReasonCode: EquipmentRuntimeReasonCodes.SourceStale,
                Severity: EquipmentRuntimeSeverity.Warning,
                StartUtc: request.WindowStartUtc,
                EndUtc: request.WindowEndUtc,
                SourceType: EquipmentRuntimeSourceType.StaleSource,
                SourceReferenceId: deviceAssetId,
                MessageKey: EquipmentRuntimeReasonCodes.SourceStale,
                SubstituteDeviceAssetIds: []));
        }

        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: request.OrganizationId,
            EnvironmentId: request.EnvironmentId,
            QueryWindowStartUtc: request.WindowStartUtc,
            QueryWindowEndUtc: request.WindowEndUtc,
            Items: windows
                .OrderBy(x => x.DeviceAssetId)
                .ThenBy(x => x.StartUtc)
                .ThenBy(x => x.ReasonCode)
                .ToArray());
    }

    private static bool IsAvailableState(string state)
    {
        return EquipmentRuntimeDeviceStates.IsRuntimeAvailable(state);
    }

    private static EquipmentRuntimeSeverity MapAlarmSeverity(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "critical" => EquipmentRuntimeSeverity.Critical,
            "high" or "error" => EquipmentRuntimeSeverity.Blocked,
            "medium" or "warning" => EquipmentRuntimeSeverity.Warning,
            _ => EquipmentRuntimeSeverity.Info,
        };
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }
}

internal sealed record RuntimeStateProjection(DeviceStateSnapshotId DeviceStateSnapshotId, string DeviceAssetId, string State, DateTimeOffset OccurredAtUtc, string SourceSequence, DateTimeOffset RecordedAtUtc);

internal sealed record RuntimeAlarmProjection(AlarmEventId AlarmEventId, string DeviceAssetId, string AlarmCode, string Severity, DateTimeOffset RaisedAtUtc, string ExternalAlarmId);
