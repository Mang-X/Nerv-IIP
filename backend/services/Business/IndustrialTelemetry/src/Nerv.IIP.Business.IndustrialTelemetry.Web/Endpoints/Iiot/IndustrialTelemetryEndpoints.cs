using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;

public abstract class IndustrialTelemetryEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected void ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContract contract)
    {
        switch (contract.HttpMethod)
        {
            case "GET":
                Get(contract.Route);
                break;
            case "POST":
                Post(contract.Route);
                break;
            default:
                throw new NotSupportedException($"HTTP method '{contract.HttpMethod}' is not supported by IndustrialTelemetry endpoints.");
        }

        Tags("Business IndustrialTelemetry");
        Policies(contract.AuthorizationPolicy);
    }
}

public sealed record CreateTelemetryTagRequest(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey, string ValueType, string UnitCode, string SamplingPolicy);
public sealed record CreateTelemetryTagResponse(TelemetryTagId TelemetryTagId);
public sealed record ListTelemetryTagsRequest(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId);
public sealed record RecordTelemetrySampleRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    DateTimeOffset BucketStartUtc,
    DateTimeOffset BucketEndUtc,
    int SampleCount,
    decimal MinValue,
    decimal MaxValue,
    decimal AverageValue,
    string SourceSequence,
    string? SourceSystem,
    string? SourceConnector,
    string? DeviceState,
    DateTimeOffset? StateOccurredAtUtc);
public sealed record RecordTelemetrySampleResponse(TelemetrySummaryId? TelemetrySummaryId, DeviceStateSnapshotId? DeviceStateSnapshotId);
public sealed record PostAlarmEventRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId,
    DateTimeOffset? ClearedAtUtc,
    string? ClearedBy,
    string? ClearReason);
public sealed record PostAlarmEventResponse(AlarmEventId AlarmEventId);
public sealed record ListAlarmEventsRequest(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, string? Status);
public sealed record QueryDeviceTimelineRequest(string DeviceAssetId, string? OrganizationId, string? EnvironmentId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);
public sealed record GetDeviceRuntimeAvailabilityRequest(string DeviceAssetId, string OrganizationId, string EnvironmentId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc, int FreshnessMaxAgeMinutes = 60);
public sealed record QueryRuntimeAvailabilityRequest(string OrganizationId, string EnvironmentId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc, string? DeviceAssetIds, string? WorkCenterIds, int FreshnessMaxAgeMinutes = 60);
public sealed record GetDeviceCurrentStateRequest(string DeviceAssetId, string OrganizationId, string EnvironmentId, DateTimeOffset? AsOfUtc, int FreshnessMaxAgeMinutes = 60);

public sealed class CreateTelemetryTagEndpoint(ISender sender) : IndustrialTelemetryEndpoint<CreateTelemetryTagRequest, ResponseData<CreateTelemetryTagResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<CreateTelemetryTagEndpoint>());

    public override async Task HandleAsync(CreateTelemetryTagRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateTelemetryTagCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.TagKey, req.ValueType, req.UnitCode, req.SamplingPolicy), ct);
        await Send.OkAsync(new CreateTelemetryTagResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListTelemetryTagsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListTelemetryTagsRequest, ResponseData<IReadOnlyCollection<TelemetryTagListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListTelemetryTagsEndpoint>());

    public override async Task HandleAsync(ListTelemetryTagsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListTelemetryTagsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordTelemetrySampleEndpoint(ISender sender) : IndustrialTelemetryEndpoint<RecordTelemetrySampleRequest, ResponseData<RecordTelemetrySampleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<RecordTelemetrySampleEndpoint>());

    public override async Task HandleAsync(RecordTelemetrySampleRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RecordTelemetrySampleCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.TagKey, req.BucketStartUtc, req.BucketEndUtc, req.SampleCount, req.MinValue, req.MaxValue, req.AverageValue, req.SourceSequence, req.SourceSystem, req.SourceConnector, req.DeviceState, req.StateOccurredAtUtc), ct);
        await Send.OkAsync(new RecordTelemetrySampleResponse(result.TelemetrySummaryId, result.DeviceStateSnapshotId).AsResponseData(), cancellation: ct);
    }
}

public sealed class PostAlarmEventEndpoint(ISender sender) : IndustrialTelemetryEndpoint<PostAlarmEventRequest, ResponseData<PostAlarmEventResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<PostAlarmEventEndpoint>());

    public override async Task HandleAsync(PostAlarmEventRequest req, CancellationToken ct)
    {
        var id = req.ClearedAtUtc is null
            ? await sender.Send(new RaiseAlarmCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.AlarmCode, req.Severity, req.RaisedAtUtc, req.ExternalAlarmId), ct)
            : await sender.Send(new ClearAlarmCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.AlarmCode, req.ExternalAlarmId, req.ClearedAtUtc.Value, req.ClearedBy ?? string.Empty, req.ClearReason), ct);
        await Send.OkAsync(new PostAlarmEventResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetDeviceRuntimeAvailabilityEndpoint(ISender sender) : IndustrialTelemetryEndpoint<GetDeviceRuntimeAvailabilityRequest, ResponseData<EquipmentRuntimeAvailabilityResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<GetDeviceRuntimeAvailabilityEndpoint>());

    public override async Task HandleAsync(GetDeviceRuntimeAvailabilityRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryRuntimeAvailabilityQuery(req.OrganizationId, req.EnvironmentId, req.WindowStartUtc, req.WindowEndUtc, [req.DeviceAssetId], null, req.FreshnessMaxAgeMinutes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryRuntimeAvailabilityEndpoint(ISender sender) : IndustrialTelemetryEndpoint<QueryRuntimeAvailabilityRequest, ResponseData<EquipmentRuntimeAvailabilityResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<QueryRuntimeAvailabilityEndpoint>());

    public override async Task HandleAsync(QueryRuntimeAvailabilityRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryRuntimeAvailabilityQuery(req.OrganizationId, req.EnvironmentId, req.WindowStartUtc, req.WindowEndUtc, SplitCsv(req.DeviceAssetIds), SplitCsv(req.WorkCenterIds), req.FreshnessMaxAgeMinutes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }

    private static IReadOnlyCollection<string>? SplitCsv(string? value)
    {
        var values = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values is { Length: > 0 } ? values : null;
    }
}

public sealed class GetDeviceCurrentStateEndpoint(ISender sender) : IndustrialTelemetryEndpoint<GetDeviceCurrentStateRequest, ResponseData<EquipmentRuntimeCurrentStateResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<GetDeviceCurrentStateEndpoint>());

    public override async Task HandleAsync(GetDeviceCurrentStateRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetRuntimeCurrentStateQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.AsOfUtc ?? DateTimeOffset.UtcNow, req.FreshnessMaxAgeMinutes), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListAlarmEventsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListAlarmEventsRequest, ResponseData<IReadOnlyCollection<AlarmEventListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListAlarmEventsEndpoint>());

    public override async Task HandleAsync(ListAlarmEventsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListAlarmEventsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Status), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryDeviceTimelineEndpoint(ISender sender) : IndustrialTelemetryEndpoint<QueryDeviceTimelineRequest, ResponseData<IReadOnlyCollection<DeviceTimelineItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<QueryDeviceTimelineEndpoint>());

    public override async Task HandleAsync(QueryDeviceTimelineRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryDeviceStateTimelineQuery(req.DeviceAssetId, req.OrganizationId, req.EnvironmentId, req.FromUtc, req.ToUtc), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed record IndustrialTelemetryEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class IndustrialTelemetryEndpointContracts
{
    public static readonly IReadOnlyCollection<IndustrialTelemetryEndpointContract> All =
    [
        new(typeof(CreateTelemetryTagEndpoint), "POST", "/api/business/v1/iiot/tags", IndustrialTelemetryPermissionCodes.TagsManage, InternalServiceAuthorizationPolicy.Name, "createBusinessIiotTelemetryTag"),
        new(typeof(ListTelemetryTagsEndpoint), "GET", "/api/business/v1/iiot/tags", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotTelemetryTags"),
        new(typeof(RecordTelemetrySampleEndpoint), "POST", "/api/business/v1/iiot/samples", IndustrialTelemetryPermissionCodes.TelemetryWrite, InternalServiceAuthorizationPolicy.Name, "recordBusinessIiotTelemetrySample"),
        new(typeof(PostAlarmEventEndpoint), "POST", "/api/business/v1/iiot/alarms", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "raiseBusinessIiotAlarm"),
        new(typeof(ListAlarmEventsEndpoint), "GET", "/api/business/v1/iiot/alarms", IndustrialTelemetryPermissionCodes.AlarmsRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotAlarms"),
        new(typeof(QueryDeviceTimelineEndpoint), "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/timeline", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "queryBusinessIiotDeviceTimeline"),
        new(typeof(GetDeviceRuntimeAvailabilityEndpoint), "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/runtime-availability", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "getBusinessIiotDeviceRuntimeAvailability"),
        new(typeof(QueryRuntimeAvailabilityEndpoint), "GET", "/api/business/v1/iiot/runtime-availability", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "queryBusinessIiotRuntimeAvailability"),
        new(typeof(GetDeviceCurrentStateEndpoint), "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/current-state", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "getBusinessIiotDeviceCurrentState"),
    ];

    public static IndustrialTelemetryEndpointContract Get<TEndpoint>() => All.Single(x => x.EndpointType == typeof(TEndpoint));

    public static bool TryGet(Type endpointType, [NotNullWhen(true)] out IndustrialTelemetryEndpointContract? contract)
    {
        contract = All.SingleOrDefault(x => x.EndpointType == endpointType);
        return contract is not null;
    }
}
