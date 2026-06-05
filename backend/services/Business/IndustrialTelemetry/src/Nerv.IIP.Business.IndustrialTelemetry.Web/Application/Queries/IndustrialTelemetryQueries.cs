using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

public sealed record ListTelemetryTagsQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId) : IQuery<IReadOnlyCollection<TelemetryTagListItem>>;

public sealed record TelemetryTagListItem(TelemetryTagId TelemetryTagId, string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, string ValueType, string UnitCode, string SamplingPolicy);

public sealed class ListTelemetryTagsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTelemetryTagsQuery, IReadOnlyCollection<TelemetryTagListItem>>
{
    public async Task<IReadOnlyCollection<TelemetryTagListItem>> Handle(ListTelemetryTagsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.TelemetryTags
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .OrderBy(x => x.DeviceAssetId)
            .ThenBy(x => x.TagKey)
            .Select(x => new TelemetryTagListItem(x.Id, x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.ValueType, x.UnitCode, x.SamplingPolicy))
            .Take(200)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record ListAlarmRulesQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, bool? IsEnabled) : IQuery<IReadOnlyCollection<AlarmRuleListItem>>;

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
    DateTimeOffset UpdatedAtUtc);

public sealed class ListAlarmRulesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAlarmRulesQuery, IReadOnlyCollection<AlarmRuleListItem>>
{
    public async Task<IReadOnlyCollection<AlarmRuleListItem>> Handle(ListAlarmRulesQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.AlarmRules
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.IsEnabled == null || x.IsEnabled == request.IsEnabled)
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
                x.UpdatedAtUtc))
            .Take(200)
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record ListAlarmEventsQuery(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, string? Status) : IQuery<IReadOnlyCollection<AlarmEventListItem>>;

public sealed record AlarmEventListItem(AlarmEventId AlarmEventId, string OrganizationId, string EnvironmentId, string DeviceAssetId, string AlarmCode, string Severity, string Status, DateTimeOffset RaisedAtUtc, DateTimeOffset? ClearedAtUtc, string ExternalAlarmId);

public sealed class ListAlarmEventsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListAlarmEventsQuery, IReadOnlyCollection<AlarmEventListItem>>
{
    public async Task<IReadOnlyCollection<AlarmEventListItem>> Handle(ListAlarmEventsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.AlarmEvents
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.DeviceAssetId == null || x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.Status == null || x.Status == request.Status)
            .OrderByDescending(x => x.RaisedAtUtc)
            .Select(x => new AlarmEventListItem(x.Id, x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.AlarmCode, x.Severity, x.Status, x.RaisedAtUtc, x.ClearedAtUtc, x.ExternalAlarmId))
            .Take(200)
            .ToArrayAsync(cancellationToken);
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
        var summaries = await dbContext.TelemetrySummaries
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.FromUtc == null || x.BucketEndUtc >= request.FromUtc)
            .Where(x => request.ToUtc == null || x.BucketStartUtc <= request.ToUtc)
            .OrderByDescending(x => x.BucketEndUtc)
            .Select(x => new DeviceTimelineItem("summary", x.DeviceAssetId, x.TagKey, x.AverageValue.ToString(), x.BucketEndUtc))
            .Take(100)
            .ToArrayAsync(cancellationToken);

        return states.Concat(summaries)
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
    decimal AvailabilityRate,
    decimal PerformanceRate,
    decimal QualityRate,
    decimal OeeRate,
    bool PerformanceRateEstimated,
    bool QualityRateEstimated);

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

public sealed class QueryOeeQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryOeeQuery, OeeResponse>
{
    private static readonly HashSet<string> RunningStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "available",
        "ready",
        "running",
        "standby",
    };

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

        var availabilityRate = CalculateAvailabilityRate(states, request.WindowStartUtc, request.WindowEndUtc);
        var performanceRate = states.Length > 0 ? 1m : 0m;
        var qualityRate = states.Length > 0 ? 1m : 0m;
        var oeeRate = availabilityRate * performanceRate * qualityRate;

        return new OeeResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            states.Length,
            Math.Round(availabilityRate, 6),
            performanceRate,
            qualityRate,
            Math.Round(oeeRate, 6),
            true,
            true);
    }

    private static decimal CalculateAvailabilityRate(IReadOnlyList<OeeStatePoint> states, DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc)
    {
        if (states.Count == 0)
        {
            return 0m;
        }

        var totalTicks = windowEndUtc.UtcTicks - windowStartUtc.UtcTicks;
        if (totalTicks <= 0)
        {
            return 0m;
        }

        var runningTicks = 0L;
        for (var i = 0; i < states.Count; i++)
        {
            var segmentStart = states[i].OccurredAtUtc < windowStartUtc ? windowStartUtc : states[i].OccurredAtUtc;
            var segmentEnd = i + 1 < states.Count ? states[i + 1].OccurredAtUtc : windowEndUtc;
            if (segmentEnd > windowEndUtc)
            {
                segmentEnd = windowEndUtc;
            }

            if (segmentEnd <= segmentStart || !IsRunningState(states[i].State))
            {
                continue;
            }

            runningTicks += segmentEnd.UtcTicks - segmentStart.UtcTicks;
        }

        return decimal.Divide(runningTicks, totalTicks);
    }

    private static bool IsRunningState(string state)
    {
        return RunningStates.Contains(state);
    }

    private sealed record OeeStatePoint(DateTimeOffset OccurredAtUtc, string State);
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
    private static readonly string[] AvailableStates = ["available", "idle", "ready", "running", "standby"];

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
        return AvailableStates.Contains(state, StringComparer.OrdinalIgnoreCase);
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
