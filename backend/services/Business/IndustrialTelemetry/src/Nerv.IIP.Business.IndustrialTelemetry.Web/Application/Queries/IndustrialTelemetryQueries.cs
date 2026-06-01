using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
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
            .OrderByDescending(x => x.SourceSequence)
            .ThenByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.RecordedAtUtc)
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

        var states = await dbContext.DeviceStateSnapshots
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.OccurredAtUtc <= request.WindowEndUtc)
            .Where(x => requestedDeviceAssetIds == null || requestedDeviceAssetIds.Contains(x.DeviceAssetId))
            .OrderByDescending(x => x.SourceSequence)
            .ThenByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.RecordedAtUtc)
            .Select(x => new RuntimeStateProjection(x.Id, x.DeviceAssetId, x.State, x.OccurredAtUtc, x.SourceSequence, x.RecordedAtUtc))
            .ToArrayAsync(cancellationToken);

        var latestStates = states
            .GroupBy(x => x.DeviceAssetId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(state => state.SourceSequence, StringComparer.Ordinal).ThenByDescending(state => state.OccurredAtUtc).ThenByDescending(state => state.RecordedAtUtc).First())
            .ToArray();

        var activeAlarms = await dbContext.AlarmEvents
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.Status == "raised")
            .Where(x => x.RaisedAtUtc <= request.WindowEndUtc)
            .Where(x => requestedDeviceAssetIds == null || requestedDeviceAssetIds.Contains(x.DeviceAssetId))
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
